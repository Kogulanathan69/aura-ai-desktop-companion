using System.Reflection;
using System.Security.Claims;
using Aura.Api.Services;
using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.AspNetCore.Http;

internal static class AuthenticatedUserMappingChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var subject = Guid.NewGuid();
        var internalId = Guid.NewGuid();
        var user = new User { Id = internalId, AuthUserId = subject, IsActive = true };
        var rows = new List<User> { user };
        var db = DispatchProxy.Create<IAuraDbContext, OwnershipContextProxy>();
        var proxy = (OwnershipContextProxy)(object)db;
        proxy.Users = new CheckDbSet<User>(rows);
        var source = new AuthenticatedUserMappingSource(db);
        var found = await source.GetAsync(subject, default);
        check(found == new AuthenticatedUserMappingResult(AuthenticatedUserMappingStatus.Found,
            subject, internalId) && internalId != subject && proxy.Saves == 0,
            "12C exact active external subject maps to distinct internal user ID read-only");
        check(typeof(AuthenticatedUserMappingResult).GetProperties().Select(p => p.Name)
                .SequenceEqual(["Status", "AuthUserId", "UserId"]) &&
            typeof(IAuthenticatedUserMappingSource).GetMethods().Single().GetParameters()
                .Select(p => p.ParameterType).SequenceEqual([typeof(Guid), typeof(CancellationToken)]),
            "12C mapping contract exposes only bounded status and two GUIDs");
        check((await new UnavailableAuthenticatedUserMappingSource().GetAsync(subject, default)).Status ==
            AuthenticatedUserMappingStatus.Unavailable,
            "12C default mapping source unavailable");
        var before = proxy.UserReads;
        check((await source.GetAsync(Guid.Empty, default)).Status == AuthenticatedUserMappingStatus.NotFound &&
            proxy.UserReads == before, "12C empty external subject denied before query");
        check((await source.GetAsync(Guid.NewGuid(), default)).Status == AuthenticatedUserMappingStatus.NotFound,
            "12C missing external subject returns bounded NotFound");
        user.IsActive = false;
        check((await source.GetAsync(subject, default)).Status == AuthenticatedUserMappingStatus.NotFound,
            "12C inactive user cannot map");
        user.IsActive = true;
        rows.Add(new User { Id = Guid.NewGuid(), AuthUserId = subject, IsActive = true });
        check((await source.GetAsync(subject, default)).Status == AuthenticatedUserMappingStatus.Ambiguous,
            "12C duplicate active rows denied even with unique schema constraint");
        rows.RemoveAt(1);
        user.Id = Guid.Empty;
        check((await source.GetAsync(subject, default)).Status == AuthenticatedUserMappingStatus.Ambiguous,
            "12C empty internal user ID denied");
        user.Id = internalId;
        proxy.FailUserRead = true;
        before = proxy.UserReads;
        check((await source.GetAsync(subject, default)).Status == AuthenticatedUserMappingStatus.Unavailable &&
            proxy.UserReads == before + 1 && proxy.Saves == 0,
            "12C DB failure unavailable without leak, retry, fallback, or write");
        proxy.FailUserRead = false;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var propagated = false;
        try { await source.GetAsync(subject, cancelled.Token); }
        catch (OperationCanceledException) { propagated = true; }
        check(propagated, "12C mapping query cancellation propagates");

        var accessor = new HttpContextAccessor();
        static HttpContext Context(bool authenticated, params Claim[] claims)
        {
            var context = new DefaultHttpContext();
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims,
                authenticated ? "Bearer" : null));
            return context;
        }
        accessor.HttpContext = Context(true, new Claim("sub", subject.ToString("D")));
        var current = new HttpCurrentUserContext(accessor, source);
        check(await current.GetAsync(default) == new CurrentUserSnapshot(internalId,
            CurrentUserStatus.Authenticated), "12C valid sub plus exact mapping returns internal identity");

        var fake = new MappingFake { Result = found };
        current = new HttpCurrentUserContext(accessor, fake);
        check(await current.GetAsync(default) == new CurrentUserSnapshot(internalId,
            CurrentUserStatus.Authenticated) && fake.Calls == 1 && fake.LastSubject == subject,
            "12C HTTP context requests exact external subject freshly");
        foreach (var bad in new[] {
            found with { AuthUserId = Guid.NewGuid() },
            found with { UserId = Guid.Empty },
            found with { Status = (AuthenticatedUserMappingStatus)99 },
            found with { Status = AuthenticatedUserMappingStatus.Ambiguous },
            found with { Status = AuthenticatedUserMappingStatus.NotFound },
            found with { Status = AuthenticatedUserMappingStatus.Unavailable } })
        {
            fake.Result = bad;
            check((await current.GetAsync(default)).Status == CurrentUserStatus.Unavailable,
                "12C mismatched, empty, unknown, or non-Found mapping denied");
        }
        fake.Result = found;
        fake.Throw = true;
        before = fake.Calls;
        check((await current.GetAsync(default)).Status == CurrentUserStatus.Unavailable &&
            fake.Calls == before + 1, "12C mapping dependency failure safe without retry");
        fake.Throw = false;
        accessor.HttpContext = Context(false, new Claim("sub", subject.ToString("D")));
        before = fake.Calls;
        check((await current.GetAsync(default)).Status == CurrentUserStatus.Unavailable && fake.Calls == before,
            "12C unauthenticated principal never calls mapping source");
        accessor.HttpContext = Context(true, new Claim("sub", subject.ToString("D")),
            new Claim("sub", subject.ToString("D")));
        check((await current.GetAsync(default)).Status == CurrentUserStatus.Unavailable && fake.Calls == before,
            "12C duplicate sub denies before mapping");
        accessor.HttpContext = Context(true, new Claim("sub", "bad"));
        check((await current.GetAsync(default)).Status == CurrentUserStatus.Unavailable && fake.Calls == before,
            "12C malformed sub denies before mapping");
        var mixed = Context(true);
        mixed.User.AddIdentity(new ClaimsIdentity([new Claim("sub", subject.ToString("D"))]));
        accessor.HttpContext = mixed;
        check((await current.GetAsync(default)).Status == CurrentUserStatus.Unavailable && fake.Calls == before,
            "12C subject on unauthenticated secondary identity cannot map");
        var multiple = Context(true, new Claim("sub", subject.ToString("D")));
        multiple.User.AddIdentity(new ClaimsIdentity([new Claim("sub", subject.ToString("D"))], "Other"));
        accessor.HttpContext = multiple;
        check((await current.GetAsync(default)).Status == CurrentUserStatus.Unavailable && fake.Calls == before,
            "12C multiple authenticated identities fail closed");
        accessor.HttpContext = Context(true, new Claim(ClaimTypes.Email, "user@example.test"),
            new Claim(ClaimTypes.Role, "Admin"));
        check((await current.GetAsync(default)).Status == CurrentUserStatus.Unavailable && fake.Calls == before,
            "12C no email or role fallback");
        accessor.HttpContext = Context(true, new Claim("sub", subject.ToString("D")));
        fake.Result = found;
        check(await current.GetAsync(default) == new CurrentUserSnapshot(internalId,
            CurrentUserStatus.Authenticated) && fake.Calls == before + 1,
            "12C request principal and mapping are evaluated afresh");

        var projectId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        proxy.Projects = new CheckDbSet<Project>([new Project { Id = projectId, UserId = internalId }]);
        var conversation = new Conversation { Id = conversationId, UserId = internalId,
            ProjectId = projectId, Type = "Project" };
        proxy.Conversations = new CheckDbSet<Conversation>([conversation]);
        var validator = new ExactOwnershipScopeValidator(
            new HttpCurrentUserContext(accessor, source), new OwnershipPersistenceSource(db));
        var scope = new ActionScope(internalId, conversationId, projectId);
        check(await validator.ValidateAsync(scope, default) && proxy.Saves == 0,
            "12C subject mapping plus exact ownership source succeeds in test seam");
        check(!await validator.ValidateAsync(scope with { ConversationId = Guid.NewGuid() }, default) &&
            !await validator.ValidateAsync(scope with { ProjectId = Guid.NewGuid() }, default),
            "12C foreign conversation and project deny after valid identity");
        before = proxy.ConversationReads;
        check(!await validator.ValidateAsync(scope with { UserId = subject }, default) &&
            proxy.ConversationReads == before,
            "12C external subject cannot substitute for internal scope user");
        fake.Result = found with { UserId = Guid.NewGuid() };
        var fakeValidator = new ExactOwnershipScopeValidator(current, new OwnershipPersistenceSource(db));
        check(!await fakeValidator.ValidateAsync(scope, default) && proxy.ConversationReads == before,
            "12C mapped internal user mismatch denies before ownership query");
        propagated = false;
        try { await current.GetAsync(cancelled.Token); }
        catch (OperationCanceledException) { propagated = true; }
        check(propagated, "12C current-user cancellation propagates");
    }

    private sealed class MappingFake : IAuthenticatedUserMappingSource
    {
        public AuthenticatedUserMappingResult Result { get; set; } =
            new(AuthenticatedUserMappingStatus.Unavailable, Guid.Empty, Guid.Empty);
        public Guid LastSubject { get; private set; }
        public int Calls { get; private set; }
        public bool Throw { get; set; }
        public Task<AuthenticatedUserMappingResult> GetAsync(Guid authUserId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastSubject = authUserId;
            if (Throw) throw new InvalidOperationException("Synthetic private mapping error.");
            return Task.FromResult(Result);
        }
    }
}
