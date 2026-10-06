using Aura.Application.Common.Exceptions;
using Aura.Application.Common.Interfaces;
using Aura.Application.ProjectMemories.DTOs;
using Aura.Application.ProjectMemories.Interfaces;
using Aura.Application.ProjectMemories.Validation;
using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aura.Application.ProjectMemories.Services;

public sealed class ProjectMemoryService(IAuraDbContext dbContext, IUserIdentityService identity,
    IDateTimeProvider clock) : IProjectMemoryService
{
    public async Task<IReadOnlyList<ProjectMemoryDto>?> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;
        return await ScopedMemories(projectId).AsNoTracking().OrderByDescending(x => x.Importance)
            .ThenByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
            .Select(x => new ProjectMemoryDto(x.Id, x.ProjectId, x.SessionId, x.Type, x.Title, x.Content,
                x.Importance, x.SourceType, x.CreatedAt, x.UpdatedAt, x.LastAccessedAt, x.AccessCount))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectMemoryDto?> GetByIdAsync(Guid projectId, Guid memoryId, CancellationToken cancellationToken = default)
    {
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;
        // Project only safe fields; do not load the stored embedding on read.
        return await ScopedMemories(projectId).AsNoTracking().Where(x => x.Id == memoryId)
            .Select(x => new ProjectMemoryDto(x.Id, x.ProjectId, x.SessionId, x.Type, x.Title, x.Content,
                x.Importance, x.SourceType, x.CreatedAt, x.UpdatedAt, x.LastAccessedAt, x.AccessCount))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ProjectMemoryDto?> CreateAsync(Guid projectId, CreateProjectMemoryRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;
        var values = ProjectMemoryValidation.Normalize(request.Type, request.Title, request.Content, request.Importance);
        await ValidateSessionAsync(projectId, userId, request.SessionId, cancellationToken);
        var now = clock.UtcNow;
        var memory = new ProjectMemory
        {
            Id = Guid.NewGuid(), ProjectId = projectId, SessionId = request.SessionId,
            Type = values.Type, Title = values.Title, Content = values.Content,
            Importance = request.Importance, SourceType = "Manual", CreatedAt = now, UpdatedAt = now,
            // Existing required vector(768) storage uses this approved sentinel for no embedding.
            // It is not an AI embedding and must not be treated as searchable semantic data.
            Embedding = new float[768],
            AccessCount = 0, LastAccessedAt = null
        };
        dbContext.ProjectMemories.Add(memory);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(memory);
    }

    public async Task<ProjectMemoryDto?> UpdateAsync(Guid projectId, Guid memoryId, UpdateProjectMemoryRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;
        var memory = await ScopedMemories(projectId).FirstOrDefaultAsync(x => x.Id == memoryId, cancellationToken);
        if (memory is null) return null;
        var values = ProjectMemoryValidation.Normalize(request.Type, request.Title, request.Content, request.Importance);
        await ValidateSessionAsync(projectId, userId, request.SessionId, cancellationToken);
        memory.Type = values.Type;
        memory.Title = values.Title;
        memory.Content = values.Content;
        memory.Importance = request.Importance;
        memory.SessionId = request.SessionId;
        memory.UpdatedAt = clock.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(memory);
    }

    public async Task<bool> DeleteAsync(Guid projectId, Guid memoryId, CancellationToken cancellationToken = default)
    {
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return false;
        var memory = await ScopedMemories(projectId).FirstOrDefaultAsync(x => x.Id == memoryId, cancellationToken);
        if (memory is null) return false;
        dbContext.ProjectMemories.Remove(memory);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Task<bool> OwnsProjectAsync(Guid projectId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.Projects.AsNoTracking().AnyAsync(x => x.Id == projectId && x.UserId == userId, cancellationToken);

    private IQueryable<ProjectMemory> ScopedMemories(Guid projectId) => dbContext.ProjectMemories.Where(x => x.ProjectId == projectId);

    private async Task ValidateSessionAsync(Guid projectId, Guid userId, Guid? sessionId, CancellationToken cancellationToken)
    {
        if (sessionId.HasValue && !await dbContext.ProjectSessions.AsNoTracking().AnyAsync(x =>
            x.Id == sessionId.Value && x.ProjectId == projectId && x.UserId == userId, cancellationToken))
            throw new AppValidationException("The selected session is unavailable for this project.");
    }

    private static ProjectMemoryDto ToDto(ProjectMemory x) => new(x.Id, x.ProjectId, x.SessionId,
        x.Type, x.Title, x.Content, x.Importance, x.SourceType, x.CreatedAt, x.UpdatedAt, x.LastAccessedAt, x.AccessCount);
}
