using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Auditing;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Application.Verifications;
using Aura.Application.Common.Interfaces;
using Aura.Api.Services;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aura.Api.Security;

// Informational only. Every field requires its complete exact production graph.
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
    private static readonly IReadOnlySet<Type> ReviewedOwnershipContext =
        new HashSet<Type> { typeof(HttpCurrentUserContext) };
    private static readonly IReadOnlySet<Type> ReviewedSubjectMapping =
        new HashSet<Type> { typeof(AuthenticatedUserMappingSource) };
    private static readonly IReadOnlySet<Type> ReviewedOwnershipSource =
        new HashSet<Type> { typeof(OwnershipPersistenceSource) };
    private static readonly IReadOnlySet<Type> ReviewedOwnershipValidator =
        new HashSet<Type> { typeof(ExactOwnershipScopeValidator) };
    // Operator-reviewed PASS from the gated signed-JWT HTTP harness on
    // aura_disposable_test, OID 24576, system 7696813047573034172.
    private const string OwnershipEvidenceDatabase = "aura_disposable_test";
    private const uint OwnershipEvidenceOid = 24576;
    private const long OwnershipEvidenceSystem = 7696813047573034172;
    private const string OwnershipEvidenceResult = "PASS";
    private static readonly IReadOnlySet<string> ReviewedOwnershipLiveEvidence =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "DB CONNECTIVITY", "SCHEMA CHECK", "FIXTURE SETUP", "SIGNED JWT ACCEPT",
            "JWT SUBJECT MAP", "OWNED PROJECT ALLOW", "FOREIGN PROJECT DENY",
            "UNKNOWN PROJECT DENY", "OWNED CONVERSATION ALLOW", "FOREIGN CONVERSATION DENY",
            "PROJECT CONVERSATION MISMATCH DENY", "OWNED ACTION ALLOW",
            "FOREIGN ACTION DENY", "ACTION CONVERSATION MISMATCH DENY",
            "ACTION PROJECT MISMATCH DENY", "ACTION USER CHAIN MISMATCH DENY",
            "UNAUTHENTICATED DENY", "MALFORMED TOKEN DENY", "UNKNOWN SUBJECT DENY",
            "NEGATIVE BINDINGS", "CLEANUP", "RESULT"
        };
    private static readonly IReadOnlySet<Type> ReviewedOwnershipFactory =
        new HashSet<Type> { typeof(AuraDbContextFactory) };
    private static readonly IReadOnlySet<Type> ReviewedPermission =
        new HashSet<Type> { typeof(ToolPermissionPersistenceSource) };
    private static readonly IReadOnlySet<Type> ReviewedPermissionPolicy =
        new HashSet<Type> { typeof(ExactToolPermissionPolicy) };
    private static readonly IReadOnlySet<Type> ReviewedPermissionValidator =
        new HashSet<Type> { typeof(ToolExecutionPermissionValidator) };
    // Operator-reviewed PASS from the gated generated-fixture permission harness on
    // aura_disposable_test, OID 24576, system 7696813047573034172.
    private const string PermissionEvidenceDatabase = "aura_disposable_test";
    private const uint PermissionEvidenceOid = 24576;
    private const long PermissionEvidenceSystem = 7696813047573034172;
    private const string PermissionEvidenceResult = "PASS";
    private static readonly IReadOnlySet<string> ReviewedPermissionLiveEvidence =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "DB CONNECTIVITY", "SCHEMA CHECK", "FIXTURE SETUP", "ACTIVE GRANT ALLOW",
            "OWNED USER GRANT ALLOW", "EXACT PROJECT GRANT ALLOW", "EXACT TOOL GRANT ALLOW",
            "EXACT PERMISSION ALLOW", "FOREIGN USER GRANT DENY", "WRONG PROJECT DENY",
            "WRONG CONVERSATION DENY", "WRONG TOOL DENY", "WRONG PERMISSION DENY",
            "UNKNOWN GRANT DENY", "REVOKED GRANT DENY",
            "REVOCATION AFTER PRIOR ALLOW DENY", "NEGATIVE BINDINGS", "CLEANUP", "RESULT"
        };
    private static readonly IReadOnlySet<Type> ReviewedExecutionState = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedAuditSink = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedAuditDelivery = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedReconciliation = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedVerification =
        new HashSet<Type> { typeof(OwnedProjectMetadataExecutionEvidenceSource) };
    private static readonly IReadOnlySet<Type> ReviewedVerificationWriter =
        new HashSet<Type> { typeof(ExecutionVerificationAssessmentPersistenceWriter) };
    private static readonly IReadOnlySet<Type> ReviewedVerificationWorkflow = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedVerificationFactory =
        new HashSet<Type> { typeof(AuraDbContextFactory) };
    private static readonly IReadOnlySet<Type> ReviewedVerificationPolicy = new HashSet<Type>();
    private static readonly IReadOnlySet<Type> ReviewedExecutionPolicy = new HashSet<Type>();
    private static readonly IReadOnlyList<(Type HandlerType, string ToolId, ToolCapability Capability,
        ToolPermissionRequirement Permission)> ReviewedToolHandlers =
        Array.Empty<(Type, string, ToolCapability, ToolPermissionRequirement)>();
    private static readonly IReadOnlySet<Type> ReviewedTrustedExecution = new HashSet<Type>();

    public static ProductionRuntimeCompositionSnapshot Inspect(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var ownership = HasRequiredOwnershipLiveEvidence() &&
            HasExactAuthenticationService(services) &&
            HasJwtBearerPath(services) &&
            HasOneSingleton<IHttpContextAccessor>(services, typeof(HttpContextAccessor)) &&
            HasExactDbContextAlias(services) &&
            HasOneScoped<AuraDbContext>(services) &&
            HasExactOwnershipFactory(services) &&
            HasOne<IAuthenticatedUserMappingSource>(services, ReviewedSubjectMapping) &&
            HasOne<ICurrentUserContext>(services, ReviewedOwnershipContext) &&
            HasOne<IOwnershipSource>(services, ReviewedOwnershipSource) &&
            HasOne<ExactOwnershipScopeValidator>(services, ReviewedOwnershipValidator) &&
            HasExactValidatorAlias<IExactActionOwnershipValidator>(services) &&
            HasExactValidatorAlias<IActionScopeValidator>(services) &&
            HasExactValidatorAlias<IActionApprovalScopeValidator>(services) &&
            HasExactValidatorAlias<IToolExecutionScopeValidator>(services) &&
            HasExactValidatorAlias<IVerificationScopeValidator>(services) &&
            HasExactValidatorAlias<IExecutionReconciliationScopeValidator>(services);
        var permission = ownership && HasRequiredPermissionLiveEvidence() &&
            HasExactPermissionSource(services) &&
            HasOne<IToolPermissionPolicy>(services, ReviewedPermissionPolicy) &&
            HasOne<IToolExecutionPermissionValidator>(services, ReviewedPermissionValidator) &&
            HasExactDbContextAlias(services) && HasOneScoped<AuraDbContext>(services);
        var executionState = HasExactExecutionStateStore(services);
        var auditSink = HasExactAuditSink(services);
        var auditDelivery = HasExactAuditDeliveryDispatcher(services);
        var reconciliation = HasExactReconciliationSource(services);
        var verification = HasExactVerificationSource(services) &&
            HasExactVerificationWriter(services) &&
            HasExactVerificationWorkflow(services) &&
            HasExactVerificationFactory(services) &&
            HasExactVerificationPolicy(services) &&
            HasOneScoped<IVerificationScopeValidator>(services) &&
            HasOneScoped<ToolRegistry>(services);
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

    private static bool HasOneSingleton<T>(IServiceCollection services, Type implementation)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(T)).ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Singleton &&
            registrations[0].ImplementationType == implementation;
    }

    private static bool HasExactDbContextAlias(IServiceCollection services)
    {
        var registrations = services.Where(item =>
            item.ServiceType == typeof(IAuraDbContext)).ToArray();
        return registrations.Length == 1 &&
            OwnershipRuntimeFoundationRegistration.IsExactDbContextAlias(registrations[0]);
    }

    private static bool HasExactAuthenticationService(IServiceCollection services)
    {
        var registrations = services.Where(item =>
            item.ServiceType == typeof(IAuthenticationService)).ToArray();
        // ASP.NET Core registers an internal concrete type for this contract.
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType is { } implementation &&
            implementation.FullName ==
                "Microsoft.AspNetCore.Authentication.AuthenticationServiceImpl" &&
            implementation.Assembly == typeof(AuthenticationService).Assembly;
    }

    private static bool HasJwtBearerPath(IServiceCollection services)
    {
        var handlers = services.Where(item =>
            item.ServiceType == typeof(JwtBearerHandler)).ToArray();
        var configurations = services.Where(item =>
            item.ServiceType == typeof(IConfigureOptions<JwtBearerOptions>) &&
            item.ImplementationType?.FullName ==
                "Microsoft.AspNetCore.Authentication.JwtBearerConfigureOptions").ToArray();
        return handlers.Length == 1 &&
            handlers[0].Lifetime == ServiceLifetime.Transient &&
            handlers[0].ImplementationType == typeof(JwtBearerHandler) &&
            configurations.Length == 1 &&
            configurations[0].Lifetime == ServiceLifetime.Singleton;
    }

    private static bool HasExactValidatorAlias<T>(IServiceCollection services)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(T)).ToArray();
        return registrations.Length == 1 &&
            OwnershipRuntimeFoundationRegistration.IsExactValidatorAlias(registrations[0]);
    }

    private static bool HasExactOwnershipFactory(IServiceCollection services)
    {
        var registrations = services.Where(item =>
            item.ServiceType == typeof(IDbContextFactory<AuraDbContext>)).ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Singleton &&
            registrations[0].ImplementationType is { } implementation &&
            ReviewedOwnershipFactory.Contains(implementation);
    }

    private static bool HasRequiredOwnershipLiveEvidence()
    {
        var required = new[] { "DB CONNECTIVITY", "SCHEMA CHECK", "FIXTURE SETUP",
            "SIGNED JWT ACCEPT", "JWT SUBJECT MAP", "OWNED PROJECT ALLOW",
            "FOREIGN PROJECT DENY", "UNKNOWN PROJECT DENY", "OWNED CONVERSATION ALLOW",
            "FOREIGN CONVERSATION DENY", "PROJECT CONVERSATION MISMATCH DENY",
            "OWNED ACTION ALLOW", "FOREIGN ACTION DENY", "ACTION CONVERSATION MISMATCH DENY",
            "ACTION PROJECT MISMATCH DENY", "ACTION USER CHAIN MISMATCH DENY",
            "UNAUTHENTICATED DENY", "MALFORMED TOKEN DENY", "UNKNOWN SUBJECT DENY",
            "NEGATIVE BINDINGS", "CLEANUP", "RESULT" };
        return OwnershipEvidenceDatabase == "aura_disposable_test" &&
            OwnershipEvidenceOid == 24576 && OwnershipEvidenceSystem == 7696813047573034172 &&
            OwnershipEvidenceResult == "PASS" &&
            ReviewedOwnershipLiveEvidence.Count == required.Length &&
            required.All(ReviewedOwnershipLiveEvidence.Contains);
    }

    // A generic Permission adapter, unavailable source, factory or registration
    // alone cannot attest an exact tool grant.
    private static bool HasExactPermissionSource(IServiceCollection services)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(IToolPermissionSource))
            .ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType == typeof(ToolPermissionPersistenceSource) &&
            ReviewedPermission.Contains(typeof(ToolPermissionPersistenceSource));
    }

    private static bool HasRequiredPermissionLiveEvidence()
    {
        var required = new[] { "DB CONNECTIVITY", "SCHEMA CHECK", "FIXTURE SETUP",
            "ACTIVE GRANT ALLOW", "OWNED USER GRANT ALLOW", "EXACT PROJECT GRANT ALLOW",
            "EXACT TOOL GRANT ALLOW", "EXACT PERMISSION ALLOW", "FOREIGN USER GRANT DENY",
            "WRONG PROJECT DENY", "WRONG CONVERSATION DENY", "WRONG TOOL DENY",
            "WRONG PERMISSION DENY", "UNKNOWN GRANT DENY", "REVOKED GRANT DENY",
            "REVOCATION AFTER PRIOR ALLOW DENY", "NEGATIVE BINDINGS", "CLEANUP", "RESULT" };
        return PermissionEvidenceDatabase == "aura_disposable_test" &&
            PermissionEvidenceOid == 24576 &&
            PermissionEvidenceSystem == 7696813047573034172 &&
            PermissionEvidenceResult == "PASS" &&
            ReviewedPermissionLiveEvidence.Count == required.Length &&
            required.All(ReviewedPermissionLiveEvidence.Contains);
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

    private static bool HasExactVerificationPolicy(IServiceCollection services)
    {
        var registrations = services.Where(item =>
            item.ServiceType == typeof(IIndependentVerificationPolicy)).ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType is { } implementation &&
            implementation != typeof(ConservativeVerificationPolicy) &&
            ReviewedVerificationPolicy.Contains(implementation);
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
