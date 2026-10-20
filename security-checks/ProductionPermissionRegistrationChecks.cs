using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionPermissionRegistrationChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var api = Read("Aura.Api", "Program.cs");
        var source = Read("Aura.Infrastructure", "Data", "ToolPermissionPersistenceSource.cs");
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var evidence = Read("STEP-15C-PRODUCTION-PERMISSION-EVIDENCE-REGISTRATION-REVIEW.md");
        check(typeof(IToolPermissionSource).IsAssignableFrom(typeof(ToolPermissionPersistenceSource)) &&
            source.Contains("grant.Status == ToolPermissionGrantStatus.Active && grant.RevokedAt == null") &&
            source.Contains("grant.UserId == request.Scope.UserId") &&
            source.Contains("grant.ConversationId == request.Scope.ConversationId") &&
            source.Contains("grant.ProjectId == request.Scope.ProjectId") &&
            source.Contains("grant.ToolId == requestedTool.Value") &&
            source.Contains("grant.RequiredPermission == (int)request.Requirement"),
            "17C exact active grant predicates exclude revoked and mismatched grants");
        check(evidence.Contains("REVOKED GRANT CHECK: NOT PROVEN") &&
            evidence.Contains("RESULT: INCOMPLETE") &&
            !api.Contains("AddScoped<IToolPermissionSource") &&
            inspector.Contains("typeof(ToolPermissionPersistenceSource)") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Permission,
            "17C unproven real revoked fixture cannot enter production catalog");
        return Task.CompletedTask;
    }
}
