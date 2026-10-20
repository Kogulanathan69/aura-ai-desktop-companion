using Aura.Api.Security;
using Aura.Application.Auditing;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionRuntimeCompositionChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var services = new ServiceCollection();
        var empty = ProductionRuntimeCompositionInspector.Inspect(services);
        check(AllFalse(empty), "14E empty production registration is NOT READY");

        var informational = new PersistenceCapabilityReadiness().GetSnapshot();
        check(informational.Ownership && AllFalse(ProductionRuntimeCompositionInspector.Inspect(services)),
            "14E informational persistence readiness cannot inflate composition");

        // Descriptor presence, factories and default-deny implementations are not
        // an explicit reviewed production implementation in the inspector catalog.
        services.AddScoped<IToolPermissionSource, UnavailableToolPermissionSource>();
        services.AddScoped<IToolExecutionPolicy, DenyToolExecutionPolicy>();
        services.AddScoped<IAuditOutboxDispatcher>(_ => throw new InvalidOperationException());
        services.AddScoped<IToolHandler>(_ => throw new InvalidOperationException());
        check(AllFalse(ProductionRuntimeCompositionInspector.Inspect(services)),
            "14E unreviewed, unavailable, deny-only and factory registrations remain false");
        services.AddScoped<IToolPermissionSource, UnavailableToolPermissionSource>();
        check(!ProductionRuntimeCompositionInspector.Inspect(services).Permission,
            "14E ambiguous duplicate permission registrations cannot count");

        var all = new ProductionRuntimeCompositionSnapshot(true, true, true, true, true,
            true, true, true, true, true);
        check(all.CompositionComplete, "14E composition requires all ten independent capabilities");
        foreach (var (name, missing) in new (string, ProductionRuntimeCompositionSnapshot)[]
        {
            ("ownership", all with { Ownership = false }),
            ("permission", all with { Permission = false }),
            ("execution state", all with { ExecutionState = false }),
            ("audit sink", all with { AuditSink = false }),
            ("audit delivery", all with { AuditDelivery = false }),
            ("reconciliation", all with { Reconciliation = false }),
            ("verification", all with { Verification = false }),
            ("execution policy", all with { ExecutionPolicy = false }),
            ("tool handlers", all with { ToolHandlers = false }),
            ("trusted execution", all with { TrustedExecution = false })
        })
            check(!missing.CompositionComplete, "14E missing " + name + " blocks composition");

        var flags = new RuntimeSafetyOptions();
        check(!flags.ProductionAdaptersEnabled && !flags.ToolExecutionEnabled &&
            !flags.AuditPersistenceEnabled && !flags.SafeFileAccessEnabled &&
            !flags.CloudAiEnabled && AllFalse(ProductionRuntimeCompositionInspector.Inspect(services)),
            "14E configuration flags cannot replace reviewed services");
        var requested = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        check(requested.ToolExecutionEnabled &&
            AllFalse(ProductionRuntimeCompositionInspector.Inspect(services)),
            "14E requested production flags do not inflate observed composition");

        var api = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Aura.Api", "Program.cs"));
        var inspector = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
            "Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs"));
        check(api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal) &&
            api.Contains("ProductionRuntimeCompositionInspector.Inspect(builder.Services)", StringComparison.Ordinal) &&
            !api.Contains("new PersistenceCapabilityReadiness()", StringComparison.Ordinal),
            "14E API observes composition but retains unavailable runtime readiness");
        check(api.Contains("AddOwnershipRuntimeFoundation(", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<ITrustedToolExecutionService", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<IAuditOutboxDispatcher", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !api.Contains("AddHostedService", StringComparison.Ordinal),
            "14E no production executor, dispatcher, migration or worker registration");
        check(!inspector.Contains("GetRequiredService", StringComparison.Ordinal) &&
            !inspector.Contains("BuildServiceProvider", StringComparison.Ordinal) &&
            !inspector.Contains("GetEnvironmentVariable", StringComparison.Ordinal) &&
            !inspector.Contains("Assembly.", StringComparison.Ordinal) &&
            !inspector.Contains("Npgsql", StringComparison.Ordinal) &&
            !inspector.Contains("HttpClient", StringComparison.Ordinal) &&
            !inspector.Contains("File.", StringComparison.Ordinal) &&
            !inspector.Contains("Migrate", StringComparison.Ordinal) &&
            !inspector.Contains("ExecuteAsync", StringComparison.Ordinal),
            "14E inspector reads descriptors only without live, migration, tool, provider or file calls");
        check(AllFalse(ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection())) &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().Handler,
            "14E current production execution remains NOT READY");
        return Task.CompletedTask;
    }

    private static bool AllFalse(ProductionRuntimeCompositionSnapshot value) =>
        !value.Ownership && !value.Permission && !value.ExecutionState &&
        !value.AuditSink && !value.AuditDelivery && !value.Reconciliation &&
        !value.Verification && !value.ExecutionPolicy && !value.ToolHandlers &&
        !value.TrustedExecution && !value.CompositionComplete;
}
