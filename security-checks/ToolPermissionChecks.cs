using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Privacy.Services;
using Aura.Application.Tools;
using Aura.Application.AI.Chat.DTOs;
using Aura.Application.ProjectFiles.Content;

internal static class ToolPermissionChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ToolIdentifier.TryCreate("permission-fixture", out var toolId);
        ToolIdentifier.TryCreate("other-fixture", out var otherToolId);
        var tool = new ToolDescriptor(toolId!, "Fixture", "Synthetic fixture",
            ToolCapability.ProjectRead, ToolPermissionRequirement.OwnedProjectRead);
        var scope = new ActionScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var grant = new ToolPermissionGrant(Guid.NewGuid(), scope, tool.Id,
            tool.RequiredPermission, true, false);
        var source = new Source { Lookup = new(ToolPermissionLookupStatus.Found, grant) };
        var policy = new ExactToolPermissionPolicy();
        var validator = new ToolExecutionPermissionValidator(source, policy);
        var valid = await validator.ValidateAsync(scope, tool, CancellationToken.None);
        check(valid.Decision == ToolPermissionDecision.Allowed &&
            await validator.HasPermissionAsync(scope, tool, CancellationToken.None) &&
            source.Calls == 2 && source.LastRequest == new ToolPermissionValidationRequest(scope,
                tool.Id, tool.RequiredPermission),
            "11V exact active grant validates; request binds scope/tool/requirement freshly");
        check(typeof(ToolPermissionValidationRequest).GetProperties().Select(x => x.Name)
                .SequenceEqual(["Scope", "ToolId", "Requirement"]) &&
            typeof(ToolPermissionGrant).GetProperties().Select(x => x.Name)
                .SequenceEqual(["GrantId", "Scope", "ToolId", "Requirement", "IsActive", "IsRevoked"]),
            "11V bounded request and grant contain no role, approval, claim or payload");

        async Task<ToolPermissionDecision> Decide(ToolPermissionLookupResult lookup,
            ActionScope? requestedScope = null, ToolDescriptor? requestedTool = null)
        {
            source.Lookup = lookup;
            return (await validator.ValidateAsync(requestedScope ?? scope, requestedTool ?? tool,
                CancellationToken.None)).Decision;
        }
        check(await Decide(new(ToolPermissionLookupStatus.NotFound, null)) == ToolPermissionDecision.Denied &&
            await Decide(new(ToolPermissionLookupStatus.Unavailable, null)) == ToolPermissionDecision.Unavailable &&
            (await new ToolExecutionPermissionValidator(new UnavailableToolPermissionSource(), policy)
                .ValidateAsync(scope, tool, CancellationToken.None)).Decision == ToolPermissionDecision.Unavailable,
            "11V missing or unavailable source denies; default source unavailable");
        check(await Decide(new(ToolPermissionLookupStatus.Found, grant with { IsRevoked = true })) ==
                ToolPermissionDecision.Revoked &&
            await Decide(new(ToolPermissionLookupStatus.Found, grant with { IsActive = false })) ==
                ToolPermissionDecision.Denied,
            "11V revoked and inactive grants deny");
        foreach (var foreign in new[] { scope with { UserId = Guid.NewGuid() },
            scope with { ConversationId = Guid.NewGuid() },
            scope with { ProjectId = Guid.NewGuid() } })
            check(await Decide(new(ToolPermissionLookupStatus.Found, grant), foreign) ==
                ToolPermissionDecision.ScopeMismatch,
                "11V foreign user/conversation/project denied");
        var otherTool = new ToolDescriptor(otherToolId!, "Other", "Other fixture",
            ToolCapability.ProjectRead, ToolPermissionRequirement.OwnedProjectRead);
        check(await Decide(new(ToolPermissionLookupStatus.Found, grant), requestedTool: otherTool) ==
            ToolPermissionDecision.ScopeMismatch,
            "11V exact tool binding denies cross-tool grant");
        var writeTool = new ToolDescriptor(tool.Id, "Write", "Different requirement",
            ToolCapability.ProjectWrite, ToolPermissionRequirement.OwnedProjectWrite);
        check(await Decide(new(ToolPermissionLookupStatus.Found, grant), requestedTool: writeTool) ==
            ToolPermissionDecision.RequirementMismatch,
            "11V exact requirement denies cross-requirement grant");
        check((await validator.ValidateAsync(scope, new ToolDescriptor(tool.Id, "Write", "Write fixture",
            ToolCapability.ProjectWrite, ToolPermissionRequirement.OwnedProjectWrite),
            CancellationToken.None)).Decision != ToolPermissionDecision.Allowed,
            "11V requirement metadata cannot upgrade existing grant");
        check(await Decide(new(ToolPermissionLookupStatus.Found, grant with { GrantId = Guid.Empty })) ==
                ToolPermissionDecision.InvalidGrant &&
            await Decide(new(ToolPermissionLookupStatus.Found, grant with
            { Requirement = (ToolPermissionRequirement)999 })) == ToolPermissionDecision.InvalidGrant &&
            await Decide(new((ToolPermissionLookupStatus)999, grant)) == ToolPermissionDecision.InvalidGrant &&
            await Decide(new(ToolPermissionLookupStatus.Found, null)) == ToolPermissionDecision.InvalidGrant,
            "11V empty ID, unknown enums and malformed grants fail closed");
        check((await policy.EvaluateAsync(new ToolPermissionValidationRequest(scope, tool.Id,
                (ToolPermissionRequirement)999), new(ToolPermissionLookupStatus.Found, grant),
                CancellationToken.None)).Decision == ToolPermissionDecision.InvalidGrant,
            "11V unknown requested requirement fails closed");
        check(!ToolIdentifier.TryCreate("*", out _) &&
            !ToolIdentifier.TryCreate("permission-*", out _) &&
            grant.Scope == scope && grant.ToolId == tool.Id,
            "11V wildcard tool IDs unavailable; grant binds exact scope and tool");
        var callsBefore = source.Calls;
        source.Lookup = new(ToolPermissionLookupStatus.Found, grant);
        check(await validator.HasPermissionAsync(scope, tool, CancellationToken.None),
            "11V exact grant can be allowed before revocation");
        source.Lookup = new(ToolPermissionLookupStatus.Found, grant with { IsRevoked = true });
        check(!await validator.HasPermissionAsync(scope, tool, CancellationToken.None) &&
            source.Calls == callsBefore + 2,
            "11V next call reads revoked grant; no stale allow cache");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); var observed = false;
            try { await validator.HasPermissionAsync(scope, tool, cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed, "11V cancellation before source propagates");
        }
        using (var cancelled = new CancellationTokenSource())
        {
            source.Cancel = cancelled; var observed = false;
            try { await validator.HasPermissionAsync(scope, tool, cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed, "11V cancellation during source propagates");
            source.Cancel = null;
        }
        source.ThrowFailure = true;
        var failed = await validator.ValidateAsync(scope, tool, CancellationToken.None);
        check(failed.Decision == ToolPermissionDecision.Failed && !failed.Message.Contains("secret"),
            "11V source exception maps fixed safe failure");
        source.ThrowFailure = false;
        var throwingPolicy = new ThrowingPolicy();
        failed = await new ToolExecutionPermissionValidator(source, throwingPolicy)
            .ValidateAsync(scope, tool, CancellationToken.None);
        check(failed.Decision == ToolPermissionDecision.Failed && !failed.Message.Contains("secret"),
            "11V policy exception maps fixed safe failure");
        source.Lookup = new(ToolPermissionLookupStatus.Found, grant);

        var handler = new Handler();
        var registry = new ToolRegistry([new(tool, handler)]);
        var owner = new Owner(scope);
        var clock = new Clock();
        var lifecycle = new ActionLifecycleService(registry, owner, clock, new PrivacyGuard());
        var proposed = (await lifecycle.ProposeAsync(scope, new(tool.Id, "Inspect project"))).Action!;
        var waiting = (await lifecycle.TransitionAsync(scope, proposed,
            ActionLifecycleStatus.ApprovalRequired)).Action!;
        var decision = await new ApprovalWorkflowService(registry, owner, clock)
            .DecideAsync(scope, waiting, new(ApprovalDecision.Approve));
        var approved = decision.Action!;
        var approval = decision.Evidence!;
        async Task<(ExecutionOperationResult Result, AtomicExecutionStore Store)> Execute(
            IToolExecutionPermissionValidator permission, IToolExecutionPolicy? executionPolicy = null,
            IToolExecutionScopeValidator? owned = null)
        {
            var store = new AtomicExecutionStore();
            var service = new TrustedToolExecutionService(registry, owned ?? owner, permission,
                executionPolicy ?? new AllowExecutionPolicy(), clock, store);
            var result = await service.ExecuteAsync(scope, approved, approval,
                new ExecutionRequest(approved.Id));
            return (result, store);
        }
        source.Lookup = new(ToolPermissionLookupStatus.NotFound, null);
        var (denied, deniedStore) = await Execute(validator);
        check(denied.Status == ExecutionOperationStatus.PermissionDenied && handler.Calls == 0 &&
            deniedStore.Reservations == 0,
            "11V approval, ownership, tool registration and policy do not bypass missing permission");
        source.Lookup = new(ToolPermissionLookupStatus.Unavailable, null);
        (denied, deniedStore) = await Execute(validator);
        check(denied.Status == ExecutionOperationStatus.PermissionDenied && handler.Calls == 0 &&
            deniedStore.Reservations == 0,
            "11V unavailable permission denies before reservation and handler");
        source.Lookup = new(ToolPermissionLookupStatus.Found, grant with { IsRevoked = true });
        (denied, deniedStore) = await Execute(validator);
        check(denied.Status == ExecutionOperationStatus.PermissionDenied && handler.Calls == 0 &&
            deniedStore.Reservations == 0,
            "11V revoked permission denies before reservation and handler");
        source.Lookup = new(ToolPermissionLookupStatus.Found, grant);
        (denied, deniedStore) = await Execute(validator, new DenyToolExecutionPolicy());
        check(denied.Status == ExecutionOperationStatus.PolicyDenied && handler.Calls == 0 &&
            deniedStore.Reservations == 0,
            "11V valid permission still requires independent execution policy");
        (denied, deniedStore) = await Execute(validator, owned: new DenyOwner());
        check(denied.Status == ExecutionOperationStatus.ScopeDenied && handler.Calls == 0 &&
            deniedStore.Reservations == 0,
            "11V valid permission still requires ownership");
        var unapproved = await new TrustedToolExecutionService(registry, owner, validator,
            new AllowExecutionPolicy(), clock, new AtomicExecutionStore())
            .ExecuteAsync(scope, waiting, approval, new ExecutionRequest(waiting.Id));
        check(unapproved.Status == ExecutionOperationStatus.InvalidActionState,
            "11V valid permission cannot replace action approval");
        var unavailableStore = await new TrustedToolExecutionService(registry, owner, validator,
            new AllowExecutionPolicy(), clock, new UnavailableActionExecutionStateStore())
            .ExecuteAsync(scope, approved, approval, new ExecutionRequest(approved.Id));
        check(unavailableStore.Status == ExecutionOperationStatus.StateUnavailable,
            "11V valid permission still requires durable reservation");
        check(source.LastRequest == new ToolPermissionValidationRequest(scope, tool.Id,
            tool.RequiredPermission) && typeof(AiChatRequest).GetProperties().Select(x => x.Name)
                .SequenceEqual(["Prompt"]) && !new ProjectFileAccessOptions().Enabled,
            "11V server-derived request; chat Prompt-only; Safe File Access disabled");
        foreach (var status in Enum.GetValues<ToolPermissionDecision>())
            check(ToolPermissionValidationResult.Denied(status).Message.Length <= 80,
                "11V fixed bounded result " + status);
        check(ToolPermissionValidationResult.Denied(ToolPermissionDecision.Allowed).Decision ==
            ToolPermissionDecision.Failed,
            "11V public denied factory cannot mint Allowed");
    }

    private sealed class Source : IToolPermissionSource
    {
        public ToolPermissionLookupResult Lookup = new(ToolPermissionLookupStatus.NotFound, null);
        public ToolPermissionValidationRequest? LastRequest;
        public int Calls;
        public bool ThrowFailure;
        public CancellationTokenSource? Cancel;
        public Task<ToolPermissionLookupResult> GetAsync(ToolPermissionValidationRequest request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++; LastRequest = request;
            if (ThrowFailure) throw new InvalidOperationException("secret source error");
            Cancel?.Cancel(); return Task.FromResult(Lookup);
        }
    }
    private sealed class ThrowingPolicy : IToolPermissionPolicy
    {
        public Task<ToolPermissionValidationResult> EvaluateAsync(ToolPermissionValidationRequest request,
            ToolPermissionLookupResult lookup, CancellationToken token) =>
            throw new InvalidOperationException("secret policy error");
    }
    private sealed class Handler : IToolHandler
    {
        public int Calls;
        public Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(ToolExecutionResult.FromStatus(ToolExecutionStatus.Success)); }
    }
    private sealed class Owner(ActionScope scope) : IActionScopeValidator,
        IActionApprovalScopeValidator, IToolExecutionScopeValidator
    {
        public Task<bool> ValidateAsync(ActionScope value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(value == scope); }
    }
    private sealed class DenyOwner : IToolExecutionScopeValidator
    {
        public Task<bool> ValidateAsync(ActionScope value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(false); }
    }
    private sealed class AllowExecutionPolicy : IToolExecutionPolicy
    {
        public Task<bool> AllowsAsync(ToolDescriptor tool, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(true); }
    }
    private sealed class Clock : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
    }
}
