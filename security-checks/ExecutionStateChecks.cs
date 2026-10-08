using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Executions;
using Aura.Application.Tools;
using System.Reflection;

internal static class ExecutionStateChecks
{
    public static async Task RunAsync(Action<bool, string> check, ActionDescriptor action,
        ApprovalEvidence approval, ToolIdentifier alternateTool, ActionIdentifier otherActionId)
    {
        var ready = ActionExecutionState.CreateReady(action, approval)!;
        check(ready is not null && ActionExecutionStatePolicy.IsValid(ready) &&
            ready.Status == ActionExecutionStateStatus.Ready && ready.Version == 0 &&
            ready.ActionId == action.Id && ready.Scope == action.Scope &&
            ready.ToolId == action.ToolId && ready.ApprovalId == approval.Id.Value &&
            ready.ExecutionId is null && ready.ApprovalConsumedByExecutionId is null && ready.Outcome is null,
            "11S exact approved initial state, unconsumed at version zero");
        check(ActionExecutionState.CreateReady(null, approval) is null &&
            ActionExecutionState.CreateReady(action, null) is null,
            "11S initial state requires approved action and evidence");
        var id = NewFixtureAttemptId();
        var otherId = NewFixtureAttemptId();
        var reserve = new ReserveExecutionRequest(action.Id, action.Scope, action.ToolId,
            approval.Id.Value, 0, id);
        static ActionExecutionStateResultStatus R(ActionExecutionState? s, ReserveExecutionRequest q) =>
            ActionExecutionStatePolicy.TryReserve(s, q).Status;
        static ActionExecutionStateResultStatus C(ActionExecutionState? s, CompleteExecutionRequest q) =>
            ActionExecutionStatePolicy.TryComplete(s, q).Status;
        check(R(ready, reserve with { ActionId = null! }) == ActionExecutionStateResultStatus.InvalidRequest &&
            R(ready, reserve with { Scope = action.Scope with { UserId = Guid.Empty } }) == ActionExecutionStateResultStatus.InvalidRequest &&
            R(ready, reserve with { ApprovalId = Guid.Empty }) == ActionExecutionStateResultStatus.InvalidRequest,
            "11S malformed reservation identities rejected");
        check(R(ready, reserve with { ActionId = otherActionId }) == ActionExecutionStateResultStatus.NotFound,
            "11S exact action binding");
        foreach (var scope in new[] { action.Scope with { UserId = Guid.NewGuid() },
            action.Scope with { ConversationId = Guid.NewGuid() },
            action.Scope with { ProjectId = Guid.NewGuid() } })
            check(R(ready, reserve with { Scope = scope }) == ActionExecutionStateResultStatus.ScopeMismatch,
                "11S exact user/conversation/project scope binding");
        check(R(ready, reserve with { ToolId = alternateTool }) == ActionExecutionStateResultStatus.ToolMismatch &&
            R(ready, reserve with { ApprovalId = Guid.NewGuid() }) == ActionExecutionStateResultStatus.ApprovalMismatch,
            "11S exact tool and approval binding");
        check(R(ready, reserve with { ExpectedVersion = -1 }) == ActionExecutionStateResultStatus.InvalidRequest &&
            R(ready, reserve with { ExpectedVersion = 1 }) == ActionExecutionStateResultStatus.StaleVersion,
            "11S nonnegative expected version and stale reserve");
        check(!ActionExecutionStatePolicy.IsValid(ready with { Version = -1 }) &&
            !ActionExecutionStatePolicy.IsValid(ready with { ApprovalConsumedByExecutionId = id.Value }) &&
            !ActionExecutionStatePolicy.IsValid(ready with { Status = (ActionExecutionStateStatus)99 }),
            "11S negative, malformed Ready and unknown state rejected");
        check(R(ready with { Version = long.MaxValue }, reserve with { ExpectedVersion = long.MaxValue }) ==
            ActionExecutionStateResultStatus.InvalidState, "11S reservation overflow fails closed");
        var first = ActionExecutionStatePolicy.TryReserve(ready, reserve);
        var reserved = first.State!;
        check(first.Status == ActionExecutionStateResultStatus.Success && reserved.Status == ActionExecutionStateStatus.Reserved &&
            reserved.Version == 1 && ready.Version == 0 && reserved.ExecutionId == id.Value &&
            reserved.ApprovalConsumedByExecutionId == id.Value && reserved.Outcome is null &&
            ActionExecutionStatePolicy.IsValid(reserved),
            "11S first reservation atomically binds execution and consumes approval once");
        check(!ActionExecutionStatePolicy.IsValid(reserved with { ExecutionId = null }) &&
            !ActionExecutionStatePolicy.IsValid(reserved with { ApprovalConsumedByExecutionId = otherId.Value }) &&
            !ActionExecutionStatePolicy.IsValid(reserved with { Outcome = ExecutionOutcome.Succeeded }),
            "11S malformed Reserved state rejected");
        check(R(reserved, reserve) == ActionExecutionStateResultStatus.StaleVersion &&
            R(reserved, reserve with { ExpectedVersion = 1 }) == ActionExecutionStateResultStatus.AlreadyReserved &&
            R(reserved, reserve with { ExpectedVersion = 1, ExecutionId = otherId }) ==
                ActionExecutionStateResultStatus.AlreadyReserved,
            "11S stale and duplicate reserve cannot mint another execution ID");
        check(R(reserved, reserve with { ExpectedVersion = 1, Scope = action.Scope with { UserId = Guid.NewGuid() } }) ==
            ActionExecutionStateResultStatus.ScopeMismatch &&
            R(reserved, reserve with { ExpectedVersion = 1, ToolId = alternateTool }) ==
            ActionExecutionStateResultStatus.ToolMismatch,
            "11S consumed approval cannot transfer to another scope or tool");
        var complete = new CompleteExecutionRequest(action.Id, action.Scope, action.ToolId,
            approval.Id.Value, id, 1, ExecutionOutcome.Succeeded);
        check(C(reserved, complete with { ApprovalId = Guid.NewGuid() }) == ActionExecutionStateResultStatus.ApprovalMismatch &&
            C(reserved, complete with { ToolId = alternateTool }) == ActionExecutionStateResultStatus.ToolMismatch &&
            C(reserved, complete with { Scope = action.Scope with { ProjectId = Guid.NewGuid() } }) ==
                ActionExecutionStateResultStatus.ScopeMismatch,
            "11S completion requires exact approval, tool and scope");
        check(C(reserved, complete with { ExecutionId = otherId }) == ActionExecutionStateResultStatus.ExecutionMismatch &&
            C(reserved, complete with { ExpectedVersion = 0 }) == ActionExecutionStateResultStatus.StaleVersion &&
            C(reserved, complete with { Outcome = (ExecutionOutcome)99 }) == ActionExecutionStateResultStatus.InvalidRequest,
            "11S wrong execution, stale completion and invalid outcome rejected");
        var completed = ActionExecutionStatePolicy.TryComplete(reserved, complete).State!;
        check(completed.Status == ActionExecutionStateStatus.Succeeded && completed.Version == 2 &&
            completed.Outcome == ExecutionOutcome.Succeeded && completed.ExecutionId == id.Value &&
            completed.ApprovalConsumedByExecutionId == id.Value && ActionExecutionStatePolicy.IsValid(completed),
            "11S completion increments once and preserves exact provenance");
        check(C(completed, complete with { ExpectedVersion = 2 }) == ActionExecutionStateResultStatus.AlreadyCompleted &&
            R(completed, reserve with { ExpectedVersion = 2 }) == ActionExecutionStateResultStatus.AlreadyCompleted &&
            !ActionExecutionStatePolicy.IsValid(completed with { Outcome = null }),
            "11S terminal state rejects duplicate completion and reserve");
        var failed = ActionExecutionStatePolicy.TryComplete(reserved, complete with { Outcome = ExecutionOutcome.Failed }).State!;
        check(failed.Status == ActionExecutionStateStatus.Failed && failed.Outcome == ExecutionOutcome.Failed &&
            C(failed, complete with { ExpectedVersion = 2 }) == ActionExecutionStateResultStatus.AlreadyCompleted &&
            R(failed, reserve with { ExpectedVersion = 2 }) == ActionExecutionStateResultStatus.AlreadyCompleted,
            "11S Failed is terminal and cannot retry");
        check(!ActionExecutionStatePolicy.IsValid(failed with { Outcome = ExecutionOutcome.Succeeded }) &&
            !ActionExecutionStatePolicy.IsValid(reserved with { Version = 0 }) &&
            !ActionExecutionStatePolicy.IsValid(completed with { Version = 1 }),
            "11S malformed terminal and lifecycle versions fail closed");
        check(C(reserved with { Version = long.MaxValue }, complete with { ExpectedVersion = long.MaxValue }) ==
            ActionExecutionStateResultStatus.InvalidState, "11S completion overflow fails closed");

        var fake = new AtomicFakeStore(ready);
        using var start = new ManualResetEventSlim(false);
        var firstCall = Task.Run(async () => { start.Wait(); return await fake.TryReserveAsync(reserve, CancellationToken.None); });
        var secondCall = Task.Run(async () => { start.Wait(); return await fake.TryReserveAsync(
            reserve with { ExecutionId = otherId }, CancellationToken.None); });
        start.Set();
        var calls = await Task.WhenAll(firstCall, secondCall);
        check(calls.Count(x => x.Status == ActionExecutionStateResultStatus.Success) == 1 &&
            calls.Count(x => x.Status == ActionExecutionStateResultStatus.StaleVersion) == 1 &&
            fake.State.Version == 1 && fake.Mutations == 1 &&
            fake.State.ExecutionId == fake.State.ApprovalConsumedByExecutionId &&
            new[] { id.Value, otherId.Value }.Contains(fake.State.ExecutionId!.Value),
            "11S two concurrent fake reserves yield exactly one atomic mutation");
        check(fake.HandlerCalls == 0 && approval.Id.Value == ready.ApprovalId &&
            approval.MatchesBinding(action.Id, action.Scope, action.ToolId),
            "11S store never invokes handler or mutates approval evidence");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var observed = false;
        try { await fake.TryCompleteAsync(complete, cancelled.Token); }
        catch (OperationCanceledException) { observed = true; }
        check(observed && fake.Mutations == 1, "11S cancellation propagates before mutation");
        var unavailable = new UnavailableActionExecutionStateStore();
        check((await unavailable.TryReserveAsync(reserve, CancellationToken.None)).Status ==
            ActionExecutionStateResultStatus.Unavailable &&
            (await unavailable.TryCompleteAsync(complete, CancellationToken.None)).Status ==
            ActionExecutionStateResultStatus.Unavailable,
            "11S default store denies without persistence or handler");
        foreach (var status in Enum.GetValues<ActionExecutionStateResultStatus>())
            check(ActionExecutionStateResult.Denied(status).Message.Length <= 80 &&
                ActionExecutionStateResult.Denied(status).State is null,
                "11S fixed bounded denial " + status);
        check(typeof(ExecutionRequest).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "ActionId" }) &&
            typeof(ExecutionAttemptIdentifier).GetConstructors().Length == 0,
            "11S public execution request cannot supply execution attempt ID");
        check(typeof(ExecutionAttemptIdentifier).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .All(method => method.ReturnType != typeof(ExecutionAttemptIdentifier)),
            "11T attempt identifier has no public static minting factory");
    }

    // Test-only reflection supplies IDs to the pure state policy without adding a
    // production factory or granting security-checks Application internals access.
    private static ExecutionAttemptIdentifier NewFixtureAttemptId() =>
        (ExecutionAttemptIdentifier)typeof(ExecutionAttemptIdentifier).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(Guid)], null)!
            .Invoke([Guid.NewGuid()]);

    private sealed class AtomicFakeStore(ActionExecutionState initial) : IActionExecutionStateStore
    {
        private readonly object gate = new();
        public ActionExecutionState State { get; private set; } = initial;
        public int Mutations { get; private set; }
        public int HandlerCalls => 0;

        public Task<ActionExecutionStateResult> TryReserveInitialAsync(ActionExecutionState ready,
            ExecutionAttemptIdentifier executionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ActionExecutionStateResult.Denied(ActionExecutionStateResultStatus.Unavailable));
        }

        public Task<ActionExecutionStateResult> TryReserveAsync(ReserveExecutionRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = ActionExecutionStatePolicy.TryReserve(State, request);
                if (result.State is not null) { State = result.State; Mutations++; }
                return Task.FromResult(result);
            }
        }

        public Task<ActionExecutionStateResult> TryCompleteAsync(CompleteExecutionRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = ActionExecutionStatePolicy.TryComplete(State, request);
                if (result.State is not null) { State = result.State; Mutations++; }
                return Task.FromResult(result);
            }
        }
    }
}
