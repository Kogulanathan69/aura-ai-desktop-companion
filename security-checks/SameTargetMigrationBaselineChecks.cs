using Aura.Api.Security;
using Aura.Application.Security;
using Microsoft.Extensions.DependencyInjection;

internal static class SameTargetMigrationBaselineChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var harness = Read("security-checks", "ProductionMigrationBaselineEvidence.cs");
        var entry = Read("security-checks", "Program.cs");
        var api = Read("Aura.Api", "Program.cs");
        var review = Read("STEP-17K-SAME-TARGET-MIGRATION-BASELINE-AND-PROVENANCE.md");

        check(ProductionMigrationBaselineEvidence.ExpectedMigrations.SequenceEqual([
                "20261014000000_AddToolPermissionGrants",
                "20261014000001_AddActionExecutionStates",
                "20261014000002_AddSecurityPersistenceBundle",
                "20261014000003_AddSecurityAuditOutbox",
                "20261014000004_HardenSecurityAuditOutboxDelivery",
                "20261018000000_AddTrustedVerificationFoundation",
                "20261019000000_AddIndependentVerificationOutcome",
                "20261019000001_AddProjectVersion"
            ]) &&
            Directory.GetFiles(Path.Combine(root, "Aura.Infrastructure", "Data", "Migrations"),
                "*ModelSnapshot.cs").Length == 0,
            "17Q seven-ID expectation; historical core snapshot still absent");
        check(ProductionMigrationBaselineEvidence.RequiredTables.ToHashSet(StringComparer.Ordinal)
            .IsSupersetOf(new[] { "Users", "Projects", "Conversations", "ToolPermissionGrants",
                "AIActions", "ActionApprovals", "ActionExecutionStates",
                "ExecutionReconciliationAssessments", "ExecutionVerificationAssessments",
                "SecurityAuditEvents", "SecurityAuditOutbox" }),
            "17K catalog probe includes ownership, permission, state, reconciliation and audit tables");
        check(harness.Contains("AURA_LIVE_PRODUCTION_MIGRATION_BASELINE_EVIDENCE") &&
            harness.Contains("AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE") &&
            harness.Contains("ConnectionStrings__DefaultConnection") &&
            harness.Contains("aura_disposable_") &&
            harness.Contains("PRODUCTION MIGRATION BASELINE: SKIPPED") &&
            entry.Contains("args[0] == ProductionMigrationBaselineEvidence.Command"),
            "17K read-only live command remains separately gated");
        check(harness.Contains("ORDER BY \\\"MigrationId\\\"") &&
            harness.Contains("snapshot.History.SequenceEqual(ExpectedMigrations)") &&
            !harness.Contains("ExecuteNonQuery") &&
            !harness.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) &&
            !harness.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) &&
            !harness.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase) &&
            !harness.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase) &&
            !harness.Contains("MigrateAsync") && !harness.Contains("EnsureCreated"),
            "17K verifier reads exact history without schema or data mutation");
        check(review.Contains("LIVE RESULT: OPERATOR-REPORTED PASS") &&
            review.Contains("database_name: aura_disposable_test") &&
            review.Contains("database_oid: 24576") &&
            review.Contains("system_identifier: 7696813047573034172") &&
            new[] { "DB CONNECTIVITY", "MIGRATION HISTORY", "MIGRATION BASELINE",
                "CORE TABLES", "KEY CONSTRAINTS", "SECURITY SCHEMA", "RESULT" }
                .All(label => review.Contains(label + ": PASS", StringComparison.Ordinal)) &&
            review.Contains("operator-attested") &&
            review.Contains("Core-schema provenance conclusion: B") &&
            review.Contains("no historical migration creating") &&
            !api.Contains("MigrateAsync(") && !api.Contains("EnsureCreated(") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).CompositionComplete &&
            !new RuntimeSafetyOptions().ProductionAdaptersEnabled &&
            !new RuntimeSafetyOptions().ToolExecutionEnabled &&
            !new RuntimeSafetyOptions().AuditPersistenceEnabled &&
            !new RuntimeSafetyOptions().SafeFileAccessEnabled &&
            !new RuntimeSafetyOptions().CloudAiEnabled &&
            !new UnavailableFinalProductionSecurityApprovalSource().IsApproved,
            "17K baseline evidence cannot by itself enable composition, flags or approval");
        return Task.CompletedTask;
    }
}
