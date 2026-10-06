using Aura.Application.Conversations.DTOs;
using Aura.Application.Conversations.Interfaces;

namespace Aura.Api.Endpoints;

public static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/conversations").WithTags("Conversations").RequireAuthorization();
        group.MapGet("/", GetAllAsync);
        group.MapGet("/{conversationId:guid}", GetByIdAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{conversationId:guid}", UpdateAsync);
        group.MapGet("/{conversationId:guid}/messages", GetMessagesAsync);
        group.MapGet("/{conversationId:guid}/messages/{messageId:guid}", GetMessageAsync);
        group.MapPost("/{conversationId:guid}/messages", CreateMessageAsync);
        return app;
    }

    private static async Task<IResult> GetAllAsync(IConversationService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.GetAllAsync(cancellationToken));
    private static async Task<IResult> GetByIdAsync(Guid conversationId, IConversationService service, CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(conversationId, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
    private static async Task<IResult> CreateAsync(CreateConversationRequest request, IConversationService service, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return result is null ? Results.NotFound() : Results.Created($"/api/conversations/{result.Id}", result);
    }
    private static async Task<IResult> UpdateAsync(Guid conversationId, UpdateConversationRequest request, IConversationService service, CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(conversationId, request, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
    private static async Task<IResult> GetMessagesAsync(Guid conversationId, IConversationService service, CancellationToken cancellationToken)
    {
        var result = await service.GetMessagesAsync(conversationId, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
    private static async Task<IResult> GetMessageAsync(Guid conversationId, Guid messageId, IConversationService service, CancellationToken cancellationToken)
    {
        var result = await service.GetMessageAsync(conversationId, messageId, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
    private static async Task<IResult> CreateMessageAsync(Guid conversationId, CreateMessageRequest request, IConversationService service, CancellationToken cancellationToken)
    {
        var result = await service.CreateMessageAsync(conversationId, request, cancellationToken);
        return result is null ? Results.NotFound() : Results.Created($"/api/conversations/{conversationId}/messages/{result.Id}", result);
    }
}
