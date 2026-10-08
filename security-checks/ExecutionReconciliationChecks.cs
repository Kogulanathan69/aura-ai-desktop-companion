using System.Reflection;
using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Application.AI.Chat.DTOs;
using Aura.Application.AI.Chat.Services;
using Aura.Application.AI.Providers;
using Aura.Application.ProjectFiles.Content;

internal static class ExecutionReconciliationChecks
{
    public static async Task RunAsync(Action<bool, string> check, ActionDescriptor action,
        ApprovalEvidence approval, ToolIdentifier otherTool, ActionIdentifier otherActionId)
    {
        var ready = ActionExecutionState.CreateReady(action, approval)!;
        var executionId = Guid.NewGuid();
        var reserved = ready with { Status = ActionExecutionStateStatus.Reserved, Version = 1,
            ExecutionId = executionId, ApprovalConsumedByExecutionId = executionId };
        check(ActionExecutionStatePolicy.IsValid(reserved), "11U valid Reserved state eligible for assessment");
        ExecutionReconciliationSnapshot Snapshot(ActionExecutionState state,
            ExecutionReconciliationReason reason = ExecutionReconciliationReason.Unknown) =>
            new(state, state.ActionId, state.Scope, state.ToolId, state.ApprovalId,
                state.ExecutionId ?? Guid.Empty, state.Version, reason);
        var source = new Source { Value = Snapshot(reserved) };
        check((await new ConservativeExecutionReconciliationPolicy().AssessAsync(source.Value,
            CancellationToken.None)) == ExecutionReconciliationDisposition.NeedsManualReview &&
            Enum.GetValues<ExecutionReconciliationReason>().Length == 6 &&
            Enum.GetValues<ExecutionReconciliationDisposition>().Length == 4,
            "11U default policy is manual review with bounded reason/disposition enums");
        var owner = new Owner(action.Scope);
        var policy = new Policy();
        var clock = new Clock();
        var service = new ExecutionReconciliationService(owner, source, policy, clock);
        var request = new ExecutionReconciliationRequest(action.Id);
        var outcome = await service.ReconcileAsync(action.Scope, request);
        var evidence = outcome.Evidence!;
        check(outcome.Status == ExecutionReconciliationStatus.Success &&
            evidence.Disposition == ExecutionReconciliationDisposition.NeedsManualReview &&
            source.Calls == 1 && policy.Calls == 1 && owner.Calls == 2,
            "11U Reserved default assessment is manual review after two ownership checks");
        check(evidence.Id.Value != Guid.Empty && evidence.ActionId == action.Id &&
            evidence.Scope.UserId == action.Scope.UserId &&
            evidence.Scope.ConversationId == action.Scope.ConversationId &&
            evidence.Scope.ProjectId == action.Scope.ProjectId &&
            evidence.ToolId == action.ToolId && evidence.ApprovalId == approval.Id.Value &&
            evidence.ExecutionId == executionId && evidence.ObservedVersion == 1 &&
            evidence.ObservedStatus == ActionExecutionStateStatus.Reserved &&
            evidence.Reason == ExecutionReconciliationReason.Unknown && evidence.IssuedAt == clock.UtcNow,
            "11U assessment evidence binds exact action/scope/tool/approval/execution/version/reason/time");
        check(typeof(ExecutionReconciliationRequest).GetProperties().Select(x => x.Name)
                .SequenceEqual(["ActionId"]) &&
            typeof(ExecutionReconciliationIdentifier).GetConstructors().Length == 0 &&
            typeof(ExecutionReconciliationIdentifier).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .All(x => x.ReturnType != typeof(ExecutionReconciliationIdentifier)) &&
            typeof(ExecutionReconciliationEvidence).GetConstructors().Length == 0 &&
            typeof(ExecutionReconciliationEvidence).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .All(x => x.ReturnType != typeof(ExecutionReconciliationEvidence)),
            "11U request is ActionId-only; ID/evidence have no public constructor or minting factory");
        check(typeof(ExecutionReconciliationEvidence).GetProperties().All(x => x.SetMethod is null) &&
            typeof(ExecutionReconciliationEvidence).GetProperties().Select(x => x.Name).SequenceEqual(
                ["Id", "ActionId", "Scope", "ToolId", "ApprovalId", "ExecutionId", "ObservedStatus",
                    "ObservedVersion", "Reason", "Disposition", "IssuedAt"]),
            "11U immutable evidence has fixed bounded fields only");
        check(Enum.GetValues<ExecutionReconciliationDisposition>().All(x => x !=
            (ExecutionReconciliationDisposition)999) &&
            !Enum.GetNames<ExecutionReconciliationDisposition>().Any(x => x.Contains("Retry") || x.Contains("Succeeded")),
            "11U bounded disposition has no retry or success recovery");

        source.Value = Snapshot(ready);
        check((await service.ReconcileAsync(action.Scope, request)).Status ==
            ExecutionReconciliationStatus.NotEligible,
            "11U Ready state not eligible");
        foreach (var terminal in new[] { ActionExecutionStateStatus.Succeeded, ActionExecutionStateStatus.Failed })
        {
            var complete = reserved with { Status = terminal, Version = 2,
                Outcome = terminal == ActionExecutionStateStatus.Succeeded
                    ? ExecutionOutcome.Succeeded : ExecutionOutcome.Failed };
            source.Value = Snapshot(complete);
            check((await service.ReconcileAsync(action.Scope, request)).Status ==
                ExecutionReconciliationStatus.AlreadyResolved,
                "11U terminal " + terminal + " is already resolved");
        }
        source.Value = Snapshot(reserved);
        check((await service.ReconcileAsync(action.Scope, new(otherActionId))).Status ==
            ExecutionReconciliationStatus.StateConflict,
            "11U foreign action request denied");
        check((await service.ReconcileAsync(action.Scope, null!)).Status ==
            ExecutionReconciliationStatus.InvalidRequest &&
            (await service.ReconcileAsync(action.Scope with { UserId = Guid.Empty }, request)).Status ==
            ExecutionReconciliationStatus.InvalidRequest,
            "11U null request and empty scope denied");
        foreach (var foreign in new[] { action.Scope with { UserId = Guid.NewGuid() },
            action.Scope with { ConversationId = Guid.NewGuid() },
            action.Scope with { ProjectId = Guid.NewGuid() } })
            check((await service.ReconcileAsync(foreign, request)).Status ==
                ExecutionReconciliationStatus.ScopeDenied,
                "11U foreign user/conversation/project denied before source");
        foreach (var mismatch in new[]
        {
            source.Value with { Scope = action.Scope with { UserId = Guid.NewGuid() } },
            source.Value with { Scope = action.Scope with { ConversationId = Guid.NewGuid() } },
            source.Value with { Scope = action.Scope with { ProjectId = Guid.NewGuid() } },
            source.Value with { ToolId = otherTool },
            source.Value with { ApprovalId = Guid.NewGuid() },
            source.Value with { ExecutionId = Guid.NewGuid() },
            source.Value with { Version = 2 },
            source.Value with { ActionId = otherActionId }
        })
        {
            source.Value = mismatch;
            check((await service.ReconcileAsync(action.Scope, request)).Status ==
                ExecutionReconciliationStatus.StateConflict,
                "11U mismatched trusted source binding denied");
        }
        source.Value = Snapshot(reserved with { ApprovalConsumedByExecutionId = Guid.NewGuid() });
        check((await service.ReconcileAsync(action.Scope, request)).Status ==
            ExecutionReconciliationStatus.StateConflict,
            "11U malformed durable state denied");
        source.Value = Snapshot(reserved, (ExecutionReconciliationReason)999);
        check((await service.ReconcileAsync(action.Scope, request)).Status ==
            ExecutionReconciliationStatus.StateConflict,
            "11U unknown reason enum fails closed");
        source.Value = Snapshot(reserved);
        policy.Disposition = (ExecutionReconciliationDisposition)999;
        check((await service.ReconcileAsync(action.Scope, request)).Status ==
            ExecutionReconciliationStatus.PolicyDenied,
            "11U unknown disposition enum fails closed");
        policy.Disposition = ExecutionReconciliationDisposition.SafeToMarkFailed;
        var advisory = await service.ReconcileAsync(action.Scope, request);
        check(advisory.Status == ExecutionReconciliationStatus.Success &&
            advisory.Evidence?.Disposition == ExecutionReconciliationDisposition.SafeToMarkFailed &&
            source.Value.State.Status == ActionExecutionStateStatus.Reserved && source.Value.State.Version == 1,
            "11U synthetic SafeToMarkFailed remains assessment-only with no mutation");
        policy.Disposition = ExecutionReconciliationDisposition.Unavailable;
        check((await service.ReconcileAsync(action.Scope, request)).Status ==
            ExecutionReconciliationStatus.StateUnavailable,
            "11U unavailable policy fails closed");
        policy.Disposition = ExecutionReconciliationDisposition.NeedsManualReview;
        owner.RevokeSecond = true; owner.Calls = 0;
        check((await service.ReconcileAsync(action.Scope, request)).Status ==
            ExecutionReconciliationStatus.ScopeDenied,
            "11U ownership revocation after policy prevents assessment");
        owner.RevokeSecond = false; owner.Allow = false;
        check((await service.ReconcileAsync(action.Scope, request)).Status ==
            ExecutionReconciliationStatus.ScopeDenied,
            "11U ownership required despite approval and reserved state");
        owner.Allow = true;
        check((await new ExecutionReconciliationService(owner,
            new UnavailableExecutionReconciliationSource(), new ConservativeExecutionReconciliationPolicy(), clock)
            .ReconcileAsync(action.Scope, request)).Status == ExecutionReconciliationStatus.StateUnavailable,
            "11U supplied source unavailable without DB/OS access");
        check((await new DenyExecutionReconciliationScopeValidator().ValidateAsync(action.Scope,
            CancellationToken.None)) == false,
            "11U supplied ownership validator denies by default");
        policy.ThrowFailure = true;
        var failed = await service.ReconcileAsync(action.Scope, request);
        check(failed.Status == ExecutionReconciliationStatus.Failed &&
            !failed.Message.Contains("secret") && failed.Evidence is null,
            "11U dependency exception maps fixed safe failure");
        policy.ThrowFailure = false;
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); var observed = false;
            try { await service.ReconcileAsync(action.Scope, request, cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed, "11U pre-cancellation propagates");
        }
        using (var cancelled = new CancellationTokenSource())
        {
            source.Cancel = cancelled; var observed = false;
            try { await service.ReconcileAsync(action.Scope, request, cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed && source.Value.State.Status == ActionExecutionStateStatus.Reserved,
                "11U source-stage cancellation propagates without state mutation");
            source.Cancel = null;
        }
        var repeated = await service.ReconcileAsync(action.Scope, request);
        check(repeated.Status == ExecutionReconciliationStatus.Success &&
            repeated.Evidence?.Id.Value != evidence.Id.Value &&
            source.Value.State.Status == ActionExecutionStateStatus.Reserved,
            "11U repeated assessment may get new ID; no single-use claim or mutation");
        check(approval.MatchesBinding(action.Id, action.Scope, action.ToolId) &&
            approval.Id.Value == reserved.ApprovalId,
            "11U approval evidence remains immutable, not reconciliation proof");
        check(typeof(ExecutionReconciliationService).GetConstructors().Single().GetParameters()
                .Select(x => x.ParameterType).SequenceEqual(
                    [typeof(IExecutionReconciliationScopeValidator), typeof(IExecutionReconciliationSource),
                        typeof(IExecutionReconciliationPolicy), typeof(IDateTimeProvider),
                        typeof(Aura.Application.Auditing.IAuditEventWriter)]) &&
            typeof(AiChatRequest).GetProperties().Select(x => x.Name).SequenceEqual(["Prompt"]) &&
            new[] { typeof(AiChatService), typeof(AiProviderRouter) }.All(type =>
                type.GetConstructors().Single().GetParameters().All(parameter =>
                    parameter.ParameterType != typeof(IExecutionReconciliationService))) &&
            !new ProjectFileAccessOptions().Enabled,
            "11U no handler/coordinator/verification/chat/provider dependency; file access disabled");
        foreach (var status in Enum.GetValues<ExecutionReconciliationStatus>())
            check(ExecutionReconciliationResult.Denied(status).Message.Length <= 80 &&
                ExecutionReconciliationResult.Denied(status).Evidence is null,
                "11U fixed bounded denial " + status);
        check(ExecutionReconciliationResult.Denied(ExecutionReconciliationStatus.Success).Status ==
            ExecutionReconciliationStatus.Failed,
            "11U public denial factory cannot fabricate success");
    }

