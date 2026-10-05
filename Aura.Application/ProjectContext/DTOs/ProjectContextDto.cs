namespace Aura.Application.ProjectContext.DTOs;

public sealed record ProjectContextDto(
    Guid Id,
    string Name,
    string? Description,
    string? RepositoryUrl,
    string? CurrentBranch,
    string Status,
    ProjectContextSessionDto? CurrentSession,
    IReadOnlyList<ProjectContextFileDto> Files);
