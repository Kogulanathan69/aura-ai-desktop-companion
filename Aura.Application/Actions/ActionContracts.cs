using Aura.Application.Tools;

namespace Aura.Application.Actions;

public static class ActionLimits
{
    public const int MaximumSummaryCharacters = 160;
    public const int MaximumResultMessageCharacters = 80;
}

public sealed record ActionIdentifier
{
    public Guid Value { get; }
    private ActionIdentifier(Guid value) => Value = value;
    internal static ActionIdentifier Create() => new(Guid.NewGuid());
}

// Server-resolved application scope only; never bind it from a public request.
public sealed record ActionScope(Guid UserId, Guid ConversationId, Guid ProjectId);
public sealed record ActionProposalRequest(ToolIdentifier ToolId, string Summary);
public enum ActionLifecycleStatus { Proposed, ApprovalRequired, Approved, Rejected, Executing, Succeeded, Failed, Cancelled }

public sealed record ActionDescriptor
{
    public ActionIdentifier Id { get; }
    public ActionScope Scope { get; }
    public ToolIdentifier ToolId { get; }
    // Untrusted display data only: never instructions, authorization or provider context.
    public string Summary { get; }
    public ActionLifecycleStatus Status { get; }
    public DateTime CreatedAt { get; }
    public DateTime UpdatedAt { get; }
    public bool RequiresApproval => true;
    public bool IsVerified => false;
    // Linkage slots only. No Step 11O path assigns or trusts these as evidence.
    public Guid? ApprovalId => null;
    public Guid? ToolExecutionId => null;
    public Guid? VerificationId => null;

    internal ActionDescriptor(ActionIdentifier id, ActionScope scope, ToolIdentifier toolId,
        string summary, ActionLifecycleStatus status, DateTime createdAt, DateTime updatedAt)
    {
        Id = id; Scope = scope; ToolId = toolId; Summary = summary;
        Status = status; CreatedAt = createdAt; UpdatedAt = updatedAt;
    }

    internal ActionDescriptor Transition(ActionLifecycleStatus status, DateTime updatedAt) =>
        new(Id, Scope, ToolId, Summary, status, CreatedAt, updatedAt);
}

public enum ActionOperationStatus { Success, InvalidRequest, ScopeDenied, UnknownTool, TransitionDenied, ApprovalRequired, ExecutionDisabled, Failed }

public sealed record ActionOperationResult
{
    public ActionOperationStatus Status { get; }
    public string Message { get; }
    public ActionDescriptor? Action { get; }
    private ActionOperationResult(ActionOperationStatus status, string message, ActionDescriptor? action)
    { Status = status; Message = message; Action = action; }

    internal static ActionOperationResult Success(ActionDescriptor action) => new(ActionOperationStatus.Success, "Action lifecycle updated.", action);
    public static ActionOperationResult Denied(ActionOperationStatus status) => status switch
    {
        ActionOperationStatus.InvalidRequest => new(status, "Invalid action request.", null),
        ActionOperationStatus.ScopeDenied => new(status, "Action scope is unavailable.", null),
        ActionOperationStatus.UnknownTool => new(status, "Action tool is unavailable.", null),
        ActionOperationStatus.TransitionDenied => new(status, "Action transition denied.", null),
        ActionOperationStatus.ApprovalRequired => new(status, "Trusted action approval is required.", null),
        ActionOperationStatus.ExecutionDisabled => new(status, "Action execution is disabled.", null),
        _ => new(ActionOperationStatus.Failed, "Action lifecycle operation failed.", null)
    };
}

public interface IActionScopeValidator
{
    // Future implementation must authenticate the current user, verify owned conversation,
    // its exact project association and owned project, following OwnedConversations rules.
    // Scope values alone are not ownership/permission/approval evidence. No production adapter yet.
    Task<bool> ValidateAsync(ActionScope scope, CancellationToken cancellationToken);
}

public interface IActionLifecycleService
{
    Task<ActionOperationResult> ProposeAsync(ActionScope scope, ActionProposalRequest request,
        CancellationToken cancellationToken = default);
    Task<ActionOperationResult> TransitionAsync(ActionScope scope, ActionDescriptor action,
        ActionLifecycleStatus target, CancellationToken cancellationToken = default);
}
