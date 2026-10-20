using Aura.Api.Security;
using Aura.Application.Verifications;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class VerificationConcurrencyAndRegistrationChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var writer = Read("Aura.Infrastructure", "Services",
            "ExecutionVerificationAssessmentPersistenceWriter.cs");
        var harness = Read("security-checks", "LiveIndependentVerificationEvidence.cs");
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var api = Read("Aura.Api", "Program.cs");
        var review = Read("STEP-17S-VERIFICATION-CONCURRENCY-DI-CATALOG.md");
        var project = Read("Aura.Application", "Projects", "Services", "ProjectService.cs");
        var configuration = Read("Aura.Infrastructure", "Data", "Configurations",
            "ProjectConfiguration.cs");
        var workflow = Read("Aura.Application", "Verifications", "VerificationWorkflowService.cs");
        check(writer.Contains("FOR UPDATE", StringComparison.Ordinal) &&
            writer.Contains("command.Transaction = transaction.GetDbTransaction()", StringComparison.Ordinal) &&
            writer.Contains("@userId FOR UPDATE", StringComparison.Ordinal) &&
            writer.Contains("reader.GetDateTime(0) != evidence.IndependentProof!.ProjectUpdatedAt", StringComparison.Ordinal),
            "17S writer locks exact owned project and rejects timestamp mismatch");
        check(writer.Contains("x.Version == executionVersion && x.ApprovalConsumed", StringComparison.Ordinal) &&
            writer.Contains("x.ExecutionId == evidence.ExecutionId", StringComparison.Ordinal) &&
            writer.Contains("state.Status != DurableExecutionStatus.Succeeded", StringComparison.Ordinal) &&
            !writer.Contains("ActionExecutionStates.ExecuteUpdateAsync", StringComparison.Ordinal) &&
            !writer.Contains("Task.Delay", StringComparison.Ordinal),
            "17S writer retains terminal exact binding and no execution mutation or retry");
        check(harness.Contains("CONCURRENT PROJECT UPDATE", StringComparison.Ordinal) &&
            harness.Contains("UpdatedProjectProofAsync", StringComparison.Ordinal) &&
            harness.Contains("if (await writer.WriteAsync(stale, 2, token)) return checks", StringComparison.Ordinal) &&
            harness.Contains("checks[3] = await writer.WriteAsync(current, 2, token)", StringComparison.Ordinal) &&
            harness.Contains("result[25] = rowsGone && fixturesGone", StringComparison.Ordinal),
            "17S live category demands stale rejection, fresh persistence and cleanup");
        check(review.Contains("PASS for all 26 categories", StringComparison.Ordinal) &&
            review.Contains("CONCURRENT PROJECT UPDATE: PASS", StringComparison.Ordinal) &&
            review.Contains("RESULT: PASS", StringComparison.Ordinal) &&
            review.Contains("OID `24576`", StringComparison.Ordinal) &&
            review.Contains("7696813047573034172", StringComparison.Ordinal),
            "17S records bounded operator-reported same-target 26-category PASS");
        check(project.Contains("project.UpdatedAt = _dateTimeProvider.UtcNow", StringComparison.Ordinal) &&
            configuration.Contains("builder.Property(x => x.UpdatedAt)", StringComparison.Ordinal) &&
            configuration.Contains("IsConcurrencyToken", StringComparison.Ordinal) &&
            review.Contains("not a database-managed monotonic version", StringComparison.Ordinal),
            "17S timestamp limitation is addressed by subsequent project version configuration");
        check(workflow.Contains("IIndependentVerificationPolicy policy", StringComparison.Ordinal) &&
            workflow.Contains("ToolRegistry tools", StringComparison.Ordinal) &&
            !workflow.Contains("IExecutionVerificationAssessmentWriter", StringComparison.Ordinal) &&
            review.Contains("without invoking the writer", StringComparison.Ordinal),
            "17S incomplete policy and end-to-end writer path block production DI");

        static bool Ready(IServiceCollection services) =>
            ProductionRuntimeCompositionInspector.Inspect(services).Verification;
        var completeShape = new ServiceCollection();
        completeShape.AddScoped<IExecutionEvidenceSource, OwnedProjectMetadataExecutionEvidenceSource>();
        completeShape.AddScoped<IExecutionVerificationAssessmentWriter,
            ExecutionVerificationAssessmentPersistenceWriter>();
        completeShape.AddScoped<IVerificationWorkflowService, VerificationWorkflowService>();
        check(!Ready(new ServiceCollection()) && !Ready(completeShape),
            "17S absent factory and empty reviewed catalog deny verification");
        var sourceOnly = new ServiceCollection();
        sourceOnly.AddScoped<IExecutionEvidenceSource, OwnedProjectMetadataExecutionEvidenceSource>();
        check(!Ready(sourceOnly), "17S source-only registration cannot assert verification");
        completeShape.AddScoped<IExecutionEvidenceSource, OwnedProjectMetadataExecutionEvidenceSource>();
        check(!Ready(completeShape), "17S duplicate source cannot assert verification");
        check(inspector.Contains("HasExactVerificationWriter(services)", StringComparison.Ordinal) &&
            inspector.Contains("HasExactVerificationWorkflow(services)", StringComparison.Ordinal) &&
            inspector.Contains("HasExactVerificationFactory(services)", StringComparison.Ordinal) &&
            inspector.Contains("registrations.Length == 1", StringComparison.Ordinal) &&
            inspector.Contains("ServiceLifetime.Scoped", StringComparison.Ordinal) &&
            inspector.Contains("ServiceLifetime.Singleton", StringComparison.Ordinal),
            "17S inspector requires unique scoped source/writer/workflow and reviewed factory");
        check(inspector.Contains("typeof(OwnedProjectMetadataExecutionEvidenceSource)", StringComparison.Ordinal) &&
            inspector.Contains("typeof(ExecutionVerificationAssessmentPersistenceWriter)", StringComparison.Ordinal) &&
            inspector.Contains("ReviewedVerificationWorkflow = new HashSet<Type>()", StringComparison.Ordinal) &&
            inspector.Contains("typeof(AuraDbContextFactory)", StringComparison.Ordinal) &&
            !inspector.Contains("Assembly.GetTypes", StringComparison.Ordinal),
            "17U reviewed foundations are exact while workflow remains uncataloged");
        check(api.Contains("AddScoped<IExecutionEvidenceSource", StringComparison.Ordinal) &&
            api.Contains("AddScoped<IExecutionVerificationAssessmentWriter", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<IVerificationWorkflowService", StringComparison.Ordinal) &&
            api.Contains("AddDbContextFactory<AuraDbContext, AuraDbContextFactory>", StringComparison.Ordinal),
            "17U source, writer and factory do not imply workflow readiness");
        var flags = new RuntimeSafetyOptions();
        var composition = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        check(!composition.Verification && !composition.ToolHandlers &&
            !composition.ExecutionPolicy && !composition.TrustedExecution &&
            !composition.CompositionComplete && !flags.ProductionAdaptersEnabled &&
            !flags.ToolExecutionEnabled && !flags.AuditPersistenceEnabled &&
            !new UnavailableFinalProductionSecurityApprovalSource().IsApproved,
            "17S production readiness, flags and final approval remain disabled");
        return Task.CompletedTask;
    }
}
