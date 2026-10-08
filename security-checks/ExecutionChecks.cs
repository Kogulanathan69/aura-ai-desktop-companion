using System.Text.Json;
using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Application.Verifications;
using Aura.Application.Common.Interfaces;
using Aura.Application.Privacy.Services;
using Aura.Application.ProjectFiles.Content;
using Aura.Application.AI.Chat.DTOs;
using Aura.Application.AI.Chat.Services;
using Aura.Application.AI.Providers;

internal static class ExecutionChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ToolIdentifier.TryCreate("fixture", out var toolId);
        ToolIdentifier.TryCreate("alternate", out var alternateId);
        var handler = new HandlerFixture();
        var alternate = new HandlerFixture();
        var tool = new ToolDescriptor(toolId!, "Fixture", "Synthetic fixture", ToolCapability.ProjectRead,
            ToolPermissionRequirement.OwnedProjectRead);
        var tools = new ToolRegistry([new(tool, handler), new(new(alternateId!, "Alternate", "Other fixture",
            ToolCapability.ProjectRead, ToolPermissionRequirement.OwnedProjectRead), alternate)]);
        var scope = new ActionScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var owner = new ScopeFixture(scope);
        var clock = new ClockFixture();
        var lifecycle = new ActionLifecycleService(tools, owner, clock, new PrivacyGuard());
        var proposed = (await lifecycle.ProposeAsync(scope, new(toolId!, "Inspect project"))).Action!;
        var waiting = (await lifecycle.TransitionAsync(scope, proposed, ActionLifecycleStatus.ApprovalRequired)).Action!;
        var approvalResult = await new ApprovalWorkflowService(tools, owner, clock).DecideAsync(scope, waiting, new(ApprovalDecision.Approve));
        var approved = approvalResult.Action!;
        var approval = approvalResult.Evidence!;
        var permission = new PermissionFixture(toolId!);
        var policy = new PolicyFixture(toolId!);
        var stateStore = new AtomicExecutionStore();
        var service = new TrustedToolExecutionService(tools, owner, permission, policy, clock, stateStore);
        TrustedToolExecutionService FreshService() => new(tools, owner, permission, policy, clock, new AtomicExecutionStore());
        var request = new ExecutionRequest(approved.Id);
        var otherProposed = (await lifecycle.ProposeAsync(scope, new(toolId!, "Other action"))).Action!;
        await ExecutionStateChecks.RunAsync(check, approved, approval, alternateId!, otherProposed.Id);
        await ExecutionReconciliationChecks.RunAsync(check, approved, approval, alternateId!, otherProposed.Id);
        await ToolPermissionChecks.RunAsync(check);
        await AuditEventChecks.RunAsync(check, approved);
        await AuditWorkflowChecks.RunAsync(check);

        check((await new TrustedToolExecutionService(tools, owner, new DenyToolExecutionPermissionValidator(), policy, clock, new AtomicExecutionStore())
            .ExecuteAsync(scope, approved, approval, request)).Status == ExecutionOperationStatus.PermissionDenied && handler.Calls == 0,
            "11R supplied permission boundary denies before handler");
        check((await new TrustedToolExecutionService(tools, owner, permission, new DenyToolExecutionPolicy(), clock, new AtomicExecutionStore())
            .ExecuteAsync(scope, approved, approval, request)).Status == ExecutionOperationStatus.PolicyDenied && handler.Calls == 0,
            "11R supplied execution policy denies before handler");
        check(!tool.Enabled && (await new DisabledToolDispatcher(tools).ExecuteAsync(new(toolId!))).Status == ToolExecutionStatus.Disabled,
            "11R Step 11N descriptor/dispatcher remain disabled despite separate policy");
        var beforeOwner = owner.Calls;
        var result = await service.ExecuteAsync(scope, approved, approval, request);
        var done = result.Action!;
        var evidence = result.Evidence!;
        check(result.Status == ExecutionOperationStatus.Success && done.Status == ActionLifecycleStatus.Succeeded &&
            approved.Status == ActionLifecycleStatus.Approved && handler.Calls == 1 && alternate.Calls == 0,
            "11R valid synthetic allow path calls exactly one handler and returns new Succeeded action");
        check(owner.Calls - beforeOwner == 2 && permission.Calls > 0 && policy.Calls > 0 && handler.Request?.ToolId == toolId,
            "11R ownership twice, permission, policy and exact tool ID checked before handler");
        check(evidence.Id.Value != Guid.Empty && done.ToolExecutionId == evidence.Id.Value &&
            evidence.Outcome == ExecutionOutcome.Succeeded,
            "11R server execution ID links immutable action/evidence");
        check(evidence.ActionId == approved.Id && evidence.Scope.UserId == scope.UserId &&
            evidence.Scope.ConversationId == scope.ConversationId && evidence.Scope.ProjectId == scope.ProjectId,
            "11R exact action/user/conversation/project evidence binding");
        check(evidence.ToolId == toolId && evidence.ApprovalId == approval.Id.Value &&
            evidence.MatchesBinding(approved.Id, scope, toolId, approval.Id.Value),
            "11R exact tool/approval evidence binding");
        check(evidence.StartedAt == clock.UtcNow && evidence.CompletedAt == clock.UtcNow &&
            done.UpdatedAt == clock.UtcNow && done.ApprovalId == approval.Id.Value,
            "11R server UTC timestamps and approval linkage preserved");
        check(!done.IsVerified && done.VerificationId is null,
            "11R execution success does not mark action verified");
        var handoff = evidence.ToVerificationInput();
        check(handoff.ExecutionId == evidence.Id.Value && handoff.ActionId == approved.Id && handoff.Scope == scope &&
            handoff.ToolId == toolId && handoff.ApprovalId == approval.Id.Value &&
            handoff.Outcome == ExecutionReportOutcome.Succeeded && handoff.CompletedAt == evidence.CompletedAt,
            "11R structural Step 11Q handoff matches exact bindings but does not verify");
        check(typeof(ExecutionEvidence).GetConstructors().Length == 0 && typeof(ExecutionIdentifier).GetConstructors().Length == 0 &&
            typeof(ExecutionEvidence).GetProperties().All(x => x.SetMethod is null),
            "11R evidence/identifier immutable and have no public constructor");
        var jsonDenied = false;
        try { _ = JsonSerializer.Deserialize<ExecutionEvidence>("{\"Outcome\":0}"); }
        catch (NotSupportedException) { jsonDenied = true; }
        check(jsonDenied, "11R caller JSON cannot fabricate execution evidence");
        check(typeof(ExecutionRequest).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "ActionId" }),
            "11R request exposes no execution ID/permission/approval flag/commands/paths/URLs/arguments");
        check(typeof(ExecutionEvidence).GetProperties().Select(x => x.Name).SequenceEqual(
            new[] { "Id", "ActionId", "Scope", "ToolId", "ApprovalId", "Outcome", "StartedAt", "CompletedAt" }),
            "11R evidence contains fixed fields only, no handler output/errors/files/logs/secrets");
        check((await lifecycle.TransitionAsync(scope, approved, ActionLifecycleStatus.Executing)).Status == ActionOperationStatus.ExecutionDisabled &&
            (await lifecycle.TransitionAsync(scope, done, ActionLifecycleStatus.Executing)).Status == ActionOperationStatus.TransitionDenied,
            "11R direct lifecycle still denies arbitrary execution/retry");
        foreach (var state in new[] { proposed, waiting, done,
            (await lifecycle.TransitionAsync(scope, waiting, ActionLifecycleStatus.Rejected)).Action!,
            (await lifecycle.TransitionAsync(scope, approved, ActionLifecycleStatus.Cancelled)).Action! })
            check((await service.ExecuteAsync(scope, state, approval, new(state.Id))).Status == ExecutionOperationStatus.InvalidActionState,
                "11R Proposed/ApprovalRequired/Rejected/Succeeded/Cancelled cannot execute");
        foreach (var invalid in new[] { (ActionScope?)null, scope with { UserId = Guid.Empty },
            scope with { ConversationId = Guid.Empty }, scope with { ProjectId = Guid.Empty } })
            check((await service.ExecuteAsync(invalid!, approved, approval, request)).Status == ExecutionOperationStatus.InvalidRequest,
                "11R missing/empty internal scope denied");
        check((await service.ExecuteAsync(scope, null!, approval, request)).Status == ExecutionOperationStatus.InvalidRequest &&
            (await service.ExecuteAsync(scope, approved, approval, null!)).Status == ExecutionOperationStatus.InvalidRequest &&
            (await service.ExecuteAsync(scope, approved, approval, new(otherProposed.Id))).Status == ExecutionOperationStatus.InvalidRequest,
            "11R null action/request or mismatched action reference denied");
        foreach (var foreign in new[] { scope with { UserId = Guid.NewGuid() }, scope with { ConversationId = Guid.NewGuid() },
            scope with { ProjectId = Guid.NewGuid() } })
        {
            var denied = await service.ExecuteAsync(foreign, approved, approval, request);
            check(denied.Status == ExecutionOperationStatus.ScopeDenied && denied.Action is null && denied.Evidence is null,
                "11R foreign user/conversation/project denied without leakage");
            check(!evidence.MatchesBinding(approved.Id, foreign, toolId, approval.Id.Value),
                "11R evidence cannot bind foreign scope");
        }
        var otherWaiting = (await lifecycle.TransitionAsync(scope, otherProposed, ActionLifecycleStatus.ApprovalRequired)).Action!;
        var otherApproval = await new ApprovalWorkflowService(tools, owner, clock).DecideAsync(scope, otherWaiting, new(ApprovalDecision.Approve));
        check((await service.ExecuteAsync(scope, approved, otherApproval.Evidence!, request)).Status == ExecutionOperationStatus.ApprovalDenied &&
            (await service.ExecuteAsync(scope, approved, null!, request)).Status == ExecutionOperationStatus.ApprovalDenied,
            "11R foreign/missing approval cannot authorize handler");
        check(!evidence.MatchesBinding(otherProposed.Id, scope, toolId, approval.Id.Value) &&
            !evidence.MatchesBinding(approved.Id, scope, alternateId, approval.Id.Value) &&
            !evidence.MatchesBinding(approved.Id, scope, toolId, otherApproval.Evidence!.Id.Value),
            "11R evidence cannot bind another action/tool/approval");
        check((await new TrustedToolExecutionService(new ToolRegistry([]), owner, permission, policy, clock, new AtomicExecutionStore())
            .ExecuteAsync(scope, approved, approval, request)).Status == ExecutionOperationStatus.UnknownTool,
            "11R unregistered tool fails closed");
        owner.Allow = false;
        check((await service.ExecuteAsync(scope, approved, approval, request)).Status == ExecutionOperationStatus.ScopeDenied,
            "11R fresh ownership required");
        owner.Allow = true;
        permission.Allow = false;
        check((await service.ExecuteAsync(scope, approved, approval, request)).Status == ExecutionOperationStatus.PermissionDenied,
            "11R approval and ownership do not replace permission");
        permission.Allow = true;
        policy.Allow = false;
        check((await service.ExecuteAsync(scope, approved, approval, request)).Status == ExecutionOperationStatus.PolicyDenied,
            "11R approval/permission do not replace exact-tool policy");
        policy.Allow = true;
        owner.RevokeSecond = true; owner.Calls = 0;
        beforeOwner = handler.Calls;
        check((await service.ExecuteAsync(scope, approved, approval, request)).Status == ExecutionOperationStatus.ScopeDenied &&
            handler.Calls == beforeOwner, "11R ownership revocation before handler prevents call");
        owner.RevokeSecond = false;
        handler.Result = ToolExecutionResult.FromStatus(ToolExecutionStatus.Failed);
        beforeOwner = handler.Calls;
        result = await FreshService().ExecuteAsync(scope, approved, approval, request);
        check(result.Status == ExecutionOperationStatus.ExecutionFailed && result.Action?.Status == ActionLifecycleStatus.Failed &&
            result.Evidence?.Outcome == ExecutionOutcome.Failed && handler.Calls == beforeOwner + 1 && alternate.Calls == 0,
            "11R safe handler failure maps Failed, exactly one call, no retry/fallback");
        handler.ThrowFailure = true;
        beforeOwner = handler.Calls;
        result = await FreshService().ExecuteAsync(scope, approved, approval, request);
        check(result.Status == ExecutionOperationStatus.ExecutionFailed && result.Message == "Tool call failed." &&
            result.Evidence?.Outcome == ExecutionOutcome.Failed && handler.Calls == beforeOwner + 1,
            "11R thrown handler exception mapped to fixed safe failure without retry");
        handler.ThrowFailure = false;
        handler.Result = ToolExecutionResult.FromStatus((ToolExecutionStatus)999);
        result = await FreshService().ExecuteAsync(scope, approved, approval, request);
        check(result.Status == ExecutionOperationStatus.ExecutionFailed && result.Evidence?.Outcome == ExecutionOutcome.Failed,
            "11R invalid handler status fails closed");
        handler.Result = ToolExecutionResult.FromStatus(ToolExecutionStatus.Success);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        beforeOwner = handler.Calls;
        var observed = false;
        try { await service.ExecuteAsync(scope, approved, approval, request, cancelled.Token); }
        catch (OperationCanceledException) { observed = true; }
        check(observed && handler.Calls == beforeOwner, "11R pre-cancellation prevents handler call");
        foreach (var stage in new[] { "scope", "permission", "policy", "handler" })
        {
            using var during = new CancellationTokenSource();
            owner.CancelAndDeny = stage == "scope" ? during : null;
            permission.CancelAndDeny = stage == "permission" ? during : null;
            policy.CancelAndDeny = stage == "policy" ? during : null;
            handler.CancelAfterCall = stage == "handler" ? during : null;
            beforeOwner = handler.Calls; observed = false;
            try { await FreshService().ExecuteAsync(scope, approved, approval, request, during.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed && (stage == "handler" || handler.Calls == beforeOwner),
                "11R cancellation propagates at " + stage + " without fabricated completion");
        }
        owner.CancelAndDeny = permission.CancelAndDeny = policy.CancelAndDeny = handler.CancelAfterCall = null;
        handler.ThrowCancellation = true;
        observed = false;
        try { await FreshService().ExecuteAsync(scope, approved, approval, request); }
        catch (OperationCanceledException) { observed = true; }
        check(observed, "11R handler cancellation exception propagates");
        handler.ThrowCancellation = false;
        foreach (var status in Enum.GetValues<ExecutionOperationStatus>())
            check(ExecutionOperationResult.Denied(status).Message.Length <= ExecutionLimits.MaximumResultMessageCharacters &&
                ExecutionOperationResult.Denied(status).Action is null && ExecutionOperationResult.Denied(status).Evidence is null,
                "11R fixed bounded denial result " + status);
        check(ExecutionOperationResult.Denied(ExecutionOperationStatus.Success).Status == ExecutionOperationStatus.Failed,
            "11R public result factory cannot fabricate success");
        beforeOwner = handler.Calls;
        var repeated = await service.ExecuteAsync(scope, approved, approval, request);
        check(repeated.Status == ExecutionOperationStatus.StateConflict && repeated.Evidence is null &&
            stateStore.Reservations == 1 && handler.Calls == beforeOwner,
            "11T stale Approved replay is denied without another handler call");
        check(typeof(TrustedToolExecutionService).GetConstructors().Single().GetParameters().Select(x => x.ParameterType).SequenceEqual(
            new[] { typeof(ToolRegistry), typeof(IToolExecutionScopeValidator), typeof(IToolExecutionPermissionValidator),
                typeof(IToolExecutionPolicy), typeof(IDateTimeProvider), typeof(IActionExecutionStateStore),
                typeof(Aura.Application.Auditing.IAuditEventWriter) }),
            "11T coordinator depends on state store but no DB/provider/OS/verification/dispatcher");
        foreach (var type in new[] { typeof(AiChatService), typeof(AiProviderRouter) })
            check(type.GetConstructors().Single().GetParameters().All(x => x.ParameterType.Namespace != typeof(ITrustedToolExecutionService).Namespace),
                "11R " + type.Name + " remains execution-free");
        check(typeof(AiChatRequest).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "Prompt" }),
            "11R public chat request remains Prompt-only");
        check(!new ProjectFileAccessOptions().Enabled, "11R Safe File Access remains disabled");
        await ExecutionIntegrationChecks.RunAsync(check);
    }

    private sealed class HandlerFixture : IToolHandler
    {
        public int Calls;
        public ToolInvocationRequest? Request;
        public ToolExecutionResult Result = ToolExecutionResult.FromStatus(ToolExecutionStatus.Success);
        public bool ThrowFailure, ThrowCancellation;
        public CancellationTokenSource? CancelAfterCall;
        public Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++; Request = request;
            if (ThrowCancellation) throw new OperationCanceledException();
            if (ThrowFailure) throw new InvalidOperationException("secret raw handler error fixture");
            CancelAfterCall?.Cancel();
            return Task.FromResult(Result);
        }
    }
    private sealed class ClockFixture : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
    }
    private sealed class ScopeFixture(ActionScope owned) : IActionScopeValidator, IActionApprovalScopeValidator, IToolExecutionScopeValidator
    {
        public bool Allow = true, RevokeSecond;
        public int Calls;
        public CancellationTokenSource? CancelAndDeny;
        public Task<bool> ValidateAsync(ActionScope scope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            if (CancelAndDeny is not null) { CancelAndDeny.Cancel(); return Task.FromResult(false); }
            return Task.FromResult(Allow && scope == owned && !(RevokeSecond && Calls >= 2));
        }
    }
    private sealed class PermissionFixture(ToolIdentifier allowed) : IToolExecutionPermissionValidator
    {
        public bool Allow = true;
        public int Calls;
        public CancellationTokenSource? CancelAndDeny;
        public Task<bool> HasPermissionAsync(ActionScope scope, ToolDescriptor tool, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            if (CancelAndDeny is not null) { CancelAndDeny.Cancel(); return Task.FromResult(false); }
            return Task.FromResult(Allow && tool.Id == allowed);
        }
    }
    private sealed class PolicyFixture(ToolIdentifier allowed) : IToolExecutionPolicy
    {
        public bool Allow = true;
        public int Calls;
        public CancellationTokenSource? CancelAndDeny;
        public Task<bool> AllowsAsync(ToolDescriptor tool, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            if (CancelAndDeny is not null) { CancelAndDeny.Cancel(); return Task.FromResult(false); }
            return Task.FromResult(Allow && tool.Id == allowed);
        }
    }
}
