using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Auditing;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Verifications;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionVerificationCompositionChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        static bool Ready(IServiceCollection services) =>
            ProductionRuntimeCompositionInspector.Inspect(services).Verification;

        check(!Ready(new ServiceCollection()), "14L empty registrations cannot assert verification");
        var unavailable = new ServiceCollection();
        unavailable.AddScoped<IExecutionEvidenceSource, UnavailableExecutionEvidenceSource>();
        check(!Ready(unavailable), "14L unavailable source cannot assert verification");

        var synthetic = new ServiceCollection();
        synthetic.AddScoped<IExecutionEvidenceSource, SyntheticEvidenceSource>();
        check(!Ready(synthetic), "14L scoped synthetic unreviewed source remains false");
        synthetic.AddScoped<IExecutionEvidenceSource, SyntheticEvidenceSource>();
        check(!Ready(synthetic), "14L duplicate source registrations remain false");

        var factory = new ServiceCollection();
        factory.AddScoped<IExecutionEvidenceSource>(_ => throw new InvalidOperationException());
        check(!Ready(factory), "14L factory registration remains false");
        var instance = new ServiceCollection();
        instance.AddSingleton<IExecutionEvidenceSource>(new UnavailableExecutionEvidenceSource());
        check(!Ready(instance), "14L instance registration remains false");
        var singleton = new ServiceCollection();
        singleton.AddSingleton<IExecutionEvidenceSource, SyntheticEvidenceSource>();
        check(!Ready(singleton), "14L singleton source remains false");
        var transient = new ServiceCollection();
        transient.AddTransient<IExecutionEvidenceSource, SyntheticEvidenceSource>();
        check(!Ready(transient), "14L transient source remains false");

        var catalog = typeof(ProductionRuntimeCompositionInspector).GetField("ReviewedVerification",
            BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IReadOnlySet<Type>;
        check(catalog is { Count: 0 } && !Ready(new ServiceCollection()),
            "14L reviewed catalog is empty and cannot replace matching DI");

        var otherCapabilities = new ServiceCollection();
        otherCapabilities.AddScoped<IActionExecutionStateStore, ActionExecutionStatePersistenceStore>();
        otherCapabilities.AddScoped<IExecutionReconciliationSource, ExecutionReconciliationPersistenceSource>();
        otherCapabilities.AddScoped<IAuditEventSink, SecurityAuditPersistenceSink>();
        otherCapabilities.AddScoped<IAuditOutboxDispatcher, SecurityAuditOutboxDispatcher>();
        check(!Ready(otherCapabilities),
            "14L execution state, reconciliation and audit cannot substitute for independent verification");

        var productionTypes = typeof(AuraDbContext).Assembly.GetTypes()
            .Concat(typeof(IExecutionEvidenceSource).Assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract &&
                typeof(IExecutionEvidenceSource).IsAssignableFrom(type)).ToArray();
        check(productionTypes.SequenceEqual([typeof(UnavailableExecutionEvidenceSource)]) &&
            !Ready(unavailable),
            "14L production assemblies have no independent evidence adapter");

        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var contracts = Read("Aura.Application", "Verifications", "VerificationContracts.cs");
        var workflow = Read("Aura.Application", "Verifications", "VerificationWorkflowService.cs");
        var entity = Read("Aura.Domain", "Entities", "ExecutionVerificationAssessmentRecord.cs");
        var configuration = Read("Aura.Infrastructure", "Data", "Configurations",
            "ExecutionVerificationAssessmentRecordConfiguration.cs");
        var migration = Read("Aura.Infrastructure", "Data", "Migrations",
            "20261014000002_AddSecurityPersistenceBundle.cs");
        var api = Read("Aura.Api", "Program.cs");

        check(inspector.Contains("HasExactVerificationSource(services)", StringComparison.Ordinal) &&
            inspector.Contains("registrations.Length == 1", StringComparison.Ordinal) &&
            inspector.Contains("ServiceLifetime.Scoped", StringComparison.Ordinal) &&
            inspector.Contains("implementation != typeof(UnavailableExecutionEvidenceSource)",
                StringComparison.Ordinal) &&
            inspector.Contains("ReviewedVerification.Contains(implementation)", StringComparison.Ordinal),
            "14L one scoped concrete catalog-reviewed independent source is required");
        check(entity.Contains("ActionId", StringComparison.Ordinal) &&
            entity.Contains("UserId", StringComparison.Ordinal) &&
            entity.Contains("ConversationId", StringComparison.Ordinal) &&
            entity.Contains("ProjectId", StringComparison.Ordinal) &&
            entity.Contains("ApprovalId", StringComparison.Ordinal) &&
            entity.Contains("ExecutionId", StringComparison.Ordinal) &&
            configuration.Contains("CK_ExecutionVerificationAssessments_State", StringComparison.Ordinal) &&
            configuration.Contains("ProofSource", StringComparison.Ordinal) &&
            configuration.Contains("ProofVersion", StringComparison.Ordinal) &&
            migration.Contains("ExecutionVerificationAssessments", StringComparison.Ordinal) &&
            !Ready(new ServiceCollection()),
            "14L assessment schema and migration presence do not assert verification readiness");
        check(contracts.Contains("Task.FromResult<ExecutionEvidenceInput?>(null)", StringComparison.Ordinal) &&
            contracts.Contains("ConservativeVerificationPolicy", StringComparison.Ordinal) &&
            contracts.Contains("VerificationOutcome.Inconclusive", StringComparison.Ordinal) &&
            !Ready(unavailable),
            "14L unavailable source and conservative policy cannot synthesize Verified");
        check(workflow.Contains("scopes.ValidateAsync(scope, cancellationToken)", StringComparison.Ordinal) &&
            workflow.Contains("execution.ActionId != action.Id", StringComparison.Ordinal) &&
            workflow.Contains("execution.Scope != scope", StringComparison.Ordinal) &&
            workflow.Contains("execution.ApprovalId != approval.Id.Value", StringComparison.Ordinal) &&
            workflow.Contains("execution.ExecutionId != request.ExecutionId", StringComparison.Ordinal) &&
            workflow.Contains("execution.CompletedAt.Kind != DateTimeKind.Utc", StringComparison.Ordinal) &&
            workflow.Contains("VerificationOperationStatus.ExecutionUnavailable", StringComparison.Ordinal) &&
            workflow.Contains("VerificationOperationStatus.EvidenceDenied", StringComparison.Ordinal),
            "14L workflow keeps scope and execution binding checks with missing/malformed denial");
        check(contracts.Contains("IIndependentVerificationPolicy", StringComparison.Ordinal) &&
            contracts.Contains("Verified requires independently established tool-specific proof", StringComparison.Ordinal) &&
            workflow.Contains("policy.EvaluateAsync(execution, cancellationToken)", StringComparison.Ordinal) &&
            workflow.Contains("execution.Outcome != ExecutionReportOutcome.Succeeded", StringComparison.Ordinal) &&
            !contracts.Contains("long Version", StringComparison.Ordinal),
            "14L execution report is not proof; version-bound independent evidence remains unavailable");
        check(!workflow.Contains("SaveChangesAsync", StringComparison.Ordinal) &&
            !workflow.Contains("ExecuteUpdateAsync", StringComparison.Ordinal) &&
            !workflow.Contains("Task.Delay", StringComparison.Ordinal) &&
            !workflow.Contains("while (", StringComparison.Ordinal) &&
            workflow.Contains("catch (OperationCanceledException)", StringComparison.Ordinal),
            "14L workflow has no execution-state mutation, hidden retry or cancellation fallback");

        var partial = new ProductionRuntimeCompositionSnapshot(true, true, true, true, true,
            true, false, true, true, true);
        check(!partial.Verification && !partial.CompositionComplete && !Ready(otherCapabilities),
            "14L all other capabilities cannot infer verification or complete composition");
        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        check(flags.ProductionAdaptersEnabled && !Ready(new ServiceCollection()) &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().Verification,
            "14L flags and disposable live evidence cannot replace reviewed independent source");
        check(!api.Contains("AddScoped<IExecutionEvidenceSource", StringComparison.Ordinal) &&
            !api.Contains("AddSingleton<IExecutionEvidenceSource", StringComparison.Ordinal) &&
            !api.Contains("AddTransient<IExecutionEvidenceSource", StringComparison.Ordinal) &&
            !api.Contains("UnavailableExecutionEvidenceSource", StringComparison.Ordinal) &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal),
            "14L normal API has no verification registration or readiness claim");
        check(!api.Contains("AddHostedService", StringComparison.Ordinal) &&
            !api.Contains("BackgroundService", StringComparison.Ordinal) &&
            !api.Contains("PeriodicTimer", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<ITrustedToolExecutionService", StringComparison.Ordinal) &&
            !inspector.Contains("GetRequiredService", StringComparison.Ordinal) &&
            !inspector.Contains("BuildServiceProvider", StringComparison.Ordinal) &&
            !inspector.Contains("ExecuteAsync", StringComparison.Ordinal) &&
            !inspector.Contains("HttpClient", StringComparison.Ordinal) &&
            !inspector.Contains("File.", StringComparison.Ordinal),
            "14L introduces no worker, scheduler, migration, live DB, provider, file or tool call");
        var defaults = new RuntimeSafetyOptions();
        check(!Ready(new ServiceCollection()) &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).CompositionComplete &&
            !defaults.ProductionAdaptersEnabled && !defaults.ToolExecutionEnabled &&
            !defaults.AuditPersistenceEnabled && !defaults.SafeFileAccessEnabled &&
            !defaults.CloudAiEnabled,
            "14L verification and production execution remain disabled");
        return Task.CompletedTask;
    }

    private sealed class SyntheticEvidenceSource : IExecutionEvidenceSource
    {
        public Task<ExecutionEvidenceInput?> GetAsync(Aura.Application.Actions.ActionScope scope,
            Guid executionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ExecutionEvidenceInput?>(null);
        }
    }
}
