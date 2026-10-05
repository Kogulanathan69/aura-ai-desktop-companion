namespace Aura.Application.Common.Interfaces;

public interface IUserIdentityService
{
    Task<Guid> GetCurrentUserIdAsync(
        CancellationToken cancellationToken = default);
}