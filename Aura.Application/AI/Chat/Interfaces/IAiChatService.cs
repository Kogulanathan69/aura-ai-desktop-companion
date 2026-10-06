using Aura.Application.AI.Chat.DTOs;

namespace Aura.Application.AI.Chat.Interfaces;

public interface IAiChatService
{
    Task<AiChatResponse> GenerateAsync(Guid conversationId, AiChatRequest request, CancellationToken cancellationToken = default);
}
