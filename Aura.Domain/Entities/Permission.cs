namespace Aura.Domain.Entities;

public class Permission
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? DeviceId { get; set; }

    public Guid? ProjectId { get; set; }

    public string ResourceType { get; set; } = string.Empty;

    public string ResourceIdentifier { get; set; } = string.Empty;

    public string AccessLevel { get; set; } = string.Empty;

    public string Status { get; set; } = "Denied";

    public DateTime? GrantedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
