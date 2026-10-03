namespace Aura.Domain.Entities;

public class ProjectSession
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid UserId { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public string? Summary { get; set; }

    public string? CurrentTask { get; set; }

    public string Status { get; set; } = "Active";

    public DateTime CreatedAt { get; set; }
}