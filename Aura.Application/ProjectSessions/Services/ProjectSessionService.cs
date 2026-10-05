using Aura.Application.Common.Exceptions;
using Aura.Application.Common.Interfaces;
using Aura.Application.ProjectSessions.DTOs;
using Aura.Application.ProjectSessions.Interfaces;
using Aura.Application.ProjectSessions.Validation;
using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aura.Application.ProjectSessions.Services;

public sealed class ProjectSessionService(
    IAuraDbContext dbContext,
    IUserIdentityService userIdentityService,
    IDateTimeProvider dateTimeProvider) : IProjectSessionService
{
    private const string Active = "Active";
    private const string Completed = "Completed";

    public async Task<ProjectSessionDto?> StartAsync(Guid projectId, StartProjectSessionRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;

        if (await ScopedSessions(projectId, userId)
            .AnyAsync(x => x.Status == Active && x.EndedAt == null, cancellationToken))
        {
            throw new AppValidationException("This project already has an active session.");
        }

        var now = dateTimeProvider.UtcNow;
        var session = new ProjectSession
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            UserId = userId,
            StartedAt = now,
            CreatedAt = now,
            CurrentTask = ProjectSessionValidation.NormalizeText(request.CurrentTask),
            Status = Active
        };

        dbContext.ProjectSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(session);
    }

    public async Task<ProjectSessionDto?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;

        var session = await ScopedSessions(projectId, userId)
            .AsNoTracking()
            .Where(x => x.Status == Active && x.EndedAt == null)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return session is null ? null : ToDto(session);
    }

    public async Task<ProjectSessionDto?> GetByIdAsync(Guid projectId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;

        var session = await ScopedSessions(projectId, userId)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        return session is null ? null : ToDto(session);
    }

    public async Task<IReadOnlyList<ProjectSessionDto>?> GetHistoryAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;

        return await ScopedSessions(projectId, userId)
            .AsNoTracking()
            .OrderByDescending(x => x.StartedAt)
            .Select(x => new ProjectSessionDto(x.Id, x.ProjectId, x.StartedAt, x.EndedAt,
                x.Summary, x.CurrentTask, x.Status, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectSessionDto?> UpdateAsync(Guid projectId, Guid sessionId, UpdateProjectSessionRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;

        var session = await ScopedSessions(projectId, userId)
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (session is null) return null;
        EnsureActive(session);

        session.CurrentTask = ProjectSessionValidation.NormalizeText(request.CurrentTask);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(session);
    }

    public async Task<ProjectSessionDto?> EndAsync(Guid projectId, Guid sessionId, EndProjectSessionRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;

        var session = await ScopedSessions(projectId, userId)
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (session is null) return null;
        EnsureActive(session);

        session.Summary = ProjectSessionValidation.NormalizeText(request.Summary);
        session.EndedAt = dateTimeProvider.UtcNow;
        session.Status = Completed;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(session);
    }

    private async Task<bool> OwnsProjectAsync(Guid projectId, Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Projects.AsNoTracking()
            .AnyAsync(x => x.Id == projectId && x.UserId == userId, cancellationToken);

    private IQueryable<ProjectSession> ScopedSessions(Guid projectId, Guid userId) =>
        dbContext.ProjectSessions.Where(x => x.ProjectId == projectId && x.UserId == userId);

    private static void EnsureActive(ProjectSession session)
    {
        if (session.Status != Active || session.EndedAt is not null)
            throw new AppValidationException("Only an active session can be changed.");
    }

    private static ProjectSessionDto ToDto(ProjectSession session) =>
        new(session.Id, session.ProjectId, session.StartedAt, session.EndedAt,
            session.Summary, session.CurrentTask, session.Status, session.CreatedAt);
}
