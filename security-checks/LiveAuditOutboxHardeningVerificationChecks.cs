using Aura.Api.Security;
using Aura.Application.Security;

internal static class LiveAuditOutboxHardeningVerificationChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var output = new List<string>();
        var calls = new List<string>();
        async Task<int> Run(Func<string, string?> environment,
            Func<LiveAuditOutboxHardeningVerification.Input, Action<string>, CancellationToken, Task<bool>>? runner = null)
        {
            output.Clear();
            calls.Clear();
            return await LiveAuditOutboxHardeningVerification.RunAsync(name =>
            { calls.Add(name); return environment(name); }, output.Add,
            runner ?? ((_, _, _) => throw new InvalidOperationException("DB runner invoked")));
        }

        check(await Run(_ => null) == 0 &&
            output.SequenceEqual(["LIVE AUDIT OUTBOX VERIFY: SKIPPED"]) &&
            calls.SequenceEqual([LiveAuditOutboxHardeningVerification.EnableVariable]),
            "13K missing opt-in skips before disposable or connection lookup");
        check(await Run(name => name == LiveAuditOutboxHardeningVerification.EnableVariable ? "true" : null) == 0 &&
            output.SequenceEqual(["LIVE AUDIT OUTBOX VERIFY: SKIPPED"]) &&
            !calls.Contains(LiveAuditOutboxHardeningVerification.ConnectionVariable),
            "13K missing disposable acknowledgement skips before connection lookup");
        check(await Run(name => name switch
        {
            LiveAuditOutboxHardeningVerification.EnableVariable => "true",
            LiveAuditOutboxHardeningVerification.DisposableVariable => "true",
            LiveAuditOutboxHardeningVerification.ConnectionVariable => "Host=example.invalid;Database=ordinary;Username=test",
            _ => null
        }) == 1 && output.SequenceEqual(["DB CONNECTIVITY: FAIL", "RESULT: FAIL"]),
            "13K malformed or non-disposable connection fails without DB runner");

        var invoked = false;
        check(await Run(name => name switch
        {
            LiveAuditOutboxHardeningVerification.EnableVariable => "true",
            LiveAuditOutboxHardeningVerification.DisposableVariable => "true",
            LiveAuditOutboxHardeningVerification.ConnectionVariable =>
                "Host=example.invalid;Database=aura_disposable_check;Username=test",
            _ => null
        }, (input, _, _) => { invoked = true; return Task.FromResult(!input.AllowMigration); }) == 0 &&
            invoked && output.SequenceEqual(["RESULT: PASS"]),
            "13K valid gated input without migration gate invokes only offline runner with migration disabled");
        check(await Run(name => name switch
        {
            LiveAuditOutboxHardeningVerification.EnableVariable => "true",
            LiveAuditOutboxHardeningVerification.DisposableVariable => "true",
            LiveAuditOutboxHardeningVerification.ConnectionVariable =>
                "Host=example.invalid;Database=aura_disposable_check;Username=test",
            LiveAuditOutboxHardeningVerification.MigrationVariable => "true",
            _ => null
        }, (input, _, _) => Task.FromResult(input.AllowMigration)) == 0,
            "13K explicit migration gate is carried only to the gated runner");
        check(await Run(name => name switch
        {
            LiveAuditOutboxHardeningVerification.EnableVariable => "true",
            LiveAuditOutboxHardeningVerification.DisposableVariable => "true",
            LiveAuditOutboxHardeningVerification.ConnectionVariable =>
                "Host=example.invalid;Database=aura_disposable_check;Username=test",
            _ => null
        }, (_, _, _) => throw new Exception("secret failure text")) == 1 &&
            output.SequenceEqual(["RESULT: FAIL"]) &&
            !output.Any(x => x.Contains("secret", StringComparison.Ordinal)),
            "13K exception output is bounded and contains no raw detail");

        static string? GatedEnvironment(string name) => name switch
        {
            LiveAuditOutboxHardeningVerification.EnableVariable => "true",
            LiveAuditOutboxHardeningVerification.DisposableVariable => "true",
            LiveAuditOutboxHardeningVerification.ConnectionVariable =>
                "Host=example.invalid;Database=aura_disposable_check;Username=test",
            _ => null
        };
        check(await Run(GatedEnvironment, (_, write, _) => Task.FromResult(
                LiveAuditOutboxHardeningVerification.CompleteDispatch(false, true, true, write))) == 1 &&
            output.SequenceEqual(["CLEANUP: FAIL", "RESULT: FAIL"]),
            "13K successful dispatch cannot pass when shape cleanup fails");
        check(await Run(GatedEnvironment, (_, write, _) => Task.FromResult(
                LiveAuditOutboxHardeningVerification.CompleteDispatch(true, false, true, write))) == 1 &&
            output.SequenceEqual(["CLEANUP: FAIL", "RESULT: FAIL"]),
            "13K successful dispatch cannot pass when dispatcher cleanup fails");
        check(await Run(GatedEnvironment, (_, write, _) => Task.FromResult(
                LiveAuditOutboxHardeningVerification.CompleteDispatch(true, true, true, write))) == 0 &&
            output.SequenceEqual(["CLEANUP: PASS", "DELIVERY SUCCESS: PASS",
                "DELIVERY FAILURE: PASS", "TERMINAL REDISPATCH: PASS", "RESULT: PASS"]),
            "13K only successful dispatch with both cleanup results can pass");

        var root = Directory.GetCurrentDirectory();
        var harness = File.ReadAllText(Path.Combine(root, "security-checks",
            "LiveAuditOutboxHardeningVerification.cs"));
        var oldHarness = File.ReadAllText(Path.Combine(root, "security-checks",
            "LiveAuditOutboxVerification.cs"));
        var entry = File.ReadAllText(Path.Combine(root, "security-checks", "Program.cs"));
        var api = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        check(entry.Contains("args.Length == 1 && args[0] == LiveAuditOutboxHardeningVerification.Command") &&
            entry.Contains("await LiveAuditOutboxHardeningVerification.RunAsync") &&
            !oldHarness.Contains("MigrateAsync(", StringComparison.Ordinal),
            "13K normal suite remains offline and old command cannot auto-apply the new migration");
        check(harness.Contains("x.DeliveryState == SecurityAuditOutboxDeliveryState.Failed && x.DeliveredAt != null") &&
            harness.IndexOf("PRE-MIGRATION DATA CHECK: PASS", StringComparison.Ordinal) <
                harness.IndexOf("MigrateAsync(HardeningMigration", StringComparison.Ordinal) &&
            harness.Contains("pending.Length != 1 || pending[0] != HardeningMigration") &&
            harness.Contains("!input.AllowMigration") &&
            harness.Contains("!applied.SequenceEqual(expected)") &&
            harness.Contains("SameExpression(expression, NewExpression)"),
            "13K invalid data and unexpected history block exact target migration before schema verification");
        check(!harness.Contains("EnsureCreated", StringComparison.Ordinal) &&
            !harness.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase) &&
            !harness.Contains("ExecuteDeleteAsync", StringComparison.Ordinal) &&
            !harness.Contains("ExecuteSqlRaw", StringComparison.Ordinal) &&
            !harness.Contains("UPDATE \"SecurityAuditOutbox\"", StringComparison.Ordinal),
            "13K has no repair, broad cleanup, truncation, reset or raw SQL migration");
        check(harness.Contains("LiveAuditOutboxVerification.CleanupAsync(factory, fixtures") &&
            harness.Contains("finally") &&
            harness.Contains("PostgresErrorCodes.CheckViolation") &&
            harness.Contains("LiveAuditOutboxVerification.ExecuteLiveAsync"),
            "13K uses generated fixtures, cleanup, database rejection and real dispatcher scenarios");
        check(!api.Contains("SecurityAuditOutboxDispatcher") &&
            !api.Contains("LiveAuditOutboxHardeningVerification") &&
            !api.Contains("AddHostedService") && !api.Contains("MigrateAsync("),
            "13K production API has no dispatcher, live verifier, worker or auto migration");
        check(!harness.Contains("PeriodicTimer") && !harness.Contains("Task.Delay") &&
            !harness.Contains("while (") && !harness.Contains("OpenAI") &&
            !harness.Contains("Ollama"),
            "13K live harness has no polling, retry, provider or real tool call");
        var readiness = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var safety = new RuntimeSafetyOptions();
        check(!readiness.AuditSink && !readiness.ExecutionState &&
            !safety.ProductionAdaptersEnabled && !safety.ToolExecutionEnabled &&
            !safety.SafeFileAccessEnabled && !safety.CloudAiEnabled,
            "13K AuditSink, ExecutionState and all production capabilities remain disabled");
    }
}
