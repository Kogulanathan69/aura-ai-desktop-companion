namespace Aura.Domain.Entities;

public class ProjectFile
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }

    public string RelativePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;

    public string? ContentHash { get; set; }

    public bool IsIndexed { get; set; }
    public bool IsSensitive { get; set; }

    public DateTime? LastIndexedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}