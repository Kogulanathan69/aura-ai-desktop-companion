using Aura.Application.Verifications;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

internal enum LiveVerificationStage
{
    Observed, Connectivity, Schema, Fixture, Persistence, ProofRules,
    ConservativeOutcome, RepeatedAssessment, StateImmutability, Cleanup, Unexpected
}

// Explicit disposable-DB evidence only. No independent production proof adapter exists.
internal static class LiveProductionVerificationEvidence
{
    internal const string Command = "--live-production-verification-evidence";
    internal const string EnableVariable = "AURA_LIVE_PRODUCTION_VERIFICATION_EVIDENCE";
    internal const string DisposableVariable = "AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE";
    private const string ConnectionVariable = "ConnectionStrings__DefaultConnection";

    private static readonly (LiveVerificationStage Stage, string Label)[] Stages =
    [
        (LiveVerificationStage.Connectivity, "DB CONNECTIVITY"),
        (LiveVerificationStage.Schema, "SCHEMA CHECK"),
        (LiveVerificationStage.Fixture, "FIXTURE SETUP"),
        (LiveVerificationStage.Persistence, "ASSESSMENT PERSISTENCE"),
        (LiveVerificationStage.ProofRules, "PROOF SOURCE RULES"),
        (LiveVerificationStage.ConservativeOutcome, "CONSERVATIVE OUTCOME"),
        (LiveVerificationStage.RepeatedAssessment, "REPEATED ASSESSMENT"),
        (LiveVerificationStage.StateImmutability, "STATE IMMUTABILITY"),
        (LiveVerificationStage.Cleanup, "CLEANUP")
    ];

