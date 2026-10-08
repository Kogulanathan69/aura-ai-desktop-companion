using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Tools;

namespace Aura.Application.Executions;

// A store snapshot, not an authorization token. A transactional adapter must enforce
// compare-and-swap and unique approval ownership across all action rows.
public enum ActionExecutionStateStatus { Ready, Reserved, Succeeded, Failed }

public sealed record ActionExecutionState(
    ActionIdentifier ActionId, ActionScope Scope, ToolIdentifier ToolId, Guid ApprovalId,
    ActionExecutionStateStatus Status, long Version, Guid? ExecutionId,
    Guid? ApprovalConsumedByExecutionId, ExecutionOutcome? Outcome)
{
    public static ActionExecutionState? CreateReady(ActionDescriptor? action, ApprovalEvidence? approval) =>
        action is not null && approval is not null &&
        ApprovalValidation.IsBoundToApprovedAction(approval, action, action.Scope, action.ToolId) &&
        ValidIdentity(action.Id, action.Scope, action.ToolId, approval.Id.Value)
            ? new(action.Id, action.Scope, action.ToolId, approval.Id.Value,
                ActionExecutionStateStatus.Ready, 0, null, null, null)
            : null;

    internal static bool ValidIdentity(ActionIdentifier? actionId, ActionScope? scope,
        ToolIdentifier? toolId, Guid approvalId) =>
        actionId is not null && actionId.Value != Guid.Empty && scope is not null &&
        scope.UserId != Guid.Empty && scope.ConversationId != Guid.Empty &&
        scope.ProjectId != Guid.Empty && toolId is not null &&
        !string.IsNullOrEmpty(toolId.Value) && approvalId != Guid.Empty;
}

// Only the trusted application workflow may create an attempt ID. It is absent from
// the public ExecutionRequest and never accepted from a chat/provider request.
public sealed class ExecutionAttemptIdentifier
{
    public Guid Value { get; }
    private ExecutionAttemptIdentifier(Guid value) => Value = value;
    public static ExecutionAttemptIdentifier CreateForTrustedWorkflow() => new(Guid.NewGuid());
}

public sealed record ReserveExecutionRequest(ActionIdentifier ActionId, ActionScope Scope,
    ToolIdentifier ToolId, Guid ApprovalId, long ExpectedVersion, ExecutionAttemptIdentifier ExecutionId);

public sealed record CompleteExecutionRequest(ActionIdentifier ActionId, ActionScope Scope,
    ToolIdentifier ToolId, Guid ApprovalId, ExecutionAttemptIdentifier ExecutionId,
    long ExpectedVersion, ExecutionOutcome Outcome);

public enum ActionExecutionStateResultStatus
{
    Success, InvalidRequest, InvalidState, NotFound, StaleVersion, AlreadyReserved,
    AlreadyCompleted, ExecutionMismatch, ScopeMismatch, ApprovalMismatch, ToolMismatch,
    Unavailable, Failed
}

public sealed class ActionExecutionStateResult
{
    public ActionExecutionStateResultStatus Status { get; }
    public string Message { get; }
    public ActionExecutionState? State { get; }
    private ActionExecutionStateResult(ActionExecutionStateResultStatus status, string message,
        ActionExecutionState? state = null)
    { Status = status; Message = message; State = state; }

    internal static ActionExecutionStateResult Success(ActionExecutionState state) =>
        new(ActionExecutionStateResultStatus.Success, "Execution state updated.", state);

    public static ActionExecutionStateResult Denied(ActionExecutionStateResultStatus status) => status switch
    {
        ActionExecutionStateResultStatus.InvalidRequest => new(status, "Invalid execution state request."),
        ActionExecutionStateResultStatus.InvalidState => new(status, "Execution state is invalid."),
        ActionExecutionStateResultStatus.NotFound => new(status, "Execution state is unavailable."),
        ActionExecutionStateResultStatus.StaleVersion => new(status, "Execution state version is stale."),
        ActionExecutionStateResultStatus.AlreadyReserved => new(status, "Execution is already reserved."),
        ActionExecutionStateResultStatus.AlreadyCompleted => new(status, "Execution is already completed."),
        ActionExecutionStateResultStatus.ExecutionMismatch => new(status, "Execution identity does not match."),
        ActionExecutionStateResultStatus.ScopeMismatch => new(status, "Execution scope does not match."),
        ActionExecutionStateResultStatus.ApprovalMismatch => new(status, "Execution approval does not match."),
        ActionExecutionStateResultStatus.ToolMismatch => new(status, "Execution tool does not match."),
        ActionExecutionStateResultStatus.Unavailable => new(status, "Execution state store is unavailable."),
        _ => new(ActionExecutionStateResultStatus.Failed, "Execution state operation failed.")
    };
}

public interface IActionExecutionStateStore
{
    Task<ActionExecutionStateResult> TryReserveAsync(ReserveExecutionRequest request,
        CancellationToken cancellationToken);
    Task<ActionExecutionStateResult> TryCompleteAsync(CompleteExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed class UnavailableActionExecutionStateStore : IActionExecutionStateStore
{
    public Task<ActionExecutionStateResult> TryReserveAsync(ReserveExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ActionExecutionStateResult.Denied(ActionExecutionStateResultStatus.Unavailable));
    }

    public Task<ActionExecutionStateResult> TryCompleteAsync(CompleteExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ActionExecutionStateResult.Denied(ActionExecutionStateResultStatus.Unavailable));
    }
}
