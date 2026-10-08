using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Executions;
using Aura.Application.Verifications;

internal static class ProductionAdapterReadinessChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var scope = new ActionScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var user = new User { Snapshot = new(scope.UserId, CurrentUserStatus.Authenticated) };
        var source = new Source();
        var validator = new ExactOwnershipScopeValidator(user, source);
        var expected = new OwnershipLookupRequest(scope.UserId, scope.ConversationId, scope.ProjectId);
        source.Result = new(OwnershipLookupStatus.Found, expected, true, true, true);

        check(validator is IActionScopeValidator && validator is IActionApprovalScopeValidator &&
            validator is IToolExecutionScopeValidator && validator is IVerificationScopeValidator &&
            validator is IExecutionReconciliationScopeValidator,
            "11Y one ownership validator backs all five workflow interfaces");
        check(await ((IActionScopeValidator)validator).ValidateAsync(scope, default) &&
            await ((IActionApprovalScopeValidator)validator).ValidateAsync(scope, default) &&
            await ((IToolExecutionScopeValidator)validator).ValidateAsync(scope, default) &&
            await ((IVerificationScopeValidator)validator).ValidateAsync(scope, default) &&
            await ((IExecutionReconciliationScopeValidator)validator).ValidateAsync(scope, default) &&
            source.LastRequest == expected && source.Calls == 5,
            "11Y every workflow requires fresh exact ownership assessment");
        check(typeof(CurrentUserSnapshot).GetProperties().Select(x => x.Name)
                .SequenceEqual(["UserId", "Status"]) &&
            typeof(OwnershipLookupRequest).GetProperties().Select(x => x.Name)
                .SequenceEqual(["UserId", "ConversationId", "ProjectId"]) &&
            typeof(OwnershipLookupResult).GetProperties().Select(x => x.Name)
                .SequenceEqual(["Status", "Binding", "OwnsConversation", "HasExactProjectRelation", "OwnsProject"]),
            "11Y current user and ownership contracts contain bounded IDs and status only");

        var before = source.Calls;
        check(!await validator.ValidateAsync(scope with { UserId = Guid.NewGuid() }, default) &&
            source.Calls == before, "11Y foreign user denied before ownership lookup");
        foreach (var foreign in new[] { scope with { ConversationId = Guid.NewGuid() },
            scope with { ProjectId = Guid.NewGuid() } })
            check(!await validator.ValidateAsync(foreign, default),
                "11Y foreign conversation or project denied by exact returned binding");
        foreach (var empty in new[] { scope with { UserId = Guid.Empty },
            scope with { ConversationId = Guid.Empty }, scope with { ProjectId = Guid.Empty } })
            check(!await validator.ValidateAsync(empty, default), "11Y empty scope ID denied");
        check(!await validator.ValidateAsync(null!, default), "11Y null scope denied");

        foreach (var result in new[] {
            new OwnershipLookupResult(OwnershipLookupStatus.Unavailable, expected, true, true, true),
            new OwnershipLookupResult(OwnershipLookupStatus.Denied, expected, true, true, true),
            new OwnershipLookupResult(OwnershipLookupStatus.Found, expected, false, true, true),
            new OwnershipLookupResult(OwnershipLookupStatus.Found, expected, true, false, true),
            new OwnershipLookupResult(OwnershipLookupStatus.Found, expected, true, true, false),
            new OwnershipLookupResult(OwnershipLookupStatus.Found, expected with { UserId = Guid.NewGuid() }, true, true, true),
            new OwnershipLookupResult(OwnershipLookupStatus.Found, expected with { ConversationId = Guid.NewGuid() }, true, true, true),
            new OwnershipLookupResult(OwnershipLookupStatus.Found, expected with { ProjectId = Guid.NewGuid() }, true, true, true),
            new OwnershipLookupResult(OwnershipLookupStatus.Found, null, true, true, true) })
        {
            source.Result = result;
            check(!await validator.ValidateAsync(scope, default),
                "11Y unavailable, partial, wildcard, or cross-boundary ownership denied");
        }
        source.Result = new(OwnershipLookupStatus.Found, expected, true, true, true);
        foreach (var snapshot in new[] { new CurrentUserSnapshot(Guid.Empty, CurrentUserStatus.Authenticated),
            new CurrentUserSnapshot(scope.UserId, CurrentUserStatus.Unavailable),
            new CurrentUserSnapshot(Guid.NewGuid(), CurrentUserStatus.Authenticated) })
        {
            user.Snapshot = snapshot;
            check(!await validator.ValidateAsync(scope, default),
                "11Y anonymous, unavailable, or mismatched identity denied");
        }
        user.Snapshot = new(scope.UserId, CurrentUserStatus.Authenticated);
        check((await new UnavailableCurrentUserContext().GetAsync(default)).Status == CurrentUserStatus.Unavailable &&
            !await new ExactOwnershipScopeValidator(new UnavailableCurrentUserContext(), source)
                .ValidateAsync(scope, default) &&
            !await new ExactOwnershipScopeValidator(user, new UnavailableOwnershipSource())
                .ValidateAsync(scope, default), "11Y default current user and ownership source fail closed");
        source.Throw = true;
        var callsBeforeFailure = source.Calls;
        check(!await validator.ValidateAsync(scope, default) && source.Calls == callsBeforeFailure + 1,
            "11Y source failure denies without leakage, retry, or fallback");
        source.Throw = false;
        using var duringRead = new CancellationTokenSource();
        source.CancelOnRead = duringRead;
        var duringReadPropagated = false;
        try { await validator.ValidateAsync(scope, duringRead.Token); }
        catch (OperationCanceledException) { duringReadPropagated = true; }
        check(duringReadPropagated, "11Y cancellation during ownership lookup propagates");
        source.CancelOnRead = null;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var propagated = false;
        try { await validator.ValidateAsync(scope, cancelled.Token); }
        catch (OperationCanceledException) { propagated = true; }
        check(propagated && source.LastToken != cancelled.Token,
            "11Y cancellation propagates before source access");
    }

    private sealed class User : ICurrentUserContext
    {
        public CurrentUserSnapshot Snapshot { get; set; } = new(Guid.Empty, CurrentUserStatus.Unavailable);
        public Task<CurrentUserSnapshot> GetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class Source : IOwnershipSource
    {
        public OwnershipLookupResult Result { get; set; } = new(OwnershipLookupStatus.Unavailable, null, false, false, false);
        public OwnershipLookupRequest? LastRequest { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public int Calls { get; private set; }
        public bool Throw { get; set; }
        public CancellationTokenSource? CancelOnRead { get; set; }
        public Task<OwnershipLookupResult> GetAsync(OwnershipLookupRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastRequest = request;
            LastToken = cancellationToken;
            if (Throw) throw new InvalidOperationException("Synthetic private source error.");
            CancelOnRead?.Cancel();
            return Task.FromResult(Result);
        }
    }
}
