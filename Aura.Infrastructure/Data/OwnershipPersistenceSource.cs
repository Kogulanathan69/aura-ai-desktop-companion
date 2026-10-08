using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Aura.Infrastructure.Data;

// Read-only persistence boundary. It is intentionally not registered in Aura.Api.
public sealed class OwnershipPersistenceSource(IAuraDbContext db) : IOwnershipSource
{
    public async Task<OwnershipLookupResult> GetAsync(OwnershipLookupRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null || request.UserId == Guid.Empty || request.ConversationId == Guid.Empty ||
            request.ProjectId == Guid.Empty)
            return new(OwnershipLookupStatus.Denied, null, false, false, false);

        try
        {
            // One bounded query requires an active user, exact owned Project conversation,
            // and exact owned project. Missing/foreign/orphaned rows are indistinguishable.
            var owned = await db.Conversations.AsNoTracking().AnyAsync(c =>
                c.Id == request.ConversationId && c.UserId == request.UserId &&
                c.Type == "Project" && c.ProjectId == request.ProjectId &&
                db.Users.AsNoTracking().Any(u => u.Id == request.UserId && u.IsActive) &&
                db.Projects.AsNoTracking().Any(p => p.Id == request.ProjectId &&
                    p.UserId == request.UserId), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return owned
                ? new(OwnershipLookupStatus.Found, request, true, true, true)
                : new(OwnershipLookupStatus.Denied, null, false, false, false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(OwnershipLookupStatus.Unavailable, null, false, false, false);
        }
    }
}
