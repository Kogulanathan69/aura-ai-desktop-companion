namespace Aura.Domain.Entities;

// Advisory historical assessment. It never authorizes an execution-state transition.
public sealed class ExecutionReconciliationAssessmentRecord
{
    public Guid Id { get; set; }
    public Guid ActionId { get; set; }
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid ProjectId { get; set; }
    public string ToolId { get; set; } = string.Empty;
    public Guid ApprovalId { get; set; }
    public Guid ExecutionId { get; set; }
    public DurableExecutionStatus ObservedStatus { get; set; }
    public long ObservedVersion { get; set; }
    public int Reason { get; set; }
    public int Disposition { get; set; }
    public DateTime AssessedAt { get; set; }
}
