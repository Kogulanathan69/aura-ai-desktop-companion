using System.Reflection;
using Aura.Application.Actions;
using Aura.Application.Auditing;
using Aura.Application.Common.Interfaces;
using Aura.Application.Tools;
using Aura.Application.AI.Chat.DTOs;
using Aura.Application.ProjectFiles.Content;

internal static class AuditEventChecks
{
    public static async Task RunAsync(Action<bool, string> check, ActionDescriptor action)
    {
        var create = typeof(AuditWriteRequest).GetMethod("Create",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        AuditWriteRequest Request(AuditEventType type, AuditEventOutcome outcome,
            AuditEventReferences references, ActionScope? scope = null) =>
            (AuditWriteRequest)create.Invoke(null, [type, outcome, scope ?? action.Scope, references])!;
        var actionId = action.Id;
        var toolId = action.ToolId;
        var approvalId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var verificationId = Guid.NewGuid();
        var reconciliationId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var actionRefs = new AuditEventReferences(actionId, toolId);
        var approvalRefs = actionRefs with { ApprovalId = approvalId };
        var executionRefs = approvalRefs with { ExecutionId = executionId };
        var permissionRefs = new AuditEventReferences(ToolId: toolId, PermissionGrantId: grantId);
        var reconciliationRefs = new AuditEventReferences(ActionId: actionId,
            ExecutionId: executionId, ReconciliationId: reconciliationId);
        var verificationRefs = new AuditEventReferences(ActionId: actionId,
            ExecutionId: executionId, VerificationId: verificationId);
        var sink = new Sink { Result = AuditSinkStatus.Accepted };
        var clock = new Clock();
        var writer = new AuditEventWriter(sink, clock);
        var cases = new (AuditEventType Type, AuditEventOutcome Outcome, AuditEventReferences Refs)[]
        {
            (AuditEventType.ActionProposed, AuditEventOutcome.Success, actionRefs),
            (AuditEventType.ApprovalRequested, AuditEventOutcome.Success, actionRefs),
            (AuditEventType.ApprovalGranted, AuditEventOutcome.Success, approvalRefs),
            (AuditEventType.ApprovalRejected, AuditEventOutcome.Denied, actionRefs),
            (AuditEventType.PermissionAllowed, AuditEventOutcome.Success, permissionRefs),
            (AuditEventType.PermissionDenied, AuditEventOutcome.Denied,
                new AuditEventReferences(ToolId: toolId)),
            (AuditEventType.ExecutionReserved, AuditEventOutcome.Success, executionRefs),
            (AuditEventType.ExecutionCompleted, AuditEventOutcome.Success, executionRefs),
            (AuditEventType.ExecutionFailed, AuditEventOutcome.Failed, executionRefs),
            (AuditEventType.ExecutionReconciliationAssessed, AuditEventOutcome.Inconclusive,
                reconciliationRefs),
            (AuditEventType.VerificationAssessed, AuditEventOutcome.Inconclusive,
                verificationRefs),
            (AuditEventType.SecurityDenied, AuditEventOutcome.Denied,
                new AuditEventReferences())
        };
        foreach (var (type, outcome, references) in cases)
        {
            var result = await writer.WriteAsync(Request(type, outcome, references));
            check(result.Status == AuditWriteStatus.Success && result.Record?.EventType == type &&
                result.Record.Outcome == outcome && result.Record.Id.Value != Guid.Empty &&
                result.Record.Scope == action.Scope && result.Record.OccurredAt == clock.UtcNow &&
                sink.LastRecord == result.Record,
                "11W valid bounded event family " + type);
        }
        check(sink.Calls == cases.Length && Enum.GetValues<AuditEventType>().Length == cases.Length &&
            Enum.GetValues<AuditEventOutcome>().Length == 4,
            "11W all bounded event types and outcomes represented");
        var record = sink.LastRecord!;
        check(typeof(AuditEventIdentifier).GetConstructors().Length == 0 &&
            typeof(AuditEventIdentifier).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .All(x => x.ReturnType != typeof(AuditEventIdentifier)) &&
            typeof(AuditEventRecord).GetConstructors().Length == 0 &&
            typeof(AuditWriteRequest).GetConstructors().Length == 0 &&
            typeof(AuditWriteRequest).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .All(x => x.ReturnType != typeof(AuditWriteRequest)),
            "11W event ID and internal request have no public constructor or minting factory");
        check(typeof(AuditEventRecord).GetProperties().All(x => x.SetMethod is null) &&
            typeof(AuditEventRecord).GetProperties().Select(x => x.Name).SequenceEqual(
                ["Id", "EventType", "Outcome", "Scope", "ActionId", "ToolId", "ApprovalId",
                    "ExecutionId", "VerificationId", "ReconciliationId", "PermissionGrantId",
                    "OccurredAt"]) &&
            typeof(AuditEventRecord).GetProperties().All(x => x.PropertyType != typeof(string)) &&
            typeof(AuditEventReferences).GetProperties().All(x => x.PropertyType != typeof(string)),
            "11W immutable record has only fixed references, no text or payload field");
        check((await writer.WriteAsync(Request((AuditEventType)999, AuditEventOutcome.Success,
                actionRefs))).Status == AuditWriteStatus.InvalidEvent &&
            (await writer.WriteAsync(Request(AuditEventType.ActionProposed,
                (AuditEventOutcome)999, actionRefs))).Status == AuditWriteStatus.InvalidEvent,
            "11W unknown event and outcome enums fail closed");
        foreach (var scope in new[] { action.Scope with { UserId = Guid.Empty },
            action.Scope with { ConversationId = Guid.Empty },
            action.Scope with { ProjectId = Guid.Empty } })
            check((await writer.WriteAsync(Request(AuditEventType.ActionProposed,
                AuditEventOutcome.Success, actionRefs, scope))).Status == AuditWriteStatus.InvalidScope,
                "11W empty user/conversation/project scope denied");
        foreach (var malformed in new[]
        {
            actionRefs with { ApprovalId = Guid.Empty },
            actionRefs with { ExecutionId = Guid.Empty },
            actionRefs with { VerificationId = Guid.Empty },
            actionRefs with { ReconciliationId = Guid.Empty },
            actionRefs with { PermissionGrantId = Guid.Empty }
        })
            check((await writer.WriteAsync(Request(AuditEventType.ActionProposed,
                AuditEventOutcome.Success, malformed))).Status == AuditWriteStatus.InvalidBinding,
                "11W empty optional reference denied");
        var invalidCases = new (AuditEventType Type, AuditEventOutcome Outcome, AuditEventReferences Refs)[]
        {
            (AuditEventType.ApprovalGranted, AuditEventOutcome.Success, actionRefs),
            (AuditEventType.PermissionAllowed, AuditEventOutcome.Success,
                new AuditEventReferences(ToolId: toolId)),
            (AuditEventType.ExecutionReserved, AuditEventOutcome.Success, approvalRefs),
            (AuditEventType.ExecutionCompleted, AuditEventOutcome.Success, approvalRefs),
            (AuditEventType.ExecutionFailed, AuditEventOutcome.Success, executionRefs),
            (AuditEventType.VerificationAssessed, AuditEventOutcome.Inconclusive,
                verificationRefs with { VerificationId = null }),
            (AuditEventType.ExecutionReconciliationAssessed, AuditEventOutcome.Inconclusive,
                reconciliationRefs with { ReconciliationId = null }),
            (AuditEventType.ActionProposed, AuditEventOutcome.Success,
                actionRefs with { ExecutionId = executionId }),
            (AuditEventType.SecurityDenied, AuditEventOutcome.Success,
                new AuditEventReferences())
        };
        foreach (var (type, outcome, references) in invalidCases)
            check((await writer.WriteAsync(Request(type, outcome, references))).Status ==
                AuditWriteStatus.InvalidBinding,
                "11W invalid event combination denied " + type);
        foreach (var verificationOutcome in new[] { AuditEventOutcome.Success, AuditEventOutcome.Failed })
            check((await writer.WriteAsync(Request(AuditEventType.VerificationAssessed,
                verificationOutcome, new AuditEventReferences()))).Status == AuditWriteStatus.InvalidBinding,
                "11W verification outcome still requires exact action/execution/verification refs");
        var callsBefore = sink.Calls;
        check((await writer.WriteAsync(Request(AuditEventType.ExecutionReserved,
                AuditEventOutcome.Success, new AuditEventReferences()))).Status ==
                AuditWriteStatus.InvalidBinding && sink.Calls == callsBefore,
            "11W malformed event never reaches sink");
        var unavailable = await new AuditEventWriter(new UnavailableAuditEventSink(), clock)
            .WriteAsync(Request(AuditEventType.ActionProposed, AuditEventOutcome.Success, actionRefs));
        check(unavailable.Status == AuditWriteStatus.Unavailable && unavailable.Record is null,
            "11W default sink unavailable and claims no persistence");
        sink.Result = AuditSinkStatus.Failed;
        var failed = await writer.WriteAsync(Request(AuditEventType.ActionProposed,
            AuditEventOutcome.Success, actionRefs));
        check(failed.Status == AuditWriteStatus.Failed && failed.Record is null,
            "11W sink failure returns no accepted record");
        sink.ThrowFailure = true;
        failed = await writer.WriteAsync(Request(AuditEventType.ActionProposed,
            AuditEventOutcome.Success, actionRefs));
        check(failed.Status == AuditWriteStatus.Failed && !failed.Message.Contains("secret"),
            "11W raw sink exception maps fixed safe failure");
        sink.ThrowFailure = false;
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); var observed = false;
            try { await writer.WriteAsync(Request(AuditEventType.ActionProposed,
                AuditEventOutcome.Success, actionRefs), cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed, "11W cancellation before sink propagates");
        }
        using (var cancelled = new CancellationTokenSource())
        {
            sink.Cancel = cancelled; var observed = false;
            try { await writer.WriteAsync(Request(AuditEventType.ActionProposed,
                AuditEventOutcome.Success, actionRefs), cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed, "11W cancellation during sink propagates");
            sink.Cancel = null;
        }
        clock.Invalid = true;
        check((await writer.WriteAsync(Request(AuditEventType.ActionProposed,
            AuditEventOutcome.Success, actionRefs))).Status == AuditWriteStatus.InvalidEvent,
            "11W invalid server timestamp denied");
        check(typeof(AuditEventWriter).GetConstructors().Single().GetParameters()
                .Select(x => x.ParameterType).SequenceEqual([typeof(IAuditEventSink), typeof(IDateTimeProvider)]) &&
            typeof(AiChatRequest).GetProperties().Select(x => x.Name).SequenceEqual(["Prompt"]) &&
            !new ProjectFileAccessOptions().Enabled,
            "11W writer has sink/clock only; chat Prompt-only and file access disabled");
        foreach (var status in Enum.GetValues<AuditWriteStatus>())
            check(AuditWriteResult.Denied(status).Message.Length <= 80 &&
                AuditWriteResult.Denied(status).Record is null,
                "11W fixed bounded denial " + status);
        check(AuditWriteResult.Denied(AuditWriteStatus.Success).Status == AuditWriteStatus.Failed,
            "11W public denial factory cannot fabricate success");
    }

    private sealed class Sink : IAuditEventSink
    {
        public AuditSinkStatus Result;
        public AuditEventRecord? LastRecord;
        public int Calls;
        public bool ThrowFailure;
        public CancellationTokenSource? Cancel;
        public Task<AuditSinkStatus> WriteAsync(AuditEventRecord record, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++; LastRecord = record;
            if (ThrowFailure) throw new InvalidOperationException("secret sink error");
            Cancel?.Cancel(); return Task.FromResult(Result);
        }
    }
    private sealed class Clock : IDateTimeProvider
    {
        public bool Invalid;
        public DateTime UtcNow => Invalid ? default :
            new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
    }
}
