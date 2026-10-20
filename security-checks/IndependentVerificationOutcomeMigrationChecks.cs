using Aura.Api.Security;
using Aura.Application.Security;
using Aura.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

internal static class IndependentVerificationOutcomeMigrationChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var api = Read("Aura.Api", "Program.cs");
        var entry = Read("security-checks", "Program.cs");
        var apply = Read("security-checks", "LiveIndependentVerificationOutcomeMigrationApplication.cs");
        var writer = Read("Aura.Infrastructure", "Services", "ExecutionVerificationAssessmentPersistenceWriter.cs");
        var workflow = Read("Aura.Application", "Verifications", "VerificationWorkflowService.cs");
        var contracts = Read("Aura.Application", "Verifications", "VerificationContracts.cs");
        var review = Read("STEP-17Q-INDEPENDENT-VERIFICATION-OUTCOME-MIGRATION-AND-PERSISTENCE.md");
        var migration = new AddIndependentVerificationOutcome();
        var baseline = ProductionMigrationBaselineEvidence.ExpectedMigrations;
        check(baseline.Length == 8 &&
            baseline[^2] == LiveIndependentVerificationOutcomeMigrationApplication.TargetMigration &&
            baseline[..^2].SequenceEqual([
                "20261014000000_AddToolPermissionGrants",
                "20261014000001_AddActionExecutionStates",
                "20261014000002_AddSecurityPersistenceBundle",
                "20261014000003_AddSecurityAuditOutbox",
                "20261014000004_HardenSecurityAuditOutboxDelivery",
                "20261018000000_AddTrustedVerificationFoundation"]),
            "17Q baseline requires seven exact ordered migration IDs");
        check(migration.UpOperations.Count == 2 &&
            migration.UpOperations[0] is DropCheckConstraintOperation dropped &&
            dropped.Table == "ExecutionVerificationAssessments" &&
            dropped.Name == "CK_ExecutionVerificationAssessments_State" &&
            migration.UpOperations[1] is AddCheckConstraintOperation added &&
            added.Table == dropped.Table && added.Name == dropped.Name &&
            added.Sql == AddIndependentVerificationOutcome.RevisedState &&
            added.Sql == ProductionMigrationBaselineEvidence.IndependentVerificationOutcomeExpression &&
            added.Sql.Contains("\"ExecutionStatus\" = 2 AND \"Outcome\" = 0 AND \"ExecutionVersion\" = 2") &&
            added.Sql.Contains("\"ProofSource\" = 1 AND \"ProofVersion\" = 1") &&
            added.Sql.Contains("\"ExecutionVersion\" IS NULL AND \"ProofSource\" = 0 AND \"ProofVersion\" = 0"),
            "17Q Up only replaces check with exact Verified and historical advisory clauses");
        check(migration.DownOperations.Count == 2 &&
            migration.DownOperations[0] is DropCheckConstraintOperation &&
            migration.DownOperations[1] is AddCheckConstraintOperation old &&
            old.Sql == ProductionMigrationBaselineEvidence.VerificationFoundationExpression &&
            !old.Sql.Contains("\"Outcome\" = 0") &&
            !Read("Aura.Infrastructure", "Data", "Migrations",
                "20261019000000_AddIndependentVerificationOutcome.cs").Contains("Sql(") &&
            !api.Contains("MigrateAsync(") && !api.Contains("EnsureCreated("),
            "17Q Down restores six-ID state and no broad SQL or startup application exists");
        var output = new List<string>();
        var called = false;
        Task<bool> Fake(string _, uint __, long ___, CancellationToken ____) {
            called = true; return Task.FromResult(true);
        }
        check(await LiveIndependentVerificationOutcomeMigrationApplication.RunAsync(
                _ => null, output.Add, Fake) == 2 && !called &&
            output.SequenceEqual(["INDEPENDENT VERIFICATION MIGRATION APPLY: SKIPPED"]),
            "17Q missing dedicated gate skips without database call");
        const string local = "Host=127.0.0.1;Port=5433;Database=aura_disposable_test;Username=fixture;SSL Mode=Disable";
        const string remote = "Host=example.invalid;Database=aura_disposable_test;Username=fixture;SSL Mode=VerifyFull";
        check(LiveIndependentVerificationOutcomeMigrationApplication.ValidConnection(remote, true, false) &&
            LiveIndependentVerificationOutcomeMigrationApplication.ValidConnection(local, true, true) &&
            !LiveIndependentVerificationOutcomeMigrationApplication.ValidConnection(local, true, false) &&
            !LiveIndependentVerificationOutcomeMigrationApplication.ValidConnection(local, false, true) &&
            !LiveIndependentVerificationOutcomeMigrationApplication.ValidConnection(
                "Host=localhost;Port=5433;Database=aura_disposable_test;Username=fixture;SSL Mode=Disable", true, true) &&
            !LiveIndependentVerificationOutcomeMigrationApplication.ValidConnection(
                "Host=192.0.2.1;Port=5433;Database=aura_disposable_test;Username=fixture;SSL Mode=Disable", true, true),
            "17Q VerifyFull remains normal; non-TLS needs exact disposable loopback gate");
        output.Clear();
        string? WrongIdentity(string key) => key switch
        {
            LiveIndependentVerificationOutcomeMigrationApplication.EnableVariable or
                LiveIndependentVerificationOutcomeMigrationApplication.DisposableVariable or
                LiveIndependentVerificationOutcomeMigrationApplication.AllowLoopbackNonTlsVariable => "true",
            LiveIndependentVerificationOutcomeMigrationApplication.ConnectionVariable => local,
            LiveIndependentVerificationOutcomeMigrationApplication.ExpectedOidVariable => "24577",
            LiveIndependentVerificationOutcomeMigrationApplication.ExpectedSystemVariable => "7696813047573034172",
            _ => null
        };
        check(await LiveIndependentVerificationOutcomeMigrationApplication.RunAsync(
                WrongIdentity, output.Add, Fake) == 1 && !called &&
            output.SequenceEqual(["INPUT: FAIL"]),
            "17Q wrong reviewed loopback OID denies before database call");
        output.Clear();
        string? WrongSystem(string key) => key ==
            LiveIndependentVerificationOutcomeMigrationApplication.ExpectedOidVariable
            ? "24576" : key == LiveIndependentVerificationOutcomeMigrationApplication.ExpectedSystemVariable
            ? "7696813047573034173" : WrongIdentity(key);
        check(await LiveIndependentVerificationOutcomeMigrationApplication.RunAsync(
                WrongSystem, output.Add, Fake) == 1 && !called &&
            output.SequenceEqual(["INPUT: FAIL"]),
            "17Q wrong reviewed loopback system identifier denies before database call");
        check(LiveIndependentVerificationOutcomeMigrationApplication.MatchesPreMigrationState(
                baseline[..^2], [baseline[^2], baseline[^1]]) &&
            !LiveIndependentVerificationOutcomeMigrationApplication.MatchesPreMigrationState(
                baseline[..^3], [baseline[^2], baseline[^1]]) &&
            !LiveIndependentVerificationOutcomeMigrationApplication.MatchesPreMigrationState(
                baseline[..^2], [baseline[^2], "unexpected"]) &&
            apply.Contains("pg_control_system()") &&
            apply.Contains("MigrateAsync(TargetMigration, token)") &&
            entry.Contains("args[0] == LiveIndependentVerificationOutcomeMigrationApplication.Command"),
            "17Q exact six-applied, one-pending and live identity precede operator-only migration");
        check(writer.Contains("evidence.IndependentProof is not { } proof") &&
            writer.Contains("ProofSource = verified ? VerificationProofSource.OwnedProjectMetadata : VerificationProofSource.None") &&
            workflow.Contains("execution.ExecutionVersion != 2") &&
            workflow.Contains("proof.ProofSource != VerificationProofSource.OwnedProjectMetadata") &&
            workflow.Contains("proof.ProofVersion != 1") &&
            api.Contains("AddScoped<IExecutionEvidenceSource,") &&
            api.Contains("AddScoped<IExecutionVerificationAssessmentWriter,") &&
            !api.Contains("AddScoped<IVerificationWorkflowService,") &&
            !writer.Contains("ExecuteUpdate") && !writer.Contains("while (") &&
            !writer.Contains("Task.Delay"),
            "17R writer requires proof; wrong/missing proof and stale version cannot promote");
        check(review.Contains("OPERATOR-REPORTED LIVE RESULT: PASS") &&
            review.Contains("INDEPENDENT VERIFICATION MIGRATION APPLY: PASS") &&
            review.Contains("MIGRATION HISTORY: PASS") &&
            review.Contains("MIGRATION BASELINE: PASS") &&
            review.Contains("SECURITY SCHEMA: PASS") &&
            review.Contains("RESULT: PASS") &&
            review.Contains("24576") && review.Contains("7696813047573034172") &&
            review.Contains("This Codex process did not connect") &&
            review.Contains("live independent-verification writer path remains **INCOMPLETE**"),
            "17Q review records bounded operator same-target PASS without claiming a writer run");
        check(contracts.Contains("public sealed class VerificationEvidence") &&
            contracts.Contains("public OwnedProjectMetadataProof? IndependentProof { get; }") &&
            contracts.Contains("public long? ExecutionVersion { get; }") &&
            writer.Contains("WriteAsync(VerificationEvidence evidence, long executionVersion") &&
            review.Contains("server-issued, immutable proof-bearing `VerificationEvidence`") &&
            review.Contains("writer must recheck those exact bindings"),
            "17R immutable proof reaches writer while historical Step 17Q review remains accurate");
        var snapshot = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        var flags = new RuntimeSafetyOptions();
        check(!snapshot.Verification && !snapshot.ToolHandlers && !snapshot.ExecutionPolicy &&
            !snapshot.TrustedExecution && !snapshot.CompositionComplete &&
            !flags.ProductionAdaptersEnabled && !flags.ToolExecutionEnabled &&
            !flags.AuditPersistenceEnabled && !flags.SafeFileAccessEnabled &&
            !flags.CloudAiEnabled &&
            !new UnavailableFinalProductionSecurityApprovalSource().IsApproved,
            "17Q live schema PASS cannot raise readiness, flags or activation");
    }
}
