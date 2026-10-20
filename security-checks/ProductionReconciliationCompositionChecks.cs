using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Auditing;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Domain.Entities;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionReconciliationCompositionChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        static bool Ready(IServiceCollection services) =>
            ProductionRuntimeCompositionInspector.Inspect(services).Reconciliation;

        check(!Ready(new ServiceCollection()), "14K empty registrations cannot assert reconciliation");
        var unavailable = new ServiceCollection();
        unavailable.AddScoped<IExecutionReconciliationSource, UnavailableExecutionReconciliationSource>();
        check(!Ready(unavailable), "14K unavailable source cannot assert reconciliation");

        var exact = new ServiceCollection();
        exact.AddScoped<IExecutionReconciliationSource, ExecutionReconciliationPersistenceSource>();
        check(!Ready(exact), "14K exact scoped source without production review remains false");
        exact.AddScoped<IExecutionReconciliationSource, ExecutionReconciliationPersistenceSource>();
        check(!Ready(exact), "14K duplicate source registrations remain false");

        var factory = new ServiceCollection();
        factory.AddScoped<IExecutionReconciliationSource>(_ => throw new InvalidOperationException());
        check(!Ready(factory), "14K source factory registration remains false");
        var instance = new ServiceCollection();
        instance.AddSingleton<IExecutionReconciliationSource>(new UnavailableExecutionReconciliationSource());
        check(!Ready(instance), "14K source instance registration remains false");
        var singleton = new ServiceCollection();
        singleton.AddSingleton<IExecutionReconciliationSource, ExecutionReconciliationPersistenceSource>();
        check(!Ready(singleton), "14K singleton source remains false");
        var transient = new ServiceCollection();
        transient.AddTransient<IExecutionReconciliationSource, ExecutionReconciliationPersistenceSource>();
        check(!Ready(transient), "14K transient source remains false");

        var catalog = typeof(ProductionRuntimeCompositionInspector).GetField("ReviewedReconciliation",
            BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IReadOnlySet<Type>;
        check(catalog is { Count: 4 } &&
            catalog.Contains(typeof(ExecutionReconciliationPersistenceSource)) &&
            catalog.Contains(typeof(ExecutionReconciliationAssessmentPersistenceWriter)) &&
            catalog.Contains(typeof(ConservativeExecutionReconciliationPolicy)) &&
            catalog.Contains(typeof(ExecutionReconciliationService)) && !Ready(new ServiceCollection()),
            "17X complete reconciliation catalog cannot replace DI and fresh evidence");

        var otherCapabilities = new ServiceCollection();
        otherCapabilities.AddScoped<IActionExecutionStateStore, ActionExecutionStatePersistenceStore>();
        otherCapabilities.AddScoped<IAuditEventSink, SecurityAuditPersistenceSink>();
        otherCapabilities.AddScoped<IAuditOutboxDispatcher, SecurityAuditOutboxDispatcher>();
        check(!Ready(otherCapabilities),
            "14K execution state and audit registrations cannot substitute for reconciliation");

        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var source = Read("Aura.Infrastructure", "Services", "ExecutionReconciliationPersistenceSource.cs");
        var service = Read("Aura.Application", "Executions", "ExecutionReconciliationService.cs");
        var contracts = Read("Aura.Application", "Executions", "ExecutionReconciliationContracts.cs");
        var entity = Read("Aura.Domain", "Entities", "ExecutionReconciliationAssessmentRecord.cs");
        var configuration = Read("Aura.Infrastructure", "Data", "Configurations",
            "ExecutionReconciliationAssessmentRecordConfiguration.cs");
        var migration = Read("Aura.Infrastructure", "Data", "Migrations",
            "20261014000002_AddSecurityPersistenceBundle.cs");
        var api = Read("Aura.Api", "Program.cs");

        check(inspector.Contains("HasExactReconciliationGraph(services)", StringComparison.Ordinal) &&
            inspector.Contains("HasReviewedScoped<IExecutionReconciliationSource", StringComparison.Ordinal) &&
            inspector.Contains("ExecutionReconciliationPersistenceSource>(services",
                StringComparison.Ordinal) &&
            inspector.Contains("ServiceLifetime.Scoped", StringComparison.Ordinal),
            "17X requires the complete scoped exact reviewed reconciliation graph");
        check(entity.Contains("ActionId", StringComparison.Ordinal) &&
            entity.Contains("UserId", StringComparison.Ordinal) &&
            entity.Contains("ConversationId", StringComparison.Ordinal) &&
            entity.Contains("ProjectId", StringComparison.Ordinal) &&
            entity.Contains("ApprovalId", StringComparison.Ordinal) &&
            entity.Contains("ExecutionId", StringComparison.Ordinal) &&
            entity.Contains("ObservedVersion", StringComparison.Ordinal) &&
            configuration.Contains("CK_ExecutionReconciliationAssessments_State", StringComparison.Ordinal) &&
            migration.Contains("ExecutionReconciliationAssessments", StringComparison.Ordinal) &&
            !Ready(new ServiceCollection()),
            "14K assessment table and migration do not assert production reconciliation");
        check(source.Contains("db.ActionExecutionStates.AsNoTracking()", StringComparison.Ordinal) &&
            source.Contains("IsolationLevel.RepeatableRead", StringComparison.Ordinal) &&
            source.Contains("x.ActionId == actionId.Value", StringComparison.Ordinal) &&
            source.Contains("x.UserId == scope.UserId", StringComparison.Ordinal) &&
            source.Contains("x.ConversationId == scope.ConversationId", StringComparison.Ordinal) &&
            source.Contains("x.ProjectId == scope.ProjectId", StringComparison.Ordinal) &&
            source.Contains("x.Id == row.ApprovalId", StringComparison.Ordinal) &&
            source.Contains("row.Version !=", StringComparison.Ordinal) &&
            source.Contains("ActionExecutionStatePolicy.IsValid(state)", StringComparison.Ordinal),
            "14K source retains exact scope, approval and durable version binding");
        check(source.Contains("if (!chain) return null", StringComparison.Ordinal) &&
            source.Contains("catch (Exception) { return null; }", StringComparison.Ordinal) &&
            source.Contains("catch (OperationCanceledException)", StringComparison.Ordinal) &&
            source.Contains("ExecutionReconciliationReason.Unknown", StringComparison.Ordinal) &&
            service.Contains("ExecutionReconciliationStatus.StateUnavailable", StringComparison.Ordinal) &&
            service.Contains("ExecutionReconciliationStatus.StateConflict", StringComparison.Ordinal) &&
            service.Contains("ExecutionReconciliationStatus.AlreadyResolved", StringComparison.Ordinal),
            "14K missing, malformed, stale and already-resolved evidence remains fail-closed");
        check(contracts.Contains("UnavailableExecutionReconciliationSource", StringComparison.Ordinal) &&
            contracts.Contains("Task.FromResult<ExecutionReconciliationSnapshot?>(null)", StringComparison.Ordinal) &&
            !source.Contains("SaveChangesAsync", StringComparison.Ordinal) &&
            !source.Contains("ExecuteUpdateAsync", StringComparison.Ordinal) &&
            !source.Contains("db.ToolExecutions", StringComparison.Ordinal) &&
            !source.Contains("Task.Delay", StringComparison.Ordinal) &&
            !source.Contains("while (", StringComparison.Ordinal) &&
            !source.Contains("MigrateAsync", StringComparison.Ordinal),
            "14K source has no synthetic success, state mutation, legacy status or hidden retry");

        var partial = new ProductionRuntimeCompositionSnapshot(true, true, true, true, true,
            false, true, true, true, true);
        check(!partial.Reconciliation && !partial.CompositionComplete && !Ready(otherCapabilities),
            "14K all other capabilities cannot infer reconciliation or complete composition");
        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        check(flags.ProductionAdaptersEnabled && !Ready(new ServiceCollection()) &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().Reconciliation,
            "14K runtime flags and disposable live evidence cannot replace reviewed production DI");
        check(api.Contains("AddStateAuditReconciliationRuntimeFoundation", StringComparison.Ordinal) &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal),
            "17X normal API has exact reconciliation registration without runtime readiness");
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
            "14K introduces no worker, scheduler, migration, live DB, provider, file or tool call");
        var defaults = new RuntimeSafetyOptions();
        check(!Ready(new ServiceCollection()) &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).CompositionComplete &&
            !defaults.ProductionAdaptersEnabled && !defaults.ToolExecutionEnabled &&
            !defaults.AuditPersistenceEnabled && !defaults.SafeFileAccessEnabled &&
            !defaults.CloudAiEnabled,
            "14K production reconciliation and execution remain disabled");
        return Task.CompletedTask;
    }
}
