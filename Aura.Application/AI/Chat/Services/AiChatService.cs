using Aura.Application.AI.Chat.DTOs;
using Aura.Application.AI.Chat.Interfaces;
using Aura.Application.AI.Chat.Validation;
using Aura.Application.AI.DTOs;
using Aura.Application.AI.Providers;
using Aura.Application.AI.Models;
using Aura.Application.Common.Interfaces;
using Aura.Application.Conversations.DTOs;
using Aura.Application.Conversations.Security;
using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aura.Application.AI.Chat.Services;

public sealed class AiChatService(IAuraDbContext dbContext, IUserIdentityService identity,
    IDateTimeProvider clock, IAiProviderRouter provider) : IAiChatService
{
    public async Task<AiChatResponse> GenerateAsync(Guid conversationId, AiChatRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        var conversation = await dbContext.OwnedConversations(userId)
            .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null) return new(AiChatStatus.NotFound);
        var originalType = conversation.Type;
        var originalProjectId = conversation.ProjectId;
        var prompt = AiChatValidation.NormalizePrompt(request.Prompt);
        cancellationToken.ThrowIfCancellationRequested();
        var userMessage = new Message
        {
            Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "User", MessageType = "Text",
            Content = prompt, CreatedAt = clock.UtcNow, ModelProvider = null, ModelName = null, TokenCount = null
        };
        dbContext.Messages.Add(userMessage);
        conversation.LastMessageAt = userMessage.CreatedAt;
        conversation.UpdatedAt = userMessage.CreatedAt;
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return new(AiChatStatus.PersistenceFailed); }

        // Only the current explicit prompt crosses this boundary; no history/context retrieval.
        var userDto = ToDto(userMessage);
        cancellationToken.ThrowIfCancellationRequested();
        var generated = await provider.GenerateAsync(new AiGenerationRequest(prompt), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (generated.Status != AiGenerationStatus.Success)
            return new(AiChatStatus.GenerationFailed, userDto, GenerationStatus: generated.Status);
        if (!AiChatValidation.ValidGeneration(generated))
            return new(AiChatStatus.GenerationFailed, userDto, GenerationStatus: AiGenerationStatus.InvalidResponse);

        // Fresh read after generation; do not attach a second Conversation or hold a transaction across AI.
        if (!await dbContext.OwnedConversations(userId).AsNoTracking().AnyAsync(x => x.Id == conversationId &&
            x.Type == originalType && x.ProjectId == originalProjectId, cancellationToken))
            return new(AiChatStatus.Denied);
        var assistantMessage = new Message
        {
            Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "Assistant", MessageType = "Text",
            Content = generated.Content!, ModelProvider = generated.Provider, ModelName = generated.Model,
            TokenCount = null, CreatedAt = clock.UtcNow
        };
        dbContext.Messages.Add(assistantMessage);
        conversation.LastMessageAt = assistantMessage.CreatedAt;
        conversation.UpdatedAt = assistantMessage.CreatedAt;
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return new(AiChatStatus.PersistenceFailed, userDto); }
        return new(AiChatStatus.Success, userDto, ToDto(assistantMessage), AiGenerationStatus.Success);
    }

    private static MessageDto ToDto(Message x) => new(x.Id, x.ConversationId, x.Role, x.Content,
        x.MessageType, x.ModelProvider, x.ModelName, x.TokenCount, x.CreatedAt);
}
