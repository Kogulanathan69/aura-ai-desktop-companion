using Aura.Application.Auditing;
using Aura.Application.Executions;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aura.Api.Security;

// Exact persistence graph only. Registration does not enable execution, delivery,
// polling, retries, migrations, runtime flags, or final activation.
public static class StateAuditReconciliationRuntimeFoundationRegistration
{
    public static IServiceCollection AddStateAuditReconciliationRuntimeFoundation(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var guarded = new[]
        {
            typeof(IActionExecutionStateStore), typeof(IAuditEventSink),
            typeof(IAuditEventWriter), typeof(IAuditOutboxDispatcher),
            typeof(IExecutionReconciliationSource),
            typeof(IExecutionReconciliationAssessmentWriter),
            typeof(IExecutionReconciliationPolicy), typeof(IExecutionReconciliationService)
        };
        if (services.Any(item => guarded.Contains(item.ServiceType)))
            throw new InvalidOperationException("State, audit, or reconciliation registration conflicts.");

        // Execution state remains unregistered until persisted ToolId binding and
        // changed-path live evidence close its independent readiness blockers.
        services.AddScoped<IAuditEventSink, SecurityAuditPersistenceSink>();
        services.AddScoped<IAuditEventWriter, AuditEventWriter>();
        services.AddScoped<IAuditOutboxDispatcher, SecurityAuditOutboxDispatcher>();
        services.AddScoped<IExecutionReconciliationSource,
            ExecutionReconciliationPersistenceSource>();
        services.AddScoped<IExecutionReconciliationAssessmentWriter,
            ExecutionReconciliationAssessmentPersistenceWriter>();
        services.AddScoped<IExecutionReconciliationPolicy,
            ConservativeExecutionReconciliationPolicy>();
        services.AddScoped<IExecutionReconciliationService, ExecutionReconciliationService>();
        return services;
    }
}
