namespace Aura.Application.ProjectContext.DTOs;

public sealed record ProjectContextSessionDto(
    Guid Id,
    DateTime StartedAt,
    string? CurrentTask,
    string Status);
