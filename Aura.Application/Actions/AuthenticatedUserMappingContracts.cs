namespace Aura.Application.Actions;

// External authentication subject and internal ownership identity are distinct.
public enum AuthenticatedUserMappingStatus { Unavailable, NotFound, Found, Ambiguous }

public sealed record AuthenticatedUserMappingResult(AuthenticatedUserMappingStatus Status,
    Guid AuthUserId, Guid UserId);

public interface IAuthenticatedUserMappingSource
{
    Task<AuthenticatedUserMappingResult> GetAsync(Guid authUserId,
        CancellationToken cancellationToken);
}

public sealed class UnavailableAuthenticatedUserMappingSource : IAuthenticatedUserMappingSource
{
    public Task<AuthenticatedUserMappingResult> GetAsync(Guid authUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AuthenticatedUserMappingResult(
            AuthenticatedUserMappingStatus.Unavailable, Guid.Empty, Guid.Empty));
    }
}
