using Aura.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Aura.Application.Common.Services;

public sealed class UserIdentityService : IUserIdentityService
{
    private readonly IAuraDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public UserIdentityService(
        IAuraDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<Guid> GetCurrentUserIdAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.IsAuthenticated)
        {
            throw new UnauthorizedAccessException(
                "An authenticated user is required.");
        }

        var authUserId = _currentUserService.AuthUserId;

        if (string.IsNullOrWhiteSpace(authUserId))
        {
            throw new UnauthorizedAccessException(
                "Authenticated user identifier is missing.");
        }

        if (!Guid.TryParse(authUserId, out var authUserGuid))
        {
            throw new UnauthorizedAccessException(
                "Authenticated user identifier is invalid.");
        }

        var userId = await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.AuthUserId == authUserGuid)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId is null)
        {
            throw new UnauthorizedAccessException(
                "Authenticated user is not registered in AURA.");
        }

        return userId.Value;
    }
}