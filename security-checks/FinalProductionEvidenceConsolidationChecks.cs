using System.Reflection;
using Aura.Api.Security;
using Aura.Application.AI.Providers;
using Aura.Application.ProjectFiles.Content;
using Aura.Application.Security;
using Aura.Infrastructure.AI.OpenAI;
using Microsoft.Extensions.DependencyInjection;

// Review-only Boolean model. It is never registered in Aura.Api.
internal static class FinalProductionEvidenceConsolidationChecks
{
    private static bool CanActivate(ProductionRuntimeCompositionSnapshot composition,
        RuntimeSafetyOptions flags, RuntimeCapabilitySnapshot readiness,
        bool finalApproval, bool applicableEvidenceComplete,
        bool fileReady = false, bool cloudReady = false)
    {
        var file = new ProjectFileAccessOptions { Enabled = flags.SafeFileAccessEnabled };
        var router = new AiProviderRouterOptions();
        var cloud = new OpenAiOptions();
        // Optional capabilities remain independent. Synthetic all-true formula
        // below keeps them disabled; no provider or file configuration is faked.
        return composition.CompositionComplete && applicableEvidenceComplete &&
            flags.ProductionAdaptersEnabled && flags.ToolExecutionEnabled &&
            flags.AuditPersistenceEnabled &&
            (!flags.SafeFileAccessEnabled || fileReady && readiness.SafeFileAccess) &&
            (!flags.CloudAiEnabled || cloudReady) &&
            readiness.Ownership && readiness.Permission && readiness.ExecutionState &&
            readiness.AuditSink && readiness.Reconciliation && readiness.Verification &&
            readiness.Handler && readiness.ExecutionPolicy && finalApproval &&
            new RuntimeSafetyValidator().Validate(flags, readiness, file, router, cloud) ==
                RuntimeSafetyStatus.Valid;
    }

    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Source(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var docs = Directory.GetFiles(root, "STEP-15*.md")
            .Select(Path.GetFileName).Where(n => n is not null).ToArray();
        for (var letter = 'A'; letter <= 'J'; letter++)
            check(docs.Count(n => n!.StartsWith($"STEP-15{letter}-", StringComparison.Ordinal)) == 1,
                "15K Step 15" + letter + " review exists exactly once");
        var entry = Source("security-checks", "Program.cs");
        var api = Source("Aura.Api", "Program.cs");
        var inspector = Source("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var safety = Source("Aura.Api", "Security", "RuntimeSafetyOptions.cs");
        var runtime = Source("Aura.Application", "Security", "RuntimeCapabilityReadiness.cs");
        var finalGate = Source("security-checks", "FinalProductionCompositionGateChecks.cs");
        check(entry.Contains("await FinalProductionEvidenceConsolidationChecks.RunAsync(Check)") &&
            new[] { "ProductionOwnershipEvidenceChecks", "ProductionMigrationBaselineEvidenceChecks",
                "ProductionPermissionEvidenceChecks", "ProductionExecutionStateEvidenceChecks",
                "ProductionAuditEvidenceChecks", "ProductionReconciliationEvidenceChecks",
                "ProductionVerificationEvidenceChecks", "ProductionExecutionPolicyEvidenceChecks",
                "ProductionToolHandlersEvidenceChecks", "ProductionTrustedExecutionEvidenceChecks" }
                .All(name => entry.Contains($"await {name}.RunAsync(Check)")) &&
            !api.Contains("LiveProduction", StringComparison.Ordinal) &&
            !api.Contains("EvidenceChecks", StringComparison.Ordinal),
            "15K evidence commands and checks stay outside normal API startup");
        check(Source("STEP-15C-PRODUCTION-PERMISSION-EVIDENCE-REGISTRATION-REVIEW.md")
                .Contains("RESULT: INCOMPLETE") &&
            Source("STEP-15G-VERIFICATION-PRODUCTION-EVIDENCE-REGISTRATION-REVIEW.md")
                .Contains("RESULT: INCOMPLETE") &&
            Source("STEP-15H-PRODUCTION-EXECUTION-POLICY-EVIDENCE-REGISTRATION-REVIEW.md")
                .Contains("No allowing production policy exists") &&
            Source("STEP-15I-PRODUCTION-TOOL-HANDLERS-EVIDENCE-REGISTRATION-REVIEW.md")
                .Contains("no concrete `IToolHandler` implementations", StringComparison.OrdinalIgnoreCase),
            "15K incomplete verification/permission and absent policy/handlers cannot become PASS by consolidation");

        var current = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        var fields = new[] { current.Ownership, current.Permission, current.ExecutionState,
            current.AuditSink, current.AuditDelivery, current.Reconciliation,
            current.Verification, current.ExecutionPolicy, current.ToolHandlers,
            current.TrustedExecution };
        check(fields.All(v => !v) && !current.CompositionComplete,
            "15K all ten current composition fields and CompositionComplete remain false");
        var catalogs = typeof(ProductionRuntimeCompositionInspector).GetFields(
            BindingFlags.Static | BindingFlags.NonPublic)
            .Where(f => f.Name.StartsWith("Reviewed", StringComparison.Ordinal)).ToArray();
        check(catalogs.Length == 13 && catalogs.All(f =>
            f.GetValue(null)?.GetType().GetProperty("Count")?.GetValue(f.GetValue(null)) is 0 ||
            f.GetValue(null) is Array { Length: 0 }),
            "15K all reviewed production catalogs are empty");
        check(inspector.Contains("ReviewedExecutionPolicy = new HashSet<Type>()") &&
            inspector.Contains("ReviewedToolHandlers =") &&
            inspector.Contains("ReviewedTrustedExecution = new HashSet<Type>()") &&
            inspector.Contains("implementation != typeof(DenyToolExecutionPolicy)") &&
            inspector.Contains("reviewed.Count is 0 or > ToolLimits.MaximumTools") &&
            inspector.Contains("HasExactTrustedExecutionService(services) &&"),
            "15K deny policy, empty handler set and unregistered executor cannot count");
        check(api.Contains("new UnavailableRuntimeCapabilityReadiness()") &&
            api.Contains("ProductionRuntimeCompositionInspector.Inspect(builder.Services)") &&
            !api.Contains("FinalProductionCompositionGateChecks") &&
            !api.Contains("FinalSecurityApproval") &&
            !api.Contains("ProductionActivationGate") &&
            !api.Contains("AddHostedService") && !api.Contains("BackgroundService") &&
            !api.Contains("MigrateAsync(") && !api.Contains("EnsureCreated(") &&
            !api.Contains("handler.ExecuteAsync(") &&
            !api.Contains("ToolExecutionEnabled = true") &&
            !api.Contains("ProductionAdaptersEnabled = true"),
            "15K startup has observational composition only, no approval/activation/worker/migration/direct handler path");
        check(finalGate.Contains("Review-only formula") &&
            finalGate.Contains("finalSecurityApproval") &&
            !api.Contains("FinalProductionCompositionGateChecks") &&
            runtime.Contains("new(false, false, false, false, false,") &&
            safety.Contains("RuntimeSafetyStatus.MissingRequiredAdapter"),
            "15K existing final gate is review-only and startup readiness is unavailable");

        var defaults = new RuntimeSafetyOptions();
        var unavailable = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        check(!defaults.ProductionAdaptersEnabled && !defaults.ToolExecutionEnabled &&
            !defaults.AuditPersistenceEnabled && !defaults.SafeFileAccessEnabled &&
            !defaults.CloudAiEnabled &&
            !unavailable.Ownership && !unavailable.Permission && !unavailable.ExecutionState &&
            !unavailable.AuditSink && !unavailable.Reconciliation && !unavailable.Verification &&
            !unavailable.Handler && !unavailable.ExecutionPolicy && !unavailable.SafeFileAccess,
            "15K runtime flags and capability readiness default false");

        var full = new ProductionRuntimeCompositionSnapshot(true, true, true, true, true,
            true, true, true, true, true);
        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        var ready = new RuntimeCapabilitySnapshot(true, true, true, true, true, true,
            true, true, false);
        check(CanActivate(full, flags, ready, true, true),
            "15K synthetic all-true review formula passes without activating runtime");
        foreach (var missing in new[] {
            full with { Ownership = false }, full with { Permission = false },
            full with { ExecutionState = false }, full with { AuditSink = false },
            full with { AuditDelivery = false }, full with { Reconciliation = false },
            full with { Verification = false }, full with { ExecutionPolicy = false },
            full with { ToolHandlers = false }, full with { TrustedExecution = false } })
            check(!CanActivate(missing, flags, ready, true, true),
                "15K each missing composition field blocks activation");
        foreach (var disabled in new[] {
            new RuntimeSafetyOptions { ToolExecutionEnabled = true, AuditPersistenceEnabled = true },
            new RuntimeSafetyOptions { ProductionAdaptersEnabled = true, AuditPersistenceEnabled = true },
            new RuntimeSafetyOptions { ProductionAdaptersEnabled = true, ToolExecutionEnabled = true } })
            check(!CanActivate(full, disabled, ready, true, true),
                "15K each missing required production flag blocks activation");
        foreach (var missing in new[] {
            ready with { Ownership = false }, ready with { Permission = false },
            ready with { ExecutionState = false }, ready with { AuditSink = false },
            ready with { Reconciliation = false }, ready with { Verification = false },
            ready with { Handler = false }, ready with { ExecutionPolicy = false } })
            check(!CanActivate(full, flags, missing, true, true),
                "15K each missing applicable runtime capability blocks activation");
        check(!CanActivate(full, flags, ready, false, true) &&
            !CanActivate(full, flags, ready, true, false) &&
            !CanActivate(current, defaults, unavailable, false, false) &&
            !CanActivate(current, flags, ready, true, true) &&
            !CanActivate(full, defaults, unavailable, true, true),
            "15K evidence, flags, DI/catalog composition, runtime readiness and final approval never substitute for one another");
        check(!CanActivate(full, new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
                ToolExecutionEnabled = true, AuditPersistenceEnabled = true,
                SafeFileAccessEnabled = true }, ready, true, true) &&
            !CanActivate(full, new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
                ToolExecutionEnabled = true, AuditPersistenceEnabled = true,
                CloudAiEnabled = true }, ready, true, true),
            "15K optional file and cloud switches remain independently gated");
        return Task.CompletedTask;
    }
}
