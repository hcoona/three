# Archive-first transaction retirement protocol — insertion-ready v4 remediation

This text is normative. **MUST**, **MUST NOT**, **SHOULD**, and **MAY** have their RFC 2119 meanings.

## Insertion map

| Protocol text | Plan placement |
|---|---|
| Stable roots and archive-first bootstrap | Replace archive-role text in Sections 11.3, 11.6, and algorithm steps 6–13. |
| Correct states/Aborted invariant | Replace the state table in 11.7 and affected recovery rows in 11.10. |
| Stable replicas and terminal retirement | Replace replica-root/retention interaction in 11.6, 11.11, and 11.12; add Section 11.15. |
| Resolution-attempt closure | Replace/expand Section 11.13 and lifecycle rules in 13.2. |
| Retention integration | Add to Sections 11.12/11.14 and fixed-retention protection rules. |
| Interfaces, UI, tests, and gates | Add to Sections 10.4, 12.3–12.5, 13.2, 14, and 16. |

## 1. Stable storage roles

### Replace transaction/journal/backup layout

Transaction journals and backup payloads have different lifetimes and MUST NOT share a deletable retention unit.

For installation record `I` and transaction `T`:

```text
%LOCALAPPDATA%\CelesphoniaModifier\State\I\Transactions\T\authoritative.ndjson
%LOCALAPPDATA%\CelesphoniaModifier\State\I\Transactions\T\mirror.ndjson
%LOCALAPPDATA%\CelesphoniaModifier\State\I\transaction-retirement-ledger.ndjson
%LOCALAPPDATA%\CelesphoniaModifier\State\I\resolution-ledger.ndjson
%LOCALAPPDATA%\CelesphoniaModifier\State\I\retention-ledger.ndjson

<live-save-directory>\.celesphonia-modifier.T.execution.ndjson
<live-save-directory>\.fileN.rpgsave.T.stage|rollback|evidence
<live-save-directory>\.global.rpgsave.T.stage|rollback|evidence

%LOCALAPPDATA%\CelesphoniaModifier\Backups\I\<slot>\<UTC>-T\
```

The two State-root journals are stable and are never members of a backup payload set. `authoritative.ndjson` is logical authority. `mirror.ndjson` is its stable byte-for-byte replica. The live-side `execution.ndjson` is a temporary same-live-volume execution witness used only while the transaction is active or unresolved.

Backup retention MUST NOT delete either stable journal, any State ledger, or the State transaction directory. MVP does not compact or delete stable terminal journals. A later compaction design requires a new certification review.

The State and Backup roots use the existing random installation record ID, qualified ownership/DACL, link count one, no-reparse traversal, complete tuple capture, and compatible append-only readers.

## 2. Archive-first bootstrap with actual tuples

### Replace predicted archive tuples in `Prepared`

No journal record may contain a predicted file identity, link count, reparse state, or metadata/security tuple for a long-term backup file that does not yet exist.

Every operation (`Save`, `RestoreSlot`, and both `ReconcilePair` modes) creates and verifies its baseline archive **before `Prepared` and before any live replacement**.

The baseline archive contains exact copies of the transaction baseline slot and global:

- for `Save`, the immutable Open/Reload baseline pair;
- for `RestoreSlot`/`ReconcilePair`, the transaction-scoped current `RestoreStart`/`ReconcileStart` pair;
- for all-`NoOp` adoption, the unchanged current pair.

`RestoreSlot` and `ReconcilePair` use this same archive as `PreResolutionEvidence`; there is no second duplicate pair.

### Bootstrap records and order

Journal schema v3 introduces a pre-Prepared bootstrap phase. It uses the same three replicas and exact-byte replication protocol as logical records.

For a linked Conflict resolution, append/flush `ResolutionAttemptOpened` before step 1; its attempt/resolving transaction IDs are embedded in every later bootstrap, archive, journal, and retirement record.

