namespace Aura.Domain.Entities;

public class ProjectError
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? SessionId { get; set; }

    public Guid? FileId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? ErrorType { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;

    public string? Analysis { get; set; }

    public string Status { get; set; } = "Open";

    public DateTime FirstSeenAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}