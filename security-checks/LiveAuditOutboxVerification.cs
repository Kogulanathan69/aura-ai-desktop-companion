using Aura.Application.Auditing;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

// Explicit test-only entry point. Never registered with the API or normal suite.
internal static class LiveAuditOutboxVerification
{
    internal const string Command = "--live-audit-outbox-verification";
    internal const string EnableVariable = "AURA_LIVE_AUDIT_OUTBOX_VERIFY";
    internal const string DisposableVariable = "AURA_AUDIT_OUTBOX_DB_IS_DISPOSABLE";
    internal const string ConnectionVariable = "AURA_AUDIT_OUTBOX_DISPOSABLE_CONNECTION";
    private const string DatabasePrefix = "aura_disposable_";

    internal static async Task<int> RunAsync(Func<string, string?> environment, Action<string> write,
        Func<string, CancellationToken, Task<bool>>? execute = null,
        CancellationToken cancellationToken = default)
    {
        if (environment(EnableVariable) != "true" || environment(DisposableVariable) != "true")
        {
            write("LIVE AUDIT OUTBOX VERIFY: SKIPPED");
            return 0;
        }
        var connection = environment(ConnectionVariable);
        if (!ValidConnection(connection))
        {
            write("LIVE AUDIT OUTBOX VERIFY: FAIL (explicit disposable connection required)");
            return 1;
        }
        try
        {
            var passed = await (execute ?? ((value, ct) => ExecuteLiveAsync(value, ct)))(
                connection!, cancellationToken);
            write(passed ? "LIVE AUDIT OUTBOX VERIFY: PASS" : "LIVE AUDIT OUTBOX VERIFY: FAIL");
            return passed ? 0 : 1;
        }
        catch (Exception)
        {
            write("LIVE AUDIT OUTBOX VERIFY: FAIL");
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
                parsed.Database?.StartsWith(DatabasePrefix, StringComparison.Ordinal) == true &&
                parsed.Database.Length > DatabasePrefix.Length;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException) { return false; }
    }

    internal sealed record Fixture(Guid User, Guid AuthUser, Guid Project, Guid Conversation,
        Guid Action, Guid Approval, Guid Execution, Guid Outbox, Guid Event)
    {
        internal static Fixture New() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }

