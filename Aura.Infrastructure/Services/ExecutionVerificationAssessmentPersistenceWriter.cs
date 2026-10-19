using Aura.Application.Verifications;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace Aura.Infrastructure.Services;

// Unregistered pending live Verified persistence and full composition review.
// Every assessment is an immutable historical row with a distinct ID.
public sealed class ExecutionVerificationAssessmentPersistenceWriter(
    IDbContextFactory<AuraDbContext> factory) : IExecutionVerificationAssessmentWriter
{
    public async Task<bool> WriteAsync(VerificationEvidence evidence, long executionVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (evidence is null || evidence.Id.Value == Guid.Empty ||
            evidence.ActionId.Value == Guid.Empty || evidence.Scope.UserId == Guid.Empty ||
            evidence.Scope.ConversationId == Guid.Empty || evidence.Scope.ProjectId == Guid.Empty ||
            evidence.ApprovalId == Guid.Empty || evidence.ExecutionId == Guid.Empty ||
            evidence.ToolId is null ||
            !ToolIdentifier.TryCreate(evidence.ToolId.Value, out var canonical) ||
            canonical != evidence.ToolId || executionVersion != 2 ||
            evidence.ExecutionVersion != executionVersion ||
            !Enum.IsDefined(evidence.Outcome) ||
            evidence.IssuedAt.Kind != DateTimeKind.Utc || evidence.IssuedAt == default)
            return false;

        var verified = evidence.Outcome == VerificationOutcome.Verified;
        if (verified && (evidence.ExecutionReport != ExecutionReportOutcome.Succeeded ||
            evidence.ToolId.Value != "owned-project-metadata-read" ||
            evidence.IndependentProof is not { } proof ||
            proof.ProjectId != evidence.Scope.ProjectId ||
            proof.ExecutionVersion != executionVersion ||
            proof.ProofSource != VerificationProofSource.OwnedProjectMetadata ||
            proof.ProofVersion != 1 ||
            proof.ProjectUpdatedAt == default ||
            proof.ProjectUpdatedAt.Kind != DateTimeKind.Utc))
            return false;
        if (!verified && evidence.Outcome is not
            (VerificationOutcome.Failed or VerificationOutcome.Inconclusive))
            return false;

        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken);
            var state = await db.ActionExecutionStates.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ActionId == evidence.ActionId.Value &&
                    x.UserId == evidence.Scope.UserId &&
                    x.ConversationId == evidence.Scope.ConversationId &&
                    x.ProjectId == evidence.Scope.ProjectId &&
                    x.ToolId == evidence.ToolId.Value &&
                    x.ApprovalId == evidence.ApprovalId &&
                    x.ExecutionId == evidence.ExecutionId &&
                    x.Version == executionVersion && x.ApprovalConsumed &&
                    (x.Status == DurableExecutionStatus.Succeeded ||
                     x.Status == DurableExecutionStatus.Failed), cancellationToken);
            if (state?.CompletedAt is not DateTime completedAt ||
                completedAt.Kind != DateTimeKind.Utc || evidence.IssuedAt < completedAt ||
                state.Status == DurableExecutionStatus.Succeeded &&
                    evidence.ExecutionReport != ExecutionReportOutcome.Succeeded ||
                state.Status == DurableExecutionStatus.Failed &&
                    evidence.ExecutionReport != ExecutionReportOutcome.Failed ||
                verified && state.Status != DurableExecutionStatus.Succeeded ||
                verified && evidence.IndependentProof!.ProjectUpdatedAt > evidence.IssuedAt)
                return false;
            var chain = await db.Users.AsNoTracking().AnyAsync(x =>
                    x.Id == evidence.Scope.UserId && x.IsActive, cancellationToken) &&
                await db.Conversations.AsNoTracking().AnyAsync(x =>
                    x.Id == evidence.Scope.ConversationId &&
                    x.UserId == evidence.Scope.UserId &&
                    x.ProjectId == evidence.Scope.ProjectId && x.Type == "Project",
                    cancellationToken) &&
                await db.AIActions.AsNoTracking().AnyAsync(x =>
                    x.Id == evidence.ActionId.Value && x.UserId == evidence.Scope.UserId &&
                    x.ConversationId == evidence.Scope.ConversationId &&
                    x.ProjectId == evidence.Scope.ProjectId, cancellationToken) &&
                await db.ActionApprovals.AsNoTracking().AnyAsync(x =>
                    x.Id == evidence.ApprovalId && x.AIActionId == evidence.ActionId.Value &&
                    x.UserId == evidence.Scope.UserId, cancellationToken);
            if (!chain) return false;
            if (verified)
            {
                // The row lock conflicts with project UPDATE/DELETE until assessment commit.
                // Under repeatable-read, a concurrent update before the lock fails closed
                // with a serialization error; an update after the lock waits for commit.
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = "SELECT \"UpdatedAt\" FROM \"Projects\" " +
                    "WHERE \"Id\" = @projectId AND \"UserId\" = @userId FOR UPDATE";
                var projectId = command.CreateParameter();
                projectId.ParameterName = "projectId";
                projectId.Value = evidence.Scope.ProjectId;
                command.Parameters.Add(projectId);
                var userId = command.CreateParameter();
                userId.ParameterName = "userId";
                userId.Value = evidence.Scope.UserId;
                command.Parameters.Add(userId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0) ||
                    reader.GetDateTime(0) != evidence.IndependentProof!.ProjectUpdatedAt ||
                    await reader.ReadAsync(cancellationToken))
                    return false;
            }
            else if (!await db.Projects.AsNoTracking().AnyAsync(x =>
                x.Id == evidence.Scope.ProjectId && x.UserId == evidence.Scope.UserId,
                cancellationToken))
                return false;

            db.ExecutionVerificationAssessments.Add(new ExecutionVerificationAssessmentRecord
            {
                Id = evidence.Id.Value, ActionId = evidence.ActionId.Value,
                UserId = evidence.Scope.UserId, ConversationId = evidence.Scope.ConversationId,
                ProjectId = evidence.Scope.ProjectId, ToolId = evidence.ToolId.Value,
                ApprovalId = evidence.ApprovalId, ExecutionId = evidence.ExecutionId,
                ExecutionVersion = executionVersion, ExecutionStatus = state.Status,
                Outcome = (int)evidence.Outcome,
                ProofSource = verified ? VerificationProofSource.OwnedProjectMetadata : VerificationProofSource.None,
                ProofVersion = verified ? 1 : 0, AssessedAt = evidence.IssuedAt
            });
            var saved = await db.SaveChangesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (saved != 1) return false;
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
    }
}
