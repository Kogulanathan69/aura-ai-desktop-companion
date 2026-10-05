namespace Aura.Application.Projects.DTOs;

public sealed record CreateProjectRequest(
    string Name,
    string? Description,
    string? LocalPath,
    string? RepositoryUrl,
    string? CurrentBranch
);