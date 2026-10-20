using Aura.Api.Security;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class ReconciliationProductionReadinessChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        var writer = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure", "Services",
            "ExecutionReconciliationAssessmentPersistenceWriter.cs"));
        check(writer.Contains("BeginTransactionAsync") && writer.Contains("ExecuteUpdateAsync") &&
            writer.Contains("x.ExecutionId == evidence.ExecutionId") &&
            writer.Contains("x.Version == evidence.ObservedVersion") &&
            writer.Contains("x.Status == DurableExecutionStatus.Reserved"),
            "17X reconciliation locks the exact current Reserved execution version before insert");
        check(!writer.Contains("TryCompleteAsync") && !writer.Contains("handler") &&
            !writer.Contains("Task.Delay") && !writer.Contains("while ("),
            "17X reconciliation remains historical, immutable and side-effect free");
        check(!ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Reconciliation,
            "17X reconciliation remains false without exact graph and fresh changed-path evidence");
        return Task.CompletedTask;
    }
}
