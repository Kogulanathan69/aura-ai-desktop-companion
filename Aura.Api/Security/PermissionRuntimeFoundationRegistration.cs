using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Aura.Api.Security;

// Exact production grant graph. Registration does not activate tool execution.
public static class PermissionRuntimeFoundationRegistration
{
    public static IServiceCollection AddPermissionRuntimeFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var guarded = new[] { typeof(IToolPermissionSource), typeof(IToolPermissionPolicy),
            typeof(IToolExecutionPermissionValidator) };
        if (services.Any(item => guarded.Contains(item.ServiceType)))
            throw new InvalidOperationException("Permission runtime registration conflicts.");
        var db = services.Where(item => item.ServiceType == typeof(IAuraDbContext)).ToArray();
        if (db.Length != 1 || !OwnershipRuntimeFoundationRegistration.IsExactDbContextAlias(db[0]))
            throw new InvalidOperationException("Permission database registration is unavailable.");
        services.AddScoped<IToolPermissionSource, ToolPermissionPersistenceSource>();
        services.AddScoped<IToolPermissionPolicy, ExactToolPermissionPolicy>();
        services.AddScoped<IToolExecutionPermissionValidator, ToolExecutionPermissionValidator>();
        return services;
    }
}