1. **Create journal roots.** Create/validate stable transaction directory, authoritative journal, stable mirror, and live execution replica. Write/flush/verify compatible envelopes.
2. **Append `ArchiveBuildStarted`.** Record operation, transaction/baseline IDs, backup-set path relative to Backup root, source baseline participant tuples, the finite allowed relative-name set, manifest schema, capacity/profile facts, and optional resolution-attempt IDs. Replicate to all three journals before creating payload files.
3. **Create ownership marker.** Create-new `archive-bootstrap.json` containing ownership magic/version, transaction ID, allowed relative names, and source baseline hashes/lengths. Flush/reopen/capture its actual tuple; append/replicate `ArchiveBootstrapOwned`.
4. **Create archive files.** Write slot/global baseline bytes from the immutable transaction baseline to create-new backup files. After each file, flush, close, reopen, capture its actual full tuple, and append/replicate `ArchiveArtifactVerified(role, tuple)`.
5. **Revalidate source baseline.** Reopen current live participants and require exact equality with the transaction baseline tuples. A mismatch closes bootstrap without creating `Prepared`.
6. **Create immutable ownership manifest.** The create-new manifest contains transaction/operation/slot IDs, source baseline tuples, actual archive file tuples, relative names, profile IDs, lengths/hashes, and manifest digest. Flush/reopen/full-tuple verify it.
7. **Append `ArchiveVerified`.** Persist the actual ownership-marker, archive, and manifest tuples plus their digest. Replicate to all three journals.
8. **Create/verify Replace stages.**
9. **Append `Prepared`.** `Prepared` references the preceding `ArchiveVerified` record hash and repeats or embeds only actual verified archive tuples. No predicted archive identity is permitted.

No `ReplaceFileW` invocation is allowed unless `ArchiveVerified` and `Prepared` are present and all three replicas are synchronized.

### Bootstrap interruption

`ArchiveBuildStarted` without `Prepared` is an owned bootstrap, not an unknown orphan.

- If no live replacement invocation exists and live tuples still equal the transaction baseline, recovery may append `CancelledBeforePrepared` or `FailedBeforePrepared`, depending on user cancellation versus operational error.
- If an archive file/manifest is missing, changed, extra, reparse, multiply linked, or tuple-inconsistent, append `BootstrapConflict` and protect the set.
- A torn/unsupported bootstrap enters version-blocked recovery.
- Bootstrap closure is replicated to stable journals and execution replica, then proceeds through terminal retirement below.

An incomplete pre-Prepared set may be deleted later only when `archive-bootstrap.json`, the `ArchiveBuildStarted` allowed-name set, and every durable `ArchiveArtifactVerified` tuple prove editor ownership. An existing unrecorded artifact may be adopted into cleanup only after opening it without following reparse points, proving it is one of the allowed names and exact baseline bytes, capturing its actual tuple, and appending the missing verification event. Any extra or ambiguous artifact is `BootstrapConflict`.

## 3. Archive disposition after terminal outcome

The pre-created archive set receives an outcome class in the retirement ledger; the immutable manifest is not rewritten.

| Outcome | Backup-set disposition |
|---|---|
| `Committed` | `CommittedBackup`; participates in the fixed newest-20 policy and is user-restorable when compatible. |
| `CancelledBeforePrepared` | `EphemeralCancelled`; immediately retention-eligible after retirement completes. |
| `FailedBeforePrepared` | `EphemeralFailedBootstrap`; immediately retention-eligible after retirement completes. |
| `Aborted` | `EphemeralAborted`; immediately retention-eligible after retirement completes because no replacement invocation occurred and live baseline is proven. |
| `RolledBack` | `RolledBackAttempt`; protect for at least 24 hours and while it is newest for the slot, then normal verified retention eligibility. |
| unresolved `Conflict` | `UnresolvedConflictEvidence`; indefinitely protected from retention/lifecycle cleanup. |
| resolved `Conflict` | `ResolvedConflictEvidence`; eligible only after durable resolution completion, retirement completion, and the normal 24-hour/newest-per-slot protection. |
| `RecoveryVersionBlocked`, `ResolutionVersionBlocked`, `BootstrapConflict`, or unreadable state | `VersionOrIntegrityBlocked`; protected indefinitely until a compatible recovery completes. |

