using Aura.Application.AI.Chat.DTOs;
using Aura.Application.AI.Chat.Interfaces;

namespace Aura.Api.Endpoints;

public static class AiChatEndpoints
{
    public static IEndpointRouteBuilder MapAiChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/conversations/{conversationId:guid}/ai/generate", GenerateAsync)
            .WithTags("AI Chat").RequireAuthorization();
        return app;
    }

    private static async Task<IResult> GenerateAsync(Guid conversationId, AiChatRequest request,
        IAiChatService service, CancellationToken cancellationToken)
    {
        var result = await service.GenerateAsync(conversationId, request, cancellationToken);
        return result.Status switch
        {
            AiChatStatus.Success => Results.Ok(result),
            AiChatStatus.NotFound => Results.NotFound(),
            AiChatStatus.Denied => Results.StatusCode(StatusCodes.Status403Forbidden),
            AiChatStatus.GenerationFailed => Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Json(result, statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
