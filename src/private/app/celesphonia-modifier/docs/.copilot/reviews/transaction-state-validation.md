# Transaction, Recovery, Resolution, Replica, Backup, and Retention Validation v4

**Status:** Normative correction, insertion-ready  
**Scope:** Replaces the state-machine semantics in Sections 11.1, 11.6–11.14 and corresponding failure-injection requirements in Section 12.3. It does not change operation preconditions, tuple strength, or the fixed NTFS qualification boundary.

## 1. Validation result

The design is implementable only if independent state projections are maintained. A single transaction enum cannot also represent replica repair, Conflict resolution, backup lifetime, or retention progress.

The corrected protocol removes four known defects:

1. `Prepared` never predicts identity or metadata for an archive file that does not exist. Required immutable backup content is created and full-tuple verified before `Prepared`; future artifacts use content/role specifications, not invented tuples.
2. Retention durably retires a terminal journal mirror before deleting it. A retired mirror is not `MirrorMissing` and cannot block later operations.
3. Every unsuccessful Conflict-resolution attempt receives a durable closing record. It does not resolve the original Conflict, but does not remain forever in progress.
4. `Verified` cannot transition to `Aborted` or `RollbackPending`. Once replacement may have occurred, `Aborted` is impossible; after `Verified`, only `Committed` or `Conflict` is legal.

The protocol also closes the retention rename gap: a crash after source-to-tombstone rename but before `Renamed` is appended is recoverable from exact identity/tuple evidence.

## 2. Product state is a product of machines

```text
SystemState(T) =
  LogicalTransactionState(T)
× ParticipantExecutionState(T, Slot)
× ParticipantExecutionState(T, Global)
× ReplicaState(T)
× ResolutionTargetState(target(T))
× ResolutionAttemptState(attempt(T))
× BackupSetState(backupSet(T))
× RetentionOperationState(retentionOp(backupSet(T)))
× ObservedRoleVector(T)
```

No projection may overwrite another. Terminal logical state derives only from the authoritative transaction journal. Replica repair, resolution, backup deletion, retention, and later filesystem observations never rewrite it.

### 2.1 Logical transaction state

```text
Initializing
Prepared
SlotSatisfied
GlobalSatisfied
Verified
RollbackPending
Committed       terminal
RolledBack      terminal
Aborted         terminal
Conflict        terminal
```

`Initializing` begins with durable `TransactionStarted` and covers backup preparation, candidate/stage preparation, and linked resolution setup before `Prepared`.

### 2.2 Participant execution state

Each participant has immutable disposition `Replace` or `NoOp`:

```text
Pending
NoOpSatisfied
ReplaceIntentDurable
ReplaceResultObserved
ReplaceSatisfied
RollbackIntentDurable
RollbackResultObserved
RollbackSatisfied
```

A `NoOp` participant has no stage, replacement intent/call, rollback file, or rollback call. It is satisfied only by a fresh exact full-tuple comparison.

A `Replace` participant is satisfied only after synchronized durable intent, the OS call or recovery classification, a unique exact post-forward role vector, and a durable result/satisfaction record.

### 2.3 Replica state

```text
ReplicaLifecycle = Provisioning | Active | Retired
ReplicaSync =
  NotApplicable
  InSync
  LiveAheadExactSuffix
  MirrorAbsentUnexpected
  AuthoritativeUnreadable
  Divergent
```

- `Provisioning` covers startup through successful Prepared mirror synchronization; participant replacement is forbidden.
- `Active` begins only after `Prepared` is `InSync`; every later mutation-enabling intent must also be `InSync` before mutation.
- `Retired` is monotonic and requires valid durable `ReplicaRetired` outside the deletable backup set.
- `Retired × NotApplicable` is the only valid post-retirement state.
- An absent retired mirror is expected, never `MirrorAbsentUnexpected`/`MirrorMissing`.

### 2.4 Resolution target and attempt state

The original terminal Conflict has:

```text
ResolutionTargetState =
  UnresolvedReady
  AttemptOpen(resolutionId)
  CommittedAwaitingCompletion(resolutionId)
  Resolved(resolutionId, resolvingTransactionId)
  ResolutionVersionBlocked
```

Each attempt independently has:

