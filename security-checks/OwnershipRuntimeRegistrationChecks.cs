using System.Reflection;
using System.Security.Claims;
using Aura.Api.Security;
using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Verifications;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

internal static class OwnershipRuntimeRegistrationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var subject = Guid.NewGuid();
        var internalId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var user = new User { AuthUserId = subject, Id = internalId, IsActive = true };
        var conversation = new Conversation { Id = conversationId, UserId = internalId,
            ProjectId = projectId, Type = "Project" };
        var project = new Project { Id = projectId, UserId = internalId };
        var db = DispatchProxy.Create<IAuraDbContext, OwnershipContextProxy>();
        var proxy = (OwnershipContextProxy)(object)db;
        proxy.Users = new CheckDbSet<User>([user]);
        proxy.Conversations = new CheckDbSet<Conversation>([conversation]);
        proxy.Projects = new CheckDbSet<Project>([project]);
        var accessor = new HttpContextAccessor();
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", subject.ToString("D"))], "Bearer"));
        accessor.HttpContext = http;

        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(accessor);
        services.AddScoped<IAuraDbContext>(_ => db);
        services.AddScoped<IAuthenticatedUserMappingSource, AuthenticatedUserMappingSource>();
        var existingMapping = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IAuthenticatedUserMappingSource));
        services.AddOwnershipRuntimeFoundation();
        check(ReferenceEquals(existingMapping, services.Single(descriptor =>
                descriptor.ServiceType == typeof(IAuthenticatedUserMappingSource))) &&
            services.Count(descriptor => descriptor.ServiceType == typeof(IAuthenticatedUserMappingSource)) == 1,
            "12E explicit foundation reuses existing scoped subject mapping registration");
        var scoped = new[] { typeof(ICurrentUserContext), typeof(IOwnershipSource),
            typeof(ExactOwnershipScopeValidator), typeof(IActionScopeValidator),
            typeof(IActionApprovalScopeValidator), typeof(IToolExecutionScopeValidator),
            typeof(IVerificationScopeValidator), typeof(IExecutionReconciliationScopeValidator) };
        check(scoped.All(type => services.Count(descriptor => descriptor.ServiceType == type &&
                descriptor.Lifetime == ServiceLifetime.Scoped) == 1) &&
            services.All(descriptor => descriptor.ServiceType != typeof(ICurrentUserContext) ||
                descriptor.Lifetime != ServiceLifetime.Singleton) &&
            proxy.UserReads == 0 && proxy.ConversationReads == 0,
            "12E registration has scoped ownership components and performs no DB probe");
        check(!services.Any(descriptor => descriptor.ServiceType == typeof(IToolExecutionPermissionValidator) ||
            descriptor.ServiceType == typeof(IToolExecutionPolicy)),
            "12E ownership wiring adds no permission or execution policy");

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var first = provider.CreateScope();
        var scopedProvider = first.ServiceProvider;
        var validator = scopedProvider.GetRequiredService<ExactOwnershipScopeValidator>();
        check(ReferenceEquals(validator, scopedProvider.GetRequiredService<IActionScopeValidator>()) &&
            ReferenceEquals(validator, scopedProvider.GetRequiredService<IActionApprovalScopeValidator>()) &&
            ReferenceEquals(validator, scopedProvider.GetRequiredService<IToolExecutionScopeValidator>()) &&
            ReferenceEquals(validator, scopedProvider.GetRequiredService<IVerificationScopeValidator>()) &&
            ReferenceEquals(validator, scopedProvider.GetRequiredService<IExecutionReconciliationScopeValidator>()),
            "12E all five workflow interfaces resolve the same scoped exact validator");
        check(scopedProvider.GetRequiredService<ICurrentUserContext>() is Aura.Api.Services.HttpCurrentUserContext &&
            scopedProvider.GetRequiredService<IOwnershipSource>() is OwnershipPersistenceSource &&
            scopedProvider.GetRequiredService<IAuthenticatedUserMappingSource>() is AuthenticatedUserMappingSource,
            "12E complete foundation chain resolves reviewed adapter types only");
        using (var second = provider.CreateScope())
            check(!ReferenceEquals(validator,
                second.ServiceProvider.GetRequiredService<ExactOwnershipScopeValidator>()),
                "12E request-scoped validator is not shared across scopes");

        var scope = new ActionScope(internalId, conversationId, projectId);
        check(await validator.ValidateAsync(scope, default) && proxy.Saves == 0,
            "12E isolated DI composition validates exact active identity and ownership read-only");
        check(!await validator.ValidateAsync(scope with { UserId = subject }, default),
            "12E external subject cannot bypass internal mapping");
        check(!await validator.ValidateAsync(scope with { ConversationId = Guid.NewGuid() }, default) &&
            !await validator.ValidateAsync(scope with { ProjectId = Guid.NewGuid() }, default),
            "12E foreign conversation or project denied");
        conversation.ProjectId = Guid.NewGuid();
        check(!await validator.ValidateAsync(scope, default),
            "12E exact conversation-project relation remains required");
        conversation.ProjectId = projectId;
        user.IsActive = false;
        check(!await validator.ValidateAsync(scope, default), "12E inactive user denied");
        user.IsActive = true;
        http.User = new ClaimsPrincipal(new ClaimsIdentity());
        var before = proxy.ConversationReads;
        check(!await validator.ValidateAsync(scope, default) && proxy.ConversationReads == before,
            "12E ownership data alone cannot bypass authenticated current user");
        http.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", subject.ToString("D"))], "Bearer"));
        project.UserId = Guid.NewGuid();
        check(!await validator.ValidateAsync(scope, default),
            "12E authenticated current user alone cannot bypass project ownership");

        var absent = new ServiceCollection();
        var rejected = false;
        try { absent.AddOwnershipRuntimeFoundation(); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && absent.Count == 0,
            "12E missing scoped DB registration fails before partial ownership registration");
        var ambiguousDb = new ServiceCollection();
        ambiguousDb.AddScoped<IAuraDbContext>(_ => db);
        ambiguousDb.AddSingleton<IAuraDbContext>(db);
        rejected = false;
        try { ambiguousDb.AddOwnershipRuntimeFoundation(); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && ambiguousDb.Count == 2,
            "12E ambiguous or singleton DB registration cannot be selected silently");
        var conflict = new ServiceCollection();
        conflict.AddScoped<IAuraDbContext>(_ => db);
        conflict.AddScoped<IActionScopeValidator>(_ => null!);
        rejected = false;
        try { conflict.AddOwnershipRuntimeFoundation(); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && conflict.Count == 2,
            "12E conflicting workflow validator fails before mutation");
        var badMapping = new ServiceCollection();
        badMapping.AddScoped<IAuraDbContext>(_ => db);
        badMapping.AddSingleton<IAuthenticatedUserMappingSource>(
            new UnavailableAuthenticatedUserMappingSource());
        rejected = false;
        try { badMapping.AddOwnershipRuntimeFoundation(); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && badMapping.Count == 2,
            "12E singleton or unreviewed mapping registration is rejected");
        check(!new UnavailableRuntimeCapabilityReadiness().GetSnapshot().Ownership &&
            new PersistenceCapabilityReadiness().GetSnapshot().Ownership &&
            !new RuntimeSafetyOptions().ProductionAdaptersEnabled &&
            !new RuntimeSafetyOptions().ToolExecutionEnabled,
            "12E implementation presence does not change disabled runtime readiness");
    }
}
