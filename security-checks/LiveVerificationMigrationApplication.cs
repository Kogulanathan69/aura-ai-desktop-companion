using System.Globalization;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Pgvector.EntityFrameworkCore;

// Explicit operator-only command. Never invoked by normal offline checks or API startup.
internal static class LiveVerificationMigrationApplication
{
    internal const string Command = "--apply-verification-foundation-migration";
    internal const string EnableVariable = "AURA_APPLY_VERIFICATION_FOUNDATION_MIGRATION";
    internal const string DisposableVariable = "AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE";
    internal const string ConnectionVariable = "ConnectionStrings__DefaultConnection";
    internal const string ExpectedOidVariable = "AURA_EXPECTED_DATABASE_OID";
    internal const string ExpectedSystemVariable = "AURA_EXPECTED_SYSTEM_IDENTIFIER";
    internal const string AllowLoopbackNonTlsVariable = "AURA_ALLOW_LOOPBACK_NON_TLS_DISPOSABLE";
    internal const string TargetMigration = "20261018000000_AddTrustedVerificationFoundation";
    private const string TargetDatabase = "aura_disposable_test";

    internal static async Task<int> RunAsync(Func<string, string?> environment, Action<string> write,
        Func<string, uint, long, CancellationToken, Task<bool>>? apply = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (environment(EnableVariable) != "true" || environment(DisposableVariable) != "true")
            {
                write("VERIFICATION MIGRATION APPLY: SKIPPED");
                return 2;
            }
            var connection = environment(ConnectionVariable);
            if (!uint.TryParse(environment(ExpectedOidVariable), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var expectedOid) || expectedOid == 0 ||
                !long.TryParse(environment(ExpectedSystemVariable), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var expectedSystem) || expectedSystem <= 0 ||
                !ValidConnection(connection, environment(DisposableVariable) == "true",
                    environment(AllowLoopbackNonTlsVariable) == "true"))
            {
                write("INPUT: FAIL");
                return 1;
            }
            var passed = await (apply ?? ApplyAsync)(connection!, expectedOid, expectedSystem,
                cancellationToken);
            write(passed ? "VERIFICATION MIGRATION APPLY: PASS" :
                "VERIFICATION MIGRATION APPLY: FAIL");
            return passed ? 0 : 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            write("VERIFICATION MIGRATION APPLY: FAIL");
            return 1;
        }
        catch (Exception)
        {
            write("VERIFICATION MIGRATION APPLY: FAIL");
            return 1;
        }
    }

    internal static bool ValidConnection(string? connection, bool disposable,
        bool allowLoopbackNonTls)
    {
        if (string.IsNullOrWhiteSpace(connection)) return false;
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(connection);
            if (string.IsNullOrWhiteSpace(parsed.Host) ||
                string.IsNullOrWhiteSpace(parsed.Username) ||
                parsed.Database != TargetDatabase)
                return false;
            if (parsed.SslMode == SslMode.VerifyFull) return true;
            return parsed.SslMode == SslMode.Disable && disposable && allowLoopbackNonTls &&
                parsed.Port == 5433 && (parsed.Host == "127.0.0.1" || parsed.Host == "::1");
        }
        catch (Exception) { return false; }
    }

    private static async Task<bool> ApplyAsync(string connection, uint expectedOid,
        long expectedSystem, CancellationToken token)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.UseVector()).Options;
        await using var db = new AuraDbContext(options);
        await db.Database.OpenConnectionAsync(token);
        var opened = db.Database.GetDbConnection();
        await using (var identity = opened.CreateCommand())
        {
            identity.CommandText = "SELECT current_database(), " +
                "(SELECT oid FROM pg_database WHERE datname = current_database()), " +
                "(SELECT system_identifier FROM pg_control_system())";
            await using var reader = await identity.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token) ||
                !MatchesIdentity(reader.GetString(0),
                    Convert.ToUInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                    Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
                    expectedOid, expectedSystem))
                return false;
        }
        var before = (await db.Database.GetAppliedMigrationsAsync(token)).ToArray();
        var pending = (await db.Database.GetPendingMigrationsAsync(token)).ToArray();
        if (!MatchesPreMigrationState(before, pending))
            return false;
        await db.Database.CloseConnectionAsync();
        await db.GetService<IMigrator>().MigrateAsync(TargetMigration, token);
        var after = (await db.Database.GetAppliedMigrationsAsync(token)).ToArray();
        return after.SequenceEqual(ProductionMigrationBaselineEvidence.ExpectedMigrations);
    }

    internal static bool MatchesIdentity(string database, uint actualOid, long actualSystem,
        uint expectedOid, long expectedSystem) =>
        database == TargetDatabase && expectedOid != 0 && expectedSystem > 0 &&
        actualOid == expectedOid && actualSystem == expectedSystem;

    internal static bool MatchesPreMigrationState(string[] applied, string[] pending) =>
        applied.SequenceEqual(ProductionMigrationBaselineEvidence.ExpectedMigrations[..^1]) &&
        pending.Length == 1 && pending[0] == TargetMigration;
}
