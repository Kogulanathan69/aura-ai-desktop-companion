# Step 17R — immutable independent proof and Verified writer

## Evidence and decision

The operator-reported Step 17Q disposable-target migration application and seven-ID baseline passed on `aura_disposable_test` (database OID `24576`, system identifier `7696813047573034172`). The operator then ran the Step 17R harness manually on the same verified disposable target. Its sanitized output reported **PASS for all 25 categories and `RESULT: PASS`**: connectivity, schema, fixtures, authoritative observation, proof creation, handler-result independence, exact execution version, exact proof source/version, Verified persistence, seven negative bindings, stale execution version, wrong proof source/version, missing proof, failed execution, repeated assessments, state immutability, and cleanup. Credentials and process environment variables were reported cleared. This Codex process did not connect to PostgreSQL or rerun the live command.

**Step 17R operator-reported live harness: PASS. Verification readiness: false.** The PASS establishes the harness's bounded source, proof, writer, negative-case, repeated-row, state-immutability, and cleanup behavior for that disposable target. It is not a production composition or concurrency proof. The production evidence source, writer, and workflow graph remain unregistered. No production execution, handler, allowing policy, or trusted execution registration was enabled. Runtime flags and activation remain unchanged and denied.

## Proof and bindings

`OwnedProjectMetadataProof` is sealed and immutable, with no public constructor or setters. Its internal factory accepts a nonempty project ID, a nondefault UTC project update timestamp, and exact durable execution version 2. Proof source and version are fixed to `OwnedProjectMetadata` and `1`. The authoritative source creates it only after a repeatable-read snapshot checks the exact execution state, active user, project conversation, action, approval, and owned project. It receives no handler result.

`VerificationEvidence` now carries the durable execution version, terminal execution report, and independent proof. Only Verified evidence retains proof. The workflow checks exact action, user, conversation, project, tool, approval, execution, version, observation, proof source/version, and terminal success before issuing Verified. Missing evidence remains Inconclusive and failed execution remains Failed under the conservative policy. Proof alone never changes the conservative production policy to Verified.

The writer accepts Verified only with successful terminal status, execution version 2, exact tool, and immutable proof. In one repeatable-read transaction it rechecks the exact durable state, ownership/approval chain, and the observed owned project update timestamp against the proof, then inserts one assessment row. Advisory Failed/Inconclusive rows retain `None/0` proof. It does not update execution state, overwrite a row, retry, or store project content. A stale project timestamp or state version visible to that transaction rejects the write.

## Assessment history and limits

Each assessment has a new ID and is an immutable historical row. The live harness established two separately inserted Verified rows for one execution on the disposable target. Repeated Verified rows are allowed if each independently passes the writer's current proof and state checks. A later Failed or Inconclusive row can coexist as history; this foundation does not define a single authoritative current verdict. The current Step 14L inspector only evaluates an evidence-source registration, so a current-verdict selector is outside that observational composition rule. No production consumer currently interprets a latest row; define its policy before consuming these records as a single decision. Approval/ownership provenance across the broader production graph remains for review.

The proof factory is internal to Application and friend-visible to Infrastructure and SecurityChecks. `Aura.Api/Program.cs` registers no `IDbContextFactory<AuraDbContext>`, `IExecutionEvidenceSource`, `IExecutionVerificationAssessmentWriter`, or `IVerificationWorkflowService`. The reviewed verification catalog in `ProductionRuntimeCompositionInspector` is empty. Its current check only recognizes exactly one scoped catalog-reviewed evidence source; it does not attest that the writer, workflow, proof policy, or factory can resolve. `ConservativeVerificationPolicy` still cannot issue Verified. Adding a source catalog entry alone would therefore overstate production composition.

The writer rejects a project timestamp or execution version that is stale within its repeatable-read snapshot. The live harness's `STALE EXECUTION VERSION: PASS` tests a wrong version argument; it does not serialize a concurrent project update. A project update after the writer reads its snapshot could commit before the assessment commit. Step 14L requires stale evidence denial for production readiness, so that unresolved timing case is a blocker to registration and `Verification=true`. A future review must choose and test a transaction/locking rule, then validate the exact scoped source, writer, workflow, factory, policy, and catalog graph without duplicate or fallback registrations. The live PASS closes the bounded disposable persistence requirement only.

## Operator-only live harness

The command below is write-capable: it inserts generated disposable fixtures, inserts assessment rows, and removes them in cleanup. Run it only against the already verified disposable target. It is never invoked by the offline suite or API startup. The harness checks target identity and the exact seven-ID migration history before fixture writes, and reports every required category. `RESULT: PASS` requires every category, including cleanup; missing categories yield `RESULT: INCOMPLETE` or `FAIL`.

Required process environment:

```powershell
$env:AURA_LIVE_PRODUCTION_INDEPENDENT_VERIFICATION_EVIDENCE = 'true'
$env:AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE = 'true'
$env:AURA_EXPECTED_DATABASE_OID = '24576'
$env:AURA_EXPECTED_SYSTEM_IDENTIFIER = '7696813047573034172'
$env:ConnectionStrings__DefaultConnection = '<operator-supplied verified disposable connection; SSL Mode=VerifyFull>'
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-independent-verification-evidence
```

For the reviewed local portable server only, use host `127.0.0.1` or `::1`, port `5433`, database `aura_disposable_test`, `SSL Mode=Disable`, and additionally set `AURA_ALLOW_LOOPBACK_NON_TLS_DISPOSABLE=true`. Remote or other database non-TLS transport is rejected. The exception is confined to explicit evidence tooling. Do not share or record connection strings or credentials in output. Clear the environment variables after the run.

The operator supplied sanitized same-target PASS output after the first implementation turn. No live command was run in this Codex process. The reported target identity and category lines are recorded above without credentials or row contents.

## Validation

```powershell
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```
