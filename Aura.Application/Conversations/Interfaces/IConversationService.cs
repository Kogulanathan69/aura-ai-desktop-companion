using Aura.Application.Conversations.DTOs;

namespace Aura.Application.Conversations.Interfaces;

public interface IConversationService
{
    Task<IReadOnlyList<ConversationDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ConversationDto?> GetByIdAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<ConversationDto?> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken = default);
    Task<ConversationDto?> UpdateAsync(Guid conversationId, UpdateConversationRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MessageDto>?> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<MessageDto?> GetMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default);
    Task<MessageDto?> CreateMessageAsync(Guid conversationId, CreateMessageRequest request, CancellationToken cancellationToken = default);
}
