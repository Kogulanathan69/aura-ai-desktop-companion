using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Actions;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionExecutionStateEvidenceChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var output = new List<string>();
        var calls = 0;
        Task<LiveExecutionStateStage> Fake(LiveExecutionStateInput _, CancellationToken __)
        {
            calls++;
            return Task.FromResult(LiveExecutionStateStage.Passed);
        }

        check(await LiveExecutionStateVerification.RunProductionAsync(
                key => key == LiveExecutionStateVerification.ProductionEnableVariable ? null :
                    throw new InvalidOperationException("Unexpected environment read"),
                output.Add, Fake) == 0 && calls == 0 &&
            output.SequenceEqual(["PRODUCTION EXECUTION STATE EVIDENCE: SKIPPED"]),
            "15D missing opt-in skips before connection and DB access");
        output.Clear();
        check(await LiveExecutionStateVerification.RunProductionAsync(
                key => key == LiveExecutionStateVerification.ProductionEnableVariable ? "true" :
                    key == LiveExecutionStateVerification.ProductionDisposableVariable ? null :
                    throw new InvalidOperationException("Unexpected connection read"),
                output.Add, Fake) == 0 && calls == 0 &&
            output.SequenceEqual(["PRODUCTION EXECUTION STATE EVIDENCE: SKIPPED"]),
            "15D missing disposable acknowledgement skips before connection and DB access");

        const string secret = "synthetic-secret";
        var variables = new Dictionary<string, string?> {
            [LiveExecutionStateVerification.ProductionEnableVariable] = "true",
            [LiveExecutionStateVerification.ProductionDisposableVariable] = "true",
            ["ConnectionStrings__DefaultConnection"] = "malformed"
        };
        string? Read(string key) => variables.GetValueOrDefault(key);
        output.Clear();
        check(await LiveExecutionStateVerification.RunProductionAsync(Read, output.Add, Fake) == 1 &&
            calls == 0 && output.SequenceEqual(["DB CONNECTIVITY: FAIL", "RESULT: FAIL"]),
            "15D malformed connection fails before DB runner");
        variables["ConnectionStrings__DefaultConnection"] =
            $"Host=example;Database=aura_disposable_evidence;Username=reviewer;Password={secret}";
        output.Clear();
        check(await LiveExecutionStateVerification.RunProductionAsync(Read, output.Add, Fake) == 0 &&
            calls == 1 && output.Count == 11 && output.Last() == "RESULT: PASS" &&
            output.Take(10).All(x => x.EndsWith(": PASS", StringComparison.Ordinal)) &&
            output.All(x => !x.Contains(secret, StringComparison.Ordinal)),
            "15D explicit opted-in run emits bounded categories");
        variables["ConnectionStrings__DefaultConnection"] =
            $"Host=example;Database=unknown;Username=reviewer;Password={secret}";
        output.Clear();
        check(await LiveExecutionStateVerification.RunProductionAsync(Read, output.Add, Fake) == 1 &&
            calls == 1 && output.SequenceEqual(["DB CONNECTIVITY: FAIL", "RESULT: FAIL"]),
            "15D unknown database name fails before DB runner");
        variables["ConnectionStrings__DefaultConnection"] =
            $"Host=example;Database=aura_disposable_evidence;Username=reviewer;Password={secret}";
        foreach (var stage in Enum.GetValues<LiveExecutionStateStage>()
            .Where(x => x != LiveExecutionStateStage.Passed))
        {
            output.Clear();
            check(await LiveExecutionStateVerification.RunProductionAsync(Read, output.Add,
                    (_, _) => Task.FromResult(stage)) == 1 &&
                output.Last() == "RESULT: FAIL" && output.Count <= 2 &&
                output.All(x => !x.Contains(secret, StringComparison.Ordinal) &&
                    !x.Contains("Exception", StringComparison.Ordinal)),
                "15D bounded failure stage " + stage);
        }
        output.Clear();
        check(await LiveExecutionStateVerification.RunProductionAsync(Read, output.Add,
                (_, _) => throw new InvalidOperationException(secret)) == 1 &&
            output.SequenceEqual(["RESULT: FAIL"]),
            "15D exceptions and secrets are suppressed");

        var root = Directory.GetCurrentDirectory();
        string Source(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var entry = Source("security-checks", "Program.cs");
        var harness = Source("security-checks", "LiveExecutionStateVerification.cs");
        var api = Source("Aura.Api", "Program.cs");
        var inspector = Source("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        check(entry.Contains("args.Length == 1 && args[0] == LiveExecutionStateVerification.ProductionCommand") &&
            entry.Contains("await ProductionExecutionStateEvidenceChecks.RunAsync(Check)") &&
            !api.Contains("LiveExecutionStateVerification", StringComparison.Ordinal),
            "15D command is explicit and normal suite only runs offline checks");
        check(harness.Contains("ids.Actions.Contains(x.ActionId)") &&
            harness.Contains("x.UserId == ids.User") &&
            harness.Contains("ids.Approvals.Contains(x.Id)") &&
            !harness.Contains("Database.Migrate", StringComparison.Ordinal) &&
            !harness.Contains("EnsureCreated", StringComparison.Ordinal) &&
            !harness.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase) &&
            !harness.Contains("AddHostedService", StringComparison.Ordinal) &&
            !harness.Contains("Task.Delay", StringComparison.Ordinal) &&
            !harness.Contains("HttpClient", StringComparison.Ordinal) &&
            !harness.Contains("ExecuteTool", StringComparison.Ordinal),
            "15D generated fixture cleanup is scoped and no migration, worker, provider or tool is invoked");
        check(api.Contains("AddStateAuditReconciliationRuntimeFoundation", StringComparison.Ordinal) &&
            inspector.Contains("ReviewedExecutionStateLiveEvidence", StringComparison.Ordinal) &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).ExecutionState &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().ExecutionState &&
            !new RuntimeSafetyOptions().ProductionAdaptersEnabled &&
            !new RuntimeSafetyOptions().ToolExecutionEnabled,
            "17X prior live evidence does not activate the changed production path");

        var action = (ActionIdentifier)Activator.CreateInstance(typeof(ActionIdentifier),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [Guid.NewGuid()], null)!;
        ExecutionAttemptIdentifier Attempt() => (ExecutionAttemptIdentifier)
            Activator.CreateInstance(typeof(ExecutionAttemptIdentifier),
                BindingFlags.Instance | BindingFlags.NonPublic, null, [Guid.NewGuid()], null)!;
        ToolIdentifier.TryCreate("evidence-tool", out var tool);
        ToolIdentifier.TryCreate("other-tool", out var otherTool);
        var scope = new ActionScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var approval = Guid.NewGuid();
        var ready = new ActionExecutionState(action, scope, tool!, approval,
            ActionExecutionStateStatus.Ready, 0, null, null, null);
        var execution = Attempt();
        var reserved = ActionExecutionStatePolicy.TryReserve(ready,
            new(action, scope, tool!, approval, 0, execution));
        check(reserved.Status == ActionExecutionStateResultStatus.Success &&
            reserved.State?.Version == 1 &&
            reserved.State.ApprovalConsumedByExecutionId == execution.Value,
            "15D synthetic reservation consumes approval and advances exact version");
        var state = reserved.State!;
        check(ActionExecutionStatePolicy.TryReserve(state,
                new(action, scope, tool!, approval, 0, Attempt())).Status ==
            ActionExecutionStateResultStatus.StaleVersion &&
            ActionExecutionStatePolicy.TryReserve(state,
                new(action, scope, tool!, approval, 1, Attempt())).Status ==
            ActionExecutionStateResultStatus.AlreadyReserved,
            "15D synthetic stale reservation and approval reuse reject");
        check(ActionExecutionStatePolicy.TryReserve(ready,
                new(action, scope, tool!, approval, 0, execution)).Status ==
            ActionExecutionStateResultStatus.Success &&
            harness.Contains("IX_ActionExecutionStates_ExecutionId") &&
            harness.Contains("reusedExecution.Status"),
            "15D synthetic policy cannot prove cross-row execution ID uniqueness; live unique index scenario is required");
        var wrongBindings = new[] {
            ActionExecutionStatePolicy.TryReserve(ready,
                new(action, scope with { UserId = Guid.NewGuid() }, tool!, approval, 0, Attempt())).Status,
            ActionExecutionStatePolicy.TryReserve(ready,
                new(action, scope with { ConversationId = Guid.NewGuid() }, tool!, approval, 0, Attempt())).Status,
            ActionExecutionStatePolicy.TryReserve(ready,
                new(action, scope with { ProjectId = Guid.NewGuid() }, tool!, approval, 0, Attempt())).Status
        };
        check(wrongBindings.All(x => x == ActionExecutionStateResultStatus.ScopeMismatch) &&
            ActionExecutionStatePolicy.TryReserve(ready,
                new(action, scope, otherTool!, approval, 0, Attempt())).Status ==
                ActionExecutionStateResultStatus.ToolMismatch &&
            ActionExecutionStatePolicy.TryReserve(ready,
                new(action, scope, tool!, Guid.NewGuid(), 0, Attempt())).Status ==
                ActionExecutionStateResultStatus.ApprovalMismatch,
            "15D synthetic scope, tool and approval binding rejects");
        check(ActionExecutionStatePolicy.TryComplete(state,
                new(action, scope, tool!, approval, execution, 0, ExecutionOutcome.Succeeded)).Status ==
                ActionExecutionStateResultStatus.StaleVersion &&
            ActionExecutionStatePolicy.TryComplete(state,
                new(action, scope, tool!, approval, Attempt(), 1, ExecutionOutcome.Succeeded)).Status ==
                ActionExecutionStateResultStatus.ExecutionMismatch,
            "15D synthetic stale completion and wrong execution binding reject");
        var completed = ActionExecutionStatePolicy.TryComplete(state,
            new(action, scope, tool!, approval, execution, 1, ExecutionOutcome.Succeeded));
        check(completed.Status == ActionExecutionStateResultStatus.Success &&
            completed.State?.Version == 2 &&
            ActionExecutionStatePolicy.TryComplete(completed.State,
                new(action, scope, tool!, approval, execution, 2, ExecutionOutcome.Succeeded)).Status ==
                ActionExecutionStateResultStatus.AlreadyCompleted &&
            ActionExecutionStatePolicy.TryReserve(completed.State,
                new(action, scope, tool!, approval, 2, Attempt())).Status ==
                ActionExecutionStateResultStatus.AlreadyCompleted,
            "15D synthetic terminal replay and reopen reject");
    }
}
