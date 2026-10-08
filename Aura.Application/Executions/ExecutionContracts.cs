using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Tools;
using Aura.Application.Verifications;

namespace Aura.Application.Executions;

public static class ExecutionLimits
{
    public const int MaximumResultMessageCharacters = 80;
}

// Internal application request: no arguments, paths, commands, URLs or authorization flags.
public sealed record ExecutionRequest(ActionIdentifier ActionId);
public enum ExecutionOutcome { Succeeded, Failed }

public sealed class ExecutionIdentifier
{
    public Guid Value { get; }
    private ExecutionIdentifier(Guid value) => Value = value;
    internal static ExecutionIdentifier FromAttempt(ExecutionAttemptIdentifier attempt) =>
        attempt is not null && attempt.Value != Guid.Empty
            ? new(attempt.Value) : throw new InvalidOperationException("Invalid execution attempt.");
}

// Minted only after the coordinator validates scope, approval, permission and policy.
// Internal to Application; not a durable grant or an execution endpoint credential.
internal sealed class TrustedExecutionContext
{
    internal ActionIdentifier ActionId { get; }
    internal ActionScope Scope { get; }
    internal ToolIdentifier ToolId { get; }
    internal Guid ApprovalId { get; }
    internal ExecutionIdentifier ExecutionId { get; }
    internal DateTime StartedAt { get; }

    private TrustedExecutionContext(ActionDescriptor action, ApprovalEvidence approval,
        ExecutionAttemptIdentifier attempt, DateTime startedAt)
    {
        ActionId = action.Id; Scope = action.Scope; ToolId = action.ToolId;
        ApprovalId = approval.Id.Value; ExecutionId = ExecutionIdentifier.FromAttempt(attempt); StartedAt = startedAt;
    }

    internal static TrustedExecutionContext Issue(ActionDescriptor action, ApprovalEvidence approval,
        ExecutionAttemptIdentifier attempt, DateTime startedAt)
    {
        if (!ApprovalValidation.IsBoundToApprovedAction(approval, action, action.Scope, action.ToolId) ||
            attempt is null || attempt.Value == Guid.Empty || startedAt.Kind != DateTimeKind.Utc ||
            startedAt == default || startedAt < action.UpdatedAt)
            throw new InvalidOperationException("Invalid execution context.");
        return new(action, approval, attempt, startedAt);
    }

    internal bool Matches(ActionDescriptor action) => action.Id == ActionId && action.Scope == Scope &&
        action.ToolId == ToolId && action.ApprovalId == ApprovalId;
}

public sealed class ExecutionEvidence
{
    public ExecutionIdentifier Id { get; }
    public ActionIdentifier ActionId { get; }
    public ActionScope Scope { get; }
    public ToolIdentifier ToolId { get; }
    public Guid ApprovalId { get; }
    public ExecutionOutcome Outcome { get; }
    public DateTime StartedAt { get; }
    public DateTime CompletedAt { get; }

    private ExecutionEvidence(TrustedExecutionContext context, ExecutionOutcome outcome, DateTime completedAt)
    {
        Id = context.ExecutionId; ActionId = context.ActionId; Scope = context.Scope;
        ToolId = context.ToolId; ApprovalId = context.ApprovalId;
        Outcome = outcome; StartedAt = context.StartedAt; CompletedAt = completedAt;
    }

    internal static ExecutionEvidence Issue(TrustedExecutionContext context, ExecutionOutcome outcome, DateTime completedAt)
    {
        if (!Enum.IsDefined(outcome) || completedAt.Kind != DateTimeKind.Utc || completedAt < context.StartedAt)
            throw new InvalidOperationException("Invalid execution evidence.");
        return new(context, outcome, completedAt);
    }

    public bool MatchesBinding(ActionIdentifier? actionId, ActionScope? scope, ToolIdentifier? toolId, Guid approvalId) =>
        actionId is not null && actionId == ActionId && scope is not null && scope == Scope &&
        toolId is not null && toolId == ToolId && approvalId != Guid.Empty && approvalId == ApprovalId;

