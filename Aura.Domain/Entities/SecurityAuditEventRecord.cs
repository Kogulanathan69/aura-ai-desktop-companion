namespace Aura.Domain.Entities;

// Numeric event/outcome values correspond to the bounded Application audit enums.
public sealed class SecurityAuditEventRecord
{
    public Guid Id { get; set; }
    public int EventType { get; set; }
    public int Outcome { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? ActionId { get; set; }
    public string? ToolId { get; set; }
    public Guid? ApprovalId { get; set; }
    public Guid? ExecutionId { get; set; }
    public Guid? VerificationId { get; set; }
    public Guid? ReconciliationId { get; set; }
    public Guid? PermissionGrantId { get; set; }
}
