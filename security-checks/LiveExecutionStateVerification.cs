using System.Data;
using System.Reflection;
using Aura.Application.Actions;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

internal sealed record LiveExecutionStateInput(string ConnectionString);

internal enum LiveExecutionStateStage
{
    Passed, Connectivity, Schema, Fixture, InitialReservation, ApprovalReuse,
    ExecutionIdReuse, StaleVersion, Completion, NegativeBindings, Cleanup, Unexpected
}

// Explicit command, two environment gates and a pre-existing reviewed test schema are required.
// The normal security suite only exercises RunAsync through an injected offline runner.
internal static class LiveExecutionStateVerification
{
    internal const string Command = "--live-execution-state";
    internal const string EnableVariable = "AURA_LIVE_EXECUTION_STATE_VERIFY";
    internal const string DisposableVariable = "AURA_EXECUTION_STATE_DB_IS_DISPOSABLE";
    internal const string ProductionCommand = "--live-production-execution-state-evidence";
    internal const string ProductionEnableVariable = "AURA_LIVE_PRODUCTION_EXECUTION_STATE_EVIDENCE";
    internal const string ProductionDisposableVariable = "AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE";
    private const string ConnectionVariable = "ConnectionStrings__DefaultConnection";

    private static readonly (LiveExecutionStateStage Stage, string Label)[] Stages =
    [
        (LiveExecutionStateStage.Connectivity, "DB CONNECTIVITY"),
        (LiveExecutionStateStage.Schema, "SCHEMA CHECK"),
        (LiveExecutionStateStage.Fixture, "FIXTURE SETUP"),
        (LiveExecutionStateStage.InitialReservation, "INITIAL RESERVATION CONCURRENCY"),
        (LiveExecutionStateStage.ApprovalReuse, "APPROVAL REUSE"),
        (LiveExecutionStateStage.ExecutionIdReuse, "EXECUTION ID REUSE"),
        (LiveExecutionStateStage.StaleVersion, "STALE VERSION"),
        (LiveExecutionStateStage.Completion, "COMPLETION CONCURRENCY"),
        (LiveExecutionStateStage.NegativeBindings, "NEGATIVE BINDINGS"),
        (LiveExecutionStateStage.Cleanup, "CLEANUP")
    ];

    internal static async Task<int> RunAsync(Func<string, string?> environment, Action<string> write,
        Func<LiveExecutionStateInput, CancellationToken, Task<LiveExecutionStateStage>>? execute = null,
        CancellationToken cancellationToken = default) =>
        await RunCoreAsync(environment, write, EnableVariable, DisposableVariable, false,
            execute, cancellationToken);

    internal static async Task<int> RunProductionAsync(Func<string, string?> environment,
        Action<string> write,
        Func<LiveExecutionStateInput, CancellationToken, Task<LiveExecutionStateStage>>? execute = null,
        CancellationToken cancellationToken = default) =>
        await RunCoreAsync(environment, write, ProductionEnableVariable,
            ProductionDisposableVariable, true, execute, cancellationToken);

    private static async Task<int> RunCoreAsync(Func<string, string?> environment,
        Action<string> write, string enableVariable, string disposableVariable,
        bool productionEvidence,
        Func<LiveExecutionStateInput, CancellationToken, Task<LiveExecutionStateStage>>? execute,
        CancellationToken cancellationToken)
    {
        try
        {
            if (environment(enableVariable) != "true" || environment(disposableVariable) != "true")
            {
                write(productionEvidence ? "PRODUCTION EXECUTION STATE EVIDENCE: SKIPPED" :
                    "LIVE EXECUTION STATE VERIFY: SKIPPED");
                return 0;
            }
            var connection = environment(ConnectionVariable);
            if (!ValidConnection(connection, productionEvidence))
            {
                write("DB CONNECTIVITY: FAIL");
                write("RESULT: FAIL");
                return 1;
            }
            var stage = await (execute ?? ExecuteLiveAsync)(new(connection!), cancellationToken);
            foreach (var (current, label) in Stages)
            {
                if (stage == current)
                {
                    write(label + ": FAIL");
                    write("RESULT: FAIL");
                    return 1;
                }
                if (stage == LiveExecutionStateStage.Passed)
                    write(label + ": PASS");
            }
            write(stage == LiveExecutionStateStage.Passed ? "RESULT: PASS" : "RESULT: FAIL");
            return stage == LiveExecutionStateStage.Passed ? 0 : 1;
        }
        catch (Exception)
        {
            write("RESULT: FAIL");
            return 1;
        }
    }

