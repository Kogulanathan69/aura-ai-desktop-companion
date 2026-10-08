using System.Reflection;
using System.Security.Claims;
using Aura.Api.Services;
using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.AspNetCore.Http;

internal static class CurrentUserRuntimeChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var accessor = new CountingHttpContextAccessor();
        var current = new HttpCurrentUserContext(accessor,
            new UnavailableAuthenticatedUserMappingSource());
        var internalUser = Guid.NewGuid();
        var subject = Guid.NewGuid();
        static HttpContext Context(bool authenticated, params Claim[] claims)
        {
            var context = new DefaultHttpContext();
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims,
                authenticated ? "Bearer" : null));
            return context;
        }
        async Task<bool> IsUnavailable() => (await current.GetAsync(default)) ==
            new CurrentUserSnapshot(Guid.Empty, CurrentUserStatus.Unavailable);

        accessor.Context = Context(true, new Claim("sub", subject.ToString("D")));
        check(await IsUnavailable(), "12B valid external sub remains unavailable without internal user mapping");
        accessor.Context = Context(false, new Claim("sub", subject.ToString("D")));
        check(await IsUnavailable(), "12B unauthenticated principal unavailable");
        accessor.Context = Context(true);
        check(await IsUnavailable(), "12B missing canonical sub unavailable");
        accessor.Context = Context(true, new Claim("sub", subject.ToString("D")),
            new Claim("sub", Guid.NewGuid().ToString("D")));
        check(await IsUnavailable(), "12B duplicate canonical sub unavailable");
        foreach (var value in new[] { "not-a-guid", Guid.Empty.ToString("D") })
        {
            accessor.Context = Context(true, new Claim("sub", value));
            check(await IsUnavailable(), "12B malformed or empty canonical subject unavailable");
        }
        accessor.Context = Context(true, new Claim(ClaimTypes.Email, "user@example.test"),
            new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.NameIdentifier, internalUser.ToString("D")));
        check(await IsUnavailable(), "12B email, role, and alternate user-id claims cannot authorize");
        accessor.Context = Context(true, new Claim("sub", subject.ToString("D")));
        accessor.Context.Request.Headers["X-User-Id"] = internalUser.ToString("D");
        check(await IsUnavailable(), "12B arbitrary user-id header ignored");
        accessor.Context = null;
        check(await IsUnavailable(), "12B absent HttpContext unavailable");
        check(accessor.Reads >= 9 && typeof(HttpCurrentUserContext).GetConstructors().Single()
                .GetParameters().Select(x => x.ParameterType).SequenceEqual([
                    typeof(IHttpContextAccessor), typeof(IAuthenticatedUserMappingSource)]) &&
            typeof(CurrentUserSnapshot).GetProperties().Select(x => x.Name)
                .SequenceEqual(["UserId", "Status"]),
            "12B fresh request read per call and bounded snapshot; no database dependency");

        // Composition remains denied even with an otherwise matching owned database row.
        var projectId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var db = DispatchProxy.Create<IAuraDbContext, OwnershipContextProxy>();
        var proxy = (OwnershipContextProxy)(object)db;
        proxy.Users = new CheckDbSet<User>([new User { Id = internalUser, AuthUserId = subject, IsActive = true }]);
        proxy.Projects = new CheckDbSet<Project>([new Project { Id = projectId, UserId = internalUser }]);
        proxy.Conversations = new CheckDbSet<Conversation>([new Conversation {
            Id = conversationId, UserId = internalUser, ProjectId = projectId, Type = "Project" }]);
        accessor.Context = Context(true, new Claim("sub", subject.ToString("D")));
        var validator = new ExactOwnershipScopeValidator(current, new OwnershipPersistenceSource(db));
        check(!await validator.ValidateAsync(new ActionScope(internalUser, conversationId, projectId), default) &&
            proxy.ConversationReads == 0 && proxy.Saves == 0,
            "12B external subject cannot bypass internal-user boundary or query ownership DB");
        check(!await validator.ValidateAsync(new ActionScope(subject, conversationId, projectId), default) &&
            proxy.ConversationReads == 0 && proxy.Saves == 0,
            "12B ActionScope user cannot fabricate current identity");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var propagated = false;
        try { await current.GetAsync(cancelled.Token); }
        catch (OperationCanceledException) { propagated = true; }
        check(propagated, "12B cancellation propagates before request access");
    }

    private sealed class CountingHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? Context { get; set; }
        public int Reads { get; private set; }
        public HttpContext? HttpContext
        {
            get { Reads++; return Context; }
            set => Context = value;
        }
    }
}