```text
ResolutionAttemptState =
  Opened
  ResolverInitializing
  ResolverPrepared
  ResolverExecuting
  ResolverCommittedAwaitingCompletion
  Completed
  ClosedFailed(SetupFailed | Aborted | RolledBack | Conflict)
  AttemptVersionBlocked
```

`ClosedFailed` is terminal for the attempt, not the original Conflict. After a failed attempt closes, the original returns to `UnresolvedReady`; a fresh resolution/resolver ID may be created, while failed IDs remain immutable and nonreusable.

### 2.5 Backup-set state

Backup state is itself a record of orthogonal fields, not one overloaded enum:

```text
BackupSetState = { Content, Protection, Lifetime }

Content =
  NotCreated | Building | BaseContentVerified |
  OutcomeContentBuilding | OutcomeContentVerified | ContentBlocked

Protection = Protected | Unprotected

Lifetime =
  Active | RetentionEligible | Retiring | Retired | Deleted
```

- Save base content is the exact Open/Reload baseline pair.
- Restore/Reconcile base content is the exact transaction-start current pair (`PreResolutionEvidence`), including all-`NoOp` adoption.
- `BaseContentVerified` means all immutable pre-mutation content and its immutable data manifest have concrete verified tuples.
- The mutable journal mirror is infrastructure, not immutable backup data and not part of the commit data-manifest hash.
- Rollback candidate evidence, when required, uses an immutable outcome manifest referencing the base manifest; the base manifest is never rewritten.
- `Protection=Protected` applies to every nonterminal transaction, unresolved Conflict, open/blocked attempt, unknown schema, and active selection.
- `Lifetime=Deleted` never changes a previously durable terminal outcome.

### 2.6 Retention operation state
```text
RetentionOperationState =
  Idle
  ReplicaRetired
  IntentDurable
  SourceRenamed
  Deleting(snapshotSubset)
  ReadyToRemove
  Completed
  StoppedBeforeDestruction
  RetentionRecoveryBlocked
```

The retention ledger is outside deletable sets and is never compacted/deleted in MVP.

## 3. Operation and disposition matrix

| Operation/mode | Slot | Global | Transaction class | Result |
|---|---:|---:|---|---|
| `Save` | Replace | Replace | `MutatingPair` | Allowed. |
| `Save` | Replace | NoOp | `MutatingPair` | Allowed. |
| `Save` | NoOp | Replace | any | Forbidden; use global repair. |
| `Save` | NoOp | NoOp | any | No transaction; no semantic edit. |
| `RestoreSlot` | Replace | Replace | `MutatingPair` | Allowed. |
| `RestoreSlot` | Replace | NoOp | `MutatingPair` | Allowed. |
| `RestoreSlot` | NoOp | Replace | `MutatingPair` | Allowed only for exact selected-entry derivation. |
| `RestoreSlot` | NoOp | NoOp | any | No Restore transaction; **Already matches**. Linked adoption routes to Reconcile. |
| `ReconcilePair/RepairSelectedGlobalEntry` | NoOp | Replace | `MutatingPair` | Allowed. |
| `ReconcilePair/AdoptCongruentCurrentPair` | NoOp | NoOp | `ResolutionOnly` | Allowed only with one eligible unresolved Conflict. |
| any `ReconcilePair` | Replace | either | any | Forbidden. |
| any unlisted combination | any | any | any | Forbidden before backup/stage creation. |

Byte-identical candidate content normalizes to `NoOp`; replacement is never metadata-only.

## 4. Normative invariants

Let `I(T)` be the set of durable `ReplaceIntent` records. Because a crash can occur between intent and the OS call, intent is the conservative invocation boundary: no call occurs without synchronized intent, and recovery assumes the call may have occurred once intent is durable.

### 4.1 Abort and invocation

The literal global statement “`Aborted` iff no replacement invocation” is incompatible with required all-`NoOp` committed resolution, which commits with no replacement invocation. It is also too strong when pre-invocation evidence is unreadable and only `Conflict` is safe.

Use these exact implementable invariants:

```text
LogicalState(T) = Aborted  => I(T) = ∅
I(T) ≠ ∅                  => LogicalState(T) ≠ Aborted
Abort transition allowed  => I(T) = ∅
                             and exact unchanged pre-forward/NoOp vector is proven
                             and no contradictory artifact exists
```

