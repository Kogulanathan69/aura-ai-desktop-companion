using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal sealed record LiveOwnershipInput(string ConnectionString, Guid AuthUserId,
    Guid InternalUserId, Guid ConversationId, Guid ProjectId,
    Guid? InactiveAuthUserId = null);

internal enum LiveOwnershipStage
{
    Passed, ConnectivityFailed, MappingFailed, OwnershipFailed,
    NegativeChecksFailed, CompositionFailed, UnexpectedFailure,
    SchemaFailed, AuthNegativeFailed, ProjectFailed, ConversationFailed,
    ScopeNegativeFailed
}

// Invoked only by explicit live commands and their environment gates.
// It never runs during the ordinary offline security suite.
internal static class LiveOwnershipVerification
{
    internal const string Command = "--live-ownership";
    internal const string EnableVariable = "AURA_LIVE_OWNERSHIP_VERIFY";
    internal const string ProductionCommand = "--live-production-ownership-evidence";
    internal const string ProductionEnableVariable = "AURA_LIVE_PRODUCTION_OWNERSHIP_EVIDENCE";
    internal const string DisposableVariable = "AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE";

    // The second command reuses the read-only source verification below. Its two gates
    // are evaluated before reading any connection input or constructing a context.
    internal static async Task<int> RunProductionAsync(Func<string, string?> environment,
        Action<string> write,
        Func<LiveOwnershipInput, CancellationToken, Task<LiveOwnershipStage>>? execute = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (environment(ProductionEnableVariable) != "true" ||
                environment(DisposableVariable) != "true")
            {
                write("PRODUCTION OWNERSHIP EVIDENCE: SKIPPED");
                return 0;
            }

            // Parse identifiers before even reading the connection string.
            if (!TryGuid(environment("AURA_VERIFY_AUTH_USER_ID"), out var authUserId) ||
                !TryGuid(environment("AURA_VERIFY_INTERNAL_USER_ID"), out var internalUserId) ||
                !TryGuid(environment("AURA_VERIFY_PROJECT_ID"), out var projectId) ||
                !TryGuid(environment("AURA_VERIFY_CONVERSATION_ID"), out var conversationId))
            {
                write("INPUT: FAIL");
                return 1;
            }

            var inactiveText = environment("AURA_VERIFY_INACTIVE_AUTH_USER_ID");
            Guid? inactiveId = null;
            if (!string.IsNullOrWhiteSpace(inactiveText))
            {
                if (!TryGuid(inactiveText, out var parsedInactive) || parsedInactive == authUserId)
                {
                    write("INPUT: FAIL");
                    return 1;
                }
                inactiveId = parsedInactive;
            }

            var connection = environment("ConnectionStrings__DefaultConnection");
            if (string.IsNullOrWhiteSpace(connection) || !ValidConnection(connection))
            {
                write("INPUT: FAIL");
                return 1;
            }

            var input = new LiveOwnershipInput(connection, authUserId, internalUserId,
                conversationId, projectId, inactiveId);
            var stage = await (execute ?? ExecuteProductionLiveAsync)(input, cancellationToken);
            if (stage == LiveOwnershipStage.Passed)
            {
                foreach (var category in new[] { "DB CONNECTIVITY", "SCHEMA CHECK",
                    "AUTH SUBJECT MAPPING", "AUTH NEGATIVE CASES", "PROJECT OWNERSHIP",
                    "CONVERSATION OWNERSHIP", "SCOPE NEGATIVE CASES" })
                    write(category + ": PASS");
                write("SOURCE RESULT: PASS");
                write("PRODUCTION OWNERSHIP: INCOMPLETE");
                return 1;
            }

            write(stage switch
            {
                LiveOwnershipStage.ConnectivityFailed => "DB CONNECTIVITY: FAIL",
                LiveOwnershipStage.SchemaFailed => "SCHEMA CHECK: FAIL",
                LiveOwnershipStage.MappingFailed => "AUTH SUBJECT MAPPING: FAIL",
                LiveOwnershipStage.AuthNegativeFailed => "AUTH NEGATIVE CASES: FAIL",
                LiveOwnershipStage.ProjectFailed => "PROJECT OWNERSHIP: FAIL",
                LiveOwnershipStage.ConversationFailed => "CONVERSATION OWNERSHIP: FAIL",
                LiveOwnershipStage.ScopeNegativeFailed => "SCOPE NEGATIVE CASES: FAIL",
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

    private static bool ValidConnection(string value)
    {
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(value);
            return !string.IsNullOrWhiteSpace(parsed.Host) &&
                !string.IsNullOrWhiteSpace(parsed.Database);
        }
        catch (Exception) { return false; }
    }

    internal static async Task<int> RunAsync(Func<string, string?> environment,
        Action<string> write,
        Func<LiveOwnershipInput, CancellationToken, Task<LiveOwnershipStage>>? execute = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (environment(EnableVariable) != "true")
            {
                write("LIVE OWNERSHIP VERIFY: SKIPPED");
                return 0;
            }

            if (!TryReadInput(environment, out var input))
            {
                write("LIVE OWNERSHIP VERIFY: INVALID INPUT");
                return 1;
            }

            var result = await (execute ?? ExecuteLiveAsync)(input!, cancellationToken);
            if (result == LiveOwnershipStage.Passed)
            {
                write("DB CONNECTIVITY: PASS");
                write("AUTH USER MAPPING: PASS");
                write("OWNERSHIP LOOKUP: PASS");
                write("OWNERSHIP NEGATIVE CHECKS: PASS");
                write("COMPOSITION: PASS");
                write("RESULT: PASS");
                return 0;
            }

            write(result switch
            {
                LiveOwnershipStage.ConnectivityFailed => "DB CONNECTIVITY: FAIL",
                LiveOwnershipStage.MappingFailed => "AUTH USER MAPPING: FAIL",
                LiveOwnershipStage.OwnershipFailed => "OWNERSHIP LOOKUP: FAIL",
                LiveOwnershipStage.NegativeChecksFailed => "OWNERSHIP NEGATIVE CHECKS: FAIL",
                LiveOwnershipStage.CompositionFailed => "COMPOSITION: FAIL",
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

    private static bool TryReadInput(Func<string, string?> environment, out LiveOwnershipInput? input)
    {
        input = null;
        var connection = environment("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection) ||
            !TryGuid(environment("AURA_VERIFY_AUTH_USER_ID"), out var authUserId) ||
            !TryGuid(environment("AURA_VERIFY_INTERNAL_USER_ID"), out var internalUserId) ||
            !TryGuid(environment("AURA_VERIFY_CONVERSATION_ID"), out var conversationId) ||
            !TryGuid(environment("AURA_VERIFY_PROJECT_ID"), out var projectId))
            return false;
        input = new(connection, authUserId, internalUserId, conversationId, projectId);
        return true;
    }

    private static bool TryGuid(string? value, out Guid id) =>
        Guid.TryParse(value, out id) && id != Guid.Empty;

    private static async Task<LiveOwnershipStage> ExecuteLiveAsync(LiveOwnershipInput input,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(input.ConnectionString, npgsql => npgsql.UseVector())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        await using var db = new AuraDbContext(options);
        if (!await db.Database.CanConnectAsync(cancellationToken))
            return LiveOwnershipStage.ConnectivityFailed;
        return await VerifySourcesAsync(db, input, cancellationToken);
    }

    private static async Task<LiveOwnershipStage> ExecuteProductionLiveAsync(LiveOwnershipInput input,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(input.ConnectionString, npgsql => npgsql.UseVector())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        await using var db = new AuraDbContext(options);
        if (!await db.Database.CanConnectAsync(cancellationToken))
            return LiveOwnershipStage.ConnectivityFailed;
        try
        {
            // Query the actual configured tables, not merely EF's in-memory model.
            await db.Users.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync(cancellationToken);
            await db.Projects.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync(cancellationToken);
            await db.Conversations.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return LiveOwnershipStage.SchemaFailed; }
        return await VerifyProductionSourcesAsync(db, input, cancellationToken);
    }

    // No SaveChanges, schema changes, cleanup or background work. This method also
    // runs against the existing offline query seam without a database connection.
    internal static async Task<LiveOwnershipStage> VerifyProductionSourcesAsync(IAuraDbContext db,
        LiveOwnershipInput input, CancellationToken cancellationToken)
    {
        var mappingSource = new AuthenticatedUserMappingSource(db);
        var mapping = await mappingSource.GetAsync(input.AuthUserId, cancellationToken);
        if (mapping.Status != AuthenticatedUserMappingStatus.Found ||
            mapping.AuthUserId != input.AuthUserId || mapping.UserId != input.InternalUserId)
            return LiveOwnershipStage.MappingFailed;

        if ((await mappingSource.GetAsync(OtherGuid(input.AuthUserId), cancellationToken)).Status !=
            AuthenticatedUserMappingStatus.NotFound ||
            (await mappingSource.GetAsync(Guid.Empty, cancellationToken)).Status !=
            AuthenticatedUserMappingStatus.NotFound)
            return LiveOwnershipStage.AuthNegativeFailed;
        if (input.InactiveAuthUserId is { } inactiveId &&
            (!await db.Users.AsNoTracking().AnyAsync(u =>
                u.AuthUserId == inactiveId && !u.IsActive, cancellationToken) ||
             (await mappingSource.GetAsync(inactiveId, cancellationToken)).Status !=
                AuthenticatedUserMappingStatus.NotFound))
            return LiveOwnershipStage.AuthNegativeFailed;

        if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == input.ProjectId &&
            p.UserId == input.InternalUserId, cancellationToken))
            return LiveOwnershipStage.ProjectFailed;
        if (!await db.Conversations.AsNoTracking().AnyAsync(c =>
            c.Id == input.ConversationId && c.UserId == input.InternalUserId &&
            c.ProjectId == input.ProjectId && c.Type == "Project", cancellationToken))
            return LiveOwnershipStage.ConversationFailed;

        var source = new OwnershipPersistenceSource(db);
        var exact = new OwnershipLookupRequest(input.InternalUserId,
            input.ConversationId, input.ProjectId);
        var owned = await source.GetAsync(exact, cancellationToken);
        if (owned.Status != OwnershipLookupStatus.Found || owned.Binding != exact ||
            !owned.OwnsProject || !owned.OwnsConversation || !owned.HasExactProjectRelation)
            return LiveOwnershipStage.CompositionFailed;

        var validator = new ExactOwnershipScopeValidator(
            new FixedCurrentUserContext(mapping.UserId), source);
        if (!await validator.ValidateAsync(new ActionScope(input.InternalUserId,
            input.ConversationId, input.ProjectId), cancellationToken))
            return LiveOwnershipStage.CompositionFailed;

        var wrongUser = OtherGuid(input.InternalUserId);
        var wrongProject = OtherGuid(input.ProjectId);
        var wrongConversation = OtherGuid(input.ConversationId);
        foreach (var denied in new[]
        {
            exact with { UserId = wrongUser },
            exact with { ProjectId = wrongProject },
            exact with { ConversationId = wrongConversation }
        })
        {
            if ((await source.GetAsync(denied, cancellationToken)).Status != OwnershipLookupStatus.Denied ||
                await validator.ValidateAsync(new ActionScope(denied.UserId,
                    denied.ConversationId, denied.ProjectId), cancellationToken))
                return LiveOwnershipStage.ScopeNegativeFailed;
        }
        return LiveOwnershipStage.Passed;
    }

    // Also exercised against the existing query-only test seam in the offline suite.
    internal static async Task<LiveOwnershipStage> VerifySourcesAsync(IAuraDbContext db,
        LiveOwnershipInput input, CancellationToken cancellationToken)
    {
        var mapping = await new AuthenticatedUserMappingSource(db)
            .GetAsync(input.AuthUserId, cancellationToken);
        if (mapping.Status != AuthenticatedUserMappingStatus.Found ||
            mapping.AuthUserId != input.AuthUserId || mapping.UserId != input.InternalUserId)
            return LiveOwnershipStage.MappingFailed;

        var source = new OwnershipPersistenceSource(db);
        var request = new OwnershipLookupRequest(input.InternalUserId,
            input.ConversationId, input.ProjectId);
        var ownership = await source.GetAsync(request, cancellationToken);
        if (ownership.Status != OwnershipLookupStatus.Found || ownership.Binding != request ||
            !ownership.OwnsConversation || !ownership.HasExactProjectRelation ||
            !ownership.OwnsProject)
            return LiveOwnershipStage.OwnershipFailed;

        var foreignUser = OtherGuid(input.InternalUserId);
        var foreignConversation = OtherGuid(input.ConversationId);
        var foreignProject = OtherGuid(input.ProjectId);
        foreach (var negative in new[] {
            request with { UserId = foreignUser },
            request with { ConversationId = foreignConversation },
            request with { ProjectId = foreignProject } })
        {
            if ((await source.GetAsync(negative, cancellationToken)).Status != OwnershipLookupStatus.Denied)
                return LiveOwnershipStage.NegativeChecksFailed;
        }

        var validator = new ExactOwnershipScopeValidator(
            new FixedCurrentUserContext(mapping.UserId), source);
        return await validator.ValidateAsync(new ActionScope(input.InternalUserId,
            input.ConversationId, input.ProjectId), cancellationToken)
            ? LiveOwnershipStage.Passed : LiveOwnershipStage.CompositionFailed;
    }

    private static Guid OtherGuid(Guid value)
    {
        Guid other;
        do { other = Guid.NewGuid(); } while (other == value);
        return other;
    }

    private sealed class FixedCurrentUserContext(Guid internalUserId) : ICurrentUserContext
    {
        public Task<CurrentUserSnapshot> GetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new CurrentUserSnapshot(internalUserId,
                CurrentUserStatus.Authenticated));
        }
    }
}
