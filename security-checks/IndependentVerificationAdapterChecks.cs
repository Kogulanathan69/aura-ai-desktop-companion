using Aura.Api.Security;
using Aura.Application.Security;
using Aura.Application.Verifications;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data.Migrations;
using Aura.Infrastructure.Services;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

internal static class IndependentVerificationAdapterChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var source = Read("Aura.Infrastructure", "Services", "OwnedProjectMetadataExecutionEvidenceSource.cs");
        var workflow = Read("Aura.Application", "Verifications", "VerificationWorkflowService.cs");
        var writer = Read("Aura.Infrastructure", "Services", "ExecutionVerificationAssessmentPersistenceWriter.cs");
        var api = Read("Aura.Api", "Program.cs");
        var migration = new AddIndependentVerificationOutcome();
        var outcomeReview = Read("STEP-17Q-INDEPENDENT-VERIFICATION-OUTCOME-MIGRATION-AND-PERSISTENCE.md");
        check(typeof(OwnedProjectMetadataExecutionEvidenceSource).IsSealed &&
            typeof(IExecutionEvidenceSource).IsAssignableFrom(typeof(OwnedProjectMetadataExecutionEvidenceSource)) &&
            source.Contains("ExpectedTool = \"owned-project-metadata-read\"") &&
            source.Contains("IsolationLevel.RepeatableRead") &&
            source.Contains("AsNoTracking()") && source.Contains("x.Id == scope.ProjectId && x.UserId == scope.UserId") &&
            source.Contains(".Select(x => new { x.Id, x.UpdatedAt })") &&
            !source.Contains("SaveChanges") && !source.Contains("ExecuteUpdate") &&
            !source.Contains("HttpClient") && !source.Contains("File.") &&
            !source.Contains("Task.Delay") && !source.Contains("while (") &&
            !source.Contains("handler", StringComparison.OrdinalIgnoreCase),
            "17P sealed source independently reads only bounded owned project state");
        check(source.Contains("x.ActionId == actionId.Value") &&
            source.Contains("x.UserId == scope.UserId") &&
            source.Contains("x.ConversationId == scope.ConversationId") &&
            source.Contains("x.ProjectId == scope.ProjectId") &&
            source.Contains("x.ToolId == ExpectedTool") &&
            source.Contains("x.ExecutionId == executionId") &&
            source.Contains("x.ApprovalId != Guid.Empty") &&
            source.Contains("x.Version == 2") &&
            source.Contains("x.Id == state.ApprovalId") &&
            source.Contains("ExecutionVersion = state.Version") &&
            source.Contains("VerificationProofSource.OwnedProjectMetadata, 1") &&
            source.Contains("catch (OperationCanceledException)") &&
            source.Contains("catch (Exception) { return null; }"),
            "17P source binds exact execution, ownership, approval, proof and version safely");
        check(workflow.Contains("executions.GetAsync(scope, action.Id, request.ExecutionId") &&
            workflow.Contains("execution.ExecutionVersion != 2") &&
            workflow.Contains("proof.ProjectId != scope.ProjectId") &&
            workflow.Contains("proof.ProofSource != VerificationProofSource.OwnedProjectMetadata") &&
            workflow.Contains("proof.ProofVersion != 1") &&
            workflow.Contains("execution.Outcome != ExecutionReportOutcome.Succeeded") &&
            new ConservativeVerificationPolicy().EvaluateAsync(
                new(Guid.NewGuid(), null!, null!, null!, Guid.NewGuid(),
                    ExecutionReportOutcome.Succeeded, DateTime.UtcNow),
                CancellationToken.None).Result == VerificationOutcome.Inconclusive,
            "17P wrong proof/version and handler success alone cannot become Verified");
        check(migration.UpOperations.Count == 2 &&
            migration.UpOperations[0] is DropCheckConstraintOperation &&
            migration.UpOperations[1] is AddCheckConstraintOperation added &&
            added.Sql == AddIndependentVerificationOutcome.RevisedState &&
            added.Sql.Contains("\"ExecutionStatus\" = 2 AND \"Outcome\" = 0") &&
            added.Sql.Contains("\"ProofSource\" = 1 AND \"ProofVersion\" = 1") &&
            migration.DownOperations.Count == 2 &&
            migration.DownOperations[1] is AddCheckConstraintOperation prior &&
            prior.Sql.Contains("\"Outcome\" IN (1, 2)") &&
            !prior.Sql.Contains("\"Outcome\" = 0"),
            "17P pending migration changes only bounded Verified check constraint");
        check(writer.Contains("evidence.Outcome is not (VerificationOutcome.Failed or VerificationOutcome.Inconclusive)") &&
            writer.Contains("ProofSource = VerificationProofSource.None") &&
            !api.Contains("AddScoped<IExecutionEvidenceSource,") &&
            !api.Contains("AddScoped<IExecutionVerificationAssessmentWriter,") &&
            !api.Contains("AddScoped<IVerificationWorkflowService,") &&
            !api.Contains("MigrateAsync(") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Verification,
            "17P writer remains advisory-only and production graph unregistered while migration pending");
        check(ProductionMigrationBaselineEvidence.ExpectedMigrations.Length == 7 &&
            outcomeReview.Contains("OPERATOR-REPORTED LIVE RESULT: PASS") &&
            outcomeReview.Contains("Verification=false") &&
            outcomeReview.Contains("Verified writer path is incomplete"),
            "17Q seven-ID live schema evidence and pending Verified writer remain fail-closed");
        var flags = new RuntimeSafetyOptions();
        var snapshot = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        check(!snapshot.Verification && !snapshot.ToolHandlers && !snapshot.ExecutionPolicy &&
            !snapshot.TrustedExecution && !snapshot.CompositionComplete &&
            !flags.ProductionAdaptersEnabled && !flags.ToolExecutionEnabled &&
            !flags.AuditPersistenceEnabled && !flags.SafeFileAccessEnabled &&
            !flags.CloudAiEnabled &&
            !new UnavailableFinalProductionSecurityApprovalSource().IsApproved,
            "17P pending proof path does not enable runtime or activation");
        return Task.CompletedTask;
    }
}
