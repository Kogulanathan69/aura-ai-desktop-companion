namespace Aura.Application.ProjectFiles.DTOs;

public sealed record UpdateProjectFileRequest(
    string RelativePath,
    string FileName,
    string Extension);
