using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionReconciliationPersistenceWriterChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Source(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var writer = Source("Aura.Infrastructure", "Services", "ExecutionReconciliationAssessmentPersistenceWriter.cs");
        var workflow = Source("Aura.Application", "Executions", "ExecutionReconciliationService.cs");
        var api = Source("Aura.Api", "Program.cs");
        var inspector = Source("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        check(typeof(ExecutionReconciliationAssessmentPersistenceWriter).IsSealed &&
            typeof(IExecutionReconciliationAssessmentWriter).GetMethods().Length == 1 &&
            writer.Contains("db.ExecutionReconciliationAssessments.Add") &&
            writer.Contains("db.SaveChangesAsync(cancellationToken)") &&
            writer.Contains("evidence.ObservedVersion != 1") &&
            writer.Contains("evidence.ObservedStatus != ActionExecutionStateStatus.Reserved") &&
            writer.Contains("Id = evidence.Id.Value") &&
            writer.Contains("ActionId = evidence.ActionId.Value") &&
            writer.Contains("UserId = evidence.Scope.UserId") &&
            writer.Contains("ConversationId = evidence.Scope.ConversationId") &&
            writer.Contains("ProjectId = evidence.Scope.ProjectId") &&
            writer.Contains("ToolId = evidence.ToolId.Value") &&
            writer.Contains("ApprovalId = evidence.ApprovalId") &&
            writer.Contains("ExecutionId = evidence.ExecutionId") &&
            writer.Contains("AssessedAt = evidence.IssuedAt"),
            "16F writer maps exact bounded historical assessment fields");
        check(!writer.Contains("ActionExecutionStates.Add") &&
            !writer.Contains("TryCompleteAsync") && writer.Contains("ExecuteUpdateAsync") &&
            writer.Contains("x => x.Version") &&
            !writer.Contains("while (") && !writer.Contains("Task.Delay") &&
            writer.Contains("catch (OperationCanceledException)") &&
            writer.Contains("catch (Exception) { return false; }") &&
            workflow.Contains("assessmentWriter.WriteAsync(evidence, cancellationToken)") &&
            workflow.Contains("ExecutionReconciliationStatus.Failed"),
            "16F persistence is opt-in, cancellation-aware, bounded and cannot mutate execution state");
        check(api.Contains("AddStateAuditReconciliationRuntimeFoundation") &&
            inspector.Contains("ReviewedReconciliationLiveEvidence") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Reconciliation,
            "17X registered writer/source remain gated by fresh production evidence");
        return Task.CompletedTask;
    }
}
