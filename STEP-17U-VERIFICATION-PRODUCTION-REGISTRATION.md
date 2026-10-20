# Step 17U — Verification production registration review

## Prior evidence

Step 17T's sanitized operator report established PASS for the Project.Version migration, the eight-migration baseline, and all 31 independent verification harness categories on `aura_disposable_test` (OID `24576`, system identifier `7696813047573034172`). Project.Version is the authoritative freshness token. This review did not rerun PostgreSQL or change that proof.

## Database graph

Before this step, `Aura.Api/Program.cs` called `AddDbContext<AuraDbContext>` with Npgsql and pgvector, then mapped `IAuraDbContext` to the scoped context. No `IDbContextFactory<AuraDbContext>` existed at API startup, while both the evidence source and assessment writer require one.

Now one `AddDbContextFactory<AuraDbContext, AuraDbContextFactory>` call configures the connection and pgvector. EF supplies a scoped `AuraDbContext` from that factory registration. The scoped `IAuraDbContext` alias resolves the same scoped object. The project-owned `AuraDbContextFactory` receives the singleton `DbContextOptions<AuraDbContext>` and creates independent contexts from those options. There is one configuration block, no EF internal implementation type in the catalog, and no singleton captures a scoped dependency. Offline scope validation built a provider with `ValidateScopes` and `ValidateOnBuild`, resolved the factory, scoped context, alias, source, and writer, and compared factory/scoped provider and connection configuration without opening a database connection.

## Registered foundations and reviewed catalog

| Contract | Exact implementation | Lifetime | Status |
| --- | --- | --- | --- |
| `IDbContextFactory<AuraDbContext>` | `AuraDbContextFactory` | Singleton | Registered and cataloged |
| `AuraDbContext` | EF factory-supplied context | Scoped | Resolved |
| `IAuraDbContext` | Scoped alias to `AuraDbContext` | Scoped | Resolved |
| `IExecutionEvidenceSource` | `OwnedProjectMetadataExecutionEvidenceSource` | Scoped | Registered and cataloged |
| `IExecutionVerificationAssessmentWriter` | `ExecutionVerificationAssessmentPersistenceWriter` | Scoped | Registered and cataloged |
| `IVerificationWorkflowService` | `VerificationWorkflowService` | Scoped intended | Not registered or cataloged |
| `IIndependentVerificationPolicy` | No reviewed allowing implementation | Scoped intended | Not registered or cataloged |

The source and writer registrations are unique, exact, scoped, and factory-resolvable. The inspector's Verification rule also requires the workflow, policy, scoped verification scope validator, and scoped `ToolRegistry`; source plus writer plus factory cannot assert readiness. The workflow and policy reviewed sets remain empty. Offline negative checks cover missing factory/source/writer, duplicate or fallback source, wrong writer lifetime, unresolved workflow dependencies, and incomplete catalog.

## Workflow blockers

`VerificationWorkflowService` requires `ToolRegistry`, `IVerificationScopeValidator`, `IExecutionEvidenceSource`, `IIndependentVerificationPolicy`, and `IDateTimeProvider`; its optional audit writer defaults to an unavailable writer. API startup supplies the clock and now the evidence source, but no ToolRegistry, verification scope validator, or verification policy. The real scope implementation is `ExactOwnershipScopeValidator`, whose authenticated identity and ownership source graph is only available through the future-only ownership registration helper. Normal startup does not invoke that helper. Registering a fake or unavailable validator would not establish ownership.

An empty `ToolRegistry` can be constructed safely but always returns UnknownTool in this workflow, so it cannot make the reviewed tool-specific verification path functional. A populated registry requires reviewed tool descriptors and handlers in later work. The existing `ConservativeVerificationPolicy` returns Failed or Inconclusive and cannot issue Verified; cataloging it as an allowing independent proof policy would misstate readiness.

No production service invokes `IExecutionVerificationAssessmentWriter.WriteAsync`. The workflow issues bounded evidence and returns it, but does not call the writer. The live harness explicitly invokes source and writer separately, proving those components but not an end-to-end production workflow. Adding writer invocation now would change workflow return and audit ordering without a complete ownership, tool-registry, policy, and caller contract. That integration remains a separate blocker; no duplicate or speculative write path was added.

## Readiness and runtime

**Verification=false.** The complete workflow graph, a reviewed policy, exact scope and tool registry composition, and a single bounded writer invocation path remain unproven. The inspector derives false from these missing nodes; it does not hardcode a readiness value. Ownership, Permission, ExecutionState, AuditSink, AuditDelivery, Reconciliation, ExecutionPolicy, ToolHandlers, and TrustedExecution remain false. CompositionComplete remains false. Runtime production flags retain false defaults, final approval is unavailable, and activation is denied. No API startup migration, handler, allowing execution policy, or fallback was added.

Step 17U stops at the safely resolvable factory/source/writer foundations. Later reviews must close the workflow dependencies and persistence integration before Verification can become true.
