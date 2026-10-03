namespace Aura.Domain.Entities;

public class Conversation
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Type { get; set; } = "General";

    public string? Summary { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? LastMessageAt { get; set; }
}