For a non-committing attempt with `I(T)=∅`, exact unchanged proof yields `Aborted`; inability to prove it yields `Conflict`. `ResolutionOnly` is the sole successful `Committed` case with `I(T)=∅`.

### 4.2 Commit proof is a transition-time fact

An authoritative `Committed` append is legal iff immediately before append:

```text
LogicalState(T) = Verified
and every Replace participant matches its exact candidate live tuple
and every NoOp participant matches its exact unchanged tuple
and the pair is congruent and fully valid
and BackupSetState.Content is BaseContentVerified or OutcomeContentVerified
and the referenced immutable data manifest and listed concrete tuples reverify
```

The terminal record freezes hashes/IDs of this proof. Later live changes or authorized retention do not change `Committed`; UI must distinguish “committed at time X” from “currently still live.”

### 4.3 Terminal immutability

```text
Terminal = {Committed, RolledBack, Aborted, Conflict}
LogicalState(T) ∈ Terminal => every later SystemState(T).LogicalState = LogicalState(T)
```

No authoritative transaction record follows a terminal record. Mirror suffix copying, resolution, replica retirement, backup retirement, and retention are external lifecycle records.

### 4.4 Exact-suffix-only replica repair

Repair is authorized iff:

```text
ReplicaLifecycle ∈ {Provisioning, Active}
and authoritative chain is valid and interpretable
and mirror is absent or an exact byte prefix of authoritative
and transaction ID, envelope, schema, and common bytes are identical
and destination ownership/path/profile checks pass
```

Repair copies original missing bytes, flushes, reopens, and verifies. It never reserializes, truncates, merges, votes, or repairs a longer/divergent mirror. `AuthoritativeUnreadable` and `Divergent` preserve/block.

### 4.5 Deliberate durable retirement

A mirror may stop being required only when:

1. logical state is terminal;
2. active replicas are `InSync` at the terminal hash;
3. no unresolved/nonterminal/version-blocked transaction or resolution references the set;
4. backup retention eligibility passes; and
5. `ReplicaRetired(transactionId, terminalRecordHash, authoritativeTuple, mirrorTuple, backupSetId, reason, operationId)` is flushed and verified in the nondeletable retention ledger.

Thereafter `ReplicaLifecycle=Retired`, `ReplicaSync=NotApplicable`, and physical mirror deletion is allowed. Retirement is monotonic and not a logical transition.

### 4.6 Conflict and resolution protection

```text
Original Conflict unresolved
or attempt Opened/Executing/CommittedAwaitingCompletion/VersionBlocked
=> every referenced original/resolver journal, stage, rollback, evidence,
   backup set, manifest, and ledger record is Protected
```

Resolver terminal `Aborted`, `RolledBack`, or `Conflict` with terminal replica `InSync` MUST be followed by idempotent `ResolutionAttemptClosed`, which leaves the original unresolved, preserves evidence, and permits a later fresh attempt. Exact-repairable lag is repaired first; unrepairable replica state becomes `AttemptVersionBlocked`, not an eternally open attempt. Only resolver `Committed` + terminal mirror `InSync` + verified backup proof + durable `ResolutionCompleted` resolves the original.

### 4.7 Retention evidence safety

For every filesystem deletion `Delete(x)`:

```text
valid RetentionIntent exists
and x is in its exact manifest snapshot
and x matches its complete tuple, or is an allowed already-deleted subset after SourceRenamed
and no unresolved/protected relationship references x
and if x is a transaction mirror, ReplicaLifecycle = Retired
and no unknown, extra, reparse, multiply-linked, escaped, or changed object exists
```

Retention never deletes the authoritative live-side journal, either state ledger, unresolved work, active selections, game `.bak`, or unknown/version-blocked artifacts.

### 4.8 `Prepared` contains only knowable facts

Before `Prepared`, every required immutable backup file, base data manifest, and Replace stage exists and has a concrete verified tuple. `Prepared` may contain:

- concrete tuples for existing live, stage, base-backup, and base-manifest objects;
- explicit `Missing` tuples for roles that must currently be absent;
- profile-qualified projections when identity comes from an already existing live/stage object moved by `ReplaceFileW`; and
- future artifact specifications containing role, path, source content hash/length, profile, and derivation.

`Prepared` MUST NOT contain a future copy's invented identity, timestamps, ACL/security digest, metadata digest, link-count observation, or final tuple. Record those only after create/flush/reopen/full-tuple verification.

