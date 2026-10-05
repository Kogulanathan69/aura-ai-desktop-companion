using Aura.Application.Common.Interfaces;
using Aura.Application.Common.Security;
using Aura.Application.ProjectContext.DTOs;
using Aura.Application.ProjectContext.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Aura.Application.ProjectContext.Services;

public sealed class ProjectContextService(
    IAuraDbContext dbContext,
    IUserIdentityService userIdentityService) : IProjectContextService
{
    public async Task<ProjectContextDto?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);

        var project = await dbContext.Projects.AsNoTracking()
            .Where(x => x.Id == projectId && x.UserId == userId)
            .Select(x => new { x.Id, x.Name, x.Description, x.RepositoryUrl, x.CurrentBranch, x.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (project is null) return null;

        var session = await dbContext.ProjectSessions.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.UserId == userId &&
                        x.Status == "Active" && x.EndedAt == null)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new ProjectContextSessionDto(x.Id, x.StartedAt, x.CurrentTask, x.Status))
            .FirstOrDefaultAsync(cancellationToken);

        var files = await dbContext.ProjectFiles.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.RelativePath)
            .Select(x => new { x.Id, x.RelativePath, x.FileName, x.Extension,
                               x.IsIndexed, x.IsSensitive, x.LastModifiedAt })
            .ToListAsync(cancellationToken);

        var fileIdentifiers = files.Select(x => ProjectFilePermissionConvention.FormatResourceIdentifier(x.Id)).ToArray();
        var approvedIdentifiers = fileIdentifiers.Length == 0
            ? []
            : await dbContext.Permissions.AsNoTracking()
                .Where(x => x.UserId == userId && x.ProjectId == projectId &&
                            x.ResourceType == ProjectFilePermissionConvention.ResourceType &&
                            x.AccessLevel == ProjectFilePermissionConvention.AccessLevel &&
                            x.Status == ProjectFilePermissionConvention.GrantedStatus && x.RevokedAt == null &&
                            fileIdentifiers.Contains(x.ResourceIdentifier))
                .Select(x => x.ResourceIdentifier)
                .ToArrayAsync(cancellationToken);
        var approved = approvedIdentifiers.ToHashSet(StringComparer.Ordinal);

        var contextFiles = files.Select(x => new ProjectContextFileDto(
            x.Id, x.RelativePath, x.FileName, x.Extension, x.IsIndexed,
            x.IsSensitive, x.LastModifiedAt,
            approved.Contains(ProjectFilePermissionConvention.FormatResourceIdentifier(x.Id))))
            .ToList();

        return new ProjectContextDto(project.Id, project.Name, project.Description,
            project.RepositoryUrl, project.CurrentBranch, project.Status, session, contextFiles);
    }
}
