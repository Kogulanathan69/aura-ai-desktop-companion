using System.Reflection;
using Aura.Application.Actions;
using Aura.Application.Executions;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

internal enum LiveReconciliationStage
{
    Passed, Connectivity, Schema, Fixture, ValidAssessment, StaleVersion,
    TerminalState, NegativeBindings, RepeatedAssessment, Persistence, Cleanup, Unexpected
}

// Explicit disposable-DB evidence only. Normal security checks inject an offline runner.
internal static class LiveProductionReconciliationEvidence
{
    internal const string Command = "--live-production-reconciliation-evidence";
    internal const string EnableVariable = "AURA_LIVE_PRODUCTION_RECONCILIATION_EVIDENCE";
    internal const string DisposableVariable = "AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE";
    private const string ConnectionVariable = "ConnectionStrings__DefaultConnection";

    private static readonly (LiveReconciliationStage Stage, string Label)[] Stages =
    [
        (LiveReconciliationStage.Connectivity, "DB CONNECTIVITY"),
        (LiveReconciliationStage.Schema, "SCHEMA CHECK"),
        (LiveReconciliationStage.Fixture, "FIXTURE SETUP"),
        (LiveReconciliationStage.ValidAssessment, "VALID RESERVED ASSESSMENT"),
        (LiveReconciliationStage.StaleVersion, "STALE VERSION"),
        (LiveReconciliationStage.TerminalState, "TERMINAL STATE"),
        (LiveReconciliationStage.NegativeBindings, "NEGATIVE BINDINGS"),
        (LiveReconciliationStage.RepeatedAssessment, "REPEATED ASSESSMENT"),
        (LiveReconciliationStage.Persistence, "ASSESSMENT PERSISTENCE"),
        (LiveReconciliationStage.Cleanup, "CLEANUP")
    ];

    internal static async Task<int> RunAsync(Func<string, string?> environment, Action<string> write,
        Func<string, CancellationToken, Task<LiveReconciliationStage>>? execute = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (environment(EnableVariable) != "true" || environment(DisposableVariable) != "true")
            {
                write("PRODUCTION RECONCILIATION EVIDENCE: SKIPPED");
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
            if (stage == LiveReconciliationStage.Passed)
            {
                foreach (var (_, label) in Stages) write(label + ": PASS");
                write("RESULT: PASS");
                return 0;
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

    private static async Task<LiveReconciliationStage> ExecuteLiveAsync(string connection,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.UseVector()).Options;
        var factory = new PooledDbContextFactory<AuraDbContext>(options);
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            if (!await db.Database.CanConnectAsync(cancellationToken))
                return LiveReconciliationStage.Connectivity;
        }
        catch (Exception) { return LiveReconciliationStage.Connectivity; }
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            if (!await SchemaReadyAsync(db, cancellationToken))
                return LiveReconciliationStage.Schema;
        }
        catch (Exception) { return LiveReconciliationStage.Schema; }

        var fixtures = new List<LiveAuditOutboxVerification.Fixture>();
        var assessmentIds = new List<Guid>();
        var stage = LiveReconciliationStage.Passed;
        var fixtureReady = false;
        try
        {
            var reserved = await LiveAuditOutboxVerification.AddFixtureAsync(factory, fixtures,
                cancellationToken);
            fixtureReady = true;
            stage = await VerifyAsync(factory, fixtures, assessmentIds, reserved, cancellationToken);
        }
        catch (Exception)
        {
            stage = !fixtureReady ? LiveReconciliationStage.Fixture :
                LiveReconciliationStage.Unexpected;
        }
        finally
        {
            try
            {
                if (!await CleanupAsync(factory, fixtures, assessmentIds, CancellationToken.None))
                    stage = LiveReconciliationStage.Cleanup;
            }
            catch (Exception) { stage = LiveReconciliationStage.Cleanup; }
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
            "ExecutionReconciliationAssessments", "SecurityAuditOutbox" })
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

