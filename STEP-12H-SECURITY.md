# Step 12H — Durable execution-state persistence compatibility

Step 12H reviewed the current EF model against `IActionExecutionStateStore` and `ActionExecutionStatePolicy`. The model cannot represent the required exact, atomic execution-state transitions. No production store was implemented or registered; `UnavailableActionExecutionStateStore` remains the only production implementation. This review does not enable execution or claim exactly-once behavior.

## Required state and current schema

A durable state row must bind a nonempty `ActionId`, `UserId`, `ConversationId`, `ProjectId`, `ToolId`, `ApprovalId`, and execution-attempt ID. It must store `Ready`, `Reserved`, `Succeeded`, or `Failed`, an integer `Version`, approval-consumption identity, and terminal outcome. The required transition is Ready/version 0 → Reserved/version 1 → terminal/version 2. Initial creation and reservation must be one atomic operation, followed by versioned, exact-binding completion. Approval consumption must be unique across action rows, so one approval cannot reserve two executions.

The four existing related entities contain partial information with different meanings:

| Entity | Existing relevant fields | Missing or incompatible durable-state semantics |
| --- | --- | --- |
| `ToolExecution` | `Id`, `AIActionId`, `ToolName`, generic `Status`, timestamps and result summaries | No exact scope, typed `ToolId`, `ApprovalId`, attempt ID, integer version/CAS, approval consumption, typed terminal outcome, or unique approval binding. Its row ID is not an attested attempt ID. |
| `AIAction` | `Id`, `UserId`, nullable `ConversationId` and `ProjectId`, generic action `Status` | Scope links are optional; no exact `ToolId`, `ApprovalId`, attempt ID, durable execution-state status, version/CAS, or consumption record. Action lifecycle status is not execution-state status. |
| `ActionApproval` | `Id`, `AIActionId`, `UserId`, decision and timestamps | The decision is not a consumed-by-attempt record. No version, attempt ID, or transaction-safe cross-row consumption constraint links it uniquely to a reservation. |
| `Verification` | `AIActionId`, optional `ToolExecutionId`, verification status and result text | Verification status is not reservation/completion state; no attempt ID, version/CAS, or approval consumption. |

The EF configurations provide ordinary keys and relationships but no reviewed concurrency token or unique approval-consumption binding for execution state. There is no execution-state table or migration in this repository. Existing `Status`, `Operation`, `ToolName`, `ActionType`, result text, or JSON cannot be repurposed to fabricate the missing fields.

Atomic initial reserve is infeasible on this schema: there is no exact Ready state row with a version and unique action/approval constraints to create and reserve in one transaction. `TryReserveAsync` cannot enforce Ready/version 0 or a single approval consumption across rows. `TryCompleteAsync` cannot verify Reserved/version 1 plus the same attempt ID and full binding before one terminal update. Thus stale-version detection, concurrent single-winner reservation, replay prevention, duplicate completion rejection, and exactly-once claims are unsupported. The pure `ActionExecutionStatePolicy` and lock-protected security-check fake exercise transition rules only; they do not establish PostgreSQL atomicity.

## Runtime and verification

Normal `Aura.Api/Program.cs` does not register an `IActionExecutionStateStore` for tool execution. Startup still uses `UnavailableRuntimeCapabilityReadiness`; execution-state readiness is false, as is informational persistence readiness for execution state. Ownership or permission availability cannot substitute for it, and approval cannot substitute for durable reservation. `ProductionAdaptersEnabled`, `ToolExecutionEnabled`, and `SafeFileAccessEnabled` default to false. Tool execution and Safe File Access remain disabled. No handler or OpenAI/Ollama call was added.

Package-free Step 12H checks inspect the EF model fields, nullability, concurrency metadata and indexes; assert the absence of a production adapter; exercise all three unavailable-store operations and cancellation; and confirm readiness/defaults remain disabled. Existing Step 11S/11T checks continue to cover pure transition and test-fake concurrency behavior. With .NET 10.0.401, the solution build passed, the full offline security suite passed **1,226 checks**, and `git diff --check` passed. No live PostgreSQL concurrency test was run.

Future work needs a separately reviewed schema and migration with required exact binding columns, constrained state and outcome values, version/CAS support, unique action identity and cross-row approval consumption, and transaction-safe atomic initial reserve and completion. A future EF adapter must detect duplicates and stale versions, fail closed on errors, propagate cancellation, avoid retry/fallback/handler side effects, and pass offline plus live PostgreSQL concurrency verification before explicit runtime registration and readiness review. This step adds no adapter, write path, migration, entity, DbSet, EF configuration, schema, package, or production activation.