Retention eligibility never implies immediate deletion. Every deletion still uses the retention ledger.

Because the baseline archive is verified before replacement, post-forward `Committed` no longer depends on copying rollback images to long-term storage. Rollback/evidence siblings remain short-lived recovery artifacts and are handled by retirement.

### Replace the unified operation algorithm's archive/commit sequence

1. Validate operation-specific preconditions/dispositions and materialize/validate in-memory candidates.
2. If resolving a Conflict, append `ResolutionAttemptOpened`.
3. Create the stable journals and execution replica; append `ArchiveBuildStarted`.
4. Create, flush, reopen, full-tuple verify, and manifest the baseline archive; revalidate current live baseline; append `ArchiveVerified`.
5. Create/verify only required `Replace` stages.
6. Append/replicate `Prepared`; require stable and execution replicas synchronized.
7. Revalidate live baseline/process/profiles/capacity.
8. Satisfy slot and global dispositions using synchronized invocation intents and complete tuple projections.
9. Verify the complete intended live pair and append `Verified`.
10. Append authoritative terminal `Committed`; then exact-suffix replicate it to stable mirror and execution replica.
11. Append `ResolutionAttemptClosed(CommittedResolved)` when applicable.
12. Start terminal retirement; do not perform a post-Verified archive-copy phase.

Cancellation/failure before `Prepared` uses bootstrap closure and attempt closure. Failure after any replacement invocation uses rollback/Conflict and can never use `Aborted`.

## 4. Correct logical state machine and `Aborted` invariant

### Replace Section 11.7 state table

Schema v3 distinguishes bootstrap outcomes from prepared transaction outcomes:

| State | Meaning | Allowed next states |
|---|---|---|
| `Initializing` | Journal envelopes exist; archive bootstrap may be in progress. | `Prepared`, `CancelledBeforePrepared`, `FailedBeforePrepared`, `BootstrapConflict` |
| `CancelledBeforePrepared` | User cancelled before `Prepared`; no replacement invocation exists. | Terminal bootstrap state |
| `FailedBeforePrepared` | Operational failure before `Prepared`; no replacement invocation exists. | Terminal bootstrap state |
| `BootstrapConflict` | Bootstrap/archive facts cannot be reconciled safely. | Terminal blocked state |
| `Prepared` | Actual archive tuples, manifest, plan, candidates/stages, and all replicas verify. | `SlotSatisfied`, `Aborted`, `RollbackPending`, `Conflict` |
| `SlotSatisfied` | Slot disposition is fulfilled. | operation-dependent transitions below |
| `GlobalSatisfied` | Global disposition is fulfilled. | `Verified`, `RollbackPending`, `Conflict` |
| `Verified` | Complete intended live pair tuples, congruence, validation, and pre-created archive verify. | `Committed`, `RollbackPending`, `Conflict` |
| `Committed` | Authoritative terminal record is durable. | Terminal logical state |
| `RollbackPending` | At least one replacement was invoked/completed and durable rollback intent exists. | `RolledBack`, `Conflict` |
| `RolledBack` | Every replaced participant has exact restored/evidence tuples; every `NoOp` remains exact. | Terminal logical state |
| `Aborted` | `Prepared` existed, but **no replacement invocation-intent record exists in any valid replica and no replacement evidence exists**; live participants remain exact baseline/NoOp tuples. | Terminal logical state |
| `Conflict` | Facts cannot authorize automatic action. | Terminal logical state; resolution uses another transaction |

### Exact transition conditions

