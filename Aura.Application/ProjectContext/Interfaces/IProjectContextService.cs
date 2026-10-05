using Aura.Application.ProjectContext.DTOs;

namespace Aura.Application.ProjectContext.Interfaces;

public interface IProjectContextService
{
    Task<ProjectContextDto?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);
}
