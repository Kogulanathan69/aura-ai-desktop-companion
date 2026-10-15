using Aura.Application.Actions;
using Aura.Application.Auditing;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Application.Verifications;
using Aura.Application.Common.Interfaces;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aura.Api.Security;

// Informational only. Every field requires one exact registered implementation from
// the reviewed production catalog. The catalog is empty until separate review adds one.
public sealed record ProductionRuntimeCompositionSnapshot(
    bool Ownership, bool Permission, bool ExecutionState, bool AuditSink,
    bool AuditDelivery, bool Reconciliation, bool Verification,
    bool ExecutionPolicy, bool ToolHandlers, bool TrustedExecution)
{
    public bool CompositionComplete => Ownership && Permission && ExecutionState &&
        AuditSink && AuditDelivery && Reconciliation && Verification &&
        ExecutionPolicy && ToolHandlers && TrustedExecution;
}

public static class ProductionRuntimeCompositionInspector
{
    // Add concrete types only after their production registration, lifetime and
    // security evidence have been reviewed. Interface presence is never sufficient.
    private static readonly IReadOnlySet<Type> ReviewedOwnershipContext = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedSubjectMapping = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedOwnershipSource = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedOwnershipValidator = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedPermission = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedExecutionState = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedAuditSink = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedAuditDelivery = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedReconciliation = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedVerification = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedExecutionPolicy = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedToolHandlers = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedTrustedExecution = new HashSet<Type>();

    public static ProductionRuntimeCompositionSnapshot Inspect(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new(
            HasOneScoped<IAuraDbContext>(services) &&
            HasOne<IAuthenticatedUserMappingSource>(services, ReviewedSubjectMapping) &&
            HasOne<ICurrentUserContext>(services, ReviewedOwnershipContext) &&
            HasOne<IOwnershipSource>(services, ReviewedOwnershipSource) &&
            HasOne<IToolExecutionScopeValidator>(services, ReviewedOwnershipValidator),
            HasExactPermissionSource(services),
            HasExactExecutionStateStore(services),
            HasExactAuditSink(services),
            HasOne<IAuditOutboxDispatcher>(services, ReviewedAuditDelivery),
            HasOne<IExecutionReconciliationSource>(services, ReviewedReconciliation),
            HasOne<IExecutionEvidenceSource>(services, ReviewedVerification),
            HasOne<IToolExecutionPolicy>(services, ReviewedExecutionPolicy),
            HasOne<IToolHandler>(services, ReviewedToolHandlers),
            HasOne<ITrustedToolExecutionService>(services, ReviewedTrustedExecution));
    }

    private static bool HasOne<T>(IServiceCollection services, IReadOnlySet<Type> reviewed)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(T)).ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType is { } implementation &&
            reviewed.Contains(implementation);
    }

    // A generic Permission adapter, unavailable source, factory or registration
    // alone cannot attest an exact tool grant. The reviewed catalog is empty today.
    private static bool HasExactPermissionSource(IServiceCollection services)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(IToolPermissionSource))
            .ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType == typeof(ToolPermissionPersistenceSource) &&
            ReviewedPermission.Contains(typeof(ToolPermissionPersistenceSource));
    }

    // Durable state requires the exact reviewed store and its normal scoped DI
    // registration. The reviewed catalog is empty until separate production review.
    private static bool HasExactExecutionStateStore(IServiceCollection services)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(IActionExecutionStateStore))
            .ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType == typeof(ActionExecutionStatePersistenceStore) &&
            ReviewedExecutionState.Contains(typeof(ActionExecutionStatePersistenceStore));
    }

    // A direct security-audit sink must be separately reviewed and registered.
    // Outbox delivery, generic logs and interface presence cannot substitute.
    private static bool HasExactAuditSink(IServiceCollection services)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(IAuditEventSink))
            .ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType == typeof(SecurityAuditPersistenceSink) &&
            ReviewedAuditSink.Contains(typeof(SecurityAuditPersistenceSink));
    }

    // The actual context registration uses a scoped factory. This is a necessary
    // registration-shape check, never sufficient production review by itself.
    private static bool HasOneScoped<T>(IServiceCollection services)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(T)).ToArray();
        return registrations.Length == 1 && registrations[0].Lifetime == ServiceLifetime.Scoped;
    }
}
