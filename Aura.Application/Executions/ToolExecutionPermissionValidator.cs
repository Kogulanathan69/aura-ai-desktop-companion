using Aura.Application.Actions;
using Aura.Application.Tools;

namespace Aura.Application.Executions;

// No cache: every invocation queries the trusted source for current grant state.
// Ownership, approval, execution policy and durable reservation stay separate gates.
public sealed class ToolExecutionPermissionValidator(IToolPermissionSource source,
    IToolPermissionPolicy policy) : IToolExecutionPermissionValidator
{
    public async Task<ToolPermissionValidationResult> ValidateAsync(ActionScope scope,
        ToolDescriptor tool, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (scope is null || scope.UserId == Guid.Empty || scope.ConversationId == Guid.Empty ||
                scope.ProjectId == Guid.Empty || tool is null || tool.Id is null ||
                !Enum.IsDefined(tool.RequiredPermission) || source is null || policy is null)
                return ToolPermissionValidationResult.Denied(ToolPermissionDecision.InvalidGrant);
            var request = new ToolPermissionValidationRequest(scope, tool.Id, tool.RequiredPermission);
            var lookup = await source.GetAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var result = await policy.EvaluateAsync(request, lookup, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result is not null && Enum.IsDefined(result.Decision)
                ? result : ToolPermissionValidationResult.Denied(ToolPermissionDecision.Failed);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return ToolPermissionValidationResult.Denied(ToolPermissionDecision.Failed); }
    }

    public async Task<bool> HasPermissionAsync(ActionScope scope, ToolDescriptor tool,
        CancellationToken cancellationToken) =>
        (await ValidateAsync(scope, tool, cancellationToken)).Decision == ToolPermissionDecision.Allowed;
}
