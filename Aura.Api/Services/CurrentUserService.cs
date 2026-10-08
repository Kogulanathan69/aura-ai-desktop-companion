using Aura.Application.Common.Interfaces;

namespace Aura.Api.Services;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? AuthUserId
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
                return null;
            var authenticatedIdentities = principal.Identities
                .Where(identity => identity.IsAuthenticated).Take(2).ToArray();
            if (authenticatedIdentities.Length != 1)
                return null;
            var subjects = principal.FindAll("sub").Take(2).ToArray();
            return subjects.Length == 1 &&
                ReferenceEquals(subjects[0].Subject, authenticatedIdentities[0])
                ? subjects[0].Value : null;
        }
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated
        ?? false;
}
