using Aura.Application.Projects.DTOs;
using Aura.Application.Projects.Interfaces;

namespace Aura.Api.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects")
            .WithTags("Projects")
            .RequireAuthorization();

        group.MapGet("/", GetAllAsync);
        group.MapGet("/{id:guid}", GetByIdAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);

        return app;
    }

    private static async Task<IResult> GetAllAsync(
        IProjectService projectService,
        CancellationToken cancellationToken)
    {
        var projects = await projectService
            .GetAllAsync(cancellationToken);

        return Results.Ok(projects);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        IProjectService projectService,
        CancellationToken cancellationToken)
    {
        var project = await projectService
            .GetByIdAsync(id, cancellationToken);

        return project is null
            ? Results.NotFound()
            : Results.Ok(project);
    }

    private static async Task<IResult> CreateAsync(
        CreateProjectRequest request,
        IProjectService projectService,
        CancellationToken cancellationToken)
    {
        var project = await projectService
            .CreateAsync(request, cancellationToken);

        return Results.Created(
            $"/api/projects/{project.Id}",
            project);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateProjectRequest request,
        IProjectService projectService,
        CancellationToken cancellationToken)
    {
        var project = await projectService
            .UpdateAsync(
                id,
                request,
                cancellationToken);

        return project is null
            ? Results.NotFound()
            : Results.Ok(project);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        IProjectService projectService,
        CancellationToken cancellationToken)
    {
        var deleted = await projectService
            .DeleteAsync(id, cancellationToken);

        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
