using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionExecutionStateCompositionChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        static bool Ready(IServiceCollection services) =>
            ProductionRuntimeCompositionInspector.Inspect(services).ExecutionState;

        check(!Ready(new ServiceCollection()), "14H empty registrations cannot assert execution state");
        var unavailable = new ServiceCollection();
        unavailable.AddScoped<IActionExecutionStateStore, UnavailableActionExecutionStateStore>();
        check(!Ready(unavailable), "14H unavailable state store is not production-ready");

        var exact = new ServiceCollection();
        exact.AddScoped<IActionExecutionStateStore, ActionExecutionStatePersistenceStore>();
        check(!Ready(exact), "14H exact scoped durable store without reviewed catalog remains false");
        exact.AddScoped<IActionExecutionStateStore, ActionExecutionStatePersistenceStore>();
        check(!Ready(exact), "14H duplicate state-store registrations remain false");

        var factory = new ServiceCollection();
        factory.AddScoped<IActionExecutionStateStore>(_ => throw new InvalidOperationException());
        check(!Ready(factory), "14H state-store factory registration remains false");
        var instance = new ServiceCollection();
        instance.AddSingleton<IActionExecutionStateStore>(new UnavailableActionExecutionStateStore());
        check(!Ready(instance), "14H state-store instance registration remains false");
        var singleton = new ServiceCollection();
        singleton.AddSingleton<IActionExecutionStateStore, ActionExecutionStatePersistenceStore>();
        check(!Ready(singleton), "14H singleton durable store remains false");
        var transient = new ServiceCollection();
        transient.AddTransient<IActionExecutionStateStore, ActionExecutionStatePersistenceStore>();
        check(!Ready(transient), "14H transient durable store remains false");

        var catalog = typeof(ProductionRuntimeCompositionInspector).GetField("ReviewedExecutionState",
            BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IReadOnlySet<Type>;
        check(catalog is { Count: 0 } && !Ready(new ServiceCollection()),
            "14H catalog is empty and catalog evidence alone cannot replace DI");

        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var store = Read("Aura.Infrastructure", "Services", "ActionExecutionStatePersistenceStore.cs");
        var entity = Read("Aura.Domain", "Entities", "ActionExecutionStateRecord.cs");
        var migration = Read("Aura.Infrastructure", "Data", "Migrations",
            "20261014000001_AddActionExecutionStates.cs");
        var api = Read("Aura.Api", "Program.cs");

        check(inspector.Contains("registrations.Length == 1", StringComparison.Ordinal) &&
            inspector.Contains("ImplementationType == typeof(ActionExecutionStatePersistenceStore)",
                StringComparison.Ordinal) &&
            inspector.Contains("ReviewedExecutionState.Contains(typeof(ActionExecutionStatePersistenceStore))",
                StringComparison.Ordinal),
            "14H one scoped exact reviewed implementation is required independently of source presence");
        check(entity.Contains("ActionId", StringComparison.Ordinal) &&
            entity.Contains("UserId", StringComparison.Ordinal) &&
            entity.Contains("ConversationId", StringComparison.Ordinal) &&
            entity.Contains("ProjectId", StringComparison.Ordinal) &&
            entity.Contains("ApprovalId", StringComparison.Ordinal) &&
            entity.Contains("ToolId", StringComparison.Ordinal) &&
            entity.Contains("Version", StringComparison.Ordinal) &&
            entity.Contains("ExecutionId", StringComparison.Ordinal) &&
            migration.Contains("IX_ActionExecutionStates_ApprovalId", StringComparison.Ordinal) &&
            migration.Contains("IX_ActionExecutionStates_ExecutionId", StringComparison.Ordinal) &&
            !Ready(new ServiceCollection()),
            "14H entity and migration bindings or indexes do not assert runtime readiness");
        check(store.Contains("BeginTransactionAsync", StringComparison.Ordinal) &&
            store.Contains("ExecuteUpdateAsync", StringComparison.Ordinal) &&
            store.Contains("x.ActionId ==", StringComparison.Ordinal) &&
            store.Contains("x.UserId ==", StringComparison.Ordinal) &&
            store.Contains("x.ConversationId ==", StringComparison.Ordinal) &&
            store.Contains("x.ProjectId ==", StringComparison.Ordinal) &&
            store.Contains("x.ToolId ==", StringComparison.Ordinal) &&
            store.Contains("x.ApprovalId ==", StringComparison.Ordinal) &&
            store.Contains("x.ExecutionId ==", StringComparison.Ordinal) &&
            store.Contains("x.Version ==", StringComparison.Ordinal) &&
            store.Contains("x.ApprovalConsumed", StringComparison.Ordinal),
            "14H existing store retains exact transactional CAS and completion predicates");
        check(!store.Contains("while (", StringComparison.Ordinal) &&
            !store.Contains("Task.Delay", StringComparison.Ordinal) &&
            !store.Contains("MigrateAsync", StringComparison.Ordinal) &&
            store.Contains("catch (OperationCanceledException)", StringComparison.Ordinal),
            "14H existing store has no hidden retry, migration or cancellation fallback");

        var partial = new ProductionRuntimeCompositionSnapshot(true, true, false, true, true,
            true, true, true, true, true);
        check(!partial.ExecutionState && !partial.CompositionComplete &&
            !Ready(new ServiceCollection()),
            "14H ownership, permission, approval and other capabilities cannot infer state readiness");
        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        check(flags.ToolExecutionEnabled && !Ready(new ServiceCollection()) &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().ExecutionState,
            "14H flags and informational live evidence cannot substitute for production state DI");
        check(!api.Contains("AddScoped<IActionExecutionStateStore", StringComparison.Ordinal) &&
            !api.Contains("AddSingleton<IActionExecutionStateStore", StringComparison.Ordinal) &&
            !api.Contains("AddTransient<IActionExecutionStateStore", StringComparison.Ordinal) &&
            !api.Contains("ActionExecutionStatePersistenceStore", StringComparison.Ordinal) &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal),
            "14H normal API has no production state-store registration or readiness claim");
        check(!api.Contains("AddScoped<ITrustedToolExecutionService", StringComparison.Ordinal) &&
            !api.Contains("AddHostedService", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !inspector.Contains("GetRequiredService", StringComparison.Ordinal) &&
            !inspector.Contains("BuildServiceProvider", StringComparison.Ordinal) &&
            !inspector.Contains("ExecuteAsync", StringComparison.Ordinal) &&
            !inspector.Contains("HttpClient", StringComparison.Ordinal) &&
            !inspector.Contains("File.", StringComparison.Ordinal),
            "14H introduces no live DB, migration, network, file, tool or worker path");
        check(!ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).CompositionComplete &&
            !new RuntimeSafetyOptions().ProductionAdaptersEnabled &&
            !new RuntimeSafetyOptions().ToolExecutionEnabled &&
            !new RuntimeSafetyOptions().AuditPersistenceEnabled &&
            !new RuntimeSafetyOptions().SafeFileAccessEnabled &&
            !new RuntimeSafetyOptions().CloudAiEnabled,
            "14H production execution and optional capabilities remain disabled");
        return Task.CompletedTask;
    }
}