## 5. Allowed logical transitions

| From | To | Guard |
|---|---|---|
| none | `Initializing` | Authoritative `TransactionStarted` durable; plan IDs and owned paths fixed. |
| `Initializing` | `Prepared` | Base content/manifest and Replace stages concretely verified; Prepared reaches `InSync`. |
| `Initializing` | `Aborted` | No replacement intent; exact unchanged live vector; owned drafts classified. |
| `Initializing` | `Conflict` | Exact unchanged/draft ownership facts cannot be proven or contradict. |
| `Prepared` | `SlotSatisfied` | Slot NoOp exact, or synchronized intent plus exact Replace result. |
| `Prepared` | `Aborted` | No replacement intent and exact unchanged vector. |
| `Prepared` | `Conflict` | Intent ambiguity, replica block, or contradictory roles. |
| `SlotSatisfied` | `GlobalSatisfied` | Global NoOp exact, or synchronized intent plus exact Replace result. |
| `SlotSatisfied` | `Aborted` | No replacement intent anywhere; exact unchanged vector. Possible only when slot was NoOp. |
| `SlotSatisfied` | `RollbackPending` | A Replace is satisfied, forward cannot safely continue, and exact rollback prerequisites exist. |
| `SlotSatisfied` | `Conflict` | Neither exact continuation nor rollback is authorized. |
| `GlobalSatisfied` | `Verified` | Complete intended pair, validation, congruence, allowed diff, and backup proof pass. |
| `GlobalSatisfied` | `Aborted` | No replacement intent and exact unchanged vector; all-`NoOp` failure before verification. |
| `GlobalSatisfied` | `RollbackPending` | Replacement occurred and verification fails while exact rollback remains authorized. |
| `GlobalSatisfied` | `Conflict` | Intended/rollback vector is not uniquely authorized. |
| `Verified` | `Committed` | Commit invariant passes; authoritative terminal append succeeds. |
| `Verified` | `Conflict` | Commit-time proof fails and a safe authoritative Conflict append remains possible; otherwise stay `Verified` with replica/recovery block. |
| `RollbackPending` | `RolledBack` | Every Replace restored; NoOp exact; candidate evidence/outcome manifest verifies. |
| `RollbackPending` | `Conflict` | Exact rollback result/evidence cannot be proven. |

`Verified -> Aborted`, `Verified -> RollbackPending`, and `Verified -> RolledBack` are forbidden.

## 6. Forbidden transitions and actions

The implementation MUST reject or preserve/block:

1. Any transition out of `Committed`, `RolledBack`, `Aborted`, or `Conflict` in the same journal.
2. `Aborted` when any durable replacement intent exists.
3. `Committed` without preceding durable `Verified` and verified concrete backup manifest.
4. `RolledBack` without `RollbackPending`, exact restored tuples, and required candidate evidence.
5. `Prepared` before `BackupSetState.Content=BaseContentVerified` or while Prepared replicas are not `InSync`.
6. Any replacement/rollback call without its intent record `InSync`.
7. Any stage, intent, or call for a `NoOp` participant.
8. Any participant satisfaction inferred from hash equality alone.
9. Suffix repair when mirror is longer/divergent/differently enveloped or authoritative is unreadable.
10. Reclassifying a terminal state because live files later changed.
11. Reclassifying a retired mirror as missing/unhealthy.
12. Deleting an active mirror before durable retirement.
13. Resolving an original Conflict from resolver `Aborted`, `RolledBack`, `Conflict`, or merely `Committed` without completion.
14. Leaving a failed, classifiable resolver attempt open after recovery.
15. Opening concurrent attempts for one Conflict or reusing IDs.
16. Selecting protected/unresolved artifacts for retention.
17. Continuing retention after unknown/extra/changed/reparse/multiply-linked content.
18. Treating predicted future archive identity/metadata as an observed tuple.
19. Rewriting a base manifest to add outcome evidence; use an immutable child outcome manifest.

## 7. Smallest coherent protocol

### 7.1 Initialization and baseline backup preparation

