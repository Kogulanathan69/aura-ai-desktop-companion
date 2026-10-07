using Aura.Application.Actions;
using Aura.Application.Tools;
using Aura.Application.Common.Interfaces;
using Aura.Application.Privacy.Services;
using Aura.Application.ProjectFiles.Content;
using Aura.Application.AI.Chat.DTOs;
using Aura.Application.AI.Chat.Services;
using Aura.Application.AI.Providers;

internal static class ActionChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ToolIdentifier.TryCreate("fixture", out var toolId);
        var handler = new NeverCalledHandler();
        var tool = new ToolDescriptor(toolId!, "Fixture", "Synthetic fixture", ToolCapability.ProjectRead,
            ToolPermissionRequirement.OwnedProjectRead);
        var tools = new ToolRegistry([new(tool, handler)]);
        var scope = new ActionScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var validator = new ScopeFixture(scope);
        var clock = new ClockFixture();
        var service = new ActionLifecycleService(tools, validator, clock, new PrivacyGuard());
        var request = new ActionProposalRequest(toolId!, " Inspect project structure ");
        var proposed = (await service.ProposeAsync(scope, request)).Action!;
        check(proposed.Status == ActionLifecycleStatus.Proposed && proposed.Id.Value != Guid.Empty &&
            proposed.Summary == "Inspect project structure" && proposed.Scope == scope && proposed.ToolId == toolId &&
            proposed.CreatedAt == clock.UtcNow && proposed.UpdatedAt == clock.UtcNow,
            "11O valid scoped proposal starts Proposed with server ID/timestamps");
        check(proposed.RequiresApproval && !proposed.IsVerified && proposed.ApprovalId is null &&
            proposed.ToolExecutionId is null && proposed.VerificationId is null,
            "11O proposal has no fabricated approval/execution/verification links");
        check(typeof(ActionDescriptor).GetConstructors().Length == 0 &&
            typeof(ActionDescriptor).GetProperties().All(x => x.SetMethod is null),
            "11O callers cannot construct initial Approved/Executing/Succeeded or mutate action state");
        check(typeof(ActionProposalRequest).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "ToolId", "Summary" }),
            "11O proposal excludes status/ID/scope/command/script/path/URL/payload/approval fields");
        foreach (var invalid in new string?[] { null, "", " ", new string('a', 161), "bad\0text", "bad\ntext",
            "\ud800", "https://example.test", "C:\\private", "password=secret", "{\"data\":1}", "-----BEGIN PRIVATE KEY-----" })
            check((await service.ProposeAsync(scope, request with { Summary = invalid! })).Status == ActionOperationStatus.InvalidRequest,
                "11O blank/malformed/oversized/path/URL/payload/credential summary rejected");
        check((await service.ProposeAsync(scope, request with { Summary = new string('a', 160) })).Action?.Summary.Length == 160,
            "11O exact summary limit accepted");
        check((await service.ProposeAsync(scope, null!)).Status == ActionOperationStatus.InvalidRequest &&
            (await service.ProposeAsync(scope, request with { ToolId = null! })).Status == ActionOperationStatus.InvalidRequest,
            "11O null proposal/tool identifier rejected");
        foreach (var invalid in new[] { scope with { UserId = Guid.Empty }, scope with { ConversationId = Guid.Empty },
            scope with { ProjectId = Guid.Empty } })
            check((await service.ProposeAsync(invalid, request)).Status == ActionOperationStatus.InvalidRequest,
                "11O empty server scope identifier rejected");
        foreach (var foreign in new[] { scope with { UserId = Guid.NewGuid() }, scope with { ProjectId = Guid.NewGuid() },
            scope with { ConversationId = Guid.NewGuid() } })
        {
            check((await service.ProposeAsync(foreign, request)).Status == ActionOperationStatus.ScopeDenied,
                "11O foreign/unrelated scope cannot propose");
            var denied = await service.TransitionAsync(foreign, proposed, ActionLifecycleStatus.ApprovalRequired);
            check(denied.Status == ActionOperationStatus.ScopeDenied && denied.Action is null,
                "11O cross-user/project/conversation transition leaks no descriptor");
        }
        ToolIdentifier.TryCreate("unknown", out var unknown);
        check((await service.ProposeAsync(scope, request with { ToolId = unknown! })).Status == ActionOperationStatus.UnknownTool,
            "11O action references known tool only");
        var waiting = (await service.TransitionAsync(scope, proposed, ActionLifecycleStatus.ApprovalRequired)).Action!;
        check(waiting.Status == ActionLifecycleStatus.ApprovalRequired && proposed.Status == ActionLifecycleStatus.Proposed &&
            waiting.Id == proposed.Id && waiting.Scope == proposed.Scope && waiting.CreatedAt == proposed.CreatedAt,
            "11O Proposed to ApprovalRequired returns new immutable value preserving identity/scope");
        check((await service.TransitionAsync(scope, waiting, ActionLifecycleStatus.Approved)).Status == ActionOperationStatus.ApprovalRequired &&
            waiting.Status == ActionLifecycleStatus.ApprovalRequired,
            "11O represented approval edge denied without trusted evidence, no fabricated approval");
        check((await service.TransitionAsync(scope, waiting, ActionLifecycleStatus.Rejected)).Action?.Status == ActionLifecycleStatus.Rejected,
            "11O ApprovalRequired to Rejected supported without execution");
        foreach (var action in new[] { proposed, waiting })
            check((await service.TransitionAsync(scope, action, ActionLifecycleStatus.Cancelled)).Action?.Status == ActionLifecycleStatus.Cancelled,
                "11O supported pre-execution cancellation");
        foreach (var target in new[] { ActionLifecycleStatus.Executing, ActionLifecycleStatus.Succeeded })
            check((await service.TransitionAsync(scope, proposed, target)).Status == ActionOperationStatus.TransitionDenied,
                "11O Proposed cannot skip approval/execution lifecycle");
        check((await service.TransitionAsync(scope, waiting, ActionLifecycleStatus.Executing)).Status == ActionOperationStatus.TransitionDenied,
            "11O ApprovalRequired cannot skip Approved");
        var edges = new HashSet<(ActionLifecycleStatus, ActionLifecycleStatus)>
        {
            (ActionLifecycleStatus.Proposed, ActionLifecycleStatus.ApprovalRequired),
            (ActionLifecycleStatus.Proposed, ActionLifecycleStatus.Cancelled),
            (ActionLifecycleStatus.ApprovalRequired, ActionLifecycleStatus.Approved),
            (ActionLifecycleStatus.ApprovalRequired, ActionLifecycleStatus.Rejected),
            (ActionLifecycleStatus.ApprovalRequired, ActionLifecycleStatus.Cancelled),
            (ActionLifecycleStatus.Approved, ActionLifecycleStatus.Executing),
            (ActionLifecycleStatus.Approved, ActionLifecycleStatus.Cancelled),
            (ActionLifecycleStatus.Executing, ActionLifecycleStatus.Succeeded),
            (ActionLifecycleStatus.Executing, ActionLifecycleStatus.Failed)
        };
        // Exhaustive graph comparison includes all forbidden skips, retries, self-loops and terminal exits.
        foreach (var current in Enum.GetValues<ActionLifecycleStatus>())
        foreach (var target in Enum.GetValues<ActionLifecycleStatus>())
            check(ActionTransitionPolicy.IsDefinedTransition(current, target) == edges.Contains((current, target)),
                "11O exact lifecycle graph " + current + " -> " + target);
        check(ActionTransitionPolicy.Evaluate(ActionLifecycleStatus.ApprovalRequired, ActionLifecycleStatus.Approved) ==
            ActionOperationStatus.ApprovalRequired, "11O approval boundary cannot accept request/metadata as evidence");
        foreach (var edge in new[] { (ActionLifecycleStatus.Approved, ActionLifecycleStatus.Executing),
            (ActionLifecycleStatus.Executing, ActionLifecycleStatus.Succeeded), (ActionLifecycleStatus.Executing, ActionLifecycleStatus.Failed) })
            check(ActionTransitionPolicy.Evaluate(edge.Item1, edge.Item2) == ActionOperationStatus.ExecutionDisabled,
                "11O represented execution/completion edge default-denied");
        foreach (var terminal in new[] { ActionLifecycleStatus.Rejected, ActionLifecycleStatus.Succeeded,
            ActionLifecycleStatus.Failed, ActionLifecycleStatus.Cancelled })
            check(ActionTransitionPolicy.Evaluate(terminal, ActionLifecycleStatus.Executing) == ActionOperationStatus.TransitionDenied,
                "11O terminal state has no retry/fallback");
        check(ActionTransitionPolicy.Evaluate((ActionLifecycleStatus)99, ActionLifecycleStatus.Cancelled) == ActionOperationStatus.TransitionDenied &&
            (await service.TransitionAsync(scope, proposed, (ActionLifecycleStatus)99)).Status == ActionOperationStatus.TransitionDenied,
            "11O invalid status fails closed");
        validator.Allow = false;
        check((await service.TransitionAsync(scope, proposed, ActionLifecycleStatus.Cancelled)).Status == ActionOperationStatus.ScopeDenied,
            "11O scope revalidated on every lifecycle operation");
        validator.Allow = true;
        validator.ThrowFailure = true;
        var failed = await service.ProposeAsync(scope, request);
        check(failed.Status == ActionOperationStatus.Failed && failed.Message == "Action lifecycle operation failed." && failed.Action is null,
            "11O dependency failure exposes fixed safe result, no raw exception/secret");
        check((await service.TransitionAsync(scope, proposed, ActionLifecycleStatus.Cancelled)).Status == ActionOperationStatus.Failed,
            "11O transition dependency failure safely mapped");
        validator.ThrowFailure = false;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var calls = validator.Calls;
        foreach (var transition in new[] { false, true })
        {
            var cancelled = false;
            try
            {
                if (transition) await service.TransitionAsync(scope, proposed, ActionLifecycleStatus.Cancelled, cts.Token);
                else await service.ProposeAsync(scope, request, cts.Token);
            }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && validator.Calls == calls, "11O pre-cancellation propagates before dependencies");
        }
        validator.Cancel = true;
        var observed = false;
        try { await service.ProposeAsync(scope, request); } catch (OperationCanceledException) { observed = true; }
        check(observed, "11O scope-boundary cancellation not converted to failure");
        validator.Cancel = false;
        foreach (var transition in new[] { false, true })
        {
            using var during = new CancellationTokenSource();
            validator.CancelAndDeny = during;
            observed = false;
            try
            {
                if (transition) await service.TransitionAsync(scope, proposed, ActionLifecycleStatus.Cancelled, during.Token);
                else await service.ProposeAsync(scope, request, during.Token);
            }
            catch (OperationCanceledException) { observed = true; }
            check(observed, "11O cancellation propagates even when validator returns denial without throwing");
        }
        validator.CancelAndDeny = null;
        foreach (var status in Enum.GetValues<ActionOperationStatus>())
            check(ActionOperationResult.Denied(status).Message.Length <= ActionLimits.MaximumResultMessageCharacters &&
                ActionOperationResult.Denied(status).Action is null, "11O fixed denial result bound " + status);
        check(ActionOperationResult.Denied(ActionOperationStatus.Success).Status == ActionOperationStatus.Failed,
            "11O public result factory cannot fabricate success");
        check(!tool.Enabled && handler.Calls == 0, "11O registration/proposal/transition never enable or invoke tool");
        check(typeof(ActionLifecycleService).GetConstructors().Single().GetParameters().Select(x => x.ParameterType).SequenceEqual(
            new[] { typeof(ToolRegistry), typeof(IActionScopeValidator), typeof(IDateTimeProvider), typeof(Aura.Application.Privacy.Interfaces.IPrivacyGuard) }),
            "11O service has no dispatcher/DB/entity store/provider/OS dependencies");
        foreach (var type in new[] { typeof(AiChatService), typeof(AiProviderRouter) })
            check(type.GetConstructors().Single().GetParameters().All(x => x.ParameterType.Namespace != typeof(IActionLifecycleService).Namespace),
                "11O " + type.Name + " remains action-free");
        check(typeof(AiChatRequest).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "Prompt" }),
            "11O public chat request remains Prompt-only without caller scope/action fields");
        check(!new ProjectFileAccessOptions().Enabled, "11O Safe File Access remains disabled");
    }

    private sealed class NeverCalledHandler : IToolHandler
    {
        public int Calls;
        public Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Unexpected tool invocation."); }
    }
    private sealed class ClockFixture : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
    }
    private sealed class ScopeFixture(ActionScope owned) : IActionScopeValidator
    {
        public bool Allow = true, ThrowFailure, Cancel;
        public int Calls;
        public CancellationTokenSource? CancelAndDeny;
        public Task<bool> ValidateAsync(ActionScope scope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (CancelAndDeny is not null) { CancelAndDeny.Cancel(); return Task.FromResult(false); }
            if (Cancel) throw new OperationCanceledException();
            if (ThrowFailure) throw new InvalidOperationException("secret path/token exception fixture");
            return Task.FromResult(Allow && scope == owned);
        }
    }
}
