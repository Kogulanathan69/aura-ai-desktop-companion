namespace Aura.Domain.Entities;

// Durable execution coordination, separate from historical ToolExecution records.
public sealed class ActionExecutionStateRecord
{
    public Guid ActionId { get; set; }
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid ProjectId { get; set; }
    public string ToolId { get; set; } = string.Empty;
    public Guid ApprovalId { get; set; }
    public Guid? ExecutionId { get; set; }
    public DurableExecutionStatus Status { get; set; }
    public long Version { get; set; }
    public bool ApprovalConsumed { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReservedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum DurableExecutionStatus { Ready = 0, Reserved = 1, Succeeded = 2, Failed = 3 }
