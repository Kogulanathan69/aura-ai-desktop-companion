namespace Aura.Application.ProjectMemories.DTOs;

public sealed record UpdateProjectMemoryRequest(
    string Type, string Title, string Content, short Importance = 3, Guid? SessionId = null);
