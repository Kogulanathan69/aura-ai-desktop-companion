using Aura.Api.Security;
using Aura.Application.Security;

internal static class LiveAuditOutboxVerificationChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(root, "security-checks", "Program.cs")))
            root = Path.GetFullPath(Path.Combine(root, ".."));
        var entry = await File.ReadAllTextAsync(Path.Combine(root, "security-checks", "Program.cs"));
        var harness = await File.ReadAllTextAsync(Path.Combine(root, "security-checks", "LiveAuditOutboxVerification.cs"));
        var api = await File.ReadAllTextAsync(Path.Combine(root, "Aura.Api", "Program.cs"));

        var lines = new List<string>();
        check(await LiveAuditOutboxVerification.RunAsync(_ => null, lines.Add,
            (_, _) => throw new InvalidOperationException("live runner called")) == 0 &&
            lines.Single().Contains("SKIPPED", StringComparison.Ordinal),
            "13I live command is disabled without explicit environment gates");

        lines.Clear();
        check(await LiveAuditOutboxVerification.RunAsync(name => name switch
        {
            LiveAuditOutboxVerification.EnableVariable => "true",
            LiveAuditOutboxVerification.DisposableVariable => "true",
            _ => null
        }, lines.Add, (_, _) => throw new InvalidOperationException("live runner called")) == 1 &&
            lines.Single().Contains("FAIL", StringComparison.Ordinal),
            "13I live command fails closed without its dedicated connection string");

        lines.Clear();
        check(await LiveAuditOutboxVerification.RunAsync(name => name switch
        {
            LiveAuditOutboxVerification.EnableVariable => "true",
            LiveAuditOutboxVerification.DisposableVariable => "true",
            LiveAuditOutboxVerification.ConnectionVariable => "Host=example.invalid;Database=ordinary;Username=test",
            _ => null
        }, lines.Add, (_, _) => throw new InvalidOperationException("live runner called")) == 1,
            "13I rejects database names outside the disposable prefix");

        lines.Clear();
        var invoked = false;
        check(await LiveAuditOutboxVerification.RunAsync(name => name switch
        {
            LiveAuditOutboxVerification.EnableVariable => "true",
            LiveAuditOutboxVerification.DisposableVariable => "true",
            LiveAuditOutboxVerification.ConnectionVariable => "Host=example.invalid;Database=aura_disposable_check;Username=test",
            _ => null
        }, lines.Add, (_, _) => { invoked = true; return Task.FromResult(true); }) == 0 && invoked &&
            lines.Single().Contains("PASS", StringComparison.Ordinal),
            "13I valid opt-in path invokes only the injected offline runner");

        check(entry.Contains("args.Length == 1 && args[0] == LiveAuditOutboxVerification.Command") &&
            entry.Contains("await LiveAuditOutboxVerification.RunAsync") &&
            entry.Contains("return;", StringComparison.Ordinal),
            "13I live command is explicit and exits before normal suite");
        check(harness.Contains("AURA_AUDIT_OUTBOX_DISPOSABLE_CONNECTION") &&
            harness.Contains("AURA_AUDIT_OUTBOX_DB_IS_DISPOSABLE") &&
            harness.Contains("aura_disposable_") &&
            !harness.Contains("Password=") && !harness.Contains("Host=localhost") &&
            !harness.Contains("Host=127.0.0.1"),
            "13I connection and disposable gates contain no credential or localhost fallback");
        check(!harness.Contains("db.Database.MigrateAsync(token)") &&
            harness.Contains("20261014000004_HardenSecurityAuditOutboxDelivery") &&
            harness.Contains("x.Id == id.Outbox") &&
            harness.Contains("x.Id == id.Event") &&
            !harness.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase) &&
            !harness.Contains("EnsureCreated", StringComparison.Ordinal),
            "13I requires hardened schema and cleanup remains confined to fixture IDs");
        check(!api.Contains("LiveAuditOutboxVerification") &&
            !api.Contains("SecurityAuditOutboxDispatcher") &&
            !api.Contains("Database.Migrate") &&
            !api.Contains("AddHostedService"),
            "13I production API has no dispatcher, verifier, auto migration, or hosted worker");
        check(!harness.Contains("PeriodicTimer") && !harness.Contains("while (") &&
            !harness.Contains("Task.Delay") && !harness.Contains("OpenAI") &&
            !harness.Contains("Ollama"),
            "13I test harness has no poller, retry loop, or provider calls");
        var startup = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var options = new RuntimeSafetyOptions();
        check(!startup.AuditSink && !options.ProductionAdaptersEnabled &&
            !options.ToolExecutionEnabled && !options.SafeFileAccessEnabled,
            "13I runtime readiness and dangerous capabilities remain disabled");
    }
}