1. Validate operation/disposition, allocate transaction/backup/resolution IDs, and freeze the immutable plan.
2. If resolving, append/flush `ResolutionAttemptOpened` before creating the resolving transaction journal or any evidence. A crash here closes as `SetupFailed`.
3. Append/flush authoritative `TransactionStarted`. Before `Prepared`, absent mirror means `Provisioning`, not `MirrorMissing`.
4. Create the owned backup directory and provision the mirror by exact-copying the authoritative prefix; verify `InSync`.
5. Create-new and full-tuple verify both base backup files:
   - Save: Open/Reload baseline slot and global;
   - Restore/Reconcile: transaction-start slot and global (`PreResolutionEvidence`);
   - all-`NoOp` adoption: the same current pair.
6. Append `BackupFileVerified` after each concrete verification.
7. Create/verify an immutable base data manifest containing only immutable backup data; append `BackupSetBaseVerified`.
8. Materialize/verify stages only for `Replace` participants.
9. Append identical `Prepared` bytes authoritative then mirror; require `InSync`.

Failure before `Prepared` can be `Aborted` only with no intent and exact unchanged live proof. Owned partial files may be removed/recreated only under exact ownership; unknown content is preserved and blocks.

### 7.2 Forward execution and live verification

For Slot then Global:

- `NoOp`: reopen, capture, compare exact unchanged/missing-role vector, append `ParticipantSatisfied(NoOp)` to both replicas.
- `Replace`: append/verify `ReplaceIntent` to both replicas; rerun final race gate; call `ReplaceFileW`; capture the complete role vector; append `ReplaceResult`/`ParticipantSatisfied(Replace)` only for the unique projected post-forward vector.

Before each later filesystem mutation, repair exact suffix lag and require the current logical prefix `InSync`.

After both participants are satisfied, revalidate intended pair, congruence, validation, allowlist, and concrete backup proof. Append `Verified` to both replicas. `Verified` is roll-forward-only: append authoritative `Committed`, then exact terminal bytes to mirror.

### 7.3 Rollback

Rollback is available only before `Verified` and only after at least one Replace is satisfied.

1. Append/verify `RollbackPending` to both replicas.
2. Roll back Replace participants in reverse order with synchronized rollback intent, call, complete vector classification, and result record.
3. Reverify every NoOp unchanged tuple.
4. If required, copy/verify candidate evidence and write an immutable outcome manifest referencing the base manifest.
5. Append authoritative `RolledBack`; mirror by exact suffix.
6. Any zero/multiple/unexpected vector becomes `Conflict` when authoritative append remains safe.

### 7.4 Conflict-resolution lifecycle

- `ResolutionAttemptOpened` starts one attempt and sets target `AttemptOpen`, only after the target Conflict is readable, terminal, unresolved, and `InSync`.
- Setup failure appends `ResolutionAttemptClosed(SetupFailed)`.
- Resolver terminal `Aborted`, `RolledBack`, or `Conflict` first reaches terminal replica `InSync`, then appends `ResolutionAttemptClosed` idempotently; original returns to `UnresolvedReady`. Unrepairable replica state is `AttemptVersionBlocked`.
- Resolver `Committed` with mirror lag is `CommittedAwaitingCompletion`; exact repair is required.
- Resolver `Committed` + `InSync` + verified backup/relationship facts permits one idempotent `ResolutionCompleted`.

### 7.5 Replica retirement and retention

1. Select only eligible, unprotected backup set whose terminal active replica is `InSync`.
2. Append/flush/verify `ReplicaRetired` in retention ledger. Mirror synchronization becomes `NotApplicable`, even while mirror remains present.
3. Append/flush `RetentionIntent` with exact source/tombstone/manifest snapshot and retirement hash.
4. Rename source to tombstone.
5. Append `Renamed`. If crash occurred after rename, append it only when source is absent, tombstone present, and directory identity plus every listed tuple exactly match Intent.
6. Delete only exact listed files; delete immutable manifests and retired journal mirror last.
7. Verify empty; append `ReadyToRemove`.
8. Remove directory; append `Completed`.

If retirement is durable but retention stops before deletion, retirement remains valid and the present mirror is optional evidence. A later operation may reselect the set after full revalidation.

## 8. Crash and failure vectors

Every append means write, flush, close, reopen, and hash-chain/canonical-byte verification.

