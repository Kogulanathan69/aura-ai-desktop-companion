namespace Aura.Application.Projects.DTOs;

public sealed record UpdateProjectRequest(
    string Name,
    string? Description,
    string? LocalPath,
    string? RepositoryUrl,
    string? CurrentBranch,
    string Status
);