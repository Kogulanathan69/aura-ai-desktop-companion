using Aura.Application.Actions;
using Aura.Application.Auditing;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Application.Verifications;
using Aura.Application.Common.Interfaces;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
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
    private static readonly IReadOnlySet<Type> ReviewedVerificationWriter = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedVerificationWorkflow = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedVerificationFactory = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedExecutionPolicy = new HashSet<Type>();
    private static readonly IReadOnlyList<(Type HandlerType, string ToolId, ToolCapability Capability,
        ToolPermissionRequirement Permission)> ReviewedToolHandlers =
        Array.Empty<(Type, string, ToolCapability, ToolPermissionRequirement)>();
    private static readonly IReadOnlySet<Type> ReviewedTrustedExecution = new HashSet<Type>();

    public static ProductionRuntimeCompositionSnapshot Inspect(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var ownership = HasOneScoped<IAuraDbContext>(services) &&
            HasOne<IAuthenticatedUserMappingSource>(services, ReviewedSubjectMapping) &&
            HasOne<ICurrentUserContext>(services, ReviewedOwnershipContext) &&
            HasOne<IOwnershipSource>(services, ReviewedOwnershipSource) &&
            HasOne<IToolExecutionScopeValidator>(services, ReviewedOwnershipValidator);
        var permission = HasExactPermissionSource(services);
        var executionState = HasExactExecutionStateStore(services);
        var auditSink = HasExactAuditSink(services);
        var auditDelivery = HasExactAuditDeliveryDispatcher(services);
        var reconciliation = HasExactReconciliationSource(services);
        var verification = HasExactVerificationSource(services) &&
            HasExactVerificationWriter(services) &&
            HasExactVerificationWorkflow(services) &&
            HasExactVerificationFactory(services);
        var executionPolicy = HasExactExecutionPolicy(services);
        var toolHandlers = MatchesReviewedToolHandlers(services, ReviewedToolHandlers);
        var trustedExecution = HasExactTrustedExecutionService(services) &&
            TrustedPrerequisitesReady(ownership, permission, executionState, auditSink,
                auditDelivery, reconciliation, verification, executionPolicy, toolHandlers);
        return new(ownership, permission, executionState, auditSink, auditDelivery,
            reconciliation, verification, executionPolicy, toolHandlers, trustedExecution);
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

    // An explicit one-item dispatcher still needs a reviewed production lifetime
    // and registration. Tables, the sink, and manual methods do not assert delivery.
    private static bool HasExactAuditDeliveryDispatcher(IServiceCollection services)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(IAuditOutboxDispatcher))
            .ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType == typeof(SecurityAuditOutboxDispatcher) &&
            ReviewedAuditDelivery.Contains(typeof(SecurityAuditOutboxDispatcher));
    }

    // Durable state and an advisory source do not assert reviewed reconciliation.
    // The reviewed catalog remains empty until separate production evidence exists.
    private static bool HasExactReconciliationSource(IServiceCollection services)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(IExecutionReconciliationSource))
            .ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType == typeof(ExecutionReconciliationPersistenceSource) &&
            ReviewedReconciliation.Contains(typeof(ExecutionReconciliationPersistenceSource));
    }

    // Presence alone is insufficient: each node must be exact, scoped and reviewed.
    private static bool HasExactVerificationSource(IServiceCollection services)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(IExecutionEvidenceSource))
            .ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType is { } implementation &&
            implementation != typeof(UnavailableExecutionEvidenceSource) &&
            ReviewedVerification.Contains(implementation);
    }

    private static bool HasExactVerificationWriter(IServiceCollection services)
    {
        var registrations = services.Where(item =>
            item.ServiceType == typeof(IExecutionVerificationAssessmentWriter)).ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType ==
                typeof(ExecutionVerificationAssessmentPersistenceWriter) &&
            ReviewedVerificationWriter.Contains(typeof(ExecutionVerificationAssessmentPersistenceWriter));
    }

    private static bool HasExactVerificationWorkflow(IServiceCollection services)
    {
        var registrations = services.Where(item =>
            item.ServiceType == typeof(IVerificationWorkflowService)).ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType == typeof(VerificationWorkflowService) &&
            ReviewedVerificationWorkflow.Contains(typeof(VerificationWorkflowService));
    }

    private static bool HasExactVerificationFactory(IServiceCollection services)
    {
        var registrations = services.Where(item =>
            item.ServiceType == typeof(IDbContextFactory<AuraDbContext>)).ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Singleton &&
            registrations[0].ImplementationType is { } implementation &&
            ReviewedVerificationFactory.Contains(implementation);
    }

    // No allowing production policy exists yet. The deny-only implementation
    // cannot count even if it is registered; review must name a future exact type.
    private static bool HasExactExecutionPolicy(IServiceCollection services)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(IToolExecutionPolicy))
            .ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType is { } implementation &&
            implementation != typeof(DenyToolExecutionPolicy) &&
            ReviewedExecutionPolicy.Contains(implementation);
    }

    // An empty catalog is never readiness. Each future entry must name one exact
    // handler, canonical tool ID, capability and corresponding permission.
    private static bool MatchesReviewedToolHandlers(IServiceCollection services,
        IReadOnlyList<(Type HandlerType, string ToolId, ToolCapability Capability,
            ToolPermissionRequirement Permission)> reviewed)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(IToolHandler))
            .ToArray();
        if (reviewed.Count is 0 or > ToolLimits.MaximumTools || registrations.Length != reviewed.Count ||
            reviewed.Select(item => item.HandlerType).Distinct().Count() != reviewed.Count ||
            reviewed.Select(item => item.ToolId).Distinct(StringComparer.Ordinal).Count() != reviewed.Count)
            return false;

        foreach (var item in reviewed)
        {
            if (!ToolIdentifier.TryCreate(item.ToolId, out var id) || id!.Value != item.ToolId ||
                (item.Capability, item.Permission) is not
                    ((ToolCapability.ProjectRead, ToolPermissionRequirement.OwnedProjectRead) or
                     (ToolCapability.ProjectWrite, ToolPermissionRequirement.OwnedProjectWrite) or
                     (ToolCapability.ExternalAction, ToolPermissionRequirement.ExternalAction)) ||
                registrations.Count(descriptor => descriptor.ImplementationType == item.HandlerType &&
                    descriptor.Lifetime == ServiceLifetime.Scoped) != 1)
                return false;
        }
        return true;
    }

    // The exact service cannot assert readiness unless every separately reviewed
    // prerequisite is present. The catalog remains empty pending production review.
    private static bool HasExactTrustedExecutionService(IServiceCollection services)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(ITrustedToolExecutionService))
            .ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType == typeof(TrustedToolExecutionService) &&
            ReviewedTrustedExecution.Contains(typeof(TrustedToolExecutionService));
    }

    private static bool TrustedPrerequisitesReady(bool ownership, bool permission,
        bool executionState, bool auditSink, bool auditDelivery, bool reconciliation,
        bool verification, bool executionPolicy, bool toolHandlers) =>
        ownership && permission && executionState && auditSink && auditDelivery &&
        reconciliation && verification && executionPolicy && toolHandlers;

    // The actual context registration uses a scoped factory. This is a necessary
    // registration-shape check, never sufficient production review by itself.
    private static bool HasOneScoped<T>(IServiceCollection services)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(T)).ToArray();
        return registrations.Length == 1 && registrations[0].Lifetime == ServiceLifetime.Scoped;
    }
}
