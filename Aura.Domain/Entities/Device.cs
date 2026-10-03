namespace Aura.Domain.Entities;

public class Device
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DeviceType { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string DeviceIdentifier { get; set; } = string.Empty;

    public bool IsTrusted { get; set; }

    public bool CanExecuteTools { get; set; }

    public string Status { get; set; } = "Active";

    public DateTime RegisteredAt { get; set; }

    public DateTime? LastSeenAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}