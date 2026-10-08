using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Privacy.Services;
using Aura.Application.Tools;
using System.Reflection;

internal static class ExecutionIntegrationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var valid = await Scenario.CreateAsync();
        var result = await valid.ExecuteAsync();
        var state = valid.Store.State!;
        check(result.Status == ExecutionOperationStatus.Success && valid.Store.Reservations == 1 &&
            valid.Store.Completions == 1 && valid.Handler.Calls == 1 &&
            state.Status == ActionExecutionStateStatus.Succeeded && state.Version == 2,
            "11T valid flow reserves, invokes one handler, then completes once");
        check(state.ExecutionId == result.Action?.ToolExecutionId &&
            state.ExecutionId == result.Evidence?.Id.Value &&
            state.ExecutionId == state.ApprovalConsumedByExecutionId &&
            state.ExecutionId == result.Evidence?.ToVerificationInput().ExecutionId &&
            state.ApprovalId == valid.Approval.Id.Value &&
            result.Action?.IsVerified == false && result.Action.VerificationId is null,
            "11T one server ID links reservation, action, evidence, completion and handoff");
        check(valid.Store.LastCompletionRequest?.ExecutionId.Value == state.ExecutionId &&
            valid.Store.LastCompletionRequest.ExpectedVersion == 1 &&
            valid.Store.LastCompletionRequest.ActionId == valid.Action.Id &&
            valid.Store.LastCompletionRequest.Scope == valid.Scope &&
            valid.Store.LastCompletionRequest.ToolId == valid.Action.ToolId &&
            valid.Store.LastCompletionRequest.ApprovalId == valid.Approval.Id.Value &&
            valid.Store.LastCompletionRequest.Outcome == ExecutionOutcome.Succeeded,
            "11T completion request uses exact reserved bindings and server-known version");
        var replay = await valid.ExecuteAsync();
        check(replay.Status == ExecutionOperationStatus.StateConflict && replay.Action is null &&
            replay.Evidence is null && valid.Handler.Calls == 1 && valid.Store.Reservations == 1,
            "11T stale Approved replay is blocked by atomic state without another handler call");
        check(typeof(ExecutionRequest).GetProperties().Select(x => x.Name).SequenceEqual(["ActionId"]) &&
            typeof(ExecutionAttemptIdentifier).GetConstructors().Length == 0 &&
            typeof(ExecutionAttemptIdentifier).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .All(method => method.ReturnType != typeof(ExecutionAttemptIdentifier)),
            "11T caller has no ID/version request field, constructor, or public minting factory");

        var mappings = new (ActionExecutionStateResultStatus Store, ExecutionOperationStatus Expected)[]
        {
            (ActionExecutionStateResultStatus.InvalidRequest, ExecutionOperationStatus.InvalidRequest),
            (ActionExecutionStateResultStatus.ScopeMismatch, ExecutionOperationStatus.ScopeDenied),
            (ActionExecutionStateResultStatus.ApprovalMismatch, ExecutionOperationStatus.ApprovalDenied),
            (ActionExecutionStateResultStatus.ToolMismatch, ExecutionOperationStatus.UnknownTool),
            (ActionExecutionStateResultStatus.StaleVersion, ExecutionOperationStatus.StateConflict),
            (ActionExecutionStateResultStatus.AlreadyReserved, ExecutionOperationStatus.StateConflict),
            (ActionExecutionStateResultStatus.AlreadyCompleted, ExecutionOperationStatus.StateConflict),
            (ActionExecutionStateResultStatus.ExecutionMismatch, ExecutionOperationStatus.StateConflict),
            (ActionExecutionStateResultStatus.NotFound, ExecutionOperationStatus.StateUnavailable),
            (ActionExecutionStateResultStatus.InvalidState, ExecutionOperationStatus.StateUnavailable),
            (ActionExecutionStateResultStatus.Unavailable, ExecutionOperationStatus.StateUnavailable),
            (ActionExecutionStateResultStatus.Failed, ExecutionOperationStatus.StateUnavailable)
        };
        foreach (var (storeStatus, expected) in mappings)
        {
            var scenario = await Scenario.CreateAsync();
            scenario.Store.ReserveOverride = storeStatus;
            var denied = await scenario.ExecuteAsync();
            check(denied.Status == expected && denied.Action is null && denied.Evidence is null &&
                scenario.Handler.Calls == 0 && scenario.Store.Reservations == 0,
                "11T reserve " + storeStatus + " denies handler with fixed result");
        }
        var unavailable = await Scenario.CreateAsync();
        check((await unavailable.ExecuteWithStoreAsync(new UnavailableActionExecutionStateStore())).Status ==
            ExecutionOperationStatus.StateUnavailable && unavailable.Handler.Calls == 0,
            "11T supplied unavailable store fails closed");
        var thrownReserve = await Scenario.CreateAsync();
        thrownReserve.Store.ThrowOnReserve = true;
        var reserveFailure = await thrownReserve.ExecuteAsync();
        check(reserveFailure.Status == ExecutionOperationStatus.ReconciliationRequired &&
            !reserveFailure.Message.Contains("secret") && thrownReserve.Handler.Calls == 0,
            "11T uncertain reserve exception requires reconciliation, no handler");

        var failedHandler = await Scenario.CreateAsync();
        failedHandler.Handler.Result = ToolExecutionResult.FromStatus(ToolExecutionStatus.Failed);
        var failedResult = await failedHandler.ExecuteAsync();
        check(failedResult.Status == ExecutionOperationStatus.ExecutionFailed &&
            failedResult.Action?.Status == ActionLifecycleStatus.Failed &&
            failedHandler.Store.State?.Status == ActionExecutionStateStatus.Failed &&
            failedHandler.Store.Completions == 1 && failedHandler.Handler.Calls == 1,
            "11T safe handler failure completes durable Failed once");
        var thrownHandler = await Scenario.CreateAsync();
        thrownHandler.Handler.ThrowFailure = true;
        var thrownResult = await thrownHandler.ExecuteAsync();
        check(thrownResult.Status == ExecutionOperationStatus.ExecutionFailed &&
            thrownHandler.Store.State?.Status == ActionExecutionStateStatus.Failed &&
            !thrownResult.Message.Contains("secret") && thrownHandler.Handler.Calls == 1,
            "11T handler exception maps fixed durable Failed without retry");

        foreach (var completionMode in new[] { "deny", "throw" })
        {
            var scenario = await Scenario.CreateAsync();
            scenario.Store.CompleteOverride = completionMode == "deny"
                ? ActionExecutionStateResultStatus.Unavailable : null;
            scenario.Store.ThrowOnComplete = completionMode == "throw";
            var failed = await scenario.ExecuteAsync();
            check(failed.Status == ExecutionOperationStatus.ReconciliationRequired &&
                failed.Action is null && failed.Evidence is null &&
                scenario.Store.State?.Status == ActionExecutionStateStatus.Reserved &&
                scenario.Handler.Calls == 1 && scenario.Store.Completions == 0 &&
                !failed.Message.Contains("secret"),
                "11T completion " + completionMode + " returns bounded reconciliation without retry");
        }
        var badClock = await Scenario.CreateAsync();
        badClock.Clock.InvalidCompletion = true;
        var badTime = await badClock.ExecuteAsync();
        check(badTime.Status == ExecutionOperationStatus.ReconciliationRequired &&
            badClock.Store.State?.Status == ActionExecutionStateStatus.Reserved &&
            badClock.Handler.Calls == 1,
            "11T invalid completion timestamp cannot fabricate durable completion");

        var before = await Scenario.CreateAsync();
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var observed = false;
            try { await before.ExecuteAsync(cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed && before.Store.Reservations == 0 && before.Handler.Calls == 0,
                "11T cancellation before reserve prevents handler");
        }
        var afterReserve = await Scenario.CreateAsync();
        using (var cancelled = new CancellationTokenSource())
        {
            afterReserve.Store.CancelAfterReserve = cancelled;
            var observed = false;
            try { await afterReserve.ExecuteAsync(cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed && afterReserve.Store.Reservations == 1 &&
                afterReserve.Store.State?.Status == ActionExecutionStateStatus.Reserved &&
                afterReserve.Handler.Calls == 0,
                "11T cancellation after reservation leaves state for reconciliation, no handler");
        }
        var duringHandler = await Scenario.CreateAsync();
        duringHandler.Handler.ThrowCancellation = true;
        var handlerCancelled = false;
        try { await duringHandler.ExecuteAsync(); }
        catch (OperationCanceledException) { handlerCancelled = true; }
        check(handlerCancelled && duringHandler.Store.Reservations == 1 &&
            duringHandler.Store.Completions == 0 && duringHandler.Handler.Calls == 1,
            "11T handler cancellation propagates without fabricated completion");
        var duringCompletion = await Scenario.CreateAsync();
        using (var cancelled = new CancellationTokenSource())
        {
            duringCompletion.Store.CancelOnComplete = cancelled;
            var observed = false;
            try { await duringCompletion.ExecuteAsync(cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed && duringCompletion.Store.Reservations == 1 &&
                duringCompletion.Store.Completions == 0 && duringCompletion.Handler.Calls == 1,
                "11T completion cancellation propagates without handler retry");
        }

        var concurrent = await Scenario.CreateAsync();
        using var start = new ManualResetEventSlim(false);
        var first = Task.Run(async () => { start.Wait(); return await concurrent.ExecuteAsync(); });
        var second = Task.Run(async () => { start.Wait(); return await concurrent.ExecuteAsync(); });
        start.Set();
        var results = await Task.WhenAll(first, second);
        check(results.Count(x => x.Status == ExecutionOperationStatus.Success) == 1 &&
            results.Count(x => x.Status == ExecutionOperationStatus.StateConflict) == 1 &&
            concurrent.Store.Reservations == 1 && concurrent.Store.Completions == 1 &&
            concurrent.Handler.Calls == 1,
            "11T concurrent fake calls reserve once and invoke handler at most once");
        check(valid.Approval.MatchesBinding(valid.Action.Id, valid.Scope, valid.Action.ToolId) &&
            valid.Approval.Id.Value == state.ApprovalId,
            "11T approval evidence remains immutable while store tracks consumption");
        foreach (var status in Enum.GetValues<ExecutionOperationStatus>())
            check(ExecutionOperationResult.Denied(status).Message.Length <= 80,
                "11T fixed bounded result " + status);
    }

    private sealed class Scenario
    {
        public required ActionScope Scope { get; init; }
        public required ActionDescriptor Action { get; init; }
        public required ApprovalEvidence Approval { get; init; }
        public required ToolRegistry Tools { get; init; }
        public required Handler Handler { get; init; }
        public required Clock Clock { get; init; }
        public AtomicExecutionStore Store { get; } = new();

        public Task<ExecutionOperationResult> ExecuteAsync(CancellationToken token = default) =>
            ExecuteWithStoreAsync(Store, token);

        public Task<ExecutionOperationResult> ExecuteWithStoreAsync(IActionExecutionStateStore store,
            CancellationToken token = default) =>
            new TrustedToolExecutionService(Tools, new ScopeGate(Scope), new PermissionGate(),
                new PolicyGate(), Clock, store).ExecuteAsync(Scope, Action, Approval,
                    new ExecutionRequest(Action.Id), token);

        public static async Task<Scenario> CreateAsync()
        {
            ToolIdentifier.TryCreate("step11t-fixture", out var id);
            var handler = new Handler();
            var tool = new ToolDescriptor(id!, "Fixture", "Synthetic fixture",
                ToolCapability.ProjectRead, ToolPermissionRequirement.OwnedProjectRead);
            var tools = new ToolRegistry([new(tool, handler)]);
            var scope = new ActionScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var clock = new Clock();
            var gate = new ScopeGate(scope);
            var lifecycle = new ActionLifecycleService(tools, gate, clock, new PrivacyGuard());
            var proposed = (await lifecycle.ProposeAsync(scope, new(id!, "Inspect project"))).Action!;
            var waiting = (await lifecycle.TransitionAsync(scope, proposed,
                ActionLifecycleStatus.ApprovalRequired)).Action!;
            var decision = await new ApprovalWorkflowService(tools, gate, clock)
                .DecideAsync(scope, waiting, new(ApprovalDecision.Approve));
            return new Scenario { Scope = scope, Action = decision.Action!, Approval = decision.Evidence!,
                Tools = tools, Handler = handler, Clock = clock };
        }
    }

    private sealed class Handler : IToolHandler
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public ToolExecutionResult Result = ToolExecutionResult.FromStatus(ToolExecutionStatus.Success);
        public bool ThrowFailure, ThrowCancellation;
        public Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref calls);
            if (ThrowCancellation) throw new OperationCanceledException();
            if (ThrowFailure) throw new InvalidOperationException("secret handler exception");
            return Task.FromResult(Result);
        }
    }

    private sealed class Clock : IDateTimeProvider
    {
        private int calls;
        private bool invalidCompletion;
        public bool InvalidCompletion { set { invalidCompletion = value; calls = 0; } }
        public DateTime UtcNow => invalidCompletion && Interlocked.Increment(ref calls) > 1
            ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
            : new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed class ScopeGate(ActionScope scope) : IActionScopeValidator,
        IActionApprovalScopeValidator, IToolExecutionScopeValidator
    {
        public Task<bool> ValidateAsync(ActionScope value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(value == scope); }
    }

    private sealed class PermissionGate : IToolExecutionPermissionValidator
    {
        public Task<bool> HasPermissionAsync(ActionScope scope, ToolDescriptor tool, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(true); }
    }

    private sealed class PolicyGate : IToolExecutionPolicy
    {
        public Task<bool> AllowsAsync(ToolDescriptor tool, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(true); }
    }
}
