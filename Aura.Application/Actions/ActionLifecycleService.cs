using Aura.Application.Common.Interfaces;
using Aura.Application.Privacy.Interfaces;
using Aura.Application.Privacy.Models;
using Aura.Application.Tools;

namespace Aura.Application.Actions;

public sealed class ActionLifecycleService(ToolRegistry tools, IActionScopeValidator scopes,
    IDateTimeProvider clock, IPrivacyGuard privacy) : IActionLifecycleService
{
    public async Task<ActionOperationResult> ProposeAsync(ActionScope scope, ActionProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!ValidScope(scope) || request?.ToolId is null || !ValidSummary(request.Summary))
                return ActionOperationResult.Denied(ActionOperationStatus.InvalidRequest);
            var owned = await scopes.ValidateAsync(scope, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!owned) return ActionOperationResult.Denied(ActionOperationStatus.ScopeDenied);
            // Look up metadata only. Never invoke the resolved handler or a dispatcher.
            if (!tools.TryResolve(request.ToolId, out _, out _)) return ActionOperationResult.Denied(ActionOperationStatus.UnknownTool);
            if (privacy.Evaluate(new(null, null, null, request.Summary)).Decision != PrivacyDecision.Allow)
                return ActionOperationResult.Denied(ActionOperationStatus.InvalidRequest);
            cancellationToken.ThrowIfCancellationRequested();
            var now = clock.UtcNow;
            return ActionOperationResult.Success(new(ActionIdentifier.Create(), scope, request.ToolId,
                request.Summary.Trim(), ActionLifecycleStatus.Proposed, now, now));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return ActionOperationResult.Denied(ActionOperationStatus.Failed); }
    }

    public async Task<ActionOperationResult> TransitionAsync(ActionScope scope, ActionDescriptor action,
        ActionLifecycleStatus target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!ValidScope(scope) || action is null) return ActionOperationResult.Denied(ActionOperationStatus.InvalidRequest);
            // Compare the entire immutable scope before any lookup; no cross-user/project/conversation result leakage.
            if (action.Scope != scope)
                return ActionOperationResult.Denied(ActionOperationStatus.ScopeDenied);
            var owned = await scopes.ValidateAsync(scope, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!owned) return ActionOperationResult.Denied(ActionOperationStatus.ScopeDenied);
            var decision = ActionTransitionPolicy.Evaluate(action.Status, target);
            if (decision != ActionOperationStatus.Success) return ActionOperationResult.Denied(decision);
            // Return a new value only; no domain/entity/database mutation or implicit execution.
            return ActionOperationResult.Success(action.Transition(target, clock.UtcNow));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return ActionOperationResult.Denied(ActionOperationStatus.Failed); }
    }

    private static bool ValidScope(ActionScope? scope) => scope is not null &&
        scope.UserId != Guid.Empty && scope.ConversationId != Guid.Empty && scope.ProjectId != Guid.Empty;

    private static bool ValidSummary(string? text) => text is not null &&
        text.Length <= ActionLimits.MaximumSummaryCharacters && !string.IsNullOrWhiteSpace(text) &&
        // Deliberately narrow display alphabet; no paths, URLs, script/JSON syntax or arbitrary payloads.
        text.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '(' or ')' or ',' or '!' or '?');
}
