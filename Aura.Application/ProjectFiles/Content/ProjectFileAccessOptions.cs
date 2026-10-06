namespace Aura.Application.ProjectFiles.Content;

public sealed class ProjectFileAccessOptions
{
    public const string SectionName = "ProjectFileAccess";
    public const int MaximumBytes = 1024 * 1024;
    public bool Enabled { get; init; } = false;
    public Dictionary<string, string[]> AllowedRootsByUser { get; init; } = new();
}
