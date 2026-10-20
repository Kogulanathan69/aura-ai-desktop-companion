using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Application.Security;
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
        var workflow = Read("Aura.Application", "Executions", "ExecutionReconciliationService.cs");
        var document = Read("STEP-17F-RECONCILIATION-WRITER-PRODUCTION-REGISTRATION.md");
        var baseline = Read("STEP-15B-POSTGRESQL-MIGRATION-BASELINE-EVIDENCE.md");
        check(typeof(IExecutionReconciliationAssessmentWriter).IsAssignableFrom(
                typeof(ExecutionReconciliationAssessmentPersistenceWriter)) &&
            live.Contains("new ExecutionReconciliationAssessmentPersistenceWriter(factory)") &&
            live.Contains("assessmentWriter: writer") &&
            live.Contains("generatedActions.Contains(x.ActionId)"),
            "17F gated live path invokes real writer and cleans generated rows");
        check(live.Contains("initial with { Version = 0 }") &&
            live.Contains("var rows = await db.ExecutionReconciliationAssessments.AsNoTracking()") &&
            live.Contains("first.Evidence.Id.Value == second.Evidence.Id.Value") &&
            workflow.Contains("assessmentWriter.WriteAsync(evidence, cancellationToken)"),
            "17F stale snapshot, readback and repeated historical assessments are explicit");
        check(writer.Contains("evidence.ObservedVersion != 1") &&
            writer.Contains("SaveChangesAsync(cancellationToken)") &&
            writer.Contains("ExecuteUpdateAsync") && !writer.Contains("Task.Delay") &&
            writer.Contains("BeginTransactionAsync") &&
            writer.Contains("ActionExecutionStates.Where"),
            "17X writer locks the exact reserved version before advisory persistence without retry");
        check(document.Contains("LIVE RESULT: OPERATOR-REPORTED PASS") &&
            new[] { "DB CONNECTIVITY", "SCHEMA CHECK", "FIXTURE SETUP",
                "VALID RESERVED ASSESSMENT", "STALE VERSION", "TERMINAL STATE",
                "NEGATIVE BINDINGS", "REPEATED ASSESSMENT", "ASSESSMENT PERSISTENCE",
                "CLEANUP", "RESULT" }
                .All(label => document.Contains(label + ": PASS", StringComparison.Ordinal)) &&
            baseline.Contains("No live PostgreSQL run was performed here") &&
            document.Contains("concurrent completion") &&
            document.Contains("same-target applicability cannot be established") &&
            document.Contains("Reconciliation=false") &&
            !new RuntimeSafetyOptions().ProductionAdaptersEnabled &&
            !new RuntimeSafetyOptions().ToolExecutionEnabled,
            "17F reported writer PASS is bounded; baseline and concurrent freshness remain unproven");
        check(api.Contains("AddStateAuditReconciliationRuntimeFoundation") &&
            inspector.Contains("ReviewedReconciliationLiveEvidence") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Reconciliation &&
            api.Contains("new UnavailableFinalProductionSecurityApprovalSource()"),
            "17X exact writer path is registered but remains unready without fresh evidence");
        return Task.CompletedTask;
    }
}