    internal static async Task<int> RunAsync(Func<string, string?> environment, Action<string> write,
        Func<string, CancellationToken, Task<LiveVerificationStage>>? execute = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (environment(EnableVariable) != "true" || environment(DisposableVariable) != "true")
            {
                write("PRODUCTION VERIFICATION EVIDENCE: SKIPPED");
                return 0;
            }
            var connection = environment(ConnectionVariable);
            if (!ValidConnection(connection))
            {
                write("DB CONNECTIVITY: FAIL");
                write("RESULT: FAIL");
                return 1;
            }
            var stage = await (execute ?? ExecuteLiveAsync)(connection!, cancellationToken);
            if (stage == LiveVerificationStage.Observed)
            {
                foreach (var (_, label) in Stages) write(label + ": PASS");
                write("NEGATIVE BINDINGS: PARTIAL");
                write("EXECUTION VERSION BINDING: NOT PROVEN");
                write("INDEPENDENT PROOF ADAPTER: NOT PROVEN");
                write("RESULT: INCOMPLETE");
                return 2;
            }
            var category = Stages.FirstOrDefault(item => item.Stage == stage).Label;
            if (category is not null) write(category + ": FAIL");
            write("RESULT: FAIL");
            return 1;
        }
        catch (Exception)
        {
            write("RESULT: FAIL");
            return 1;
        }
    }

    private static bool ValidConnection(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection)) return false;
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

    private static async Task<LiveVerificationStage> ExecuteLiveAsync(string connection,
        CancellationToken token)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.UseVector()).Options;
        var factory = new PooledDbContextFactory<AuraDbContext>(options);
        try
        {
            await using var db = await factory.CreateDbContextAsync(token);
            if (!await db.Database.CanConnectAsync(token)) return LiveVerificationStage.Connectivity;
        }
        catch (Exception) { return LiveVerificationStage.Connectivity; }
        try
        {
            await using var db = await factory.CreateDbContextAsync(token);
            if (!await SchemaReadyAsync(db, token)) return LiveVerificationStage.Schema;
        }
        catch (Exception) { return LiveVerificationStage.Schema; }

        var fixtures = new List<LiveAuditOutboxVerification.Fixture>();
        var assessmentIds = new List<Guid>();
        var stage = LiveVerificationStage.Observed;
        var setupComplete = false;
        try
        {
            var succeeded = await LiveAuditOutboxVerification.AddFixtureAsync(factory, fixtures, token);
            var failed = await LiveAuditOutboxVerification.AddFixtureAsync(factory, fixtures, token);
            if (!await CompleteFixtureAsync(factory, succeeded, DurableExecutionStatus.Succeeded, token) ||
                !await CompleteFixtureAsync(factory, failed, DurableExecutionStatus.Failed, token))
                stage = LiveVerificationStage.Fixture;
            else
            {
                setupComplete = true;
                stage = await VerifyAsync(factory, succeeded, failed, assessmentIds, token);
            }
        }
        catch (Exception) { stage = setupComplete ? LiveVerificationStage.Unexpected : LiveVerificationStage.Fixture; }
        finally
        {
            try
            {
                if (!await CleanupAsync(factory, fixtures, assessmentIds, CancellationToken.None))
                    stage = LiveVerificationStage.Cleanup;
            }
            catch (Exception) { stage = LiveVerificationStage.Cleanup; }
        }
        return stage;
    }

    private static async Task<bool> SchemaReadyAsync(AuraDbContext db, CancellationToken token)
    {
        var migrations = await db.Database.GetAppliedMigrationsAsync(token);
        if (!migrations.Contains("20261014000002_AddSecurityPersistenceBundle") ||
            !migrations.Contains("20261014000003_AddSecurityAuditOutbox") ||
            !migrations.Contains("20261014000004_HardenSecurityAuditOutboxDelivery")) return false;
        await db.Database.OpenConnectionAsync(token);
        foreach (var table in new[] { "__EFMigrationsHistory", "Users", "Projects", "Conversations",
            "AIActions", "ActionApprovals", "ActionExecutionStates",
            "ExecutionVerificationAssessments", "SecurityAuditOutbox" })
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT to_regclass(@table) IS NOT NULL";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "table";
            parameter.Value = "\"" + table + "\"";
            command.Parameters.Add(parameter);
            if (await command.ExecuteScalarAsync(token) is not true) return false;
        }
        return true;
    }

    private static async Task<bool> CompleteFixtureAsync(IDbContextFactory<AuraDbContext> factory,
        LiveAuditOutboxVerification.Fixture fixture, DurableExecutionStatus status, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.ActionExecutionStates
            .Where(x => x.ActionId == fixture.Action && x.UserId == fixture.User &&
                x.Status == DurableExecutionStatus.Reserved && x.Version == 1)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.Version, 2L)
                .SetProperty(x => x.CompletedAt, DateTime.UtcNow), token) == 1;
    }

    private static async Task<LiveVerificationStage> VerifyAsync(
        IDbContextFactory<AuraDbContext> factory,
        LiveAuditOutboxVerification.Fixture succeeded,
        LiveAuditOutboxVerification.Fixture failed,
        List<Guid> assessmentIds, CancellationToken token)
    {
        if (!await StateMatchesAsync(factory, succeeded, DurableExecutionStatus.Succeeded, token) ||
            !await StateMatchesAsync(factory, failed, DurableExecutionStatus.Failed, token))
            return LiveVerificationStage.Fixture;

        // Only schema-permitted advisory rows are written. No independent proof is minted.
        var first = Record(succeeded, DurableExecutionStatus.Succeeded,
            VerificationOutcome.Inconclusive);
        var second = Record(failed, DurableExecutionStatus.Failed, VerificationOutcome.Failed);
        var repeated = Record(succeeded, DurableExecutionStatus.Succeeded,
            VerificationOutcome.Inconclusive);
        await using (var db = await factory.CreateDbContextAsync(token))
        {
            foreach (var row in new[] { first, second, repeated })
            {
                assessmentIds.Add(row.Id);
                db.ExecutionVerificationAssessments.Add(row);
            }
            if (await db.SaveChangesAsync(token) != 3) return LiveVerificationStage.Persistence;
        }
        await using (var db = await factory.CreateDbContextAsync(token))
        {
            var rows = await db.ExecutionVerificationAssessments.AsNoTracking()
                .Where(x => assessmentIds.Contains(x.Id)).ToArrayAsync(token);
            if (rows.Length != 3 || rows.Count(x => x.ActionId == succeeded.Action) != 2 ||
                rows.Count(x => x.ActionId == failed.Action) != 1 ||
                !rows.Where(x => x.ActionId == succeeded.Action)
                    .All(x => Matches(x, succeeded, DurableExecutionStatus.Succeeded,
                        VerificationOutcome.Inconclusive)) ||
                !rows.Where(x => x.ActionId == failed.Action)
                    .All(x => Matches(x, failed, DurableExecutionStatus.Failed,
                        VerificationOutcome.Failed)))
                return LiveVerificationStage.Persistence;
        }
        if (!await InvalidRowRejectedAsync(factory, first, assessmentIds,
                row => row.Outcome = (int)VerificationOutcome.Verified, token) ||
            !await InvalidRowRejectedAsync(factory, first, assessmentIds,
                row => row.ProofSource = (VerificationProofSource)999, token) ||
            !await InvalidRowRejectedAsync(factory, first, assessmentIds,
                row => row.ProofVersion = 1, token))
            return LiveVerificationStage.ProofRules;

        var policy = new ConservativeVerificationPolicy();
        var completed = DateTime.UtcNow;
        var scope = new Aura.Application.Actions.ActionScope(succeeded.User,
            succeeded.Conversation, succeeded.Project);
        Aura.Application.Tools.ToolIdentifier.TryCreate("audit-outbox-check", out var tool);
        var action = (Aura.Application.Actions.ActionIdentifier)Activator.CreateInstance(
            typeof(Aura.Application.Actions.ActionIdentifier),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, [succeeded.Action], null)!;
        async Task<VerificationOutcome> Assess(ExecutionReportOutcome report) =>
            await policy.EvaluateAsync(new(succeeded.Execution, action, scope, tool!,
                succeeded.Approval, report, completed), token);
        if (await Assess(ExecutionReportOutcome.Succeeded) != VerificationOutcome.Inconclusive ||
            await Assess(ExecutionReportOutcome.Failed) != VerificationOutcome.Failed ||
            await Assess(ExecutionReportOutcome.Unknown) != VerificationOutcome.Inconclusive ||
            await new UnavailableExecutionEvidenceSource().GetAsync(scope, succeeded.Execution, token)
                is not null)
            return LiveVerificationStage.ConservativeOutcome;

        if (first.Id == repeated.Id || !await StateMatchesAsync(factory, succeeded,
                DurableExecutionStatus.Succeeded, token))
            return LiveVerificationStage.RepeatedAssessment;
        if (!await StateMatchesAsync(factory, succeeded, DurableExecutionStatus.Succeeded, token) ||
            !await StateMatchesAsync(factory, failed, DurableExecutionStatus.Failed, token))
            return LiveVerificationStage.StateImmutability;
        return LiveVerificationStage.Observed;
    }

    private static ExecutionVerificationAssessmentRecord Record(
        LiveAuditOutboxVerification.Fixture fixture, DurableExecutionStatus status,
        VerificationOutcome outcome) => new()
    {
        Id = Guid.NewGuid(), ActionId = fixture.Action, UserId = fixture.User,
        ConversationId = fixture.Conversation, ProjectId = fixture.Project,
        ToolId = "audit-outbox-check", ApprovalId = fixture.Approval,
        ExecutionId = fixture.Execution, ExecutionStatus = status,
        Outcome = (int)outcome, ProofSource = VerificationProofSource.None,
        ProofVersion = 0, AssessedAt = DateTime.UtcNow
    };

    private static bool Matches(ExecutionVerificationAssessmentRecord row,
        LiveAuditOutboxVerification.Fixture fixture, DurableExecutionStatus status,
        VerificationOutcome outcome) =>
        row.ActionId == fixture.Action && row.UserId == fixture.User &&
        row.ConversationId == fixture.Conversation && row.ProjectId == fixture.Project &&
        row.ToolId == "audit-outbox-check" && row.ApprovalId == fixture.Approval &&
        row.ExecutionId == fixture.Execution && row.ExecutionStatus == status &&
        row.Outcome == (int)outcome && row.ProofSource == VerificationProofSource.None &&
        row.ProofVersion == 0 && row.AssessedAt.Kind == DateTimeKind.Utc;

    private static async Task<bool> InvalidRowRejectedAsync(IDbContextFactory<AuraDbContext> factory,
        ExecutionVerificationAssessmentRecord valid, List<Guid> assessmentIds,
        Action<ExecutionVerificationAssessmentRecord> invalidate, CancellationToken token)
    {
        var row = new ExecutionVerificationAssessmentRecord {
            Id = Guid.NewGuid(), ActionId = valid.ActionId, UserId = valid.UserId,
            ConversationId = valid.ConversationId, ProjectId = valid.ProjectId,
            ToolId = valid.ToolId, ApprovalId = valid.ApprovalId,
            ExecutionId = valid.ExecutionId, ExecutionStatus = valid.ExecutionStatus,
            Outcome = valid.Outcome, ProofSource = valid.ProofSource,
            ProofVersion = valid.ProofVersion, AssessedAt = valid.AssessedAt };
        assessmentIds.Add(row.Id); // Track even an unexpected insert for exact cleanup.
        invalidate(row);
        var rejected = false;
        await using (var db = await factory.CreateDbContextAsync(token))
        {
            db.ExecutionVerificationAssessments.Add(row);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres &&
                postgres.SqlState == PostgresErrorCodes.CheckViolation &&
                postgres.ConstraintName == "CK_ExecutionVerificationAssessments_State")
            { rejected = true; }
        }
        if (!rejected) return false;
        await using var verify = await factory.CreateDbContextAsync(token);
        return !await verify.ExecutionVerificationAssessments.AsNoTracking()
            .AnyAsync(x => x.Id == row.Id, token);
    }

    private static async Task<bool> StateMatchesAsync(IDbContextFactory<AuraDbContext> factory,
        LiveAuditOutboxVerification.Fixture fixture, DurableExecutionStatus status, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var row = await db.ActionExecutionStates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ActionId == fixture.Action && x.UserId == fixture.User, token);
        return row is not null && row.ConversationId == fixture.Conversation &&
            row.ProjectId == fixture.Project && row.ToolId == "audit-outbox-check" &&
            row.ApprovalId == fixture.Approval && row.ExecutionId == fixture.Execution &&
            row.ApprovalConsumed && row.Status == status && row.Version == 2 &&
            row.CompletedAt is not null;
    }

    private static async Task<bool> CleanupAsync(IDbContextFactory<AuraDbContext> factory,
        List<LiveAuditOutboxVerification.Fixture> fixtures, List<Guid> assessmentIds,
        CancellationToken token)
    {
        var assessmentsGone = false;
        try
        {
            await using var db = await factory.CreateDbContextAsync(token);
            foreach (var id in assessmentIds)
                await db.ExecutionVerificationAssessments.Where(x => x.Id == id)
                    .ExecuteDeleteAsync(token);
            assessmentsGone = !await db.ExecutionVerificationAssessments.AsNoTracking()
                .AnyAsync(x => assessmentIds.Contains(x.Id), token);
        }
        catch (Exception) { assessmentsGone = false; }
        var fixturesGone = false;
        try { fixturesGone = await LiveAuditOutboxVerification.CleanupAsync(factory, fixtures, token); }
        catch (Exception) { fixturesGone = false; }
        return assessmentsGone && fixturesGone;
    }
}
