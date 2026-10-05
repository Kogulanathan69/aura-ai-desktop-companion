namespace Aura.Application.Projects.DTOs;

public sealed record ProjectDto(
    Guid Id,
    string Name,
    string? Description,
    string? LocalPath,
    string? RepositoryUrl,
    string? CurrentBranch,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastOpenedAt
);