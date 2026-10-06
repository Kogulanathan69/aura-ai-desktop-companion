using Aura.Application.ProjectMemories.DTOs;
using Aura.Application.ProjectMemories.Interfaces;

namespace Aura.Api.Endpoints;

public static class ProjectMemoryEndpoints
{
    public static IEndpointRouteBuilder MapProjectMemoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/memories").WithTags("Project Memories").RequireAuthorization();
        group.MapGet("/", GetByProjectAsync);
        group.MapGet("/{memoryId:guid}", GetByIdAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{memoryId:guid}", UpdateAsync);
        group.MapDelete("/{memoryId:guid}", DeleteAsync);
        return app;
    }

    private static async Task<IResult> GetByProjectAsync(Guid projectId, IProjectMemoryService service, CancellationToken cancellationToken)
    {
        var memories = await service.GetByProjectAsync(projectId, cancellationToken);
        return memories is null ? Results.NotFound() : Results.Ok(memories);
    }
    private static async Task<IResult> GetByIdAsync(Guid projectId, Guid memoryId, IProjectMemoryService service, CancellationToken cancellationToken)
    {
        var memory = await service.GetByIdAsync(projectId, memoryId, cancellationToken);
        return memory is null ? Results.NotFound() : Results.Ok(memory);
    }
    private static async Task<IResult> CreateAsync(Guid projectId, CreateProjectMemoryRequest request, IProjectMemoryService service, CancellationToken cancellationToken)
    {
        var memory = await service.CreateAsync(projectId, request, cancellationToken);
        return memory is null ? Results.NotFound() : Results.Created($"/api/projects/{projectId}/memories/{memory.Id}", memory);
    }
    private static async Task<IResult> UpdateAsync(Guid projectId, Guid memoryId, UpdateProjectMemoryRequest request, IProjectMemoryService service, CancellationToken cancellationToken)
    {
        var memory = await service.UpdateAsync(projectId, memoryId, request, cancellationToken);
        return memory is null ? Results.NotFound() : Results.Ok(memory);
    }
    private static async Task<IResult> DeleteAsync(Guid projectId, Guid memoryId, IProjectMemoryService service, CancellationToken cancellationToken) =>
        await service.DeleteAsync(projectId, memoryId, cancellationToken) ? Results.NoContent() : Results.NotFound();
}
