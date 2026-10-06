namespace Aura.Application.Conversations.DTOs;

public sealed record ConversationDto(Guid Id, Guid? ProjectId, string Title, string Type,
    string? Summary, bool IsArchived, DateTime CreatedAt, DateTime UpdatedAt, DateTime? LastMessageAt);
