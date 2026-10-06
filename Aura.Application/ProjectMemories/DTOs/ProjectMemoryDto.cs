namespace Aura.Application.ProjectMemories.DTOs;

public sealed record ProjectMemoryDto(Guid Id, Guid ProjectId, Guid? SessionId, string Type,
    string Title, string Content, short Importance, string SourceType, DateTime CreatedAt,
    DateTime UpdatedAt, DateTime? LastAccessedAt, int AccessCount);
