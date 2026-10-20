using Aura.Application.Auditing;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aura.Infrastructure.Services;

// Deliberately unregistered.
// PostgreSQL constraints and conditional updates are the concurrency boundary.
//
// Step 13G additionally stages a bounded pending audit outbox row inside
// the SAME database transaction as a successful execution-state transition.
//
// This does NOT mean audit delivery occurred.
// No dispatcher, retry worker or exactly-once delivery claim exists yet.
public sealed class ActionExecutionStatePersistenceStore(
    IDbContextFactory<AuraDbContext> factory)
    : IActionExecutionStateStore
{
    public async Task<ActionExecutionStateResult> TryReserveInitialAsync(
        ActionExecutionState ready,
        ExecutionAttemptIdentifier executionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ready is null ||
            !ActionExecutionStatePolicy.IsValid(ready) ||
            ready.Status != ActionExecutionStateStatus.Ready ||
            ready.Version != 0 ||
            !Canonical(ready.ToolId) ||
            executionId is null ||
            executionId.Value == Guid.Empty)
        {
            return Denied(
                ActionExecutionStateResultStatus.InvalidRequest);
        }

        try
        {
            await using var db =
                await factory.CreateDbContextAsync(
                    cancellationToken);

            await using var tx =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            var now = DateTime.UtcNow;
            var exactChain =
                await db.Users.AsNoTracking().AnyAsync(x =>
                        x.Id == ready.Scope.UserId && x.IsActive, cancellationToken) &&
                await db.Projects.AsNoTracking().AnyAsync(x =>
                        x.Id == ready.Scope.ProjectId && x.UserId == ready.Scope.UserId,
                        cancellationToken) &&
                await db.Conversations.AsNoTracking().AnyAsync(x =>
                        x.Id == ready.Scope.ConversationId &&
                        x.UserId == ready.Scope.UserId &&
                        x.ProjectId == ready.Scope.ProjectId && x.Type == "Project",
                        cancellationToken) &&
                await db.AIActions.AsNoTracking().AnyAsync(x =>
                        x.Id == ready.ActionId.Value && x.UserId == ready.Scope.UserId &&
                        x.ConversationId == ready.Scope.ConversationId &&
                        x.ProjectId == ready.Scope.ProjectId && x.ActionType == "Tool" &&
                        x.RequiresApproval && x.Status == "Approved",
                        cancellationToken) &&
                await db.ActionApprovals.AsNoTracking().AnyAsync(x =>
                        x.Id == ready.ApprovalId && x.AIActionId == ready.ActionId.Value &&
                        x.UserId == ready.Scope.UserId && x.Decision == "Approved" &&
                        x.DecidedAt != null && (x.ExpiresAt == null || x.ExpiresAt > now),
                        cancellationToken);
            if (!exactChain)
                return Denied(ActionExecutionStateResultStatus.InvalidRequest);

            var row =
                await db.ActionExecutionStates
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.ActionId ==
                             ready.ActionId.Value,
                        cancellationToken);

            if (row is null)
            {
                row =
                    new ActionExecutionStateRecord
                    {
                        ActionId =
                            ready.ActionId.Value,

                        UserId =
                            ready.Scope.UserId,

                        ConversationId =
                            ready.Scope.ConversationId,

                        ProjectId =
                            ready.Scope.ProjectId,

                        ToolId =
                            ready.ToolId.Value,

                        ApprovalId =
                            ready.ApprovalId,

                        Status =
                            DurableExecutionStatus.Ready,

                        Version = 0,

                        CreatedAt = now
                    };

                db.ActionExecutionStates.Add(row);

                await db.SaveChangesAsync(
                    cancellationToken);

                db.Entry(row).State =
                    EntityState.Detached;
            }

            var request =
                new ReserveExecutionRequest(
                    ready.ActionId,
                    ready.Scope,
                    ready.ToolId,
                    ready.ApprovalId,
                    0,
                    executionId);

            var result =
                await ReserveCoreAsync(
                    db,
                    row,
                    request,
                    cancellationToken);

            if (result.Status ==
                ActionExecutionStateResultStatus.Success)
            {
                AddPendingAuditOutbox(
                    db,
                    AuditEventType.ExecutionReserved,
                    AuditEventOutcome.Success,
                    ready.ActionId.Value,
                    ready.Scope.UserId,
                    ready.Scope.ConversationId,
                    ready.Scope.ProjectId,
                    ready.ToolId.Value,
                    ready.ApprovalId,
                    executionId.Value);

                await db.SaveChangesAsync(
                    cancellationToken);

                await tx.CommitAsync(
                    cancellationToken);
            }

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException ex)
            when (Unique(ex))
        {
            return Denied(
                ActionExecutionStateResultStatus.AlreadyReserved);
        }
        catch (PostgresException ex)
            when (ex.SqlState ==
                  PostgresErrorCodes.UniqueViolation)
        {
            return Denied(
                ActionExecutionStateResultStatus.AlreadyReserved);
        }
        catch (Exception)
        {
            return Denied(
                ActionExecutionStateResultStatus.Unavailable);
        }
    }

    public async Task<ActionExecutionStateResult> TryReserveAsync(
        ReserveExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Valid(request))
        {
            return Denied(
                ActionExecutionStateResultStatus.InvalidRequest);
        }

        try
        {
            await using var db =
                await factory.CreateDbContextAsync(
                    cancellationToken);

            await using var tx =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            var row =
                await db.ActionExecutionStates
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.ActionId ==
                             request.ActionId.Value,
                        cancellationToken);

            var result =
                await ReserveCoreAsync(
                    db,
                    row,
                    request,
                    cancellationToken);

            if (result.Status ==
                ActionExecutionStateResultStatus.Success)
            {
                AddPendingAuditOutbox(
                    db,
                    AuditEventType.ExecutionReserved,
                    AuditEventOutcome.Success,
                    request.ActionId.Value,
                    request.Scope.UserId,
                    request.Scope.ConversationId,
                    request.Scope.ProjectId,
                    request.ToolId.Value,
                    request.ApprovalId,
                    request.ExecutionId.Value);

                await db.SaveChangesAsync(
                    cancellationToken);

                await tx.CommitAsync(
                    cancellationToken);
            }

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException ex)
            when (Unique(ex))
        {
            return Denied(
                ActionExecutionStateResultStatus.AlreadyReserved);
        }
        catch (PostgresException ex)
            when (ex.SqlState ==
                  PostgresErrorCodes.UniqueViolation)
        {
            return Denied(
                ActionExecutionStateResultStatus.AlreadyReserved);
        }
        catch (Exception)
        {
            return Denied(
                ActionExecutionStateResultStatus.Unavailable);
        }
    }

    public async Task<ActionExecutionStateResult> TryCompleteAsync(
        CompleteExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Valid(request))
        {
            return Denied(
                ActionExecutionStateResultStatus.InvalidRequest);
        }

        try
        {
            await using var db =
                await factory.CreateDbContextAsync(
                    cancellationToken);

            await using var tx =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            var row =
                await db.ActionExecutionStates
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.ActionId ==
                             request.ActionId.Value,
                        cancellationToken);

            var binding =
                Binding(
                    row,
                    request.Scope,
                    request.ToolId,
                    request.ApprovalId);

            if (binding !=
                ActionExecutionStateResultStatus.Success)
            {
                return Denied(binding);
            }

            var prior =
                Snapshot(
                    row!,
                    request.ActionId,
                    request.Scope,
                    request.ToolId);

            var decision =
                ActionExecutionStatePolicy.TryComplete(
                    prior,
                    request);

            if (decision.Status !=
                ActionExecutionStateResultStatus.Success)
            {
                return decision;
            }

            var now =
                DateTime.UtcNow;

            var target =
                request.Outcome ==
                ExecutionOutcome.Succeeded
                    ? DurableExecutionStatus.Succeeded
                    : DurableExecutionStatus.Failed;

            var count =
                await db.ActionExecutionStates
                    .Where(x =>
                        x.ActionId ==
                            request.ActionId.Value &&

                        x.UserId ==
                            request.Scope.UserId &&

                        x.ConversationId ==
                            request.Scope.ConversationId &&

                        x.ProjectId ==
                            request.Scope.ProjectId &&

                        x.ToolId ==
                            request.ToolId.Value &&

                        x.ApprovalId ==
                            request.ApprovalId &&

                        x.ExecutionId ==
                            request.ExecutionId.Value &&

                        x.Status ==
                            DurableExecutionStatus.Reserved &&

                        x.Version ==
                            request.ExpectedVersion &&

                        x.ApprovalConsumed &&

                        x.ReservedAt != null &&

                        x.CompletedAt == null)
                    .ExecuteUpdateAsync(
                        setters => setters

                            .SetProperty(
                                x => x.Status,
                                target)

                            .SetProperty(
                                x => x.Version,
                                2L)

                            .SetProperty(
                                x => x.CompletedAt,
                                now),

                        cancellationToken);

            if (count != 1)
            {
                return Denied(
                    ActionExecutionStateResultStatus.StaleVersion);
            }

            var auditEventType =
                request.Outcome ==
                ExecutionOutcome.Succeeded
                    ? AuditEventType.ExecutionCompleted
                    : AuditEventType.ExecutionFailed;

            var auditOutcome =
                request.Outcome ==
                ExecutionOutcome.Succeeded
                    ? AuditEventOutcome.Success
                    : AuditEventOutcome.Failed;

            AddPendingAuditOutbox(
                db,
                auditEventType,
                auditOutcome,
                request.ActionId.Value,
                request.Scope.UserId,
                request.Scope.ConversationId,
                request.Scope.ProjectId,
                request.ToolId.Value,
                request.ApprovalId,
                request.ExecutionId.Value);

            await db.SaveChangesAsync(
                cancellationToken);

            await tx.CommitAsync(
                cancellationToken);

            return decision;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Denied(
                ActionExecutionStateResultStatus.Unavailable);
        }
    }

    private static async Task<ActionExecutionStateResult>
        ReserveCoreAsync(
            AuraDbContext db,
            ActionExecutionStateRecord? row,
            ReserveExecutionRequest request,
            CancellationToken cancellationToken)
    {
        var binding =
            Binding(
                row,
                request.Scope,
                request.ToolId,
                request.ApprovalId);

        if (binding !=
            ActionExecutionStateResultStatus.Success)
        {
            return Denied(binding);
        }

        var prior =
            Snapshot(
                row!,
                request.ActionId,
                request.Scope,
                request.ToolId);

        var decision =
            ActionExecutionStatePolicy.TryReserve(
                prior,
                request);

        if (decision.Status !=
            ActionExecutionStateResultStatus.Success)
        {
            return decision;
        }

        var now =
            DateTime.UtcNow;

        var count =
            await db.ActionExecutionStates
                .Where(x =>
                    x.ActionId ==
                        request.ActionId.Value &&

                    x.UserId ==
                        request.Scope.UserId &&

                    x.ConversationId ==
                        request.Scope.ConversationId &&

                    x.ProjectId ==
                        request.Scope.ProjectId &&

                    x.ToolId ==
                        request.ToolId.Value &&

                    x.ApprovalId ==
                        request.ApprovalId &&

                    x.Status ==
                        DurableExecutionStatus.Ready &&

                    x.Version ==
                        request.ExpectedVersion &&

                    x.ExecutionId == null &&

                    !x.ApprovalConsumed &&

                    x.ReservedAt == null &&

                    x.CompletedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters

                        .SetProperty(
                            x => x.Status,
                            DurableExecutionStatus.Reserved)

                        .SetProperty(
                            x => x.Version,
                            1L)

                        .SetProperty(
                            x => x.ExecutionId,
                            request.ExecutionId.Value)

                        .SetProperty(
                            x => x.ApprovalConsumed,
                            true)

                        .SetProperty(
                            x => x.ReservedAt,
                            now),

                    cancellationToken);

        return count == 1
            ? decision
            : Denied(
                ActionExecutionStateResultStatus.StaleVersion);
    }

    private static void AddPendingAuditOutbox(
        AuraDbContext db,
        AuditEventType eventType,
        AuditEventOutcome outcome,
        Guid actionId,
        Guid userId,
        Guid conversationId,
        Guid projectId,
        string toolId,
        Guid approvalId,
        Guid executionId)
    {
        db.SecurityAuditOutbox.Add(
            new SecurityAuditOutboxRecord
            {
                Id =
                    Guid.NewGuid(),

                AuditEventId =
                    Guid.NewGuid(),

                EventType =
                    (int)eventType,

                Outcome =
                    (int)outcome,

                OccurredAt =
                    DateTime.UtcNow,

                UserId =
                    userId,

                ConversationId =
                    conversationId,

                ProjectId =
                    projectId,

                ActionId =
                    actionId,

                ToolId =
                    toolId,

                ApprovalId =
                    approvalId,

                ExecutionId =
                    executionId,

                DeliveryState =
                    SecurityAuditOutboxDeliveryState.Pending,

                AttemptCount =
                    0,

                DeliveredAt =
                    null
            });
    }

    private static ActionExecutionState Snapshot(
        ActionExecutionStateRecord row,
        Aura.Application.Actions.ActionIdentifier actionId,
        Aura.Application.Actions.ActionScope scope,
        ToolIdentifier toolId)
        =>
        new(
            actionId,
            scope,
            toolId,
            row.ApprovalId,
            (ActionExecutionStateStatus)row.Status,
            row.Version,
            row.ExecutionId,
            row.ApprovalConsumed
                ? row.ExecutionId
                : null,
            row.Status ==
                DurableExecutionStatus.Succeeded
                    ? ExecutionOutcome.Succeeded
                    : row.Status ==
                        DurableExecutionStatus.Failed
                            ? ExecutionOutcome.Failed
                            : null);

    private static ActionExecutionStateResultStatus Binding(
        ActionExecutionStateRecord? row,
        Aura.Application.Actions.ActionScope scope,
        ToolIdentifier toolId,
        Guid approvalId)
    {
        if (row is null)
        {
            return ActionExecutionStateResultStatus.NotFound;
        }

        if (row.UserId != scope.UserId ||
            row.ConversationId != scope.ConversationId ||
            row.ProjectId != scope.ProjectId)
        {
            return ActionExecutionStateResultStatus.ScopeMismatch;
        }

        if (row.ToolId != toolId.Value)
        {
            return ActionExecutionStateResultStatus.ToolMismatch;
        }

        if (row.ApprovalId != approvalId)
        {
            return ActionExecutionStateResultStatus.ApprovalMismatch;
        }

        return ActionExecutionStateResultStatus.Success;
    }

    private static bool Valid(
        ReserveExecutionRequest? request)
        =>
        request is not null &&
        Identity(
            request.ActionId,
            request.Scope,
            request.ToolId,
            request.ApprovalId) &&
        Canonical(request.ToolId) &&
        request.ExpectedVersion == 0 &&
        request.ExecutionId is not null &&
        request.ExecutionId.Value != Guid.Empty;

    private static bool Valid(
        CompleteExecutionRequest? request)
        =>
        request is not null &&
        Identity(
            request.ActionId,
            request.Scope,
            request.ToolId,
            request.ApprovalId) &&
        Canonical(request.ToolId) &&
        request.ExpectedVersion == 1 &&
        request.ExecutionId is not null &&
        request.ExecutionId.Value != Guid.Empty &&
        Enum.IsDefined(request.Outcome);

    private static bool Canonical(
        ToolIdentifier identifier)
        =>
        ToolIdentifier.TryCreate(
            identifier.Value,
            out var canonical) &&
        canonical == identifier;

    private static bool Identity(
        Aura.Application.Actions.ActionIdentifier? actionId,
        Aura.Application.Actions.ActionScope? scope,
        ToolIdentifier? toolId,
        Guid approvalId)
        =>
        actionId is not null &&
        actionId.Value != Guid.Empty &&
        scope is not null &&
        scope.UserId != Guid.Empty &&
        scope.ConversationId != Guid.Empty &&
        scope.ProjectId != Guid.Empty &&
        toolId is not null &&
        approvalId != Guid.Empty;

    private static bool Unique(
        DbUpdateException exception)
        =>
        exception.InnerException
            is PostgresException postgres &&
        postgres.SqlState ==
            PostgresErrorCodes.UniqueViolation;

    private static ActionExecutionStateResult Denied(
        ActionExecutionStateResultStatus status)
        =>
        ActionExecutionStateResult.Denied(status);
}
