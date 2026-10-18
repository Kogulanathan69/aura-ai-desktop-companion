using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
            cancellationToken.ThrowIfCancellationRequested();
            return saved == 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
    }
}
