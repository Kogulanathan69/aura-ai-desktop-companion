using System.Data;
using Aura.Application.Actions;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal sealed record LivePermissionInput(string ConnectionString, Guid UserId,
    Guid ConversationId, Guid ProjectId, ToolIdentifier ToolId,
    ToolPermissionRequirement Requirement, Guid? GrantId, Guid? RevokedGrantId = null);

internal enum LivePermissionStage
{
    Passed, ConnectivityFailed, SchemaFailed, NoTestGrant,
    LookupFailed, NegativeChecksFailed, UnexpectedFailure,
    ScopeNegativeFailed, ToolNegativeFailed, RevokedNotProven, RevokedFixtureFailed
}

// Explicit command and environment opt-in only. Never called by the offline suite.
internal static class LivePermissionVerification
{
    internal const string Command = "--live-permission";
    internal const string EnableVariable = "AURA_LIVE_PERMISSION_VERIFY";
    internal const string ProductionCommand = "--live-production-permission-evidence";
    internal const string ProductionEnableVariable = "AURA_LIVE_PRODUCTION_PERMISSION_EVIDENCE";
    internal const string DisposableVariable = "AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE";

    internal static async Task<int> RunProductionAsync(Func<string, string?> environment,
        Action<string> write,
        Func<LivePermissionInput, CancellationToken, Task<LivePermissionStage>>? execute = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (environment(ProductionEnableVariable) != "true" ||
                environment(DisposableVariable) != "true")
            {
                write("PRODUCTION PERMISSION EVIDENCE: SKIPPED");
                return 0;
            }
            if (!TryReadProductionInput(environment, out var input))
            {
                write("INPUT: FAIL");
                write("RESULT: FAIL");
                return 1;
            }
            var stage = await (execute ?? ExecuteProductionLiveAsync)(input!, cancellationToken);
            if (stage == LivePermissionStage.Passed && input!.RevokedGrantId is null)
                stage = LivePermissionStage.RevokedNotProven;
            if (stage == LivePermissionStage.Passed)
            {
                foreach (var category in new[] { "DB CONNECTIVITY", "PERMISSION SCHEMA",
                    "EXACT GRANT LOOKUP", "SCOPE NEGATIVE CASES",
                    "TOOL/PERMISSION NEGATIVE CASES", "REVOKED GRANT CHECK" })
                    write(category + ": PASS");
                write("RESULT: PASS");
                return 0;
            }
            if (stage == LivePermissionStage.RevokedNotProven)
            {
                write("REVOKED GRANT CHECK: NOT PROVEN");
                write("RESULT: INCOMPLETE");
                return 2;
            }
            write(stage switch
            {
                LivePermissionStage.ConnectivityFailed => "DB CONNECTIVITY: FAIL",
                LivePermissionStage.SchemaFailed => "PERMISSION SCHEMA: FAIL",
                LivePermissionStage.NoTestGrant or LivePermissionStage.LookupFailed =>
                    "EXACT GRANT LOOKUP: FAIL",
                LivePermissionStage.ScopeNegativeFailed => "SCOPE NEGATIVE CASES: FAIL",
                LivePermissionStage.ToolNegativeFailed => "TOOL/PERMISSION NEGATIVE CASES: FAIL",
                LivePermissionStage.RevokedFixtureFailed => "REVOKED GRANT CHECK: FAIL",
                _ => "RESULT: FAIL"
            });
            write("RESULT: FAIL");
            return 1;
        }
        catch (Exception)
        {
            write("RESULT: FAIL");
            return 1;
        }
    }

    private static bool TryReadProductionInput(Func<string, string?> environment,
        out LivePermissionInput? input)
    {
        input = null;
        if (!TryGuid(environment("AURA_VERIFY_PERMISSION_USER_ID"), out var userId) ||
            !TryGuid(environment("AURA_VERIFY_PERMISSION_CONVERSATION_ID"), out var conversationId) ||
            !TryGuid(environment("AURA_VERIFY_PERMISSION_PROJECT_ID"), out var projectId))
            return false;
        var toolText = environment("AURA_VERIFY_PERMISSION_TOOL_ID");
        if (!ToolIdentifier.TryCreate(toolText, out var toolId) || toolId!.Value != toolText ||
            !TryRequirement(environment("AURA_VERIFY_PERMISSION_REQUIREMENT"), out var requirement))
            return false;
        Guid? grantId = null;
        var grantText = environment("AURA_VERIFY_PERMISSION_GRANT_ID");
        if (!string.IsNullOrWhiteSpace(grantText))
        {
            if (!TryGuid(grantText, out var parsed)) return false;
            grantId = parsed;
        }
        Guid? revokedId = null;
        var revokedText = environment("AURA_VERIFY_REVOKED_PERMISSION_GRANT_ID");
        if (!string.IsNullOrWhiteSpace(revokedText))
        {
            if (!TryGuid(revokedText, out var parsed) || parsed == grantId) return false;
            revokedId = parsed;
        }
        var connection = environment("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection) || !ValidProductionConnection(connection))
            return false;
        input = new(connection, userId, conversationId, projectId, toolId, requirement,
            grantId, revokedId);
        return true;
    }

    private static bool ValidProductionConnection(string connection)
    {
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(connection);
            return !string.IsNullOrWhiteSpace(parsed.Host) &&
                !string.IsNullOrWhiteSpace(parsed.Username) &&
                parsed.Database?.StartsWith("aura_disposable_", StringComparison.Ordinal) == true &&
                parsed.Database.Length > "aura_disposable_".Length;
        }
        catch (Exception) { return false; }
    }

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

    private static async Task<LivePermissionStage> ExecuteProductionLiveAsync(
        LivePermissionInput input, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(input.ConnectionString, npgsql => npgsql.UseVector())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options;
        await using var db = new AuraDbContext(options);
        if (!await db.Database.CanConnectAsync(cancellationToken))
            return LivePermissionStage.ConnectivityFailed;
        if (!await SchemaMatchesAsync(db, cancellationToken))
            return LivePermissionStage.SchemaFailed;
        return await VerifyProductionSourceAsync(db, input, cancellationToken);
    }

    internal static async Task<LivePermissionStage> VerifyProductionSourceAsync(
        Aura.Application.Common.Interfaces.IAuraDbContext db, LivePermissionInput input,
        CancellationToken cancellationToken)
    {
        var sourceStage = await VerifySourceAsync(db, input, cancellationToken, detailed: true);
        if (sourceStage != LivePermissionStage.Passed) return sourceStage;
        var exact = await new ToolPermissionPersistenceSource(db).GetAsync(
            new ToolPermissionValidationRequest(
                new ActionScope(input.UserId, input.ConversationId, input.ProjectId),
                input.ToolId, input.Requirement), cancellationToken);
        if (exact.Status != ToolPermissionLookupStatus.Found || exact.Grant is null ||
            await VerifySourceAsync(db,
                input with { GrantId = OtherGuid(exact.Grant.GrantId) },
                cancellationToken, detailed: true) != LivePermissionStage.LookupFailed)
            return LivePermissionStage.ScopeNegativeFailed;
        if (input.RevokedGrantId is not { } revokedId)
            return LivePermissionStage.RevokedNotProven;

        // The supplied fixture must be a real, separately revoked row. Its own
        // exact tuple must not be accepted by the production source.
        var revoked = await db.ToolPermissionGrants.AsNoTracking()
            .Where(x => x.Id == revokedId)
            .Select(x => new { x.UserId, x.ConversationId, x.ProjectId, x.ToolId,
                x.RequiredPermission, x.Status, x.RevokedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (revoked is null || revoked.Status != Aura.Domain.Entities.ToolPermissionGrantStatus.Revoked ||
            revoked.RevokedAt is null || revoked.UserId == Guid.Empty ||
            revoked.ConversationId == Guid.Empty || revoked.ProjectId == Guid.Empty ||
            !ToolIdentifier.TryCreate(revoked.ToolId, out var revokedTool) ||
            revokedTool!.Value != revoked.ToolId ||
            !Enum.IsDefined(typeof(ToolPermissionRequirement), revoked.RequiredPermission))
            return LivePermissionStage.RevokedFixtureFailed;
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == revoked.UserId && x.IsActive,
                cancellationToken) ||
            !await db.Conversations.AsNoTracking().AnyAsync(x =>
                x.Id == revoked.ConversationId && x.UserId == revoked.UserId &&
                x.ProjectId == revoked.ProjectId && x.Type == "Project", cancellationToken) ||
            !await db.Projects.AsNoTracking().AnyAsync(x =>
                x.Id == revoked.ProjectId && x.UserId == revoked.UserId, cancellationToken))
            return LivePermissionStage.RevokedFixtureFailed;
        var revokedRequest = new ToolPermissionValidationRequest(
            new ActionScope(revoked.UserId, revoked.ConversationId, revoked.ProjectId),
            revokedTool, (ToolPermissionRequirement)revoked.RequiredPermission);
        return (await new ToolPermissionPersistenceSource(db)
            .GetAsync(revokedRequest, cancellationToken)).Status == ToolPermissionLookupStatus.NotFound
            ? LivePermissionStage.Passed : LivePermissionStage.RevokedFixtureFailed;
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
        CancellationToken cancellationToken, bool detailed = false)
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
            request with { Scope = scope with { ProjectId = OtherGuid(input.ProjectId) } } })
            if ((await source.GetAsync(negative, cancellationToken)).Status != ToolPermissionLookupStatus.NotFound)
                return detailed ? LivePermissionStage.ScopeNegativeFailed :
                    LivePermissionStage.NegativeChecksFailed;
        foreach (var negative in new[] {
            request with { ToolId = otherTool! },
            request with { Requirement = otherRequirement } })
            if ((await source.GetAsync(negative, cancellationToken)).Status != ToolPermissionLookupStatus.NotFound)
                return detailed ? LivePermissionStage.ToolNegativeFailed :
                    LivePermissionStage.NegativeChecksFailed;
        return LivePermissionStage.Passed;
    }

    private static Guid OtherGuid(Guid value)
    {
        Guid other;
        do { other = Guid.NewGuid(); } while (other == value);
        return other;
    }
}
