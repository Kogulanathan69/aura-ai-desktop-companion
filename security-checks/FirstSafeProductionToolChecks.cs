using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Application.Verifications;
using Microsoft.Extensions.DependencyInjection;

internal static class FirstSafeProductionToolChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));
        var api = Read("Aura.Api", "Program.cs");
        var tools = Read("Aura.Application", "Tools", "ToolContracts.cs");
        var executor = Read("Aura.Application", "Executions", "TrustedToolExecutionService.cs");
        var verification = Read("Aura.Application", "Verifications", "VerificationContracts.cs");
        var schema = Read("Aura.Infrastructure", "Data", "Configurations",
            "ExecutionVerificationAssessmentRecordConfiguration.cs");
        var safeFile = Read("Aura.Application", "ProjectFiles", "Content", "SafeProjectFileContentService.cs");
        var review = Read("STEP-17M-FIRST-SAFE-PRODUCTION-TOOL-VERIFICATION-POLICY.md");
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");

        check(tools.Contains("record ToolInvocationRequest(ToolIdentifier ToolId)") &&
            executor.Contains("new ToolInvocationRequest(action.ToolId)") &&
            review.Contains("approved target is not carried") &&
            !tools.Contains("Dictionary<string, object>") &&
            !tools.Contains("JsonElement"),
            "17M current handler request has no approved target or arbitrary payload");
        check(safeFile.Contains("if (!options.Enabled)") &&
            review.Contains("SafeFileAccessEnabled` remains false") &&
            !new RuntimeSafetyOptions().SafeFileAccessEnabled,
            "17M gated file access is not used as a tool bypass");
        check(verification.Contains("class UnavailableExecutionEvidenceSource") &&
            verification.Contains("VerificationOutcome.Failed : VerificationOutcome.Inconclusive") &&
            !verification.Contains("public enum VerificationProofSource") &&
            schema.Contains("ProofSource") && schema.Contains("ProofVersion") &&
            schema.Contains("Outcome") && schema.Contains("IN (1, 2)") &&
            review.Contains("Verified is forbidden by the current database check"),
            "17M handler success cannot become independent Verified proof");

        ToolIdentifier.TryCreate("owned-project-metadata-read", out var proposedId);
        ToolIdentifier.TryCreate("unknown-project-tool", out var unknownId);
        check(proposedId is not null && proposedId.Value == "owned-project-metadata-read" &&
            unknownId is not null &&
            new ToolRegistry(Array.Empty<ToolRegistration>()).TryResolve(unknownId, out _, out _) == false,
            "17M proposed ID is canonical and unknown tool has no registry resolution");
        var descriptor = new ToolDescriptor(proposedId!, "Owned project metadata read",
            "Read bounded owned-project metadata.", ToolCapability.ProjectRead,
            ToolPermissionRequirement.OwnedProjectRead);
        var fake = new TestHandler();
        var duplicateDenied = false;
        try { _ = new ToolRegistry([new(descriptor, fake), new(descriptor, fake)]); }
        catch (ArgumentException) { duplicateDenied = true; }
        check(duplicateDenied && descriptor.RequiresApproval && descriptor.RequiresOwnedScope &&
            !descriptor.Enabled && review.Contains("OwnedProjectRead"),
            "17M duplicate candidate ID denied and descriptor metadata grants nothing");

        var conservative = new ConservativeVerificationPolicy();
        var reportedSuccess = new ExecutionEvidenceInput(Guid.NewGuid(),
            null!, // Conservative policy must not promote even a reported success.
            new Aura.Application.Actions.ActionScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            proposedId!, Guid.NewGuid(), ExecutionReportOutcome.Succeeded, DateTime.UtcNow);
        check(await conservative.EvaluateAsync(reportedSuccess, CancellationToken.None) ==
            VerificationOutcome.Inconclusive,
            "17M success report alone remains Inconclusive");
        check(!await new DenyToolExecutionPolicy().AllowsAsync(descriptor, CancellationToken.None) &&
            review.Contains("No allowing production policy was implemented"),
            "17M descriptor, approval, ownership and permission cannot bypass deny policy");

        check(!api.Contains("AddScoped<IToolHandler,") &&
            !api.Contains("AddScoped<IExecutionEvidenceSource,") &&
            !api.Contains("AddScoped<IToolExecutionPolicy,") &&
            !api.Contains("AddScoped<ITrustedToolExecutionService,") &&
            !api.Contains("AddHostedService") &&
            inspector.Contains("ReviewedToolHandlers =") &&
            inspector.Contains("ReviewedVerification = new HashSet<Type>()") &&
            inspector.Contains("ReviewedExecutionPolicy = new HashSet<Type>()"),
            "17M no candidate handler, proof source or allowing policy enters production DI/catalog");
        var snapshot = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        var flags = new RuntimeSafetyOptions();
        check(!snapshot.ToolHandlers && !snapshot.Verification && !snapshot.ExecutionPolicy &&
            !snapshot.TrustedExecution && !snapshot.CompositionComplete &&
            !flags.ProductionAdaptersEnabled && !flags.ToolExecutionEnabled &&
            !flags.AuditPersistenceEnabled && !flags.SafeFileAccessEnabled &&
            !flags.CloudAiEnabled &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()") &&
            api.Contains("new UnavailableFinalProductionSecurityApprovalSource()") &&
            !new UnavailableFinalProductionSecurityApprovalSource().IsApproved,
            "17M runtime flags and final activation remain denied");
    }

    private sealed class TestHandler : IToolHandler
    {
        public Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(ToolExecutionResult.FromStatus(ToolExecutionStatus.Disabled));
    }
}
