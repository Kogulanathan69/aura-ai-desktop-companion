# Step 12K — Production runtime readiness consolidation

This step reviews runtime composition and adds fail-closed validation and package-free checks. It does not register or enable a production adapter, tool handler, Safe File Access, durable persistence, or a provider call. The normal API still uses `UnavailableRuntimeCapabilityReadiness` and all production safety switches default to false.

## Readiness matrix

| Capability | Implementation foundation | Persistence | Live verification | Runtime registration | Production ready |
| --- | --- | --- | --- | --- | --- |
| Ownership | Read-only source, identity mapping, future-only DI helper | Existing ownership rows can be queried | PostgreSQL/JWT verification incomplete | Workflow registration inactive | No |
| Permission | Exact contract and policy | Existing schema lacks exact conversation/tool/requirement grant; no adapter | No | No | No |
| ExecutionState | State policy and synthetic checks | Existing schema lacks exact bindings, version/CAS and approval consumption; no adapter | No live concurrency evidence | No | No |
| AuditSink | Bounded event contract and observational writer | Existing schema/outbox insufficient; no sink | No | No | No |
| Reconciliation | Assessment contract and conservative service | Exact execution-state provenance and durable assessment unavailable | No | No | No |
| Verification | Evidence contract and conservative proof policy | Trusted execution/proof provenance and bounded schema unavailable | No | No | No |

`PersistenceCapabilityReadiness.Ownership=true` records a reader implementation only. It is informational, unregistered, and neither a live verification result nor a runtime authorization assertion. The startup snapshot has **all nine values false**, including handler, execution policy, and Safe File Access. An entity, contract, test fake, DI helper, or live harness does not change that snapshot.

## Dependency graph and unsafe combinations

Production adapter mode requires independently ready Ownership, Permission, ExecutionState, AuditSink, Reconciliation, and Verification. Tool execution additionally requires a ready handler and execution policy, `ProductionAdaptersEnabled=true`, and `ToolExecutionEnabled=true`; the validator also requires Ownership, Permission, ExecutionState, and AuditSink directly. Ownership, permission, approval, audit, or verification alone never authorizes execution. Durable audit is required by this production gate, even though current workflow `AuditObservation` is observational and a write failure does not roll back a business result. A future production audit policy must separately review that delivery gap.

Reconciliation requires durable ExecutionState plus an exact trusted reconciliation source; a durable assessment design is still absent. Verification requires durable ExecutionState, trusted completed-execution evidence, and independent proof, plus a reviewed persistence design for a durable claim. A successful handler report is not verification proof. The validator now rejects a readiness snapshot that asserts Reconciliation or Verification while ExecutionState is false. It does not infer either capability from ExecutionState.

`AuditPersistenceEnabled=true` requires production mode and an independent ready AuditSink. `SafeFileAccessEnabled=true` must agree with `ProjectFileAccess.Enabled`, have independent SafeFileAccess readiness, and have server-configured, nonempty, absolute, non-volume roots keyed by nonempty GUIDs. These startup checks do not prove ACL safety or remove filesystem TOCTOU risk. The content endpoint is unmapped while `ProjectFileAccess.Enabled=false`; service and reader gates remain in place. Ownership readiness does not imply file access readiness.

`CloudAiEnabled=true` must agree with cloud router mode and `OpenAiOptions.Enabled`; the validator now also requires valid bounded OpenAI provider configuration. `Program.cs` independently validates enabled OpenAI configuration before runtime safety validation. Cloud AI remains separate from production adapter and tool readiness. A valid configuration test with synthetic values is only a pure gate test and performs no network call.

The runtime safety validator rejects flag mismatches, tool execution without production mode, audit persistence without production mode or sink, incomplete production readiness, missing file roots/readiness, inconsistent derived readiness, and invalid enabled cloud settings. Startup throws the fixed `Runtime safety configuration is invalid.` message for rejected configurations; no root, key, or other configuration value is included. Its `Valid` result means only that the supplied assertions and options pass these checks. It is not live evidence or an automatic readiness declaration.

## Default startup and blockers

Normal `Aura.Api/Program.cs` constructs `UnavailableRuntimeCapabilityReadiness`. It does **not** call `AddOwnershipRuntimeFoundation` or register workflow `ICurrentUserContext`, `IOwnershipSource`, production `IToolPermissionSource`, `IActionExecutionStateStore`, `IAuditEventSink`, `IExecutionReconciliationSource`, `IExecutionEvidenceSource`, or `ITrustedToolExecutionService`. The existing endpoint identity mapping registration remains separate from production workflow ownership. Cloud and local AI transport registrations exist, but the router/default settings do not activate cloud AI; this step makes no provider request. The file content endpoint remains guarded by the existing disabled file option.

Production work remains: live Supabase JWT and PostgreSQL ownership verification and reviewed runtime registration; exact permission schema and adapter; durable, concurrency-safe execution state; typed audit persistence and a transactional delivery decision; exact reconciliation source and persistence design; trusted execution evidence and independent verification proof; reviewed tool handlers/policy; and Windows filesystem race, ACL, and least-privilege verification before Safe File Access can be enabled. Every component requires its own review and evidence before a readiness assertion changes.

## Checks and limits

Step 12K adds package-free checks for all disabled startup readiness dimensions, separation of informational implementation presence, flag defaults and unsafe combinations, tool prerequisites, audit independence, reconciliation/verification dependencies, file-root and cloud configuration gates, fixed failure text, and source-level normal-startup registration boundaries. Earlier checks remain intact. Source inspection is an offline guard, not proof of deployed configuration, live JWT/PostgreSQL behavior, Windows ACLs, or provider behavior. This step adds no migration, schema/entity/DbSet/EF configuration change, package, database write path, real tool execution, OpenAI/Ollama live call, or production activation.
