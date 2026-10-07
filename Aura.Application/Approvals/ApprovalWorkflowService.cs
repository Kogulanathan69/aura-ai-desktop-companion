using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Tools;

namespace Aura.Application.Approvals;

public sealed class ApprovalWorkflowService(ToolRegistry tools, IActionApprovalScopeValidator scopes,
    IDateTimeProvider clock) : IApprovalWorkflowService
{
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
                return ApprovalOperationResult.Success(action.Transition(ActionLifecycleStatus.Rejected, now), null);
            var evidence = ApprovalEvidence.Issue(action, now);
            return ApprovalOperationResult.Success(action.Approve(evidence), evidence);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return ApprovalOperationResult.Denied(ApprovalOperationStatus.Failed); }
    }
}
