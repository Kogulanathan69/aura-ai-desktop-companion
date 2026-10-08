using System.Reflection;
using System.Security.Claims;
using Aura.Api.Services;
using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Common.Services;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.AspNetCore.Http;

internal static class UserIdentityHardeningChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var subject = Guid.NewGuid();
        var internalId = Guid.NewGuid();
        var user = new User { AuthUserId = subject, Id = internalId, IsActive = true };
        var rows = new List<User> { user };
        var db = DispatchProxy.Create<IAuraDbContext, OwnershipContextProxy>();
        var proxy = (OwnershipContextProxy)(object)db;
        proxy.Users = new CheckDbSet<User>(rows);
        var current = new CurrentFake { AuthUserId = subject.ToString("D"), IsAuthenticated = true };
        var mapping = new AuthenticatedUserMappingSource(db);
        var service = new UserIdentityService(mapping, current);
        check(await service.GetCurrentUserIdAsync() == internalId && internalId != subject && proxy.Saves == 0,
            "12D endpoint identity uses exact active mapping to internal User.Id without write");
        check(typeof(UserIdentityService).GetConstructors().Single().GetParameters()
                .Select(parameter => parameter.ParameterType).SequenceEqual([
                    typeof(IAuthenticatedUserMappingSource), typeof(ICurrentUserService)]),
            "12D identity service depends on reviewed mapping contract, not direct DbContext");

        async Task<bool> Denied(UserIdentityService target)
        {
            try { await target.GetCurrentUserIdAsync(); return false; }
            catch (UnauthorizedAccessException ex)
            { return ex.Message.Length <= 80 && ex.InnerException is null; }
        }
        user.IsActive = false;
        check(await Denied(service), "12D inactive user cannot resolve endpoint identity");
        user.IsActive = true;
        rows.Add(new User { AuthUserId = subject, Id = Guid.NewGuid(), IsActive = true });
        check(await Denied(service), "12D duplicate active mapping cannot first-match endpoint identity");
        rows.RemoveAt(1);
        rows.Clear();
        check(await Denied(service), "12D missing mapping denied");
        rows.Add(user);
        current.AuthUserId = Guid.Empty.ToString("D");
        var before = proxy.UserReads;
        check(await Denied(service) && proxy.UserReads == before,
            "12D empty external ID denied before mapping");
        current.AuthUserId = "not-a-guid";
        check(await Denied(service) && proxy.UserReads == before,
            "12D malformed external ID denied before mapping");
        current.AuthUserId = null;
        check(await Denied(service) && proxy.UserReads == before,
            "12D missing subject denied before mapping");
        current.AuthUserId = subject.ToString("D");
        current.IsAuthenticated = false;
        check(await Denied(service) && proxy.UserReads == before,
            "12D unauthenticated caller denied before mapping");
        current.IsAuthenticated = true;
        proxy.FailUserRead = true;
        before = proxy.UserReads;
        check(await Denied(service) && proxy.UserReads == before + 1 && proxy.Saves == 0,
            "12D database failure denied without retry, fallback, leakage, or write");
        proxy.FailUserRead = false;

        var fake = new MappingFake { Result = new(AuthenticatedUserMappingStatus.Found, subject, internalId) };
        service = new UserIdentityService(fake, current);
        check(await service.GetCurrentUserIdAsync() == internalId && fake.Calls == 1 &&
            fake.LastSubject == subject, "12D dependency receives exact canonical subject");
        foreach (var result in new[] {
            fake.Result with { AuthUserId = Guid.NewGuid() },
            fake.Result with { UserId = Guid.Empty },
            fake.Result with { Status = AuthenticatedUserMappingStatus.Unavailable },
            fake.Result with { Status = AuthenticatedUserMappingStatus.NotFound },
            fake.Result with { Status = AuthenticatedUserMappingStatus.Ambiguous },
            fake.Result with { Status = (AuthenticatedUserMappingStatus)99 } })
        {
            fake.Result = result;
            check(await Denied(service), "12D non-exact, empty, unavailable, or unknown mapping denied");
        }
        fake.Throw = true;
        before = fake.Calls;
        check(await Denied(service) && fake.Calls == before + 1,
            "12D mapping exception denied with no retry or raw error");
        fake.Throw = false;
        fake.Result = new(AuthenticatedUserMappingStatus.Found, subject, internalId);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var propagated = false;
        try { await service.GetCurrentUserIdAsync(cancelled.Token); }
        catch (OperationCanceledException) { propagated = true; }
        check(propagated, "12D cancellation propagates before lookup");
        fake.CancelOnRead = new CancellationTokenSource();
        propagated = false;
        try { await service.GetCurrentUserIdAsync(fake.CancelOnRead.Token); }
        catch (OperationCanceledException) { propagated = true; }
        check(propagated, "12D cancellation during mapping propagates");

        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", subject.ToString("D")),
            new Claim(ClaimTypes.Email, "other@example.test"),
            new Claim(ClaimTypes.Role, "Admin")], "Bearer"));
        var accessor = new HttpContextAccessor { HttpContext = context };
        var currentService = new CurrentUserService(accessor);
        check(currentService.IsAuthenticated && currentService.AuthUserId == subject.ToString("D"),
            "12D CurrentUserService still reads canonical sub, not email or role");
        var endpointIdentity = new UserIdentityService(mapping, currentService);
        check(await endpointIdentity.GetCurrentUserIdAsync() == internalId,
            "12D authenticated endpoint principal resolves only exact internal user ID");
        context.User.AddIdentity(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString("D"))]));
        check(currentService.AuthUserId is null && await Denied(endpointIdentity),
            "12D secondary unauthenticated subject cannot become endpoint identity");
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", subject.ToString("D")),
            new Claim("sub", Guid.NewGuid().ToString("D"))], "Bearer"));
        check(currentService.AuthUserId is null && await Denied(endpointIdentity),
            "12D duplicate canonical subjects deny endpoint identity");
        context.User.AddIdentity(new ClaimsIdentity([new Claim("sub", subject.ToString("D"))], "Other"));
        check(currentService.AuthUserId is null,
            "12D multiple authenticated identities deny endpoint identity");
    }

    private sealed class CurrentFake : ICurrentUserService
    {
        public string? AuthUserId { get; set; }
        public bool IsAuthenticated { get; set; }
    }

    private sealed class MappingFake : IAuthenticatedUserMappingSource
    {
        public AuthenticatedUserMappingResult Result { get; set; } =
            new(AuthenticatedUserMappingStatus.Unavailable, Guid.Empty, Guid.Empty);
        public Guid LastSubject { get; private set; }
        public int Calls { get; private set; }
        public bool Throw { get; set; }
        public CancellationTokenSource? CancelOnRead { get; set; }
        public Task<AuthenticatedUserMappingResult> GetAsync(Guid authUserId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastSubject = authUserId;
            if (Throw) throw new InvalidOperationException("Synthetic private mapping error.");
            CancelOnRead?.Cancel();
            return Task.FromResult(Result);
        }
    }
}
