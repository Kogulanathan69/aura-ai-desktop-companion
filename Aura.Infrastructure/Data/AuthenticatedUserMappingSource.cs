using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Aura.Infrastructure.Data;

// Read-only, unregistered external-subject to internal-user mapping.
public sealed class AuthenticatedUserMappingSource(IAuraDbContext db) : IAuthenticatedUserMappingSource
{
    public async Task<AuthenticatedUserMappingResult> GetAsync(Guid authUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authUserId == Guid.Empty)
            return new(AuthenticatedUserMappingStatus.NotFound, Guid.Empty, Guid.Empty);

        try
        {
            var matches = await db.Users.AsNoTracking()
                .Where(user => user.AuthUserId == authUserId && user.IsActive)
                .Select(user => user.Id)
                .Take(2)
                .ToArrayAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return matches.Length switch
            {
                0 => new(AuthenticatedUserMappingStatus.NotFound, Guid.Empty, Guid.Empty),
                1 when matches[0] != Guid.Empty =>
                    new(AuthenticatedUserMappingStatus.Found, authUserId, matches[0]),
                _ => new(AuthenticatedUserMappingStatus.Ambiguous, Guid.Empty, Guid.Empty)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(AuthenticatedUserMappingStatus.Unavailable, Guid.Empty, Guid.Empty);
        }
    }
}
