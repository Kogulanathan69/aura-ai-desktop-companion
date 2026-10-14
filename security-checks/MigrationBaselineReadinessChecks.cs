using Aura.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

internal static class MigrationBaselineReadinessChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();

        var migrationDirectory = Path.Combine(
            root,
            "Aura.Infrastructure",
            "Data",
            "Migrations");

        var migrationFiles = Directory
            .GetFiles(migrationDirectory, "*.cs")
            .Select(Path.GetFileName)
            .Where(x => x is not null)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "20261014000000_AddToolPermissionGrants.cs",
            "20261014000001_AddActionExecutionStates.cs",
            "20261014000002_AddSecurityPersistenceBundle.cs"
        };

        check(
            migrationFiles.SequenceEqual(expected),
            "13F current targeted migration sequence is exact and ordered");

        check(
            string.CompareOrdinal(expected[0], expected[1]) < 0 &&
            string.CompareOrdinal(expected[1], expected[2]) < 0,
            "13F security persistence migration follows permission and execution-state migrations");

        var bundle = new AddSecurityPersistenceBundle();

        check(
            bundle.UpOperations.Count(
                x => x is CreateTableOperation) == 3,
            "13F security persistence migration remains additive-only for three tables");

        check(
            bundle.UpOperations.All(
                x => x is CreateTableOperation or CreateIndexOperation),
            "13F migration performs no data mutation or existing-table alteration");

        check(
            bundle.DownOperations.Count == 3 &&
            bundle.DownOperations.All(
                x => x is DropTableOperation),
            "13F rollback remains limited to newly introduced security tables");

        var snapshotFiles = Directory.GetFiles(
            migrationDirectory,
            "*ModelSnapshot.cs");

        check(
            snapshotFiles.Length == 0,
            "13F no EF model snapshot is present; historical baseline remains explicitly unverified");

        return Task.CompletedTask;
    }
}