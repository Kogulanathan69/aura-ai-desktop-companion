namespace Aura.Application.ProjectSessions.DTOs;

public sealed record ProjectSessionDto(
    Guid Id,
    Guid ProjectId,
    DateTime StartedAt,
    DateTime? EndedAt,
    string? Summary,
    string? CurrentTask,
    string Status,
    DateTime CreatedAt);
