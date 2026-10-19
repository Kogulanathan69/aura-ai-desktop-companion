using System.Globalization;
using Aura.Application.Actions;
using Aura.Application.Verifications;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

// Operator-only disposable database evidence. Normal offline checks never invoke this path.
internal static class LiveIndependentVerificationEvidence
{
    internal const string Command = "--live-production-independent-verification-evidence";
    internal const string EnableVariable = "AURA_LIVE_PRODUCTION_INDEPENDENT_VERIFICATION_EVIDENCE";
    private static readonly string[] Labels = ["DB CONNECTIVITY", "SCHEMA CHECK", "FIXTURE SETUP",
        "AUTHORITATIVE PROJECT OBSERVATION", "INDEPENDENT PROOF CREATION",
        "HANDLER RESULT INDEPENDENCE", "EXACT EXECUTION VERSION", "EXACT PROOF SOURCE",
        "EXACT PROOF VERSION", "VERIFIED ASSESSMENT PERSISTENCE", "NEGATIVE ACTION BINDING",
        "NEGATIVE USER BINDING", "NEGATIVE CONVERSATION BINDING", "NEGATIVE PROJECT BINDING",
        "NEGATIVE TOOL BINDING", "NEGATIVE APPROVAL BINDING", "NEGATIVE EXECUTION BINDING",
        "STALE EXECUTION VERSION", "WRONG PROOF SOURCE", "WRONG PROOF VERSION",
        "MISSING PROOF", "FAILED EXECUTION", "REPEATED ASSESSMENT",
        "STATE IMMUTABILITY", "CLEANUP"];

    internal static async Task<int> RunAsync(Func<string, string?> environment, Action<string> write,
        Func<string, uint, long, CancellationToken, Task<bool[]>>? execute = null,
        CancellationToken token = default)
    {
        if (environment(EnableVariable) != "true" ||
            environment(LiveIndependentVerificationOutcomeMigrationApplication.DisposableVariable) != "true")
        {
            write("RESULT: SKIPPED");
            return 2;
        }
        var connection = environment(LiveIndependentVerificationOutcomeMigrationApplication.ConnectionVariable);
        var oidOk = uint.TryParse(environment(LiveIndependentVerificationOutcomeMigrationApplication.ExpectedOidVariable),
            NumberStyles.None, CultureInfo.InvariantCulture, out var oid) && oid != 0;
        var systemOk = long.TryParse(environment(LiveIndependentVerificationOutcomeMigrationApplication.ExpectedSystemVariable),
            NumberStyles.None, CultureInfo.InvariantCulture, out var system) && system > 0;
        var allowLocal = environment(LiveIndependentVerificationOutcomeMigrationApplication.AllowLoopbackNonTlsVariable) == "true";
        if (!oidOk || !systemOk || !LiveIndependentVerificationOutcomeMigrationApplication.ValidConnection(
                connection, true, allowLocal) ||
            new NpgsqlConnectionStringBuilder(connection!).SslMode == SslMode.Disable &&
                (oid != 24576 || system != 7696813047573034172))
        {
            write("INPUT: FAIL");
            write("RESULT: FAIL");
            return 1;
        }
        bool[] results;
        try { results = await (execute ?? ExecuteAsync)(connection!, oid, system, token); }
        catch (Exception) { write("RESULT: FAIL"); return 1; }
        for (var i = 0; i < Labels.Length; i++)
            write(Labels[i] + ": " + (i < results.Length && results[i] ? "PASS" : "FAIL"));
        var passed = results.Length == Labels.Length && results.All(x => x);
        write("RESULT: " + (passed ? "PASS" : "INCOMPLETE"));
        return passed ? 0 : 2;
    }

