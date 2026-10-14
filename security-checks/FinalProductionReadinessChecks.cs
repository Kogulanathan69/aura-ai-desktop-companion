using Aura.Api.Security;
using Aura.Application.AI.Providers;
using Aura.Application.Auditing;
using Aura.Application.Executions;
using Aura.Application.ProjectFiles.Content;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Application.Verifications;
using Aura.Infrastructure.AI.OpenAI;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;

// Review-only decision. It is never registered in the API and cannot enable a workflow.
internal static class FinalProductionReadinessChecks
{
    private sealed record Evidence(
        bool LiveJwt, bool LivePostgresOwnership, bool PermissionPersistence,
        bool AtomicExecutionState, bool DurableAudit, bool TrustedReconciliation,
        bool IndependentVerification, bool ReviewedHandlers, bool ReviewedExecutionPolicy,
        bool ProductionRegistration, bool FinalSecurityReview);

    private static bool CanEnable(RuntimeSafetyOptions options, RuntimeCapabilitySnapshot readiness,
        Evidence evidence) =>
        options.ProductionAdaptersEnabled && options.ToolExecutionEnabled &&
        readiness.Ownership && readiness.Permission && readiness.ExecutionState &&
        readiness.AuditSink && readiness.Reconciliation && readiness.Verification &&
        readiness.Handler && readiness.ExecutionPolicy &&
        evidence.LiveJwt && evidence.LivePostgresOwnership && evidence.PermissionPersistence &&
        evidence.AtomicExecutionState && evidence.DurableAudit && evidence.TrustedReconciliation &&
        evidence.IndependentVerification && evidence.ReviewedHandlers &&
        evidence.ReviewedExecutionPolicy && evidence.ProductionRegistration &&
        evidence.FinalSecurityReview;

