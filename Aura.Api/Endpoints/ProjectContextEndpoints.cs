using Aura.Application.ProjectContext.Interfaces;

namespace Aura.Api.Endpoints;

public static class ProjectContextEndpoints
{
    public static IEndpointRouteBuilder MapProjectContextEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/projects/{projectId:guid}/context")
            .WithTags("Project Context")
            .RequireAuthorization()
            .MapGet("/", GetAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(Guid projectId,
        IProjectContextService service, CancellationToken cancellationToken)
    {
        var context = await service.GetAsync(projectId, cancellationToken);
        return context is null ? Results.NotFound() : Results.Ok(context);
    }
}
