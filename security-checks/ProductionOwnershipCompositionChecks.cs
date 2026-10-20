using Aura.Api.Security;
using Aura.Api.Services;
using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionOwnershipCompositionChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var api = Read("Aura.Api", "Program.cs");
        var current = Read("Aura.Api", "Services", "CurrentUserService.cs");
        var request = Read("Aura.Api", "Services", "HttpCurrentUserContext.cs");
        var mapping = Read("Aura.Infrastructure", "Data", "AuthenticatedUserMappingSource.cs");
        var ownership = Read("Aura.Infrastructure", "Data", "OwnershipPersistenceSource.cs");
        var validator = Read("Aura.Application", "Actions", "OwnershipScopeValidator.cs");

        check(api.Contains("options.MapInboundClaims = false", StringComparison.Ordinal) &&
            current.Contains("FindAll(\"sub\")", StringComparison.Ordinal) &&
            request.Contains("FindAll(\"sub\")", StringComparison.Ordinal) &&
            !request.Contains("NameIdentifier", StringComparison.Ordinal),
            "14F endpoint and workflow identities use one raw JWT subject");
        check(request.Contains("mapping.GetAsync(subject, cancellationToken)", StringComparison.Ordinal) &&
            request.Contains("result.AuthUserId == subject", StringComparison.Ordinal) &&
            mapping.Contains("user.AuthUserId == authUserId && user.IsActive", StringComparison.Ordinal) &&
            mapping.Contains(".Take(2)", StringComparison.Ordinal) &&
            mapping.Contains(".AsNoTracking()", StringComparison.Ordinal),
            "14F exact active subject maps to one bounded internal user without tracking");
        check(ownership.Contains("c.UserId == request.UserId", StringComparison.Ordinal) &&
            ownership.Contains("c.ProjectId == request.ProjectId", StringComparison.Ordinal) &&
            ownership.Contains("p.UserId == request.UserId", StringComparison.Ordinal) &&
            ownership.Contains("u.IsActive", StringComparison.Ordinal) &&
            ownership.Contains(".AsNoTracking()", StringComparison.Ordinal),
            "14F ownership query requires active user and exact conversation-project-user relation");
        check(validator.Contains("user.UserId != scope.UserId", StringComparison.Ordinal) &&
            validator.Contains("result.Binding == request", StringComparison.Ordinal) &&
            validator.Contains("IToolExecutionScopeValidator", StringComparison.Ordinal),
            "14F tool execution validator binds authenticated internal user to exact ownership result");

        var services = new ServiceCollection();
        services.AddScoped<IAuraDbContext>(_ => throw new InvalidOperationException());
        services.AddOwnershipRuntimeFoundation();
        check(services.Count(x => x.ServiceType == typeof(IAuthenticatedUserMappingSource)) == 1 &&
            services.Single(x => x.ServiceType == typeof(IAuthenticatedUserMappingSource))
                .ImplementationType == typeof(AuthenticatedUserMappingSource) &&
            services.Single(x => x.ServiceType == typeof(ICurrentUserContext))
                .ImplementationType == typeof(HttpCurrentUserContext) &&
            services.Single(x => x.ServiceType == typeof(IOwnershipSource))
                .ImplementationType == typeof(OwnershipPersistenceSource) &&
            services.Single(x => x.ServiceType == typeof(IToolExecutionScopeValidator))
                .Lifetime == ServiceLifetime.Scoped,
            "14F isolated ownership helper has exact scoped mapping, context, source and tool validator");
        check(!ProductionRuntimeCompositionInspector.Inspect(services).Ownership &&
            !ProductionRuntimeCompositionInspector.Inspect(services).CompositionComplete,
            "14F future-only ownership helper does not constitute reviewed production composition");
        services.AddScoped<IAuthenticatedUserMappingSource, AuthenticatedUserMappingSource>();
        check(!ProductionRuntimeCompositionInspector.Inspect(services).Ownership,
            "14F ambiguous subject mapping remains unready");

        var normal = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        var flags = new RuntimeSafetyOptions();
        check(api.Contains(".AddOwnershipRuntimeFoundation(", StringComparison.Ordinal) &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal) &&
            !normal.Ownership && !normal.CompositionComplete &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().Ownership &&
            !flags.ProductionAdaptersEnabled && !flags.ToolExecutionEnabled &&
            !flags.SafeFileAccessEnabled && !flags.CloudAiEnabled,
            "14F normal API retains unavailable ownership and disabled execution, file and cloud flags");
        check(!api.Contains("AddScoped<ITrustedToolExecutionService", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !api.Contains("AddHostedService", StringComparison.Ordinal),
            "14F adds no trusted executor, migration or background worker");
        return Task.CompletedTask;
    }
}
