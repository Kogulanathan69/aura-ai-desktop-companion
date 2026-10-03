namespace Aura.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public Guid? DeviceId { get; set; }

    public Guid? ProjectId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string Result { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}