using Aura.Application.Actions;
using Aura.Application.Tools;

namespace Aura.Application.Executions;

// Constructed from server-known scope and the resolved ToolDescriptor, never chat.
public sealed record ToolPermissionValidationRequest(ActionScope Scope, ToolIdentifier ToolId,
    ToolPermissionRequirement Requirement);

// Bounded source data, not a reusable execution capability. A future adapter must
// attest this exact grant from current non-revoked authorization state.
public sealed record ToolPermissionGrant(Guid GrantId, ActionScope Scope, ToolIdentifier ToolId,
    ToolPermissionRequirement Requirement, bool IsActive, bool IsRevoked);

public enum ToolPermissionLookupStatus { Found, NotFound, Unavailable }
public sealed record ToolPermissionLookupResult(ToolPermissionLookupStatus Status, ToolPermissionGrant? Grant);

public interface IToolPermissionSource
{
    Task<ToolPermissionLookupResult> GetAsync(ToolPermissionValidationRequest request,
        CancellationToken cancellationToken);
}

public sealed class UnavailableToolPermissionSource : IToolPermissionSource
{
    public Task<ToolPermissionLookupResult> GetAsync(ToolPermissionValidationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ToolPermissionLookupResult(ToolPermissionLookupStatus.Unavailable, null));
    }
}

public enum ToolPermissionDecision
{
    Allowed, Denied, Unavailable, Revoked, ScopeMismatch,
    RequirementMismatch, InvalidGrant, Failed
}

public sealed class ToolPermissionValidationResult
{
    public ToolPermissionDecision Decision { get; }
    public string Message { get; }
    private ToolPermissionValidationResult(ToolPermissionDecision decision, string message)
    { Decision = decision; Message = message; }

    internal static ToolPermissionValidationResult Allowed() =>
        new(ToolPermissionDecision.Allowed, "Tool permission validated.");

    public static ToolPermissionValidationResult Denied(ToolPermissionDecision decision) => decision switch
    {
        ToolPermissionDecision.Denied => new(decision, "Tool permission denied."),
        ToolPermissionDecision.Unavailable => new(decision, "Tool permission source unavailable."),
        ToolPermissionDecision.Revoked => new(decision, "Tool permission revoked."),
        ToolPermissionDecision.ScopeMismatch => new(decision, "Tool permission scope mismatch."),
        ToolPermissionDecision.RequirementMismatch => new(decision, "Tool permission requirement mismatch."),
        ToolPermissionDecision.InvalidGrant => new(decision, "Tool permission grant invalid."),
        _ => new(ToolPermissionDecision.Failed, "Tool permission validation failed.")
    };
}

public interface IToolPermissionPolicy
{
    Task<ToolPermissionValidationResult> EvaluateAsync(ToolPermissionValidationRequest request,
        ToolPermissionLookupResult lookup, CancellationToken cancellationToken);
}

public sealed class ExactToolPermissionPolicy : IToolPermissionPolicy
{
    public Task<ToolPermissionValidationResult> EvaluateAsync(ToolPermissionValidationRequest request,
        ToolPermissionLookupResult lookup, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ToolPermissionValidationResult result;
        if (request is null || request.Scope is null || request.Scope.UserId == Guid.Empty ||
            request.Scope.ConversationId == Guid.Empty || request.Scope.ProjectId == Guid.Empty ||
            request.ToolId is null || !Enum.IsDefined(request.Requirement) || lookup is null)
            result = ToolPermissionValidationResult.Denied(ToolPermissionDecision.InvalidGrant);
        else if (lookup.Status == ToolPermissionLookupStatus.Unavailable)
            result = ToolPermissionValidationResult.Denied(ToolPermissionDecision.Unavailable);
        else if (lookup.Status == ToolPermissionLookupStatus.NotFound)
            result = ToolPermissionValidationResult.Denied(ToolPermissionDecision.Denied);
        else if (lookup.Status != ToolPermissionLookupStatus.Found || lookup.Grant is null ||
            lookup.Grant.GrantId == Guid.Empty || lookup.Grant.Scope is null ||
            lookup.Grant.ToolId is null || !Enum.IsDefined(lookup.Grant.Requirement))
            result = ToolPermissionValidationResult.Denied(ToolPermissionDecision.InvalidGrant);
        else if (lookup.Grant.Scope != request.Scope || lookup.Grant.ToolId != request.ToolId)
            result = ToolPermissionValidationResult.Denied(ToolPermissionDecision.ScopeMismatch);
        else if (lookup.Grant.Requirement != request.Requirement)
            result = ToolPermissionValidationResult.Denied(ToolPermissionDecision.RequirementMismatch);
        else if (lookup.Grant.IsRevoked)
            result = ToolPermissionValidationResult.Denied(ToolPermissionDecision.Revoked);
        else if (!lookup.Grant.IsActive)
            result = ToolPermissionValidationResult.Denied(ToolPermissionDecision.Denied);
        else
            result = ToolPermissionValidationResult.Allowed();
        return Task.FromResult(result);
    }
}
