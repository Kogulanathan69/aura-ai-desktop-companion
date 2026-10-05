namespace Aura.Application.ProjectFiles.DTOs;

public sealed record RegisterProjectFileRequest(
    string RelativePath,
    string FileName,
    string Extension,
    bool IsSensitive);
