using Aura.Api.Security;
using Aura.Application.Security;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionActivationGateChecks
{
    private sealed class SyntheticApproval : IFinalProductionSecurityApprovalSource
    {
        public bool IsApproved => true;
    }

    internal static Task RunAsync(Action<bool, string> check)
    {
        var gate = new ProductionActivationGate();
        var full = new ProductionRuntimeCompositionSnapshot(true, true, true, true, true,
            true, true, true, true, true);
        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        var runtime = new RuntimeCapabilitySnapshot(true, true, true, true, true, true,
            true, true, false);
        bool Active(ProductionRuntimeCompositionSnapshot c, RuntimeSafetyOptions f,
            RuntimeCapabilitySnapshot r, RuntimeSafetyStatus s,
            IFinalProductionSecurityApprovalSource a, bool cloud = false) =>
            gate.CanActivate(c, f, r, s, a, cloud);
        check(Active(full, flags, runtime, RuntimeSafetyStatus.Valid, new SyntheticApproval()),
            "16J synthetic all-true formula is satisfiable in offline checks only");
        var unavailable = new UnavailableFinalProductionSecurityApprovalSource();
        check(!unavailable.IsApproved &&
            !Active(full, flags, runtime, RuntimeSafetyStatus.Valid, unavailable) &&
            !Active(ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()),
                new RuntimeSafetyOptions(),
                new UnavailableRuntimeCapabilityReadiness().GetSnapshot(),
                RuntimeSafetyStatus.SafeDisabled, unavailable),
            "16J default approval and actual startup snapshot deny activation");
        foreach (var missing in new[] {
            full with { Ownership = false }, full with { Permission = false },
            full with { ExecutionState = false }, full with { AuditSink = false },
            full with { AuditDelivery = false }, full with { Reconciliation = false },
            full with { Verification = false }, full with { ExecutionPolicy = false },
            full with { ToolHandlers = false }, full with { TrustedExecution = false } })
            check(!Active(missing, flags, runtime, RuntimeSafetyStatus.Valid, new SyntheticApproval()),
                "16J each missing composition field denies activation");
        foreach (var missing in new[] {
            runtime with { Ownership = false }, runtime with { Permission = false },
            runtime with { ExecutionState = false }, runtime with { AuditSink = false },
            runtime with { Reconciliation = false }, runtime with { Verification = false },
            runtime with { Handler = false }, runtime with { ExecutionPolicy = false } })
            check(!Active(full, flags, missing, RuntimeSafetyStatus.Valid, new SyntheticApproval()),
                "16J each missing applicable runtime field denies activation");
        foreach (var missing in new[] {
            new RuntimeSafetyOptions { ToolExecutionEnabled = true, AuditPersistenceEnabled = true },
            new RuntimeSafetyOptions { ProductionAdaptersEnabled = true, AuditPersistenceEnabled = true },
            new RuntimeSafetyOptions { ProductionAdaptersEnabled = true, ToolExecutionEnabled = true } })
            check(!Active(full, missing, runtime, RuntimeSafetyStatus.Valid, new SyntheticApproval()),
                "16J each missing required production flag denies activation");
        check(!Active(full, flags, runtime, RuntimeSafetyStatus.SafeDisabled, new SyntheticApproval()) &&
            !Active(full, flags, runtime, RuntimeSafetyStatus.MissingRequiredAdapter, new SyntheticApproval()) &&
            !Active(full, flags, runtime, RuntimeSafetyStatus.Valid, unavailable),
            "16J safety result and independent approval are required");
        check(!Active(full, new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
                ToolExecutionEnabled = true, AuditPersistenceEnabled = true,
                SafeFileAccessEnabled = true }, runtime, RuntimeSafetyStatus.Valid,
                new SyntheticApproval()) &&
            !Active(full, new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
                ToolExecutionEnabled = true, AuditPersistenceEnabled = true,
                CloudAiEnabled = true }, runtime, RuntimeSafetyStatus.Valid,
                new SyntheticApproval()),
            "16J optional file and cloud switches require independent readiness");
        var api = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Aura.Api", "Program.cs"));
        check(api.Contains("runtimeSafetyOptions.ToolExecutionEnabled && !productionActivationGate.CanActivate(") &&
            api.Contains("new UnavailableFinalProductionSecurityApprovalSource()") &&
            !api.Contains("AddScoped<ITrustedToolExecutionService") &&
            !api.Contains("AddScoped<IToolHandler") &&
            !api.Contains("handler.ExecuteAsync(") &&
            !api.Contains("MigrateAsync(") && !api.Contains("AddHostedService"),
            "16J startup enforces unavailable gate and exposes no trusted executor or handler path");
        return Task.CompletedTask;
    }
}
