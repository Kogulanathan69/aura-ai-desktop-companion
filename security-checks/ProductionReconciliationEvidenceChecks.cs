using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionReconciliationEvidenceChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var output = new List<string>();
        var reads = new List<string>();
        var runs = 0;
        Task<LiveReconciliationStage> Fake(string _, CancellationToken __)
        { runs++; return Task.FromResult(LiveReconciliationStage.Passed); }
        string? Read(Func<string, string?> source, string key)
        { reads.Add(key); return source(key); }
        check(await LiveProductionReconciliationEvidence.RunAsync(
                key => Read(_ => null, key), output.Add, Fake) == 0 &&
            output.SequenceEqual(["PRODUCTION RECONCILIATION EVIDENCE: SKIPPED"]) &&
            reads.SequenceEqual([LiveProductionReconciliationEvidence.EnableVariable]) && runs == 0,
            "15F absent opt-in skips before connection and DB runner");
        output.Clear(); reads.Clear();
        check(await LiveProductionReconciliationEvidence.RunAsync(
                key => Read(name => name == LiveProductionReconciliationEvidence.EnableVariable
                    ? "true" : null, key), output.Add, Fake) == 0 &&
            output.SequenceEqual(["PRODUCTION RECONCILIATION EVIDENCE: SKIPPED"]) &&
            !reads.Contains("ConnectionStrings__DefaultConnection") && runs == 0,
            "15F absent disposable acknowledgement skips before connection and DB runner");

        const string secret = "synthetic-secret";
        var variables = new Dictionary<string, string?> {
            [LiveProductionReconciliationEvidence.EnableVariable] = "true",
            [LiveProductionReconciliationEvidence.DisposableVariable] = "true",
            ["ConnectionStrings__DefaultConnection"] = "malformed"
        };
        string? Environment(string key) => variables.GetValueOrDefault(key);
        output.Clear();
        check(await LiveProductionReconciliationEvidence.RunAsync(Environment, output.Add, Fake) == 1 &&
            runs == 0 && output.SequenceEqual(["DB CONNECTIVITY: FAIL", "RESULT: FAIL"]),
            "15F malformed connection fails before runner");
        variables["ConnectionStrings__DefaultConnection"] =
            $"Host=example;Database=unreviewed;Username=reviewer;Password={secret}";
        output.Clear();
        check(await LiveProductionReconciliationEvidence.RunAsync(Environment, output.Add, Fake) == 1 &&
            runs == 0 && output.SequenceEqual(["DB CONNECTIVITY: FAIL", "RESULT: FAIL"]),
            "15F non-disposable database name fails before runner");
        variables["ConnectionStrings__DefaultConnection"] =
            $"Host=example;Database=aura_disposable_evidence;Username=reviewer;Password={secret}";
        output.Clear();
        check(await LiveProductionReconciliationEvidence.RunAsync(Environment, output.Add, Fake) == 0 &&
            runs == 1 && output.Count == 11 &&
            output.Take(10).All(x => x.EndsWith(": PASS", StringComparison.Ordinal)) &&
            output.Last() == "RESULT: PASS" &&
            output.All(x => !x.Contains(secret, StringComparison.Ordinal)),
            "15F opted-in runner emits bounded categories only");
        foreach (var stage in Enum.GetValues<LiveReconciliationStage>()
            .Where(x => x != LiveReconciliationStage.Passed))
        {
            output.Clear();
            check(await LiveProductionReconciliationEvidence.RunAsync(Environment, output.Add,
                    (_, _) => Task.FromResult(stage)) == 1 &&
                output.Last() == "RESULT: FAIL" && output.Count <= 2 &&
                output.All(x => !x.Contains(secret, StringComparison.Ordinal) &&
                    !x.Contains("Exception", StringComparison.Ordinal)),
                "15F bounded failed category " + stage);
        }
        output.Clear();
        check(await LiveProductionReconciliationEvidence.RunAsync(Environment, output.Add,
                (_, _) => throw new InvalidOperationException(secret)) == 1 &&
            output.SequenceEqual(["RESULT: FAIL"]),
            "15F raw exception and secret are suppressed");

        var root = Directory.GetCurrentDirectory();
        string Source(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var entry = Source("security-checks", "Program.cs");
        var harness = Source("security-checks", "LiveProductionReconciliationEvidence.cs");
        var api = Source("Aura.Api", "Program.cs");
        var reader = Source("Aura.Infrastructure", "Services", "ExecutionReconciliationPersistenceSource.cs");
        var serviceSource = Source("Aura.Application", "Executions", "ExecutionReconciliationService.cs");
        var inspector = Source("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        check(entry.Contains("args.Length == 1 && args[0] == LiveProductionReconciliationEvidence.Command") &&
            entry.Contains("await ProductionReconciliationEvidenceChecks.RunAsync(Check)") &&
            !api.Contains("LiveProductionReconciliationEvidence", StringComparison.Ordinal),
            "15F explicit live command exits before normal offline suite");
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
            "15F generated fixture cleanup is exact with no migration, worker, retry, provider or tool path");
        check(reader.Contains("AsNoTracking()") &&
            reader.Contains("IsolationLevel.RepeatableRead") &&
            !reader.Contains("SaveChangesAsync") &&
            !reader.Contains("ExecuteUpdateAsync") &&
            !reader.Contains("ExecuteDeleteAsync") &&
            serviceSource.Contains("AlreadyResolved") &&
            !serviceSource.Contains("TryCompleteAsync") &&
            !serviceSource.Contains("TryReserveAsync"),
            "15F real source is read-only and advisory service never mutates durable state");
        check(!api.Contains("ExecutionReconciliationPersistenceSource", StringComparison.Ordinal) &&
            !api.Contains("AddHostedService", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync", StringComparison.Ordinal) &&
            inspector.Contains("ReviewedReconciliation = new HashSet<Type>()") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Reconciliation &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().Reconciliation &&
            !new RuntimeSafetyOptions().ProductionAdaptersEnabled &&
            !new RuntimeSafetyOptions().ToolExecutionEnabled,
            "15F live PASS cannot activate production reconciliation or execution");

        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only", npgsql => npgsql.UseVector()).Options;
        await using var db = new AuraDbContext(options);
        var entity = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(ExecutionReconciliationAssessmentRecord))!;
        var stateSql = entity.GetCheckConstraints()
            .Single(x => x.Name == "CK_ExecutionReconciliationAssessments_State").Sql!;
        check(stateSql.Contains("\"ObservedStatus\" = 1") &&
            stateSql.Contains("\"ObservedVersion\" = 1") &&
            stateSql.Contains("\"Reason\" BETWEEN 0 AND 5") &&
            stateSql.Contains("\"Disposition\" IN (0, 1)"),
            "15F offline model bounds Reserved v1, reason and disposition");
        check(!entity.GetIndexes().Any(x => x.IsUnique &&
                x.Properties.Any(p => p.Name is "ActionId" or "ExecutionId")) &&
            entity.FindPrimaryKey()!.Properties.Single().Name == "Id",
            "15F assessment rows have distinct IDs but no action/execution single-use guarantee");

        var action = Identifier(Guid.NewGuid());
        var scope = new ActionScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        ToolIdentifier.TryCreate("reconciliation-check", out var tool);
        var approval = Guid.NewGuid();
        var execution = Guid.NewGuid();
        var ready = new ActionExecutionState(action, scope, tool!, approval,
            ActionExecutionStateStatus.Ready, 0, null, null, null);
        var reserved = ready with { Status = ActionExecutionStateStatus.Reserved,
            Version = 1, ExecutionId = execution, ApprovalConsumedByExecutionId = execution };
        ExecutionReconciliationSnapshot Snapshot(ActionExecutionState state) =>
            new(state, action, scope, tool!, approval, execution, state.Version,
                ExecutionReconciliationReason.Unknown);
        var source = new FixedSource(Snapshot(reserved));
        var workflow = new ExecutionReconciliationService(new AllowScope(), source,
            new ConservativeExecutionReconciliationPolicy(), new FixedClock());
        var first = await workflow.ReconcileAsync(scope, new(action));
        var second = await workflow.ReconcileAsync(scope, new(action));
        check(first.Status == ExecutionReconciliationStatus.Success &&
            second.Status == ExecutionReconciliationStatus.Success &&
            first.Evidence?.Id.Value != second.Evidence?.Id.Value &&
            source.Snapshot!.State == reserved,
            "15F synthetic Reserved v1 repeat issues distinct advisory evidence without state mutation");
        source.Snapshot = Snapshot(ready) with { ExecutionId = Guid.Empty };
        check((await workflow.ReconcileAsync(scope, new(action))).Status ==
            ExecutionReconciliationStatus.NotEligible,
            "15F synthetic Ready is ineligible");
        foreach (var terminal in new[] { ActionExecutionStateStatus.Succeeded,
            ActionExecutionStateStatus.Failed })
        {
            source.Snapshot = Snapshot(reserved with { Status = terminal, Version = 2,
                Outcome = terminal == ActionExecutionStateStatus.Succeeded
                    ? ExecutionOutcome.Succeeded : ExecutionOutcome.Failed });
            check((await workflow.ReconcileAsync(scope, new(action))).Status ==
                ExecutionReconciliationStatus.AlreadyResolved,
                "15F synthetic terminal " + terminal + " is ineligible");
        }
        source.Snapshot = Snapshot(reserved) with { Version = 0 };
        check((await workflow.ReconcileAsync(scope, new(action))).Status ==
            ExecutionReconciliationStatus.StateConflict,
            "15F synthetic snapshot version mismatch is denied");
        var baseline = Snapshot(reserved);
        foreach (var wrong in new[] {
            baseline with { Scope = scope with { UserId = Guid.NewGuid() } },
            baseline with { Scope = scope with { ConversationId = Guid.NewGuid() } },
            baseline with { Scope = scope with { ProjectId = Guid.NewGuid() } },
            baseline with { ToolId = OtherTool() },
            baseline with { ApprovalId = Guid.NewGuid() },
            baseline with { ExecutionId = Guid.NewGuid() },
            baseline with { ActionId = Identifier(Guid.NewGuid()) }
        })
        {
            source.Snapshot = wrong;
            check((await workflow.ReconcileAsync(scope, new(action))).Status ==
                ExecutionReconciliationStatus.StateConflict,
                "15F synthetic wrong reconciliation binding denied");
        }
        source.Snapshot = baseline with { Reason = (ExecutionReconciliationReason)999 };
        check((await workflow.ReconcileAsync(scope, new(action))).Status ==
            ExecutionReconciliationStatus.StateConflict,
            "15F unknown reason fails closed");
        source.Snapshot = baseline;
        var invalidPolicy = new ExecutionReconciliationService(new AllowScope(), source,
            new InvalidPolicy(), new FixedClock());
        check((await invalidPolicy.ReconcileAsync(scope, new(action))).Status ==
            ExecutionReconciliationStatus.PolicyDenied,
            "15F unknown disposition fails closed");
    }

    private static ActionIdentifier Identifier(Guid id) =>
        (ActionIdentifier)Activator.CreateInstance(typeof(ActionIdentifier),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [id], null)!;

    private static ToolIdentifier OtherTool()
    { ToolIdentifier.TryCreate("other-tool", out var value); return value!; }

    private sealed class FixedSource(ExecutionReconciliationSnapshot snapshot)
        : IExecutionReconciliationSource
    {
        internal ExecutionReconciliationSnapshot? Snapshot = snapshot;
        public Task<ExecutionReconciliationSnapshot?> GetAsync(ActionScope _, ActionIdentifier __,
            CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(Snapshot); }
    }

    private sealed class AllowScope : IExecutionReconciliationScopeValidator
    {
        public Task<bool> ValidateActionAsync(ExactActionOwnershipScope action, CancellationToken token) =>
            ValidateAsync(action.Scope, token);
        public Task<bool> ValidateAsync(ActionScope _, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(true); }
    }

    private sealed class FixedClock : IDateTimeProvider
    { public DateTime UtcNow => new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc); }

    private sealed class InvalidPolicy : IExecutionReconciliationPolicy
    {
        public Task<ExecutionReconciliationDisposition> AssessAsync(
            ExecutionReconciliationSnapshot _, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult((ExecutionReconciliationDisposition)999); }
    }
}