    // Structural handoff only. The Step 11Q trusted source must independently attest
    // provenance/current state; this DTO is not proof of verification or persistence.
    public ExecutionEvidenceInput ToVerificationInput() => new(Id.Value, ActionId, Scope, ToolId, ApprovalId,
        Outcome == ExecutionOutcome.Succeeded ? ExecutionReportOutcome.Succeeded : ExecutionReportOutcome.Failed,
        CompletedAt);
}

public interface IToolExecutionScopeValidator
{
    // Future adapter must authenticate current user and verify owned conversation,
    // exact project association and owned project. Never trust scope IDs alone.
    Task<bool> ValidateAsync(ActionScope scope, CancellationToken cancellationToken);
}

public interface IToolExecutionPermissionValidator
{
    // Future adapter must validate required permission for the exact user/project/
    // conversation/tool, including non-revoked grant. Metadata itself grants nothing.
    Task<bool> HasPermissionAsync(ActionScope scope, ToolDescriptor tool, CancellationToken cancellationToken);
}

public sealed class DenyToolExecutionPermissionValidator : IToolExecutionPermissionValidator
{
    public Task<bool> HasPermissionAsync(ActionScope scope, ToolDescriptor tool, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}

public interface IToolExecutionPolicy
{
    // Independent feature enablement for one exact registered tool; never approval or permission.
    Task<bool> AllowsAsync(ToolDescriptor tool, CancellationToken cancellationToken);
}

public sealed class DenyToolExecutionPolicy : IToolExecutionPolicy
{
    public Task<bool> AllowsAsync(ToolDescriptor tool, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}

public enum ExecutionOperationStatus { Success, InvalidRequest, ScopeDenied, InvalidActionState,
    ApprovalDenied, UnknownTool, PermissionDenied, PolicyDenied, StateConflict,
    StateUnavailable, ReconciliationRequired, ExecutionFailed, Failed }

public sealed class ExecutionOperationResult
{
    public ExecutionOperationStatus Status { get; }
    public string Message { get; }
    public ActionDescriptor? Action { get; }
    public ExecutionEvidence? Evidence { get; }
    private ExecutionOperationResult(ExecutionOperationStatus status, string message,
        ActionDescriptor? action = null, ExecutionEvidence? evidence = null)
    { Status = status; Message = message; Action = action; Evidence = evidence; }

    internal static ExecutionOperationResult Complete(ActionDescriptor action, ExecutionEvidence evidence) =>
        new(evidence.Outcome == ExecutionOutcome.Succeeded ? ExecutionOperationStatus.Success : ExecutionOperationStatus.ExecutionFailed,
            evidence.Outcome == ExecutionOutcome.Succeeded ? "Tool call completed." : "Tool call failed.", action, evidence);

    public static ExecutionOperationResult Denied(ExecutionOperationStatus status) => status switch
    {
        ExecutionOperationStatus.InvalidRequest => new(status, "Invalid execution request."),
        ExecutionOperationStatus.ScopeDenied => new(status, "Execution scope is unavailable."),
        ExecutionOperationStatus.InvalidActionState => new(status, "Action is ineligible for execution."),
        ExecutionOperationStatus.ApprovalDenied => new(status, "Trusted action approval is required."),
        ExecutionOperationStatus.UnknownTool => new(status, "Execution tool is unavailable."),
        ExecutionOperationStatus.PermissionDenied => new(status, "Tool execution permission denied."),
        ExecutionOperationStatus.PolicyDenied => new(status, "Tool execution policy denied."),
        ExecutionOperationStatus.StateConflict => new(status, "Execution state conflicts with this request."),
        ExecutionOperationStatus.StateUnavailable => new(status, "Execution state is unavailable."),
        ExecutionOperationStatus.ReconciliationRequired => new(status, "Execution completion requires reconciliation."),
        _ => new(ExecutionOperationStatus.Failed, "Execution operation failed.")
    };
}

public interface ITrustedToolExecutionService
{
    Task<ExecutionOperationResult> ExecuteAsync(ActionScope scope, ActionDescriptor action,
        ApprovalEvidence approval, ExecutionRequest request, CancellationToken cancellationToken = default);
}
