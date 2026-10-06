using Aura.Application.Common.Interfaces;
using Aura.Application.Conversations.DTOs;
using Aura.Application.Conversations.Interfaces;
using Aura.Application.Conversations.Validation;
using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aura.Application.Conversations.Services;

public sealed class ConversationService(IAuraDbContext dbContext, IUserIdentityService identity,
    IDateTimeProvider clock) : IConversationService
{
    public async Task<IReadOnlyList<ConversationDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        return await OwnedConversations(userId).AsNoTracking()
            .OrderByDescending(x => x.LastMessageAt.HasValue).ThenByDescending(x => x.LastMessageAt)
            .ThenByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
            .Select(x => new ConversationDto(x.Id, x.ProjectId, x.Title, x.Type, x.Summary,
                x.IsArchived, x.CreatedAt, x.UpdatedAt, x.LastMessageAt)).ToListAsync(cancellationToken);
    }

    public async Task<ConversationDto?> GetByIdAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        var conversation = await OwnedConversations(userId).AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        return conversation is null ? null : ToDto(conversation);
    }

    public async Task<ConversationDto?> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        var title = ConversationValidation.NormalizeTitle(request.Title);
        var type = ConversationValidation.NormalizeType(request.Type, request.ProjectId);
        if (request.ProjectId.HasValue && !await dbContext.Projects.AsNoTracking().AnyAsync(x =>
            x.Id == request.ProjectId.Value && x.UserId == userId, cancellationToken)) return null;
        var now = clock.UtcNow;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), UserId = userId, ProjectId = request.ProjectId,
            Title = title, Type = type, Summary = null, IsArchived = false,
            CreatedAt = now, UpdatedAt = now, LastMessageAt = null
        };
        dbContext.Conversations.Add(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(conversation);
    }

    public async Task<ConversationDto?> UpdateAsync(Guid conversationId, UpdateConversationRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        var conversation = await OwnedConversations(userId).FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null) return null;
        var title = ConversationValidation.NormalizeTitle(request.Title);
        conversation.Title = title;
        conversation.IsArchived = request.IsArchived;
        conversation.UpdatedAt = clock.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(conversation);
    }

    public async Task<IReadOnlyList<MessageDto>?> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnedConversations(userId).AsNoTracking().AnyAsync(x => x.Id == conversationId, cancellationToken)) return null;
        return await ScopedMessages(conversationId).AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new MessageDto(x.Id, x.ConversationId, x.Role, x.Content, x.MessageType,
                x.ModelProvider, x.ModelName, x.TokenCount, x.CreatedAt)).ToListAsync(cancellationToken);
    }

    public async Task<MessageDto?> GetMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default)
    {
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        if (!await OwnedConversations(userId).AsNoTracking().AnyAsync(x => x.Id == conversationId, cancellationToken)) return null;
        var message = await ScopedMessages(conversationId).AsNoTracking().FirstOrDefaultAsync(x => x.Id == messageId, cancellationToken);
        return message is null ? null : ToDto(message);
    }

    public async Task<MessageDto?> CreateMessageAsync(Guid conversationId, CreateMessageRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await identity.GetCurrentUserIdAsync(cancellationToken);
        var conversation = await OwnedConversations(userId).FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null) return null;
        var content = ConversationValidation.NormalizeContent(request.Content);
        var now = clock.UtcNow;
        var message = new Message
        {
            Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "User", Content = content,
            MessageType = "Text", ModelProvider = null, ModelName = null, TokenCount = null, CreatedAt = now
        };
        dbContext.Messages.Add(message);
        conversation.LastMessageAt = now;
        conversation.UpdatedAt = now;
        // One EF unit of work: message insert and parent timestamp update.
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(message);
    }

    private IQueryable<Conversation> OwnedConversations(Guid userId) => dbContext.Conversations.Where(x =>
        x.UserId == userId && ((x.Type == "General" && x.ProjectId == null) ||
        (x.Type == "Project" && x.ProjectId != null && dbContext.Projects.Any(p => p.Id == x.ProjectId && p.UserId == userId))));

    private IQueryable<Message> ScopedMessages(Guid conversationId) => dbContext.Messages.Where(x => x.ConversationId == conversationId);
    private static ConversationDto ToDto(Conversation x) => new(x.Id, x.ProjectId, x.Title, x.Type,
        x.Summary, x.IsArchived, x.CreatedAt, x.UpdatedAt, x.LastMessageAt);
    private static MessageDto ToDto(Message x) => new(x.Id, x.ConversationId, x.Role, x.Content,
        x.MessageType, x.ModelProvider, x.ModelName, x.TokenCount, x.CreatedAt);
}
