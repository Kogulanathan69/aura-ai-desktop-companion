using Aura.Api.Security;
using Aura.Application.Security;
using Aura.Infrastructure.Data;

internal static class LiveExecutionStateVerificationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var lines = new List<string>();
        var calls = 0;
        Task<LiveExecutionStateStage> Fake(LiveExecutionStateInput _, CancellationToken __)
        { calls++; return Task.FromResult(LiveExecutionStateStage.Passed); }
        string? Absent(string key) => key == LiveExecutionStateVerification.EnableVariable
            ? null : throw new InvalidOperationException("Unexpected environment read");
        check(await LiveExecutionStateVerification.RunAsync(Absent, lines.Add, Fake) == 0 &&
            lines.SequenceEqual(["LIVE EXECUTION STATE VERIFY: SKIPPED"]) && calls == 0,
            "13D absent opt-in skips before any DB runner or connection input");
        lines.Clear();
        check(await LiveExecutionStateVerification.RunAsync(
                key => key == LiveExecutionStateVerification.EnableVariable ? "true" :
                    key == LiveExecutionStateVerification.DisposableVariable ? null :
                    throw new InvalidOperationException("Unexpected connection read"),
                lines.Add, Fake) == 0 &&
            lines.SequenceEqual(["LIVE EXECUTION STATE VERIFY: SKIPPED"]) && calls == 0,
            "13D absent disposable acknowledgement skips before any DB runner or connection input");
        const string secret = "Host=example;Database=disposable;Password=synthetic-secret";
        var variables = new Dictionary<string, string?> {
            [LiveExecutionStateVerification.EnableVariable] = "true",
            [LiveExecutionStateVerification.DisposableVariable] = "true",
            ["ConnectionStrings__DefaultConnection"] = secret
        };
        string? Read(string key) => variables.GetValueOrDefault(key);
        variables["ConnectionStrings__DefaultConnection"] = "malformed";
        lines.Clear();
        check(await LiveExecutionStateVerification.RunAsync(Read, lines.Add, Fake) == 1 &&
            lines.SequenceEqual(["DB CONNECTIVITY: FAIL", "RESULT: FAIL"]) && calls == 0,
            "13D malformed connection fails before DB runner without disclosure");
        variables["ConnectionStrings__DefaultConnection"] = secret;
        lines.Clear();
        check(await LiveExecutionStateVerification.RunAsync(Read, lines.Add, Fake) == 0 && calls == 1 &&
            lines.Last() == "RESULT: PASS" && lines.Count == 11 &&
            lines.Take(10).All(line => line.EndsWith(": PASS", StringComparison.Ordinal)) &&
            lines.All(line => !line.Contains(secret, StringComparison.Ordinal)),
            "13D opted-in runner emits only bounded success categories");
        foreach (var stage in Enum.GetValues<LiveExecutionStateStage>()
            .Where(x => x != LiveExecutionStateStage.Passed))
        {
            lines.Clear();
            check(await LiveExecutionStateVerification.RunAsync(Read, lines.Add,
                    (_, _) => Task.FromResult(stage)) == 1 &&
                lines.Last() == "RESULT: FAIL" && lines.Count <= 2 &&
                lines.All(line => !line.Contains(secret, StringComparison.Ordinal) &&
                    !line.Contains("Exception", StringComparison.Ordinal)),
                "13D failure category is bounded: " + stage);
        }
        lines.Clear();
        check(await LiveExecutionStateVerification.RunAsync(Read, lines.Add,
                (_, _) => throw new InvalidOperationException(secret)) == 1 &&
            lines.SequenceEqual(["RESULT: FAIL"]),
            "13D raw database exception and secret never reach output");

        var root = Directory.GetCurrentDirectory();
        var entry = File.ReadAllText(Path.Combine(root, "security-checks", "Program.cs"));
        var harness = File.ReadAllText(Path.Combine(root, "security-checks", "LiveExecutionStateVerification.cs"));
        var api = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        check(entry.Contains("args.Length == 1 && args[0] == LiveExecutionStateVerification.Command") &&
            entry.Contains("return;", StringComparison.Ordinal) &&
            !api.Contains("LiveExecutionStateVerification", StringComparison.Ordinal) &&
            !api.Contains("ActionExecutionStatePersistenceStore", StringComparison.Ordinal),
            "13D explicit command is separate from normal offline suite and API runtime");
        check(!harness.Contains("Database.Migrate", StringComparison.Ordinal) &&
            !harness.Contains("EnsureCreated", StringComparison.Ordinal) &&
            !harness.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase) &&
            !harness.Contains("ExecuteDeleteAsync(CancellationToken.None)", StringComparison.Ordinal) &&
            harness.Contains("db.SecurityAuditOutbox", StringComparison.Ordinal) &&
            harness.Contains("db.SecurityAuditEvents", StringComparison.Ordinal) &&
            harness.Contains("db.ExecutionVerificationAssessments", StringComparison.Ordinal) &&
            harness.Contains("db.ExecutionReconciliationAssessments", StringComparison.Ordinal) &&
            harness.Contains("ids.Actions.Contains(", StringComparison.Ordinal) &&
            harness.Contains("x.UserId == ids.User", StringComparison.Ordinal) &&
            harness.Contains("ids.Conversation &&", StringComparison.Ordinal) &&
            harness.Contains("ids.Project &&", StringComparison.Ordinal) &&
            harness.Contains("ids.User &&", StringComparison.Ordinal) &&
            harness.Contains("ids.AuthUser", StringComparison.Ordinal),
            "13D cleanup includes later security dependents and remains scoped to generated fixture IDs");
        var startup = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var informational = new PersistenceCapabilityReadiness().GetSnapshot();
        var safety = new RuntimeSafetyOptions();
        check(!startup.ExecutionState && !informational.ExecutionState &&
            !safety.ProductionAdaptersEnabled && !safety.ToolExecutionEnabled &&
            !safety.SafeFileAccessEnabled,
            "13D live harness does not inflate runtime readiness or enable execution and file access");
    }
}
