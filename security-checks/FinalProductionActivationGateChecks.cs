using Aura.Api.Security;
using Aura.Application.Auditing;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Application.Verifications;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;

// Review-only evidence model. This file is not part of Aura.Api composition.
internal static class FinalProductionActivationGateChecks
{
    private enum Dimension
    {
        Jwt, UserMapping, Ownership, Permission, Approval, ExecutionPolicy,
        Handlers, DurableExecutionState, AuditPersistence, AuditDelivery,
        Verification, Reconciliation, MigrationBaseline, LivePostgres,
        SafeFileAccessIfNeeded, CloudAiIfEnabled, RuntimeComposition,
        RuntimeFlags, FinalSecurityApproval
    }

    private sealed record ProductionActivationEvidence(IReadOnlySet<Dimension> Reviewed)
    {
        internal bool ProductionReady => Enum.GetValues<Dimension>().All(Reviewed.Contains);
    }

    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        var program = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        var runtime = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var informational = new PersistenceCapabilityReadiness().GetSnapshot();
        var flags = new RuntimeSafetyOptions();
        var current = new ProductionActivationEvidence(new HashSet<Dimension>());
        check(!current.ProductionReady, "13L production tool execution is NOT READY");

        // A code artifact or one positive gate cannot stand in for the full review.
        var all = Enum.GetValues<Dimension>().ToHashSet();
        check(new ProductionActivationEvidence(all).ProductionReady,
            "13L conceptual gate requires every independent reviewed dimension");
        foreach (var missing in all)
            check(!new ProductionActivationEvidence(all.Where(x => x != missing).ToHashSet()).ProductionReady,
                "13L missing " + missing + " blocks activation");
        foreach (var lone in new[] { Dimension.Jwt, Dimension.UserMapping, Dimension.Ownership,
            Dimension.Permission, Dimension.Approval, Dimension.DurableExecutionState,
            Dimension.AuditPersistence, Dimension.AuditDelivery, Dimension.Verification,
            Dimension.Reconciliation, Dimension.MigrationBaseline })
            check(!new ProductionActivationEvidence(new HashSet<Dimension> { lone }).ProductionReady,
                "13L isolated " + lone + " foundation cannot activate execution");

        // Presence of an opt-in command is never a reviewed live PASS report.
        var evidenceDocs = new[] { "STEP-12F-SECURITY.md", "STEP-13B-SECURITY.md",
            "STEP-13D-SECURITY.md", "STEP-13I-SECURITY.md", "STEP-13J-SECURITY.md",
            "STEP-13K-SECURITY.md" }.Select(name =>
            File.ReadAllText(Path.Combine(root, name))).ToArray();
        check(evidenceDocs.Length == 6 &&
            File.Exists(Path.Combine(root, "security-checks", "LiveOwnershipVerification.cs")) &&
            File.Exists(Path.Combine(root, "security-checks", "LivePermissionVerification.cs")) &&
            File.Exists(Path.Combine(root, "security-checks", "LiveExecutionStateVerification.cs")) &&
            File.Exists(Path.Combine(root, "security-checks", "LiveAuditOutboxHardeningVerification.cs")) &&
            !current.ProductionReady,
            "13L live harness presence supplies no reviewed PASS evidence");
        check(evidenceDocs[0].Contains("not run", StringComparison.OrdinalIgnoreCase) &&
            evidenceDocs[1].Contains("not run", StringComparison.OrdinalIgnoreCase),
            "13L ownership and permission live evidence remains absent");
        check(evidenceDocs[2].Contains("live command was **not run**", StringComparison.OrdinalIgnoreCase) &&
            evidenceDocs[3].Contains("This PC has no PostgreSQL", StringComparison.OrdinalIgnoreCase) &&
            evidenceDocs[3].Contains("**not** run", StringComparison.OrdinalIgnoreCase),
            "13L execution concurrency and audit dispatcher live evidence remains absent");
        check(evidenceDocs[4].Contains("not applied", StringComparison.OrdinalIgnoreCase) &&
            evidenceDocs[5].Contains("No live PostgreSQL command was run", StringComparison.OrdinalIgnoreCase),
            "13L migration application and Step 13K live PASS evidence remain absent");

        var migrationDirectory = Path.Combine(root, "Aura.Infrastructure", "Data", "Migrations");
        var migrations = Directory.GetFiles(migrationDirectory, "*.cs").Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        check(migrations.SequenceEqual(new[] {
            "20261014000000_AddToolPermissionGrants.cs",
            "20261014000001_AddActionExecutionStates.cs",
            "20261014000002_AddSecurityPersistenceBundle.cs",
            "20261014000003_AddSecurityAuditOutbox.cs",
            "20261014000004_HardenSecurityAuditOutboxDelivery.cs",
            "20261018000000_AddTrustedVerificationFoundation.cs",
            "20261019000000_AddIndependentVerificationOutcome.cs",
            "20261019000001_AddProjectVersion.cs" }),
            "17P six baseline sources plus pending Verified constraint source exist in order");
        check(Directory.GetFiles(migrationDirectory, "*ModelSnapshot.cs").Length == 0 &&
            evidenceDocs[5].Contains("historical core schema baseline is still unresolved",
                StringComparison.OrdinalIgnoreCase),
            "13L historical migration baseline remains unresolved");
        check(!program.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !program.Contains("Database.Migrate(", StringComparison.Ordinal) &&
            !program.Contains("EnsureCreated", StringComparison.Ordinal),
            "13L API startup never applies migrations or creates schema");