| Checkpoint | Restart observation | Required recovery result |
|---|---|---|
| After authoritative journal create, before `TransactionStarted` | Empty/torn owned journal | No transaction state; preserve unless exact app ownership permits removal. |
| After authoritative `TransactionStarted`, before mirror | Valid `Initializing`; `Provisioning` | Resume provisioning or exact abort; never terminal `MirrorMissing`. |
| After backup directory creation | Owned draft referenced by start record | Resume; remove on abort only if exact and empty. Unknown content blocks. |
| During/after mirror initial copy | Absent/torn/exact-prefix mirror | Recopy exact authoritative bytes while Provisioning; divergence blocks. |
| After mirror verifies | `Initializing × Provisioning/InSync` | Continue backup preparation; no participant mutation legal. |
| During base backup file creation | Missing/partial/complete unrecorded file | With no intent, recreate only if exact owned path and no unknown content; otherwise protect/block. |
| After file verifies, before `BackupFileVerified` | Concrete matching tuple, record absent | Reverify source relationship and append idempotently, or recreate before invocation. |
| After authoritative `BackupFileVerified`, before mirror | Backup projection advances; live-ahead | Exact suffix repair; do not recreate verified file. |
| After mirror `BackupFileVerified` | InSync | Continue. |
| During base manifest creation | Missing/partial/unrecorded manifest | Recreate only under exact ownership/no invocation; never infer verified. |
| After manifest verifies, before `BackupSetBaseVerified` | Concrete manifest exists | Reverify all listed tuples and append idempotently. |
| After authoritative `BackupSetBaseVerified`, before mirror | Backup verified; live-ahead | Exact suffix repair. |
| During/after Replace stage creation | Missing/partial/complete stage | Before Prepared/intent, recreate or abort only under exact ownership; unknown tuple blocks. |
| After authoritative `Prepared`, before mirror | `Prepared`; live-ahead; no call allowed | Exact repair. Then abort only if no intent/exact pre-vector; never call while lagging. |
| After mirror `Prepared` | `Prepared × Active/InSync` | Rerun final race gates. |
| After authoritative `NoOpSatisfied`, before mirror | Participant advanced; live-ahead | Exact repair; revalidate before next mutation. |
| After mirror `NoOpSatisfied` | InSync satisfaction | Continue. |
| After authoritative `ReplaceIntent`, before mirror | Intent exists; live-ahead | Exact repair; call forbidden until intent InSync. |
| After mirror `ReplaceIntent`, before call | Intent InSync; live pre-forward | If abandoned, intent prevents Aborted; exact pre-vector still becomes Conflict. |
| During/after `ReplaceFileW`, before result | Pre-forward, post-forward, or Other vector | Unique post-forward permits result append; pre-forward with intent is Conflict; Other/ambiguous is Conflict. |
| After authoritative Replace result, before mirror | Satisfaction authoritative; live-ahead | Exact repair; never repeat call. |
| After mirror Replace result | InSync | Continue or guarded rollback. |
| After authoritative `RollbackPending`, before mirror | Rollback required; live-ahead | Exact repair before rollback intent/call. |
| After synchronized rollback intent, before call | Intent durable | Resume classification/call policy; never forward-Abort. |
| During/after rollback call, before result | Pre/post/Other rollback vector | Unique post-rollback permits result; ambiguity/contradiction is Conflict. |
| During candidate evidence/outcome manifest | Base immutable; outcome incomplete | Protect/resume exact-owned creation; `RolledBack` not legal yet. |
| After authoritative `Verified`, before mirror | `Verified`; live-ahead | Exact repair, then revalidate: append `Committed`, append `Conflict` if safely recordable, or remain `Verified` with recovery block; never abort/rollback. |
| After mirror `Verified` | `Verified × InSync` | Append `Committed` if commit proof still passes. |
| After authoritative terminal append, before mirror | Terminal logical state; live-ahead | Preserve terminal and copy exact terminal suffix. Never rollback/downgrade. Applies to all terminal kinds. |
| After mirror terminal append | Terminal × InSync | Execution complete; linked resolution/retirement may proceed. |
| After `ResolutionAttemptOpened` | Attempt open; original protected | Resume setup or append `ClosedFailed(SetupFailed)`. |
| After resolver terminal failure, before close | Resolver terminal readable; attempt appears open | Revalidate relationship and append close idempotently. Original stays unresolved. |
| After `ResolutionAttemptClosed` | Attempt failed terminal; original `UnresolvedReady` | Permit fresh attempt ID; retain evidence while original unresolved. |
| After resolver `Committed`, before mirror InSync | `CommittedAwaitingCompletion` | Repair exact suffix; do not complete resolution yet. |
| After resolver InSync, before `ResolutionCompleted` | All success facts exist | Revalidate and append completion idempotently. |
| After `ResolutionCompleted` | Original resolved | Never reopen/duplicate; artifacts only potentially retention-eligible. |
| After `ReplicaRetired` | Mirror may remain; lifecycle Retired | Never report missing-mirror repair. Resume/retry retention independently. |
| After `RetentionIntent`, before rename | Source present, tombstone absent | Revalidate/rename or append `StoppedBeforeDestruction`. |
| After rename, before `Renamed` | Source absent, exact tombstone present | Recognize rename gap and append `Renamed` idempotently. Both/neither/changed layouts block. |
| After `Renamed` | Tombstone sole source | Resume deletion from exact snapshot. |
| After any listed deletion | `Renamed`; missing set is snapshot subset | Accept subset, verify every remainder exactly, continue. Unknown/changed remainder blocks. |
| After retired mirror deletion | Lifecycle Retired | Continue retention; never `MirrorMissing`. |
| After last deletion, before `ReadyToRemove` | Exact empty tombstone | Append `ReadyToRemove` idempotently. |
| After `ReadyToRemove`, before directory removal | Empty tombstone | Remove and append `Completed`. |
| After directory removal, before `Completed` | Both paths absent; Ready durable | Append `Completed` idempotently. |
| After `Completed` | Backup deleted | Replay does nothing; logical transaction unchanged. |

