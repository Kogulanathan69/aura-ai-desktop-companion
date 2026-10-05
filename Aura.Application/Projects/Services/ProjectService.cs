using Aura.Application.Common.Interfaces;
using Aura.Application.Projects.DTOs;
using Aura.Application.Projects.Interfaces;
using Aura.Application.Projects.Validation;
using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aura.Application.Projects.Services;

public sealed class ProjectService : IProjectService
{
    private readonly IAuraDbContext _dbContext;
    private readonly IUserIdentityService _userIdentityService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ProjectService(
        IAuraDbContext dbContext,
        IUserIdentityService userIdentityService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _userIdentityService = userIdentityService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<IReadOnlyList<ProjectDto>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = await _userIdentityService
            .GetCurrentUserIdAsync(cancellationToken);

        return await _dbContext.Projects
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new ProjectDto(
                x.Id,
                x.Name,
                x.Description,
                x.LocalPath,
                x.RepositoryUrl,
                x.CurrentBranch,
                x.Status,
                x.CreatedAt,
                x.UpdatedAt,
                x.LastOpenedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectDto?> GetByIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var userId = await _userIdentityService
            .GetCurrentUserIdAsync(cancellationToken);

        var project = await _dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == projectId &&
                     x.UserId == userId,
                cancellationToken);

        return project is null
            ? null
            : ToDto(project);
    }

    public async Task<ProjectDto> CreateAsync(
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        ProjectValidation.ValidateName(request.Name);
        var currentBranch = ProjectValidation.NormalizeCurrentBranch(request.CurrentBranch);

        var userId = await _userIdentityService
            .GetCurrentUserIdAsync(cancellationToken);

        var now = _dateTimeProvider.UtcNow;

        var project = new Project
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name.Trim(),
            Description = request.Description,
            LocalPath = request.LocalPath,
            RepositoryUrl = request.RepositoryUrl,
            CurrentBranch = currentBranch,
            Status = "Active",
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.Projects.Add(project);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return ToDto(project);
    }

    public async Task<ProjectDto?> UpdateAsync(
        Guid projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        ProjectValidation.ValidateName(request.Name);
        var currentBranch = ProjectValidation.NormalizeCurrentBranch(request.CurrentBranch);
        var status = ProjectValidation.NormalizeStatus(request.Status);

        var userId = await _userIdentityService
            .GetCurrentUserIdAsync(cancellationToken);

        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(
                x => x.Id == projectId &&
                     x.UserId == userId,
                cancellationToken);

        if (project is null)
        {
            return null;
        }

        project.Name = request.Name.Trim();
        project.Description = request.Description;
        project.LocalPath = request.LocalPath;
        project.RepositoryUrl = request.RepositoryUrl;
        project.CurrentBranch = currentBranch;
        project.Status = status;
        project.UpdatedAt = _dateTimeProvider.UtcNow;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return ToDto(project);
    }

    public async Task<bool> DeleteAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var userId = await _userIdentityService
            .GetCurrentUserIdAsync(cancellationToken);

        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(
                x => x.Id == projectId &&
                     x.UserId == userId,
                cancellationToken);

        if (project is null)
        {
            return false;
        }

        _dbContext.Projects.Remove(project);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    private static ProjectDto ToDto(Project project)
    {
        return new ProjectDto(
            project.Id,
            project.Name,
            project.Description,
            project.LocalPath,
            project.RepositoryUrl,
            project.CurrentBranch,
            project.Status,
            project.CreatedAt,
            project.UpdatedAt,
            project.LastOpenedAt);
    }
}
