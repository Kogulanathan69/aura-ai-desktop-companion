using System.Reflection;
using Aura.Api.Security;
using Aura.Application.AI.Providers;
using Aura.Application.ProjectFiles.Content;
using Aura.Application.Security;
using Aura.Infrastructure.AI.OpenAI;
using Microsoft.Extensions.DependencyInjection;

// Review-only formula. No production activation service or approval signal exists.
internal static class FinalProductionCompositionGateChecks
{
    private static bool CanActivate(ProductionRuntimeCompositionSnapshot composition,
        RuntimeSafetyOptions flags, RuntimeCapabilitySnapshot runtime, bool finalSecurityApproval) =>
        composition.CompositionComplete && flags.ProductionAdaptersEnabled &&
        flags.ToolExecutionEnabled && flags.AuditPersistenceEnabled &&
        runtime.Ownership && runtime.Permission && runtime.ExecutionState &&
        runtime.AuditSink && runtime.Reconciliation && runtime.Verification &&
        runtime.Handler && runtime.ExecutionPolicy && finalSecurityApproval &&
        new RuntimeSafetyValidator().Validate(flags, runtime, new ProjectFileAccessOptions(),
            new AiProviderRouterOptions(), new OpenAiOptions()) == RuntimeSafetyStatus.Valid;

    internal static Task RunAsync(Action<bool, string> check)
    {
        var empty = new ProductionRuntimeCompositionSnapshot(false, false, false, false, false,
            false, false, false, false, false);
        var full = new ProductionRuntimeCompositionSnapshot(true, true, true, true, true,
            true, true, true, true, true);
        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        var runtime = new RuntimeCapabilitySnapshot(true, true, true, true, true, true,
            true, true, false);

        check(!empty.CompositionComplete, "14P all-false composition is incomplete");
        foreach (var (name, lone) in new (string, ProductionRuntimeCompositionSnapshot)[]
        {
            ("ownership", empty with { Ownership = true }),
            ("permission", empty with { Permission = true }),
            ("execution state", empty with { ExecutionState = true }),
            ("audit sink", empty with { AuditSink = true }),
            ("audit delivery", empty with { AuditDelivery = true }),
            ("reconciliation", empty with { Reconciliation = true }),
            ("verification", empty with { Verification = true }),
            ("execution policy", empty with { ExecutionPolicy = true }),
            ("tool handlers", empty with { ToolHandlers = true }),
            ("trusted execution", empty with { TrustedExecution = true })
        })
            check(!lone.CompositionComplete && !CanActivate(lone, flags, runtime, true),
                "14P isolated " + name + " cannot complete composition or activation");

        foreach (var (name, missing) in new (string, ProductionRuntimeCompositionSnapshot)[]
        {
            ("ownership", full with { Ownership = false }),
            ("permission", full with { Permission = false }),
            ("execution state", full with { ExecutionState = false }),
            ("audit sink", full with { AuditSink = false }),
            ("audit delivery", full with { AuditDelivery = false }),
            ("reconciliation", full with { Reconciliation = false }),
            ("verification", full with { Verification = false }),
            ("execution policy", full with { ExecutionPolicy = false }),
            ("tool handlers", full with { ToolHandlers = false }),
            ("trusted execution", full with { TrustedExecution = false })
        })
            check(!missing.CompositionComplete && !CanActivate(missing, flags, runtime, true),
                "14P missing " + name + " blocks composition and activation");
        check(full.CompositionComplete, "14P all ten synthetic fields complete composition only");

        check(!CanActivate(full, new RuntimeSafetyOptions(), runtime, true),
            "14P complete composition cannot bypass disabled production flags");
        check(!CanActivate(full, WithToolExecutionDisabled(), runtime, true),
            "14P ToolExecutionEnabled false blocks activation");
        check(!CanActivate(full, new RuntimeSafetyOptions { ToolExecutionEnabled = true,
                AuditPersistenceEnabled = true }, runtime, true),
            "14P ProductionAdaptersEnabled false blocks activation");
        check(!CanActivate(full, new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
                ToolExecutionEnabled = true }, runtime, true),
            "14P AuditPersistenceEnabled false blocks activation");

        foreach (var (name, missing) in new (string, RuntimeCapabilitySnapshot)[]
        {
            ("ownership", runtime with { Ownership = false }),
            ("permission", runtime with { Permission = false }),
            ("execution state", runtime with { ExecutionState = false }),
            ("audit sink", runtime with { AuditSink = false }),
            ("reconciliation", runtime with { Reconciliation = false }),
            ("verification", runtime with { Verification = false }),
            ("handler", runtime with { Handler = false }),
            ("execution policy", runtime with { ExecutionPolicy = false })
        })
            check(!CanActivate(full, flags, missing, true),
                "14P incomplete runtime " + name + " readiness blocks activation");
        check(!CanActivate(full, flags, runtime, false),
            "14P unavailable final security approval blocks activation");
        check(CanActivate(full, flags, runtime, true),
            "14P synthetic all-true formula is internally complete, not production evidence");

