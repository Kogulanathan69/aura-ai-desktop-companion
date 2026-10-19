using Aura.Api.Security;
using Aura.Application.Security;
using Aura.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

internal static class VerificationMigrationBaselineRefreshChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));
        var api = Read("Aura.Api", "Program.cs");
        var harness = Read("security-checks", "ProductionMigrationBaselineEvidence.cs");
        var apply = Read("security-checks", "LiveVerificationMigrationApplication.cs");
        var program = Read("security-checks", "Program.cs");
        var review = Read("STEP-17O-VERIFICATION-MIGRATION-BASELINE-REFRESH.md");
        var migrations = ProductionMigrationBaselineEvidence.ExpectedMigrations;
        check(migrations.SequenceEqual([
                "20261014000000_AddToolPermissionGrants",
                "20261014000001_AddActionExecutionStates",
                "20261014000002_AddSecurityPersistenceBundle",
                "20261014000003_AddSecurityAuditOutbox",
                "20261014000004_HardenSecurityAuditOutboxDelivery",
                LiveVerificationMigrationApplication.TargetMigration]) &&
            File.Exists(Path.Combine(root, "Aura.Infrastructure", "Data", "Migrations",
                "20261018000000_AddTrustedVerificationFoundation.cs")),
            "17O six exact migration IDs and reviewed sixth source exist");
        var migration = new AddTrustedVerificationFoundation();
        check(migration.UpOperations.Count == 3 &&
            migration.UpOperations[0] is AddColumnOperation column &&
            column.Table == "ExecutionVerificationAssessments" &&
            column.Name == "ExecutionVersion" && column.ClrType == typeof(long) &&
            column.IsNullable && column.DefaultValue is null &&
            migration.UpOperations[1] is DropCheckConstraintOperation old &&
            old.Table == "ExecutionVerificationAssessments" &&
            old.Name == "CK_ExecutionVerificationAssessments_State" &&
            migration.UpOperations[2] is AddCheckConstraintOperation current &&
            current.Table == "ExecutionVerificationAssessments" &&
            current.Name == "CK_ExecutionVerificationAssessments_State" &&
            current.Sql == ProductionMigrationBaselineEvidence.VerificationFoundationExpression &&
            !current.Sql.Contains("\"Outcome\" IN (0"),
            "17O Up changes only nullable version and exact conservative check");
        check(migration.DownOperations.Count == 3 &&
            migration.DownOperations[0] is DropCheckConstraintOperation &&
            migration.DownOperations[1] is AddCheckConstraintOperation previous &&
            previous.Table == "ExecutionVerificationAssessments" &&
            previous.Sql.Contains("\"ProofSource\" = 0 AND \"ProofVersion\" = 0") &&
            migration.DownOperations[2] is DropColumnOperation removed &&
            removed.Table == "ExecutionVerificationAssessments" &&
            removed.Name == "ExecutionVersion",
            "17O Down restores prior check and removes only added column");
        check(ProductionMigrationBaselineEvidence.RequiredColumns.Any(x =>
                x.Table == "ExecutionVerificationAssessments" &&
                x.Name == "ExecutionVersion" && x.Type == "int8" && !x.Required) &&
            ProductionMigrationBaselineEvidence.CheckConstraints.Any(x =>
                x.Table == "ExecutionVerificationAssessments" &&
                x.Name == "CK_ExecutionVerificationAssessments_State" &&
                x.Markers.Contains("executionversionisnull") &&
                x.Markers.Contains("executionversion=2") &&
                x.Markers.Contains("proofsource=1") &&
                x.Markers.Contains("proofversion=1")) &&
            harness.Contains("proofsource=1andproofversion=1") &&
            harness.Contains("snapshot.History.SequenceEqual(ExpectedMigrations)"),
            "17O read-only baseline checks exact history, nullable bigint and proof pairing");
        var output = new List<string>();
        var called = false;
        string? Missing(string key) => null;
        check(await LiveVerificationMigrationApplication.RunAsync(Missing, output.Add,
                (_, _, _, _) => { called = true; return Task.FromResult(true); }) == 2 &&
            !called && output.SequenceEqual(["VERIFICATION MIGRATION APPLY: SKIPPED"]),
            "17O absent explicit gates cannot call migration runner");
        output.Clear();
        string? Invalid(string key) => key switch
        {
            LiveVerificationMigrationApplication.EnableVariable or
                LiveVerificationMigrationApplication.DisposableVariable => "true",
            LiveVerificationMigrationApplication.ConnectionVariable =>
                "Host=localhost;Username=fixture;Database=unknown_database",
            LiveVerificationMigrationApplication.ExpectedOidVariable => "24576",
            LiveVerificationMigrationApplication.ExpectedSystemVariable => "7696813047573034172",
            _ => null
        };
        check(await LiveVerificationMigrationApplication.RunAsync(Invalid, output.Add,
                (_, _, _, _) => { called = true; return Task.FromResult(true); }) == 1 &&
            !called && output.SequenceEqual(["INPUT: FAIL"]),
            "17O unknown database denied before migration runner");
        const string secure = "Host=example.invalid;Port=5433;Username=fixture;Database=aura_disposable_test;SSL Mode=VerifyFull";
        const string local = "Host=127.0.0.1;Port=5433;Username=fixture;Database=aura_disposable_test;SSL Mode=Disable";
        check(LiveVerificationMigrationApplication.ValidConnection(secure, true, false) &&
            !LiveVerificationMigrationApplication.ValidConnection(
                "Host=example.invalid;Username=fixture;Database=aura_disposable_test", true, false),
            "17O VerifyFull accepted and implicit non-TLS/default SSL mode rejected");
        check(LiveVerificationMigrationApplication.ValidConnection(local, true, true) &&
            LiveVerificationMigrationApplication.ValidConnection(
                "Host=::1;Port=5433;Username=fixture;Database=aura_disposable_test;SSL Mode=Disable", true, true) &&
            !LiveVerificationMigrationApplication.ValidConnection(local, true, false) &&
            !LiveVerificationMigrationApplication.ValidConnection(local, false, true),
            "17O non-TLS accepts only explicit disposable gated loopback");
        check(!LiveVerificationMigrationApplication.ValidConnection(
                "Host=192.0.2.1;Port=5433;Username=fixture;Database=aura_disposable_test;SSL Mode=Disable", true, true) &&
            !LiveVerificationMigrationApplication.ValidConnection(
                "Host=localhost;Port=5433;Username=fixture;Database=aura_disposable_test;SSL Mode=Disable", true, true) &&
            !LiveVerificationMigrationApplication.ValidConnection(
                "Host=example.invalid;Port=5433;Username=fixture;Database=aura_disposable_test;SSL Mode=Disable", true, true) &&
            !LiveVerificationMigrationApplication.ValidConnection(
                "Host=127.0.0.1;Port=5432;Username=fixture;Database=aura_disposable_test;SSL Mode=Disable", true, true) &&
            !LiveVerificationMigrationApplication.ValidConnection(
                "Host=127.0.0.1;Port=5433;Username=fixture;Database=other;SSL Mode=Disable", true, true),
            "17O non-TLS rejects remote, hostname, localhost alias, other port and database");
        check(LiveVerificationMigrationApplication.MatchesIdentity(
                "aura_disposable_test", 24576, 7696813047573034172, 24576, 7696813047573034172) &&
            !LiveVerificationMigrationApplication.MatchesIdentity(
                "aura_disposable_test", 24577, 7696813047573034172, 24576, 7696813047573034172) &&
            !LiveVerificationMigrationApplication.MatchesIdentity(
                "aura_disposable_test", 24576, 7696813047573034173, 24576, 7696813047573034172),
            "17O wrong OID or system identifier fails exact identity comparison");
        var expectedApplied = ProductionMigrationBaselineEvidence.ExpectedMigrations[..^1];
        check(LiveVerificationMigrationApplication.MatchesPreMigrationState(
                expectedApplied, [LiveVerificationMigrationApplication.TargetMigration]) &&
            !LiveVerificationMigrationApplication.MatchesPreMigrationState(
                expectedApplied[..^1], [LiveVerificationMigrationApplication.TargetMigration]) &&
            !LiveVerificationMigrationApplication.MatchesPreMigrationState(
                expectedApplied, [LiveVerificationMigrationApplication.TargetMigration, "unexpected"]) &&
            !LiveVerificationMigrationApplication.MatchesPreMigrationState(
                expectedApplied, ["unexpected"]),
            "17O unexpected applied history or multiple/wrong pending migrations denied");
        output.Clear();
        called = false;
        string? MissingIdentity(string key) => key switch
        {
            LiveVerificationMigrationApplication.EnableVariable or
                LiveVerificationMigrationApplication.DisposableVariable or
                LiveVerificationMigrationApplication.AllowLoopbackNonTlsVariable => "true",
            LiveVerificationMigrationApplication.ConnectionVariable => local,
            LiveVerificationMigrationApplication.ExpectedOidVariable => "0",
            LiveVerificationMigrationApplication.ExpectedSystemVariable => "7696813047573034172",
            _ => null
        };
        check(await LiveVerificationMigrationApplication.RunAsync(MissingIdentity, output.Add,
                (_, _, _, _) => { called = true; return Task.FromResult(true); }) == 1 &&
            !called && output.SequenceEqual(["INPUT: FAIL"]),
            "17O zero expected database OID denies apply before connection");
        output.Clear();
        string? MissingSystem(string key) => key == LiveVerificationMigrationApplication.ExpectedSystemVariable
            ? "0" : MissingIdentity(key) == "0" ? "24576" : MissingIdentity(key);
        check(await LiveVerificationMigrationApplication.RunAsync(MissingSystem, output.Add,
                (_, _, _, _) => { called = true; return Task.FromResult(true); }) == 1 &&
            !called && output.SequenceEqual(["INPUT: FAIL"]),
            "17O zero expected system identifier denies apply before connection");
        check(apply.Contains("pg_control_system()") &&
            apply.Contains("MatchesPreMigrationState(before, pending)") &&
            apply.Contains("MigrateAsync(TargetMigration, token)") &&
            program.Contains("args[0] == LiveVerificationMigrationApplication.Command") &&
            !api.Contains("MigrateAsync(") && !api.Contains("EnsureCreated(") &&
            !harness.Contains("MigrateAsync(") && !harness.Contains("EnsureCreated("),
            "17O migration apply is exact, gated and separate from API and read-only baseline");
        check(review.Contains("OPERATOR-REPORTED LIVE RESULT: PASS") &&
            review.Contains("VERIFICATION MIGRATION APPLY: PASS") &&
            review.Contains("MIGRATION HISTORY: PASS") &&
            review.Contains("MIGRATION BASELINE: PASS") &&
            review.Contains("SECURITY SCHEMA: PASS") &&
            review.Contains("RESULT: PASS") &&
            review.Contains("24576") && review.Contains("7696813047573034172") &&
            review.Contains("This Codex process did not connect") &&
            review.Contains("historical core-schema creation provenance"),
            "17O review records bounded same-target operator PASS without claiming direct execution or provenance");
        var snapshot = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        var flags = new RuntimeSafetyOptions();
        check(!snapshot.Verification && !snapshot.ToolHandlers && !snapshot.ExecutionPolicy &&
            !snapshot.TrustedExecution && !snapshot.CompositionComplete &&
            !flags.ProductionAdaptersEnabled && !flags.ToolExecutionEnabled &&
            !flags.AuditPersistenceEnabled && !flags.SafeFileAccessEnabled && !flags.CloudAiEnabled &&
            !new UnavailableFinalProductionSecurityApprovalSource().IsApproved &&
            review.Contains("Verification=false") &&
            review.Contains("CompositionComplete=false") &&
            review.Contains("activation denied"),
            "17O schema readiness cannot raise composition, flags or activation");
    }
}
