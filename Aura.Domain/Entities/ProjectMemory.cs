namespace Aura.Domain.Entities;

public class ProjectMemory
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? SessionId { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public float[] Embedding { get; set; } = Array.Empty<float>();

    public short Importance { get; set; }

    public string SourceType { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? LastAccessedAt { get; set; }

    public int AccessCount { get; set; }
}