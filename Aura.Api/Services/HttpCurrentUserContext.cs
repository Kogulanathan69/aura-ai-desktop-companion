using Aura.Application.Actions;

namespace Aura.Api.Services;

// Foundation only; deliberately unregistered. The existing JWT "sub" maps through
// a bounded source to internal User.Id; this request adapter performs no DB query.
public sealed class HttpCurrentUserContext(IHttpContextAccessor accessor,
    IAuthenticatedUserMappingSource mapping) : ICurrentUserContext
{
    public async Task<CurrentUserSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
                return Unavailable();

            var authenticatedIdentities = principal.Identities
                .Where(identity => identity.IsAuthenticated).Take(2).ToArray();
            if (authenticatedIdentities.Length != 1)
                return Unavailable();

            // The repository's JWT bearer mapping disables inbound claim remapping.
            // Require one canonical subject; never fall back to roles, email, or headers.
            var subjects = principal.FindAll("sub").Take(2).ToArray();
            if (subjects.Length != 1 ||
                !ReferenceEquals(subjects[0].Subject, authenticatedIdentities[0]) ||
                !Guid.TryParse(subjects[0].Value, out var subject) ||
                subject == Guid.Empty)
                return Unavailable();

            var result = await mapping.GetAsync(subject, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result is not null && result.Status == AuthenticatedUserMappingStatus.Found &&
                result.AuthUserId == subject && result.UserId != Guid.Empty
                ? new CurrentUserSnapshot(result.UserId, CurrentUserStatus.Authenticated)
                : Unavailable();
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

    private static CurrentUserSnapshot Unavailable() =>
        new(Guid.Empty, CurrentUserStatus.Unavailable);
}
