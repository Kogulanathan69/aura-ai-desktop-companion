namespace Aura.Application.Executions;

// Pure transition rules. Persistence adapters must make read/check/write one atomic
// transaction; calling this policy alone provides no concurrent replay protection.
public static class ActionExecutionStatePolicy
{
    public static bool IsValid(ActionExecutionState? state)
    {
        if (state is null || !ActionExecutionState.ValidIdentity(state.ActionId, state.Scope,
                state.ToolId, state.ApprovalId) || state.Version < 0 || !Enum.IsDefined(state.Status))
            return false;
        return state.Status switch
        {
            ActionExecutionStateStatus.Ready => state.ExecutionId is null &&
                state.ApprovalConsumedByExecutionId is null && state.Outcome is null,
            ActionExecutionStateStatus.Reserved => state.Version >= 1 &&
                state.ExecutionId is Guid id && id != Guid.Empty &&
                state.ApprovalConsumedByExecutionId == id && state.Outcome is null,
            ActionExecutionStateStatus.Succeeded => TerminalValid(state, ExecutionOutcome.Succeeded),
            ActionExecutionStateStatus.Failed => TerminalValid(state, ExecutionOutcome.Failed),
            _ => false
        };
    }

    private static bool TerminalValid(ActionExecutionState state, ExecutionOutcome expected) =>
        state.Version >= 2 && state.ExecutionId is Guid id && id != Guid.Empty &&
        state.ApprovalConsumedByExecutionId == id && state.Outcome == expected;

    public static ActionExecutionStateResult TryReserve(ActionExecutionState? state,
        ReserveExecutionRequest? request)
    {
        if (request is null || !ActionExecutionState.ValidIdentity(request.ActionId, request.Scope,
                request.ToolId, request.ApprovalId) || request.ExecutionId is null ||
            request.ExecutionId.Value == Guid.Empty || request.ExpectedVersion < 0)
            return Deny(ActionExecutionStateResultStatus.InvalidRequest);
        var binding = CheckBinding(state, request.ActionId, request.Scope, request.ToolId, request.ApprovalId);
        if (binding != ActionExecutionStateResultStatus.Success) return Deny(binding);
        if (state!.Version != request.ExpectedVersion) return Deny(ActionExecutionStateResultStatus.StaleVersion);
        if (state.Status == ActionExecutionStateStatus.Reserved) return Deny(ActionExecutionStateResultStatus.AlreadyReserved);
        if (state.Status != ActionExecutionStateStatus.Ready) return Deny(ActionExecutionStateResultStatus.AlreadyCompleted);
        if (state.Version == long.MaxValue) return Deny(ActionExecutionStateResultStatus.InvalidState);
        return ActionExecutionStateResult.Success(state with
        {
            Status = ActionExecutionStateStatus.Reserved, Version = state.Version + 1,
            ExecutionId = request.ExecutionId.Value,
            ApprovalConsumedByExecutionId = request.ExecutionId.Value
        });
    }

    public static ActionExecutionStateResult TryComplete(ActionExecutionState? state,
        CompleteExecutionRequest? request)
    {
        if (request is null || !ActionExecutionState.ValidIdentity(request.ActionId, request.Scope,
                request.ToolId, request.ApprovalId) || request.ExecutionId is null ||
            request.ExecutionId.Value == Guid.Empty || request.ExpectedVersion < 0 ||
            !Enum.IsDefined(request.Outcome))
            return Deny(ActionExecutionStateResultStatus.InvalidRequest);
        var binding = CheckBinding(state, request.ActionId, request.Scope, request.ToolId, request.ApprovalId);
        if (binding != ActionExecutionStateResultStatus.Success) return Deny(binding);
        if (state!.Version != request.ExpectedVersion) return Deny(ActionExecutionStateResultStatus.StaleVersion);
        if (state.ExecutionId != request.ExecutionId.Value)
            return Deny(ActionExecutionStateResultStatus.ExecutionMismatch);
        if (state.Status != ActionExecutionStateStatus.Reserved)
            return Deny(state.Status == ActionExecutionStateStatus.Ready
                ? ActionExecutionStateResultStatus.InvalidState : ActionExecutionStateResultStatus.AlreadyCompleted);
        if (state.Version == long.MaxValue) return Deny(ActionExecutionStateResultStatus.InvalidState);
        return ActionExecutionStateResult.Success(state with
        {
            Status = request.Outcome == ExecutionOutcome.Succeeded
                ? ActionExecutionStateStatus.Succeeded : ActionExecutionStateStatus.Failed,
            Version = state.Version + 1, Outcome = request.Outcome
        });
    }

    private static ActionExecutionStateResultStatus CheckBinding(ActionExecutionState? state,
        Aura.Application.Actions.ActionIdentifier actionId, Aura.Application.Actions.ActionScope scope,
        Aura.Application.Tools.ToolIdentifier toolId, Guid approvalId)
    {
        if (state is null) return ActionExecutionStateResultStatus.NotFound;
        if (!IsValid(state)) return ActionExecutionStateResultStatus.InvalidState;
        if (state.ActionId != actionId) return ActionExecutionStateResultStatus.NotFound;
        if (state.Scope != scope) return ActionExecutionStateResultStatus.ScopeMismatch;
        if (state.ToolId != toolId) return ActionExecutionStateResultStatus.ToolMismatch;
        if (state.ApprovalId != approvalId) return ActionExecutionStateResultStatus.ApprovalMismatch;
        return ActionExecutionStateResultStatus.Success;
    }

    private static ActionExecutionStateResult Deny(ActionExecutionStateResultStatus status) =>
        ActionExecutionStateResult.Denied(status);
}
