using Aura.Api.Security;
using Aura.Application.Security;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionCompositionImplementationPlanChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var current = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        var flags = new RuntimeSafetyOptions();
        var readiness = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        check(!current.Ownership && !current.Permission && !current.ExecutionState &&
            !current.AuditSink && !current.AuditDelivery && !current.Reconciliation &&
            !current.Verification && !current.ExecutionPolicy && !current.ToolHandlers &&
            !current.TrustedExecution && !current.CompositionComplete,
            "16A current composition stays all false before production registration");
        foreach (var partial in new[] {
            current with { Ownership = true },
            current with { Ownership = true, Permission = true },
            current with { Ownership = true, Permission = true, ExecutionState = true },
            current with { Ownership = true, Permission = true, ExecutionState = true,
                AuditSink = true, AuditDelivery = true } })
            check(!partial.TrustedExecution && !partial.ExecutionPolicy &&
                !partial.ToolHandlers && !partial.CompositionComplete,
                "16A foundation registration cannot infer later capabilities or activation");
        check(!flags.ProductionAdaptersEnabled && !flags.ToolExecutionEnabled &&
            !flags.AuditPersistenceEnabled && !flags.SafeFileAccessEnabled &&
            !flags.CloudAiEnabled && !readiness.Ownership && !readiness.Permission,
            "16A flags and runtime assertions remain unavailable");
        var root = Directory.GetCurrentDirectory();
        var api = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        var inspector = File.ReadAllText(Path.Combine(root, "Aura.Api", "Security",
            "ProductionRuntimeCompositionInspector.cs"));
        check(!api.Contains("ITrustedToolExecutionService") &&
            !api.Contains("AddScoped<IToolHandler") && !api.Contains("AddScoped<IToolExecutionPolicy") &&
            !api.Contains("MigrateAsync(") && !api.Contains("EnsureCreated(") &&
            !api.Contains("AddHostedService") &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()") &&
            inspector.Contains("typeof(HttpCurrentUserContext)") &&
            inspector.Contains("typeof(ToolPermissionPersistenceSource)") &&
            inspector.Contains("ReviewedExecutionStateLiveEvidence") &&
            inspector.Contains("ReviewedAuditSinkLiveEvidence") &&
            inspector.Contains("ReviewedAuditDeliveryLiveEvidence"),
            "16A startup has no activation path; registration without reviewed catalogs is not readiness");
        return Task.CompletedTask;
    }
}