        check(!flags.ProductionAdaptersEnabled && !flags.ToolExecutionEnabled &&
            !flags.SafeFileAccessEnabled && !flags.CloudAiEnabled && !flags.AuditPersistenceEnabled,
            "13L production, execution, file, cloud and audit flags default false");
        check(!runtime.Ownership && !runtime.Permission && !runtime.ExecutionState &&
            !runtime.AuditSink && !runtime.Reconciliation && !runtime.Verification &&
            !runtime.Handler && !runtime.ExecutionPolicy && !runtime.SafeFileAccess &&
            informational.Ownership && !informational.Permission &&
            !informational.ExecutionState && !informational.AuditSink,
            "13L startup is all false; informational ownership cannot inflate runtime readiness");
        check(program.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal) &&
            !program.Contains("new PersistenceCapabilityReadiness()", StringComparison.Ordinal),
            "13L API uses unavailable startup readiness");
        foreach (var contract in new[] { "IToolPermissionSource", "IActionExecutionStateStore",
            "IAuditEventSink", "IAuditOutboxDispatcher", "IExecutionReconciliationSource",
            "ITrustedToolExecutionService" })
            check(!program.Contains("AddScoped<" + contract, StringComparison.Ordinal) &&
                !program.Contains("AddSingleton<" + contract, StringComparison.Ordinal) &&
                !program.Contains("AddTransient<" + contract, StringComparison.Ordinal),
                "13L no production registration for " + contract);
        check(program.Contains(".AddOwnershipRuntimeFoundation(", StringComparison.Ordinal) &&
            !program.Contains("SecurityAuditOutboxDispatcher", StringComparison.Ordinal) &&
            !program.Contains("AddHostedService", StringComparison.Ordinal) &&
            !program.Contains("TrustedToolExecutionService", StringComparison.Ordinal),
            "13L no ownership helper, outbox worker or trusted execution activation");

        var productionTypes = typeof(ToolRegistry).Assembly.GetTypes()
            .Concat(typeof(AuraDbContext).Assembly.GetTypes())
            .Concat(typeof(RuntimeSafetyOptions).Assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract).ToArray();
        check(!productionTypes.Any(type => typeof(IToolHandler).IsAssignableFrom(type)),
            "13L no reviewed production tool handler set exists");
        check(productionTypes.Where(type => typeof(IToolExecutionPolicy).IsAssignableFrom(type))
            .SequenceEqual([typeof(DenyToolExecutionPolicy)]),
            "13L deny-only execution policy cannot establish allowing production policy");
        check(productionTypes.Where(type => typeof(IExecutionEvidenceSource).IsAssignableFrom(type))
            .ToHashSet().SetEquals([typeof(UnavailableExecutionEvidenceSource),
                typeof(OwnedProjectMetadataExecutionEvidenceSource)]) &&
            productionTypes.Where(type => typeof(IIndependentVerificationPolicy).IsAssignableFrom(type))
            .SequenceEqual([typeof(ConservativeVerificationPolicy)]),
            "13L execution, audit or AI success cannot become independent verification proof");
        check(productionTypes.Any(type => type == typeof(ExecutionReconciliationPersistenceSource)) &&
            !runtime.Reconciliation && !runtime.ExecutionState,
            "13L reconciliation source foundation cannot activate without exact durable state and live review");
        check(productionTypes.Any(type => type == typeof(SecurityAuditPersistenceSink)) &&
            productionTypes.Any(type => type == typeof(SecurityAuditOutboxDispatcher)) &&
            !runtime.AuditSink && !current.ProductionReady,
            "13L audit sink and outbox artifacts cannot establish delivery or exactly-once behavior");
        check(!new Aura.Application.ProjectFiles.Content.ProjectFileAccessOptions().Enabled &&
            !new Aura.Infrastructure.AI.OpenAI.OpenAiOptions().Enabled,
            "13L Safe File Access and Cloud AI stay disabled independent of tool readiness");
        check(File.ReadAllText(Path.Combine(root, "STEP-13K-SECURITY.md"))
            .Contains("external side effects may duplicate", StringComparison.OrdinalIgnoreCase),
            "13L external audit side-effect duplication risk remains documented");
        return Task.CompletedTask;
    }
}
