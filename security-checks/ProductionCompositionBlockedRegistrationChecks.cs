using Aura.Api.Security;
using Aura.Application.Actions;
using Aura.Application.Auditing;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

// The Step 16B-E checkpoints deliberately leave registration/catalog readiness
// unavailable until independently reviewable live evidence closes each blocker.
internal static class ProductionCompositionBlockedRegistrationChecks
{
    private static readonly string Root = Directory.GetCurrentDirectory();
    private static string Source(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));
    private static bool Absent(string api, string contract) =>
        !api.Contains("AddScoped<" + contract, StringComparison.Ordinal) &&
        !api.Contains("AddSingleton<" + contract, StringComparison.Ordinal) &&
        !api.Contains("AddTransient<" + contract, StringComparison.Ordinal);

    internal static Task OwnershipAsync(Action<bool, string> check)
    {
        var api = Source("Aura.Api", "Program.cs");
        var inspector = Source("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        check(typeof(ExactOwnershipScopeValidator).IsSealed &&
            typeof(OwnershipPersistenceSource).IsSealed &&
            typeof(AuthenticatedUserMappingSource).IsSealed &&
            api.Contains("AddScoped<IAuthenticatedUserMappingSource, AuthenticatedUserMappingSource>()") &&
            api.Contains(".AddOwnershipRuntimeFoundation(") &&
            Absent(api, "ICurrentUserContext") && Absent(api, "IOwnershipSource") &&
            Absent(api, "IToolExecutionScopeValidator") &&
            inspector.Contains("typeof(HttpCurrentUserContext)") &&
            inspector.Contains("typeof(ExactOwnershipScopeValidator)") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Ownership,
            "16B incomplete ownership graph and empty reviewed catalog cannot be promoted");
        return Task.CompletedTask;
    }

    internal static Task PermissionAsync(Action<bool, string> check)
    {
        var api = Source("Aura.Api", "Program.cs");
        var inspector = Source("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var evidence = Source("STEP-15C-PRODUCTION-PERMISSION-EVIDENCE-REGISTRATION-REVIEW.md");
        var source = Source("Aura.Infrastructure", "Data", "ToolPermissionPersistenceSource.cs");
        var model = Source("Aura.Infrastructure", "Data", "Configurations",
            "ToolPermissionGrantRecordConfiguration.cs");
        check(typeof(ToolPermissionPersistenceSource).IsSealed &&
            Absent(api, "IToolPermissionSource") &&
            inspector.Contains("ReviewedPermission = new HashSet<Type>()") &&
            evidence.Contains("REVOKED GRANT CHECK: NOT PROVEN") &&
            evidence.Contains("RESULT: INCOMPLETE") &&
            source.Contains("grant.Status == ToolPermissionGrantStatus.Active && grant.RevokedAt == null") &&
            source.Contains("grant.UserId == request.Scope.UserId") &&
            source.Contains("grant.ConversationId == request.Scope.ConversationId") &&
            source.Contains("grant.ProjectId == request.Scope.ProjectId") &&
            source.Contains("grant.ToolId == requestedTool.Value") &&
            source.Contains("grant.RequiredPermission == (int)request.Requirement") &&
            source.Contains(".Take(2)") && !source.Contains("FirstOrDefault") &&
            model.Contains("IS NOT NULL") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Permission,
            "16C revoked-grant evidence and reviewed registration remain incomplete");
        return Task.CompletedTask;
    }

    internal static Task ExecutionStateAsync(Action<bool, string> check)
    {
        var api = Source("Aura.Api", "Program.cs");
        var inspector = Source("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var evidence = Source("STEP-15D-DURABLE-EXECUTION-STATE-EVIDENCE-REGISTRATION-REVIEW.md");
        check(typeof(ActionExecutionStatePersistenceStore).IsSealed &&
            Absent(api, "IActionExecutionStateStore") &&
            !api.Contains("AddDbContextFactory<AuraDbContext>") &&
            inspector.Contains("ReviewedExecutionState = new HashSet<Type>()") &&
            evidence.Contains("baseline", StringComparison.OrdinalIgnoreCase) &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).ExecutionState &&
            !api.Contains("MigrateAsync(") && !api.Contains("EnsureCreated(") &&
            Absent(api, "IToolHandler") && Absent(api, "ITrustedToolExecutionService"),
            "16D unreviewed factory/store cannot enable state or any tool side effect");
        return Task.CompletedTask;
    }

    internal static Task AuditAsync(Action<bool, string> check)
    {
        var api = Source("Aura.Api", "Program.cs");
        var inspector = Source("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var delivery = Source("Aura.Infrastructure", "Services", "SecurityAuditOutboxDispatcher.cs");
        var evidence = Source("STEP-15E-AUDIT-PERSISTENCE-RECOVERY-EVIDENCE-REGISTRATION-REVIEW.md");
        var snapshot = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        check(typeof(SecurityAuditPersistenceSink).IsSealed &&
            typeof(SecurityAuditOutboxDispatcher).IsSealed &&
            Absent(api, "IAuditEventSink") && Absent(api, "IAuditOutboxDispatcher") &&
            inspector.Contains("ReviewedAuditSink = new HashSet<Type>()") &&
            inspector.Contains("ReviewedAuditDelivery = new HashSet<Type>()") &&
            evidence.Contains("ambiguous", StringComparison.OrdinalIgnoreCase) &&
            !delivery.Contains("BackgroundService") && !delivery.Contains("PeriodicTimer") &&
            !api.Contains("AddHostedService") && !snapshot.AuditSink && !snapshot.AuditDelivery &&
            !snapshot.ExecutionPolicy && !snapshot.ToolHandlers && !snapshot.TrustedExecution &&
            !snapshot.CompositionComplete && !new RuntimeSafetyOptions().AuditPersistenceEnabled,
            "16E audit registration and replay review remain blocked; no worker or activation");
        return Task.CompletedTask;
    }
}
