using Aura.Application.ProjectFiles.Content;

namespace Aura.Api.Endpoints;

public static class ProjectFileContentEndpoints
{
    public static IEndpointRouteBuilder MapProjectFileContentEndpoints(this IEndpointRouteBuilder app)
    {
        if (!app.ServiceProvider.GetRequiredService<ProjectFileAccessOptions>().Enabled) return app;
        app.MapGet("/api/projects/{projectId:guid}/files/{projectFileId:guid}/content", GetAsync)
            .WithTags("Project Files").RequireAuthorization();
        return app;
    }

    private static async Task<IResult> GetAsync(Guid projectId, Guid projectFileId,
        ISafeProjectFileContentService service, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(projectId, projectFileId, cancellationToken);
        return result.Status switch
        {
            ProjectFileContentStatus.Success => Results.Ok(result),
            ProjectFileContentStatus.NotFound => Results.NotFound(),
            ProjectFileContentStatus.Denied => Results.StatusCode(StatusCodes.Status403Forbidden),
            ProjectFileContentStatus.TooLarge => Results.StatusCode(StatusCodes.Status413PayloadTooLarge),
            ProjectFileContentStatus.UnsupportedType => Results.StatusCode(StatusCodes.Status415UnsupportedMediaType),
            ProjectFileContentStatus.Unavailable => Results.StatusCode(StatusCodes.Status403Forbidden),
            _ => Results.StatusCode(StatusCodes.Status422UnprocessableEntity)
        };
    }
}
