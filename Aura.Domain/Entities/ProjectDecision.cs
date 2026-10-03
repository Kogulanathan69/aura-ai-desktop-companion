namespace Aura.Domain.Entities;

public class ProjectDecision
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? SessionId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Decision { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string? Alternatives { get; set; }

    public string Status { get; set; } = "Active";

    public short Importance { get; set; }

    public DateTime DecidedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}