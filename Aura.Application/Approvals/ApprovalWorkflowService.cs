using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Tools;
using Aura.Application.Auditing;

namespace Aura.Application.Approvals;

public sealed class ApprovalWorkflowService(ToolRegistry tools, IActionApprovalScopeValidator scopes,
    IDateTimeProvider clock, IAuditEventWriter? auditWriter = null) : IApprovalWorkflowService
{
    private readonly IAuditEventWriter audit = auditWriter ?? new UnavailableAuditEventWriter();
    public async Task<ApprovalOperationResult> DecideAsync(ActionScope scope, ActionDescriptor action,
        ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (scope is null || scope.UserId == Guid.Empty || scope.ConversationId == Guid.Empty ||
                scope.ProjectId == Guid.Empty || action is null || request is null || !Enum.IsDefined(request.Decision))
                return ApprovalOperationResult.Denied(ApprovalOperationStatus.InvalidRequest);
            if (action.Scope != scope) return ApprovalOperationResult.Denied(ApprovalOperationStatus.ScopeDenied);
            if (!ApprovalValidation.CanDecide(action.Status))
                return ApprovalOperationResult.Denied(ApprovalOperationStatus.InvalidActionState);
            var owned = await scopes.ValidateAsync(scope, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!owned) return ApprovalOperationResult.Denied(ApprovalOperationStatus.ScopeDenied);
            // Resolve metadata only; handler reference is discarded, never executed.
            if (!tools.TryResolve(action.ToolId, out var tool, out _))
                return ApprovalOperationResult.Denied(ApprovalOperationStatus.UnknownTool);
            if (tool is null || tool.Id != action.ToolId || tool.Enabled)
                return ApprovalOperationResult.Denied(ApprovalOperationStatus.DecisionDenied);
            // Revalidate immediately before producing a decision/evidence. Not a durable atomic guarantee.
            owned = await scopes.ValidateAsync(scope, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!owned) return ApprovalOperationResult.Denied(ApprovalOperationStatus.ScopeDenied);
            var now = clock.UtcNow;
            if (now.Kind != DateTimeKind.Utc || now == default || now < action.UpdatedAt)
                return ApprovalOperationResult.Denied(ApprovalOperationStatus.Failed);
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Decision == ApprovalDecision.Reject)
            {
                var rejected = action.Transition(ActionLifecycleStatus.Rejected, now);
                var result = ApprovalOperationResult.Success(rejected, null);
                await AuditObservation.RecordAsync(audit, AuditWriteRequest.Create(
                    AuditEventType.ApprovalRejected, AuditEventOutcome.Denied, scope,
                    new(action.Id, action.ToolId)), cancellationToken);
                return result;
            }
            var evidence = ApprovalEvidence.Issue(action, now);
            var approved = action.Approve(evidence);
            var approvedResult = ApprovalOperationResult.Success(approved, evidence);
            await AuditObservation.RecordAsync(audit, AuditWriteRequest.Create(
                AuditEventType.ApprovalGranted, AuditEventOutcome.Success, scope,
                new(action.Id, action.ToolId, ApprovalId: evidence.Id.Value)), cancellationToken);
            return approvedResult;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return ApprovalOperationResult.Denied(ApprovalOperationStatus.Failed); }
    }
}
