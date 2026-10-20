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
            state.Contains("db.ActionApprovals.AsNoTracking()") &&
            state.Contains("x.Decision == \"Approved\"") &&
            state.Contains("x.ExpiresAt == null || x.ExpiresAt > now"),
            "17X state pre-insert validates the durable ownership and approval chain");
        check(source.Contains("BeginTransactionAsync(IsolationLevel.RepeatableRead") &&
            writer.Contains("db.ExecutionReconciliationAssessments.Add(") &&
            writer.Contains("ExecuteUpdateAsync") &&
            reconciliation.Contains("assessmentWriter is not null") &&
            inspector.Contains("HasExactReconciliationGraph(services)") &&
            review.Contains("point-in-time historical advisory") &&
            review.Contains("complete writer path"),
            "17L reconciliation observation and writer composition limits remain explicit");
        check(sink.Contains("IDbContextFactory<AuraDbContext> factory") &&
            sink.Contains("ResolveDuplicateAsync") &&
            dispatcher.Contains("IAuditEventSink sink") &&
            dispatcher.Contains("AuditOutboxDispatchStatus.Unavailable") &&
            review.Contains("sink commit") && review.Contains("Pending"),
            "17L audit sink and delivery recovery window is documented");

        var registeredContracts = new[] {
            "IActionExecutionStateStore", "IExecutionReconciliationSource",
            "IExecutionReconciliationAssessmentWriter", "IExecutionReconciliationService",
            "IAuditEventSink", "IAuditOutboxDispatcher", "IOwnershipSource",
            "IToolExecutionScopeValidator", "IToolPermissionSource" };
        check(registeredContracts.All(contract =>
                api.Contains("AddStateAuditReconciliationRuntimeFoundation", StringComparison.Ordinal) ||
                contract is "IOwnershipSource" or "IToolExecutionScopeValidator" or "IToolPermissionSource") &&
            api.Contains("AddDbContextFactory<AuraDbContext, AuraDbContextFactory>(") &&
            inspector.Contains("ReviewedExecutionStateLiveEvidence"),
            "17X exact persistence graph is registered while changed-path evidence gates readiness");
        check(inspector.Contains("typeof(HttpCurrentUserContext)") &&
            inspector.Contains("typeof(ExactOwnershipScopeValidator)") &&
            new[] { "ReviewedExecutionStateLiveEvidence", "ReviewedAuditSinkLiveEvidence",
                "ReviewedAuditDeliveryLiveEvidence", "ReviewedReconciliationLiveEvidence" }
            .All(name => inspector.Contains(name, StringComparison.Ordinal)) &&
            inspector.Contains("HasRequiredLiveEvidence", StringComparison.Ordinal),
            "17X reviewed DB-backed catalogs remain gated by fresh changed-path evidence");

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
