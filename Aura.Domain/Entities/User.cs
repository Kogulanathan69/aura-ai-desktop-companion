namespace Aura.Domain.Entities;

public class User
{
    public Guid Id { get; set; }

    public Guid AuthUserId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? LastActiveAt { get; set; }

    public bool IsActive { get; set; }
}