- `Prepared -> Aborted` is allowed only when every valid replica contains zero replacement invocation-intent records and artifact tuples show no invocation/replacement evidence.
- `SlotSatisfied(NoOp) -> Aborted` is allowed only for a plan whose global disposition is `Replace`, before the global invocation intent, when every valid replica contains zero replacement invocation intents and artifacts are noncontradictory.
- `SlotSatisfied(Replace)` cannot transition to `Aborted`.
- `GlobalSatisfied` cannot transition to `Aborted`.
- `Verified` cannot transition to `Aborted`.
- `RollbackPending` cannot transition to `Aborted`.
- Any transaction containing any forward or rollback replacement invocation-intent record can terminate only `Committed`, `RolledBack`, or `Conflict`.
- `ResolutionOnly` all-`NoOp` reconciliation permits `Prepared -> SlotSatisfied(NoOp) -> GlobalSatisfied(NoOp) -> Verified -> Committed|Conflict`; it never permits `Aborted`.

An append validator MUST reject any `Aborted` record whose chain contains a replacement invocation intent or whose predecessor/operation condition is not allowed.

## 5. Stable journal replication

### Replace dual-replica semantics

Logical records use this order:

1. append exact canonical bytes to State authoritative;
2. flush/reopen/verify; logical state advances;
3. append identical bytes to State mirror;
4. flush/reopen/verify;
5. append identical bytes to live execution replica while it is required;
6. flush/reopen/verify.

`StableReplicaSyncState` describes authoritative/mirror (`InSync`, `AuthoritativeAhead`, `MirrorMissing`, `Divergent`, `AuthoritativeUnreadable`). `ExecutionReplicaState` is `RequiredInSync`, `RequiredLagging`, `RetirementPending`, `Retired`, or `Blocked`.

Before every live replacement, the invocation-intent record MUST be synchronized to all three replicas. A crash after the authoritative append but before mirror/execution append advances logical state but cannot authorize the associated `ReplaceFileW` until exact suffix repair completes.

After any replacement, observed/satisfaction records may be authoritative-ahead after a crash; recovery uses complete tuples plus exact suffix repair. Terminal `Committed`, `RolledBack`, `Aborted`, `Conflict`, and bootstrap terminal states become logical truth when their authoritative record is flushed. Stable mirror/execution synchronization is separate.

Exact suffix repair copies authoritative raw record bytes only to an exact-prefix stable mirror or required execution replica. It never reserializes, truncates, merges divergence, or changes logical state.

If stable mirror is longer, diverges by byte/sequence/envelope, or contains a record after terminality, set `Divergent` and block mutation/retirement. If authoritative is missing/unreadable/corrupt, set `AuthoritativeUnreadable`; stable mirror and execution replica are diagnostic evidence only and cannot become authority automatically. A deliberately absent execution replica is recognized only through valid `RetirementCompleted`.

## 6. Terminal transaction retirement

### Add Section 11.15 — Retirement ledger/protocol

The append-only versioned retirement ledger is:

```text
%LOCALAPPDATA%\CelesphoniaModifier\State\I\transaction-retirement-ledger.ndjson
```

It uses the same app-owned-root validation, stable envelope, source-generated canonical records, hash chain, flush/reopen verification, backward readers, and torn-final-record policy as other State ledgers.

Retirement removes only temporary live-directory replicas/artifacts. Stable authoritative/mirror journals and all State ledgers remain.

### Retirement eligibility

A transaction is eligible only when:

- authoritative logical state is a supported terminal state;
- State authoritative/mirror are exact and `InSync`;
- execution replica contains the exact terminal prefix;
- no unresolved/unknown resolution attempt references artifacts being removed;
- archive set/manifest actual tuples verify;
- terminal outcome's archive disposition from Section 3 is determined;
- for `Conflict`, durable `ResolutionAttemptClosed(CommittedResolved)` exists and every temporary artifact selected for removal has an explicit `DeleteTemporary` disposition plus complete tuple in `RetirementIntent`; any artifact policy requires preserving is already in a verified ownership manifest;
- no version, replica, tuple, ownership, or retention block exists.

Unresolved Conflict, open resolution attempt, version-blocked state, divergent stable journals, or authoritative loss is not retirement-eligible.

