namespace Aura.Application.Conversations.DTOs;

public sealed record CreateConversationRequest(string Title, string Type = "General", Guid? ProjectId = null);
