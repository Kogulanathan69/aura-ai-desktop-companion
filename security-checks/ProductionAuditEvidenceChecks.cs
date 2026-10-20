using Aura.Api.Security;
using Aura.Application.Security;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionAuditEvidenceChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var output = new List<string>();
        var calls = new List<string>();
        var runs = 0;
        Task<(bool Passed, bool? Cleanup)> Fake(string _, CancellationToken __)
        {
            runs++;
            return Task.FromResult<(bool, bool?)>((true, true));
        }
        string? Read(Func<string, string?> source, string key)
        {
            calls.Add(key);
            return source(key);
        }
        check(await LiveAuditOutboxVerification.RunProductionAsync(
                key => Read(_ => null, key), output.Add, Fake) == 0 &&
            output.SequenceEqual(["PRODUCTION AUDIT EVIDENCE: SKIPPED"]) &&
            calls.SequenceEqual([LiveAuditOutboxVerification.ProductionEnableVariable]) && runs == 0,
            "15E absent opt-in skips before disposable gate, connection and DB runner");
        output.Clear(); calls.Clear();
        check(await LiveAuditOutboxVerification.RunProductionAsync(
                key => Read(name => name == LiveAuditOutboxVerification.ProductionEnableVariable
                    ? "true" : null, key), output.Add, Fake) == 0 &&
            output.SequenceEqual(["PRODUCTION AUDIT EVIDENCE: SKIPPED"]) &&
            !calls.Contains(LiveAuditOutboxVerification.ProductionConnectionVariable) && runs == 0,
            "15E absent disposable acknowledgement skips before connection and DB runner");

        const string secret = "synthetic-secret";
        var variables = new Dictionary<string, string?> {
            [LiveAuditOutboxVerification.ProductionEnableVariable] = "true",
            [LiveAuditOutboxVerification.ProductionDisposableVariable] = "true",
            [LiveAuditOutboxVerification.ProductionConnectionVariable] = "malformed"
        };
        string? Environment(string key) => variables.GetValueOrDefault(key);
        output.Clear();
        check(await LiveAuditOutboxVerification.RunProductionAsync(Environment,
                output.Add, Fake) == 1 && runs == 0 &&
            output.SequenceEqual(["DB CONNECTIVITY: FAIL", "RESULT: FAIL"]),
            "15E malformed connection fails before DB runner");
        variables[LiveAuditOutboxVerification.ProductionConnectionVariable] =
            $"Host=example;Database=unverified;Username=reviewer;Password={secret}";
        output.Clear();
        check(await LiveAuditOutboxVerification.RunProductionAsync(Environment,
                output.Add, Fake) == 1 && runs == 0 &&
            output.SequenceEqual(["DB CONNECTIVITY: FAIL", "RESULT: FAIL"]),
            "15E non-disposable database name fails before DB runner");
        variables[LiveAuditOutboxVerification.ProductionConnectionVariable] =
            $"Host=example;Database=aura_disposable_evidence;Username=reviewer;Password={secret}";
        output.Clear();
        check(await LiveAuditOutboxVerification.RunProductionAsync(Environment,
                output.Add, Fake) == 0 && runs == 1 && output.Count == 10 &&
            output.Take(9).All(x => x.EndsWith(": PASS", StringComparison.Ordinal)) &&
            output.Last() == "RESULT: PASS" &&
            output.All(x => !x.Contains(secret, StringComparison.Ordinal)),
            "15E opted-in evidence emits bounded PASS categories only after runner and cleanup PASS");
        output.Clear();
        check(await LiveAuditOutboxVerification.RunProductionAsync(Environment,
                output.Add, (_, _) => Task.FromResult<(bool, bool?)>((true, false))) == 1 &&
            output.SequenceEqual(["CLEANUP: FAIL", "RESULT: FAIL"]),
            "15E cleanup failure prevents overall PASS");
        output.Clear();
        check(await LiveAuditOutboxVerification.RunProductionAsync(Environment,
                output.Add, (_, _) => Task.FromResult<(bool, bool?)>((true, null))) == 1 &&
            output.SequenceEqual(["RESULT: FAIL"]),
            "15E missing cleanup result prevents overall PASS");
        output.Clear();
        check(await LiveAuditOutboxVerification.RunProductionAsync(Environment,
                output.Add, (_, _) => Task.FromResult<(bool, bool?)>((false, true))) == 1 &&
            output.SequenceEqual(["RESULT: FAIL"]),
            "15E failed evidence cannot pass with successful cleanup");
        output.Clear();
        check(await LiveAuditOutboxVerification.RunProductionAsync(Environment,
                output.Add, (_, _) => throw new InvalidOperationException(secret)) == 1 &&
            output.SequenceEqual(["RESULT: FAIL"]),
            "15E raw exception and secret are suppressed");

        var root = Directory.GetCurrentDirectory();
        string Source(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var entry = Source("security-checks", "Program.cs");
        var harness = Source("security-checks", "LiveAuditOutboxVerification.cs");
        var api = Source("Aura.Api", "Program.cs");
        var sink = Source("Aura.Infrastructure", "Services", "SecurityAuditPersistenceSink.cs");
        var dispatcher = Source("Aura.Infrastructure", "Services", "SecurityAuditOutboxDispatcher.cs");
        var stateStore = Source("Aura.Infrastructure", "Services", "ActionExecutionStatePersistenceStore.cs");
        var inspector = Source("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        check(entry.Contains("args.Length == 1 && args[0] == LiveAuditOutboxVerification.ProductionCommand") &&
            entry.Contains("await ProductionAuditEvidenceChecks.RunAsync(Check)") &&
            !api.Contains("LiveAuditOutboxVerification", StringComparison.Ordinal),
            "15E evidence command is explicit and normal suite stays offline");
        check(!harness.Contains("MigrateAsync", StringComparison.Ordinal) &&
            !harness.Contains("EnsureCreated", StringComparison.Ordinal) &&
            !harness.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase) &&
            !harness.Contains("Task.Delay", StringComparison.Ordinal) &&
            !harness.Contains("PeriodicTimer", StringComparison.Ordinal) &&
            !harness.Contains("while (", StringComparison.Ordinal) &&
            !harness.Contains("HttpClient", StringComparison.Ordinal) &&
            !harness.Contains("OpenAI", StringComparison.Ordinal) &&
            !harness.Contains("Ollama", StringComparison.Ordinal) &&
            harness.Contains("x.Id == id.Event") && harness.Contains("x.Id == id.Outbox") &&
            harness.Contains("x.ActionId == id.Action && x.UserId == id.User") &&
            harness.Contains("x.Id == id.User && x.AuthUserId == id.AuthUser"),
            "15E reused harness has exact generated cleanup and no migration, worker, retry or provider path");
        check(harness.Contains("DuplicateOutboxEventIdRejectedAsync") &&
            harness.Contains("CountAsync(x => x.AuditEventId == id.Event") &&
            harness.Contains("AuditOutboxDispatchStatus.Unavailable") &&
            harness.Contains("AuditOutboxDispatchStatus.NotPending") &&
            harness.Contains("StateAsync(factory, unavailable, SecurityAuditOutboxDeliveryState.Delivered"),
            "15E live harness covers unique event ID, pending recovery and terminal replay");
        check(sink.Contains("db.SecurityAuditEvents.Add") &&
            !sink.Contains("db.SecurityAuditOutbox.Add") &&
            stateStore.Contains("AddPendingAuditOutbox") &&
            stateStore.Contains("BeginTransactionAsync") &&
            dispatcher.Contains("BeginTransactionAsync") &&
            dispatcher.Contains("sink.WriteAsync") &&
            dispatcher.Contains("AuditSinkStatus.Unavailable") &&
            !dispatcher.Contains("while (", StringComparison.Ordinal),
            "15E direct sink and transactional outbox are distinct; dispatcher is one-item and explicit");
        check(api.Contains("AddStateAuditReconciliationRuntimeFoundation", StringComparison.Ordinal) &&
            !api.Contains("AddHostedService", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync", StringComparison.Ordinal) &&
            inspector.Contains("ReviewedAuditSinkLiveEvidence") &&
            inspector.Contains("ReviewedAuditDeliveryLiveEvidence") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).AuditSink &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).AuditDelivery &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().AuditSink &&
            !new RuntimeSafetyOptions().ProductionAdaptersEnabled &&
            !new RuntimeSafetyOptions().ToolExecutionEnabled,
            "17X prior live evidence cannot activate changed audit paths or execution");

        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only", npgsql => npgsql.UseVector()).Options;
        await using var db = new AuraDbContext(options);
        var model = db.GetService<IDesignTimeModel>().Model;
        var outbox = model.FindEntityType(typeof(SecurityAuditOutboxRecord))!;
        var deliverySql = outbox.GetCheckConstraints()
            .Single(x => x.Name == "CK_SecurityAuditOutbox_Delivery").Sql!;
        check(deliverySql.Contains("\"DeliveryState\" = 0 AND \"AttemptCount\" = 0 AND \"DeliveredAt\" IS NULL") &&
            deliverySql.Contains("\"DeliveryState\" = 1 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NOT NULL") &&
            deliverySql.Contains("\"DeliveryState\" = 2 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NULL"),
            "15E offline model pins pending, delivered and failed attempt/timestamp shapes");
        check(outbox.GetIndexes().Any(x => x.IsUnique && x.Properties.SingleOrDefault()?.Name == "AuditEventId") &&
            model.FindEntityType(typeof(SecurityAuditEventRecord))!.FindPrimaryKey()!
                .Properties.Single().Name == "Id",
            "15E offline model pins duplicate event and outbox ID rejection");
        check(dispatcher.Contains("row.DeliveryState !=", StringComparison.Ordinal) &&
            dispatcher.Contains("SecurityAuditOutboxDeliveryState.Pending", StringComparison.Ordinal) &&
            dispatcher.Contains("x.AttemptCount == 0", StringComparison.Ordinal) &&
            dispatcher.Contains("x.DeliveredAt == null", StringComparison.Ordinal) &&
            dispatcher.Contains("AuditOutboxDispatchStatus.NotPending", StringComparison.Ordinal) &&
            dispatcher.Contains("AuditOutboxDispatchStatus.Conflict", StringComparison.Ordinal),
            "15E terminal replay and delivered-to-pending reversion have no dispatcher path");
    }
}
