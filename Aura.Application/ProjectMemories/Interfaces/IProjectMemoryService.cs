using Aura.Application.ProjectMemories.DTOs;

namespace Aura.Application.ProjectMemories.Interfaces;

public interface IProjectMemoryService
{
    Task<IReadOnlyList<ProjectMemoryDto>?> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectMemoryDto?> GetByIdAsync(Guid projectId, Guid memoryId, CancellationToken cancellationToken = default);
    Task<ProjectMemoryDto?> CreateAsync(Guid projectId, CreateProjectMemoryRequest request, CancellationToken cancellationToken = default);
    Task<ProjectMemoryDto?> UpdateAsync(Guid projectId, Guid memoryId, UpdateProjectMemoryRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid projectId, Guid memoryId, CancellationToken cancellationToken = default);
}
