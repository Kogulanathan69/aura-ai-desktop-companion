using Aura.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

internal static class TrustedTargetVerificationMigrationChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var migration = new AddTrustedVerificationFoundation();
        var up = migration.UpOperations;
        var down = migration.DownOperations;
        check(up.Count == 3 && up[0] is AddColumnOperation add &&
            add.Table == "ExecutionVerificationAssessments" &&
            add.Name == "ExecutionVersion" && add.IsNullable && add.DefaultValue is null &&
            up[1] is DropCheckConstraintOperation drop &&
            drop.Table == "ExecutionVerificationAssessments" &&
            drop.Name == "CK_ExecutionVerificationAssessments_State" &&
            up[2] is AddCheckConstraintOperation next &&
            next.Table == "ExecutionVerificationAssessments" &&
            next.Name == "CK_ExecutionVerificationAssessments_State" &&
            next.Sql.Contains("\"ExecutionVersion\" = 2") &&
            next.Sql.Contains("\"Outcome\" IN (1, 2)") &&
            !next.Sql.Contains("\"Outcome\" IN (0"),
            "17N migration adds only nullable version and bounded non-Verified state check");
        check(down.Count == 3 && down[0] is DropCheckConstraintOperation &&
            down[1] is AddCheckConstraintOperation old &&
            old.Sql.Contains("\"ProofSource\" = 0 AND \"ProofVersion\" = 0") &&
            down[2] is DropColumnOperation removed &&
            removed.Table == "ExecutionVerificationAssessments" &&
            removed.Name == "ExecutionVersion",
            "17N Down restores old check and drops only new column");
        var root = Directory.GetCurrentDirectory();
        var api = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        var source = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure", "Data", "Migrations",
            "20261018000000_AddTrustedVerificationFoundation.cs"));
        check(!api.Contains("MigrateAsync(") && !api.Contains("EnsureCreated(") &&
            !source.Contains("DeleteData") && !source.Contains("DropTable") &&
            !source.Contains("Sql(") && !source.Contains("defaultValue:") &&
            source.Contains("nullable: true"),
            "17N migration is source-only, additive, and not applied at startup");
        return Task.CompletedTask;
    }
}