    private static bool ValidConnection(string? value, bool productionEvidence)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(value);
            return !string.IsNullOrWhiteSpace(parsed.Host) &&
                !string.IsNullOrWhiteSpace(parsed.Database) &&
                (!productionEvidence ||
                    (!string.IsNullOrWhiteSpace(parsed.Username) &&
                     parsed.Database.StartsWith("aura_disposable_", StringComparison.Ordinal) &&
                     parsed.Database.Length > "aura_disposable_".Length));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException) { return false; }
    }

    private static async Task<LiveExecutionStateStage> ExecuteLiveAsync(LiveExecutionStateInput input,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(input.ConnectionString, npgsql => npgsql.UseVector()).Options;
        var factory = new PooledDbContextFactory<AuraDbContext>(options);
        try
        {
            await using var probe = await factory.CreateDbContextAsync(cancellationToken);
            if (!await probe.Database.CanConnectAsync(cancellationToken)) return LiveExecutionStateStage.Connectivity;
            try
            {
                if (!await SchemaMatchesAsync(probe, cancellationToken)) return LiveExecutionStateStage.Schema;
            }
            catch (Exception) { return LiveExecutionStateStage.Schema; }
        }
        catch (Exception) { return LiveExecutionStateStage.Connectivity; }

        var ids = FixtureIds.New();
        var setupAttempted = false;
        var fixtureCreated = false;
        var stage = LiveExecutionStateStage.Passed;
        try
        {
            setupAttempted = true;
            fixtureCreated = await CreateFixturesAsync(factory, ids, cancellationToken);
            if (!fixtureCreated)
                stage = LiveExecutionStateStage.Fixture;
            else
                stage = await VerifyStoreAsync(factory, ids, cancellationToken);
        }
        catch (Exception) { stage = fixtureCreated ? LiveExecutionStateStage.Unexpected : LiveExecutionStateStage.Fixture; }
        finally
        {
            if (setupAttempted)
            {
                try
                {
                    if (!await CleanupAsync(factory, ids, CancellationToken.None))
                        stage = LiveExecutionStateStage.Cleanup;
                }
                catch (Exception) { stage = LiveExecutionStateStage.Cleanup; }
            }
        }
        return stage;
    }

    // Fixed catalog queries only; no migration or DDL is ever executed by this harness.
    private static async Task<bool> SchemaMatchesAsync(AuraDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        async Task<string[]> ReadAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var values = new List<string>();
            while (await reader.ReadAsync(cancellationToken)) values.Add(reader.GetString(0));
            return values.ToArray();
        }
        var history = await ReadAsync("SELECT COALESCE(to_regclass('\"__EFMigrationsHistory\"')::text, '')");
        if (history.Length != 1 || history[0].Length == 0) return false;
        var applied = await ReadAsync("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" " +
            "WHERE \"MigrationId\" = '20261014000001_AddActionExecutionStates'");
        if (applied.Length != 1) return false;
        var table = await ReadAsync("SELECT COALESCE(to_regclass('\"ActionExecutionStates\"')::text, '')");
        if (table.Length != 1 || table[0].Length == 0) return false;
        var columns = await ReadAsync("SELECT attname || ':' || format_type(atttypid, atttypmod) || ':' || attnotnull::text FROM pg_catalog.pg_attribute " +
            "WHERE attrelid = to_regclass('\"ActionExecutionStates\"') AND attnum > 0 AND NOT attisdropped");
        var expectedColumns = new[] { "ActionId:uuid:true", "UserId:uuid:true", "ConversationId:uuid:true",
                "ProjectId:uuid:true", "ToolId:character varying(64):true", "ApprovalId:uuid:true",
                "ExecutionId:uuid:false", "Status:integer:true", "Version:bigint:true",
                "ApprovalConsumed:boolean:true", "CreatedAt:timestamp with time zone:true",
                "ReservedAt:timestamp with time zone:false", "CompletedAt:timestamp with time zone:false" };
        if (!columns.ToHashSet().SetEquals(expectedColumns)) return false;
        var constraints = await ReadAsync("SELECT conname || ':' || pg_get_constraintdef(oid) FROM pg_catalog.pg_constraint " +
            "WHERE conrelid = to_regclass('\"ActionExecutionStates\"')");
        if (!new[] { "PK_ActionExecutionStates", "CK_ActionExecutionStates_NonemptyIds",
                "CK_ActionExecutionStates_ToolId", "CK_ActionExecutionStates_State",
                "FK_ActionExecutionStates_AIActions_ActionId", "FK_ActionExecutionStates_Users_UserId",
                "FK_ActionExecutionStates_Conversations_ConversationId",
                "FK_ActionExecutionStates_Projects_ProjectId",
                "FK_ActionExecutionStates_ActionApprovals_ApprovalId" }
            .All(name => constraints.Any(value => value.StartsWith(name + ":", StringComparison.Ordinal)))) return false;
        if (!constraints.Where(value => value.StartsWith("FK_ActionExecutionStates_", StringComparison.Ordinal))
            .All(value => value.Contains("ON DELETE RESTRICT", StringComparison.OrdinalIgnoreCase))) return false;
        if (!constraints.Any(value => value.StartsWith("CK_ActionExecutionStates_State:", StringComparison.Ordinal) &&
                value.Contains("Version", StringComparison.Ordinal) &&
                value.Contains("ApprovalConsumed", StringComparison.Ordinal) &&
                value.Contains("CompletedAt", StringComparison.Ordinal) &&
                value.Contains("Status", StringComparison.Ordinal)) ||
            !constraints.Any(value => value.StartsWith("CK_ActionExecutionStates_ToolId:", StringComparison.Ordinal) &&
                value.Contains("a-z", StringComparison.Ordinal)) ||
            !constraints.Any(value => value.StartsWith("CK_ActionExecutionStates_NonemptyIds:", StringComparison.Ordinal) &&
                value.Contains("00000000-0000-0000-0000-000000000000", StringComparison.Ordinal))) return false;
        var indexes = await ReadAsync("SELECT index_class.relname || ':' || pg_get_indexdef(index_data.indexrelid) FROM pg_catalog.pg_index index_data " +
            "JOIN pg_catalog.pg_class index_class ON index_class.oid = index_data.indexrelid " +
            "WHERE index_data.indrelid = to_regclass('\"ActionExecutionStates\"') " +
            "AND index_data.indisunique");
        return indexes.Any(value => value.StartsWith("PK_ActionExecutionStates:", StringComparison.Ordinal)) &&
            indexes.Any(value => value.StartsWith("IX_ActionExecutionStates_ApprovalId:", StringComparison.Ordinal) &&
                value.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
                value.Contains("\"ApprovalId\"", StringComparison.Ordinal)) &&
            indexes.Any(value => value.StartsWith("IX_ActionExecutionStates_ExecutionId:", StringComparison.Ordinal) &&
                value.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
                value.Contains("WHERE", StringComparison.OrdinalIgnoreCase) &&
                value.Contains("\"ExecutionId\" IS NOT NULL", StringComparison.Ordinal));
    }

    private sealed record FixtureIds(Guid User, Guid AuthUser, Guid Project, Guid Conversation,
        Guid ActionA, Guid ActionB, Guid ActionC, Guid ApprovalA, Guid ApprovalC)
    {
        internal static FixtureIds New() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        internal Guid[] Actions => [ActionA, ActionB, ActionC];
        internal Guid[] Approvals => [ApprovalA, ApprovalC];
    }

    private static async Task<bool> CreateFixturesAsync(IDbContextFactory<AuraDbContext> factory,
        FixtureIds ids, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        db.Users.Add(new User { Id = ids.User, AuthUserId = ids.AuthUser,
            DisplayName = "aura-execution-state-check", Email = "execution-state-check.invalid",
            CreatedAt = now, UpdatedAt = now, IsActive = true });
        db.Projects.Add(new Project { Id = ids.Project, UserId = ids.User,
            Name = "aura-execution-state-check", CreatedAt = now, UpdatedAt = now });
        db.Conversations.Add(new Conversation { Id = ids.Conversation, UserId = ids.User,
            ProjectId = ids.Project, Title = "aura-execution-state-check", Type = "Project",
            CreatedAt = now, UpdatedAt = now });
        foreach (var actionId in ids.Actions)
            db.AIActions.Add(new AIAction { Id = actionId, UserId = ids.User,
                ProjectId = ids.Project, ConversationId = ids.Conversation,
                ActionType = "Tool", Title = "aura-execution-state-check", Description = "test fixture",
                Status = "Approved", RequiresApproval = true, CreatedAt = now, UpdatedAt = now });
        db.ActionApprovals.Add(new ActionApproval { Id = ids.ApprovalA, AIActionId = ids.ActionA,
            UserId = ids.User, Decision = "Approved", ApprovalSource = "Test",
            RequestedAt = now, DecidedAt = now, CreatedAt = now });
        db.ActionApprovals.Add(new ActionApproval { Id = ids.ApprovalC, AIActionId = ids.ActionC,
            UserId = ids.User, Decision = "Approved", ApprovalSource = "Test",
            RequestedAt = now, DecidedAt = now, CreatedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static async Task<LiveExecutionStateStage> VerifyStoreAsync(
        IDbContextFactory<AuraDbContext> factory, FixtureIds ids, CancellationToken cancellationToken)
    {
        ToolIdentifier.TryCreate("aura-execution-check", out var tool);
        ToolIdentifier.TryCreate("aura-other-check", out var otherTool);
        var scope = new ActionScope(ids.User, ids.Conversation, ids.Project);
        var actionA = Identifier(ids.ActionA);
        var actionB = Identifier(ids.ActionB);
        var actionC = Identifier(ids.ActionC);
        var first = Attempt(Guid.NewGuid());
        var second = Attempt(Guid.NewGuid());
        var readyA = Ready(actionA, scope, tool!, ids.ApprovalA);
        var store = new ActionExecutionStatePersistenceStore(factory);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ActionExecutionStateResult> Compete(ExecutionAttemptIdentifier attempt)
        { await gate.Task.WaitAsync(cancellationToken); return await store.TryReserveInitialAsync(readyA, attempt, cancellationToken); }
        var a = Task.Run(() => Compete(first), cancellationToken);
        var b = Task.Run(() => Compete(second), cancellationToken);
        gate.SetResult();
        var initial = await Task.WhenAll(a, b);
        var winner = initial[0].Status == ActionExecutionStateResultStatus.Success ? first : second;
        if (initial.Count(x => x.Status == ActionExecutionStateResultStatus.Success) != 1 ||
            initial.Count(x => Conflict(x.Status)) != 1 ||
            !await StateMatchesAsync(factory, ids.ActionA, DurableExecutionStatus.Reserved, 1,
                winner.Value, true, cancellationToken)) return LiveExecutionStateStage.InitialReservation;

        var reserved = await store.TryReserveAsync(new(actionA, scope, tool!, ids.ApprovalA, 0,
            Attempt(Guid.NewGuid())), cancellationToken);
        if (!Conflict(reserved.Status) || !await StateMatchesAsync(factory, ids.ActionA,
                DurableExecutionStatus.Reserved, 1, winner.Value, true, cancellationToken))
            return LiveExecutionStateStage.InitialReservation;

        var reusedApproval = await store.TryReserveInitialAsync(
            Ready(actionB, scope, tool!, ids.ApprovalA), Attempt(Guid.NewGuid()), cancellationToken);
        if (!Conflict(reusedApproval.Status) || await RowExistsAsync(factory, ids.ActionB, cancellationToken))
            return LiveExecutionStateStage.ApprovalReuse;

        var reusedExecution = await store.TryReserveInitialAsync(
            Ready(actionC, scope, tool!, ids.ApprovalC), winner, cancellationToken);
        if (!Conflict(reusedExecution.Status) || await RowExistsAsync(factory, ids.ActionC, cancellationToken))
            return LiveExecutionStateStage.ExecutionIdReuse;

        var stale = await store.TryReserveAsync(new(actionA, scope, tool!, ids.ApprovalA, 0,
            Attempt(Guid.NewGuid())), cancellationToken);
        var wrongVersion = await store.TryReserveAsync(new(actionA, scope, tool!, ids.ApprovalA, 1,
            Attempt(Guid.NewGuid())), cancellationToken);
        if (!Conflict(stale.Status) || wrongVersion.Status != ActionExecutionStateResultStatus.InvalidRequest ||
            !await StateMatchesAsync(factory, ids.ActionA, DurableExecutionStatus.Reserved, 1,
                winner.Value, true, cancellationToken)) return LiveExecutionStateStage.StaleVersion;

        var wrongReservations = new (ReserveExecutionRequest Request,
            ActionExecutionStateResultStatus Expected)[] {
            (new(actionA, scope with { UserId = Guid.NewGuid() }, tool!, ids.ApprovalA, 0,
                Attempt(Guid.NewGuid())), ActionExecutionStateResultStatus.ScopeMismatch),
            (new(actionA, scope with { ConversationId = Guid.NewGuid() }, tool!, ids.ApprovalA, 0,
                Attempt(Guid.NewGuid())), ActionExecutionStateResultStatus.ScopeMismatch),
            (new(actionA, scope with { ProjectId = Guid.NewGuid() }, tool!, ids.ApprovalA, 0,
                Attempt(Guid.NewGuid())), ActionExecutionStateResultStatus.ScopeMismatch),
            (new(actionA, scope, otherTool!, ids.ApprovalA, 0,
                Attempt(Guid.NewGuid())), ActionExecutionStateResultStatus.ToolMismatch),
            (new(actionA, scope, tool!, ids.ApprovalC, 0,
                Attempt(Guid.NewGuid())), ActionExecutionStateResultStatus.ApprovalMismatch)
        };
        foreach (var (wrong, expected) in wrongReservations)
            if ((await store.TryReserveAsync(wrong, cancellationToken)).Status != expected)
                return LiveExecutionStateStage.NegativeBindings;

        var wrongs = new[] {
            new CompleteExecutionRequest(actionB, scope, tool!, ids.ApprovalA, winner, 1, ExecutionOutcome.Succeeded),
            new CompleteExecutionRequest(actionA, scope with { UserId = Guid.NewGuid() }, tool!, ids.ApprovalA, winner, 1, ExecutionOutcome.Succeeded),
            new CompleteExecutionRequest(actionA, scope with { ConversationId = Guid.NewGuid() }, tool!, ids.ApprovalA, winner, 1, ExecutionOutcome.Succeeded),
            new CompleteExecutionRequest(actionA, scope with { ProjectId = Guid.NewGuid() }, tool!, ids.ApprovalA, winner, 1, ExecutionOutcome.Succeeded),
            new CompleteExecutionRequest(actionA, scope, otherTool!, ids.ApprovalA, winner, 1, ExecutionOutcome.Succeeded),
            new CompleteExecutionRequest(actionA, scope, tool!, ids.ApprovalC, winner, 1, ExecutionOutcome.Succeeded),
            new CompleteExecutionRequest(actionA, scope, tool!, ids.ApprovalA, Attempt(Guid.NewGuid()), 1, ExecutionOutcome.Succeeded)
        };
        var expectedCompletions = new[] {
            ActionExecutionStateResultStatus.NotFound,
            ActionExecutionStateResultStatus.ScopeMismatch,
            ActionExecutionStateResultStatus.ScopeMismatch,
            ActionExecutionStateResultStatus.ScopeMismatch,
            ActionExecutionStateResultStatus.ToolMismatch,
            ActionExecutionStateResultStatus.ApprovalMismatch,
            ActionExecutionStateResultStatus.ExecutionMismatch
        };
        for (var index = 0; index < wrongs.Length; index++)
            if ((await store.TryCompleteAsync(wrongs[index], cancellationToken)).Status !=
                expectedCompletions[index]) return LiveExecutionStateStage.NegativeBindings;
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try
            {
                await store.TryCompleteAsync(new(actionA, scope, tool!, ids.ApprovalA,
                    winner, 1, ExecutionOutcome.Succeeded), cancelled.Token);
                return LiveExecutionStateStage.NegativeBindings;
            }
            catch (OperationCanceledException) { }
        }
        if (!await StateMatchesAsync(factory, ids.ActionA, DurableExecutionStatus.Reserved, 1,
                winner.Value, true, cancellationToken)) return LiveExecutionStateStage.NegativeBindings;

        var completeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ActionExecutionStateResult> Complete(ExecutionOutcome outcome)
        {
            await completeGate.Task.WaitAsync(cancellationToken);
            return await store.TryCompleteAsync(new(actionA, scope, tool!, ids.ApprovalA, winner, 1, outcome),
                cancellationToken);
        }
        var c = Task.Run(() => Complete(ExecutionOutcome.Succeeded), cancellationToken);
        var d = Task.Run(() => Complete(ExecutionOutcome.Failed), cancellationToken);
        completeGate.SetResult();
        var completions = await Task.WhenAll(c, d);
        var winningOutcome = completions[0].Status == ActionExecutionStateResultStatus.Success
            ? ExecutionOutcome.Succeeded : ExecutionOutcome.Failed;
        var terminal = winningOutcome == ExecutionOutcome.Succeeded
            ? DurableExecutionStatus.Succeeded : DurableExecutionStatus.Failed;
        if (completions.Count(x => x.Status == ActionExecutionStateResultStatus.Success) != 1 ||
            completions.Count(x => Conflict(x.Status)) != 1 ||
            !await StateMatchesAsync(factory, ids.ActionA, terminal, 2, winner.Value, true,
                cancellationToken)) return LiveExecutionStateStage.Completion;
        var completedAt = await CompletedAtAsync(factory, ids.ActionA, cancellationToken);
        var duplicate = await store.TryCompleteAsync(new(actionA, scope, tool!, ids.ApprovalA,
            winner, 1, winningOutcome), cancellationToken);
        if (!Conflict(duplicate.Status) || !await StateMatchesAsync(factory, ids.ActionA,
                terminal, 2, winner.Value, true, cancellationToken) ||
            completedAt is null || completedAt != await CompletedAtAsync(factory, ids.ActionA, cancellationToken))
            return LiveExecutionStateStage.Completion;
        return LiveExecutionStateStage.Passed;
    }

    private static bool Conflict(ActionExecutionStateResultStatus status) => status is
        ActionExecutionStateResultStatus.StaleVersion or ActionExecutionStateResultStatus.AlreadyReserved or
        ActionExecutionStateResultStatus.AlreadyCompleted;

    private static ActionExecutionState Ready(ActionIdentifier action, ActionScope scope,
        ToolIdentifier tool, Guid approvalId) => new(action, scope, tool, approvalId,
        ActionExecutionStateStatus.Ready, 0, null, null, null);

    // Test-only reflection is confined to this opt-in harness; production APIs remain closed.
    private static ActionIdentifier Identifier(Guid value) =>
        (ActionIdentifier)Activator.CreateInstance(typeof(ActionIdentifier),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [value], null)!;

    private static ExecutionAttemptIdentifier Attempt(Guid value) =>
        (ExecutionAttemptIdentifier)Activator.CreateInstance(typeof(ExecutionAttemptIdentifier),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [value], null)!;

    private static async Task<bool> RowExistsAsync(IDbContextFactory<AuraDbContext> factory,
        Guid actionId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ActionExecutionStates.AsNoTracking()
            .AnyAsync(x => x.ActionId == actionId, cancellationToken);
    }

    private static async Task<DateTime?> CompletedAtAsync(IDbContextFactory<AuraDbContext> factory,
        Guid actionId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ActionExecutionStates.AsNoTracking().Where(x => x.ActionId == actionId)
            .Select(x => x.CompletedAt).SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<bool> StateMatchesAsync(IDbContextFactory<AuraDbContext> factory,
        Guid actionId, DurableExecutionStatus status, long version, Guid executionId,
        bool consumed, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ActionExecutionStates.AsNoTracking()
            .Where(x => x.ActionId == actionId).ToArrayAsync(cancellationToken);
        return rows.Length == 1 && rows[0].Status == status && rows[0].Version == version &&
            rows[0].ExecutionId == executionId && rows[0].ApprovalConsumed == consumed &&
            rows[0].ReservedAt is not null &&
            (status == DurableExecutionStatus.Reserved ? rows[0].CompletedAt is null :
                rows[0].CompletedAt is not null);
    }

    private static async Task<bool> CleanupAsync(
        IDbContextFactory<AuraDbContext> factory,
        FixtureIds ids,
        CancellationToken cancellationToken)
    {
        await using var db =
            await factory.CreateDbContextAsync(cancellationToken);

        await db.SecurityAuditOutbox
            .Where(x =>
                ids.Actions.Contains(x.ActionId) &&
                x.UserId == ids.User)
            .ExecuteDeleteAsync(cancellationToken);

        await db.SecurityAuditEvents
            .Where(x =>
                x.ActionId.HasValue &&
                ids.Actions.Contains(x.ActionId.Value) &&
                x.UserId == ids.User)
            .ExecuteDeleteAsync(cancellationToken);

        await db.ExecutionVerificationAssessments
            .Where(x =>
                ids.Actions.Contains(x.ActionId) &&
                x.UserId == ids.User)
            .ExecuteDeleteAsync(cancellationToken);

        await db.ExecutionReconciliationAssessments
            .Where(x =>
                ids.Actions.Contains(x.ActionId) &&
                x.UserId == ids.User)
            .ExecuteDeleteAsync(cancellationToken);

        await db.ActionExecutionStates
            .Where(x =>
                ids.Actions.Contains(x.ActionId) &&
                x.UserId == ids.User)
            .ExecuteDeleteAsync(cancellationToken);

        await db.ActionApprovals
            .Where(x =>
                ids.Approvals.Contains(x.Id) &&
                x.UserId == ids.User)
            .ExecuteDeleteAsync(cancellationToken);

        await db.AIActions
            .Where(x =>
                ids.Actions.Contains(x.Id) &&
                x.UserId == ids.User)
            .ExecuteDeleteAsync(cancellationToken);

        await db.Conversations
            .Where(x =>
                x.Id == ids.Conversation &&
                x.UserId == ids.User)
            .ExecuteDeleteAsync(cancellationToken);

        await db.Projects
            .Where(x =>
                x.Id == ids.Project &&
                x.UserId == ids.User)
            .ExecuteDeleteAsync(cancellationToken);

        await db.Users
            .Where(x =>
                x.Id == ids.User &&
                x.AuthUserId == ids.AuthUser)
            .ExecuteDeleteAsync(cancellationToken);

        var outboxGone =
            !await db.SecurityAuditOutbox
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        ids.Actions.Contains(x.ActionId) &&
                        x.UserId == ids.User,
                    cancellationToken);

        var auditEventsGone =
            !await db.SecurityAuditEvents
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ActionId.HasValue &&
                        ids.Actions.Contains(x.ActionId.Value) &&
                        x.UserId == ids.User,
                    cancellationToken);

        var verificationGone =
            !await db.ExecutionVerificationAssessments
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        ids.Actions.Contains(x.ActionId) &&
                        x.UserId == ids.User,
                    cancellationToken);

        var reconciliationGone =
            !await db.ExecutionReconciliationAssessments
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        ids.Actions.Contains(x.ActionId) &&
                        x.UserId == ids.User,
                    cancellationToken);

        var executionStatesGone =
            !await db.ActionExecutionStates
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        ids.Actions.Contains(x.ActionId) &&
                        x.UserId == ids.User,
                    cancellationToken);

        var approvalsGone =
            !await db.ActionApprovals
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        ids.Approvals.Contains(x.Id) &&
                        x.UserId == ids.User,
                    cancellationToken);

        var actionsGone =
            !await db.AIActions
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        ids.Actions.Contains(x.Id) &&
                        x.UserId == ids.User,
                    cancellationToken);

        var conversationGone =
            !await db.Conversations
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Id == ids.Conversation &&
                        x.UserId == ids.User,
                    cancellationToken);

        var projectGone =
            !await db.Projects
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Id == ids.Project &&
                        x.UserId == ids.User,
                    cancellationToken);

        var userGone =
            !await db.Users
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Id == ids.User &&
                        x.AuthUserId == ids.AuthUser,
                    cancellationToken);

        return
            outboxGone &&
            auditEventsGone &&
            verificationGone &&
            reconciliationGone &&
            executionStatesGone &&
            approvalsGone &&
            actionsGone &&
            conversationGone &&
            projectGone &&
            userGone;
    }
}
