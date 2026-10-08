using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Common.Interfaces;
using Aura.Application.Tools;

namespace Aura.Application.Executions;

public sealed class TrustedToolExecutionService(ToolRegistry tools, IToolExecutionScopeValidator scopes,
    IToolExecutionPermissionValidator permissions, IToolExecutionPolicy policy, IDateTimeProvider clock,
    IActionExecutionStateStore states)
    : ITrustedToolExecutionService
{
    public async Task<ExecutionOperationResult> ExecuteAsync(ActionScope scope, ActionDescriptor action,
        ApprovalEvidence approval, ExecutionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reserved = false;
        var reservationAttempted = false;
        try
        {
            if (scope is null || scope.UserId == Guid.Empty || scope.ConversationId == Guid.Empty || scope.ProjectId == Guid.Empty ||
                action is null || request?.ActionId is null || request.ActionId != action.Id)
                return ExecutionOperationResult.Denied(ExecutionOperationStatus.InvalidRequest);
            if (action.Scope != scope) return ExecutionOperationResult.Denied(ExecutionOperationStatus.ScopeDenied);
            if (action.Status != ActionLifecycleStatus.Approved || action.ToolExecutionId is not null)
                return ExecutionOperationResult.Denied(ExecutionOperationStatus.InvalidActionState);
            if (!ApprovalValidation.IsBoundToApprovedAction(approval, action, scope, action.ToolId))
                return ExecutionOperationResult.Denied(ExecutionOperationStatus.ApprovalDenied);
            var owned = await scopes.ValidateAsync(scope, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!owned) return ExecutionOperationResult.Denied(ExecutionOperationStatus.ScopeDenied);
            if (!tools.TryResolve(action.ToolId, out var tool, out var handler) || tool is null || handler is null ||
                tool.Id != action.ToolId)
                return ExecutionOperationResult.Denied(ExecutionOperationStatus.UnknownTool);
            // Descriptor.Enabled remains false. Only this separate exact-tool policy can allow
            // a synthetic Step 11R call; it never changes DisabledToolDispatcher.
            var permitted = await permissions.HasPermissionAsync(scope, tool, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!permitted) return ExecutionOperationResult.Denied(ExecutionOperationStatus.PermissionDenied);
            var allowed = await policy.AllowsAsync(tool, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!allowed) return ExecutionOperationResult.Denied(ExecutionOperationStatus.PolicyDenied);
            owned = await scopes.ValidateAsync(scope, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!owned) return ExecutionOperationResult.Denied(ExecutionOperationStatus.ScopeDenied);
            var startedAt = clock.UtcNow;
            if (startedAt.Kind != DateTimeKind.Utc || startedAt == default || startedAt < action.UpdatedAt)
                return ExecutionOperationResult.Denied(ExecutionOperationStatus.Failed);
            var ready = ActionExecutionState.CreateReady(action, approval);
            if (ready is null || states is null)
                return ExecutionOperationResult.Denied(ExecutionOperationStatus.StateUnavailable);
            var attempt = ExecutionAttemptIdentifier.CreateForTrustedWorkflow();
            reservationAttempted = true;
            var reservation = await states.TryReserveInitialAsync(ready, attempt, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (reservation is null)
                return ExecutionOperationResult.Denied(ExecutionOperationStatus.ReconciliationRequired);
            if (reservation.Status != ActionExecutionStateResultStatus.Success)
                return ExecutionOperationResult.Denied(MapReservationStatus(reservation.Status));
            reserved = true;
            var reservedState = reservation.State;
            if (reservedState is null || !ActionExecutionStatePolicy.IsValid(reservedState) ||
                reservedState.Status != ActionExecutionStateStatus.Reserved ||
                reservedState.ActionId != action.Id || reservedState.Scope != scope ||
                reservedState.ToolId != action.ToolId || reservedState.ApprovalId != approval.Id.Value ||
                reservedState.ExecutionId != attempt.Value ||
                reservedState.ApprovalConsumedByExecutionId != attempt.Value ||
                reservedState.Version != 1)
                return ExecutionOperationResult.Denied(ExecutionOperationStatus.ReconciliationRequired);
            var context = TrustedExecutionContext.Issue(action, approval, attempt, startedAt);
            var executing = action.BeginExecution(context);
            cancellationToken.ThrowIfCancellationRequested();

            ToolExecutionResult? handlerResult;
            try
            {
                // The sole handler invocation. No dispatcher, retry or alternative handler.
                handlerResult = await handler.ExecuteAsync(new ToolInvocationRequest(action.ToolId), cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { handlerResult = ToolExecutionResult.FromStatus(ToolExecutionStatus.Failed); }
            cancellationToken.ThrowIfCancellationRequested();
            // A handler's fixed Success category is a report, not proof of verification.
            var outcome = handlerResult?.Status == ToolExecutionStatus.Success
                ? ExecutionOutcome.Succeeded : ExecutionOutcome.Failed;
            var completedAt = clock.UtcNow;
            if (completedAt.Kind != DateTimeKind.Utc || completedAt < context.StartedAt)
                return ExecutionOperationResult.Denied(ExecutionOperationStatus.ReconciliationRequired);
            cancellationToken.ThrowIfCancellationRequested();
            var completion = await states.TryCompleteAsync(new(action.Id, scope, action.ToolId,
                approval.Id.Value, attempt, reservedState.Version, outcome), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var completedState = completion?.State;
            if (completion?.Status != ActionExecutionStateResultStatus.Success ||
                completedState is null || !ActionExecutionStatePolicy.IsValid(completedState) ||
                completedState.ActionId != action.Id || completedState.Scope != scope ||
                completedState.ToolId != action.ToolId || completedState.ApprovalId != approval.Id.Value ||
                completedState.ExecutionId != attempt.Value ||
                completedState.ApprovalConsumedByExecutionId != attempt.Value ||
                completedState.Version != reservedState.Version + 1 ||
                completedState.Outcome != outcome ||
                completedState.Status != (outcome == ExecutionOutcome.Succeeded
                    ? ActionExecutionStateStatus.Succeeded : ActionExecutionStateStatus.Failed))
                return ExecutionOperationResult.Denied(ExecutionOperationStatus.ReconciliationRequired);
            var evidence = ExecutionEvidence.Issue(context, outcome, completedAt);
            return ExecutionOperationResult.Complete(executing.CompleteExecution(evidence), evidence);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return ExecutionOperationResult.Denied(reserved || reservationAttempted
            ? ExecutionOperationStatus.ReconciliationRequired : ExecutionOperationStatus.Failed); }
    }

    private static ExecutionOperationStatus MapReservationStatus(ActionExecutionStateResultStatus status) => status switch
    {
        ActionExecutionStateResultStatus.InvalidRequest => ExecutionOperationStatus.InvalidRequest,
        ActionExecutionStateResultStatus.ScopeMismatch => ExecutionOperationStatus.ScopeDenied,
        ActionExecutionStateResultStatus.ApprovalMismatch => ExecutionOperationStatus.ApprovalDenied,
        ActionExecutionStateResultStatus.ToolMismatch => ExecutionOperationStatus.UnknownTool,
        ActionExecutionStateResultStatus.StaleVersion or ActionExecutionStateResultStatus.AlreadyReserved or
            ActionExecutionStateResultStatus.AlreadyCompleted or ActionExecutionStateResultStatus.ExecutionMismatch
            => ExecutionOperationStatus.StateConflict,
        _ => ExecutionOperationStatus.StateUnavailable
    };
}