### Retirement records

`RetirementIntent` contains retirement schema/ID, transaction ID, terminal state/record hash, State authoritative/mirror tuples and terminal prefix hash, execution-replica tuple, complete tuples for every remaining stage/rollback/evidence sibling, archive/manifest IDs and outcome disposition, resolution status, quarantine relative path, and expected post-retirement absences.

Subsequent record kinds:

| Kind | Meaning |
|---|---|
| `ArtifactsQuarantined` | Every existing execution/stage/rollback/evidence sibling was moved by same-volume rename into a transaction-specific hidden quarantine directory and exact tuples verify there. |
| `ArtifactsDeleted` | All manifest-listed quarantined files were deleted; quarantine is verified empty. |
| `RetirementCompleted` | Empty quarantine directory was removed; execution replica and temporary artifacts are durably declared retired. |
| `RetirementBlocked` | Facts diverged; remaining data is preserved and retirement stops. |

### Retirement execution/recovery

1. Revalidate eligibility and append/flush `RetirementIntent`.
2. Create-new quarantine directory `.celesphonia-modifier.T.retiring`.
3. For each recorded sibling, require it at either original or quarantine path with the exact tuple, never both. Rename original to quarantine and verify.
4. After all roles are uniquely classified in quarantine or expected missing, append/flush `ArtifactsQuarantined`.
5. Delete only quarantined files whose tuples still match. Missing files during resume are accepted only after `ArtifactsQuarantined`.
6. Verify quarantine empty; append/flush `ArtifactsDeleted`.
7. Remove empty quarantine directory; append/flush `RetirementCompleted`.

Startup resume:

- `Intent` only: reconcile original/quarantine locations by exact tuples. Exactly one location per present role may continue; both/neither/unexpected is `RetirementBlocked`.
- `ArtifactsQuarantined`: original paths must be absent; resume verified deletion.
- `ArtifactsDeleted`: quarantine must be empty or absent; remove if needed and complete.
- `RetirementCompleted`: no live-directory replica/artifact is required.
- `RetirementBlocked`: no automatic deletion.

Crash after every rename/delete/append is therefore idempotently classifiable.

### Replica-health after retirement

Active/recovery transactions require the execution replica. A transaction with valid `RetirementCompleted` is `Retired`; execution-replica absence and temporary-artifact absence are expected and excluded from write/lifecycle replica-health gating.

Terminal proof for a retired transaction is:

1. stable authoritative/mirror exact terminal journals;
2. valid `RetirementCompleted`;
3. either the archive payload/manifest still exists and verifies against journaled actual tuples, or a valid retention-ledger `Completed` record links the same manifest digest and proves its intentional deletion.

Backup retention may delete a retired payload set without creating `MirrorMissing`, because neither stable journal is inside that set. Missing payload after valid retention completion is expected and does not reclassify the terminal transaction.

## 7. Conflict-resolution attempt closure

### Replace/expand Section 11.13

Each resolution attempt has exactly one versioned open record and one durable closure record in `resolution-ledger.ndjson`.

`ResolutionAttemptOpened` contains:

- `resolutionAttemptId`;
- original `resolvesTransactionId` and terminal Conflict record hash;
- planned resolving transaction/bootstrap ID;
- operation/mode;
- baseline/archive IDs;
- app/schema/profile versions;
- start time.

`ResolutionAttemptClosed` contains the same linkage plus exactly one outcome:

