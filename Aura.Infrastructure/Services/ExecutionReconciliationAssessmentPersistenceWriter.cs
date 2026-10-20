using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Aura.Infrastructure.Services;

// Explicit, unregistered writer. Repeated assessments are distinct historical rows.
public sealed class ExecutionReconciliationAssessmentPersistenceWriter(
    IDbContextFactory<AuraDbContext> factory) : IExecutionReconciliationAssessmentWriter
{
    public async Task<bool> WriteAsync(ExecutionReconciliationEvidence evidence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (evidence is null || evidence.Id.Value == Guid.Empty ||
            evidence.ActionId.Value == Guid.Empty || evidence.Scope.UserId == Guid.Empty ||
            evidence.Scope.ConversationId == Guid.Empty || evidence.Scope.ProjectId == Guid.Empty ||
            evidence.ApprovalId == Guid.Empty || evidence.ExecutionId == Guid.Empty ||
            evidence.ToolId is null || !ToolIdentifier.TryCreate(evidence.ToolId.Value, out var tool) ||
            tool != evidence.ToolId ||
            evidence.ObservedStatus != ActionExecutionStateStatus.Reserved ||
            evidence.ObservedVersion != 1 || !Enum.IsDefined(evidence.Reason) ||
            evidence.Disposition is not (ExecutionReconciliationDisposition.NeedsManualReview or
                ExecutionReconciliationDisposition.SafeToMarkFailed) ||
            evidence.IssuedAt.Kind != DateTimeKind.Utc || evidence.IssuedAt == default)
            return false;

        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken);
            // The no-op conditional update locks the exact Reserved/version row until
            // the assessment insert commits. Completion cannot cross this boundary.
            var current = await db.ActionExecutionStates.Where(x =>
                x.ActionId == evidence.ActionId.Value &&
                x.UserId == evidence.Scope.UserId &&
                x.ConversationId == evidence.Scope.ConversationId &&
                x.ProjectId == evidence.Scope.ProjectId &&
                x.ToolId == evidence.ToolId.Value &&
                x.ApprovalId == evidence.ApprovalId &&
                x.ExecutionId == evidence.ExecutionId &&
                x.Status == DurableExecutionStatus.Reserved &&
                x.Version == evidence.ObservedVersion &&
                x.ApprovalConsumed && x.ReservedAt != null && x.CompletedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, x => x.Version),
                    cancellationToken);
            if (current != 1) return false;
            db.ExecutionReconciliationAssessments.Add(new ExecutionReconciliationAssessmentRecord
            {
                Id = evidence.Id.Value,
                ActionId = evidence.ActionId.Value,
                UserId = evidence.Scope.UserId,
                ConversationId = evidence.Scope.ConversationId,
                ProjectId = evidence.Scope.ProjectId,
                ToolId = evidence.ToolId.Value,
                ApprovalId = evidence.ApprovalId,
                ExecutionId = evidence.ExecutionId,
                ObservedStatus = DurableExecutionStatus.Reserved,
                ObservedVersion = evidence.ObservedVersion,
                Reason = (int)evidence.Reason,
                Disposition = (int)evidence.Disposition,
                AssessedAt = evidence.IssuedAt
            });
            var saved = await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return saved == 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
    }
}
