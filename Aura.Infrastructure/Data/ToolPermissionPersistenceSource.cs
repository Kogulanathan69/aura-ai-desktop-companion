using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aura.Infrastructure.Data;

// Read-only exact grant boundary, registered as a scoped production source.
public sealed class ToolPermissionPersistenceSource(IAuraDbContext db) : IToolPermissionSource
{
    public async Task<ToolPermissionLookupResult> GetAsync(ToolPermissionValidationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request?.Scope is null || request.Scope.UserId == Guid.Empty ||
            request.Scope.ConversationId == Guid.Empty || request.Scope.ProjectId == Guid.Empty ||
            request.ToolId is null ||
            !ToolIdentifier.TryCreate(request.ToolId.Value, out var requestedTool) ||
            requestedTool != request.ToolId || !Enum.IsDefined(request.Requirement))
            return NotFound();

        try
        {
            // A single bounded SQL statement checks the grant and current scope integrity.
            // The independent ExactOwnershipScopeValidator is still required by execution.
            var rows = await db.ToolPermissionGrants.AsNoTracking()
                .Where(grant => grant.UserId == request.Scope.UserId &&
                    grant.ConversationId == request.Scope.ConversationId &&
                    grant.ProjectId == request.Scope.ProjectId &&
                    grant.ToolId == requestedTool.Value &&
                    grant.RequiredPermission == (int)request.Requirement &&
                    grant.Status == ToolPermissionGrantStatus.Active && grant.RevokedAt == null &&
                    db.Users.AsNoTracking().Any(user =>
                        user.Id == request.Scope.UserId && user.IsActive) &&
                    db.Conversations.AsNoTracking().Any(conversation =>
                        conversation.Id == request.Scope.ConversationId &&
                        conversation.UserId == request.Scope.UserId &&
                        conversation.Type == "Project" &&
                        conversation.ProjectId == request.Scope.ProjectId) &&
                    db.Projects.AsNoTracking().Any(project =>
                        project.Id == request.Scope.ProjectId &&
                        project.UserId == request.Scope.UserId))
                .Select(grant => new { grant.Id, grant.UserId, grant.ConversationId,
                    grant.ProjectId, grant.ToolId, grant.RequiredPermission,
                    grant.Status, grant.RevokedAt })
                .Take(2)
                .ToListAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (rows.Count == 0) return NotFound();
            if (rows.Count != 1) return Unavailable();

            var row = rows[0];
            if (row.Id == Guid.Empty || row.UserId != request.Scope.UserId ||
                row.ConversationId != request.Scope.ConversationId ||
                row.ProjectId != request.Scope.ProjectId ||
                !ToolIdentifier.TryCreate(row.ToolId, out var storedTool) ||
                storedTool!.Value != row.ToolId || storedTool != requestedTool ||
                !TryMapRequirement(row.RequiredPermission, out var storedRequirement) ||
                storedRequirement != request.Requirement ||
                row.Status != ToolPermissionGrantStatus.Active || row.RevokedAt is not null)
                return Unavailable();

            return new(ToolPermissionLookupStatus.Found,
                new ToolPermissionGrant(row.Id,
                    new ActionScope(row.UserId, row.ConversationId, row.ProjectId),
                    storedTool, storedRequirement, IsActive: true, IsRevoked: false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Unavailable();
        }
    }

    private static bool TryMapRequirement(int value, out ToolPermissionRequirement requirement)
    {
        requirement = value switch
        {
            0 => ToolPermissionRequirement.OwnedProjectRead,
            1 => ToolPermissionRequirement.OwnedProjectWrite,
            2 => ToolPermissionRequirement.ExternalAction,
            _ => default
        };
        return value is 0 or 1 or 2;
    }

    private static ToolPermissionLookupResult NotFound() =>
        new(ToolPermissionLookupStatus.NotFound, null);

    private static ToolPermissionLookupResult Unavailable() =>
        new(ToolPermissionLookupStatus.Unavailable, null);
}