| Outcome | Required proof | Original Conflict resolved? | Retry effect |
|---|---|---:|---|
| `CancelledBeforePrepared` | Bootstrap terminal cancellation; no invocation; live baseline exact. | No | Retry after bootstrap retirement. |
| `FailedBeforePrepared` | Bootstrap terminal failure; no invocation; live baseline exact or safely blocked. | No | Retry after bootstrap retirement if not blocked. |
| `Aborted` | Resolving transaction terminal Aborted and no invocation anywhere. | No | Retry after retirement. |
| `RolledBack` | Resolving transaction terminal RolledBack; exact restored/evidence tuples. | No | Retry after retirement; failed-attempt archive remains protected per policy. |
| `Conflict` | Resolving transaction terminal Conflict. | No | New attempt is blocked until this newer Conflict is itself resolved/retired. |
| `VersionBlocked` | Resolution ledger is readable, but the resolving bootstrap/transaction schema is unsupported; no unsafe transaction outcome is inferred. | No | Retry only in a compatible version after reclassification/retirement. |
| `CommittedResolved` | Resolving transaction terminal Committed, stable journals `InSync`, archive verified, and relationship fields match. | **Yes** | No further attempt for the original Conflict. |

`ResolutionAttemptClosed(CommittedResolved)` replaces the prior separate success-only completion meaning; readers map older `ResolutionCompleted` to this outcome.

The original Conflict is resolved only by one valid `CommittedResolved` closure. Every other closure leaves it unresolved.

### Pre-Prepared cancellation/failure

If the user cancels or an error occurs after `ResolutionAttemptOpened` but before `Prepared`:

1. classify bootstrap/archive facts;
2. append `CancelledBeforePrepared` or `FailedBeforePrepared` to the resolving bootstrap journal when safe;
3. append/flush the matching `ResolutionAttemptClosed`;
4. retire the bootstrap execution replica/artifacts;
5. mark its pre-created archive `EphemeralCancelled`/`EphemeralFailedBootstrap`.

An open attempt is never silently abandoned. Startup finds every `ResolutionAttemptOpened` without closure and either resumes/classifies it or leaves it open/blocking; it never permits another attempt concurrently. If the resolution ledger itself is unsupported/corrupt, no closure can be appended and the attempt remains `ResolutionVersionBlocked` until a compatible reader is available.

### Retry eligibility and lifecycle

A new attempt for the original Conflict may start only when:

- the previous attempt has a readable durable closure;
- its stable replicas are `InSync`;
- its retirement is complete;
- no child Conflict from the failed attempt remains unresolved;
- no version/resolution/retention ledger block exists.

Open attempts, `Conflict`/`VersionBlocked` closures, unresolved child Conflicts, `RetirementBlocked`, and retirement-in-progress block normal upgrade/uninstall and another attempt. Closed `CancelledBeforePrepared`, `FailedBeforePrepared`, `Aborted`, or `RolledBack` attempts do not resolve the original Conflict but become retry-eligible after retirement.

UI statuses are:

- **Resolution cancelled before changes — original Conflict remains unresolved**
- **Resolution failed before changes — original Conflict remains unresolved**
- **Resolution attempt aborted — retry available after cleanup**
- **Resolution attempt rolled back — retry available after cleanup**
- **Resolution attempt created another Conflict — resolve the newest Conflict first**
- **Compatible version required to continue this resolution**
- **Conflict resolved by committed transaction**

## 8. Retention and lifecycle integration

### Update protection/eligibility rules

Retention MUST protect:

- every pre-created archive until its bootstrap/transaction is terminal and retirement outcome disposition is durable;
- every archive for an open or unclosed resolution attempt;
- every unresolved/child Conflict and VersionBlocked attempt;
- every State journal/ledger and every retirement quarantine;
- every transaction not yet `RetirementCompleted`;
- game `.bak` files.

Retention may select:

- `EphemeralCancelled`/`EphemeralFailedBootstrap` only after `RetirementCompleted` and either a verified immutable manifest or a verified bootstrap ownership marker plus allowed-name/per-artifact tuple records;
- `EphemeralAborted` only after `RetirementCompleted` and verified immutable manifest;
- `RolledBackAttempt` only after retirement plus 24-hour/newest-per-slot protection;
- `CommittedBackup` under the fixed newest-20 policy after retirement;
- `ResolvedConflictEvidence` only after `CommittedResolved`, retirement, and protection expiry.

The retention ledger's intent records terminal/attempt/retirement references. Revalidation immediately before rename must prove they remain eligible.

