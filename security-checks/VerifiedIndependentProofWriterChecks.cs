using Aura.Application.Actions;
using Aura.Application.Tools;
using Aura.Application.Verifications;
using Aura.Api.Security;
using Aura.Domain.Entities;
using System.Reflection;

internal static class VerifiedIndependentProofWriterChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var proofType = typeof(OwnedProjectMetadataProof);
        check(proofType.IsSealed && proofType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0 &&
            proofType.GetProperties().All(p => p.SetMethod is null), "independent proof is sealed and caller immutable");
        check(OwnedProjectMetadataProof.FromAuthoritativeRead(Guid.Empty, DateTime.UtcNow, 2) is null &&
            OwnedProjectMetadataProof.FromAuthoritativeRead(Guid.NewGuid(), DateTime.UtcNow, 1) is null &&
            OwnedProjectMetadataProof.FromAuthoritativeRead(Guid.NewGuid(), DateTime.Now, 2) is null,
            "independent proof rejects invalid project, version, and clock");
        var user = Guid.NewGuid(); var conversation = Guid.NewGuid(); var project = Guid.NewGuid();
        var action = ActionIdentifier.RestoreTrusted(Guid.NewGuid());
        ToolIdentifier.TryCreate("owned-project-metadata-read", out var tool);
        var completed = DateTime.UtcNow.AddSeconds(-1);
        var proof = OwnedProjectMetadataProof.FromAuthoritativeRead(project, completed, 2);
        var input = new ExecutionEvidenceInput(Guid.NewGuid(), action,
            new ActionScope(user, conversation, project), tool!, Guid.NewGuid(),
            ExecutionReportOutcome.Succeeded, completed)
        {
            ExecutionVersion = 2, IndependentProof = proof,
            ProjectObservation = new(project, completed, VerificationProofSource.OwnedProjectMetadata, 1)
        };
        var evidence = VerificationEvidence.Issue(input, VerificationOutcome.Verified, DateTime.UtcNow);
        check(evidence.IndependentProof == proof && evidence.ExecutionVersion == 2 &&
            evidence.ExecutionReport == ExecutionReportOutcome.Succeeded &&
            evidence.MatchesBinding(action, input.Scope, tool, input.ApprovalId, input.ExecutionId),
            "issued Verified evidence retains immutable proof and exact binding");
        check(Reject(input with { IndependentProof = null }) &&
            Reject(input with { ExecutionVersion = 1 }) &&
            Reject(input with { Scope = new(user, conversation, Guid.NewGuid()) }) &&
            Reject(input with { Outcome = ExecutionReportOutcome.Failed }),
            "missing, stale, cross-project, or failed proof cannot issue Verified");
        var source = Read("Aura.Infrastructure", "Services", "OwnedProjectMetadataExecutionEvidenceSource.cs");
        var writer = Read("Aura.Infrastructure", "Services", "ExecutionVerificationAssessmentPersistenceWriter.cs");
        var workflow = Read("Aura.Application", "Verifications", "VerificationWorkflowService.cs");
        check(source.Contains("AsNoTracking()") && source.Contains("IndependentProof = independentProof") &&
            source.Contains("OwnedProjectMetadataProof.FromAuthoritativeRead") &&
            !source.Contains("handlerResult") && !source.Contains("Task.Delay"),
            "authoritative source creates proof from database without handler output or retry");
        check(workflow.Contains("execution.IndependentProof is not { } independent") &&
            workflow.Contains("independent.ProjectUpdatedAt != proof.UpdatedAt") &&
            workflow.Contains("execution.ExecutionId != request.ExecutionId") &&
            workflow.Contains("execution.ApprovalId != approval.Id.Value"),
            "workflow checks exact bindings and independent proof");
        check(writer.Contains("evidence.IndependentProof is not { } proof") &&
            writer.Contains("reader.GetDateTime(0) != evidence.IndependentProof!.ProjectUpdatedAt") &&
            writer.Contains("FOR UPDATE") &&
            writer.Contains("state.Status != DurableExecutionStatus.Succeeded") &&
            writer.Contains("IsolationLevel.RepeatableRead") &&
            !writer.Contains("ExecuteUpdateAsync") && !writer.Contains("Task.Delay"),
            "writer rechecks durable state and project without mutating execution");
        check(writer.Contains("VerificationOutcome.Failed or VerificationOutcome.Inconclusive") &&
            writer.Contains("VerificationProofSource.None") && writer.Contains("ProofVersion = verified ? 1 : 0"),
            "writer retains advisory assessment path");
        var api = Read("Aura.Api", "Program.cs");
        check(!api.Contains("AddScoped<IExecutionEvidenceSource, OwnedProjectMetadataExecutionEvidenceSource>") &&
            !api.Contains("AddScoped<IExecutionVerificationAssessmentWriter, ExecutionVerificationAssessmentPersistenceWriter>"),
            "production proof and writer graph remains unregistered");
        var skipped = await LiveIndependentVerificationEvidence.RunAsync(_ => null, _ => { });
        check(skipped == 2, "live harness requires explicit environment gates");
        var local = "Host=127.0.0.1;Port=5433;Database=aura_disposable_test;Username=test;SSL Mode=Disable";
        var values = new Dictionary<string, string> {
            [LiveIndependentVerificationEvidence.EnableVariable] = "true",
            [LiveIndependentVerificationOutcomeMigrationApplication.DisposableVariable] = "true",
            [LiveIndependentVerificationOutcomeMigrationApplication.ConnectionVariable] = local,
            [LiveIndependentVerificationOutcomeMigrationApplication.ExpectedOidVariable] = "24576",
            [LiveIndependentVerificationOutcomeMigrationApplication.ExpectedSystemVariable] = "7696813047573034172"
        };
        var invoked = false;
        async Task<bool[]> Fake(string _, uint __, long ___, CancellationToken ____) {
            invoked = true; await Task.CompletedTask; return new bool[25]; }
        check(await LiveIndependentVerificationEvidence.RunAsync(k => values.GetValueOrDefault(k), _ => { }, Fake) == 1 &&
            !invoked, "non-TLS harness rejects absent local exception gate");
        values[LiveIndependentVerificationOutcomeMigrationApplication.AllowLoopbackNonTlsVariable] = "true";
        check(await LiveIndependentVerificationEvidence.RunAsync(k => values.GetValueOrDefault(k), _ => { }, Fake) == 2 &&
            invoked, "explicit reviewed loopback can enter harness but requires all categories PASS");

        var review = Read("STEP-17R-IMMUTABLE-INDEPENDENT-PROOF-AND-VERIFIED-WRITER.md");
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        check(review.Contains("operator-reported live harness: PASS", StringComparison.Ordinal) &&
            review.Contains("RESULT: PASS", StringComparison.Ordinal) &&
            review.Contains("database OID `24576`", StringComparison.Ordinal) &&
            review.Contains("7696813047573034172", StringComparison.Ordinal),
            "17R documents exact same-target operator PASS without claiming a local live run");
        check(review.Contains("STALE EXECUTION VERSION: PASS", StringComparison.Ordinal) &&
            review.Contains("does not serialize a concurrent project update", StringComparison.Ordinal) &&
            review.Contains("Verification readiness: false", StringComparison.Ordinal),
            "17R distinguishes observed stale-version denial from unresolved concurrent update");
        check(inspector.Contains("ReviewedVerification = new HashSet<Type>()", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<IExecutionEvidenceSource", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<IExecutionVerificationAssessmentWriter", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<IVerificationWorkflowService", StringComparison.Ordinal) &&
            !ProductionRuntimeCompositionInspector.Inspect(new Microsoft.Extensions.DependencyInjection.ServiceCollection())
                .Verification &&
            !new RuntimeSafetyOptions().ProductionAdaptersEnabled &&
            !new RuntimeSafetyOptions().ToolExecutionEnabled,
            "17R live PASS does not activate unreviewed verification DI or production flags");
    }

    private static bool Reject(ExecutionEvidenceInput input)
    {
        try { VerificationEvidence.Issue(input, VerificationOutcome.Verified, DateTime.UtcNow); return false; }
        catch (InvalidOperationException) { return true; }
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", .. parts]));
}
