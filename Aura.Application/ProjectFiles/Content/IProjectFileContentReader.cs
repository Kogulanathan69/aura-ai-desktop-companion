namespace Aura.Application.ProjectFiles.Content;

public interface IProjectFileContentReader
{
    Task<ProjectFileReadResult> ReadAsync(Guid localUserId, string? projectRoot,
        string relativePath, string fileName, string extension, CancellationToken cancellationToken = default);
}

public interface ISafeProjectFileContentService
{
    Task<ProjectFileContentResult> GetAsync(Guid projectId, Guid projectFileId,
        CancellationToken cancellationToken = default);
}
