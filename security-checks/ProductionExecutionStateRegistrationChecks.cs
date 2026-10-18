using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionExecutionStateRegistrationChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));
        var api = Read("Aura.Api", "Program.cs");
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var store = Read("Aura.Infrastructure", "Services", "ActionExecutionStatePersistenceStore.cs");
        var harness = Read("security-checks", "LiveExecutionStateVerification.cs");
        var suite = Read("security-checks", "Program.cs");
        var review = Read("STEP-17A-DURABLE-EXECUTION-STATE-PRODUCTION-REGISTRATION.md");
        var baseline = Read("STEP-15B-POSTGRESQL-MIGRATION-BASELINE-EVIDENCE.md");

        var constructor = typeof(ActionExecutionStatePersistenceStore).GetConstructors()
            .SingleOrDefault();
        check(typeof(IActionExecutionStateStore).IsAssignableFrom(
                typeof(ActionExecutionStatePersistenceStore)) &&
            constructor?.GetParameters().Select(x => x.ParameterType)
                .SequenceEqual([typeof(IDbContextFactory<AuraDbContext>)]) == true,
            "17A exact durable store contract and context-factory dependency");
        check(store.Contains("CreateDbContextAsync(", StringComparison.Ordinal) &&
            store.Contains("BeginTransactionAsync(", StringComparison.Ordinal) &&
            !store.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !store.Contains("EnsureCreated(", StringComparison.Ordinal),
            "17A store creates operation contexts without schema creation");

        var catalog = typeof(ProductionRuntimeCompositionInspector).GetField(
            "ReviewedExecutionState", BindingFlags.NonPublic | BindingFlags.Static)?
            .GetValue(null) as IReadOnlySet<Type>;
        check(catalog is { Count: 0 } &&
            inspector.Contains("ReviewedExecutionState = new HashSet<Type>()", StringComparison.Ordinal),
            "17A unreviewed evidence cannot enter the exact production catalog");
        check(!api.Contains("AddDbContextFactory<AuraDbContext>", StringComparison.Ordinal) &&
            !api.Contains("AddPooledDbContextFactory<AuraDbContext>", StringComparison.Ordinal) &&
            !api.Contains("IActionExecutionStateStore", StringComparison.Ordinal) &&
            api.Contains("AddDbContext<AuraDbContext>", StringComparison.Ordinal) &&
            api.Contains("AddScoped<IAuraDbContext>", StringComparison.Ordinal),
            "17A normal startup leaves factory and state store unregistered pending evidence");
        check(inspector.Contains("registrations.Length == 1", StringComparison.Ordinal) &&
            inspector.Contains("registrations[0].Lifetime == ServiceLifetime.Scoped", StringComparison.Ordinal) &&
            inspector.Contains("ImplementationType == typeof(ActionExecutionStatePersistenceStore)",
                StringComparison.Ordinal) &&
            inspector.Contains("ReviewedExecutionState.Contains(typeof(ActionExecutionStatePersistenceStore))",
                StringComparison.Ordinal),
            "17A exact scoped registration and reviewed type are both required");

        var fixture = new ServiceCollection();
        fixture.AddScoped<IActionExecutionStateStore, ActionExecutionStatePersistenceStore>();
        check(!ProductionRuntimeCompositionInspector.Inspect(fixture).ExecutionState,
            "17A registration without reviewed evidence cannot assert readiness");
        fixture.AddScoped<IActionExecutionStateStore, ActionExecutionStatePersistenceStore>();
        check(!ProductionRuntimeCompositionInspector.Inspect(fixture).ExecutionState,
            "17A duplicate registration cannot assert readiness");
        var fake = new ServiceCollection();
        fake.AddScoped<IActionExecutionStateStore, UnavailableActionExecutionStateStore>();
        check(!ProductionRuntimeCompositionInspector.Inspect(fake).ExecutionState,
            "17A test or unavailable state store cannot assert readiness");
        check(!ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection())
                .CompositionComplete &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().ExecutionState,
            "17A blocked state cannot complete production composition");

        check(api.Contains("ConnectionStrings:DefaultConnection is required.", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !api.Contains("EnsureCreated(", StringComparison.Ordinal) &&
            !api.Contains("AddHostedService", StringComparison.Ordinal),
            "17A missing DB configuration fails closed without startup migration or worker");
        check(!api.Contains("AddScoped<ITrustedToolExecutionService", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<IToolHandler", StringComparison.Ordinal) &&
            api.Contains("new UnavailableFinalProductionSecurityApprovalSource()", StringComparison.Ordinal) &&
            !new RuntimeSafetyOptions().ProductionAdaptersEnabled &&
            !new RuntimeSafetyOptions().ToolExecutionEnabled &&
            !new RuntimeSafetyOptions().AuditPersistenceEnabled &&
            !new RuntimeSafetyOptions().SafeFileAccessEnabled &&
            !new RuntimeSafetyOptions().CloudAiEnabled,
            "17A no handler, trusted executor, approval or enabled runtime flag");
        check(harness.Contains("--live-production-execution-state-evidence", StringComparison.Ordinal) &&
            harness.Contains("AURA_LIVE_PRODUCTION_EXECUTION_STATE_EVIDENCE", StringComparison.Ordinal) &&
            harness.Contains("AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE", StringComparison.Ordinal) &&
            suite.Contains("args[0] == LiveExecutionStateVerification.ProductionCommand", StringComparison.Ordinal) &&
            !api.Contains("LiveExecutionStateVerification", StringComparison.Ordinal),
            "17A live write-capable evidence remains separate from normal startup");
        check(new[] { "DB CONNECTIVITY", "SCHEMA CHECK", "FIXTURE SETUP",
                "INITIAL RESERVATION CONCURRENCY", "APPROVAL REUSE", "EXECUTION ID REUSE",
                "STALE VERSION", "COMPLETION CONCURRENCY", "NEGATIVE BINDINGS", "CLEANUP",
                "RESULT" }.All(label => review.Contains(label + ": PASS", StringComparison.Ordinal)) &&
            review.Contains("operator-reported", StringComparison.Ordinal) &&
            baseline.Contains("No live PostgreSQL run was performed here", StringComparison.Ordinal) &&
            review.Contains("ExecutionState=false", StringComparison.Ordinal),
            "17A reported execution PASS is documented without inventing baseline approval");
        return Task.CompletedTask;
    }
}
