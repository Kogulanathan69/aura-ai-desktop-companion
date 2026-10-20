using Aura.Api.Services;
using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Verifications;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Aura.Api.Security;

// Explicit production composition. Registration alone does not establish readiness
// or permission to enable production workflows.
public static class OwnershipRuntimeFoundationRegistration
{
    public static IAuraDbContext ResolveProductionDbContext(IServiceProvider provider) =>
        provider.GetRequiredService<AuraDbContext>();

    internal static bool IsExactDbContextAlias(ServiceDescriptor descriptor) =>
        descriptor.Lifetime == ServiceLifetime.Scoped &&
        descriptor.ImplementationFactory?.Method ==
            ((Func<IServiceProvider, IAuraDbContext>)ResolveProductionDbContext).Method;

    public static IServiceCollection AddOwnershipRuntimeFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var guarded = new[] { typeof(ICurrentUserContext), typeof(IOwnershipSource),
            typeof(ExactOwnershipScopeValidator), typeof(IExactActionOwnershipValidator),
            typeof(IActionScopeValidator),
            typeof(IActionApprovalScopeValidator), typeof(IToolExecutionScopeValidator),
            typeof(IVerificationScopeValidator), typeof(IExecutionReconciliationScopeValidator) };
        if (services.Any(descriptor => guarded.Contains(descriptor.ServiceType)))
            throw new InvalidOperationException("Ownership runtime registration conflicts.");
        var contexts = services.Where(descriptor =>
            descriptor.ServiceType == typeof(IAuraDbContext)).ToArray();
        if (contexts.Length != 1 || contexts[0].Lifetime != ServiceLifetime.Scoped)
            throw new InvalidOperationException("Ownership database registration is unavailable.");

        var mappings = services.Where(descriptor =>
            descriptor.ServiceType == typeof(IAuthenticatedUserMappingSource)).ToArray();
        if (mappings.Length == 0)
            services.AddScoped<IAuthenticatedUserMappingSource, AuthenticatedUserMappingSource>();
        else if (mappings.Length != 1 || mappings[0].Lifetime != ServiceLifetime.Scoped ||
            mappings[0].ImplementationType != typeof(AuthenticatedUserMappingSource))
            throw new InvalidOperationException("Ownership mapping registration conflicts.");

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
        services.AddScoped<IOwnershipSource, OwnershipPersistenceSource>();
        services.AddScoped<ExactOwnershipScopeValidator>();
        services.AddScoped<IExactActionOwnershipValidator>(ResolveValidator);
        services.AddScoped<IActionScopeValidator>(ResolveValidator);
        services.AddScoped<IActionApprovalScopeValidator>(ResolveValidator);
        services.AddScoped<IToolExecutionScopeValidator>(ResolveValidator);
        services.AddScoped<IVerificationScopeValidator>(ResolveValidator);
        services.AddScoped<IExecutionReconciliationScopeValidator>(ResolveValidator);
        return services;
    }

    private static ExactOwnershipScopeValidator ResolveValidator(IServiceProvider provider) =>
        provider.GetRequiredService<ExactOwnershipScopeValidator>();

    internal static bool IsExactValidatorAlias(ServiceDescriptor descriptor) =>
        descriptor.Lifetime == ServiceLifetime.Scoped &&
        descriptor.ImplementationFactory?.Method ==
            ((Func<IServiceProvider, ExactOwnershipScopeValidator>)ResolveValidator).Method;
}
