using Aura.Api.Security;
using Aura.Application.Security;
using Aura.Infrastructure.Data;


internal static class PersistenceHardeningReadinessChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        var runtime =
            new UnavailableRuntimeCapabilityReadiness()
                .GetSnapshot();

        var persistence =
            new PersistenceCapabilityReadiness()
                .GetSnapshot();

        var safety =
            new RuntimeSafetyOptions();

        check(
            !runtime.AuditSink &&
            !runtime.Reconciliation &&
            !runtime.Verification &&
            !runtime.ExecutionState,
            "13F runtime security persistence capabilities remain unavailable");

        check(
            !persistence.AuditSink &&
            !persistence.Reconciliation &&
            !persistence.Verification &&
            !persistence.ExecutionState,
            "13F persistence readiness remains false pending live database verification");

        check(
            !safety.ProductionAdaptersEnabled,
            "13F production adapters remain disabled");

        check(
            !safety.ToolExecutionEnabled,
            "13F tool execution remains disabled");

        check(
            !safety.SafeFileAccessEnabled,
            "13F Safe File Access remains disabled");

        var root = Directory.GetCurrentDirectory();

        var apiProgram = File.ReadAllText(Path.Combine(
            root,
            "Aura.Api",
            "Program.cs"));

        check(
            !apiProgram.Contains(
                "SecurityAuditPersistenceSink",
                StringComparison.Ordinal) &&
            !apiProgram.Contains(
                "ExecutionReconciliationPersistenceSource",
                StringComparison.Ordinal),
            "13F persistence adapters remain absent from production dependency injection");

        return Task.CompletedTask;
    }
}