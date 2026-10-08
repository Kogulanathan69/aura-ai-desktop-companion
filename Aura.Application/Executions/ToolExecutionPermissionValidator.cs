using Aura.Application.Actions;
using Aura.Application.Tools;
using Aura.Application.Auditing;

namespace Aura.Application.Executions;

// No cache: every invocation queries the trusted source for current grant state.
// Ownership, approval, execution policy and durable reservation stay separate gates.
public sealed class ToolExecutionPermissionValidator(IToolPermissionSource source,
    IToolPermissionPolicy policy, IAuditEventWriter? auditWriter = null) : IToolExecutionPermissionValidator
{
    private readonly IAuditEventWriter audit = auditWriter ?? new UnavailableAuditEventWriter();
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
            var decision = result is not null && Enum.IsDefined(result.Decision)
                ? result : ToolPermissionValidationResult.Denied(ToolPermissionDecision.Failed);
            var trustedGrantId = lookup?.Status == ToolPermissionLookupStatus.Found &&
                lookup.Grant is { GrantId: var grantId } && grantId != Guid.Empty &&
                lookup.Grant.Scope == scope && lookup.Grant.ToolId == tool.Id &&
                lookup.Grant.Requirement == tool.RequiredPermission ? grantId : (Guid?)null;
            await AuditObservation.RecordAsync(audit, AuditWriteRequest.Create(
                decision.Decision == ToolPermissionDecision.Allowed
                    ? AuditEventType.PermissionAllowed : AuditEventType.PermissionDenied,
                decision.Decision == ToolPermissionDecision.Allowed
                    ? AuditEventOutcome.Success : AuditEventOutcome.Denied,
                scope, new(ToolId: tool.Id, PermissionGrantId: trustedGrantId)), cancellationToken);
            return decision;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return ToolPermissionValidationResult.Denied(ToolPermissionDecision.Failed); }
    }

    public async Task<bool> HasPermissionAsync(ActionScope scope, ToolDescriptor tool,
        CancellationToken cancellationToken) =>
        (await ValidateAsync(scope, tool, cancellationToken)).Decision == ToolPermissionDecision.Allowed;
}
