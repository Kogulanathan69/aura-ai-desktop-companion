using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

internal sealed record LiveOwnershipInput(string ConnectionString, Guid AuthUserId,
    Guid InternalUserId, Guid ConversationId, Guid ProjectId);

internal enum LiveOwnershipStage
{
    Passed, ConnectivityFailed, MappingFailed, OwnershipFailed,
    NegativeChecksFailed, CompositionFailed, UnexpectedFailure
}

// Invoked only by the explicit --live-ownership command and environment opt-in.
// It never runs during the ordinary offline security suite.
internal static class LiveOwnershipVerification
{
    internal const string Command = "--live-ownership";
    internal const string EnableVariable = "AURA_LIVE_OWNERSHIP_VERIFY";

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
