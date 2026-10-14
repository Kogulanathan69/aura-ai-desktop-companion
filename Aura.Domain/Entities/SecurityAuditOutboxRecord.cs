namespace Aura.Domain.Entities;

// Pending bounded audit intent written inside the same transaction
// as a successful durable execution-state transition.
//
// This is NOT proof of audit delivery.
// A future dispatcher may deliver this record to SecurityAuditEvents.
public sealed class SecurityAuditOutboxRecord
{
    public Guid Id { get; set; }

    // Stable eventual audit event identity.
    // This must remain unchanged across any future delivery attempt.
    public Guid AuditEventId { get; set; }

    // Numeric values correspond to Aura.Application.Auditing enums.
    public int EventType { get; set; }

    public int Outcome { get; set; }

    public DateTime OccurredAt { get; set; }

    public Guid UserId { get; set; }

    public Guid ConversationId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ActionId { get; set; }

    public string ToolId { get; set; } = string.Empty;

    public Guid ApprovalId { get; set; }

    public Guid ExecutionId { get; set; }

    // Step 13G foundation supports Pending only.
    // No dispatcher or retry worker is activated yet.
    public SecurityAuditOutboxDeliveryState DeliveryState { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? DeliveredAt { get; set; }
}

public enum SecurityAuditOutboxDeliveryState
{
    Pending = 0,
    Delivered = 1,
    Failed = 2
}