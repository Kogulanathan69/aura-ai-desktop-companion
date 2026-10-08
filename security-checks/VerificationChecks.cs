using System.Text.Json;
using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Verifications;
using Aura.Application.Tools;
using Aura.Application.Common.Interfaces;
using Aura.Application.Privacy.Services;
using Aura.Application.ProjectFiles.Content;
using Aura.Application.AI.Chat.DTOs;
using Aura.Application.AI.Chat.Services;
using Aura.Application.AI.Providers;

internal static class VerificationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ToolIdentifier.TryCreate("fixture", out var toolId);
        ToolIdentifier.TryCreate("other", out var otherTool);
        var handler = new NeverCalledHandler();
        var tools = new ToolRegistry([new(new(toolId!, "Fixture", "Synthetic fixture", ToolCapability.ProjectRead,
            ToolPermissionRequirement.OwnedProjectRead), handler)]);
        var scope = new ActionScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var owner = new ScopeFixture(scope);
        var clock = new ClockFixture();
        var lifecycle = new ActionLifecycleService(tools, owner, clock, new PrivacyGuard());
        var proposed = (await lifecycle.ProposeAsync(scope, new(toolId!, "Inspect project"))).Action!;
        var waiting = (await lifecycle.TransitionAsync(scope, proposed, ActionLifecycleStatus.ApprovalRequired)).Action!;
        var approvalResult = await new ApprovalWorkflowService(tools, owner, clock).DecideAsync(scope, waiting, new(ApprovalDecision.Approve));
        var action = approvalResult.Action!;
        var approval = approvalResult.Evidence!;
        var request = new VerificationRequest(Guid.NewGuid());
        // Synthetic bounded adapter data, not a ToolExecution/entity or fabricated executing action.
        var input = new ExecutionEvidenceInput(request.ExecutionId, action.Id, scope, toolId!, approval.Id.Value,
            ExecutionReportOutcome.Succeeded, clock.UtcNow);
        var source = new SourceFixture { Input = input };
        var proof = new ProofFixture();
        var workflow = new VerificationWorkflowService(tools, owner, source, proof, clock);
        owner.Calls = 0;
        var result = await workflow.VerifyAsync(scope, action, approval, request);
        var evidence = result.Evidence!;
        check(result.Status == VerificationOperationStatus.Success && evidence.Outcome == VerificationOutcome.Verified,
            "11Q explicit synthetic independent-proof boundary can issue verification assessment");
        check(evidence.Id.Value != Guid.Empty && evidence.IssuedAt == clock.UtcNow, "11Q ID and issue timestamp server-generated");
        check(evidence.ActionId == action.Id, "11Q exact action evidence binding");
        check(evidence.Scope.UserId == scope.UserId, "11Q exact user evidence binding");
        check(evidence.Scope.ConversationId == scope.ConversationId, "11Q exact conversation evidence binding");
        check(evidence.Scope.ProjectId == scope.ProjectId, "11Q exact project evidence binding");
        check(evidence.ToolId == toolId && evidence.ExecutionId == request.ExecutionId && evidence.ApprovalId == approval.Id.Value,
            "11Q exact tool/execution/approval evidence bindings");
        check(evidence.MatchesBinding(action.Id, scope, toolId, approval.Id.Value, request.ExecutionId), "11Q positive exact binding");
        check(owner.Calls == 2 && source.Scope == scope && source.Id == request.ExecutionId && source.Calls == 1 && proof.Calls == 1,
            "11Q scope freshly validated twice, source scoped, no retry/fallback");
        check(typeof(VerificationEvidence).GetConstructors().Length == 0 && typeof(VerificationIdentifier).GetConstructors().Length == 0 &&
            typeof(VerificationEvidence).GetProperties().All(x => x.SetMethod is null),
            "11Q immutable evidence/identifier cannot be constructed by caller");
        var deniedJson = false;
        try { _ = JsonSerializer.Deserialize<VerificationEvidence>("{\"Outcome\":0,\"IssuedAt\":\"2026-10-07T00:00:00Z\"}"); }
        catch (NotSupportedException) { deniedJson = true; }
        check(deniedJson, "11Q caller JSON cannot fabricate evidence");
        check(typeof(VerificationRequest).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "ExecutionId" }),
            "11Q request excludes outcomes, proof, IDs/timestamps/scope/text/payload selectors");
        check(typeof(ExecutionEvidenceInput).GetProperties().Select(x => x.Name).SequenceEqual(
            new[] { "ExecutionId", "ActionId", "Scope", "ToolId", "ApprovalId", "Outcome", "CompletedAt" }),
            "11Q execution adapter DTO has only fixed fields, no logs/contents/commands/secrets/JSON/facts");
        foreach (var foreign in new[] { scope with { UserId = Guid.NewGuid() }, scope with { ConversationId = Guid.NewGuid() },
            scope with { ProjectId = Guid.NewGuid() } })
        {
            check(!evidence.MatchesBinding(action.Id, foreign, toolId, approval.Id.Value, request.ExecutionId),
                "11Q evidence rejects foreign user/conversation/project");
            var denied = await workflow.VerifyAsync(foreign, action, approval, request);
            check(denied.Status == VerificationOperationStatus.ScopeDenied && denied.Evidence is null,
                "11Q exact scope mismatch denied without evidence leakage");
        }
        var otherAction = (await lifecycle.ProposeAsync(scope, new(toolId!, "Other action"))).Action!;
        check(!evidence.MatchesBinding(otherAction.Id, scope, toolId, approval.Id.Value, request.ExecutionId), "11Q another action binding denied");
        check(!evidence.MatchesBinding(action.Id, scope, otherTool, approval.Id.Value, request.ExecutionId), "11Q another tool binding denied");
        check(!evidence.MatchesBinding(action.Id, scope, toolId, approval.Id.Value, Guid.NewGuid()), "11Q another execution binding denied");
        check(!evidence.MatchesBinding(action.Id, scope, toolId, Guid.NewGuid(), request.ExecutionId), "11Q another approval binding denied");
        foreach (var invalid in new[] { scope with { UserId = Guid.Empty }, scope with { ConversationId = Guid.Empty },
            scope with { ProjectId = Guid.Empty }, null! })
            check((await workflow.VerifyAsync(invalid, action, approval, request)).Status == VerificationOperationStatus.InvalidRequest,
                "11Q empty/missing scope fails closed");
        check((await workflow.VerifyAsync(scope, null!, approval, request)).Status == VerificationOperationStatus.InvalidRequest &&
            (await workflow.VerifyAsync(scope, action, approval, null!)).Status == VerificationOperationStatus.InvalidRequest &&
            (await workflow.VerifyAsync(scope, action, approval, new(Guid.Empty))).Status == VerificationOperationStatus.InvalidRequest,
            "11Q missing action/request/execution reference fails closed");
        check((await workflow.VerifyAsync(scope, action, null!, request)).Status == VerificationOperationStatus.ApprovalDenied,
            "11Q actual approved evidence required, ID alone insufficient");
        foreach (var state in Enum.GetValues<ActionLifecycleStatus>())
            check(VerificationContextPolicy.IsEligibleContext(state) == (state == ActionLifecycleStatus.Approved),
                "11Q exact foundation context eligibility " + state);
        foreach (var invalidAction in new[] { proposed, waiting,
            (await lifecycle.TransitionAsync(scope, action, ActionLifecycleStatus.Cancelled)).Action! })
            check((await workflow.VerifyAsync(scope, invalidAction, approval, request)).Status == VerificationOperationStatus.InvalidActionState,
                "11Q non-Approved context denied without fake execution state");
        var otherWaiting = (await lifecycle.TransitionAsync(scope, otherAction, ActionLifecycleStatus.ApprovalRequired)).Action!;
        var otherApproval = await new ApprovalWorkflowService(tools, owner, clock).DecideAsync(scope, otherWaiting, new(ApprovalDecision.Approve));
        check((await workflow.VerifyAsync(scope, action, otherApproval.Evidence!, request)).Status == VerificationOperationStatus.ApprovalDenied,
            "11Q foreign action approval cannot authorize verification context");
        check((await new VerificationWorkflowService(new ToolRegistry([]), owner, source, proof, clock)
            .VerifyAsync(scope, action, approval, request)).Status == VerificationOperationStatus.UnknownTool, "11Q unknown tool denied");
        foreach (var bad in new[] { input with { ExecutionId = Guid.NewGuid() }, input with { ActionId = otherAction.Id },
            input with { Scope = scope with { UserId = Guid.NewGuid() } }, input with { Scope = scope with { ProjectId = Guid.NewGuid() } },
            input with { Scope = scope with { ConversationId = Guid.NewGuid() } }, input with { ToolId = otherTool! },
            input with { ApprovalId = Guid.NewGuid() }, input with { Outcome = (ExecutionReportOutcome)99 },
            input with { CompletedAt = default }, input with { CompletedAt = clock.UtcNow.AddDays(-1) },
            input with { CompletedAt = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Unspecified) },
            input with { ActionId = null! }, input with { Scope = null! }, input with { ToolId = null! } })
        {
            source.Input = bad;
            check((await workflow.VerifyAsync(scope, action, approval, request)).Status == VerificationOperationStatus.EvidenceDenied,
                "11Q malformed/mismatched/untrusted execution adapter input fails closed");
        }
        source.Input = null;
        check((await workflow.VerifyAsync(scope, action, approval, request)).Status == VerificationOperationStatus.ExecutionUnavailable,
            "11Q approval/registration alone produce no verification");
        check((await new VerificationWorkflowService(tools, owner, new UnavailableExecutionEvidenceSource(), proof, clock)
            .VerifyAsync(scope, action, approval, request)).Status == VerificationOperationStatus.ExecutionUnavailable,
            "11Q supplied production source attests no nonexistent execution");
        source.Input = input;
        var conservative = new VerificationWorkflowService(tools, owner, source, new ConservativeVerificationPolicy(), clock);
        foreach (var report in Enum.GetValues<ExecutionReportOutcome>())
        {
            source.Input = input with { Outcome = report };
            result = await conservative.VerifyAsync(scope, action, approval, request);
            check(result.Evidence?.Outcome == (report == ExecutionReportOutcome.Failed ? VerificationOutcome.Failed : VerificationOutcome.Inconclusive),
                "11Q conservative policy never infers Verified from report " + report);
        }
        foreach (var report in new[] { ExecutionReportOutcome.Failed, ExecutionReportOutcome.Unknown })
        {
            source.Input = input with { Outcome = report };
            check((await workflow.VerifyAsync(scope, action, approval, request)).Status == VerificationOperationStatus.EvidenceDenied,
                "11Q faulty proof policy cannot promote failed/unknown execution to Verified");
        }
        source.Input = input;
        foreach (var outcome in Enum.GetValues<VerificationOutcome>())
        {
            proof.Outcome = outcome;
            check((await workflow.VerifyAsync(scope, action, approval, request)).Evidence?.Outcome == outcome,
                "11Q fixed outcome produced only through policy boundary " + outcome);
        }
        proof.Outcome = (VerificationOutcome)99;
        check((await workflow.VerifyAsync(scope, action, approval, request)).Status == VerificationOperationStatus.EvidenceDenied,
            "11Q invalid verification outcome fails closed");
        proof.Outcome = VerificationOutcome.Verified;
        owner.Allow = false;
        check((await workflow.VerifyAsync(scope, action, approval, request)).Status == VerificationOperationStatus.ScopeDenied,
            "11Q revoked ownership freshly denied");
        owner.Allow = true;
        owner.RevokeSecond = true;
        owner.Calls = 0;
        check((await workflow.VerifyAsync(scope, action, approval, request)).Status == VerificationOperationStatus.ScopeDenied,
            "11Q revocation during verification yields no evidence");
        owner.RevokeSecond = false;
        foreach (var dependency in new[] { "scope", "source", "policy" })
        {
            owner.ThrowFailure = dependency == "scope"; source.ThrowFailure = dependency == "source"; proof.ThrowFailure = dependency == "policy";
            result = await workflow.VerifyAsync(scope, action, approval, request);
            check(result.Status == VerificationOperationStatus.Failed && result.Message == "Verification operation failed." && result.Evidence is null,
                "11Q dependency exception fixed safe result " + dependency);
        }
        owner.ThrowFailure = source.ThrowFailure = proof.ThrowFailure = false;
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var calls = owner.Calls;
        var observed = false;
        try { await workflow.VerifyAsync(scope, action, approval, request, cts.Token); } catch (OperationCanceledException) { observed = true; }
        check(observed && owner.Calls == calls, "11Q pre-cancellation before dependencies");
        foreach (var dependency in new[] { "scope", "source", "policy" })
        {
            using var during = new CancellationTokenSource();
            owner.CancelAndReturn = dependency == "scope" ? during : null;
            source.CancelAndReturn = dependency == "source" ? during : null;
            proof.CancelAndReturn = dependency == "policy" ? during : null;
            observed = false;
            try { await workflow.VerifyAsync(scope, action, approval, request, during.Token); } catch (OperationCanceledException) { observed = true; }
            check(observed, "11Q nonthrowing dependency cancellation propagates " + dependency);
        }
        owner.CancelAndReturn = source.CancelAndReturn = proof.CancelAndReturn = null;
        foreach (var invalidTime in new[] { default(DateTime), clock.UtcNow.AddDays(-1), DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Unspecified) })
        {
            clock.Value = invalidTime;
            check((await workflow.VerifyAsync(scope, action, approval, request)).Status == VerificationOperationStatus.Failed,
                "11Q default/backwards/non-UTC verification time denied");
        }
        clock.Value = input.CompletedAt;
        var again = await workflow.VerifyAsync(scope, action, approval, request);
        check(again.Evidence!.Id.Value != evidence.Id.Value, "11Q no false durable single-use claim; stale references may be assessed again");
        check(!typeof(VerificationEvidence).GetProperties().Any(x => x.Name.Contains("Expir") || x.Name.Contains("Consumed")),
            "11Q expiry/consumption explicitly absent/deferred");
        foreach (var status in Enum.GetValues<VerificationOperationStatus>())
            check(VerificationOperationResult.Denied(status).Message.Length <= VerificationLimits.MaximumResultMessageCharacters &&
                VerificationOperationResult.Denied(status).Evidence is null, "11Q fixed bounded result " + status);
        check(VerificationOperationResult.Denied(VerificationOperationStatus.Success).Status == VerificationOperationStatus.Failed,
            "11Q public result factory cannot fabricate success/evidence");
        check(handler.Calls == 0 && action.Status == ActionLifecycleStatus.Approved && !action.IsVerified &&
            action.ToolExecutionId is null && action.VerificationId is null,
            "11Q action integration deferred; no fabricated execution, linkage or Verified state");
        check(typeof(VerificationWorkflowService).GetConstructors().Single().GetParameters().Select(x => x.ParameterType).SequenceEqual(
            new[] { typeof(ToolRegistry), typeof(IVerificationScopeValidator), typeof(IExecutionEvidenceSource), typeof(IIndependentVerificationPolicy), typeof(IDateTimeProvider), typeof(Aura.Application.Auditing.IAuditEventWriter) }),
            "11Q no dispatcher/provider/database/persistence/OS dependencies");
        foreach (var type in new[] { typeof(AiChatService), typeof(AiProviderRouter) })
            check(type.GetConstructors().Single().GetParameters().All(x => x.ParameterType.Namespace != typeof(IVerificationWorkflowService).Namespace),
                "11Q " + type.Name + " remains verification-free");
        check(typeof(AiChatRequest).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "Prompt" }), "11Q public chat request Prompt-only");
        check(!new ProjectFileAccessOptions().Enabled, "11Q Safe File Access remains disabled");
    }

    private sealed class NeverCalledHandler : IToolHandler
    {
        public int Calls;
        public Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Unexpected execution."); }
    }
    private sealed class ClockFixture : IDateTimeProvider
    {
        public DateTime Value = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Value;
    }
    private sealed class ScopeFixture(ActionScope owned) : IActionScopeValidator, IActionApprovalScopeValidator, IVerificationScopeValidator
    {
        public bool Allow = true, RevokeSecond, ThrowFailure;
        public int Calls;
        public CancellationTokenSource? CancelAndReturn;
        public Task<bool> ValidateAsync(ActionScope scope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            if (CancelAndReturn is not null) { CancelAndReturn.Cancel(); return Task.FromResult(false); }
            if (ThrowFailure) throw new InvalidOperationException("secret raw error fixture");
            return Task.FromResult(Allow && scope == owned && !(RevokeSecond && Calls >= 2));
        }
    }
    private sealed class SourceFixture : IExecutionEvidenceSource
    {
        public ExecutionEvidenceInput? Input;
        public ActionScope? Scope;
        public Guid Id;
        public int Calls;
        public bool ThrowFailure;
        public CancellationTokenSource? CancelAndReturn;
        public Task<ExecutionEvidenceInput?> GetAsync(ActionScope scope, Guid executionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++; Scope = scope; Id = executionId;
            if (CancelAndReturn is not null) { CancelAndReturn.Cancel(); return Task.FromResult<ExecutionEvidenceInput?>(null); }
            if (ThrowFailure) throw new InvalidOperationException("secret source error");
            return Task.FromResult(Input);
        }
    }
    private sealed class ProofFixture : IIndependentVerificationPolicy
    {
        // TEST ONLY: synthetic independently asserted outcome. No real proof or execution is claimed.
        public VerificationOutcome Outcome = VerificationOutcome.Verified;
        public int Calls;
        public bool ThrowFailure;
        public CancellationTokenSource? CancelAndReturn;
        public Task<VerificationOutcome> EvaluateAsync(ExecutionEvidenceInput execution, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            if (CancelAndReturn is not null) { CancelAndReturn.Cancel(); return Task.FromResult(VerificationOutcome.Inconclusive); }
            if (ThrowFailure) throw new InvalidOperationException("secret proof error");
            return Task.FromResult(Outcome);
        }
    }
}
