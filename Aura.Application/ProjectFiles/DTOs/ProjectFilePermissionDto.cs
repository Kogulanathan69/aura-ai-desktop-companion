namespace Aura.Application.ProjectFiles.DTOs;

public sealed record ProjectFilePermissionDto(Guid ProjectId, Guid FileId, bool IsApproved);
