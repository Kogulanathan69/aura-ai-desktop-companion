using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Actions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Application.Verifications;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionVerificationEvidenceChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var output = new List<string>();
        var reads = new List<string>();
        var runs = 0;
        Task<LiveVerificationStage> Fake(string _, CancellationToken __)
        { runs++; return Task.FromResult(LiveVerificationStage.Observed); }
        string? Read(Func<string, string?> source, string key)
        { reads.Add(key); return source(key); }
        check(await LiveProductionVerificationEvidence.RunAsync(
                key => Read(_ => null, key), output.Add, Fake) == 0 &&
            output.SequenceEqual(["PRODUCTION VERIFICATION EVIDENCE: SKIPPED"]) &&
            reads.SequenceEqual([LiveProductionVerificationEvidence.EnableVariable]) && runs == 0,
            "15G absent opt-in skips before connection and DB runner");
        output.Clear(); reads.Clear();
        check(await LiveProductionVerificationEvidence.RunAsync(
                key => Read(name => name == LiveProductionVerificationEvidence.EnableVariable
                    ? "true" : null, key), output.Add, Fake) == 0 &&
            output.SequenceEqual(["PRODUCTION VERIFICATION EVIDENCE: SKIPPED"]) &&
            !reads.Contains("ConnectionStrings__DefaultConnection") && runs == 0,
            "15G absent disposable acknowledgement skips before connection and DB runner");

        const string secret = "synthetic-secret";
        var variables = new Dictionary<string, string?> {
            [LiveProductionVerificationEvidence.EnableVariable] = "true",
            [LiveProductionVerificationEvidence.DisposableVariable] = "true",
            ["ConnectionStrings__DefaultConnection"] = "malformed"
        };
        string? Environment(string key) => variables.GetValueOrDefault(key);
        output.Clear();
        check(await LiveProductionVerificationEvidence.RunAsync(Environment, output.Add, Fake) == 1 &&
            runs == 0 && output.SequenceEqual(["DB CONNECTIVITY: FAIL", "RESULT: FAIL"]),
            "15G malformed connection fails before runner");
        variables["ConnectionStrings__DefaultConnection"] =
            $"Host=example;Database=unreviewed;Username=reviewer;Password={secret}";
        output.Clear();
        check(await LiveProductionVerificationEvidence.RunAsync(Environment, output.Add, Fake) == 1 &&
            runs == 0 && output.SequenceEqual(["DB CONNECTIVITY: FAIL", "RESULT: FAIL"]),
            "15G non-disposable DB name fails before runner");
        variables["ConnectionStrings__DefaultConnection"] =
            $"Host=example;Database=aura_disposable_evidence;Username=reviewer;Password={secret}";
        output.Clear();
        check(await LiveProductionVerificationEvidence.RunAsync(Environment, output.Add, Fake) == 2 &&
            runs == 1 && output.Count == 13 &&
            output.Take(9).All(x => x.EndsWith(": PASS", StringComparison.Ordinal)) &&
            output.Contains("NEGATIVE BINDINGS: PARTIAL") &&
            output.Contains("EXECUTION VERSION BINDING: NOT PROVEN") &&
            output.Contains("INDEPENDENT PROOF ADAPTER: NOT PROVEN") &&
            output.Last() == "RESULT: INCOMPLETE" &&
            output.All(x => !x.Contains(secret, StringComparison.Ordinal)),
            "15G observed DB result stays INCOMPLETE without independent proof");
        foreach (var stage in Enum.GetValues<LiveVerificationStage>()
            .Where(x => x != LiveVerificationStage.Observed))
        {
            output.Clear();
            check(await LiveProductionVerificationEvidence.RunAsync(Environment, output.Add,
                    (_, _) => Task.FromResult(stage)) == 1 &&
                output.Last() == "RESULT: FAIL" && output.Count <= 2 &&
                output.All(x => !x.Contains(secret, StringComparison.Ordinal) &&
                    !x.Contains("Exception", StringComparison.Ordinal)),
                "15G bounded failed category " + stage);
        }
        output.Clear();
        check(await LiveProductionVerificationEvidence.RunAsync(Environment, output.Add,
                (_, _) => throw new InvalidOperationException(secret)) == 1 &&
            output.SequenceEqual(["RESULT: FAIL"]),
            "15G raw exception and secret are suppressed");

        var root = Directory.GetCurrentDirectory();
        string Source(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var entry = Source("security-checks", "Program.cs");
        var harness = Source("security-checks", "LiveProductionVerificationEvidence.cs");
        var api = Source("Aura.Api", "Program.cs");
        var workflow = Source("Aura.Application", "Verifications", "VerificationWorkflowService.cs");
        var contracts = Source("Aura.Application", "Verifications", "VerificationContracts.cs");
        var inspector = Source("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        check(entry.Contains("args.Length == 1 && args[0] == LiveProductionVerificationEvidence.Command") &&
            entry.Contains("await ProductionVerificationEvidenceChecks.RunAsync(Check)") &&
            !api.Contains("LiveProductionVerificationEvidence", StringComparison.Ordinal),
            "15G explicit command exits before normal offline suite");
        check(harness.Contains("LiveAuditOutboxVerification.AddFixtureAsync") &&
            harness.Contains("LiveAuditOutboxVerification.CleanupAsync") &&
            harness.Contains("x.Id == id") &&
            !harness.Contains("MigrateAsync", StringComparison.Ordinal) &&
            !harness.Contains("EnsureCreated", StringComparison.Ordinal) &&
            !harness.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase) &&
            !harness.Contains("Task.Delay", StringComparison.Ordinal) &&
            !harness.Contains("while (", StringComparison.Ordinal) &&
            !harness.Contains("HttpClient", StringComparison.Ordinal) &&
            !harness.Contains("OpenAI", StringComparison.Ordinal) &&
            !harness.Contains("Ollama", StringComparison.Ordinal) &&
            !harness.Contains("ExecuteTool", StringComparison.Ordinal),
            "15G exact generated cleanup and no migration, worker, retry, provider or tool path");
        check(workflow.Contains("execution.ExecutionId != request.ExecutionId") &&
            workflow.Contains("execution.ActionId != action.Id") &&
            workflow.Contains("execution.Scope != scope") &&
            workflow.Contains("execution.ToolId != action.ToolId") &&
            workflow.Contains("execution.ApprovalId != approval.Id.Value") &&
            workflow.Contains("execution.Outcome != ExecutionReportOutcome.Succeeded") &&
            !workflow.Contains("SaveChangesAsync", StringComparison.Ordinal) &&
            !workflow.Contains("ExecuteUpdateAsync", StringComparison.Ordinal) &&
            !workflow.Contains("TryCompleteAsync", StringComparison.Ordinal),
            "15G existing workflow checks exact available bindings and never mutates state");
        var adapters = typeof(AuraDbContext).Assembly.GetTypes()
            .Concat(typeof(IExecutionEvidenceSource).Assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract &&
                typeof(IExecutionEvidenceSource).IsAssignableFrom(type)).ToArray();
        check(adapters.ToHashSet().SetEquals([typeof(UnavailableExecutionEvidenceSource),
                typeof(Aura.Infrastructure.Services.OwnedProjectMetadataExecutionEvidenceSource)]) &&
            api.Contains("AddScoped<IExecutionEvidenceSource", StringComparison.Ordinal) &&
            inspector.Contains("ReviewedVerificationWorkflow = new HashSet<Type>()") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Verification &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().Verification &&
            !new RuntimeSafetyOptions().ProductionAdaptersEnabled &&
            !new RuntimeSafetyOptions().ToolExecutionEnabled,
            "17U independent source is registered but incomplete workflow cannot claim readiness");
        check(typeof(ExecutionEvidenceInput).GetProperties().Any(x => x.Name == "ExecutionVersion") &&
            typeof(ExecutionEvidenceInput).GetProperties().Any(x => x.Name == "ProjectObservation") &&
            typeof(ExecutionVerificationAssessmentRecord).GetProperties().Any(x =>
                x.Name == "ExecutionVersion"),
            "15G historical evidence contract still lacks a live adapter despite pending version field");

        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only", npgsql => npgsql.UseVector()).Options;
        await using var db = new AuraDbContext(options);
        var assessment = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(ExecutionVerificationAssessmentRecord))!;
        var sql = assessment.GetCheckConstraints()
            .Single(x => x.Name == "CK_ExecutionVerificationAssessments_State").Sql!;
        check(sql.Contains("\"ExecutionStatus\" IN (2, 3)") &&
            sql.Contains("\"Outcome\" IN (1, 2)") &&
            sql.Contains("\"ProofSource\" = 0") &&
            sql.Contains("\"ProofVersion\" = 0") &&
            sql.Contains("\"ExecutionVersion\" = 2") &&
            Enum.GetValues<VerificationProofSource>().SequenceEqual([
                VerificationProofSource.None, VerificationProofSource.OwnedProjectMetadata]),
            "15G assessment schema still forbids Verified; new source remains unproven");
        check(assessment.FindPrimaryKey()!.Properties.Single().Name == "Id" &&
            !assessment.GetIndexes().Any(x => x.IsUnique && x.Properties.Any(p =>
                p.Name is "ActionId" or "ExecutionId")),
            "15G repeated assessments have no action/execution single-use constraint");

        var action = (ActionIdentifier)Activator.CreateInstance(typeof(ActionIdentifier),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [Guid.NewGuid()], null)!;
        var scope = new ActionScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        ToolIdentifier.TryCreate("verification-check", out var tool);
        var policy = new ConservativeVerificationPolicy();
        async Task<VerificationOutcome> Assess(ExecutionReportOutcome report) =>
            await policy.EvaluateAsync(new(Guid.NewGuid(), action, scope, tool!, Guid.NewGuid(),
                report, DateTime.UtcNow), CancellationToken.None);
        check(await Assess(ExecutionReportOutcome.Succeeded) == VerificationOutcome.Inconclusive &&
            await Assess(ExecutionReportOutcome.Failed) == VerificationOutcome.Failed &&
            await Assess(ExecutionReportOutcome.Unknown) == VerificationOutcome.Inconclusive &&
            await new UnavailableExecutionEvidenceSource().GetAsync(scope, Guid.NewGuid(),
                CancellationToken.None) is null,
            "15G success report alone, failure and unknown cannot produce synthetic Verified");
    }
}
