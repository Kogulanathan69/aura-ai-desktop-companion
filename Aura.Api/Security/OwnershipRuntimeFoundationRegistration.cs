using Aura.Api.Services;
using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Verifications;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Aura.Api.Security;

// Explicit, future-only composition. Normal Program.cs deliberately does not call it.
// Registration is not a readiness claim or permission to enable production workflows.
public static class OwnershipRuntimeFoundationRegistration
{
    public static IServiceCollection AddOwnershipRuntimeFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var guarded = new[] { typeof(ICurrentUserContext), typeof(IOwnershipSource),
            typeof(ExactOwnershipScopeValidator), typeof(IActionScopeValidator),
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
        services.AddScoped<IActionScopeValidator>(provider =>
            provider.GetRequiredService<ExactOwnershipScopeValidator>());
        services.AddScoped<IActionApprovalScopeValidator>(provider =>
            provider.GetRequiredService<ExactOwnershipScopeValidator>());
        services.AddScoped<IToolExecutionScopeValidator>(provider =>
            provider.GetRequiredService<ExactOwnershipScopeValidator>());
        services.AddScoped<IVerificationScopeValidator>(provider =>
            provider.GetRequiredService<ExactOwnershipScopeValidator>());
        services.AddScoped<IExecutionReconciliationScopeValidator>(provider =>
            provider.GetRequiredService<ExactOwnershipScopeValidator>());
        return services;
    }
}
