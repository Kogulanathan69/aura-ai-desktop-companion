using System.Data;
using Aura.Application.Actions;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal sealed record LivePermissionInput(string ConnectionString, Guid UserId,
    Guid ConversationId, Guid ProjectId, ToolIdentifier ToolId,
    ToolPermissionRequirement Requirement, Guid? GrantId);

internal enum LivePermissionStage
{
    Passed, ConnectivityFailed, SchemaFailed, NoTestGrant,
    LookupFailed, NegativeChecksFailed, UnexpectedFailure
}

// Explicit command and environment opt-in only. Never called by the offline suite.
internal static class LivePermissionVerification
{
    internal const string Command = "--live-permission";
    internal const string EnableVariable = "AURA_LIVE_PERMISSION_VERIFY";

    internal static async Task<int> RunAsync(Func<string, string?> environment, Action<string> write,
        Func<LivePermissionInput, CancellationToken, Task<LivePermissionStage>>? execute = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (environment(EnableVariable) != "true")
            {
                write("LIVE PERMISSION VERIFY: SKIPPED");
                return 0;
            }
            if (!TryReadInput(environment, out var input))
            {
                write("LIVE PERMISSION VERIFY: INVALID INPUT");
                return 1;
            }
            var stage = await (execute ?? ExecuteLiveAsync)(input!, cancellationToken);
            if (stage == LivePermissionStage.Passed)
            {
                write("DB CONNECTIVITY: PASS");
                write("SCHEMA CHECK: PASS");
                write("PERMISSION LOOKUP: PASS");
                write("NEGATIVE CHECKS: PASS");
                write("RESULT: PASS");
                return 0;
            }
            write(stage switch
            {
                LivePermissionStage.ConnectivityFailed => "DB CONNECTIVITY: FAIL",
                LivePermissionStage.SchemaFailed => "SCHEMA CHECK: FAIL",
                LivePermissionStage.NoTestGrant => "PERMISSION LOOKUP: NO TEST GRANT",
                LivePermissionStage.LookupFailed => "PERMISSION LOOKUP: FAIL",
                LivePermissionStage.NegativeChecksFailed => "NEGATIVE CHECKS: FAIL",
                _ => "RESULT: FAIL"
            });
            return 1;
        }
        catch (Exception)
        {
            write("RESULT: FAIL");
            return 1;
        }
    }

    private static bool TryReadInput(Func<string, string?> environment, out LivePermissionInput? input)
    {
        input = null;
        var connection = environment("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection) || !ValidConnectionString(connection) ||
            !TryGuid(environment("AURA_VERIFY_PERMISSION_USER_ID"), out var userId) ||
            !TryGuid(environment("AURA_VERIFY_PERMISSION_CONVERSATION_ID"), out var conversationId) ||
            !TryGuid(environment("AURA_VERIFY_PERMISSION_PROJECT_ID"), out var projectId) ||
            !ToolIdentifier.TryCreate(environment("AURA_VERIFY_PERMISSION_TOOL_ID"), out var toolId) ||
            !TryRequirement(environment("AURA_VERIFY_PERMISSION_REQUIREMENT"), out var requirement))
            return false;
        var grantText = environment("AURA_VERIFY_PERMISSION_GRANT_ID");
        Guid? grantId = null;
        if (grantText is not null)
        {
            if (!TryGuid(grantText, out var parsed)) return false;
            grantId = parsed;
        }
        input = new(connection, userId, conversationId, projectId, toolId!, requirement, grantId);
        return true;
    }

    private static bool ValidConnectionString(string value)
    {
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(value);
            return !string.IsNullOrWhiteSpace(parsed.Host) &&
                !string.IsNullOrWhiteSpace(parsed.Database);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return false;
        }
    }

    private static bool TryGuid(string? text, out Guid value) =>
        Guid.TryParse(text, out value) && value != Guid.Empty;

    private static bool TryRequirement(string? text, out ToolPermissionRequirement requirement)
    {
        requirement = text switch
        {
            nameof(ToolPermissionRequirement.OwnedProjectRead) => ToolPermissionRequirement.OwnedProjectRead,
            nameof(ToolPermissionRequirement.OwnedProjectWrite) => ToolPermissionRequirement.OwnedProjectWrite,
            nameof(ToolPermissionRequirement.ExternalAction) => ToolPermissionRequirement.ExternalAction,
            _ => default
        };
        return text is nameof(ToolPermissionRequirement.OwnedProjectRead) or
            nameof(ToolPermissionRequirement.OwnedProjectWrite) or
            nameof(ToolPermissionRequirement.ExternalAction);
    }

    private static async Task<LivePermissionStage> ExecuteLiveAsync(LivePermissionInput input,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(input.ConnectionString, npgsql => npgsql.UseVector())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options;
        await using var db = new AuraDbContext(options);
        if (!await db.Database.CanConnectAsync(cancellationToken))
            return LivePermissionStage.ConnectivityFailed;
        if (!await SchemaMatchesAsync(db, cancellationToken))
            return LivePermissionStage.SchemaFailed;
        return await VerifySourceAsync(db, input, cancellationToken);
    }

    // Catalog reads only. This step never calls Migrate, EnsureCreated, or SaveChanges.
    private static async Task<bool> SchemaMatchesAsync(AuraDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        async Task<string[]> ReadAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql; // Fixed catalog query, never composed from inputs.
            command.CommandType = CommandType.Text;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var values = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
                values.Add(reader.GetString(0));
            return values.ToArray();
        }
        var table = await ReadAsync("SELECT COALESCE(to_regclass('\"ToolPermissionGrants\"')::text, '')");
        if (table.Length != 1 || table[0].Length == 0) return false;
        var columns = await ReadAsync("SELECT attname FROM pg_catalog.pg_attribute " +
            "WHERE attrelid = to_regclass('\"ToolPermissionGrants\"') AND attnum > 0 AND NOT attisdropped");
        if (!new[] { "Id", "UserId", "ConversationId", "ProjectId", "ToolId",
                "RequiredPermission", "Status", "RevokedAt", "CreatedAt", "GrantedAt" }
            .All(columns.Contains)) return false;
        var constraints = await ReadAsync("SELECT conname FROM pg_catalog.pg_constraint " +
            "WHERE conrelid = to_regclass('\"ToolPermissionGrants\"')");
        if (!new[] { "PK_ToolPermissionGrants", "CK_ToolPermissionGrants_NonemptyIds",
                "CK_ToolPermissionGrants_ToolId", "CK_ToolPermissionGrants_RequiredPermission",
                "CK_ToolPermissionGrants_Status", "FK_ToolPermissionGrants_Users_UserId",
                "FK_ToolPermissionGrants_Conversations_ConversationId",
                "FK_ToolPermissionGrants_Projects_ProjectId" }.All(constraints.Contains)) return false;
        var indexes = await ReadAsync("SELECT index_class.relname FROM pg_catalog.pg_index index_data " +
            "JOIN pg_catalog.pg_class index_class ON index_class.oid = index_data.indexrelid " +
            "WHERE index_data.indrelid = to_regclass('\"ToolPermissionGrants\"') " +
            "AND index_data.indisunique AND index_data.indpred IS NOT NULL");
        if (!indexes.Contains("UX_ToolPermissionGrants_ActiveExactGrant")) return false;
        var history = await ReadAsync("SELECT COALESCE(to_regclass('\"__EFMigrationsHistory\"')::text, '')");
        if (history.Length != 1 || history[0].Length == 0) return false;
        var applied = await ReadAsync("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" " +
            "WHERE \"MigrationId\" = '20261014000000_AddToolPermissionGrants'");
        return applied.Length == 1;
    }

    internal static async Task<LivePermissionStage> VerifySourceAsync(
        Aura.Application.Common.Interfaces.IAuraDbContext db, LivePermissionInput input,
        CancellationToken cancellationToken)
    {
        var source = new ToolPermissionPersistenceSource(db);
        var scope = new ActionScope(input.UserId, input.ConversationId, input.ProjectId);
        var request = new ToolPermissionValidationRequest(scope, input.ToolId, input.Requirement);
        var positive = await source.GetAsync(request, cancellationToken);
        if (positive.Status == ToolPermissionLookupStatus.NotFound)
            return LivePermissionStage.NoTestGrant;
        if (positive.Status != ToolPermissionLookupStatus.Found || positive.Grant is null ||
            positive.Grant.GrantId == Guid.Empty ||
            input.GrantId is { } expectedGrant && positive.Grant.GrantId != expectedGrant ||
            positive.Grant.Scope != scope || positive.Grant.ToolId != input.ToolId ||
            positive.Grant.Requirement != input.Requirement || !positive.Grant.IsActive ||
            positive.Grant.IsRevoked)
            return LivePermissionStage.LookupFailed;

        ToolIdentifier.TryCreate("aura-negative-" + Guid.NewGuid().ToString("N")[..8], out var otherTool);
        var otherRequirement = input.Requirement == ToolPermissionRequirement.OwnedProjectRead
            ? ToolPermissionRequirement.OwnedProjectWrite : ToolPermissionRequirement.OwnedProjectRead;
        foreach (var negative in new[] {
            request with { Scope = scope with { UserId = OtherGuid(input.UserId) } },
            request with { Scope = scope with { ConversationId = OtherGuid(input.ConversationId) } },
            request with { Scope = scope with { ProjectId = OtherGuid(input.ProjectId) } },
            request with { ToolId = otherTool! },
            request with { Requirement = otherRequirement } })
            if ((await source.GetAsync(negative, cancellationToken)).Status != ToolPermissionLookupStatus.NotFound)
                return LivePermissionStage.NegativeChecksFailed;
        return LivePermissionStage.Passed;
    }

    private static Guid OtherGuid(Guid value)
    {
        Guid other;
        do { other = Guid.NewGuid(); } while (other == value);
        return other;
    }
}
