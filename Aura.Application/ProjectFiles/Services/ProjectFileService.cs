using Aura.Application.Common.Exceptions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Common.Security;
using Aura.Application.ProjectFiles.DTOs;
using Aura.Application.ProjectFiles.Interfaces;
using Aura.Application.ProjectFiles.Validation;
using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aura.Application.ProjectFiles.Services;

public sealed class ProjectFileService(
    IAuraDbContext dbContext,
    IUserIdentityService userIdentityService,
    IDateTimeProvider dateTimeProvider) : IProjectFileService
{
    public async Task<ProjectFileDto?> RegisterAsync(Guid projectId, RegisterProjectFileRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;

        var (path, name, extension) = ProjectFileValidation.Normalize(request.RelativePath, request.FileName, request.Extension);
        if (await ScopedFiles(projectId).AnyAsync(x => x.RelativePath == path, cancellationToken))
            throw new AppValidationException("This file path is already registered for the project.");

        var file = new ProjectFile
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            RelativePath = path,
            FileName = name,
            Extension = extension,
            IsSensitive = request.IsSensitive,
            IsIndexed = false,
            CreatedAt = dateTimeProvider.UtcNow
        };
        dbContext.ProjectFiles.Add(file);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(file);
    }

    public async Task<IReadOnlyList<ProjectFileDto>?> GetAllAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;
        return await ScopedFiles(projectId).AsNoTracking()
            .OrderBy(x => x.RelativePath)
            .Select(x => new ProjectFileDto(x.Id, x.ProjectId, x.RelativePath, x.FileName,
                x.Extension, x.IsIndexed, x.IsSensitive, x.LastIndexedAt, x.LastModifiedAt, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectFileDto?> GetByIdAsync(Guid projectId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;
        var file = await ScopedFiles(projectId).AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == fileId, cancellationToken);
        return file is null ? null : ToDto(file);
    }

    public async Task<ProjectFileDto?> UpdateAsync(Guid projectId, Guid fileId, UpdateProjectFileRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return null;
        var file = await ScopedFiles(projectId).FirstOrDefaultAsync(x => x.Id == fileId, cancellationToken);
        if (file is null) return null;

        var (path, name, extension) = ProjectFileValidation.Normalize(request.RelativePath, request.FileName, request.Extension);
        var stored = ProjectFileValidation.Normalize(file.RelativePath, file.FileName, file.Extension);
        if ((path, name, extension) != stored)
            throw new AppValidationException("Registered file identity cannot be changed. Remove and register the file again to change its path or file type.");
        // IsSensitive and indexing fields are server managed after registration.
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(file);
    }

    public async Task<bool> RemoveAsync(Guid projectId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsProjectAsync(projectId, userId, cancellationToken)) return false;
        var file = await ScopedFiles(projectId).FirstOrDefaultAsync(x => x.Id == fileId, cancellationToken);
        if (file is null) return false;

        var permissions = await ScopedPermissions(projectId, fileId, userId).ToListAsync(cancellationToken);
        dbContext.Permissions.RemoveRange(permissions);
        dbContext.ProjectFiles.Remove(file);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ProjectFilePermissionDto?> GrantAccessAsync(Guid projectId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsFileAsync(projectId, fileId, userId, cancellationToken)) return null;

        var permissions = await ScopedPermissions(projectId, fileId, userId).ToListAsync(cancellationToken);
        var now = dateTimeProvider.UtcNow;
        if (permissions.Count == 0)
        {
            dbContext.Permissions.Add(new Permission
            {
                Id = Guid.NewGuid(), UserId = userId, ProjectId = projectId,
                ResourceType = ProjectFilePermissionConvention.ResourceType,
                ResourceIdentifier = ProjectFilePermissionConvention.FormatResourceIdentifier(fileId),
                AccessLevel = ProjectFilePermissionConvention.AccessLevel,
                Status = ProjectFilePermissionConvention.GrantedStatus,
                GrantedAt = now, CreatedAt = now, UpdatedAt = now
            });
        }
        else
        {
            foreach (var permission in permissions)
            {
                permission.Status = ProjectFilePermissionConvention.GrantedStatus;
                permission.GrantedAt = now;
                permission.RevokedAt = null;
                permission.UpdatedAt = now;
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(projectId, fileId, true);
    }

    public async Task<ProjectFilePermissionDto?> RevokeAccessAsync(Guid projectId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsFileAsync(projectId, fileId, userId, cancellationToken)) return null;
        await RevokePermissionRecordsAsync(projectId, fileId, userId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(projectId, fileId, false);
    }

    public async Task<ProjectFilePermissionDto?> GetAccessAsync(Guid projectId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var userId = await userIdentityService.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnsFileAsync(projectId, fileId, userId, cancellationToken)) return null;
        var approved = await ScopedPermissions(projectId, fileId, userId).AsNoTracking()
            .AnyAsync(x => x.Status == ProjectFilePermissionConvention.GrantedStatus && x.RevokedAt == null, cancellationToken);
        return new(projectId, fileId, approved);
    }

    private async Task<bool> OwnsProjectAsync(Guid projectId, Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Projects.AsNoTracking().AnyAsync(x => x.Id == projectId && x.UserId == userId, cancellationToken);

    private async Task<bool> OwnsFileAsync(Guid projectId, Guid fileId, Guid userId, CancellationToken cancellationToken) =>
        await OwnsProjectAsync(projectId, userId, cancellationToken) &&
        await ScopedFiles(projectId).AsNoTracking().AnyAsync(x => x.Id == fileId, cancellationToken);

    private IQueryable<ProjectFile> ScopedFiles(Guid projectId) =>
        dbContext.ProjectFiles.Where(x => x.ProjectId == projectId);

    private IQueryable<Permission> ScopedPermissions(Guid projectId, Guid fileId, Guid userId) =>
        dbContext.Permissions.Where(x => x.UserId == userId && x.ProjectId == projectId &&
            x.ResourceType == ProjectFilePermissionConvention.ResourceType &&
            x.ResourceIdentifier == ProjectFilePermissionConvention.FormatResourceIdentifier(fileId) &&
            x.AccessLevel == ProjectFilePermissionConvention.AccessLevel);

    private async Task RevokePermissionRecordsAsync(Guid projectId, Guid fileId, Guid userId, CancellationToken cancellationToken)
    {
        var permissions = await ScopedPermissions(projectId, fileId, userId).ToListAsync(cancellationToken);
        var now = dateTimeProvider.UtcNow;
        foreach (var permission in permissions)
        {
            permission.Status = ProjectFilePermissionConvention.RevokedStatus;
            permission.RevokedAt = now;
            permission.UpdatedAt = now;
        }
    }

    private static ProjectFileDto ToDto(ProjectFile file) =>
        new(file.Id, file.ProjectId, file.RelativePath, file.FileName, file.Extension,
            file.IsIndexed, file.IsSensitive, file.LastIndexedAt, file.LastModifiedAt, file.CreatedAt);
}
