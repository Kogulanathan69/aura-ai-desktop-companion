using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Common.Interfaces;
using Aura.Application.Tools;

namespace Aura.Application.Executions;

public sealed class TrustedToolExecutionService(ToolRegistry tools, IToolExecutionScopeValidator scopes,
    IToolExecutionPermissionValidator permissions, IToolExecutionPolicy policy, IDateTimeProvider clock)
    : ITrustedToolExecutionService
{
    public async Task<ExecutionOperationResult> ExecuteAsync(ActionScope scope, ActionDescriptor action,
        ApprovalEvidence approval, ExecutionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            var context = TrustedExecutionContext.Issue(action, approval, startedAt);
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
                return ExecutionOperationResult.Denied(ExecutionOperationStatus.Failed);
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = ExecutionEvidence.Issue(context, outcome, completedAt);
            return ExecutionOperationResult.Complete(executing.CompleteExecution(evidence), evidence);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return ExecutionOperationResult.Denied(ExecutionOperationStatus.Failed); }
    }
}
