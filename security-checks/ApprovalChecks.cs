using System.Text.Json;
using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Tools;
using Aura.Application.Common.Interfaces;
using Aura.Application.Privacy.Services;
using Aura.Application.AI.Chat.DTOs;
using Aura.Application.AI.Chat.Services;
using Aura.Application.AI.Providers;
using Aura.Application.ProjectFiles.Content;

internal static class ApprovalChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ToolIdentifier.TryCreate("fixture", out var toolId);
        ToolIdentifier.TryCreate("other", out var otherTool);
        var handler = new NeverCalledHandler();
        var tool = new ToolDescriptor(toolId!, "Fixture", "Fixture", ToolCapability.ProjectRead, ToolPermissionRequirement.OwnedProjectRead);
        var registry = new ToolRegistry([new(tool, handler)]);
        var scope = new ActionScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var ownership = new ScopeFixture(scope);
        var clock = new ClockFixture();
        var lifecycle = new ActionLifecycleService(registry, ownership, clock, new PrivacyGuard());
        var workflow = new ApprovalWorkflowService(registry, ownership, clock);
        var proposed = (await lifecycle.ProposeAsync(scope, new(toolId!, "Inspect project"))).Action!;
        var waiting = (await lifecycle.TransitionAsync(scope, proposed, ActionLifecycleStatus.ApprovalRequired)).Action!;
        ownership.Calls = 0;
        var result = await workflow.DecideAsync(scope, waiting, new(ApprovalDecision.Approve));
        var approved = result.Action!;
        var evidence = result.Evidence!;
        check(result.Status == ApprovalOperationStatus.Success && approved.Status == ActionLifecycleStatus.Approved &&
            approved.Id == waiting.Id && waiting.Status == ActionLifecycleStatus.ApprovalRequired,
            "11P explicit workflow approves exact immutable waiting action");
        check(evidence.Id.Value != Guid.Empty && approved.ApprovalId == evidence.Id.Value,
            "11P server generates nonempty approval ID and links action");
        check(evidence.ActionId == waiting.Id, "11P evidence binds exact action ID");
        check(evidence.Scope.UserId == scope.UserId, "11P evidence binds user");
        check(evidence.Scope.ConversationId == scope.ConversationId, "11P evidence binds conversation");
        check(evidence.Scope.ProjectId == scope.ProjectId, "11P evidence binds project");
        check(evidence.ToolId == waiting.ToolId && evidence.Decision == ApprovalDecision.Approve,
            "11P evidence binds tool and approved decision");
        check(evidence.IssuedAt == clock.UtcNow && approved.UpdatedAt == clock.UtcNow && approved.CreatedAt == waiting.CreatedAt,
            "11P issue/update timestamp server-generated and creation retained");
        check(ownership.Calls == 2, "11P ownership freshly validated twice before approval");
        check(ApprovalValidation.IsBoundToApprovedAction(evidence, approved, scope, toolId), "11P positive evidence/action binding");
        check(!ApprovalValidation.IsBoundToApprovedAction(evidence, waiting, scope, toolId),
            "11P waiting action does not count as linked Approved action");
        check(typeof(ApprovalEvidence).GetConstructors().Length == 0 && typeof(ApprovalIdentifier).GetConstructors().Length == 0 &&
            typeof(ApprovalEvidence).GetProperties().All(x => x.SetMethod is null) &&
            typeof(ApprovalIdentifier).GetProperties().All(x => x.SetMethod is null),
            "11P evidence/ID have no public constructor or writable/copy-with properties");
        var jsonDenied = false;
        try { _ = JsonSerializer.Deserialize<ApprovalEvidence>("{\"Decision\":0,\"IssuedAt\":\"2026-10-07T00:00:00Z\"}"); }
        catch (NotSupportedException) { jsonDenied = true; }
        check(jsonDenied, "11P JSON cannot deserialize caller-fabricated evidence");
        check(typeof(ApprovalRequest).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "Decision" }),
            "11P decision request cannot select IDs/scope/tool/status/timestamps/text/commands");
        check(Enum.GetValues<ApprovalDecision>().SequenceEqual(new[] { ApprovalDecision.Approve, ApprovalDecision.Reject }),
            "11P exactly Approve/Reject decision values");
        check((await lifecycle.TransitionAsync(scope, waiting, ActionLifecycleStatus.Approved)).Status == ActionOperationStatus.ApprovalRequired,
            "11P direct lifecycle still cannot approve");
        check((await lifecycle.TransitionAsync(scope, approved, ActionLifecycleStatus.Executing)).Status == ActionOperationStatus.ExecutionDisabled,
            "11P approval cannot enable execution");
        foreach (var state in Enum.GetValues<ActionLifecycleStatus>())
            check(ApprovalValidation.CanDecide(state) == (state == ActionLifecycleStatus.ApprovalRequired),
                "11P exact decision precondition " + state);
        var rejected = (await workflow.DecideAsync(scope, waiting, new(ApprovalDecision.Reject))).Action!;
        var cancelled = (await lifecycle.TransitionAsync(scope, waiting, ActionLifecycleStatus.Cancelled)).Action!;
        foreach (var action in new[] { proposed, approved, rejected, cancelled })
        foreach (var decision in Enum.GetValues<ApprovalDecision>())
            check((await workflow.DecideAsync(scope, action, new(decision))).Status == ApprovalOperationStatus.InvalidActionState,
                "11P reachable non-waiting state cannot approve/reject " + action.Status);
        result = await workflow.DecideAsync(scope, waiting, new(ApprovalDecision.Reject));
        check(result.Status == ApprovalOperationStatus.Success && result.Action?.Status == ActionLifecycleStatus.Rejected &&
            result.Evidence is null && result.Action.ApprovalId is null,
            "11P rejection creates no Approved evidence or approval link");
        foreach (var foreign in new[] { scope with { UserId = Guid.NewGuid() }, scope with { ConversationId = Guid.NewGuid() },
            scope with { ProjectId = Guid.NewGuid() } })
        {
            result = await workflow.DecideAsync(foreign, waiting, new(ApprovalDecision.Approve));
            check(result.Status == ApprovalOperationStatus.ScopeDenied && result.Action is null && result.Evidence is null,
                "11P foreign/mismatched scope denied without action/evidence leakage");
            check(!evidence.MatchesBinding(waiting.Id, foreign, toolId) &&
                !ApprovalValidation.IsBoundToApprovedAction(evidence, approved, foreign, toolId),
                "11P evidence cannot bind foreign user/conversation/project");
        }
        var differentAction = (await lifecycle.ProposeAsync(scope, new(toolId!, "Other action"))).Action!;
        check(!evidence.MatchesBinding(differentAction.Id, scope, toolId), "11P evidence cannot bind another action ID");
        check(!evidence.MatchesBinding(waiting.Id, scope, otherTool) &&
            !ApprovalValidation.IsBoundToApprovedAction(evidence, approved, scope, otherTool),
            "11P mismatched tool binding denied");
        result = await new ApprovalWorkflowService(new ToolRegistry([]), ownership, clock)
            .DecideAsync(scope, waiting, new(ApprovalDecision.Approve));
        check(result.Status == ApprovalOperationStatus.UnknownTool && result.Evidence is null, "11P unknown/no-longer-registered tool denied");
        foreach (var invalid in new[] { scope with { UserId = Guid.Empty }, scope with { ConversationId = Guid.Empty },
            scope with { ProjectId = Guid.Empty }, null! })
            check((await workflow.DecideAsync(invalid, waiting, new(ApprovalDecision.Approve))).Status == ApprovalOperationStatus.InvalidRequest,
                "11P empty/missing server scope fails closed");
        check((await workflow.DecideAsync(scope, null!, new(ApprovalDecision.Approve))).Status == ApprovalOperationStatus.InvalidRequest &&
            (await workflow.DecideAsync(scope, waiting, null!)).Status == ApprovalOperationStatus.InvalidRequest,
            "11P null action/request fails closed");
        check((await workflow.DecideAsync(scope, waiting, new((ApprovalDecision)99))).Status == ApprovalOperationStatus.InvalidRequest,
            "11P unknown decision fails closed");
        ownership.Allow = false;
        check((await workflow.DecideAsync(scope, waiting, new(ApprovalDecision.Approve))).Status == ApprovalOperationStatus.ScopeDenied,
            "11P revoked ownership fails fresh validation");
        ownership.Allow = true;
        ownership.RevokeSecond = true;
        ownership.Calls = 0;
        check((await workflow.DecideAsync(scope, waiting, new(ApprovalDecision.Reject))).Status == ApprovalOperationStatus.ScopeDenied &&
            waiting.Status == ActionLifecycleStatus.ApprovalRequired,
            "11P revocation during operation produces no decision/evidence");
        ownership.RevokeSecond = false;
        ownership.ThrowFailure = true;
        ownership.Calls = 0;
        result = await workflow.DecideAsync(scope, waiting, new(ApprovalDecision.Approve));
        check(result.Status == ApprovalOperationStatus.Failed && result.Message == "Approval operation failed." &&
            result.Action is null && result.Evidence is null && ownership.Calls == 1,
            "11P raw dependency failures mapped to safe fixed result without retry/fallback");
        ownership.ThrowFailure = false;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var before = ownership.Calls;
        var observed = false;
        try { await workflow.DecideAsync(scope, waiting, new(ApprovalDecision.Approve), cts.Token); }
        catch (OperationCanceledException) { observed = true; }
        check(observed && ownership.Calls == before, "11P pre-cancellation before dependencies");
        ownership.Cancel = true;
        observed = false;
        try { await workflow.DecideAsync(scope, waiting, new(ApprovalDecision.Approve)); }
        catch (OperationCanceledException) { observed = true; }
        check(observed, "11P validator cancellation propagates unchanged");
        ownership.Cancel = false;
        using var during = new CancellationTokenSource();
        ownership.CancelAndDeny = during;
        observed = false;
        try { await workflow.DecideAsync(scope, waiting, new(ApprovalDecision.Approve), during.Token); }
        catch (OperationCanceledException) { observed = true; }
        check(observed, "11P cancellation observed even on validator nonthrowing denial");
        ownership.CancelAndDeny = null;
        foreach (var invalidTime in new[] { default(DateTime), clock.UtcNow.AddDays(-1), DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Unspecified) })
        {
            clock.Value = invalidTime;
            check((await workflow.DecideAsync(scope, waiting, new(ApprovalDecision.Approve))).Status == ApprovalOperationStatus.Failed,
                "11P malformed/non-UTC/backwards issue time denied");
        }
        clock.Value = waiting.UpdatedAt;
        // Honest limitation: an old immutable waiting value can be decided again; no durable state/consumption store.
        var again = await workflow.DecideAsync(scope, waiting, new(ApprovalDecision.Approve));
        check(again.Status == ApprovalOperationStatus.Success && again.Evidence!.Id.Value != evidence.Id.Value,
            "11P no false durable replay/single-use claim for stale waiting values");
        check(!typeof(ApprovalEvidence).GetProperties().Any(x => x.Name.Contains("Expir") || x.Name.Contains("Consumed")),
            "11P expiry/consumption fields absent, explicitly deferred");
        foreach (var status in Enum.GetValues<ApprovalOperationStatus>())
            check(ApprovalOperationResult.Denied(status).Message.Length <= ApprovalLimits.MaximumResultMessageCharacters &&
                ApprovalOperationResult.Denied(status).Action is null && ApprovalOperationResult.Denied(status).Evidence is null,
                "11P fixed bounded safe denial result " + status);
        check(ApprovalOperationResult.Denied(ApprovalOperationStatus.Success).Status == ApprovalOperationStatus.Failed &&
            ApprovalOperationResult.Denied((ApprovalOperationStatus)99).Status == ApprovalOperationStatus.Failed,
            "11P result factory cannot fabricate success/evidence");
        check(!tool.Enabled && handler.Calls == 0 && approved.Status != ActionLifecycleStatus.Executing &&
            approved.ToolExecutionId is null && approved.VerificationId is null && !approved.IsVerified,
            "11P approval leaves tools disabled/uninvoked and action unexecuted/unverified");
        check(typeof(ApprovalWorkflowService).GetConstructors().Single().GetParameters().Select(x => x.ParameterType).SequenceEqual(
            new[] { typeof(ToolRegistry), typeof(IActionApprovalScopeValidator), typeof(IDateTimeProvider),
                typeof(Aura.Application.Auditing.IAuditEventWriter) }),
            "11P no dispatcher/provider/database/persistence/OS dependency");
        foreach (var type in new[] { typeof(AiChatService), typeof(AiProviderRouter) })
            check(type.GetConstructors().Single().GetParameters().All(x => x.ParameterType.Namespace != typeof(IApprovalWorkflowService).Namespace),
                "11P " + type.Name + " remains approval-free");
        check(typeof(AiChatRequest).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "Prompt" }), "11P chat request Prompt-only");
        check(!new ProjectFileAccessOptions().Enabled, "11P Safe File Access remains disabled");
    }

    private sealed class NeverCalledHandler : IToolHandler
    {
        public int Calls;
        public Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Unexpected tool invocation."); }
    }
    private sealed class ClockFixture : IDateTimeProvider
    {
        public DateTime Value = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Value;
    }
    private sealed class ScopeFixture(ActionScope owned) : IActionScopeValidator, IActionApprovalScopeValidator
    {
        public bool Allow = true, RevokeSecond, ThrowFailure, Cancel;
        public int Calls;
        public CancellationTokenSource? CancelAndDeny;
        public Task<bool> ValidateAsync(ActionScope scope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (CancelAndDeny is not null) { CancelAndDeny.Cancel(); return Task.FromResult(false); }
            if (Cancel) throw new OperationCanceledException();
            if (ThrowFailure) throw new InvalidOperationException("secret raw error fixture");
            return Task.FromResult(Allow && scope == owned && !(RevokeSecond && Calls >= 2));
        }
    }
}
