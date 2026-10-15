using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionTrustedExecutionCompositionChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        static bool Ready(IServiceCollection services) =>
            ProductionRuntimeCompositionInspector.Inspect(services).TrustedExecution;

        check(!Ready(new ServiceCollection()), "14O empty registrations cannot assert trusted execution");
        var exact = new ServiceCollection();
        exact.AddScoped<ITrustedToolExecutionService, TrustedToolExecutionService>();
        check(!Ready(exact), "14O exact scoped service without catalog and prerequisites remains false");
        exact.AddScoped<ITrustedToolExecutionService, TrustedToolExecutionService>();
        check(!Ready(exact), "14O duplicate trusted service registrations remain false");

        var unreviewed = new ServiceCollection();
        unreviewed.AddScoped<ITrustedToolExecutionService, UnreviewedTrustedExecutionService>();
        check(!Ready(unreviewed), "14O unreviewed direct or approval-only service remains false");
        var factory = new ServiceCollection();
        factory.AddScoped<ITrustedToolExecutionService>(_ => throw new InvalidOperationException());
        check(!Ready(factory), "14O factory registration remains false");
        var instance = new ServiceCollection();
        instance.AddSingleton<ITrustedToolExecutionService>(new UnreviewedTrustedExecutionService());
        check(!Ready(instance), "14O instance registration remains false");
        var singleton = new ServiceCollection();
        singleton.AddSingleton<ITrustedToolExecutionService, TrustedToolExecutionService>();
        check(!Ready(singleton), "14O singleton trusted service remains false");
        var transient = new ServiceCollection();
        transient.AddTransient<ITrustedToolExecutionService, TrustedToolExecutionService>();
        check(!Ready(transient), "14O transient trusted service remains false");

        var catalog = typeof(ProductionRuntimeCompositionInspector).GetField("ReviewedTrustedExecution",
            BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IReadOnlySet<Type>;
        check(catalog is { Count: 0 } && !Ready(new ServiceCollection()),
            "14O source-controlled trusted execution catalog remains empty");

        var all = new ProductionRuntimeCompositionSnapshot(true, true, true, true, true,
            true, true, true, true, false);
        check(!all.CompositionComplete && !Ready(new ServiceCollection()),
            "14O all prerequisites without exact trusted registration cannot complete composition");

        var predicate = typeof(ProductionRuntimeCompositionInspector).GetMethod(
            "TrustedPrerequisitesReady", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Prerequisites(params bool[] values) => predicate.Invoke(null, values.Cast<object>().ToArray()) is true;
        check(Prerequisites(Enumerable.Repeat(true, 9).ToArray()),
            "14O pure prerequisite gate recognizes all nine independent true values");
        var names = new[] { "ownership", "permission", "execution state", "audit sink",
            "audit delivery", "reconciliation", "verification", "execution policy", "tool handlers" };
        for (var index = 0; index < names.Length; index++)
        {
            var values = Enumerable.Repeat(true, 9).ToArray();
            values[index] = false;
            check(!Prerequisites(values), "14O missing " + names[index] + " blocks trusted execution");
        }
        check(!Ready(exact) && !Prerequisites([false, true, true, true, true, true, true, true, true]),
            "14O exact service registration cannot override a missing prerequisite");

        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var api = Read("Aura.Api", "Program.cs");
        var service = Read("Aura.Application", "Executions", "TrustedToolExecutionService.cs");
        var contracts = Read("Aura.Application", "Executions", "ExecutionContracts.cs");

        check(inspector.Contains("HasExactTrustedExecutionService(services) &&", StringComparison.Ordinal) &&
            inspector.Contains("TrustedPrerequisitesReady(ownership, permission, executionState, auditSink,",
                StringComparison.Ordinal) &&
            inspector.Contains("registrations.Length == 1", StringComparison.Ordinal) &&
            inspector.Contains("ServiceLifetime.Scoped", StringComparison.Ordinal) &&
            inspector.Contains("ImplementationType == typeof(TrustedToolExecutionService)",
                StringComparison.Ordinal) &&
            inspector.Contains("ReviewedTrustedExecution.Contains(typeof(TrustedToolExecutionService))",
                StringComparison.Ordinal),
            "14O exact scoped reviewed service depends on all independently computed fields");
        check(contracts.Contains("interface ITrustedToolExecutionService", StringComparison.Ordinal) &&
            service.Contains("ToolRegistry tools", StringComparison.Ordinal) &&
            service.Contains("IToolExecutionScopeValidator scopes", StringComparison.Ordinal) &&
            service.Contains("IToolExecutionPermissionValidator permissions", StringComparison.Ordinal) &&
            service.Contains("IToolExecutionPolicy policy", StringComparison.Ordinal) &&
            service.Contains("IActionExecutionStateStore states", StringComparison.Ordinal) &&
            service.Contains("IAuditEventWriter? auditWriter", StringComparison.Ordinal),
            "14O trusted service dependency graph is explicit and audit writer remains optional");
        check(service.Contains("ApprovalValidation.IsBoundToApprovedAction", StringComparison.Ordinal) &&
            service.Contains("scopes.ValidateAsync(scope, cancellationToken)", StringComparison.Ordinal) &&
            service.Contains("permissions.HasPermissionAsync(scope, tool, cancellationToken)",
                StringComparison.Ordinal) &&
            service.Contains("policy.AllowsAsync(tool, cancellationToken)", StringComparison.Ordinal) &&
            service.Contains("states.TryReserveInitialAsync(ready, attempt, cancellationToken)",
                StringComparison.Ordinal) &&
            service.Contains("handler.ExecuteAsync(new ToolInvocationRequest(action.ToolId), cancellationToken)",
                StringComparison.Ordinal) &&
            service.IndexOf("policy.AllowsAsync", StringComparison.Ordinal) <
                service.IndexOf("states.TryReserveInitialAsync", StringComparison.Ordinal) &&
            service.IndexOf("states.TryReserveInitialAsync", StringComparison.Ordinal) <
                service.IndexOf("handler.ExecuteAsync", StringComparison.Ordinal),
            "14O approval, ownership, permission, policy and durable reservation precede side effects");
        check(service.Contains("reservedState.ActionId != action.Id", StringComparison.Ordinal) &&
            service.Contains("reservedState.Scope != scope", StringComparison.Ordinal) &&
            service.Contains("reservedState.ToolId != action.ToolId", StringComparison.Ordinal) &&
            service.Contains("reservedState.ApprovalId != approval.Id.Value", StringComparison.Ordinal) &&
            service.Contains("reservedState.Version != 1", StringComparison.Ordinal) &&
            service.Contains("states.TryCompleteAsync", StringComparison.Ordinal) &&
            service.Contains("completedState.Version != reservedState.Version + 1", StringComparison.Ordinal),
            "14O reserved and completed state remain exactly bound and versioned");
        check(service.Contains("AuditEventType.ExecutionReserved", StringComparison.Ordinal) &&
            service.Contains("AuditEventType.ExecutionCompleted", StringComparison.Ordinal) &&
            service.Contains("AuditEventType.ExecutionFailed", StringComparison.Ordinal) &&
            service.Contains("new UnavailableAuditEventWriter()", StringComparison.Ordinal) &&
            service.Contains("ExecutionOperationStatus.ReconciliationRequired", StringComparison.Ordinal) &&
            service.Contains("catch (OperationCanceledException) { throw; }", StringComparison.Ordinal) &&
            service.IndexOf("handler.ExecuteAsync", StringComparison.Ordinal) ==
                service.LastIndexOf("handler.ExecuteAsync", StringComparison.Ordinal),
            "14O one handler call, audit observation, uncertain-state handling and cancellation remain explicit");

        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        check(flags.ToolExecutionEnabled && !Ready(new ServiceCollection()) &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().Handler,
            "14O runtime flags and informational evidence cannot substitute for composition");
        check(!api.Contains("AddScoped<ITrustedToolExecutionService", StringComparison.Ordinal) &&
            !api.Contains("AddSingleton<ITrustedToolExecutionService", StringComparison.Ordinal) &&
            !api.Contains("AddTransient<ITrustedToolExecutionService", StringComparison.Ordinal) &&
            !api.Contains("TrustedToolExecutionService", StringComparison.Ordinal) &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal),
            "14O normal API has no trusted service registration or readiness claim");
        check(!api.Contains("AddHostedService", StringComparison.Ordinal) &&
            !api.Contains("BackgroundService", StringComparison.Ordinal) &&
            !api.Contains("PeriodicTimer", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<IToolHandler", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<IToolExecutionPolicy", StringComparison.Ordinal) &&
            !inspector.Contains("GetRequiredService", StringComparison.Ordinal) &&
            !inspector.Contains("BuildServiceProvider", StringComparison.Ordinal) &&
            !inspector.Contains("ExecuteAsync", StringComparison.Ordinal) &&
            !inspector.Contains("HttpClient", StringComparison.Ordinal) &&
            !inspector.Contains("File.", StringComparison.Ordinal),
            "14O introduces no execution, worker, scheduler, migration, DB, provider or file call");
        var defaults = new RuntimeSafetyOptions();
        check(!Ready(new ServiceCollection()) &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).CompositionComplete &&
            !defaults.ProductionAdaptersEnabled && !defaults.ToolExecutionEnabled &&
            !defaults.AuditPersistenceEnabled && !defaults.SafeFileAccessEnabled &&
            !defaults.CloudAiEnabled,
            "14O production execution and optional capabilities remain disabled");
        return Task.CompletedTask;
    }

    private sealed class UnreviewedTrustedExecutionService : ITrustedToolExecutionService
    {
        public Task<ExecutionOperationResult> ExecuteAsync(ActionScope scope, ActionDescriptor action,
            ApprovalEvidence approval, ExecutionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExecutionOperationResult.Denied(ExecutionOperationStatus.Failed));
        }
    }
}
