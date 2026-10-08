using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Tools;

namespace Aura.Application.Verifications;

public static class VerificationLimits
{
    public const int MaximumResultMessageCharacters = 80;
}

// Internal server-known reference only. Caller cannot supply outcome, proof, verification ID or time.
public sealed record VerificationRequest(Guid ExecutionId);
public enum ExecutionReportOutcome { Succeeded, Failed, Unknown }
public enum VerificationOutcome { Verified, Failed, Inconclusive }

// Bounded adapter DTO, not a capability. Accepted only from the injected trusted source,
// never from a public request. No logs, contents, commands, arbitrary facts or JSON.
public sealed record ExecutionEvidenceInput(Guid ExecutionId, ActionIdentifier ActionId,
    ActionScope Scope, ToolIdentifier ToolId, Guid ApprovalId, ExecutionReportOutcome Outcome,
    DateTime CompletedAt);

public interface IExecutionEvidenceSource
{
    // Future adapter must attest an actual completed authorized execution, exact bindings
    // and current evidence provenance. Return null for unavailable/untrusted execution.
    // Must scope before reading, never execute a tool to manufacture evidence.
    Task<ExecutionEvidenceInput?> GetAsync(ActionScope scope, Guid executionId, CancellationToken cancellationToken);
}

public sealed class UnavailableExecutionEvidenceSource : IExecutionEvidenceSource
{
    public Task<ExecutionEvidenceInput?> GetAsync(ActionScope scope, Guid executionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ExecutionEvidenceInput?>(null);
    }
}

public interface IVerificationScopeValidator
{
    // Future adapter: authenticated current user, owned conversation, exact associated
    // project and owned project. IDs or approval alone are not ownership evidence.
    Task<bool> ValidateAsync(ActionScope scope, CancellationToken cancellationToken);
}

public interface IIndependentVerificationPolicy
{
    // Verified requires independently established tool-specific proof, not Outcome=Succeeded.
    // No production proof adapter exists. Never execute a tool here to generate that proof.
    Task<VerificationOutcome> EvaluateAsync(ExecutionEvidenceInput execution, CancellationToken cancellationToken);
}

public sealed class ConservativeVerificationPolicy : IIndependentVerificationPolicy
{
    public Task<VerificationOutcome> EvaluateAsync(ExecutionEvidenceInput execution, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(execution.Outcome == ExecutionReportOutcome.Failed
            ? VerificationOutcome.Failed : VerificationOutcome.Inconclusive);
    }
}

public sealed class VerificationIdentifier
{
    public Guid Value { get; }
    private VerificationIdentifier(Guid value) => Value = value;
    internal static VerificationIdentifier Create() => new(Guid.NewGuid());
}

public sealed class VerificationEvidence
{
    public VerificationIdentifier Id { get; }
    public ActionIdentifier ActionId { get; }
    public ActionScope Scope { get; }
    public ToolIdentifier ToolId { get; }
    public Guid ApprovalId { get; }
    public Guid ExecutionId { get; }
    public VerificationOutcome Outcome { get; }
    public DateTime IssuedAt { get; }

    private VerificationEvidence(ExecutionEvidenceInput execution, VerificationOutcome outcome, DateTime issuedAt)
    {
        Id = VerificationIdentifier.Create(); ActionId = execution.ActionId; Scope = execution.Scope;
        ToolId = execution.ToolId; ApprovalId = execution.ApprovalId; ExecutionId = execution.ExecutionId;
        Outcome = outcome; IssuedAt = issuedAt;
    }

    internal static VerificationEvidence Issue(ExecutionEvidenceInput execution, VerificationOutcome outcome, DateTime issuedAt)
    {
        if (!Enum.IsDefined(outcome) || issuedAt.Kind != DateTimeKind.Utc || issuedAt == default || issuedAt < execution.CompletedAt)
            throw new InvalidOperationException("Invalid verification issuance.");
        return new(execution, outcome, issuedAt);
    }

    // Exact binding only, not permission, durable state, expiry, proof authenticity or consumption.
    public bool MatchesBinding(ActionIdentifier? actionId, ActionScope? scope, ToolIdentifier? toolId,
        Guid approvalId, Guid executionId) => actionId is not null && ActionId == actionId &&
        scope is not null && Scope == scope && toolId is not null && ToolId == toolId &&
        approvalId != Guid.Empty && ApprovalId == approvalId && executionId != Guid.Empty && ExecutionId == executionId;
}

public static class VerificationContextPolicy
{
    // Step 11Q uses Approved solely as authorization context for independently sourced execution
    // evidence. It does not assert ActionDescriptor has executed or change its lifecycle.
    public static bool IsEligibleContext(ActionLifecycleStatus status) => status == ActionLifecycleStatus.Approved;
}

public enum VerificationOperationStatus { Success, InvalidRequest, ScopeDenied, InvalidActionState,
    ApprovalDenied, UnknownTool, ExecutionUnavailable, EvidenceDenied, Failed }

public sealed class VerificationOperationResult
{
    public VerificationOperationStatus Status { get; }
    public string Message { get; }
    public VerificationEvidence? Evidence { get; }
    private VerificationOperationResult(VerificationOperationStatus status, string message, VerificationEvidence? evidence = null)
    { Status = status; Message = message; Evidence = evidence; }

    internal static VerificationOperationResult Success(VerificationEvidence evidence) =>
        new(VerificationOperationStatus.Success, "Verification assessment completed.", evidence);
    public static VerificationOperationResult Denied(VerificationOperationStatus status) => status switch
    {
        VerificationOperationStatus.InvalidRequest => new(status, "Invalid verification request."),
        VerificationOperationStatus.ScopeDenied => new(status, "Verification scope is unavailable."),
        VerificationOperationStatus.InvalidActionState => new(status, "Action context is ineligible for verification."),
        VerificationOperationStatus.ApprovalDenied => new(status, "Verification approval context denied."),
        VerificationOperationStatus.UnknownTool => new(status, "Verification tool is unavailable."),
        VerificationOperationStatus.ExecutionUnavailable => new(status, "Trusted execution evidence is unavailable."),
        VerificationOperationStatus.EvidenceDenied => new(status, "Verification evidence denied."),
        _ => new(VerificationOperationStatus.Failed, "Verification operation failed.")
    };
}

public interface IVerificationWorkflowService
{
    Task<VerificationOperationResult> VerifyAsync(ActionScope scope, ActionDescriptor action,
        ApprovalEvidence approval, VerificationRequest request, CancellationToken cancellationToken = default);
}
