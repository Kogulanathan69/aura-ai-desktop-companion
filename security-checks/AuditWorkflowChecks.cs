using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Auditing;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Privacy.Services;
using Aura.Application.Tools;
using Aura.Application.Verifications;
using Aura.Application.AI.Chat.DTOs;
using Aura.Application.ProjectFiles.Content;

internal static class AuditWorkflowChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ToolIdentifier.TryCreate("audit-fixture", out var toolId);
        var tool = new ToolDescriptor(toolId!, "Fixture", "Synthetic fixture",
            ToolCapability.ProjectRead, ToolPermissionRequirement.OwnedProjectRead);
        var handler = new Handler();
        var registry = new ToolRegistry([new(tool, handler)]);
        var scope = new ActionScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var owner = new Owner(scope);
        var clock = new Clock();
        var sink = new Sink();
        var audit = new AuditEventWriter(sink, clock);
        var lifecycle = new ActionLifecycleService(registry, owner, clock, new PrivacyGuard());
        async Task<ActionDescriptor> Waiting()
        {
            var proposed = (await lifecycle.ProposeAsync(scope, new(tool.Id, "Synthetic action"))).Action!;
            return (await lifecycle.TransitionAsync(scope, proposed,
                ActionLifecycleStatus.ApprovalRequired)).Action!;
        }
        var waiting = await Waiting();
        var approvalWorkflow = new ApprovalWorkflowService(registry, owner, clock, audit);
        var approvedResult = await approvalWorkflow.DecideAsync(scope, waiting, new(ApprovalDecision.Approve));
        var approved = approvedResult.Action!;
        var approval = approvedResult.Evidence!;
        check(approvedResult.Status == ApprovalOperationStatus.Success && sink.Count == 1 &&
            sink.At(0).EventType == AuditEventType.ApprovalGranted &&
            sink.At(0).Outcome == AuditEventOutcome.Success &&
            sink.At(0).Scope == scope && sink.At(0).ActionId == approved.Id &&
            sink.At(0).ToolId == tool.Id && sink.At(0).ApprovalId == approval.Id.Value,
            "11X approval success emits one exact ApprovalGranted after evidence exists");
        sink.Reset();
        var rejected = await approvalWorkflow.DecideAsync(scope, await Waiting(), new(ApprovalDecision.Reject));
        check(rejected.Status == ApprovalOperationStatus.Success && rejected.Evidence is null &&
            sink.Count == 1 && sink.At(0).EventType == AuditEventType.ApprovalRejected &&
            sink.At(0).Outcome == AuditEventOutcome.Denied && sink.At(0).ApprovalId is null &&
            sink.At(0).ActionId == rejected.Action?.Id && sink.At(0).ToolId == tool.Id,
            "11X rejection emits one Denied event without fabricated approval ID");
        sink.Result = AuditSinkStatus.Unavailable; sink.Reset();
        var unavailableApproval = await approvalWorkflow.DecideAsync(scope, await Waiting(),
            new(ApprovalDecision.Approve));
        check(unavailableApproval.Status == ApprovalOperationStatus.Success &&
            unavailableApproval.Action?.Status == ActionLifecycleStatus.Approved &&
            unavailableApproval.Evidence is not null && sink.Count == 1,
            "11X unavailable audit sink does not change approval outcome");
        sink.Result = AuditSinkStatus.Accepted; sink.ThrowFailure = true; sink.Reset();
        var throwingApproval = await approvalWorkflow.DecideAsync(scope, await Waiting(),
            new(ApprovalDecision.Reject));
        check(throwingApproval.Status == ApprovalOperationStatus.Success &&
            throwingApproval.Action?.Status == ActionLifecycleStatus.Rejected && sink.Count == 1,
            "11X sink exception does not change rejection outcome or retry audit");
        sink.ThrowFailure = false;

        var grant = new ToolPermissionGrant(Guid.NewGuid(), scope, tool.Id,
            tool.RequiredPermission, true, false);
        var permissionSource = new PermissionSource { Lookup = new(ToolPermissionLookupStatus.Found, grant) };
        var permission = new ToolExecutionPermissionValidator(permissionSource,
            new ExactToolPermissionPolicy(), audit);
        sink.Reset();
        check(await permission.HasPermissionAsync(scope, tool, CancellationToken.None) &&
            sink.Count == 1 && sink.At(0).EventType == AuditEventType.PermissionAllowed &&
            sink.At(0).Scope == scope && sink.At(0).ToolId == tool.Id &&
            sink.At(0).PermissionGrantId == grant.GrantId &&
            sink.At(0).Outcome == AuditEventOutcome.Success,
            "11X Allowed emits one exact PermissionAllowed with trusted grant ID");
        permissionSource.Lookup = new(ToolPermissionLookupStatus.NotFound, null); sink.Reset();
        check(!await permission.HasPermissionAsync(scope, tool, CancellationToken.None) &&
            sink.Count == 1 && sink.At(0).EventType == AuditEventType.PermissionDenied &&
            sink.At(0).PermissionGrantId is null && sink.At(0).Outcome == AuditEventOutcome.Denied,
            "11X missing permission emits Denied without invented grant ID");
        permissionSource.Lookup = new(ToolPermissionLookupStatus.Found, grant with { IsRevoked = true });
        sink.Reset();
        check(!await permission.HasPermissionAsync(scope, tool, CancellationToken.None) &&
            sink.Count == 1 && sink.At(0).EventType == AuditEventType.PermissionDenied &&
            sink.At(0).PermissionGrantId == grant.GrantId,
            "11X revoked permission emits Denied with exact trusted grant ID");
        permissionSource.Lookup = new(ToolPermissionLookupStatus.Found, grant); sink.Result = AuditSinkStatus.Unavailable;
        sink.Reset();
        check(await permission.HasPermissionAsync(scope, tool, CancellationToken.None) && sink.Count == 1,
            "11X unavailable audit sink does not convert Allowed to Denied");
        permissionSource.Lookup = new(ToolPermissionLookupStatus.NotFound, null); sink.ThrowFailure = true;
        sink.Reset();
        check(!await permission.HasPermissionAsync(scope, tool, CancellationToken.None) && sink.Count == 1,
            "11X sink exception does not convert Denied to Allowed");
        sink.ThrowFailure = false; sink.Result = AuditSinkStatus.Accepted;
        permissionSource.Lookup = new(ToolPermissionLookupStatus.Found, grant);

        var execution = new TrustedToolExecutionService(registry, owner, permission,
            new AllowPolicy(), clock, new AtomicExecutionStore(), audit);
        permissionSource.Lookup = new(ToolPermissionLookupStatus.NotFound, null); sink.Reset();
        var deniedStore = new AtomicExecutionStore();
        var deniedExecution = await new TrustedToolExecutionService(registry, owner, permission,
            new AllowPolicy(), clock, deniedStore, audit)
            .ExecuteAsync(scope, approved, approval, new ExecutionRequest(approved.Id));
        check(deniedExecution.Status == ExecutionOperationStatus.PermissionDenied &&
            handler.Calls == 0 && deniedStore.Reservations == 0 && sink.Count == 1 &&
            sink.At(0).EventType == AuditEventType.PermissionDenied,
            "11X permission denial emits no false execution reservation or handler call");
        permissionSource.Lookup = new(ToolPermissionLookupStatus.Found, grant);
        sink.Reset();
        var successful = await execution.ExecuteAsync(scope, approved, approval,
            new ExecutionRequest(approved.Id));
        check(successful.Status == ExecutionOperationStatus.Success && handler.Calls == 1 &&
            sink.Count == 3 && sink.At(0).EventType == AuditEventType.PermissionAllowed &&
            sink.At(1).EventType == AuditEventType.ExecutionReserved &&
            sink.At(2).EventType == AuditEventType.ExecutionCompleted &&
            sink.At(1).ExecutionId == successful.Evidence?.Id.Value &&
            sink.At(2).ExecutionId == successful.Evidence?.Id.Value &&
            sink.At(1).ActionId == approved.Id && sink.At(1).ToolId == tool.Id &&
            sink.At(1).ApprovalId == approval.Id.Value && sink.At(1).Scope == scope,
            "11X execution emits reserve then complete with same exact evidence ID");
        sink.Reset();
        var replay = await execution.ExecuteAsync(scope, approved, approval,
            new ExecutionRequest(approved.Id));
        check(replay.Status == ExecutionOperationStatus.StateConflict && handler.Calls == 1 &&
            sink.Count == 1 && sink.At(0).EventType == AuditEventType.PermissionAllowed,
            "11X replay emits no false reservation/completion or handler retry");
        handler.Fail = true; sink.Reset();
        var failedExecution = await new TrustedToolExecutionService(registry, owner, permission,
            new AllowPolicy(), clock, new AtomicExecutionStore(), audit)
            .ExecuteAsync(scope, approved, approval, new ExecutionRequest(approved.Id));
        check(failedExecution.Status == ExecutionOperationStatus.ExecutionFailed &&
            sink.Count == 3 && sink.At(1).EventType == AuditEventType.ExecutionReserved &&
            sink.At(2).EventType == AuditEventType.ExecutionFailed &&
            sink.At(2).Outcome == AuditEventOutcome.Failed &&
            sink.At(2).ExecutionId == failedExecution.Evidence?.Id.Value,
            "11X durable Failed emits ExecutionFailed, not completed success");
        handler.Fail = false; sink.Reset();
        var uncertainStore = new AtomicExecutionStore { CompleteOverride = ActionExecutionStateResultStatus.Unavailable };
        var uncertain = await new TrustedToolExecutionService(registry, owner, permission,
            new AllowPolicy(), clock, uncertainStore, audit)
            .ExecuteAsync(scope, approved, approval, new ExecutionRequest(approved.Id));
        check(uncertain.Status == ExecutionOperationStatus.ReconciliationRequired &&
            sink.Count == 2 && sink.At(1).EventType == AuditEventType.ExecutionReserved &&
            uncertainStore.State?.Status == ActionExecutionStateStatus.Reserved,
            "11X uncertain completion emits no false completed/failed event");
        sink.Result = AuditSinkStatus.Unavailable; sink.Reset();
        var beforeHandler = handler.Calls;
        var noAuditPersistence = await new TrustedToolExecutionService(registry, owner, permission,
            new AllowPolicy(), clock, new AtomicExecutionStore(), audit)
            .ExecuteAsync(scope, approved, approval, new ExecutionRequest(approved.Id));
        check(noAuditPersistence.Status == ExecutionOperationStatus.Success &&
            handler.Calls == beforeHandler + 1 && sink.Count == 3,
            "11X unavailable sink leaves durable execution and handler count unchanged");
        sink.Result = AuditSinkStatus.Accepted; sink.ThrowFailure = true; sink.Reset();
        beforeHandler = handler.Calls;
        var throwAudit = await new TrustedToolExecutionService(registry, owner, permission,
            new AllowPolicy(), clock, new AtomicExecutionStore(), audit)
            .ExecuteAsync(scope, approved, approval, new ExecutionRequest(approved.Id));
        check(throwAudit.Status == ExecutionOperationStatus.Success &&
            handler.Calls == beforeHandler + 1 && sink.Count == 3,
            "11X sink exception leaves execution outcome and handler count unchanged");
        sink.ThrowFailure = false;
        using (var cancelled = new CancellationTokenSource())
        {
            var reserveStore = new AtomicExecutionStore();
            var noPermissionAudit = new ToolExecutionPermissionValidator(permissionSource,
                new ExactToolPermissionPolicy());
            sink.Cancel = cancelled; sink.Reset();
            var callsBeforeCancellation = handler.Calls;
            var observed = false;
            try
            {
                await new TrustedToolExecutionService(registry, owner, noPermissionAudit,
                    new AllowPolicy(), clock, reserveStore, audit)
                    .ExecuteAsync(scope, approved, approval, new ExecutionRequest(approved.Id),
                        cancelled.Token);
            }
            catch (OperationCanceledException) { observed = true; }
            check(observed && reserveStore.Reservations == 1 &&
                reserveStore.State?.Status == ActionExecutionStateStatus.Reserved &&
                handler.Calls == callsBeforeCancellation && sink.Count == 1 &&
                sink.At(0).EventType == AuditEventType.ExecutionReserved,
                "11X cancellation during reserve audit leaves Reserved state and no handler");
            sink.Cancel = null;
        }

        var ready = ActionExecutionState.CreateReady(approved, approval)!;
        var reconciliationExecutionId = Guid.NewGuid();
        var reserved = ready with { Status = ActionExecutionStateStatus.Reserved, Version = 1,
            ExecutionId = reconciliationExecutionId,
            ApprovalConsumedByExecutionId = reconciliationExecutionId };
        var reconciliationSource = new ReconciliationSource(new(reserved, approved.Id, scope,
            tool.Id, approval.Id.Value, reconciliationExecutionId, 1,
            ExecutionReconciliationReason.Unknown));
        sink.Reset();
        var reconciliation = await new ExecutionReconciliationService(owner,
            reconciliationSource, new ConservativeExecutionReconciliationPolicy(), clock, audit)
            .ReconcileAsync(scope, new(approved.Id));
        check(reconciliation.Status == ExecutionReconciliationStatus.Success && sink.Count == 1 &&
            sink.At(0).EventType == AuditEventType.ExecutionReconciliationAssessed &&
            sink.At(0).Outcome == AuditEventOutcome.Inconclusive &&
            sink.At(0).ReconciliationId == reconciliation.Evidence?.Id.Value &&
            sink.At(0).ExecutionId == reconciliationExecutionId &&
            reconciliationSource.Value.State.Status == ActionExecutionStateStatus.Reserved,
            "11X reconciliation assessment emits Inconclusive without state mutation");

        var verificationSource = new VerificationSource(new ExecutionEvidenceInput(
            Guid.NewGuid(), approved.Id, scope, tool.Id, approval.Id.Value,
            ExecutionReportOutcome.Succeeded, clock.UtcNow));
        var proof = new Proof();
        foreach (var (verificationOutcome, auditOutcome) in new[]
        {
            (VerificationOutcome.Verified, AuditEventOutcome.Success),
            (VerificationOutcome.Failed, AuditEventOutcome.Failed),
            (VerificationOutcome.Inconclusive, AuditEventOutcome.Inconclusive)
        })
        {
            proof.Result = verificationOutcome; sink.Reset();
            var verified = await new VerificationWorkflowService(registry, owner,
                verificationSource, proof, clock, audit)
                .VerifyAsync(scope, approved, approval,
                    new VerificationRequest(verificationSource.Value.ExecutionId));
            check(verified.Status == VerificationOperationStatus.Success &&
                verified.Evidence?.Outcome == verificationOutcome && sink.Count == 1 &&
                sink.At(0).EventType == AuditEventType.VerificationAssessed &&
                sink.At(0).Outcome == auditOutcome &&
                sink.At(0).VerificationId == verified.Evidence.Id.Value &&
                sink.At(0).ExecutionId == verificationSource.Value.ExecutionId,
                "11X verification " + verificationOutcome + " emits exact mapped audit outcome");
        }
        sink.Result = AuditSinkStatus.Unavailable; sink.Reset();
        proof.Result = VerificationOutcome.Inconclusive;
        var withoutAudit = await new VerificationWorkflowService(registry, owner,
            verificationSource, proof, clock, audit)
            .VerifyAsync(scope, approved, approval,
                new VerificationRequest(verificationSource.Value.ExecutionId));
        check(withoutAudit.Status == VerificationOperationStatus.Success &&
            withoutAudit.Evidence?.Outcome == VerificationOutcome.Inconclusive && sink.Count == 1,
            "11X unavailable audit sink does not change verification outcome");
        sink.Result = AuditSinkStatus.Accepted; sink.ThrowFailure = true; sink.Reset();
        var throwVerification = await new VerificationWorkflowService(registry, owner,
            verificationSource, proof, clock, audit)
            .VerifyAsync(scope, approved, approval,
                new VerificationRequest(verificationSource.Value.ExecutionId));
        check(throwVerification.Status == VerificationOperationStatus.Success &&
            throwVerification.Evidence?.Outcome == VerificationOutcome.Inconclusive && sink.Count == 1,
            "11X sink exception does not change verification outcome");
        sink.ThrowFailure = false;

        using (var cancelled = new CancellationTokenSource())
        {
            sink.Cancel = cancelled; sink.Reset();
            var observed = false;
            try { await approvalWorkflow.DecideAsync(scope, await Waiting(),
                new(ApprovalDecision.Approve), cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed && sink.Count == 1,
                "11X cancellation during post-decision audit propagates without rollback claim");
            sink.Cancel = null;
        }
        check(typeof(AuditEventRecord).GetProperties().All(x => x.PropertyType != typeof(string)) &&
            typeof(AiChatRequest).GetProperties().Select(x => x.Name).SequenceEqual(["Prompt"]) &&
            !new ProjectFileAccessOptions().Enabled,
            "11X no prompt/chat/output string channel; file access remains disabled");
    }

    private sealed class Sink : IAuditEventSink
    {
        private readonly AuditEventRecord?[] records = new AuditEventRecord?[4];
        public int Count { get; private set; }
        public AuditSinkStatus Result = AuditSinkStatus.Accepted;
        public bool ThrowFailure;
        public CancellationTokenSource? Cancel;
        public AuditEventRecord At(int index) => records[index]!;
        public void Reset() { Count = 0; Array.Clear(records); }
        public Task<AuditSinkStatus> WriteAsync(AuditEventRecord record, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Count == records.Length) throw new InvalidOperationException("Too many test audit events.");
            records[Count++] = record;
            if (ThrowFailure) throw new InvalidOperationException("secret sink failure");
            Cancel?.Cancel();
            return Task.FromResult(Result);
        }
    }
    private sealed class Handler : IToolHandler
    {
        public int Calls;
        public bool Fail;
        public Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            return Task.FromResult(ToolExecutionResult.FromStatus(Fail
                ? ToolExecutionStatus.Failed : ToolExecutionStatus.Success));
        }
    }
    private sealed class Owner(ActionScope scope) : IActionScopeValidator,
        IActionApprovalScopeValidator, IToolExecutionScopeValidator, IVerificationScopeValidator,
        IExecutionReconciliationScopeValidator
    {
        public Task<bool> ValidateAsync(ActionScope value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(value == scope); }
    }
    private sealed class Clock : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
    }
    private sealed class PermissionSource : IToolPermissionSource
    {
        public ToolPermissionLookupResult Lookup = new(ToolPermissionLookupStatus.NotFound, null);
        public Task<ToolPermissionLookupResult> GetAsync(ToolPermissionValidationRequest request,
            CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(Lookup); }
    }
    private sealed class AllowPolicy : IToolExecutionPolicy
    {
        public Task<bool> AllowsAsync(ToolDescriptor tool, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(true); }
    }
    private sealed class ReconciliationSource(ExecutionReconciliationSnapshot value) : IExecutionReconciliationSource
    {
        public ExecutionReconciliationSnapshot Value = value;
        public Task<ExecutionReconciliationSnapshot?> GetAsync(ActionScope scope, ActionIdentifier actionId,
            CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<ExecutionReconciliationSnapshot?>(Value); }
    }
    private sealed class VerificationSource(ExecutionEvidenceInput value) : IExecutionEvidenceSource
    {
        public ExecutionEvidenceInput Value = value;
        public Task<ExecutionEvidenceInput?> GetAsync(ActionScope scope, Guid executionId,
            CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<ExecutionEvidenceInput?>(Value); }
    }
    private sealed class Proof : IIndependentVerificationPolicy
    {
        public VerificationOutcome Result;
        public Task<VerificationOutcome> EvaluateAsync(ExecutionEvidenceInput execution,
            CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(Result); }
    }
}
