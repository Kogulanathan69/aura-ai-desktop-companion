using Aura.Application.Auditing;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Aura.Infrastructure.Services;

// Explicit, unregistered one-item dispatcher.
// The conditional no-op update takes a PostgreSQL row lock
// until this transaction ends.
// No polling, hosted worker, timer, or automatic retry is performed.
public sealed class SecurityAuditOutboxDispatcher(
    IDbContextFactory<AuraDbContext> factory,
    IAuditEventSink sink)
    : IAuditOutboxDispatcher
{
    public async Task<AuditOutboxDispatchResult> DispatchAsync(
        Guid outboxId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (outboxId == Guid.Empty)
        {
            return Result(
                AuditOutboxDispatchStatus.InvalidRequest);
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
                await db.SecurityAuditOutbox
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.Id == outboxId,
                        cancellationToken);

            if (row is null)
            {
                return Result(
                    AuditOutboxDispatchStatus.NotFound);
            }

            if (row.DeliveryState !=
                SecurityAuditOutboxDeliveryState.Pending)
            {
                return Result(
                    AuditOutboxDispatchStatus.NotPending);
            }

            if (row.AttemptCount != 0 ||
                row.DeliveredAt is not null)
            {
                return Result(
                    AuditOutboxDispatchStatus.Conflict);
            }

            // Deliberate no-op UPDATE.
            // PostgreSQL still locks the exact matching row
            // until this transaction finishes.
            var locked =
                await db.SecurityAuditOutbox
                    .Where(x =>
                        x.Id == outboxId &&
                        x.AuditEventId == row.AuditEventId &&
                        x.DeliveryState ==
                            SecurityAuditOutboxDeliveryState.Pending &&
                        x.AttemptCount == 0 &&
                        x.DeliveredAt == null)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            x => x.AttemptCount,
                            x => x.AttemptCount),
                        cancellationToken);

            if (locked != 1)
            {
                return Result(
                    AuditOutboxDispatchStatus.Conflict);
            }

            // Re-read while the transaction owns the row lock.
            row =
                await db.SecurityAuditOutbox
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.Id == outboxId,
                        cancellationToken);

            if (row is null ||
                row.DeliveryState !=
                    SecurityAuditOutboxDeliveryState.Pending ||
                row.AttemptCount != 0 ||
                row.DeliveredAt is not null)
            {
                return Result(
                    AuditOutboxDispatchStatus.Conflict);
            }

            var audit =
                AuditEventRecord.RestoreExecutionOutbox(
                    row.AuditEventId,
                    (AuditEventType)row.EventType,
                    (AuditEventOutcome)row.Outcome,
                    row.OccurredAt,
                    row.UserId,
                    row.ConversationId,
                    row.ProjectId,
                    row.ActionId,
                    row.ToolId,
                    row.ApprovalId,
                    row.ExecutionId);

            var valid =
                audit is not null &&
                row.Id != Guid.Empty &&
                await db.ActionExecutionStates
                    .AsNoTracking()
                    .AnyAsync(
                        state =>
                            state.ActionId == row.ActionId &&
                            state.UserId == row.UserId &&
                            state.ConversationId ==
                                row.ConversationId &&
                            state.ProjectId ==
                                row.ProjectId &&
                            state.ToolId ==
                                row.ToolId &&
                            state.ApprovalId ==
                                row.ApprovalId &&
                            state.ExecutionId ==
                                row.ExecutionId &&
                            state.ApprovalConsumed &&
                            (
                                row.EventType ==
                                    (int)AuditEventType.ExecutionReserved &&
                                state.Status !=
                                    DurableExecutionStatus.Ready

                                ||

                                row.EventType ==
                                    (int)AuditEventType.ExecutionCompleted &&
                                state.Status ==
                                    DurableExecutionStatus.Succeeded

                                ||

                                row.EventType ==
                                    (int)AuditEventType.ExecutionFailed &&
                                state.Status ==
                                    DurableExecutionStatus.Failed
                            ),
                        cancellationToken);

            AuditSinkStatus sinkStatus;

            if (!valid)
            {
                sinkStatus =
                    AuditSinkStatus.Failed;
            }
            else
            {
                try
                {
                    sinkStatus =
                        await sink.WriteAsync(
                            audit!,
                            cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    sinkStatus =
                        AuditSinkStatus.Unavailable;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Transient/unavailable delivery must NOT permanently
            // consume the outbox record.
            //
            // Returning here without committing causes the transaction
            // to roll back, leaving:
            // Pending + AttemptCount 0 + DeliveredAt null.
            if (sinkStatus == AuditSinkStatus.Unavailable)
            {
                return Result(
                    AuditOutboxDispatchStatus.Unavailable);
            }

            var delivered =
                sinkStatus == AuditSinkStatus.Accepted;

            var target =
                delivered
                    ? SecurityAuditOutboxDeliveryState.Delivered
                    : SecurityAuditOutboxDeliveryState.Failed;

            DateTime? deliveredAt =
                delivered
                    ? DateTime.UtcNow
                    : null;

            var changed =
                await db.SecurityAuditOutbox
                    .Where(x =>
                        x.Id == outboxId &&
                        x.AuditEventId == row.AuditEventId &&
                        x.DeliveryState ==
                            SecurityAuditOutboxDeliveryState.Pending &&
                        x.AttemptCount == 0 &&
                        x.DeliveredAt == null)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                x => x.DeliveryState,
                                target)
                            .SetProperty(
                                x => x.AttemptCount,
                                1)
                            .SetProperty(
                                x => x.DeliveredAt,
                                deliveredAt),
                        cancellationToken);

            if (changed != 1)
            {
                return Result(
                    AuditOutboxDispatchStatus.Conflict);
            }

            await tx.CommitAsync(
                cancellationToken);

            return Result(
                delivered
                    ? AuditOutboxDispatchStatus.Delivered
                    : AuditOutboxDispatchStatus.Failed);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result(
                AuditOutboxDispatchStatus.Unavailable);
        }
    }

    private static AuditOutboxDispatchResult Result(
        AuditOutboxDispatchStatus status)
        =>
        new(status);
}