    public static Task RunAsync(Action<bool, string> check)
    {
        var startup = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var informational = new PersistenceCapabilityReadiness().GetSnapshot();
        var defaults = new RuntimeSafetyOptions();
        var absent = new Evidence(false, false, false, false, false, false, false,
            false, false, false, false);
        var validator = new RuntimeSafetyValidator();
        var safety = validator.Validate(defaults, startup, new ProjectFileAccessOptions(),
            new AiProviderRouterOptions(), new OpenAiOptions());
        check(!startup.Ownership && !startup.Permission && !startup.ExecutionState &&
            !startup.AuditSink && !startup.Reconciliation && !startup.Verification &&
            !startup.Handler && !startup.ExecutionPolicy && !startup.SafeFileAccess,
            "12L startup capability snapshot is all false");
        check(!defaults.ProductionAdaptersEnabled, "12L production adapter flag defaults false");
        check(!defaults.ToolExecutionEnabled, "12L tool execution flag defaults false");
        check(!defaults.AuditPersistenceEnabled, "12L audit persistence flag defaults false");
        check(!defaults.SafeFileAccessEnabled, "12L Safe File Access flag defaults false");
        check(!defaults.CloudAiEnabled, "12L cloud AI flag defaults false");
        check(safety == RuntimeSafetyStatus.SafeDisabled && !CanEnable(defaults, startup, absent),
            "12L final production execution decision is NOT READY");
        foreach (var (ready, label) in new[] {
            (startup.Permission, "permission"), (startup.ExecutionState, "execution state"),
            (startup.AuditSink, "audit sink"), (startup.Reconciliation, "reconciliation"),
            (startup.Verification, "verification"), (startup.Ownership, "ownership"),
            (startup.Handler, "handler"), (startup.ExecutionPolicy, "execution policy") })
            check(!ready, "12L production " + label + " readiness false");
        check(informational.Ownership && !startup.Ownership && !CanEnable(defaults, informational, absent),
            "12L informational ownership implementation cannot enable production");
        check(!CanEnable(defaults, startup with { Ownership = true }, absent),
            "12L ownership helper or identity mapping alone cannot enable production");
        check(!CanEnable(defaults, startup with { Permission = true }, absent),
            "12L approval or permission alone cannot enable production");
        check(!CanEnable(defaults, startup with { AuditSink = true }, absent) &&
            !CanEnable(defaults, startup with { Verification = true }, absent),
            "12L audit or verification alone cannot enable production");
        check(!CanEnable(new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true }, startup, absent),
            "12L configuration flags cannot replace independent readiness");
        var allCapabilities = new RuntimeCapabilitySnapshot(true, true, true, true, true, true,
            true, true, true);
        check(!CanEnable(defaults, allCapabilities, absent),
            "12L readiness assertions cannot replace explicit enablement and live evidence");
        check(!CanEnable(new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true }, allCapabilities, absent),
            "12L asserted capabilities and flags cannot replace reviewed live evidence");
        check(!absent.LiveJwt && !absent.LivePostgresOwnership,
            "12L live JWT and PostgreSQL production authorization evidence incomplete");
        check(!absent.PermissionPersistence && !absent.AtomicExecutionState && !absent.DurableAudit,
            "12L permission, atomic state, and audit persistence evidence absent");
        check(!absent.TrustedReconciliation && !absent.IndependentVerification,
            "12L reconciliation and independent verification evidence absent");
        check(!absent.ReviewedHandlers && !absent.ReviewedExecutionPolicy &&
            !absent.ProductionRegistration && !absent.FinalSecurityReview,
            "12L handler, policy, registration, and final review evidence absent");

        var productionTypes = typeof(ToolRegistry).Assembly.GetTypes()
            .Concat(typeof(AuraDbContext).Assembly.GetTypes())
            .Concat(typeof(RuntimeSafetyOptions).Assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract).ToArray();
        check(!productionTypes.Any(type => typeof(IToolHandler).IsAssignableFrom(type)),
            "12L no production tool handler implementation exists");
        check(productionTypes.Where(type => typeof(IToolExecutionPolicy).IsAssignableFrom(type))
                .SequenceEqual([typeof(DenyToolExecutionPolicy)]),
            "12L only default-deny execution policy exists");
        check(productionTypes.Where(type => typeof(IToolPermissionSource).IsAssignableFrom(type))
                .ToHashSet().SetEquals([typeof(UnavailableToolPermissionSource),
                    typeof(ToolPermissionPersistenceSource)]),
            "12L read-only permission source foundation does not make production runtime ready");
        check(productionTypes.Where(type => typeof(IActionExecutionStateStore).IsAssignableFrom(type))
                .ToHashSet().SetEquals([typeof(UnavailableActionExecutionStateStore),
                    typeof(ActionExecutionStatePersistenceStore)]),
            "13C unregistered execution-state foundation does not make production runtime ready");
        check(productionTypes.Where(type => typeof(IAuditEventSink).IsAssignableFrom(type))
                .SequenceEqual([typeof(UnavailableAuditEventSink)]),
            "12L no production audit sink exists");
        check(productionTypes.Where(type => typeof(IExecutionReconciliationSource).IsAssignableFrom(type))
                .SequenceEqual([typeof(UnavailableExecutionReconciliationSource)]),
            "12L no production reconciliation source exists");
        check(productionTypes.Where(type => typeof(IExecutionEvidenceSource).IsAssignableFrom(type))
                .SequenceEqual([typeof(UnavailableExecutionEvidenceSource)]),
            "12L no production verification evidence source exists");
        check(productionTypes.Where(type => typeof(IIndependentVerificationPolicy).IsAssignableFrom(type))
                .SequenceEqual([typeof(ConservativeVerificationPolicy)]),
            "12L no independent production proof policy exists");

        var root = Directory.GetCurrentDirectory();
        var program = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        check(program.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal) &&
            !program.Contains("new PersistenceCapabilityReadiness()", StringComparison.Ordinal),
            "12L normal startup uses unavailable readiness, not informational snapshot");
        check(!program.Contains(".AddOwnershipRuntimeFoundation(", StringComparison.Ordinal),
            "12L ownership helper is not invoked by normal startup");
        foreach (var contract in new[] { "IToolPermissionSource", "IActionExecutionStateStore",
            "IAuditEventSink", "IExecutionReconciliationSource", "IExecutionEvidenceSource",
            "ITrustedToolExecutionService" })
            check(!program.Contains("AddScoped<" + contract, StringComparison.Ordinal) &&
                !program.Contains("AddSingleton<" + contract, StringComparison.Ordinal) &&
                !program.Contains("AddTransient<" + contract, StringComparison.Ordinal),
                "12L no production registration for " + contract);
        foreach (var flag in new[] { "ProductionAdaptersEnabled", "ToolExecutionEnabled",
            "AuditPersistenceEnabled", "SafeFileAccessEnabled", "CloudAiEnabled" })
            check(!program.Contains(flag + " = true", StringComparison.Ordinal),
                "12L normal startup does not force " + flag);
        check(!program.Contains("handler.ExecuteAsync(", StringComparison.Ordinal) &&
            !program.Contains("TrustedToolExecutionService", StringComparison.Ordinal),
            "12L normal startup has no real tool execution path");
        check(!new ProjectFileAccessOptions().Enabled && !startup.SafeFileAccess,
            "12L Safe File Access remains disabled without live Windows ACL/race evidence");
        check(!new OpenAiOptions().Enabled && new AiProviderRouterOptions().Mode == AiProviderMode.Local,
            "12L cloud provider remains disabled without live evidence");
        check(RuntimeSafetyValidator.FailureMessage == "Runtime safety configuration is invalid.",
            "12L fixed bounded runtime safety failure remains");
        return Task.CompletedTask;
    }
}
