# Step 17S — verification freshness and composition review

## Evidence and result

Step 17R's operator-reported independent verification harness passed all 25 categories on the verified disposable `aura_disposable_test` target (OID `24576`, system identifier `7696813047573034172`). After the Step 17S change, the operator manually ran the revised harness on the same target and supplied sanitized **PASS for all 26 categories, including `CONCURRENT PROJECT UPDATE: PASS`, `STATE IMMUTABILITY: PASS`, `CLEANUP: PASS`, and `RESULT: PASS`**. Credentials and process environment variables were reported cleared. This Codex process did not connect to PostgreSQL or run the live command. **Verification remains false.**

## Freshness rule

The proof carries the owned project's persisted `UpdatedAt`, observed separately from handler output. For a Verified insert, the writer now begins a repeatable-read transaction, checks the exact terminal execution and ownership/approval chain, then executes a parameterized `SELECT "UpdatedAt" ... WHERE "Id" = @projectId AND "UserId" = @userId FOR UPDATE`. It requires equality with the proof timestamp and holds the project row lock through the assessment insert and commit. A committed update before the writer's snapshot has a different timestamp and fails the comparison. If a concurrent update reaches the row after the snapshot but before the lock, PostgreSQL repeatable-read must abort the locking read; the writer returns false without retry. An update after the lock waits until the assessment commits. No execution-state row is modified.

The new `CONCURRENT PROJECT UPDATE` harness category uses generated disposable fixtures. After proof observation it changes the project name and `UpdatedAt`, requires the stale proof's Verified write to fail with no row, obtains fresh authoritative proof, requires the fresh write to succeed, and then checks the execution state and cleanup. The operator-reported PASS establishes these bounded outcomes on the verified target. The test deterministically covers an update between observation and persistence. It does not create two overlapping writer/update transactions, so it does not directly observe lock contention or serialization behavior. The old `STALE EXECUTION VERSION: PASS` only covered the version argument.

`ProjectService.UpdateAsync` sets `UpdatedAt` to its clock value, but `Project.UpdatedAt` is not a database-managed monotonic version or concurrency token. Its EF configuration requires the value but does not enforce a distinct value on each update. Same-precision updates and writers outside the reviewed service can preserve a timestamp. The lock closes the after-read commit window for an update that touches the project row; it does not prove every meaningful project change produces a distinct timestamp. The proof observes only project identity, owner and this timestamp, not a wider content snapshot. A database-enforced monotonic row version or an independently verified invariant that every relevant project update changes the persisted token is still needed to claim universal stale-proof rejection. No schema or migration was changed. The operator PASS covers changed-timestamp fixtures, not this missing guarantee.

## Historical assessment semantics

Assessments remain immutable historical observations with distinct IDs. Repeated Verified rows were exercised by the Step 17R live harness. No current-verdict selection is defined and no production consumer reads these rows as a single authorization decision. Step 14L's observational verification rule does not require a canonical latest verdict. A later consumer must define an explicit trusted ordering or pointer before claiming a current result; this step does not invent one.

## Production DI and catalog

The exact intended graph is one scoped `IExecutionEvidenceSource` → `OwnedProjectMetadataExecutionEvidenceSource`, one scoped `IExecutionVerificationAssessmentWriter` → `ExecutionVerificationAssessmentPersistenceWriter`, one scoped `IVerificationWorkflowService` → `VerificationWorkflowService`, and a compatible, explicitly reviewed `IDbContextFactory<AuraDbContext>`. Existing API startup registers a scoped `AuraDbContext` for ordinary services but no factory or verification graph. Both source and writer require the factory; no choice between `AddDbContextFactory`, pooled factory, and the existing scoped context has been validated in production DI. The workflow additionally requires `ToolRegistry`, `IVerificationScopeValidator`, `IIndependentVerificationPolicy`, and `IDateTimeProvider`; normal startup provides only the clock. The ownership foundation's scope-validator registration is future-only. `ConservativeVerificationPolicy` cannot produce Verified, no allowing production verification policy has been reviewed, and the workflow currently issues evidence without invoking the writer. Merely registering the three classes would therefore not establish an end-to-end Verified persistence path.

The composition inspector now requires the exact source, writer, workflow and factory descriptors with single registrations and reviewed catalog membership. The reviewed sets remain empty. A source alone cannot assert Verification. Its descriptor check does not yet prove that every workflow dependency resolves or that workflow evidence reaches the writer; this must be covered before any catalog entries are populated. No wildcard, scanning, reflection registration, duplicate, factory-lambda fallback, handler, execution policy, or trusted execution registration was added. Despite the live PASS, the timestamp-token guarantee and factory/policy/end-to-end graph remain incomplete, so adding production registrations or catalog entries now would be premature. The source/assessment writer are application foundations, not production activation.

Broader ownership and approval provenance belongs to the Ownership and TrustedExecution capability reviews, but the verification source still depends on their exact state and must be re-reviewed with the complete graph. Their separate readiness fields remain false. Verification, ToolHandlers, ExecutionPolicy, TrustedExecution and CompositionComplete remain false; runtime flags retain false defaults, final approval is unavailable, and activation remains denied.

## Operator command and validation

The revised live command remains `--live-production-independent-verification-evidence`, with the Step 17R disposable target, transport and identity gates. It has 26 required categories. The operator supplied the new sanitized same-target PASS, including `CONCURRENT PROJECT UPDATE: PASS`, `STATE IMMUTABILITY: PASS`, `CLEANUP: PASS`, and `RESULT: PASS`. Harness existence alone would not have established those results; the supplied run did. The remaining production blockers above are separate from this bounded live result.

```powershell
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
git status --short
git diff --stat
```
