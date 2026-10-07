using Aura.Application.Actions;
using Aura.Application.Tools;

namespace Aura.Application.Approvals;

public static class ApprovalLimits
{
    public const int MaximumResultMessageCharacters = 80;
}

public enum ApprovalDecision { Approve, Reject }
// Explicit trusted application input, never an AI/chat/public endpoint payload.
public sealed record ApprovalRequest(ApprovalDecision Decision);

public sealed class ApprovalIdentifier
{
    public Guid Value { get; }
    private ApprovalIdentifier(Guid value) => Value = value;
    internal static ApprovalIdentifier Create() => new(Guid.NewGuid());
}

// In-process evidence only. No public construction, mutation, deserialization factory or arbitrary text.
public sealed class ApprovalEvidence
{
    public ApprovalIdentifier Id { get; }
    public ActionIdentifier ActionId { get; }
    public ActionScope Scope { get; }
    public ToolIdentifier ToolId { get; }
    public ApprovalDecision Decision => ApprovalDecision.Approve;
    public DateTime IssuedAt { get; }

    private ApprovalEvidence(ApprovalIdentifier id, ActionDescriptor action, DateTime issuedAt)
    { Id = id; ActionId = action.Id; Scope = action.Scope; ToolId = action.ToolId; IssuedAt = issuedAt; }

    internal static ApprovalEvidence Issue(ActionDescriptor action, DateTime issuedAt)
    {
        if (!ApprovalValidation.CanDecide(action.Status) || issuedAt.Kind != DateTimeKind.Utc ||
            issuedAt == default || issuedAt < action.UpdatedAt)
            throw new InvalidOperationException("Invalid approval issuance.");
        return new(ApprovalIdentifier.Create(), action, issuedAt);
    }

    // Binding is NOT fresh ownership, permission, expiry or single-use validation.
    public bool MatchesBinding(ActionIdentifier? actionId, ActionScope? scope, ToolIdentifier? toolId) =>
        actionId is not null && actionId == ActionId && scope is not null && scope == Scope &&
        toolId is not null && toolId == ToolId;
}

public static class ApprovalValidation
{
    public static bool CanDecide(ActionLifecycleStatus status) => status == ActionLifecycleStatus.ApprovalRequired;

    public static bool IsBoundToApprovedAction(ApprovalEvidence? evidence, ActionDescriptor? action,
        ActionScope? scope, ToolIdentifier? toolId) => evidence is not null && action is not null &&
        action.Status == ActionLifecycleStatus.Approved && action.ApprovalId == evidence.Id.Value &&
        action.Scope == scope && action.ToolId == toolId && evidence.MatchesBinding(action.Id, scope, toolId);
}

public enum ApprovalOperationStatus { Success, InvalidRequest, ScopeDenied, InvalidActionState, UnknownTool, DecisionDenied, Failed }

public sealed class ApprovalOperationResult
{
    public ApprovalOperationStatus Status { get; }
    public string Message { get; }
    public ActionDescriptor? Action { get; }
    public ApprovalEvidence? Evidence { get; }
    private ApprovalOperationResult(ApprovalOperationStatus status, string message,
        ActionDescriptor? action = null, ApprovalEvidence? evidence = null)
    { Status = status; Message = message; Action = action; Evidence = evidence; }

    internal static ApprovalOperationResult Success(ActionDescriptor action, ApprovalEvidence? evidence) =>
        new(ApprovalOperationStatus.Success, "Action decision recorded in memory.", action, evidence);
    public static ApprovalOperationResult Denied(ApprovalOperationStatus status) => status switch
    {
        ApprovalOperationStatus.InvalidRequest => new(status, "Invalid approval request."),
        ApprovalOperationStatus.ScopeDenied => new(status, "Approval scope is unavailable."),
        ApprovalOperationStatus.InvalidActionState => new(status, "Action cannot receive an approval decision."),
        ApprovalOperationStatus.UnknownTool => new(status, "Approval tool is unavailable."),
        ApprovalOperationStatus.DecisionDenied => new(status, "Approval decision denied."),
        _ => new(ApprovalOperationStatus.Failed, "Approval operation failed.")
    };
}

public interface IActionApprovalScopeValidator
{
    // Future adapter must authenticate current user and verify owned conversation, exact
    // project association and owned project; it must not trust IDs as authorization.
    // An explicit decision call must come from a trusted human-facing application path,
    // never model output. No production adapter or endpoint is implemented in Step 11P.
    Task<bool> ValidateAsync(ActionScope scope, CancellationToken cancellationToken);
}

public interface IApprovalWorkflowService
{
    Task<ApprovalOperationResult> DecideAsync(ActionScope scope, ActionDescriptor action,
        ApprovalRequest request, CancellationToken cancellationToken = default);
}
