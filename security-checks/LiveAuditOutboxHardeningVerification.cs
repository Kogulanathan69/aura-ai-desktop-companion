using System.Data;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

// Explicit disposable-database command. No API registration or automatic execution.
internal static class LiveAuditOutboxHardeningVerification
{
    internal const string Command = "--live-audit-outbox-hardening";
    internal const string EnableVariable = "AURA_LIVE_AUDIT_OUTBOX_VERIFY";
    internal const string DisposableVariable = "AURA_AUDIT_OUTBOX_DB_IS_DISPOSABLE";
    internal const string MigrationVariable = "AURA_ALLOW_AUDIT_OUTBOX_MIGRATION";
    internal const string ConnectionVariable = "ConnectionStrings__DefaultConnection";
    private const string Prefix = "aura_disposable_";
    private const string OutboxMigration = "20261014000003_AddSecurityAuditOutbox";
    private const string HardeningMigration = "20261014000004_HardenSecurityAuditOutboxDelivery";
    private const string OldExpression =
        "(\"DeliveryState\" = 0 AND \"AttemptCount\" = 0 AND \"DeliveredAt\" IS NULL) OR " +
        "(\"DeliveryState\" = 1 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NOT NULL) OR " +
        "(\"DeliveryState\" = 2 AND \"AttemptCount\" > 0)";
    private const string NewExpression =
        "(\"DeliveryState\" = 0 AND \"AttemptCount\" = 0 AND \"DeliveredAt\" IS NULL) OR " +
        "(\"DeliveryState\" = 1 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NOT NULL) OR " +
        "(\"DeliveryState\" = 2 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NULL)";

    internal sealed record Input(string Connection, bool AllowMigration);

    internal static async Task<int> RunAsync(Func<string, string?> environment, Action<string> write,
        Func<Input, Action<string>, CancellationToken, Task<bool>>? execute = null,
        CancellationToken token = default)
    {
        // Short-circuit before reading connection configuration.
        if (environment(EnableVariable) != "true" || environment(DisposableVariable) != "true")
        {
            write("LIVE AUDIT OUTBOX VERIFY: SKIPPED");
            return 0;
        }
        var connection = environment(ConnectionVariable);
        if (!ValidConnection(connection))
        {
            write("DB CONNECTIVITY: FAIL");
            write("RESULT: FAIL");
            return 1;
        }
        try
        {
            var input = new Input(connection!, environment(MigrationVariable) == "true");
            var passed = await (execute ?? ExecuteLiveAsync)(input, write, token);
            write(passed ? "RESULT: PASS" : "RESULT: FAIL");
            return passed ? 0 : 1;
        }
        catch (Exception)
        {
            write("RESULT: FAIL");
            return 1;
        }
    }