    private sealed class Source : IExecutionReconciliationSource
    {
        public required ExecutionReconciliationSnapshot Value;
        public int Calls;
        public CancellationTokenSource? Cancel;
        public Task<ExecutionReconciliationSnapshot?> GetAsync(ActionScope scope, ActionIdentifier actionId,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++; Cancel?.Cancel();
            return Task.FromResult<ExecutionReconciliationSnapshot?>(Value);
        }
    }
    private sealed class Owner(ActionScope owned) : IExecutionReconciliationScopeValidator
    {
        public bool Allow = true, RevokeSecond;
        public int Calls;
        public Task<bool> ValidateAsync(ActionScope scope, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            return Task.FromResult(Allow && scope == owned && !(RevokeSecond && Calls >= 2));
        }
    }
    private sealed class Policy : IExecutionReconciliationPolicy
    {
        public int Calls;
        public bool ThrowFailure;
        public ExecutionReconciliationDisposition Disposition = ExecutionReconciliationDisposition.NeedsManualReview;
        public Task<ExecutionReconciliationDisposition> AssessAsync(ExecutionReconciliationSnapshot snapshot,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            if (ThrowFailure) throw new InvalidOperationException("secret policy error");
            return Task.FromResult(Disposition);
        }
    }
    private sealed class Clock : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
    }
}
