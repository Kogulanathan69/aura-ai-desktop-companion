using Aura.Application.ProjectFiles.DTOs;
using Aura.Application.ProjectFiles.Interfaces;

namespace Aura.Api.Endpoints;

public static class ProjectFileEndpoints
{
    public static IEndpointRouteBuilder MapProjectFileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/files")
            .WithTags("Project Files")
            .RequireAuthorization();

        group.MapPost("/", RegisterAsync);
        group.MapGet("/", GetAllAsync);
        group.MapGet("/{fileId:guid}", GetByIdAsync);
        group.MapPut("/{fileId:guid}", UpdateAsync);
        group.MapDelete("/{fileId:guid}", RemoveAsync);
        group.MapPost("/{fileId:guid}/permission", GrantAccessAsync);
        group.MapDelete("/{fileId:guid}/permission", RevokeAccessAsync);
        group.MapGet("/{fileId:guid}/permission", GetAccessAsync);
        return app;
    }

    private static async Task<IResult> RegisterAsync(Guid projectId, RegisterProjectFileRequest request,
        IProjectFileService service, CancellationToken cancellationToken)
    {
        var file = await service.RegisterAsync(projectId, request, cancellationToken);
        return file is null ? Results.NotFound() :
            Results.Created($"/api/projects/{projectId}/files/{file.Id}", file);
    }

    private static async Task<IResult> GetAllAsync(Guid projectId,
        IProjectFileService service, CancellationToken cancellationToken)
    {
        var files = await service.GetAllAsync(projectId, cancellationToken);
        return files is null ? Results.NotFound() : Results.Ok(files);
    }

    private static async Task<IResult> GetByIdAsync(Guid projectId, Guid fileId,
        IProjectFileService service, CancellationToken cancellationToken)
    {
        var file = await service.GetByIdAsync(projectId, fileId, cancellationToken);
        return file is null ? Results.NotFound() : Results.Ok(file);
    }

    private static async Task<IResult> UpdateAsync(Guid projectId, Guid fileId, UpdateProjectFileRequest request,
        IProjectFileService service, CancellationToken cancellationToken)
    {
        var file = await service.UpdateAsync(projectId, fileId, request, cancellationToken);
        return file is null ? Results.NotFound() : Results.Ok(file);
    }

    private static async Task<IResult> RemoveAsync(Guid projectId, Guid fileId,
        IProjectFileService service, CancellationToken cancellationToken)
    {
        var removed = await service.RemoveAsync(projectId, fileId, cancellationToken);
        return removed ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> GrantAccessAsync(Guid projectId, Guid fileId,
        IProjectFileService service, CancellationToken cancellationToken)
    {
        var permission = await service.GrantAccessAsync(projectId, fileId, cancellationToken);
        return permission is null ? Results.NotFound() : Results.Ok(permission);
    }

    private static async Task<IResult> RevokeAccessAsync(Guid projectId, Guid fileId,
        IProjectFileService service, CancellationToken cancellationToken)
    {
        var permission = await service.RevokeAccessAsync(projectId, fileId, cancellationToken);
        return permission is null ? Results.NotFound() : Results.Ok(permission);
    }

    private static async Task<IResult> GetAccessAsync(Guid projectId, Guid fileId,
        IProjectFileService service, CancellationToken cancellationToken)
    {
        var permission = await service.GetAccessAsync(projectId, fileId, cancellationToken);
        return permission is null ? Results.NotFound() : Results.Ok(permission);
    }
}
