namespace Aura.Domain.Entities;

public class Project
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? LocalPath { get; set; }

    public string? RepositoryUrl { get; set; }

    public string? CurrentBranch { get; set; }

    public string Status { get; set; } = "Active";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? LastOpenedAt { get; set; }
}