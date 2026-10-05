using Aura.Application.Projects.DTOs;

namespace Aura.Application.Projects.Interfaces;

public interface IProjectService
{
    Task<IReadOnlyList<ProjectDto>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<ProjectDto?> GetByIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<ProjectDto> CreateAsync(
        CreateProjectRequest request,
        CancellationToken cancellationToken = default);

    Task<ProjectDto?> UpdateAsync(
        Guid projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}