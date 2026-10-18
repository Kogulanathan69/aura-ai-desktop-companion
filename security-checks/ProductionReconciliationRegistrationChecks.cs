using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionReconciliationRegistrationChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var api = Read("Aura.Api", "Program.cs");
        var live = Read("security-checks", "LiveProductionReconciliationEvidence.cs");
        var writer = Read("Aura.Infrastructure", "Services",
            "ExecutionReconciliationAssessmentPersistenceWriter.cs");
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        check(typeof(IExecutionReconciliationAssessmentWriter).IsAssignableFrom(
                typeof(ExecutionReconciliationAssessmentPersistenceWriter)) &&
            live.Contains("new ExecutionReconciliationAssessmentPersistenceWriter(factory)") &&
            live.Contains("assessmentWriter: writer") &&
            live.Contains("generatedActions.Contains(x.ActionId)"),
            "17F gated live path invokes real writer and cleans generated rows");
        check(writer.Contains("evidence.ObservedVersion != 1") &&
            writer.Contains("SaveChangesAsync(cancellationToken)") &&
            !writer.Contains("ExecuteUpdateAsync") && !writer.Contains("Task.Delay"),
            "17F writer persists bounded advisory evidence without state mutation or retry");
        check(!api.Contains("AddScoped<IExecutionReconciliationSource") &&
            !api.Contains("AddScoped<IExecutionReconciliationAssessmentWriter") &&
            inspector.Contains("ReviewedReconciliation = new HashSet<Type>()") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Reconciliation,
            "17F unproven writer path leaves reconciliation unregistered and unready");
        return Task.CompletedTask;
    }
}