    private static async Task<bool[]> ExecuteAsync(string connection, uint oid, long system,
        CancellationToken token)
    {
        var result = new bool[Labels.Length];
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(connection, n => n.UseVector()).Options;
        var factory = new PooledDbContextFactory<AuraDbContext>(options);
        var fixtures = new List<LiveAuditOutboxVerification.Fixture>();
        var assessmentIds = new List<Guid>();
        try
        {
            await using (var db = await factory.CreateDbContextAsync(token))
            {
                await db.Database.OpenConnectionAsync(token);
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT current_database(), (SELECT oid FROM pg_database WHERE datname = current_database()), (SELECT system_identifier FROM pg_control_system())";
                await using var reader = await command.ExecuteReaderAsync(token);
                result[0] = await reader.ReadAsync(token) &&
                    LiveIndependentVerificationOutcomeMigrationApplication.MatchesIdentity(
                        reader.GetString(0), Convert.ToUInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                        Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture), oid, system);
                if (!result[0]) return result;
                await reader.DisposeAsync();
                var history = (await db.Database.GetAppliedMigrationsAsync(token)).ToArray();
                result[1] = history.SequenceEqual(ProductionMigrationBaselineEvidence.ExpectedMigrations);
                if (!result[1]) return result;
            }
            var first = await LiveAuditOutboxVerification.AddFixtureAsync(factory, fixtures, token,
                tool: "owned-project-metadata-read");
            var second = await LiveAuditOutboxVerification.AddFixtureAsync(factory, fixtures, token,
                tool: "owned-project-metadata-read");
            result[2] = await CompleteAsync(factory, first, DurableExecutionStatus.Succeeded, token) &&
                await CompleteAsync(factory, second, DurableExecutionStatus.Failed, token);
            if (!result[2]) return result;
            var source = new OwnedProjectMetadataExecutionEvidenceSource(factory);
            var scope = new ActionScope(first.User, first.Conversation, first.Project);
            var input = await source.GetAsync(scope, ActionIdentifier.RestoreTrusted(first.Action),
                first.Execution, token);
            var observation = input?.ProjectObservation;
            var proof = input?.IndependentProof;
            result[3] = observation is not null &&
                observation.ProjectId == first.Project && observation.UpdatedAt.Kind == DateTimeKind.Utc;
            result[4] = proof is not null &&
                proof.ProjectId == first.Project && proof.ProjectUpdatedAt == observation?.UpdatedAt;
            result[5] = input?.Outcome == ExecutionReportOutcome.Succeeded &&
                input.IndependentProof is not null; // Source has no handler-result argument.
            result[6] = input?.ExecutionVersion == 2 && proof?.ExecutionVersion == 2;
            result[7] = proof?.ProofSource == VerificationProofSource.OwnedProjectMetadata;
            result[8] = proof?.ProofVersion == 1;
            if (!result.Skip(3).Take(6).All(x => x)) return result;
            var writer = new ExecutionVerificationAssessmentPersistenceWriter(factory);
            var evidence = VerificationEvidence.Issue(input!, VerificationOutcome.Verified, DateTime.UtcNow);
            assessmentIds.Add(evidence.Id.Value);
            result[9] = await writer.WriteAsync(evidence, 2, token) &&
                await AssessmentMatchesAsync(factory, evidence.Id.Value, first, token);
            if (!result[9]) return result;
            // Exact source queries reject alternate IDs before proof issuance.
            result[10] = await source.GetAsync(scope, ActionIdentifier.RestoreTrusted(second.Action), first.Execution, token) is null;
            result[11] = await source.GetAsync(new(second.User, first.Conversation, first.Project),
                input!.ActionId, first.Execution, token) is null;
            result[12] = await source.GetAsync(new(first.User, second.Conversation, first.Project),
                input!.ActionId, first.Execution, token) is null;
            result[13] = await source.GetAsync(new(first.User, first.Conversation, second.Project),
                input!.ActionId, first.Execution, token) is null;
            Aura.Application.Tools.ToolIdentifier.TryCreate("different-tool", out var wrongTool);
            result[14] = wrongTool is not null && ThrowsInvalidVerified(input with { ToolId = wrongTool });
            var wrongApproval = VerificationEvidence.Issue(input with { ApprovalId = second.Approval },
                VerificationOutcome.Verified, DateTime.UtcNow);
            assessmentIds.Add(wrongApproval.Id.Value);
            result[15] = !await writer.WriteAsync(wrongApproval, 2, token);
            result[16] = await source.GetAsync(scope, input.ActionId, second.Execution, token) is null;
            result[17] = !await writer.WriteAsync(evidence, 1, token);
            // Source and version are fixed get-only properties of the sealed proof.
            result[18] = proof!.ProofSource == VerificationProofSource.OwnedProjectMetadata &&
                typeof(OwnedProjectMetadataProof).GetProperty(nameof(proof.ProofSource))!.SetMethod is null;
            result[19] = proof.ProofVersion == 1 &&
                typeof(OwnedProjectMetadataProof).GetProperty(nameof(proof.ProofVersion))!.SetMethod is null;
            var noProof = input with { IndependentProof = null };
            result[20] = ThrowsInvalidVerified(noProof);
            var failed = await source.GetAsync(new(second.User, second.Conversation, second.Project),
                ActionIdentifier.RestoreTrusted(second.Action), second.Execution, token);
            result[21] = failed?.Outcome == ExecutionReportOutcome.Failed && failed.IndependentProof is null &&
                ThrowsInvalidVerified(failed);
            var repeated = VerificationEvidence.Issue(input, VerificationOutcome.Verified, DateTime.UtcNow);
            assessmentIds.Add(repeated.Id.Value);
            result[22] = repeated.Id.Value != evidence.Id.Value &&
                await writer.WriteAsync(repeated, 2, token) &&
                await AssessmentMatchesAsync(factory, repeated.Id.Value, first, token);
            await using (var db = await factory.CreateDbContextAsync(token))
            {
                var state = await db.ActionExecutionStates.AsNoTracking().SingleAsync(x =>
                    x.ActionId == first.Action && x.UserId == first.User, token);
                result[23] = state.Status == DurableExecutionStatus.Succeeded &&
                    state.Version == 2 && state.ExecutionId == first.Execution &&
                    state.ApprovalId == first.Approval;
            }
        }
        catch (Exception) { /* Missing category remains FAIL; cleanup still runs. */ }
        finally
        {
            try
            {
                await using var db = await factory.CreateDbContextAsync(CancellationToken.None);
                foreach (var id in assessmentIds)
                    await db.ExecutionVerificationAssessments.Where(x => x.Id == id)
                        .ExecuteDeleteAsync(CancellationToken.None);
                var rowsGone = !await db.ExecutionVerificationAssessments.AsNoTracking()
                    .AnyAsync(x => assessmentIds.Contains(x.Id), CancellationToken.None);
                var fixturesGone = await LiveAuditOutboxVerification.CleanupAsync(
                    factory, fixtures, CancellationToken.None);
                result[24] = rowsGone && fixturesGone;
            }
            catch (Exception) { result[24] = false; }
        }
        return result;
    }

    private static bool ThrowsInvalidVerified(ExecutionEvidenceInput input)
    {
        try { VerificationEvidence.Issue(input, VerificationOutcome.Verified, DateTime.UtcNow); return false; }
        catch (InvalidOperationException) { return true; }
    }

    private static async Task<bool> CompleteAsync(IDbContextFactory<AuraDbContext> factory,
        LiveAuditOutboxVerification.Fixture fixture, DurableExecutionStatus status, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.ActionExecutionStates.Where(x => x.ActionId == fixture.Action &&
            x.UserId == fixture.User && x.Status == DurableExecutionStatus.Reserved && x.Version == 1)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)
                .SetProperty(x => x.Version, 2L)
                .SetProperty(x => x.CompletedAt, DateTime.UtcNow), token) == 1;
    }

    private static async Task<bool> AssessmentMatchesAsync(IDbContextFactory<AuraDbContext> factory,
        Guid id, LiveAuditOutboxVerification.Fixture fixture, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var row = await db.ExecutionVerificationAssessments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, token);
        return row is not null && row.ActionId == fixture.Action && row.UserId == fixture.User &&
            row.ConversationId == fixture.Conversation && row.ProjectId == fixture.Project &&
            row.ToolId == "owned-project-metadata-read" && row.ApprovalId == fixture.Approval &&
            row.ExecutionId == fixture.Execution && row.ExecutionVersion == 2 &&
            row.ExecutionStatus == DurableExecutionStatus.Succeeded &&
            row.Outcome == (int)VerificationOutcome.Verified &&
            row.ProofSource == VerificationProofSource.OwnedProjectMetadata && row.ProofVersion == 1;
    }
}