### Lifecycle rules

Upgrade/repair/uninstall preflight additionally classifies:

- open/closed resolution attempts and their outcomes;
- transaction/bootstrap retirement state;
- stable journal sync independent of retired execution replica;
- pre-created archive outcome disposition;
- retention deletion/retirement in progress.

Lifecycle proceeds only when every transaction/bootstrap is terminal, stable journals are readable and `InSync`, every required retirement is completed, every resolution attempt is closed, no unresolved Conflict/VersionBlocked state exists, and retention/retirement ledgers are readable/not blocked.

A deliberately retired execution replica or retention-deleted payload is not a missing-replica error when the corresponding durable ledger completion proves it.

## 9. Required interfaces and UI

### Add to Section 10.4

- `IArchiveBootstrapService`
- `IArchiveManifestWriter`
- `IStableJournalStore`
- `IExecutionJournalReplica`
- `ITransactionRetirementLedger`
- `ITransactionRetirementService`
- `IResolutionAttemptStore`

`ITransactionOperationPlanner` reserves and identifies the archive set. `IArchiveBootstrapService` creates/verifies actual artifacts before `Prepared`. `ISaveTransactionWriter` receives only actual archive tuples. `IJournalReplicaCoordinator` manages two stable replicas plus the temporary execution replica. `IRetentionService` consumes retirement/archive dispositions rather than inferring them from missing files.

### UI truth model

Recovery/history surfaces distinguish:

- bootstrap state;
- logical transaction state;
- stable-replica synchronization;
- execution-replica active/retirement state;
- archive disposition;
- resolution-attempt open/closed outcome;
- retention state.

User-visible completion examples:

- **Backup verified; save prepared**
- **Save committed; temporary recovery files are being retired**
- **Save committed; retirement will resume next launch**
- **Resolution attempt rolled back; original Conflict remains unresolved**
- **Transaction retired; missing temporary journal is expected**

UI MUST NOT show `MirrorMissing` for a valid retired execution replica, call a failed attempt resolved, call `Verified` aborted, or claim an archive exists before `ArchiveVerified`.

## 10. Acceptance and failure-injection tests

### Archive timing

1. For every allowed operation/disposition, create baseline archive files/manifest before `Prepared`; assert actual archive tuples exist in `ArchiveVerified` and `Prepared`.
2. Static/schema tests reject predicted/nonexistent archive identities and any `Prepared` without a referenced verified manifest.
3. Crash after journal-envelope creation, `ArchiveBuildStarted`, ownership-marker write and `ArchiveBootstrapOwned`, each archive file write/flush and `ArchiveArtifactVerified`, source revalidation, manifest write/flush, `ArchiveVerified` on each replica, every stage, and `Prepared`; classify exactly.
4. Change live source during archive creation; bootstrap closes without `Prepared` or live replacement.
5. Corrupt/missing/extra/reparse/hard-linked archive artifacts before `Prepared`; enter BootstrapConflict and protect set.
6. Assert outcome dispositions: Committed retained, cancelled/failed/Aborted delete-eligible after retirement, RolledBack protected 24 hours, unresolved Conflict/version blocked protected.
7. Interrupt before the immutable manifest exists; cleanup is permitted only from the verified bootstrap marker, allowed-name set, and actual per-artifact tuples.

### State/Aborted invariants

8. Validate the complete transition table for each operation/disposition.
9. Reject `Verified -> Aborted`, `GlobalSatisfied -> Aborted`, `RollbackPending -> Aborted`, and `SlotSatisfied(Replace) -> Aborted`.
10. Reject any `Aborted` chain containing any forward or rollback invocation-intent record in any valid replica, even when all live bytes equal baseline.
11. Allow `Prepared -> Aborted` only with zero invocation records and exact baseline tuples.
12. Allow `SlotSatisfied(NoOp) -> Aborted` only for a pending global `Replace`, zero invocation records, and exact tuples.
13. All-`NoOp` reconciliation always reaches Verified then Committed or Conflict; it never reaches Aborted.