## 9. Automated model and property tests

### 9.1 Reference model

Implement a pure model:

```text
Model = {
  operation, mode, slotDisposition, globalDisposition,
  logicalState,
  slotProgress, globalProgress,
  durableIntentSet,
  authoritativeRecords, mirrorBytes,
  replicaLifecycle, replicaSync,
  resolutionTarget, resolutionAttempts,
  backupSetState, retentionState,
  roleVector, protectedArtifactSet
}
```

Commands:

```text
StartTransaction
ProvisionMirror
CreateBackupFile(role)
VerifyBackupFile(role)
CreateBaseManifest
VerifyBaseManifest
CreateStage(participant)
AppendPrepared(replica)
SatisfyNoOp(participant, tupleClass)
AppendReplaceIntent(participant, replica)
InvokeReplace(participant, outcomeVector)
AppendReplaceResult(participant, replica)
AppendVerified(replica)
AppendRollbackPending(replica)
AppendRollbackIntent(participant, replica)
InvokeRollback(participant, outcomeVector)
AppendTerminal(kind, replica)
RepairExactSuffix
ExternalMutate(role, mutationKind)
OpenResolutionAttempt
CloseResolutionAttempt
CompleteResolution
RetireReplica
AppendRetentionIntent
RenameToTombstone
DeleteListedFile(role)
AppendReadyToRemove
RemoveTombstone
AppendRetentionCompleted
CrashAndRecover
```

Tuple classes include exact pre-forward, exact post-forward, exact post-rollback, hash-equal/new-identity, wrong volume, link-count change, reparse, metadata/security mismatch, missing, unreadable, swapped, duplicated, extra, and zero/multiple matches.

### 9.2 Bounded state-space exploration

Exhaustively explore:

- every allowed/forbidden operation-disposition row;
- both participants and dispositions;
- every append split into authoritative-only, exact-prefix mirror, torn mirror, and InSync;
- every filesystem mutation with crash before/after its following record;
- every Replace/rollback vector class;
- all four terminal states;
- resolution success plus SetupFailed/Aborted/RolledBack/Conflict outcomes;
- retention crashes before/after retirement, intent, rename, each deletion, ReadyToRemove, directory removal, completion.

Use an abstract filesystem and breadth-first search through terminal execution plus optional resolution/retention. Hash/tuple values may be symbolic; identity aliasing must be explicit.

### 9.3 Required properties

