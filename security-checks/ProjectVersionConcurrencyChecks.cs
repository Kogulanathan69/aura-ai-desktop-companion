using Aura.Api.Security;
using Aura.Application.Verifications;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

internal static class ProjectVersionConcurrencyChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        string Read(params string[] path) => File.ReadAllText(Path.Combine(
            [Directory.GetCurrentDirectory(), .. path]));
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=not_used;Username=not_used",
                provider => provider.UseVector()).Options;
        await using var context = new AuraDbContext(options);
        var version = context.Model.FindEntityType(typeof(Project))?.FindProperty(nameof(Project.Version));
        check(version?.ClrType == typeof(long) && !version.IsNullable &&
            version.IsConcurrencyToken && version.ValueGenerated == ValueGenerated.OnAddOrUpdate &&
            Equals(version.GetDefaultValue(), 1L) &&
            typeof(Project).GetProperty(nameof(Project.Version))?.SetMethod?.IsPublic == false,
            "17T project version is required, database generated, and an EF concurrency token");

        var migration = new AddProjectVersion();
        check(migration.UpOperations.Count == 3 &&
            migration.UpOperations[0] is AddColumnOperation column &&
            column.Table == "Projects" && column.Name == "Version" &&
            column.ClrType == typeof(long) && !column.IsNullable && Equals(column.DefaultValue, 1L) &&
            migration.UpOperations[1] is AddCheckConstraintOperation positive &&
            positive.Table == "Projects" && positive.Sql == "\"Version\" > 0" &&
            migration.UpOperations[2] is SqlOperation sql &&
            sql.Sql.Contains("BEFORE INSERT OR UPDATE ON \"Projects\"") &&
            sql.Sql.Contains("NEW.\"Version\" IS DISTINCT FROM 1") &&
            sql.Sql.Contains("NEW.\"Version\" := OLD.\"Version\" + 1") &&
            sql.Sql.Contains("IS DISTINCT FROM OLD.\"Version\"") &&
            sql.Sql.Contains("9223372036854775807") &&
            !sql.Sql.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase),
            "17T migration adds only version, positive constraint, and guarded update trigger");
        check(migration.DownOperations.Count == 4 &&
            migration.DownOperations[0] is SqlOperation &&
            migration.DownOperations[1] is SqlOperation &&
            migration.DownOperations[2] is DropCheckConstraintOperation &&
            migration.DownOperations[3] is DropColumnOperation drop &&
            drop.Table == "Projects" && drop.Name == "Version",
            "17T Down removes only its trigger, function, constraint, and column");

        var proof = OwnedProjectMetadataProof.FromAuthoritativeRead(Guid.NewGuid(), DateTime.UtcNow, 2, 7);
        check(proof?.ProjectVersion == 7 &&
            OwnedProjectMetadataProof.FromAuthoritativeRead(Guid.NewGuid(), DateTime.UtcNow, 2, 0) is null &&
            typeof(OwnedProjectMetadataProof).GetProperty(nameof(proof.ProjectVersion))!.SetMethod is null,
            "17T proof carries positive immutable ProjectVersion");
        var source = Read("Aura.Infrastructure", "Services", "OwnedProjectMetadataExecutionEvidenceSource.cs");
        var workflow = Read("Aura.Application", "Verifications", "VerificationWorkflowService.cs");
        var writer = Read("Aura.Infrastructure", "Services", "ExecutionVerificationAssessmentPersistenceWriter.cs");
        check(source.Contains("x.Version, x.UpdatedAt") &&
            source.Contains("state.Version, project.Version") &&
            workflow.Contains("independent.ProjectVersion != proof.ProjectVersion") &&
            writer.Contains("SELECT ") && writer.Contains("\\\"Version\\\" FROM ") &&
            writer.Contains("reader.GetInt64(1) != evidence.IndependentProof.ProjectVersion") &&
            writer.Contains("FOR UPDATE") &&
            !writer.Contains("ActionExecutionStates.ExecuteUpdateAsync") &&
            !writer.Contains("Task.Delay"),
            "17T source and writer bind locked current version without execution mutation or retry");

        var harness = Read("security-checks", "LiveIndependentVerificationEvidence.cs");
        check(harness.Contains("IsolationLevel.RepeatableRead") &&
            harness.Contains("await using var observer") &&
            harness.Contains("var changed = await db.Projects") &&
            harness.Contains("if (await writer.WriteAsync(stale, 2, token)) return checks") &&
            harness.Contains("checks[3] = await writer.WriteAsync(current, 2, token)") &&
            harness.Contains("proof.ProjectUpdatedAt != original.IndependentProof!.ProjectUpdatedAt") &&
            harness.Contains("proof.ProjectVersion != oldVersion + 1") &&
            !harness.Contains("SetProperty(x => x.UpdatedAt") &&
            harness.Contains("PROJECT VERSION INITIALIZATION") &&
            harness.Contains("OVERLAPPING PROJECT UPDATE"),
            "17T live harness requires overlap and version-based rejection with unchanged UpdatedAt");
        var skipped = await LiveProjectVersionMigrationApplication.RunAsync(_ => null, _ => { });
        check(skipped == 2 &&
            !LiveProjectVersionMigrationApplication.MatchesPreMigrationState(
                ProductionMigrationBaselineEvidence.ExpectedMigrations[..^2],
                [LiveProjectVersionMigrationApplication.TargetMigration]) &&
            LiveProjectVersionMigrationApplication.MatchesPreMigrationState(
                ProductionMigrationBaselineEvidence.ExpectedMigrations[..^1],
                [LiveProjectVersionMigrationApplication.TargetMigration]),
            "17T migration command needs dedicated gate and exact seven-applied one-pending state");
        const string local = "Host=127.0.0.1;Port=5433;Database=aura_disposable_test;Username=fixture;SSL Mode=Disable";
        var variables = new Dictionary<string, string>
        {
            [LiveProjectVersionMigrationApplication.EnableVariable] = "true",
            [LiveProjectVersionMigrationApplication.DisposableVariable] = "true",
            [LiveProjectVersionMigrationApplication.ConnectionVariable] = local,
            [LiveProjectVersionMigrationApplication.ExpectedOidVariable] = "24576",
            [LiveProjectVersionMigrationApplication.ExpectedSystemVariable] = "7696813047573034172"
        };
        var called = false;
        Task<bool> Fake(string _, uint __, long ___, CancellationToken ____)
        {
            called = true;
            return Task.FromResult(true);
        }
        var lines = new List<string>();
        check(await LiveProjectVersionMigrationApplication.RunAsync(
                key => variables.GetValueOrDefault(key), lines.Add, Fake) == 1 && !called &&
            lines.SequenceEqual(["INPUT: FAIL", "RESULT: FAIL"]),
            "17T loopback non-TLS requires separate explicit exception gate");
        variables[LiveProjectVersionMigrationApplication.AllowLoopbackNonTlsVariable] = "true";
        lines.Clear();
        check(await LiveProjectVersionMigrationApplication.RunAsync(
                key => variables.GetValueOrDefault(key), lines.Add, Fake) == 0 && called &&
            lines.Last() == "RESULT: PASS" &&
            !LiveProjectVersionMigrationApplication.ValidConnection(
                "Host=localhost;Port=5433;Database=aura_disposable_test;Username=fixture;SSL Mode=Disable",
                true, true),
            "17T exact reviewed loopback gate can apply; hostname alias is denied");
        var api = Read("Aura.Api", "Program.cs");
        var runtime = new RuntimeSafetyOptions();
        check(!api.Contains("MigrateAsync(") && !api.Contains("EnsureCreated(") &&
            api.Contains("AddScoped<IExecutionEvidenceSource") &&
            !api.Contains("AddScoped<IVerificationWorkflowService") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Verification &&
            !runtime.ProductionAdaptersEnabled && !runtime.ToolExecutionEnabled &&
            !new UnavailableFinalProductionSecurityApprovalSource().IsApproved,
            "17T production verification and activation remain denied before live evidence");
    }
}
