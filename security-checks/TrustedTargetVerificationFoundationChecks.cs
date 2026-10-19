using Aura.Api.Security;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

internal static class TrustedTargetVerificationFoundationChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var target = Read("Aura.Application", "Tools", "OwnedProjectTarget.cs");
        var request = Read("Aura.Application", "Tools", "ToolContracts.cs");
        var trusted = Read("Aura.Application", "Executions", "TrustedToolExecutionService.cs");
        var writer = Read("Aura.Infrastructure", "Services", "ExecutionVerificationAssessmentPersistenceWriter.cs");
        var api = Read("Aura.Api", "Program.cs");
        var review = Read("STEP-17N-TRUSTED-TARGET-VERIFICATION-FOUNDATION.md");

        check(typeof(OwnedProjectTarget).IsSealed &&
            typeof(OwnedProjectTarget).GetConstructors().Length == 0 &&
            typeof(OwnedProjectTarget).GetProperties().Select(x => x.Name).SequenceEqual(["ProjectId"]) &&
            typeof(OwnedProjectTarget).GetProperties().All(x => x.SetMethod is null) &&
            target.Contains("action.Scope.ProjectId == Guid.Empty") &&
            !target.Contains("Dictionary<") && !target.Contains("Json"),
            "17N target is closed, immutable, nonempty project identity only");
        check(request.Contains("internal static ToolInvocationRequest? FromReserved") &&
            request.Contains("private ToolInvocationRequest(ToolIdentifier toolId, OwnedProjectTarget target)") &&
            trusted.Contains("ToolInvocationRequest.FromReserved(action.ToolId, action,") &&
            trusted.Contains("invocation.Target?.ProjectId != scope.ProjectId") &&
            trusted.IndexOf("states.TryReserveInitialAsync", StringComparison.Ordinal) <
                trusted.IndexOf("ToolInvocationRequest.FromReserved", StringComparison.Ordinal) &&
            trusted.IndexOf("ToolInvocationRequest.FromReserved", StringComparison.Ordinal) <
                trusted.IndexOf("handler.ExecuteAsync(invocation", StringComparison.Ordinal),
            "17N trusted request derives target after exact reservation, never from caller input");
        check(target.Contains("ApprovalValidation.IsBoundToApprovedAction") &&
            target.Contains("reserved.ActionId != action.Id") &&
            target.Contains("reserved.Scope != action.Scope") &&
            target.Contains("reserved.ToolId != action.ToolId") &&
            target.Contains("reserved.ApprovalId != approval.Id.Value") &&
            target.Contains("reserved.Status != ActionExecutionStateStatus.Reserved") &&
            target.Contains("reserved.Version != 1") &&
            target.Contains("reserved.ExecutionId is not Guid executionId"),
            "17N wrong action, approval, project, user, conversation, tool and version fail binding");
        check(Enum.GetValues<VerificationProofSource>().SequenceEqual([
                VerificationProofSource.None, VerificationProofSource.OwnedProjectMetadata]) &&
            writer.Contains("executionVersion != 2") &&
            writer.Contains("evidence.IndependentProof is not { } proof") &&
            writer.Contains("ProofSource = verified ? VerificationProofSource.OwnedProjectMetadata : VerificationProofSource.None") &&
            writer.Contains("ProofVersion = verified ? 1 : 0") &&
            review.Contains("no independent proof adapter"),
            "17N bounded proof designation cannot promote handler success to Verified");
        check(writer.Contains("IDbContextFactory<AuraDbContext> factory") &&
            writer.Contains("CreateDbContextAsync(cancellationToken)") &&
            writer.Contains("AsNoTracking()") &&
            writer.Contains("x.Version == executionVersion") &&
            writer.Contains("x.ExecutionId == evidence.ExecutionId") &&
            writer.Contains("SaveChangesAsync(cancellationToken)") &&
            !writer.Contains("ExecuteUpdate") && !writer.Contains("ActionExecutionStates.Add") &&
            !writer.Contains("MigrateAsync") && !writer.Contains("EnsureCreated") &&
            !writer.Contains("while (") && !writer.Contains("for (") &&
            writer.Contains("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)"),
            "17N unregistered writer binds terminal version and cannot mutate state or retry");
        var snapshot = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        var flags = new RuntimeSafetyOptions();
        check(!api.Contains("AddScoped<IToolHandler,") &&
            !api.Contains("AddScoped<IToolExecutionPolicy,") &&
            !api.Contains("AddScoped<ITrustedToolExecutionService,") &&
            !api.Contains("AddScoped<IExecutionEvidenceSource,") &&
            !api.Contains("AddScoped<IExecutionVerificationAssessmentWriter,") &&
            !snapshot.Verification && !snapshot.ToolHandlers && !snapshot.ExecutionPolicy &&
            !snapshot.TrustedExecution && !snapshot.CompositionComplete &&
            !flags.ProductionAdaptersEnabled && !flags.ToolExecutionEnabled &&
            !flags.AuditPersistenceEnabled && !flags.SafeFileAccessEnabled && !flags.CloudAiEnabled &&
            !new UnavailableFinalProductionSecurityApprovalSource().IsApproved,
            "17N no production adapter, handler, policy, trusted service or activation");
        return Task.CompletedTask;
    }
}
