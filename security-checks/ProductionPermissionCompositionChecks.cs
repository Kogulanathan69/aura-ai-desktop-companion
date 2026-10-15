using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionPermissionCompositionChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        static bool Ready(IServiceCollection services) =>
            ProductionRuntimeCompositionInspector.Inspect(services).Permission;

        check(!Ready(new ServiceCollection()), "14G empty registrations cannot assert permission");

        var unavailable = new ServiceCollection();
        unavailable.AddScoped<IToolPermissionSource, UnavailableToolPermissionSource>();
        check(!Ready(unavailable), "14G unavailable permission source is not production-ready");

        var exact = new ServiceCollection();
        exact.AddScoped<IToolPermissionSource, ToolPermissionPersistenceSource>();
        check(!Ready(exact), "14G exact scoped source without reviewed catalog entry remains false");
        exact.AddScoped<IToolPermissionSource, ToolPermissionPersistenceSource>();
        check(!Ready(exact), "14G duplicate permission registrations remain false");

        var factory = new ServiceCollection();
        factory.AddScoped<IToolPermissionSource>(_ => throw new InvalidOperationException());
        check(!Ready(factory), "14G permission factory registration remains false");
        var instance = new ServiceCollection();
        instance.AddSingleton<IToolPermissionSource>(new UnavailableToolPermissionSource());
        check(!Ready(instance), "14G permission instance registration remains false");
        var singleton = new ServiceCollection();
        singleton.AddSingleton<IToolPermissionSource, ToolPermissionPersistenceSource>();
        check(!Ready(singleton), "14G singleton exact source remains false");
        var transient = new ServiceCollection();
        transient.AddTransient<IToolPermissionSource, ToolPermissionPersistenceSource>();
        check(!Ready(transient), "14G transient exact source remains false");

        var catalog = typeof(ProductionRuntimeCompositionInspector).GetField("ReviewedPermission",
            BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IReadOnlySet<Type>;
        check(catalog is { Count: 0 } && !Ready(new ServiceCollection()),
            "14G reviewed catalog is empty and a catalog entry alone could not replace DI");

        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var source = Read("Aura.Infrastructure", "Data", "ToolPermissionPersistenceSource.cs");
        var grant = Read("Aura.Domain", "Entities", "ToolPermissionGrantRecord.cs");
        var legacy = Read("Aura.Domain", "Entities", "Permission.cs");
        var api = Read("Aura.Api", "Program.cs");

        check(inspector.Contains("registrations.Length == 1", StringComparison.Ordinal) &&
            inspector.Contains("ServiceLifetime.Scoped", StringComparison.Ordinal) &&
            inspector.Contains("ImplementationType == typeof(ToolPermissionPersistenceSource)",
                StringComparison.Ordinal) &&
            inspector.Contains("ReviewedPermission.Contains(typeof(ToolPermissionPersistenceSource))",
                StringComparison.Ordinal),
            "14G permission requires unique scoped exact implementation and independent review");
        check(source.Contains("db.ToolPermissionGrants.AsNoTracking()", StringComparison.Ordinal) &&
            !source.Contains("db.Permissions", StringComparison.Ordinal) &&
            !source.Contains("ResourceType", StringComparison.Ordinal) &&
            !source.Contains("ApprovalId", StringComparison.Ordinal) &&
            grant.Contains("ConversationId", StringComparison.Ordinal) &&
            grant.Contains("ToolId", StringComparison.Ordinal) &&
            legacy.Contains("ResourceType", StringComparison.Ordinal) &&
            !legacy.Contains("ConversationId", StringComparison.Ordinal),
            "14G generic Permissions and approval cannot substitute for exact tool grants");
        check(source.Contains("grant.UserId == request.Scope.UserId", StringComparison.Ordinal) &&
            source.Contains("grant.ConversationId == request.Scope.ConversationId", StringComparison.Ordinal) &&
            source.Contains("grant.ProjectId == request.Scope.ProjectId", StringComparison.Ordinal) &&
            source.Contains("grant.ToolId == requestedTool.Value", StringComparison.Ordinal) &&
            source.Contains("grant.RequiredPermission == (int)request.Requirement", StringComparison.Ordinal) &&
            source.Contains("grant.Status == ToolPermissionGrantStatus.Active", StringComparison.Ordinal) &&
            source.Contains("grant.RevokedAt == null", StringComparison.Ordinal) &&
            source.Contains(".Take(2)", StringComparison.Ordinal) &&
            !source.Contains("FirstOrDefault", StringComparison.Ordinal),
            "14G exact grant tuple, active state and duplicate rejection remain required");
        check(source.Contains("request.Scope.UserId == Guid.Empty", StringComparison.Ordinal) &&
            source.Contains("request.Scope.ConversationId == Guid.Empty", StringComparison.Ordinal) &&
            source.Contains("request.Scope.ProjectId == Guid.Empty", StringComparison.Ordinal) &&
            source.Contains("!Enum.IsDefined(request.Requirement)", StringComparison.Ordinal) &&
            source.Contains("row.Id == Guid.Empty", StringComparison.Ordinal) &&
            source.Contains("ToolIdentifier.TryCreate(row.ToolId", StringComparison.Ordinal) &&
            source.Contains("TryMapRequirement(row.RequiredPermission", StringComparison.Ordinal),
            "14G malformed scope, requirement, stored tool and empty grant remain denied");

        var ownershipOnly = new ProductionRuntimeCompositionSnapshot(true, false, false, false,
            false, false, false, false, false, false);
        var approvalOrState = new ProductionRuntimeCompositionSnapshot(false, false, true, false,
            false, false, false, false, false, false);
        check(!ownershipOnly.Permission && !ownershipOnly.CompositionComplete &&
            !approvalOrState.Permission && !approvalOrState.CompositionComplete &&
            !Ready(new ServiceCollection()),
            "14G ownership, approval evidence and execution state cannot infer permission");
        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        check(flags.ToolExecutionEnabled && !Ready(new ServiceCollection()) &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().Permission,
            "14G configuration flags and informational readiness cannot assert permission");
        check(!api.Contains("AddScoped<IToolPermissionSource", StringComparison.Ordinal) &&
            !api.Contains("AddSingleton<IToolPermissionSource", StringComparison.Ordinal) &&
            !api.Contains("AddTransient<IToolPermissionSource", StringComparison.Ordinal) &&
            !api.Contains("ToolPermissionPersistenceSource", StringComparison.Ordinal) &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal),
            "14G normal API has no production permission registration or readiness claim");
        check(!api.Contains("AddScoped<ITrustedToolExecutionService", StringComparison.Ordinal) &&
            !api.Contains("AddHostedService", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !inspector.Contains("GetRequiredService", StringComparison.Ordinal) &&
            !inspector.Contains("BuildServiceProvider", StringComparison.Ordinal) &&
            !inspector.Contains("ExecuteAsync", StringComparison.Ordinal) &&
            !inspector.Contains("HttpClient", StringComparison.Ordinal) &&
            !inspector.Contains("File.", StringComparison.Ordinal),
            "14G adds no live DB, migration, network, file, tool or worker path");
        check(!ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).CompositionComplete &&
            !new RuntimeSafetyOptions().ToolExecutionEnabled,
            "14G production execution remains NOT READY");
        return Task.CompletedTask;
    }
}
