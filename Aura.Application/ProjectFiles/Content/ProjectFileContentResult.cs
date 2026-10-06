namespace Aura.Application.ProjectFiles.Content;

public enum ProjectFileContentStatus
{
    Success, NotFound, Denied, Unavailable, TooLarge, UnsupportedType, UnsafeOrUnreadable
}

public sealed record ProjectFileContentResult(
    ProjectFileContentStatus Status,
    Guid? ProjectFileId = null,
    string? FileName = null,
    string? RelativePath = null,
    string? Content = null,
    bool IsRedacted = false);

// Internal reader output must never be returned directly by the API.
public sealed record ProjectFileReadResult(ProjectFileContentStatus Status, string? Content = null);
