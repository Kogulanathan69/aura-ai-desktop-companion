using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;

namespace Aura.Application.Common.Services;

public sealed class UserIdentityService : IUserIdentityService
{
    private readonly IAuthenticatedUserMappingSource _mappingSource;
    private readonly ICurrentUserService _currentUserService;

    public UserIdentityService(IAuthenticatedUserMappingSource mappingSource,
        ICurrentUserService currentUserService)
    {
        _mappingSource = mappingSource;
        _currentUserService = currentUserService;
    }

    public async Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!_currentUserService.IsAuthenticated)
                throw new UnauthorizedAccessException("An authenticated user is required.");

            var authUserId = _currentUserService.AuthUserId;
            if (string.IsNullOrWhiteSpace(authUserId) ||
                !Guid.TryParse(authUserId, out var authUserGuid) || authUserGuid == Guid.Empty)
                throw new UnauthorizedAccessException("Authenticated user identifier is invalid.");

            var mapping = await _mappingSource.GetAsync(authUserGuid, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (mapping is not null && mapping.Status == AuthenticatedUserMappingStatus.Found &&
                mapping.AuthUserId == authUserGuid && mapping.UserId != Guid.Empty)
                return mapping.UserId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        throw new UnauthorizedAccessException("Authenticated user is unavailable.");
    }
}
