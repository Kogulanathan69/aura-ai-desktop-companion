using Aura.Application.Auditing;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aura.Infrastructure.Services;

// Unregistered direct durable sink. It does not share the execution-state transaction.
public sealed class SecurityAuditPersistenceSink(IDbContextFactory<AuraDbContext> factory) : IAuditEventSink
{
    public async Task<AuditSinkStatus> WriteAsync(AuditEventRecord record,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Valid(record)) return AuditSinkStatus.Failed;
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            db.SecurityAuditEvents.Add(new SecurityAuditEventRecord
            {
                Id = record.Id.Value, EventType = (int)record.EventType,
                Outcome = (int)record.Outcome, OccurredAt = record.OccurredAt,
                UserId = record.Scope.UserId, ConversationId = record.Scope.ConversationId,
                ProjectId = record.Scope.ProjectId, ActionId = record.ActionId?.Value,
                ToolId = record.ToolId?.Value, ApprovalId = record.ApprovalId,
                ExecutionId = record.ExecutionId, VerificationId = record.VerificationId,
                ReconciliationId = record.ReconciliationId,
                PermissionGrantId = record.PermissionGrantId
            });
            return await db.SaveChangesAsync(cancellationToken) == 1
                ? AuditSinkStatus.Accepted : AuditSinkStatus.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        { return await ResolveDuplicateAsync(record, cancellationToken); }
        catch (Exception) { return AuditSinkStatus.Unavailable; }
    }

    private async Task<AuditSinkStatus> ResolveDuplicateAsync(AuditEventRecord record,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var existing = await db.SecurityAuditEvents.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == record.Id.Value, cancellationToken);
            if (existing is null) return AuditSinkStatus.Failed;
            return existing.EventType == (int)record.EventType &&
                existing.Outcome == (int)record.Outcome &&
                existing.OccurredAt.Kind == DateTimeKind.Utc &&
                existing.OccurredAt.Ticks == record.OccurredAt.Ticks - record.OccurredAt.Ticks % 10 &&
                existing.UserId == record.Scope.UserId &&
                existing.ConversationId == record.Scope.ConversationId &&
                existing.ProjectId == record.Scope.ProjectId &&
                existing.ActionId == record.ActionId?.Value &&
                existing.ToolId == record.ToolId?.Value &&
                existing.ApprovalId == record.ApprovalId &&
                existing.ExecutionId == record.ExecutionId &&
                existing.VerificationId == record.VerificationId &&
                existing.ReconciliationId == record.ReconciliationId &&
                existing.PermissionGrantId == record.PermissionGrantId
                    ? AuditSinkStatus.Accepted : AuditSinkStatus.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return AuditSinkStatus.Unavailable; }
    }

    private static bool Valid(AuditEventRecord? record) => record is not null &&
        record.Id is not null && record.Id.Value != Guid.Empty && Enum.IsDefined(record.EventType) &&
        Enum.IsDefined(record.Outcome) && record.OccurredAt.Kind == DateTimeKind.Utc &&
        record.OccurredAt != default && record.Scope is not null &&
        record.Scope.UserId != Guid.Empty && record.Scope.ConversationId != Guid.Empty &&
        record.Scope.ProjectId != Guid.Empty &&
        (record.ActionId is null || record.ActionId.Value != Guid.Empty) &&
        (record.ToolId is null || ToolIdentifier.TryCreate(record.ToolId.Value, out var tool) &&
            tool == record.ToolId) &&
        Valid(record.ApprovalId) && Valid(record.ExecutionId) &&
        Valid(record.VerificationId) && Valid(record.ReconciliationId) &&
        Valid(record.PermissionGrantId);

    private static bool Valid(Guid? value) => value is null || value.Value != Guid.Empty;
}
