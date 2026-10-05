using Aura.Application.ProjectSessions.DTOs;

namespace Aura.Application.ProjectSessions.Interfaces;

public interface IProjectSessionService
{
    Task<ProjectSessionDto?> StartAsync(Guid projectId, StartProjectSessionRequest request, CancellationToken cancellationToken = default);
    Task<ProjectSessionDto?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectSessionDto?> GetByIdAsync(Guid projectId, Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectSessionDto>?> GetHistoryAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectSessionDto?> UpdateAsync(Guid projectId, Guid sessionId, UpdateProjectSessionRequest request, CancellationToken cancellationToken = default);
    Task<ProjectSessionDto?> EndAsync(Guid projectId, Guid sessionId, EndProjectSessionRequest request, CancellationToken cancellationToken = default);
}
