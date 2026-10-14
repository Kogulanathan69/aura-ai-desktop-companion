using System.Data;
using Aura.Application.Actions;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Aura.Infrastructure.Services;

// Unregistered read-only source. Unknown is an honest reason: storage cannot infer a cause.
public sealed class ExecutionReconciliationPersistenceSource(IDbContextFactory<AuraDbContext> factory)
    : IExecutionReconciliationSource
{
    public async Task<ExecutionReconciliationSnapshot?> GetAsync(ActionScope scope,
        ActionIdentifier actionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scope is null || actionId is null || actionId.Value == Guid.Empty ||
            scope.UserId == Guid.Empty || scope.ConversationId == Guid.Empty ||
            scope.ProjectId == Guid.Empty) return null;
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,
                cancellationToken);
            var row = await db.ActionExecutionStates.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ActionId == actionId.Value &&
                    x.UserId == scope.UserId && x.ConversationId == scope.ConversationId &&
                    x.ProjectId == scope.ProjectId, cancellationToken);
            if (row is null || row.ExecutionId is not Guid executionId || executionId == Guid.Empty ||
                !row.ApprovalConsumed || row.ApprovalId == Guid.Empty ||
                row.Status is not (DurableExecutionStatus.Reserved or
                    DurableExecutionStatus.Succeeded or DurableExecutionStatus.Failed) ||
                row.Version != (row.Status == DurableExecutionStatus.Reserved ? 1 : 2) ||
                row.ReservedAt is null ||
                (row.Status == DurableExecutionStatus.Reserved) != (row.CompletedAt is null) ||
                row.CreatedAt == default || row.CreatedAt.Kind != DateTimeKind.Utc ||
                row.ReservedAt.Value.Kind != DateTimeKind.Utc ||
                row.ReservedAt.Value < row.CreatedAt ||
                row.CompletedAt is { } completedAt &&
                    (completedAt.Kind != DateTimeKind.Utc || completedAt < row.ReservedAt.Value) ||
                !ToolIdentifier.TryCreate(row.ToolId, out var tool) || tool!.Value != row.ToolId)
                return null;
            var chain = await db.Users.AsNoTracking().AnyAsync(x => x.Id == scope.UserId && x.IsActive,
                    cancellationToken) &&
                await db.Projects.AsNoTracking().AnyAsync(x => x.Id == scope.ProjectId &&
                    x.UserId == scope.UserId, cancellationToken) &&
                await db.Conversations.AsNoTracking().AnyAsync(x => x.Id == scope.ConversationId &&
                    x.UserId == scope.UserId && x.ProjectId == scope.ProjectId && x.Type == "Project",
                    cancellationToken) &&
                await db.AIActions.AsNoTracking().AnyAsync(x => x.Id == actionId.Value &&
                    x.UserId == scope.UserId && x.ConversationId == scope.ConversationId &&
                    x.ProjectId == scope.ProjectId, cancellationToken) &&
                await db.ActionApprovals.AsNoTracking().AnyAsync(x => x.Id == row.ApprovalId &&
                    x.AIActionId == actionId.Value && x.UserId == scope.UserId, cancellationToken);
            if (!chain) return null;
            var state = new ActionExecutionState(actionId, scope, tool!, row.ApprovalId,
                (ActionExecutionStateStatus)row.Status, row.Version, executionId,
                executionId, row.Status == DurableExecutionStatus.Succeeded ? ExecutionOutcome.Succeeded :
                    row.Status == DurableExecutionStatus.Failed ? ExecutionOutcome.Failed : null);
            if (!ActionExecutionStatePolicy.IsValid(state)) return null;
            await tx.CommitAsync(cancellationToken);
            return new(state, actionId, scope, tool!, row.ApprovalId, executionId,
                row.Version, ExecutionReconciliationReason.Unknown);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }
}
