# Step 15H — production execution-policy evidence and registration review

## Purpose and scope

**ExecutionPolicy: false. CompositionComplete: false — NOT READY.** This is an offline review of the policy boundary, its trusted execution call site, and the production registration rule. No allowing production policy exists, so this step supplies evidence of the current deny-only state. It does not enable execution or change production DI, runtime flags, or migration state.

## Current policy and input model

`IToolExecutionPolicy` exposes `Task<bool> AllowsAsync(ToolDescriptor tool, CancellationToken cancellationToken)`. The only concrete policy implementation in the production application/API assemblies is `DenyToolExecutionPolicy`. It returns `false` for every input after checking cancellation. Synthetic allowing policies in `security-checks` are test fixtures only. There is no policy request DTO or decision enum: the input is a server-registered descriptor and the result is one Boolean. Thus there is no arbitrary command, file path, URL, payload, secret, or exception-text field in the policy interface/result.

`ToolDescriptor` contains canonical `ToolIdentifier` (bounded, normalized ID), display name, description, `ToolCapability`, `ToolPermissionRequirement`, and fixed `RequiresApproval`, `RequiresOwnedScope`, and `Enabled` properties. Construction rejects invalid enum values and malformed text. The registry requires a unique registered ID. `TrustedToolExecutionService` passes the resolved descriptor, not a client-supplied descriptor. An unknown tool is denied before policy invocation.

**Binding limit:** the policy receives tool ID, capability and required-permission metadata through the descriptor. It does **not** receive ActionId, UserId, ConversationId, ProjectId, approval identity, or the execution scope. Those are checked independently in the trusted execution path. The present contract cannot support a scope-specific policy decision or independently attest those bindings. A future allowing policy must be designed and reviewed against this limitation; the existing metadata is classification, not proof of ownership, grant, approval, or production authorization. The policy has no external state dependency today.

## Gate separation and failure behavior

`TrustedToolExecutionService` checks an approved action and matching approval, validates the user/conversation/project scope, resolves the exact registered tool and handler, checks exact permission, then calls the independent policy before durable reservation or handler invocation. It validates scope again before reservation. Approval, ownership, permission and handler presence each remain insufficient to allow when policy denies. `DisabledToolDispatcher` still denies execution, and a descriptor remains disabled.

The current policy can return only `true`/`false`; `false` maps to `PolicyDenied`. The trusted service rejects invalid action state and unknown tool before policy. Cancellation is checked before and after policy and `OperationCanceledException` propagates. An exception from policy is caught by the outer execution boundary and returns a fixed `Failed` status before reservation/handler; no raw exception text crosses the result. The path has no policy retry, alternate policy, or fallback. There is no unknown policy-result enum; a future richer result model would need explicit fail-closed handling. The existing tool-result model also uses fixed messages.

## Production DI, catalog and activation

Normal `Aura.Api/Program.cs` registers no `IToolExecutionPolicy` and continues to provide `UnavailableRuntimeCapabilityReadiness`. The source-controlled `ReviewedExecutionPolicy` catalog is empty. `ProductionRuntimeCompositionInspector` requires exactly one scoped registration with a concrete implementation type in that catalog and explicitly rejects `DenyToolExecutionPolicy`. It does not resolve a service or execute a tool. Factories, instances, duplicate registrations, singleton/transient lifetimes and an unreviewed synthetic allowing type cannot pass. Runtime option flags cannot populate the catalog or make `ExecutionPolicy` true.

Step 14M's exact policy rule and Step 14P's final activation conjunction remain in force. Current snapshot: Ownership=false, Permission=false, ExecutionState=false, AuditSink=false, AuditDelivery=false, Reconciliation=false, Verification=false, ExecutionPolicy=false, ToolHandlers=false, TrustedExecution=false; **CompositionComplete=false, NOT READY**. All reviewed production catalogs are still empty. A true composition snapshot alone would also be insufficient without independent runtime evidence, runtime safety validation and final security approval.

## Future allowing-policy requirements and remaining blockers

An eventual policy needs an independently reviewed, concrete non-deny implementation; an exact known type, unique scoped normal-startup registration, and explicit reviewed catalog entry. It must bind the canonical registered tool, capability and required permission, reject malformed/unknown metadata and caller-controlled bypasses, return bounded deterministic decisions, propagate cancellation, fail closed on exceptions, and have no hidden retry or fallback. If authorization must vary by action or user/conversation/project, the interface and call-site bindings require a separate security review because they are absent from today's policy request. Approval, ownership, permission and handler registration cannot substitute for an independent allow decision. Any external state/provider dependency requires its own review and evidence. No allowing policy is fabricated merely to satisfy the composition rule.

Other readiness blockers remain: independently reviewed identity/ownership, permission lifecycle, durable execution state, audit delivery, reconciliation, verification, real handlers, trusted execution composition, historical migration/live PostgreSQL evidence where applicable, and the Step 14P final approval and executable activation gate. Steps 15C, 15D and 15G document those separate evidence limits. No production execution capability is enabled here.

**LIVE DATABASE EVIDENCE: NOT APPLICABLE** for this deny-only policy review. No policy DB adapter exists, and no live database harness was run. Harness existence or synthetic all-true fixtures cannot establish production PASS evidence.

## Validation

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```
