namespace Aura.Application.ProjectFiles.DTOs;

public sealed record ProjectFileDto(
    Guid Id,
    Guid ProjectId,
    string RelativePath,
    string FileName,
    string Extension,
    bool IsIndexed,
    bool IsSensitive,
    DateTime? LastIndexedAt,
    DateTime? LastModifiedAt,
    DateTime CreatedAt);
