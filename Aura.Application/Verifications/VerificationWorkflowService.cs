using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Common.Interfaces;
using Aura.Application.Tools;

namespace Aura.Application.Verifications;

public sealed class VerificationWorkflowService(ToolRegistry tools, IVerificationScopeValidator scopes,
    IExecutionEvidenceSource executions, IIndependentVerificationPolicy policy, IDateTimeProvider clock)
    : IVerificationWorkflowService
{
    public async Task<VerificationOperationResult> VerifyAsync(ActionScope scope, ActionDescriptor action,
        ApprovalEvidence approval, VerificationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (scope is null || scope.UserId == Guid.Empty || scope.ConversationId == Guid.Empty || scope.ProjectId == Guid.Empty ||
                action is null || request is null || request.ExecutionId == Guid.Empty)
                return VerificationOperationResult.Denied(VerificationOperationStatus.InvalidRequest);
            if (action.Scope != scope) return VerificationOperationResult.Denied(VerificationOperationStatus.ScopeDenied);
            if (!VerificationContextPolicy.IsEligibleContext(action.Status))
                return VerificationOperationResult.Denied(VerificationOperationStatus.InvalidActionState);
            if (!ApprovalValidation.IsBoundToApprovedAction(approval, action, scope, action.ToolId))
                return VerificationOperationResult.Denied(VerificationOperationStatus.ApprovalDenied);
            var owned = await scopes.ValidateAsync(scope, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!owned) return VerificationOperationResult.Denied(VerificationOperationStatus.ScopeDenied);
            if (!tools.TryResolve(action.ToolId, out var tool, out _) || tool is null)
                return VerificationOperationResult.Denied(VerificationOperationStatus.UnknownTool);
            if (tool.Id != action.ToolId || tool.Enabled)
                return VerificationOperationResult.Denied(VerificationOperationStatus.EvidenceDenied);
            var execution = await executions.GetAsync(scope, request.ExecutionId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (execution is null) return VerificationOperationResult.Denied(VerificationOperationStatus.ExecutionUnavailable);
            if (execution.ExecutionId != request.ExecutionId || execution.ActionId != action.Id || execution.Scope != scope ||
                execution.ToolId != action.ToolId || execution.ApprovalId != approval.Id.Value || !Enum.IsDefined(execution.Outcome) ||
                execution.CompletedAt.Kind != DateTimeKind.Utc || execution.CompletedAt == default || execution.CompletedAt < approval.IssuedAt)
                return VerificationOperationResult.Denied(VerificationOperationStatus.EvidenceDenied);
            var outcome = await policy.EvaluateAsync(execution, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Failure/unknown reports cannot be promoted to Verified even by a faulty proof policy.
            if (!Enum.IsDefined(outcome) || (outcome == VerificationOutcome.Verified && execution.Outcome != ExecutionReportOutcome.Succeeded))
                return VerificationOperationResult.Denied(VerificationOperationStatus.EvidenceDenied);
            owned = await scopes.ValidateAsync(scope, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!owned) return VerificationOperationResult.Denied(VerificationOperationStatus.ScopeDenied);
            var now = clock.UtcNow;
            if (now.Kind != DateTimeKind.Utc || now == default || now < execution.CompletedAt || now < action.UpdatedAt)
                return VerificationOperationResult.Denied(VerificationOperationStatus.Failed);
            cancellationToken.ThrowIfCancellationRequested();
            return VerificationOperationResult.Success(VerificationEvidence.Issue(execution, outcome, now));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return VerificationOperationResult.Denied(VerificationOperationStatus.Failed); }
    }
}
