using Aura.Application.Common.Exceptions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Common.Security;
using Aura.Application.Privacy.Interfaces;
using Aura.Application.Privacy.Models;
using Aura.Application.ProjectFiles.Validation;
using Microsoft.EntityFrameworkCore;

namespace Aura.Application.ProjectFiles.Content;

public sealed class SafeProjectFileContentService(
    IAuraDbContext dbContext, IUserIdentityService identity, IProjectFileContentReader reader,
    IPrivacyGuard privacyGuard, ProjectFileAccessOptions options) : ISafeProjectFileContentService
{
    public async Task<ProjectFileContentResult> GetAsync(Guid projectId, Guid projectFileId,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled) return new(ProjectFileContentStatus.Unavailable);
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        var project = await dbContext.Projects.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == projectId && x.UserId == userId, cancellationToken);
        if (project is null) return new(ProjectFileContentStatus.NotFound);
        var file = await dbContext.ProjectFiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == projectFileId && x.ProjectId == projectId, cancellationToken);
        if (file is null) return new(ProjectFileContentStatus.NotFound);
        if (!await HasReadAsync(userId, projectId, projectFileId, cancellationToken))
            return new(ProjectFileContentStatus.Denied);

        (string RelativePath, string FileName, string Extension) metadata;
        try { metadata = ProjectFileValidation.Normalize(file.RelativePath, file.FileName, file.Extension); }
        catch (AppValidationException) { return new(ProjectFileContentStatus.UnsafeOrUnreadable); }
        var input = new PrivacyGuardInput(metadata.RelativePath, metadata.FileName, metadata.Extension);
        if (file.IsSensitive || privacyGuard.Evaluate(input).Decision == PrivacyDecision.Block)
            return new(ProjectFileContentStatus.Denied);

        var read = await reader.ReadAsync(userId, project.LocalPath, metadata.RelativePath,
            metadata.FileName, metadata.Extension, cancellationToken);
        if (read.Status != ProjectFileContentStatus.Success) return new(read.Status);

        // Fresh untracked queries: deny persistent changes, without claiming uninterrupted authorization.
        var currentProject = await dbContext.Projects.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == projectId && x.UserId == userId, cancellationToken);
        var currentFile = await dbContext.ProjectFiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == projectFileId && x.ProjectId == projectId, cancellationToken);
        if (currentProject is null || currentProject.LocalPath != project.LocalPath || currentFile is null ||
            currentFile.RelativePath != file.RelativePath || currentFile.FileName != file.FileName ||
            currentFile.Extension != file.Extension || currentFile.IsSensitive ||
            !await HasReadAsync(userId, projectId, projectFileId, cancellationToken))
            return new(ProjectFileContentStatus.Denied);

        cancellationToken.ThrowIfCancellationRequested();
        if (read.Content is null) return new(ProjectFileContentStatus.UnsafeOrUnreadable);
        var decision = privacyGuard.Evaluate(input with { Content = read.Content });
        if (decision.Decision == PrivacyDecision.Block ||
            (decision.Decision == PrivacyDecision.Redact && decision.RedactedContent is null))
            return new(ProjectFileContentStatus.Denied);
        return new(ProjectFileContentStatus.Success, file.Id, metadata.FileName, metadata.RelativePath,
            decision.Decision == PrivacyDecision.Redact ? decision.RedactedContent : read.Content,
            decision.Decision == PrivacyDecision.Redact);
    }

    private Task<bool> HasReadAsync(Guid userId, Guid projectId, Guid fileId, CancellationToken cancellationToken)
    {
        var resource = ProjectFilePermissionConvention.FormatResourceIdentifier(fileId);
        return dbContext.Permissions.AsNoTracking().AnyAsync(x => x.UserId == userId &&
            x.ProjectId == projectId && x.ResourceType == ProjectFilePermissionConvention.ResourceType &&
            x.ResourceIdentifier == resource && x.AccessLevel == ProjectFilePermissionConvention.AccessLevel &&
            x.Status == ProjectFilePermissionConvention.GrantedStatus && x.RevokedAt == null, cancellationToken);
    }
}
