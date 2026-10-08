using Aura.Api.Security;
using Aura.Application.Actions;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

internal static class PermissionPersistenceReadinessChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only", npgsql => npgsql.UseVector())
            .Options;
        await using var db = new AuraDbContext(options);
        var entity = db.Model.FindEntityType(typeof(Permission));
        var columns = entity?.GetProperties().Select(property => property.Name).ToHashSet();
        check(entity is not null && entity.GetTableName() == "Permissions" &&
            columns is not null && columns.Contains(nameof(Permission.Id)) &&
            columns.Contains(nameof(Permission.UserId)) &&
            columns.Contains(nameof(Permission.ProjectId)) &&
            columns.Contains(nameof(Permission.ResourceType)) &&
            columns.Contains(nameof(Permission.ResourceIdentifier)) &&
            columns.Contains(nameof(Permission.AccessLevel)) &&
            columns.Contains(nameof(Permission.Status)) &&
            columns.Contains(nameof(Permission.RevokedAt)),
            "12G existing generic Permission table and grant identity reviewed");
        check(columns is not null && !columns.Contains("ConversationId") &&
            !columns.Contains("ToolId") && !columns.Contains("RequiredPermission") &&
            !columns.Contains("IsActive") &&
            entity!.FindProperty(nameof(Permission.ProjectId))!.IsNullable,
            "12G schema lacks exact conversation, tool, requirement and required project bindings");
        check(typeof(Permission).GetProperty(nameof(Permission.ResourceIdentifier))?.PropertyType ==
                typeof(string) &&
            typeof(Permission).GetProperty(nameof(Permission.AccessLevel))?.PropertyType ==
                typeof(string) &&
            typeof(Permission).GetProperty(nameof(Permission.Status))?.PropertyType ==
                typeof(string),
            "12G generic text fields cannot be reinterpreted as workflow permission bindings");

        var sourceTypes = typeof(AuraDbContext).Assembly.GetTypes()
            .Concat(typeof(IToolPermissionSource).Assembly.GetTypes())
            .Concat(typeof(RuntimeSafetyOptions).Assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract &&
                typeof(IToolPermissionSource).IsAssignableFrom(type)).ToArray();
        check(sourceTypes.SequenceEqual([typeof(UnavailableToolPermissionSource)]),
            "12G no production permission persistence adapter exists for incompatible schema");

        ToolIdentifier.TryCreate("permission-readiness", out var toolId);
        var request = new ToolPermissionValidationRequest(
            new ActionScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), toolId!,
            ToolPermissionRequirement.OwnedProjectRead);
        var unavailable = await new UnavailableToolPermissionSource()
            .GetAsync(request, CancellationToken.None);
        check(unavailable.Status == ToolPermissionLookupStatus.Unavailable &&
            unavailable.Grant is null,
            "12G default permission source returns no inferred or wildcard grant");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var propagated = false;
            try { await new UnavailableToolPermissionSource().GetAsync(request, cancelled.Token); }
            catch (OperationCanceledException) { propagated = true; }
            check(propagated, "12G unavailable permission source propagates cancellation");
        }

        var readiness = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var defaults = new RuntimeSafetyOptions();
        check(!readiness.Permission && !readiness.Ownership &&
            !defaults.ProductionAdaptersEnabled && !defaults.ToolExecutionEnabled &&
            !defaults.SafeFileAccessEnabled,
            "12G startup permission readiness and execution/file defaults remain disabled");
        check(!new PersistenceCapabilityReadiness().GetSnapshot().Permission,
            "12G informational persistence readiness does not claim permission support");
    }
}