1. **Operation matrix:** only listed rows reach `Initializing`; prohibited rows create no artifact.
2. **Backup-before-Prepared:** every `Prepared` has concrete verified base manifest and no predicted tuple for nonexistent archive copy.
3. **No premature mutation:** every forward/rollback call has matching intent in both replicas and prior prefix `InSync`.
4. **NoOp purity:** NoOp never owns stage, intent, call, rollback, or evidence role; it satisfies only by exact tuple equality.
5. **Abort safety:** `Aborted` implies empty intent set and exact unchanged live proof.
6. **All-NoOp resolution:** adoption reaches `Committed` with empty intent set only through Prepared, both NoOp satisfactions, Verified, backup verification, terminal mirror sync, and `ResolutionCompleted`.
7. **Commit safety:** at Commit transition, candidate/NoOp vectors, congruence, validation, and backup proof hold.
8. **Verified monotonicity:** no trace contains `Verified -> Aborted`, `Verified -> RollbackPending`, or `Verified -> RolledBack`.
9. **Terminal immutability:** crashes, external changes, replica repair, resolution, retirement, and retention preserve terminal value/hash.
10. **Exact suffix:** repair changes only absent/exact-prefix mirror into exact equality; longer/divergent/unreadable cases do nothing.
11. **Terminal lag:** authoritative terminal plus mirror prefix remains terminal and never enables rollback.
12. **Durable retirement:** mirror deletion is unreachable before `ReplicaRetired`; afterward absence never yields `MirrorMissing` or blocks new transactions.
13. **Resolution closure:** every classifiable failed attempt reaches `ClosedFailed`; original stays unresolved and fresh attempt is allowed.
14. **Resolution success:** original resolves iff one matching resolver is Committed, mirror InSync, backup proof valid, and exactly one completion exists.
15. **Conflict protection:** while original unresolved or attempt open/blocked, no referenced artifact enters retention deletion.
16. **Retention eligibility:** selected set is terminal/resolved, unprotected, exact, policy-eligible, and retired before deletion.
17. **Rename-gap recovery:** crash after rename/before record converges when exact and blocks without deletion otherwise.
18. **Deletion subset:** after `Renamed`, only an exact subset of listed files may be missing; every remainder matches full tuple.
19. **Idempotent recovery:** applying recovery twice yields identical state except one permitted missing idempotent completion record.
20. **Evidence conservation:** no trace deletes evidence required by nonterminal, unresolved, open/blocked, or version-blocked work.
21. **Terminal partition:** every safely classifiable finite attempt ends in exactly one terminal state; no trace has two terminal records.
22. **Record legality:** versioned transition validator accepts a generated record iff the model guard is true.

### 9.4 Property-based fault generation

Generate a valid operation plan, then interleave:

- one crash at every pre/post durable boundary;
- one I/O failure at every create, flush, reopen, append, replace, rename, delete, and directory removal;
- zero or one external tuple mutation at every race gate;
- replica outcomes `{success, torn, absent, exact-prefix, longer, divergent}`; and
- recovery repetitions `{1, 2, 3}`.

Compare implementation state and enabled commands to the pure product-state model. Persist minimized failures as golden recovery vectors.

## 10. Required plan corrections and hidden contradictions

Apply these corrections consistently in the algorithm, transition table, UI projection, retention rules, and tests:

1. Replace “Prepared contains complete archive tuples for future archive files” with pre-Prepared concrete base-backup verification plus future artifact specifications.
2. Remove `Verified -> Aborted` and `Verified -> RollbackPending`; use `Verified -> Committed | Conflict` only.
3. Add `ResolutionAttemptClosed` and distinguish original Conflict state from each attempt state.
4. Add durable `ReplicaRetired` outside deletable sets and exclude retired mirrors from missing-mirror classification.
5. Change retention Intent-only recovery to accept the exact source-absent/tombstone-present rename gap.
6. State commit/live/backup requirements as facts proven at the Commit transition. Otherwise terminal immutability contradicts later external live changes and authorized backup deletion.
7. Qualify the abort biconditional. Literal “Aborted iff no replacement invocation” contradicts all-`NoOp` committed adoption and pre-invocation Conflict. The exact rule is: Aborted implies no intent; intent forbids Aborted; no-intent successful `ResolutionOnly` may Commit; unprovable no-intent state becomes Conflict.
8. Keep durable retirement outside the transaction journal. The rule forbidding records after terminal otherwise contradicts deliberate post-terminal mirror retirement.

With these corrections, Save, RestoreSlot, global repair, and all-`NoOp` adoption share one implementation-ready protocol without weakening tuple authorization, terminal immutability, or retention protection.



