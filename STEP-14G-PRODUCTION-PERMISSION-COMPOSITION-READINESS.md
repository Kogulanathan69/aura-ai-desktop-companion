# Step 14G — production permission composition readiness

## Purpose and current truth

**Production Permission: false. CompositionComplete: false — NOT READY.** This step hardens an observational startup snapshot; it does not register a permission source or authorize tool execution. `ToolPermissionPersistenceSource` is an exact, read-only `IToolPermissionSource` implementation in Infrastructure. Normal `Aura.Api/Program.cs` does not register it or any other production `IToolPermissionSource`. Consequently there is no production lifetime to claim. A source file, interface, model, migration, configuration flag or isolated offline check cannot substitute for a reviewed normal-startup registration.

## Exact permission foundation reviewed

The application contract binds one internal `UserId`, `ConversationId`, `ProjectId`, canonical `ToolId` and exact `ToolPermissionRequirement`. The source queries only `ToolPermissionGrants`, requiring that exact tuple, an active grant with `RevokedAt` null, an active user, an owned Project conversation linked to the exact project, and an owned project. It uses no tracking and takes at most two rows. It rejects missing, duplicate, malformed, revoked or inactive matches; it reparses stored tool IDs, maps only the three defined numeric requirements, and rejects an empty grant ID. Errors yield bounded unavailability. The separate `ExactToolPermissionPolicy` rejects scope, tool, requirement, status and grant mismatches. Existing Step 11V and 13A/13B offline checks cover wildcard IDs, empty scope IDs, unknown requirements, duplicates, revocation, malformed persisted values and the dedicated schema. This step adds composition checks without repeating all of those behavior tests.

The legacy `Permissions` entity/table represents generic resource access for project files. It lacks a required conversation, canonical tool ID and typed requirement. Neither its rows nor roles, ownership, approvals or execution state are tool grants. Ownership and approval remain independent execution gates; passing either does not grant tool permission. The exact source does not read `Permissions`, infer an allow from an owner/role/approval, or fall back to a first match. Grant issuance, revocation authorization and timing remain separate production concerns: a read can race with a later revocation before handler invocation.

## Fail-closed composition rule and changes

`ProductionRuntimeCompositionInspector` now pins Permission to **one** `IToolPermissionSource` descriptor with scoped lifetime, concrete `ToolPermissionPersistenceSource` implementation type, and an independent entry in the reviewed-production catalog. The catalog is intentionally empty. Factory, instance, duplicate, singleton, transient, unavailable or alternative/generic source registrations cannot pass. Even a future catalog entry without matching DI, or matching DI without reviewed catalog entry, cannot pass. The inspector inspects descriptors only; it does not resolve services, query PostgreSQL or invoke a tool.

Step 14G adds `ProductionPermissionCompositionChecks` and wires it into the offline suite. Checks cover all registration shapes above, exact model boundaries, absence of generic permission fallback and normal API registration, independence from ownership/approval/state/flags, unchanged runtime defaults and no new live or background path. Existing `RuntimeSafetyValidator` and `UnavailableRuntimeCapabilityReadiness` are unchanged.

| Composition field | Current value |
| --- | --- |
| Ownership | false |
| Permission | false |
| ExecutionState | false |
| AuditSink | false |
| AuditDelivery | false |
| Reconciliation | false |
| Verification | false |
| ExecutionPolicy | false |
| ToolHandlers | false |
| TrustedExecution | false |
| **CompositionComplete** | **false — NOT READY** |

No production tool execution, audit persistence, Safe File Access or Cloud AI is enabled. No migration, grant write, hosted worker, polling/retry loop, provider call or live harness is introduced.

## Remaining blockers and validation

Before Permission could be reviewed for production readiness, an operator must independently review the controlled PostgreSQL schema and migration baseline, a live exact-grant verification PASS, real JWT/user and ownership evidence, grant issuance/revocation policy and authorization timing, and the source's production DI lifetime. Only a separate reviewed change may register the exact source and add it to the catalog. Permission alone would still leave the other composition fields and the Step 13L final activation gate outstanding.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```