### Stable replicas and retirement

14. Crash after each authoritative, stable-mirror, and execution-replica append for every bootstrap/logical/terminal record; logical state and repair permissions match protocol.
15. No `ReplaceFileW` occurs until its invocation intent is present in all three replicas.
16. Retention deletion of a payload set never deletes stable journals or produces missing-replica blocking.
17. Crash before/after RetirementIntent, quarantine creation, every artifact rename, ArtifactsQuarantined, every deletion, ArtifactsDeleted, directory removal, and RetirementCompleted; startup follows exact resume rules.
18. After RetirementCompleted, execution journal/stage/rollback/evidence absence is expected and excluded from health gating.
19. Original/quarantine both present, both absent before quarantine proof, tuple mismatch, extra file, hard link, reparse, or unresolved Conflict enters RetirementBlocked without deletion.
20. Stable terminal journals plus retirement completion plus retention completion continue to prove a retired transaction after payload deletion.

### Resolution-attempt closure

21. Every ResolutionAttemptOpened receives exactly one durable closure or remains explicitly open/blocking.
22. Inject cancellation/failure before archive, after archive, after stages, and before Prepared; assert correct bootstrap/attempt closure and retry eligibility.
23. Resolver terminal Aborted/RolledBack/Conflict/VersionBlocked/Committed maps to the exact closure outcome.
24. Only CommittedResolved marks the original Conflict resolved.
25. Crash after resolving Committed but before attempt closure; startup appends CommittedResolved only after stable replicas/archive/relationship revalidation.
26. Closed Cancelled/Failed/Aborted/RolledBack permits retry only after retirement; Conflict requires resolving newest child Conflict; VersionBlocked requires compatible recovery.
27. Attempt artifacts use the protection/retention policy for their closure outcome.
28. Upgrade/uninstall blocks open attempts, child Conflict, VersionBlocked, retirement in progress, or unreadable closure records; verified completed resolution/retirement proceeds.

### Retention/lifecycle/cross-version

29. Retention revalidates terminal outcome, attempt closure, retirement completion, archive disposition, and protection immediately before intent/rename.
30. Retention never deletes stable State journals/ledgers, retirement quarantine, unresolved attempt/Conflict artifacts, or game `.bak`.
31. Test every released journal/archive-bootstrap/archive-manifest/resolution-attempt/retirement/retention schema across all states and torn-record positions.
32. Unknown schema/corruption preserves archives/artifacts and blocks only the unsafe mutation paths specified.
33. Controlled end-to-end tests cover Save, Restore `NoOp/Replace`, global repair, all-`NoOp` adoption, rollback, failed retry, successful retry, retirement, payload retention deletion, restart, and later lifecycle preflight.

## 11. Roadmap and definition-of-done additions

Phase 0 exit additionally requires archive-first bootstrap, actual archive tuples, schema-v3 states/readers, stable State journals, three-replica active protocol, retirement ledger, attempt-closure outcomes, and every failure vector above.

Phase 1 exit additionally requires no write before verified baseline archive, no false Aborted path, successful retirement after each terminal outcome, retry after every eligible failed resolution attempt, fixed retention without replica-health regression, and installer preflight over retired/deleted-payload histories.

Product definition of done includes:

- no predicted archive file identity in `Prepared`;
- no terminal proof stored only in a deletable backup set;
- no deliberately retired execution replica treated as missing;
- no unclosed failed resolution attempt;
- no `Aborted` after any replacement invocation;
- no retention deletion before retirement/attempt eligibility is durable.

## Windows API basis

`ReplaceFileW` requires replacement, replaced, and backup paths on one volume; gives the result the replacement file's identity; optionally moves the replaced file to the backup name; merges documented metadata; and does not support `REPLACEFILE_WRITE_THROUGH`: <https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-replacefilew>.

`FlushFileBuffers` is used for each writable file/ledger/journal handle, subject to Windows/device durability guarantees: <https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers>.