    private static bool ValidConnection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(value);
            return !string.IsNullOrWhiteSpace(parsed.Host) &&
                !string.IsNullOrWhiteSpace(parsed.Username) &&
                parsed.Database?.StartsWith(Prefix, StringComparison.Ordinal) == true &&
                parsed.Database.Length > Prefix.Length;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException) { return false; }
    }

    private static async Task<bool> ExecuteLiveAsync(Input input, Action<string> write,
        CancellationToken token)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(input.Connection, npgsql => npgsql.UseVector()).Options;
        var factory = new PooledDbContextFactory<AuraDbContext>(options);
        await using var db = await factory.CreateDbContextAsync(token);
        if (!await db.Database.CanConnectAsync(token))
        { write("DB CONNECTIVITY: FAIL"); return false; }
        write("DB CONNECTIVITY: PASS");

        // No historical baseline exists in the repository. A reviewed disposable
        // baseline and exact ordered migration history are mandatory.
        if (!await TableExistsAsync(db, "__EFMigrationsHistory", token) ||
            !await TableExistsAsync(db, "SecurityAuditOutbox", token) ||
            !await TableExistsAsync(db, "SecurityAuditEvents", token))
        { write("BASELINE CHECK: FAIL"); return false; }
        var applied = (await db.Database.GetAppliedMigrationsAsync(token)).ToArray();
        var expected = new[] { "20261014000000_AddToolPermissionGrants",
            "20261014000001_AddActionExecutionStates",
            "20261014000002_AddSecurityPersistenceBundle", OutboxMigration };
        var alreadyHardened = applied.SequenceEqual(expected.Append(HardeningMigration));
        if (!applied.SequenceEqual(expected) && !alreadyHardened)
        { write("BASELINE CHECK: FAIL"); return false; }
        var before = await ConstraintExpressionAsync(db, token);
        if (before is null || !SameExpression(before, alreadyHardened ? NewExpression : OldExpression))
        { write("BASELINE CHECK: FAIL"); return false; }
        write("BASELINE CHECK: PASS");

        var invalid = await db.SecurityAuditOutbox.AsNoTracking().CountAsync(x =>
            x.DeliveryState == SecurityAuditOutboxDeliveryState.Failed && x.DeliveredAt != null, token);
        if (invalid != 0)
        { write("PRE-MIGRATION DATA CHECK: FAIL"); return false; }
        write("PRE-MIGRATION DATA CHECK: PASS");

        if (!alreadyHardened)
        {
            if (!input.AllowMigration)
            { write("MIGRATION APPLY: SKIPPED"); return false; }
            // Exact history above proves that only Step 13J is pending. Targeting
            // the reviewed ID avoids applying any later migrations.
            var pending = (await db.Database.GetPendingMigrationsAsync(token)).ToArray();
            if (pending.Length != 1 || pending[0] != HardeningMigration)
            { write("MIGRATION APPLY: FAIL"); return false; }
            try
            {
                await db.Database.CloseConnectionAsync();
                await db.GetService<IMigrator>().MigrateAsync(HardeningMigration, token);
            }
            catch (Exception) { write("MIGRATION APPLY: FAIL"); return false; }
            write("MIGRATION APPLY: PASS");
        }
        else write("MIGRATION APPLY: SKIPPED");

        var after = (await db.Database.GetAppliedMigrationsAsync(token)).ToArray();
        var expression = await ConstraintExpressionAsync(db, token);
        if (!after.SequenceEqual(expected.Append(HardeningMigration)) ||
            expression is null || !SameExpression(expression, NewExpression))
        { write("SCHEMA CHECK: FAIL"); return false; }
        write("SCHEMA CHECK: PASS");

        var shapes = await VerifyShapesAsync(factory, write, token);
        if (!shapes.Valid)
        {
            write(shapes.Cleanup ? "CLEANUP: PASS" : "CLEANUP: FAIL");
            return false;
        }

        // Reuse all Step 13I real-dispatcher scenarios after exact schema checks.
        // They include success, Failed and Unavailable sinks, duplicate IDs,
        // concurrency, terminal redispatch, cancellation and binding rejection.
        bool? dispatchCleanup = null;
        var dispatch = false;
        try
        {
            dispatch = await LiveAuditOutboxVerification.ExecuteLiveAsync(input.Connection, token,
                cleaned => dispatchCleanup = cleaned);
        }
        catch (Exception) { dispatch = false; }
        return CompleteDispatch(shapes.Cleanup, dispatchCleanup, dispatch, write);
    }

    // Shared by the live path and offline result checks. Cleanup is a success gate.
    internal static bool CompleteDispatch(bool shapesCleanup, bool? dispatchCleanup,
        bool dispatch, Action<string> write)
    {
        if (!shapesCleanup || dispatchCleanup != true)
        {
            write("CLEANUP: FAIL");
            return false;
        }
        write("CLEANUP: PASS");
        if (!dispatch)
        { write("DELIVERY SUCCESS: FAIL"); return false; }
        write("DELIVERY SUCCESS: PASS");
        write("DELIVERY FAILURE: PASS");
        write("TERMINAL REDISPATCH: PASS");
        return true;
    }

    private static async Task<bool> TableExistsAsync(AuraDbContext db, string table,
        CancellationToken token)
    {
        if (table is not ("__EFMigrationsHistory" or "SecurityAuditOutbox" or "SecurityAuditEvents"))
            return false;
        if (db.Database.GetDbConnection().State != ConnectionState.Open)
            await db.Database.OpenConnectionAsync(token);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT to_regclass('\"{table}\"') IS NOT NULL";
        return (bool)(await command.ExecuteScalarAsync(token) ?? false);
    }

    private static async Task<string?> ConstraintExpressionAsync(AuraDbContext db,
        CancellationToken token)
    {
        if (db.Database.GetDbConnection().State != ConnectionState.Open)
            await db.Database.OpenConnectionAsync(token);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_get_constraintdef(oid) FROM pg_catalog.pg_constraint " +
            "WHERE conrelid = to_regclass('\"SecurityAuditOutbox\"') " +
            "AND conname = 'CK_SecurityAuditOutbox_Delivery' AND contype = 'c'";
        return await command.ExecuteScalarAsync(token) as string;
    }

    // pg_get_constraintdef adds grouping parentheses and spacing. Remove only
    // those presentation characters; keep operators, quoted names and order.
    private static bool SameExpression(string catalog, string expected)
    {
        static string Canonical(string value) => new(value.Where(c =>
            !char.IsWhiteSpace(c) && c is not '(' and not ')').ToArray());
        return Canonical(catalog) == "CHECK" + Canonical(expected);
    }

    private static async Task<(bool Valid, bool Cleanup)> VerifyShapesAsync(IDbContextFactory<AuraDbContext> factory,
        Action<string> write, CancellationToken token)
    {
        var fixtures = new List<LiveAuditOutboxVerification.Fixture>();
        var invalidRejected = false;
        var valid = false;
        var cleanup = false;
        try
        {
            var pending = await LiveAuditOutboxVerification.AddFixtureAsync(factory, fixtures, token);
            await using (var db = await factory.CreateDbContextAsync(token))
            {
                try
                {
                    await db.SecurityAuditOutbox.Where(x => x.Id == pending.Outbox)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.DeliveryState, SecurityAuditOutboxDeliveryState.Failed)
                            .SetProperty(x => x.AttemptCount, 1)
                            .SetProperty(x => x.DeliveredAt, DateTime.UtcNow), token);
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
                { invalidRejected = true; }
            }
            await using (var db = await factory.CreateDbContextAsync(token))
            {
                var row = await db.SecurityAuditOutbox.AsNoTracking()
                    .SingleAsync(x => x.Id == pending.Outbox, token);
                invalidRejected &= row.DeliveryState == SecurityAuditOutboxDeliveryState.Pending &&
                    row.AttemptCount == 0 && row.DeliveredAt is null;
            }
            if (invalidRejected)
            {
                var delivered = await LiveAuditOutboxVerification.AddFixtureAsync(factory, fixtures, token);
                var failed = await LiveAuditOutboxVerification.AddFixtureAsync(factory, fixtures, token);
                await using var db = await factory.CreateDbContextAsync(token);
                var deliveredChanged = await db.SecurityAuditOutbox.Where(x => x.Id == delivered.Outbox)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.DeliveryState, SecurityAuditOutboxDeliveryState.Delivered)
                        .SetProperty(x => x.AttemptCount, 1)
                        .SetProperty(x => x.DeliveredAt, DateTime.UtcNow), token);
                var failedChanged = await db.SecurityAuditOutbox.Where(x => x.Id == failed.Outbox)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.DeliveryState, SecurityAuditOutboxDeliveryState.Failed)
                        .SetProperty(x => x.AttemptCount, 1), token);
                var rows = await db.SecurityAuditOutbox.AsNoTracking()
                    .Where(x => x.Id == pending.Outbox || x.Id == delivered.Outbox || x.Id == failed.Outbox)
                    .ToArrayAsync(token);
                valid = deliveredChanged == 1 && failedChanged == 1 && rows.Length == 3 &&
                    rows.Single(x => x.Id == pending.Outbox) is { DeliveryState: SecurityAuditOutboxDeliveryState.Pending,
                        AttemptCount: 0, DeliveredAt: null } &&
                    rows.Single(x => x.Id == delivered.Outbox).DeliveryState == SecurityAuditOutboxDeliveryState.Delivered &&
                    rows.Single(x => x.Id == delivered.Outbox).AttemptCount == 1 &&
                    rows.Single(x => x.Id == delivered.Outbox).DeliveredAt is not null &&
                    rows.Single(x => x.Id == failed.Outbox) is { DeliveryState: SecurityAuditOutboxDeliveryState.Failed,
                        AttemptCount: 1, DeliveredAt: null };
            }
        }
        catch (Exception) { valid = false; }
        finally
        {
            try { cleanup = await LiveAuditOutboxVerification.CleanupAsync(factory, fixtures, CancellationToken.None); }
            catch (Exception) { cleanup = false; }
        }
        write(invalidRejected ? "INVALID FAILED ROW REJECTION: PASS" : "INVALID FAILED ROW REJECTION: FAIL");
        write(valid ? "VALID STATE SHAPES: PASS" : "VALID STATE SHAPES: FAIL");
        return (invalidRejected && valid && cleanup, cleanup);
    }
}
