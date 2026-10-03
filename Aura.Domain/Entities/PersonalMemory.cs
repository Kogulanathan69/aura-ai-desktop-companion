namespace Aura.Domain.Entities;

public class PersonalMemory
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public float[]? Embedding { get; set; }

    public short Importance { get; set; }

    public string SourceType { get; set; } = "ExplicitUser";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? LastAccessedAt { get; set; }

    public int AccessCount { get; set; }
}