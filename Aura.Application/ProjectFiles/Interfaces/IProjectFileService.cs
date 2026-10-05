using Aura.Application.ProjectFiles.DTOs;

namespace Aura.Application.ProjectFiles.Interfaces;

public interface IProjectFileService
{
    Task<ProjectFileDto?> RegisterAsync(Guid projectId, RegisterProjectFileRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectFileDto>?> GetAllAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectFileDto?> GetByIdAsync(Guid projectId, Guid fileId, CancellationToken cancellationToken = default);
    Task<ProjectFileDto?> UpdateAsync(Guid projectId, Guid fileId, UpdateProjectFileRequest request, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(Guid projectId, Guid fileId, CancellationToken cancellationToken = default);
    Task<ProjectFilePermissionDto?> GrantAccessAsync(Guid projectId, Guid fileId, CancellationToken cancellationToken = default);
    Task<ProjectFilePermissionDto?> RevokeAccessAsync(Guid projectId, Guid fileId, CancellationToken cancellationToken = default);
    Task<ProjectFilePermissionDto?> GetAccessAsync(Guid projectId, Guid fileId, CancellationToken cancellationToken = default);
}
