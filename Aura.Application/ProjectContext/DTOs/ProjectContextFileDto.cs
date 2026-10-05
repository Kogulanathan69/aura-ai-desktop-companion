namespace Aura.Application.ProjectContext.DTOs;

public sealed record ProjectContextFileDto(
    Guid Id,
    string RelativePath,
    string FileName,
    string Extension,
    bool IsIndexed,
    bool IsSensitive,
    DateTime? LastModifiedAt,
    bool HasReadPermission);