    private static async Task<LiveReconciliationStage> VerifyAsync(
        IDbContextFactory<AuraDbContext> factory,
        List<LiveAuditOutboxVerification.Fixture> fixtures,
        List<Guid> assessmentIds,
        LiveAuditOutboxVerification.Fixture reserved,
        CancellationToken token)
    {
        var scope = new ActionScope(reserved.User, reserved.Conversation, reserved.Project);
        var action = Identifier(reserved.Action);
        var source = new ExecutionReconciliationPersistenceSource(factory);
        var initial = await source.GetAsync(scope, action, token);
        if (initial is null || initial.State.Status != ActionExecutionStateStatus.Reserved ||
            initial.Version != 1 || initial.State.Version != 1 ||
            initial.ActionId != action || initial.Scope != scope ||
            initial.ToolId.Value != "audit-outbox-check" ||
            initial.ApprovalId != reserved.Approval || initial.ExecutionId != reserved.Execution ||
            initial.Reason != ExecutionReconciliationReason.Unknown ||
            !await StateMatchesAsync(factory, reserved, DurableExecutionStatus.Reserved, 1, token))
            return LiveReconciliationStage.ValidAssessment;

        // The fixture supplies a generated identity, while the real ownership source
        // reads the generated database chain. This is not Supabase authentication proof.
        await using var ownershipDb = await factory.CreateDbContextAsync(token);
        var validator = new ExactOwnershipScopeValidator(
            new FixtureCurrentUser(reserved.User), new OwnershipPersistenceSource(ownershipDb));
        var service = new ExecutionReconciliationService(validator, source,
            new ConservativeExecutionReconciliationPolicy(), new DateTimeProvider());
        var first = await service.ReconcileAsync(scope, new(action), token);
        if (first.Status != ExecutionReconciliationStatus.Success || first.Evidence is null ||
            !EvidenceMatches(first.Evidence, reserved) ||
            !await StateMatchesAsync(factory, reserved, DurableExecutionStatus.Reserved, 1, token))
            return LiveReconciliationStage.ValidAssessment;

        // A stale or altered snapshot must fail within the application boundary.
        // A concurrent DB transition after source read remains a documented gap.
        var stale = new ExecutionReconciliationService(validator,
            new FixedSource(initial with { Version = 0 }),
            new ConservativeExecutionReconciliationPolicy(), new DateTimeProvider());
        if ((await stale.ReconcileAsync(scope, new(action), token)).Status !=
            ExecutionReconciliationStatus.StateConflict) return LiveReconciliationStage.StaleVersion;

        foreach (var status in new[] { DurableExecutionStatus.Succeeded, DurableExecutionStatus.Failed })
        {
            var fixture = await LiveAuditOutboxVerification.AddFixtureAsync(factory, fixtures, token);
            await using (var db = await factory.CreateDbContextAsync(token))
            {
                var changed = await db.ActionExecutionStates
                    .Where(x => x.ActionId == fixture.Action && x.UserId == fixture.User &&
                        x.Status == DurableExecutionStatus.Reserved && x.Version == 1)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Status, status)
                        .SetProperty(x => x.Version, 2L)
                        .SetProperty(x => x.CompletedAt, DateTime.UtcNow), token);
                if (changed != 1) return LiveReconciliationStage.TerminalState;
            }
            var terminalScope = new ActionScope(fixture.User, fixture.Conversation, fixture.Project);
            var terminalAction = Identifier(fixture.Action);
            var snapshot = await source.GetAsync(terminalScope, terminalAction, token);
            if (snapshot?.State.Status != (ActionExecutionStateStatus)status || snapshot.Version != 2)
                return LiveReconciliationStage.TerminalState;
            await using var terminalDb = await factory.CreateDbContextAsync(token);
            var terminalValidator = new ExactOwnershipScopeValidator(
                new FixtureCurrentUser(fixture.User), new OwnershipPersistenceSource(terminalDb));
            var terminalService = new ExecutionReconciliationService(terminalValidator, source,
                new ConservativeExecutionReconciliationPolicy(), new DateTimeProvider());
            if ((await terminalService.ReconcileAsync(terminalScope, new(terminalAction), token)).Status !=
                ExecutionReconciliationStatus.AlreadyResolved)
                return LiveReconciliationStage.TerminalState;
        }

        foreach (var wrong in new[] { scope with { UserId = Guid.NewGuid() },
            scope with { ConversationId = Guid.NewGuid() },
            scope with { ProjectId = Guid.NewGuid() } })
            if (await source.GetAsync(wrong, action, token) is not null)
                return LiveReconciliationStage.NegativeBindings;
        if (await source.GetAsync(scope, Identifier(Guid.NewGuid()), token) is not null)
            return LiveReconciliationStage.NegativeBindings;
        var wrongSnapshots = new[] {
            initial with { ToolId = Tool("other-tool") },
            initial with { ApprovalId = Guid.NewGuid() },
            initial with { ExecutionId = Guid.NewGuid() },
            initial with { ActionId = Identifier(Guid.NewGuid()) }
        };
        foreach (var wrong in wrongSnapshots)
        {
            var altered = new ExecutionReconciliationService(validator, new FixedSource(wrong),
                new ConservativeExecutionReconciliationPolicy(), new DateTimeProvider());
            if ((await altered.ReconcileAsync(scope, new(action), token)).Status !=
                ExecutionReconciliationStatus.StateConflict)
                return LiveReconciliationStage.NegativeBindings;
        }
        var unknownReason = new ExecutionReconciliationService(validator,
            new FixedSource(initial with { Reason = (ExecutionReconciliationReason)999 }),
            new ConservativeExecutionReconciliationPolicy(), new DateTimeProvider());
        if ((await unknownReason.ReconcileAsync(scope, new(action), token)).Status !=
            ExecutionReconciliationStatus.StateConflict)
            return LiveReconciliationStage.NegativeBindings;

        var second = await service.ReconcileAsync(scope, new(action), token);
        if (second.Status != ExecutionReconciliationStatus.Success || second.Evidence is null ||
            !EvidenceMatches(second.Evidence, reserved) ||
            first.Evidence.Id.Value == second.Evidence.Id.Value ||
            !await StateMatchesAsync(factory, reserved, DurableExecutionStatus.Reserved, 1, token))
            return LiveReconciliationStage.RepeatedAssessment;

        // No production assessment writer exists. Persist the two generated bounded
        // evidence records directly through EF to verify the current table only.
        await using (var db = await factory.CreateDbContextAsync(token))
        {
            foreach (var evidence in new[] { first.Evidence, second.Evidence })
            {
                assessmentIds.Add(evidence.Id.Value);
                db.ExecutionReconciliationAssessments.Add(ToRecord(evidence));
            }
            if (await db.SaveChangesAsync(token) != 2)
                return LiveReconciliationStage.Persistence;
        }
        await using (var db = await factory.CreateDbContextAsync(token))
        {
            var rows = await db.ExecutionReconciliationAssessments.AsNoTracking()
                .Where(x => assessmentIds.Contains(x.Id)).ToArrayAsync(token);
            if (rows.Length != 2 || rows.Any(x => !RowMatches(x, reserved)) ||
                !await StateMatchesAsync(factory, reserved, DurableExecutionStatus.Reserved, 1, token))
                return LiveReconciliationStage.Persistence;
        }
        if (!await InvalidAssessmentRejectedAsync(factory, first.Evidence, assessmentIds,
                invalidReason: true, token) ||
            !await InvalidAssessmentRejectedAsync(factory, first.Evidence, assessmentIds,
                invalidReason: false, token))
            return LiveReconciliationStage.Persistence;
        return LiveReconciliationStage.Passed;
    }

    private static async Task<bool> InvalidAssessmentRejectedAsync(
        IDbContextFactory<AuraDbContext> factory, ExecutionReconciliationEvidence evidence,
        List<Guid> assessmentIds, bool invalidReason, CancellationToken token)
    {
        var id = Guid.NewGuid();
        assessmentIds.Add(id); // Track even an unexpected successful insert for exact cleanup.
        var record = ToRecord(evidence);
        record.Id = id;
        if (invalidReason) record.Reason = 999;
        else record.Disposition = 999;
        var rejected = false;
        await using (var db = await factory.CreateDbContextAsync(token))
        {
            db.ExecutionReconciliationAssessments.Add(record);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres &&
                postgres.SqlState == PostgresErrorCodes.CheckViolation &&
                postgres.ConstraintName == "CK_ExecutionReconciliationAssessments_State")
            { rejected = true; }
        }
        if (!rejected) return false;
        await using var verify = await factory.CreateDbContextAsync(token);
        return !await verify.ExecutionReconciliationAssessments.AsNoTracking()
            .AnyAsync(x => x.Id == id, token);
    }

    private static ExecutionReconciliationAssessmentRecord ToRecord(ExecutionReconciliationEvidence evidence) =>
        new() { Id = evidence.Id.Value, ActionId = evidence.ActionId.Value,
            UserId = evidence.Scope.UserId, ConversationId = evidence.Scope.ConversationId,
            ProjectId = evidence.Scope.ProjectId, ToolId = evidence.ToolId.Value,
            ApprovalId = evidence.ApprovalId, ExecutionId = evidence.ExecutionId,
            ObservedStatus = DurableExecutionStatus.Reserved, ObservedVersion = evidence.ObservedVersion,
            Reason = (int)evidence.Reason, Disposition = (int)evidence.Disposition,
            AssessedAt = evidence.IssuedAt };

    private static bool EvidenceMatches(ExecutionReconciliationEvidence evidence,
        LiveAuditOutboxVerification.Fixture fixture) =>
        evidence.ActionId.Value == fixture.Action && evidence.Scope.UserId == fixture.User &&
        evidence.Scope.ConversationId == fixture.Conversation &&
        evidence.Scope.ProjectId == fixture.Project && evidence.ToolId.Value == "audit-outbox-check" &&
        evidence.ApprovalId == fixture.Approval && evidence.ExecutionId == fixture.Execution &&
        evidence.ObservedStatus == ActionExecutionStateStatus.Reserved &&
        evidence.ObservedVersion == 1 && evidence.Reason == ExecutionReconciliationReason.Unknown &&
        evidence.Disposition == ExecutionReconciliationDisposition.NeedsManualReview &&
        evidence.IssuedAt.Kind == DateTimeKind.Utc;

    private static bool RowMatches(ExecutionReconciliationAssessmentRecord row,
        LiveAuditOutboxVerification.Fixture fixture) =>
        row.ActionId == fixture.Action && row.UserId == fixture.User &&
        row.ConversationId == fixture.Conversation && row.ProjectId == fixture.Project &&
        row.ToolId == "audit-outbox-check" && row.ApprovalId == fixture.Approval &&
        row.ExecutionId == fixture.Execution && row.ObservedStatus == DurableExecutionStatus.Reserved &&
        row.ObservedVersion == 1 && row.Reason == (int)ExecutionReconciliationReason.Unknown &&
        row.Disposition == (int)ExecutionReconciliationDisposition.NeedsManualReview &&
        row.AssessedAt.Kind == DateTimeKind.Utc;

    private static async Task<bool> StateMatchesAsync(IDbContextFactory<AuraDbContext> factory,
        LiveAuditOutboxVerification.Fixture fixture, DurableExecutionStatus status, long version,
        CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var row = await db.ActionExecutionStates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ActionId == fixture.Action && x.UserId == fixture.User, token);
        return row is not null && row.ConversationId == fixture.Conversation &&
            row.ProjectId == fixture.Project && row.ToolId == "audit-outbox-check" &&
            row.ApprovalId == fixture.Approval && row.ExecutionId == fixture.Execution &&
            row.ApprovalConsumed && row.Status == status && row.Version == version;
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
                await db.ExecutionReconciliationAssessments.Where(x => x.Id == id)
                    .ExecuteDeleteAsync(token);
            assessmentsGone = !await db.ExecutionReconciliationAssessments.AsNoTracking()
                .AnyAsync(x => assessmentIds.Contains(x.Id), token);
        }
        catch (Exception) { assessmentsGone = false; }
        var fixturesGone = false;
        try { fixturesGone = await LiveAuditOutboxVerification.CleanupAsync(factory, fixtures, token); }
        catch (Exception) { fixturesGone = false; }
        return assessmentsGone && fixturesGone;
    }

    private static ActionIdentifier Identifier(Guid id) =>
        (ActionIdentifier)Activator.CreateInstance(typeof(ActionIdentifier),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [id], null)!;

    private static Aura.Application.Tools.ToolIdentifier Tool(string value)
    {
        Aura.Application.Tools.ToolIdentifier.TryCreate(value, out var tool);
        return tool!;
    }

    private sealed class FixtureCurrentUser(Guid id) : ICurrentUserContext
    {
        public Task<CurrentUserSnapshot> GetAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new CurrentUserSnapshot(id, CurrentUserStatus.Authenticated));
        }
    }

    private sealed class FixedSource(ExecutionReconciliationSnapshot value)
        : IExecutionReconciliationSource
    {
        public Task<ExecutionReconciliationSnapshot?> GetAsync(ActionScope _,
            ActionIdentifier __, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<ExecutionReconciliationSnapshot?>(value);
        }
    }
}
