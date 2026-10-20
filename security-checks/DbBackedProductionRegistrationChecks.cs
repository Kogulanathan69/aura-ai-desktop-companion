using Aura.Api.Security;
using Aura.Application.Security;
using Microsoft.Extensions.DependencyInjection;

internal static class DbBackedProductionRegistrationChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));
        var api = Read("Aura.Api", "Program.cs");
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var state = Read("Aura.Infrastructure", "Services", "ActionExecutionStatePersistenceStore.cs");
        var reconciliation = Read("Aura.Application", "Executions", "ExecutionReconciliationService.cs");
        var source = Read("Aura.Infrastructure", "Services", "ExecutionReconciliationPersistenceSource.cs");
        var writer = Read("Aura.Infrastructure", "Services", "ExecutionReconciliationAssessmentPersistenceWriter.cs");
        var sink = Read("Aura.Infrastructure", "Services", "SecurityAuditPersistenceSink.cs");
        var dispatcher = Read("Aura.Infrastructure", "Services", "SecurityAuditOutboxDispatcher.cs");
        var baseline = Read("STEP-17K-SAME-TARGET-MIGRATION-BASELINE-AND-PROVENANCE.md");
        var review = Read("STEP-17L-DB-BACKED-PRODUCTION-REGISTRATION-UNBLOCK.md");

        check(baseline.Contains("LIVE RESULT: OPERATOR-REPORTED PASS") &&
            baseline.Contains("Core-schema provenance conclusion: B") &&
            review.Contains("operator-attested") &&
            review.Contains("partially unresolved"),
            "17L reuses bounded live baseline without inventing core-schema provenance");
        check(!api.Contains("AddDbContext<AuraDbContext>(") &&
            api.Contains("AddScoped<IAuraDbContext>(") &&
            api.Contains("OwnershipRuntimeFoundationRegistration.ResolveProductionDbContext") &&
            api.Contains("AddDbContextFactory<AuraDbContext, AuraDbContextFactory>(") &&
            !api.Contains("AddPooledDbContextFactory<AuraDbContext>(") &&
            !api.Contains("MigrateAsync(") && !api.Contains("EnsureCreated(") &&
            !api.Contains("AddHostedService"),
            "17U one shared factory/scoped context graph and no startup mutation or worker");
        check(state.Contains("IDbContextFactory<AuraDbContext> factory") &&
            state.Contains("db.ActionExecutionStates.Add(row)") &&
            !state.Contains("db.ActionApprovals.AsNoTracking()") &&
            review.Contains("cross-entity") && review.Contains("approval-state policy"),
            "17L state pre-insert binding gap remains explicit before registration");
        check(source.Contains("BeginTransactionAsync(IsolationLevel.RepeatableRead") &&
            writer.Contains("db.ExecutionReconciliationAssessments.Add(") &&
            reconciliation.Contains("assessmentWriter is not null") &&
            inspector.Contains("HasExactReconciliationSource(services)") &&
            review.Contains("point-in-time historical advisory") &&
            review.Contains("complete writer path"),
            "17L reconciliation observation and writer composition limits remain explicit");
        check(sink.Contains("IDbContextFactory<AuraDbContext> factory") &&
            sink.Contains("ResolveDuplicateAsync") &&
            dispatcher.Contains("IAuditEventSink sink") &&
            dispatcher.Contains("AuditOutboxDispatchStatus.Unavailable") &&
            review.Contains("sink commit") && review.Contains("Pending"),
            "17L audit sink and delivery recovery window is documented");

        var blockedContracts = new[] {
            "IActionExecutionStateStore", "IExecutionReconciliationSource",
            "IExecutionReconciliationAssessmentWriter", "IExecutionReconciliationService",
            "IAuditEventSink", "IAuditOutboxDispatcher", "IOwnershipSource",
            "IToolExecutionScopeValidator", "IToolPermissionSource" };
        check(blockedContracts.All(contract => !api.Contains($"AddScoped<{contract},", StringComparison.Ordinal)) &&
            api.Contains("AddDbContextFactory<AuraDbContext, AuraDbContextFactory>(") &&
            review.Contains("No production registrations or catalog entries were added"),
            "17L unresolved capabilities have no premature normal-startup registration");
        check(inspector.Contains("typeof(HttpCurrentUserContext)") &&
            inspector.Contains("typeof(ExactOwnershipScopeValidator)") &&
            new[] { "ReviewedPermission", "ReviewedExecutionState",
                "ReviewedAuditSink", "ReviewedAuditDelivery", "ReviewedReconciliation" }
            .All(name => inspector.Contains($"{name} = new HashSet<Type>()", StringComparison.Ordinal)) &&
            review.Contains("No production registrations or catalog entries were added"),
            "17L reviewed DB-backed catalogs stay empty pending independent approval");

        var snapshot = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        var flags = new RuntimeSafetyOptions();
        check(!snapshot.Ownership && !snapshot.Permission && !snapshot.ExecutionState &&
            !snapshot.AuditSink && !snapshot.AuditDelivery && !snapshot.Reconciliation &&
            !snapshot.Verification && !snapshot.ExecutionPolicy && !snapshot.ToolHandlers &&
            !snapshot.TrustedExecution && !snapshot.CompositionComplete &&
            !flags.ProductionAdaptersEnabled && !flags.ToolExecutionEnabled &&
            !flags.AuditPersistenceEnabled && !flags.SafeFileAccessEnabled &&
            !flags.CloudAiEnabled &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()") &&
            api.Contains("new UnavailableFinalProductionSecurityApprovalSource()") &&
            !new UnavailableFinalProductionSecurityApprovalSource().IsApproved,
            "17L all ten capabilities, runtime flags and final activation remain blocked");
        return Task.CompletedTask;
    }
}
