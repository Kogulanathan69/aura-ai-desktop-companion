namespace Aura.Application.Conversations.DTOs;

public sealed record MessageDto(Guid Id, Guid ConversationId, string Role, string Content,
    string MessageType, string? ModelProvider, string? ModelName, int? TokenCount, DateTime CreatedAt);
