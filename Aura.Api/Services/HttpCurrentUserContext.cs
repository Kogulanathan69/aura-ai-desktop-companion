using Aura.Application.Actions;

namespace Aura.Api.Services;

// Foundation only; deliberately unregistered. The existing JWT "sub" is
// User.AuthUserId, while ActionScope.UserId requires User.Id. No database lookup
// or untrusted claim-to-internal-ID assumption belongs in this request adapter.
public sealed class HttpCurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    public Task<CurrentUserSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
                return Unavailable();

            // The repository's JWT bearer mapping disables inbound claim remapping.
            // Require one canonical subject; never fall back to roles, email, or headers.
            var subjects = principal.FindAll("sub").Take(2).ToArray();
            if (subjects.Length != 1 || !Guid.TryParse(subjects[0].Value, out var subject) ||
                subject == Guid.Empty)
                return Unavailable();

            // A valid external subject is not the internal User.Id required by
            // ExactOwnershipScopeValidator. Mapping remains a separate future step.
            cancellationToken.ThrowIfCancellationRequested();
            return Unavailable();
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

    private static Task<CurrentUserSnapshot> Unavailable() =>
        Task.FromResult(new CurrentUserSnapshot(Guid.Empty, CurrentUserStatus.Unavailable));
}