        var current = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        var startupRuntime = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        check(!current.CompositionComplete && !CanActivate(current, flags, runtime, true) &&
            !CanActivate(full, new RuntimeSafetyOptions(), startupRuntime, false),
            "14P flags-only and composition-only scenarios cannot bypass the final gate");
        check(!CanActivate(current, new RuntimeSafetyOptions(), startupRuntime, false),
            "14P current production activation is NOT READY");

        var catalogNames = new[] { "ReviewedOwnershipContext", "ReviewedSubjectMapping",
            "ReviewedOwnershipSource", "ReviewedOwnershipValidator", "ReviewedPermission",
            "ReviewedExecutionState", "ReviewedAuditSink", "ReviewedAuditDelivery",
            "ReviewedReconciliation", "ReviewedVerification", "ReviewedExecutionPolicy",
            "ReviewedToolHandlers", "ReviewedTrustedExecution" };
        foreach (var name in catalogNames)
        {
            var catalog = typeof(ProductionRuntimeCompositionInspector).GetField(name,
                BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            check(catalog is Array { Length: 0 } ||
                catalog?.GetType().GetProperty("Count")?.GetValue(catalog) is 0,
                "14P reviewed production catalog remains intentionally empty: " + name);
        }

        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var api = Read("Aura.Api", "Program.cs");
        var safety = Read("Aura.Api", "Security", "RuntimeSafetyOptions.cs");
        var runtimeSource = Read("Aura.Application", "Security", "RuntimeCapabilityReadiness.cs");
        check(inspector.Contains("Ownership && Permission && ExecutionState &&", StringComparison.Ordinal) &&
            inspector.Contains("AuditSink && AuditDelivery && Reconciliation && Verification &&",
                StringComparison.Ordinal) &&
            inspector.Contains("ExecutionPolicy && ToolHandlers && TrustedExecution", StringComparison.Ordinal) &&
            inspector.Contains("TrustedPrerequisitesReady(ownership, permission, executionState, auditSink,",
                StringComparison.Ordinal),
            "14P formula retains every independent field and nine trusted prerequisites");
        check(api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal) &&
            api.Contains("ProductionRuntimeCompositionInspector.Inspect(builder.Services)",
                StringComparison.Ordinal) &&
            runtimeSource.Contains("false, false, false, false);", StringComparison.Ordinal) &&
            safety.Contains("RuntimeSafetyStatus.MissingRequiredAdapter", StringComparison.Ordinal),
            "14P normal API still uses unavailable runtime readiness and fail-closed safety validation");
        foreach (var contract in new[] { "IToolPermissionSource", "IActionExecutionStateStore",
            "IAuditEventSink", "IAuditOutboxDispatcher", "IExecutionReconciliationSource",
            "IExecutionEvidenceSource", "IToolExecutionPolicy", "IToolHandler",
            "ITrustedToolExecutionService" })
            check(!api.Contains("AddScoped<" + contract, StringComparison.Ordinal) &&
                !api.Contains("AddSingleton<" + contract, StringComparison.Ordinal) &&
                !api.Contains("AddTransient<" + contract, StringComparison.Ordinal),
                "14P normal API has no production registration for " + contract);
        check(!api.Contains("AddHostedService", StringComparison.Ordinal) &&
            !api.Contains("BackgroundService", StringComparison.Ordinal) &&
            !api.Contains("PeriodicTimer", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !api.Contains("Database.Migrate(", StringComparison.Ordinal) &&
            !api.Contains("handler.ExecuteAsync(", StringComparison.Ordinal) &&
            !api.Contains("ProductionActivationGate", StringComparison.Ordinal),
            "14P API has no worker, migration, direct handler or activation path");
        var defaults = new RuntimeSafetyOptions();
        check(!defaults.ProductionAdaptersEnabled && !defaults.ToolExecutionEnabled &&
            !defaults.AuditPersistenceEnabled && !defaults.SafeFileAccessEnabled &&
            !defaults.CloudAiEnabled && !new ProjectFileAccessOptions().Enabled &&
            !new OpenAiOptions().Enabled,
            "14P production, audit, Safe File Access and Cloud AI defaults remain false");
        var step14 = Enumerable.Range('E', 'O' - 'E' + 1)
            .Select(letter => Directory.GetFiles(root, "STEP-14" + (char)letter + "-*.md").Single())
            .ToArray();
        check(step14.Length == 11 && step14.All(File.Exists),
            "14P Step 14E through 14O per-capability review documents exist");
        check(Read("STEP-13L-SECURITY.md").Contains("PRODUCTION TOOL EXECUTION: NOT READY",
                StringComparison.Ordinal) &&
            Read("STEP-14O-PRODUCTION-TRUSTED-EXECUTION-COMPOSITION-READINESS.md")
                .Contains("CompositionComplete: false", StringComparison.Ordinal),
            "14P prior final security review and per-capability composition remain NOT READY");
        return Task.CompletedTask;

        static RuntimeSafetyOptions WithToolExecutionDisabled() => new()
        {
            ProductionAdaptersEnabled = true, AuditPersistenceEnabled = true
        };
    }
}