    internal static async Task<bool> ExecuteLiveAsync(string connection, CancellationToken token,
        Action<bool>? cleanupStatus = null)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.UseVector()).Options;
        var factory = new PooledDbContextFactory<AuraDbContext>(options);
        await using (var db = await factory.CreateDbContextAsync(token))
        {
            // This repository has no historical baseline migration. Refuse an empty
            // database; a reviewed disposable baseline and prerequisites must exist.
            if (!await db.Database.CanConnectAsync(token) ||
                !await TableExistsAsync(db, "__EFMigrationsHistory", token) ||
                !await TableExistsAsync(db, "Users", token) ||
                !await TableExistsAsync(db, "Projects", token) ||
                !await TableExistsAsync(db, "Conversations", token) ||
                !await TableExistsAsync(db, "AIActions", token) ||
                !await TableExistsAsync(db, "ActionApprovals", token)) return false;
            var prerequisites = await db.Database.GetAppliedMigrationsAsync(token);
            if (!new[] { "20261014000000_AddToolPermissionGrants",
                "20261014000001_AddActionExecutionStates",
                "20261014000002_AddSecurityPersistenceBundle" }
                .All(prerequisites.Contains)) return false;
            var migrations = await db.Database.GetAppliedMigrationsAsync(token);
            if (!migrations.Contains("20261014000003_AddSecurityAuditOutbox") ||
                !migrations.Contains("20261014000004_HardenSecurityAuditOutboxDelivery") ||
                !await TableExistsAsync(db, "SecurityAuditOutbox", token) ||
                !await TableExistsAsync(db, "SecurityAuditEvents", token)) return false;
        }

        var fixtures = new List<Fixture>();
        var passed = false;
        try
        {
            // Each scenario has unique IDs and its own complete ownership/action fixture.
            var success = await AddFixtureAsync(factory, fixtures, token);
            var dispatcher = new SecurityAuditOutboxDispatcher(factory, new SecurityAuditPersistenceSink(factory));
            if ((await dispatcher.DispatchAsync(success.Outbox, token)).Status != AuditOutboxDispatchStatus.Delivered ||
                (await dispatcher.DispatchAsync(success.Outbox, token)).Status != AuditOutboxDispatchStatus.NotPending ||
                !await StateAsync(factory, success, SecurityAuditOutboxDeliveryState.Delivered, 1, true, 1, token)) return false;

            var failed = await AddFixtureAsync(factory, fixtures, token);
            if ((await new SecurityAuditOutboxDispatcher(factory, new FixedSink(AuditSinkStatus.Failed))
                    .DispatchAsync(failed.Outbox, token)).Status != AuditOutboxDispatchStatus.Failed ||
                (await dispatcher.DispatchAsync(failed.Outbox, token)).Status != AuditOutboxDispatchStatus.NotPending ||
                !await StateAsync(factory, failed, SecurityAuditOutboxDeliveryState.Failed, 1, false, 0, token)) return false;

            var unavailable = await AddFixtureAsync(factory, fixtures, token);
            if ((await new SecurityAuditOutboxDispatcher(factory, new FixedSink(AuditSinkStatus.Unavailable))
                    .DispatchAsync(unavailable.Outbox, token)).Status != AuditOutboxDispatchStatus.Unavailable ||
                !await StateAsync(factory, unavailable, SecurityAuditOutboxDeliveryState.Pending, 0, false, 0, token)) return false;

            var duplicate = await AddFixtureAsync(factory, fixtures, token);
            await PreinsertEventAsync(factory, duplicate, false, token);
            if ((await dispatcher.DispatchAsync(duplicate.Outbox, token)).Status != AuditOutboxDispatchStatus.Delivered ||
                !await StateAsync(factory, duplicate, SecurityAuditOutboxDeliveryState.Delivered, 1, true, 1, token)) return false;

            var mismatch = await AddFixtureAsync(factory, fixtures, token);
            await PreinsertEventAsync(factory, mismatch, true, token);
            if ((await dispatcher.DispatchAsync(mismatch.Outbox, token)).Status != AuditOutboxDispatchStatus.Failed ||
                !await StateAsync(factory, mismatch, SecurityAuditOutboxDeliveryState.Failed, 1, false, 1, token)) return false;

            var concurrent = await AddFixtureAsync(factory, fixtures, token);
            var counted = new CountingSink();
            var one = new SecurityAuditOutboxDispatcher(factory, counted);
            var two = new SecurityAuditOutboxDispatcher(factory, counted);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<AuditOutboxDispatchResult> Compete(SecurityAuditOutboxDispatcher candidate)
            { await gate.Task.WaitAsync(token); return await candidate.DispatchAsync(concurrent.Outbox, token); }
            var first = Task.Run(() => Compete(one), token);
            var second = Task.Run(() => Compete(two), token);
            gate.SetResult();
            var results = await Task.WhenAll(first, second);
            if (results.Count(x => x.Status == AuditOutboxDispatchStatus.Delivered) != 1 ||
                results.Count(x => x.Status is AuditOutboxDispatchStatus.Conflict or AuditOutboxDispatchStatus.NotPending) != 1 ||
                counted.Calls != 1 ||
                !await StateAsync(factory, concurrent, SecurityAuditOutboxDeliveryState.Delivered, 1, true, 0, token)) return false;

            var cancelled = await AddFixtureAsync(factory, fixtures, token);
            using (var source = new CancellationTokenSource())
            {
                source.Cancel();
                try { await dispatcher.DispatchAsync(cancelled.Outbox, source.Token); return false; }
                catch (OperationCanceledException) { }
            }
            if (!await StateAsync(factory, cancelled, SecurityAuditOutboxDeliveryState.Pending, 0, false, 0, token)) return false;
            using (var source = new CancellationTokenSource())
            {
                var cancelling = new SecurityAuditOutboxDispatcher(factory, new CancellingSink(source));
                try { await cancelling.DispatchAsync(cancelled.Outbox, source.Token); return false; }
                catch (OperationCanceledException) { }
            }
            if (!await StateAsync(factory, cancelled, SecurityAuditOutboxDeliveryState.Pending, 0, false, 0, token)) return false;

            var malformed = await AddFixtureAsync(factory, fixtures, token, AuditEventOutcome.Failed);
            if ((await dispatcher.DispatchAsync(malformed.Outbox, token)).Status != AuditOutboxDispatchStatus.Failed ||
                !await StateAsync(factory, malformed, SecurityAuditOutboxDeliveryState.Failed, 1, false, 0, token)) return false;

            var wrongBinding = await AddFixtureAsync(factory, fixtures, token, tool: "different-tool");
            if ((await dispatcher.DispatchAsync(wrongBinding.Outbox, token)).Status != AuditOutboxDispatchStatus.Failed ||
                !await StateAsync(factory, wrongBinding, SecurityAuditOutboxDeliveryState.Failed, 1, false, 0, token)) return false;

            // Every binding column is checked separately against an existing but
            // unrelated fixture, so FK constraints do not mask a missing predicate.
            var other = await AddFixtureAsync(factory, fixtures, token);
            foreach (var field in Enum.GetValues<BindingField>())
            {
                var target = await AddFixtureAsync(factory, fixtures, token);
                await ChangeBindingAsync(factory, target, other, field, token);
                if ((await dispatcher.DispatchAsync(target.Outbox, token)).Status != AuditOutboxDispatchStatus.Failed ||
                    !await StateAsync(factory, target, SecurityAuditOutboxDeliveryState.Failed, 1, false, 0, token)) return false;
            }
            passed = true;
        }
        finally
        {
            // A failure to remove any fixture is a failed verification, never a pass.
            var cleanupOk = false;
            try { cleanupOk = await CleanupAsync(factory, fixtures, CancellationToken.None); }
            catch (Exception) { cleanupOk = false; }
            cleanupStatus?.Invoke(cleanupOk);
            if (!cleanupOk) passed = false;
        }
        return passed;
    }

    private static async Task<bool> TableExistsAsync(AuraDbContext db, string name, CancellationToken token)
    {
        if (name is not ("__EFMigrationsHistory" or "Users" or "Projects" or
            "Conversations" or "AIActions" or "ActionApprovals" or
            "SecurityAuditOutbox" or "SecurityAuditEvents")) return false;
        await db.Database.OpenConnectionAsync(token);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT to_regclass('\"{name}\"') IS NOT NULL";
        return (bool)(await command.ExecuteScalarAsync(token) ?? false);
    }

    internal static async Task<Fixture> AddFixtureAsync(IDbContextFactory<AuraDbContext> factory,
        List<Fixture> fixtures, CancellationToken token,
        AuditEventOutcome outcome = AuditEventOutcome.Success, string tool = "audit-outbox-check")
    {
        var id = Fixture.New();
        fixtures.Add(id); // Track IDs before partial insertion for cleanup.
        await using var db = await factory.CreateDbContextAsync(token);
        var now = DateTime.UtcNow;
        db.Users.Add(new User { Id = id.User, AuthUserId = id.AuthUser,
            DisplayName = "aura-audit-outbox-check", Email = $"{id.User:N}@invalid.example",
            CreatedAt = now, UpdatedAt = now, IsActive = true });
        db.Projects.Add(new Project { Id = id.Project, UserId = id.User,
            Name = "aura-audit-outbox-check", CreatedAt = now, UpdatedAt = now });
        db.Conversations.Add(new Conversation { Id = id.Conversation, UserId = id.User,
            ProjectId = id.Project, Title = "aura-audit-outbox-check", Type = "Project",
            CreatedAt = now, UpdatedAt = now });
        db.AIActions.Add(new AIAction { Id = id.Action, UserId = id.User, ProjectId = id.Project,
            ConversationId = id.Conversation, ActionType = "Tool", Title = "aura-audit-outbox-check",
            Description = "test fixture", Status = "Approved", RequiresApproval = true,
            CreatedAt = now, UpdatedAt = now });
        db.ActionApprovals.Add(new ActionApproval { Id = id.Approval, AIActionId = id.Action,
            UserId = id.User, Decision = "Approved", ApprovalSource = "Test",
            RequestedAt = now, DecidedAt = now, CreatedAt = now });
        db.ActionExecutionStates.Add(new ActionExecutionStateRecord { ActionId = id.Action,
            UserId = id.User, ConversationId = id.Conversation, ProjectId = id.Project,
            ToolId = "audit-outbox-check", ApprovalId = id.Approval, ExecutionId = id.Execution,
            Status = DurableExecutionStatus.Reserved, Version = 1, ApprovalConsumed = true,
            CreatedAt = now, ReservedAt = now });
        db.SecurityAuditOutbox.Add(new SecurityAuditOutboxRecord { Id = id.Outbox,
            AuditEventId = id.Event, EventType = (int)AuditEventType.ExecutionReserved,
            Outcome = (int)outcome, OccurredAt = now, UserId = id.User,
            ConversationId = id.Conversation, ProjectId = id.Project, ActionId = id.Action,
            ToolId = tool, ApprovalId = id.Approval, ExecutionId = id.Execution,
            DeliveryState = SecurityAuditOutboxDeliveryState.Pending, AttemptCount = 0 });
        await db.SaveChangesAsync(token);
        return id;
    }

    private static async Task PreinsertEventAsync(IDbContextFactory<AuraDbContext> factory,
        Fixture id, bool mismatch, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var row = await db.SecurityAuditOutbox.AsNoTracking().SingleAsync(x => x.Id == id.Outbox, token);
        db.SecurityAuditEvents.Add(new SecurityAuditEventRecord { Id = id.Event,
            EventType = row.EventType, Outcome = mismatch ? (int)AuditEventOutcome.Failed : row.Outcome,
            OccurredAt = row.OccurredAt, UserId = id.User, ConversationId = id.Conversation,
            ProjectId = id.Project, ActionId = id.Action, ToolId = row.ToolId,
            ApprovalId = id.Approval, ExecutionId = id.Execution });
        await db.SaveChangesAsync(token);
    }

    private enum BindingField { User, Conversation, Project, Action, Approval, Execution }

    private static async Task ChangeBindingAsync(IDbContextFactory<AuraDbContext> factory,
        Fixture target, Fixture other, BindingField field, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var query = db.SecurityAuditOutbox.Where(x => x.Id == target.Outbox);
        var changed = field switch
        {
            BindingField.User => await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, other.User), token),
            BindingField.Conversation => await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.ConversationId, other.Conversation), token),
            BindingField.Project => await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.ProjectId, other.Project), token),
            BindingField.Action => await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.ActionId, other.Action), token),
            BindingField.Approval => await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.ApprovalId, other.Approval), token),
            BindingField.Execution => await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExecutionId, other.Execution), token),
            _ => 0
        };
        if (changed != 1) throw new InvalidOperationException("Test binding was not changed.");
    }

    private static async Task<bool> StateAsync(IDbContextFactory<AuraDbContext> factory, Fixture id,
        SecurityAuditOutboxDeliveryState status, int attempts, bool timestamp, int events,
        CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var row = await db.SecurityAuditOutbox.AsNoTracking().SingleAsync(x => x.Id == id.Outbox, token);
        var count = await db.SecurityAuditEvents.AsNoTracking().CountAsync(x => x.Id == id.Event, token);
        var audit = count == 1 ? await db.SecurityAuditEvents.AsNoTracking()
            .SingleAsync(x => x.Id == id.Event, token) : null;
        return row.AuditEventId == id.Event && row.DeliveryState == status &&
            row.AttemptCount == attempts && (row.DeliveredAt is not null) == timestamp &&
            (row.DeliveredAt is null || row.DeliveredAt.Value.Kind == DateTimeKind.Utc) &&
            count == events && (audit is null || audit.UserId == id.User &&
                audit.ConversationId == id.Conversation && audit.ProjectId == id.Project &&
                audit.ActionId == id.Action && audit.ApprovalId == id.Approval &&
                audit.ExecutionId == id.Execution && audit.ToolId == "audit-outbox-check" &&
                audit.EventType == (int)AuditEventType.ExecutionReserved &&
                audit.Outcome == (count == 1 && status == SecurityAuditOutboxDeliveryState.Failed
                    ? (int)AuditEventOutcome.Failed : (int)AuditEventOutcome.Success) &&
                audit.VerificationId is null && audit.ReconciliationId is null &&
                audit.PermissionGrantId is null);
    }

    internal static async Task<bool> CleanupAsync(IDbContextFactory<AuraDbContext> factory,
        List<Fixture> fixtures, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        foreach (var id in fixtures)
        {
            await db.SecurityAuditEvents.Where(x => x.Id == id.Event).ExecuteDeleteAsync(token);
            await db.SecurityAuditOutbox.Where(x => x.Id == id.Outbox).ExecuteDeleteAsync(token);
            await db.ActionExecutionStates.Where(x => x.ActionId == id.Action && x.UserId == id.User).ExecuteDeleteAsync(token);
            await db.ActionApprovals.Where(x => x.Id == id.Approval && x.UserId == id.User).ExecuteDeleteAsync(token);
            await db.AIActions.Where(x => x.Id == id.Action && x.UserId == id.User).ExecuteDeleteAsync(token);
            await db.Conversations.Where(x => x.Id == id.Conversation && x.UserId == id.User).ExecuteDeleteAsync(token);
            await db.Projects.Where(x => x.Id == id.Project && x.UserId == id.User).ExecuteDeleteAsync(token);
            await db.Users.Where(x => x.Id == id.User && x.AuthUserId == id.AuthUser).ExecuteDeleteAsync(token);
        }
        foreach (var id in fixtures)
            if (await db.SecurityAuditEvents.AnyAsync(x => x.Id == id.Event, token) ||
                await db.SecurityAuditOutbox.AnyAsync(x => x.Id == id.Outbox, token) ||
                await db.Users.AnyAsync(x => x.Id == id.User, token)) return false;
        return true;
    }

    private sealed class FixedSink(AuditSinkStatus status) : IAuditEventSink
    {
        public Task<AuditSinkStatus> WriteAsync(AuditEventRecord record, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(status); }
    }

    private sealed class CountingSink : IAuditEventSink
    {
        private int calls;
        internal int Calls => Volatile.Read(ref calls);
        public Task<AuditSinkStatus> WriteAsync(AuditEventRecord record, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Interlocked.Increment(ref calls); return Task.FromResult(AuditSinkStatus.Accepted); }
    }

    private sealed class CancellingSink(CancellationTokenSource source) : IAuditEventSink
    {
        public Task<AuditSinkStatus> WriteAsync(AuditEventRecord record, CancellationToken token)
        { source.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(AuditSinkStatus.Accepted); }
    }
}
