using System.Data;
using Aura.Application.Actions;
using Aura.Application.Tools;
using Aura.Application.Verifications;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Aura.Infrastructure.Services;

// Unregistered pending live Verified persistence and full composition review.
// Reads one exact durable execution and its authoritative owned project in one snapshot.
public sealed class OwnedProjectMetadataExecutionEvidenceSource(
    IDbContextFactory<AuraDbContext> factory) : IExecutionEvidenceSource
{
    private const string ExpectedTool = "owned-project-metadata-read";

    public Task<ExecutionEvidenceInput?> GetAsync(ActionScope scope, Guid executionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ExecutionEvidenceInput?>(null);
    }

    public async Task<ExecutionEvidenceInput?> GetAsync(ActionScope scope, ActionIdentifier actionId,
        Guid executionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scope is null || actionId is null || actionId.Value == Guid.Empty ||
            scope.UserId == Guid.Empty || scope.ConversationId == Guid.Empty ||
            scope.ProjectId == Guid.Empty || executionId == Guid.Empty)
            return null;
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken);
            var state = await db.ActionExecutionStates.AsNoTracking().SingleOrDefaultAsync(x =>
                x.ActionId == actionId.Value && x.UserId == scope.UserId &&
                x.ConversationId == scope.ConversationId && x.ProjectId == scope.ProjectId &&
                x.ToolId == ExpectedTool && x.ExecutionId == executionId &&
                x.ApprovalId != Guid.Empty && x.ApprovalConsumed && x.Version == 2 &&
                (x.Status == DurableExecutionStatus.Succeeded ||
                 x.Status == DurableExecutionStatus.Failed), cancellationToken);
            if (state?.CompletedAt is not DateTime completedAt ||
                completedAt.Kind != DateTimeKind.Utc || completedAt == default ||
                state.ReservedAt is not DateTime reservedAt ||
                reservedAt.Kind != DateTimeKind.Utc || completedAt < reservedAt ||
                !ToolIdentifier.TryCreate(state.ToolId, out var tool) ||
                tool!.Value != ExpectedTool)
                return null;
            var chain = await db.Users.AsNoTracking().AnyAsync(x =>
                    x.Id == scope.UserId && x.IsActive, cancellationToken) &&
                await db.Conversations.AsNoTracking().AnyAsync(x =>
                    x.Id == scope.ConversationId && x.UserId == scope.UserId &&
                    x.ProjectId == scope.ProjectId && x.Type == "Project", cancellationToken) &&
                await db.AIActions.AsNoTracking().AnyAsync(x =>
                    x.Id == actionId.Value && x.UserId == scope.UserId &&
                    x.ConversationId == scope.ConversationId &&
                    x.ProjectId == scope.ProjectId, cancellationToken) &&
                await db.ActionApprovals.AsNoTracking().AnyAsync(x =>
                    x.Id == state.ApprovalId && x.AIActionId == actionId.Value &&
                    x.UserId == scope.UserId, cancellationToken);
            if (!chain) return null;
            // The project query re-observes the approved scope from the database.
            // A failed execution retains a failure report, never project proof.
            OwnedProjectMetadataObservation? observation = null;
            OwnedProjectMetadataProof? independentProof = null;
            if (state.Status == DurableExecutionStatus.Succeeded)
            {
                var project = await db.Projects.AsNoTracking()
                    .Where(x => x.Id == scope.ProjectId && x.UserId == scope.UserId)
                    .Select(x => new { x.Id, x.UpdatedAt })
                    .SingleOrDefaultAsync(cancellationToken);
                if (project is null || project.UpdatedAt == default ||
                    project.UpdatedAt.Kind != DateTimeKind.Utc)
                    return null;
                observation = new(project.Id, project.UpdatedAt,
                    VerificationProofSource.OwnedProjectMetadata, 1);
                independentProof = OwnedProjectMetadataProof.FromAuthoritativeRead(
                    project.Id, project.UpdatedAt, state.Version);
                if (independentProof is null) return null;
            }
            await transaction.CommitAsync(cancellationToken);
            return new ExecutionEvidenceInput(executionId, actionId, scope, tool!,
                state.ApprovalId,
                state.Status == DurableExecutionStatus.Succeeded
                    ? ExecutionReportOutcome.Succeeded : ExecutionReportOutcome.Failed,
                completedAt)
            {
                ExecutionVersion = state.Version,
                ProjectObservation = observation,
                IndependentProof = independentProof
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }
}
