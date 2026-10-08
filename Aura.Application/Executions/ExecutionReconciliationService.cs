using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Auditing;

namespace Aura.Application.Executions;

// Assessment only: never calls a tool, execution coordinator, verification workflow,
// or a durable-state mutation method.
public sealed class ExecutionReconciliationService(IExecutionReconciliationScopeValidator scopes,
    IExecutionReconciliationSource source, IExecutionReconciliationPolicy policy,
    IDateTimeProvider clock, IAuditEventWriter? auditWriter = null) : IExecutionReconciliationService
{
    private readonly IAuditEventWriter audit = auditWriter ?? new UnavailableAuditEventWriter();
    public async Task<ExecutionReconciliationResult> ReconcileAsync(ActionScope scope,
        ExecutionReconciliationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (scope is null || scope.UserId == Guid.Empty || scope.ConversationId == Guid.Empty ||
                scope.ProjectId == Guid.Empty || request?.ActionId is null ||
                request.ActionId.Value == Guid.Empty)
                return ExecutionReconciliationResult.Denied(ExecutionReconciliationStatus.InvalidRequest);
            var owned = await scopes.ValidateAsync(scope, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!owned) return ExecutionReconciliationResult.Denied(ExecutionReconciliationStatus.ScopeDenied);
            var snapshot = await source.GetAsync(scope, request.ActionId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot is null)
                return ExecutionReconciliationResult.Denied(ExecutionReconciliationStatus.StateUnavailable);
            var state = snapshot.State;
            if (!ActionExecutionStatePolicy.IsValid(state) || !Enum.IsDefined(snapshot.Reason) ||
                snapshot.Version < 0)
                return ExecutionReconciliationResult.Denied(ExecutionReconciliationStatus.StateConflict);
            if (snapshot.ActionId != request.ActionId || snapshot.Scope != scope ||
                snapshot.ToolId != state.ToolId || snapshot.ApprovalId != state.ApprovalId ||
                snapshot.Version != state.Version || state.ActionId != request.ActionId ||
                state.Scope != scope)
                return ExecutionReconciliationResult.Denied(ExecutionReconciliationStatus.StateConflict);
            if (state.Status == ActionExecutionStateStatus.Ready)
                return ExecutionReconciliationResult.Denied(snapshot.ExecutionId == Guid.Empty
                    ? ExecutionReconciliationStatus.NotEligible : ExecutionReconciliationStatus.StateConflict);
            if (snapshot.ExecutionId == Guid.Empty || snapshot.ExecutionId != state.ExecutionId)
                return ExecutionReconciliationResult.Denied(ExecutionReconciliationStatus.StateConflict);
            if (state.Status is ActionExecutionStateStatus.Succeeded or ActionExecutionStateStatus.Failed)
                return ExecutionReconciliationResult.Denied(ExecutionReconciliationStatus.AlreadyResolved);
            // Reserved is the only remaining valid status. Policy is advisory; no
            // transition to Failed or Succeeded is issued in this step.
            var disposition = await policy.AssessAsync(snapshot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (disposition == ExecutionReconciliationDisposition.Unavailable)
                return ExecutionReconciliationResult.Denied(ExecutionReconciliationStatus.StateUnavailable);
            if (disposition is not (ExecutionReconciliationDisposition.NeedsManualReview or
                ExecutionReconciliationDisposition.SafeToMarkFailed))
                return ExecutionReconciliationResult.Denied(ExecutionReconciliationStatus.PolicyDenied);
            owned = await scopes.ValidateAsync(scope, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!owned) return ExecutionReconciliationResult.Denied(ExecutionReconciliationStatus.ScopeDenied);
            var issuedAt = clock.UtcNow;
            if (issuedAt.Kind != DateTimeKind.Utc || issuedAt == default)
                return ExecutionReconciliationResult.Denied(ExecutionReconciliationStatus.Failed);
            var evidence = ExecutionReconciliationEvidence.Issue(snapshot, disposition, issuedAt);
            var result = ExecutionReconciliationResult.Success(evidence);
            await AuditObservation.RecordAsync(audit, AuditWriteRequest.Create(
                AuditEventType.ExecutionReconciliationAssessed,
                AuditEventOutcome.Inconclusive, scope,
                new(snapshot.ActionId, snapshot.ToolId, snapshot.ApprovalId,
                    snapshot.ExecutionId, ReconciliationId: evidence.Id.Value)), cancellationToken);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return ExecutionReconciliationResult.Denied(ExecutionReconciliationStatus.Failed); }
    }
}
