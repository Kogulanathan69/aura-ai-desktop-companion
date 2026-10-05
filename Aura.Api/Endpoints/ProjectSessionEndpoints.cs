using Aura.Application.ProjectSessions.DTOs;
using Aura.Application.ProjectSessions.Interfaces;

namespace Aura.Api.Endpoints;

public static class ProjectSessionEndpoints
{
    public static IEndpointRouteBuilder MapProjectSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/sessions")
            .WithTags("Project Sessions")
            .RequireAuthorization();

        group.MapPost("/", StartAsync);
        group.MapGet("/current", GetCurrentAsync);
        group.MapGet("/", GetHistoryAsync);
        group.MapGet("/{sessionId:guid}", GetByIdAsync);
        group.MapPut("/{sessionId:guid}", UpdateAsync);
        group.MapPost("/{sessionId:guid}/end", EndAsync);
        return app;
    }

    private static async Task<IResult> StartAsync(Guid projectId, StartProjectSessionRequest request,
        IProjectSessionService service, CancellationToken cancellationToken)
    {
        var session = await service.StartAsync(projectId, request, cancellationToken);
        return session is null ? Results.NotFound() :
            Results.Created($"/api/projects/{projectId}/sessions/{session.Id}", session);
    }

    private static async Task<IResult> GetCurrentAsync(Guid projectId,
        IProjectSessionService service, CancellationToken cancellationToken)
    {
        var session = await service.GetCurrentAsync(projectId, cancellationToken);
        return session is null ? Results.NotFound() : Results.Ok(session);
    }

    private static async Task<IResult> GetByIdAsync(Guid projectId, Guid sessionId,
        IProjectSessionService service, CancellationToken cancellationToken)
    {
        var session = await service.GetByIdAsync(projectId, sessionId, cancellationToken);
        return session is null ? Results.NotFound() : Results.Ok(session);
    }

    private static async Task<IResult> GetHistoryAsync(Guid projectId,
        IProjectSessionService service, CancellationToken cancellationToken)
    {
        var sessions = await service.GetHistoryAsync(projectId, cancellationToken);
        return sessions is null ? Results.NotFound() : Results.Ok(sessions);
    }

    private static async Task<IResult> UpdateAsync(Guid projectId, Guid sessionId,
        UpdateProjectSessionRequest request, IProjectSessionService service, CancellationToken cancellationToken)
    {
        var session = await service.UpdateAsync(projectId, sessionId, request, cancellationToken);
        return session is null ? Results.NotFound() : Results.Ok(session);
    }

    private static async Task<IResult> EndAsync(Guid projectId, Guid sessionId,
        EndProjectSessionRequest request, IProjectSessionService service, CancellationToken cancellationToken)
    {
        var session = await service.EndAsync(projectId, sessionId, request, cancellationToken);
        return session is null ? Results.NotFound() : Results.Ok(session);
    }
}
