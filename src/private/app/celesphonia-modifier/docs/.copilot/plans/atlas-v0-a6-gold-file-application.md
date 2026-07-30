# Atlas V0 A6R4 Gold file application plan

## Document control

- **Audience:** Celesphonia Modifier product leadership, implementers, reviewers, and release-gate
  owners.
- **Purpose:** Define the smallest safe increment that applies one released G6R3 Gold mutation to
  one canonical save-slot file.
- **Authority:** Conditional governing-plan candidate. It grants no implementation, filesystem-write,
  operation, or private-execution authority before a verified shared `R6R4`.
- **Status:** Proposed A6R4 plan candidate, based on released `G6R3` commit
  `c7568573f3a8c6ecf89409ab40e5480dfaf01b18`.
- **Relationship to higher authority:** Repository and project agent instructions and
  `project-operating-model.md` remain controlling. This plan narrows A6R4 mechanics only. Released
  G6R3 remains the mutation authority and is not modified or reinterpreted here.
- **Lifecycle:** Active only as a conditional plan candidate. A verified shared `R6R4` makes this
  document active normative for A6R4 implementation. `G6R4` releases only the library behavior
  defined here.

## Decision summary

A6R4 will add a Windows-only library API that applies one Gold value to one canonical
`file1.rpgsave` through `file20.rpgsave` slot. It will use the released G6R3 reader and mutation
kernel, preserve one fixed adjacent archive-first backup, use one fixed adjacent candidate stage,
and classify the actual filesystem state when `File.Replace` reports an expected replacement
failure.

The trusted local single-user risk model is mandatory. The design prevents credible accidental
save loss while deliberately excluding journals, ledgers, runtime Git checks, attestation,
multi-file transactions, recovery services, automatic reconciliation, and other high-maintenance
machinery.

## Outcome and user value

After `G6R4`, a later explicitly authorized operation increment can invoke one reviewed library
method to update one confirmed canonical slot while:

1. preserving a verified baseline backup before the first changed edit;
2. refusing ambiguous backup, staging, source-change, or replacement states;
3. retaining enough fixed artifacts for safe manual inspection or an identical retry; and
4. avoiding claims of infallible atomicity or automatic recovery.

A6R4 itself performs no private execution and provides no end-user command surface.

## Threat model and safety posture

### In scope

- One trusted local user.
- The game and other save writers are closed before use.
- Accidental interruption, ordinary local I/O failure, an observable external replacement or
  change before the final replacement call, and an expected `File.Replace` exception.
- A single save-slot file and three fixed adjacent artifact paths on the same volume.
- Preservation of a completed backup and conservative refusal when state cannot be classified.

### Accepted residual risks

- Another local process can replace or change the live path after the final exact reread and before
  or during `File.Replace`. This final-call race is accepted only under the explicit game-closed,
  trusted-local operating assumption.
- `File.Replace` provides the official .NET same-volume replacement semantics, but this plan does
  not claim that replacement is infallible or that every filesystem or hardware failure is atomic.
- The application retains only one completed baseline backup. If the user deletes it, a later
  changed edit creates a new baseline from the then-current source. The application cannot recover
  the deleted older baseline.
- A proven failed replacement intentionally retains the fixed candidate stage. A different desired
  value then conflicts until the user resolves that artifact outside A6R4.

### Out of scope

- Adversarial local processes, hostile filesystems, concurrent writers, or a running game.
- Multiple slots, global/config saves, cross-file consistency, transactions, rollback, or recovery.
- Journals, ledgers, manifests, hashes in artifact names, GUID artifact names, versioned recovery
  protocols, automatic stale-stage reconciliation, or cleanup services.
- Runtime Git state, binary attestation, approval tokens, request receipts, or provenance ceremony.
- Private save access, private execution, original-data experiments, telemetry, or evidence capture.

## Scope

### In-scope implementation

After activation, the complete implementation candidate is limited to:

1. new
   `src\private\app\celesphonia-modifier\Hcoona.CelesphoniaModifier.Atlas\AtlasGoldFileApplication.cs`;
2. new
   `tests\private\app\celesphonia-modifier\Hcoona.CelesphoniaModifier.Atlas.Tests\AtlasGoldFileApplicationTests.cs`;
3. existing
   `tests\private\app\celesphonia-modifier\Hcoona.CelesphoniaModifier.Atlas.Tests\ProjectBoundaryTests.cs`.

The SDK-style projects must discover the new source and test files without project-file changes.

### Explicit exclusions

A6R4 adds no CLI command, project-file edit, schema, request file, operation receipt, cleanup API,
reader change, mutation-kernel change, or seam change. It does not modify any existing production
file.

## Public contract

All public types are in the existing Atlas library namespace and reside in the one new production
file.

```csharp
public static class AtlasGoldFileApplication
{
    public static ValueTask<AtlasGoldFileApplicationDisposition> ApplyAsync(
        string slotPath,
        long value,
        AtlasSaveReaderLimits limits,
        CancellationToken cancellationToken = default);
}
```

```csharp
public enum AtlasGoldFileApplicationDisposition
{
    Unchanged,
    AppliedWithBackupCreated,
    AppliedWithBackupPreserved,
}
```

```csharp
public enum AtlasGoldFileApplicationFailure
{
    UnsupportedPlatform,
    UnsupportedSlotPath,
    BackupConflict,
    StagingConflict,
    SourceChanged,
    ReplacementFailed,
    ReplacementOutcomeUnknown,
    PostReplaceVerificationFailed,
}
```

```csharp
public sealed class AtlasGoldFileApplicationException : Exception
{
    public AtlasGoldFileApplicationFailure Failure { get; }
}
```

`AtlasGoldFileApplicationException` has no public constructor. Its internal construction may accept
an inner exception, but callers cannot supply a message or path. Its public messages are fixed and
value-free:

| Failure                         | Exact message                                                               |
| ------------------------------- | --------------------------------------------------------------------------- |
| `UnsupportedPlatform`           | `Gold file application is supported only on Windows.`                       |
| `UnsupportedSlotPath`           | `The slot path is not a supported canonical save-slot path.`                |
| `BackupConflict`                | `The fixed backup artifacts conflict with this operation.`                  |
| `StagingConflict`               | `The fixed candidate stage conflicts with this operation.`                  |
| `SourceChanged`                 | `The source slot changed before replacement.`                               |
| `ReplacementFailed`             | `The source slot replacement failed without changing the classified files.` |
| `ReplacementOutcomeUnknown`     | `The source slot replacement outcome is unknown.`                           |
| `PostReplaceVerificationFailed` | `The replaced slot failed verification.`                                    |

These eight members are the complete domain-failure set. Null or invalid arguments, reader failures,
G6R3 mutation failures, ordinary pre-replacement I/O failures, and cancellation retain their
established exception types unless this plan explicitly requires one of the domain classifications
above.

Only the top-level domain message is fixed and value-free. An optional internal I/O exception may
retain its normal local diagnostic details, including a path, through `InnerException`; A6R4 does
not add path-redaction machinery for a trusted local application.

## Path and platform qualification

The method must reject before any artifact inspection or mutation unless all of these conditions
hold:

1. the current platform is Windows;
2. `slotPath` is non-null and fully qualified;
3. its leaf is an exact ordinal canonical name from `file1.rpgsave` through `file20.rpgsave`;
4. `AtlasSaveSnapshot.TryGetCanonicalName` accepts the leaf and returns exactly the supplied leaf;
5. the path names an existing ordinary file through the existing
   `AtlasDiscovery.ValidateExistingOrdinaryFile` policy; and
6. the relevant parent is ordinary under that existing policy.

The implementation must not case-fold, normalize an alias into acceptance, or accept global/config
files or slot-name variants. Syntactic, qualification, and canonical-leaf refusals are
`UnsupportedSlotPath`. Existing ordinary-path validation retains its established
`AtlasSafetyException` or I/O behavior for directories, reparse points, nonordinary parents/files,
missing paths, and inaccessible paths; A6R4 does not duplicate that path walk or invent a second
taxonomy.

The three artifact paths are formed directly beside the accepted slot:

```text
<slot>.celesphonia-original.bak
<slot>.celesphonia-original.bak.staging
<slot>.celesphonia-stage.tmp
```

No other application-owned path is permitted.

## Required reuse

The implementation must reuse, without modifying:

- `AtlasIoSeams` for the filesystem seams it already exposes, including the replacement call;
- `AtlasDiscovery.ValidateExistingOrdinaryFile` for ordinary-file validation;
- `AtlasSaveSnapshot.TryGetCanonicalName` for canonical slot names;
- existing write-through and disk-flush patterns;
- the released save reader and `AtlasSaveReaderLimits`; and
- the released G6R3 Gold mutation kernel and its semantic no-op behavior.

Private helpers may exist only inside `AtlasGoldFileApplication.cs`. A helper must not introduce a
second protocol, persistent state, or public surface.

For deterministic tests, the same file may provide one internal overload that additionally accepts
`AtlasIoSeams io` and `bool isWindows`. The public overload must call it with
`AtlasIoSeams.Default` and `OperatingSystem.IsWindows()`. No new seam type or existing seam change is
permitted.

## External API binding

The replacement design is bound to the official .NET
[`File.Replace`](https://learn.microsoft.com/dotnet/api/system.io.file.replace) contract: the source
file replaces the destination, the source file is consumed on success, different-volume
source/destination replacement fails, and a null backup argument creates no replacement-time
backup. The source-handle design is bound to the official
[`FileShare`](https://learn.microsoft.com/dotnet/api/system.io.fileshare) meanings: omitting
`FileShare.Write` denies subsequent write opens, while `FileShare.Delete` permits deletion or
replacement. These API facts do not elevate the replacement call into an infallible transaction.

## Operation protocol

### 1. Validate and capture the source

The method must:

1. perform normal argument validation and honor cancellation;
2. enforce Windows, canonical path, and ordinary-file requirements;
3. open the live slot read-only while denying write sharing and allowing readers and replacement:
   `FileAccess.Read` with `FileShare.Read | FileShare.Delete`;
4. hold that source handle until replacement outcome classification is complete;
5. read from the held handle while enforcing the encoded-byte limit in `limits`;
6. retain the exact initial source bytes in memory;
7. parse those bytes with the released reader; and
8. invoke the released G6R3 mutation kernel for `value`.

The source handle prevents a cooperative writer from opening with write access while still allowing
path replacement. The method must not weaken this share mode.

If G6R3 reports a byte-for-byte semantic no-op, return `Unchanged` immediately. The no-op branch must
not inspect, create, delete, move, replace, or reconcile any backup or candidate artifact. Tests must
observe zero artifact probes and zero mutating filesystem seam calls; the required source
qualification and read are not artifact operations.

### 2. Establish or validate the archive-first backup

The completed backup is immutable to the application.

#### No completed backup exists

The backup staging path must also be absent. If any entity already occupies it, fail with
`BackupConflict`.

Create the backup staging file with `CreateNew`, write-through behavior, and the exact initial source
bytes. Complete the write and disk flush, then close and reread the path. Before promotion, require:

- an ordinary file;
- exact byte equality with the already parsed initial source; and
- no encoded-limit bypass.

Move the staging file to the completed backup path without overwrite. Reread the completed backup
and require the same ordinary-file, exact-byte, and limit checks. Record for this invocation that
the backup was created.

The implementation never overwrites or automatically deletes a completed backup. A collision or
invalid artifact at any backup step is `BackupConflict` when it is a classified state conflict;
ordinary failures while creating, writing, flushing, reading, or moving retain their established I/O
exceptions.

#### A completed backup exists

The backup staging path must be absent; otherwise fail with `BackupConflict`. Validate and snapshot
the completed backup as an ordinary file, then parse it once with the released reader and require
Gold consistency because its bytes lack current-operation provenance. Retain its exact bytes for
later equality checks. The backup need not equal the current source because it is the preserved
baseline from an earlier edit. Record for this invocation that the backup was preserved.

If the user manually deletes a prior completed backup, the next changed operation follows the
no-backup branch and archives the then-current source. This reset is intentional and must be stated
in API documentation; A6R4 cannot recreate the older deleted baseline.

### 3. Establish or reuse the candidate stage

The candidate bytes are exactly the G6R3 output for the captured source and requested `value`.

If the fixed candidate-stage path is absent, create it with `CreateNew`, write-through behavior, and
the exact candidate bytes. Complete the write and disk flush, then reread and require an ordinary
file with exact byte equality to the already kernel-verified generated candidate.

If the fixed candidate-stage path already exists, reuse it only when it is ordinary and its exact
bytes equal the newly generated candidate. Any different bytes, nonordinary entity, or reparse point
is `StagingConflict`. An ordinary failure to probe, open, read, or flush the artifact retains its
established I/O exception type before replacement.

A partially written stage left by an ordinary I/O failure is not automatically repaired. A later
call classifies it under the same fixed-stage rules.

### 4. Pre-replacement convergence

Immediately before replacement, the method must reread the live slot by its path, not merely from
the held handle, and require exact equality with the initial source bytes. A missing, replaced,
nonordinary, unreadable-as-source, or byte-different live path at this phase is `SourceChanged` when
it demonstrates an observable change.

The method must then:

1. revalidate the completed backup as ordinary and exactly equal to the backup bytes retained for
   this invocation;
2. revalidate the candidate stage as ordinary and exactly equal to the generated candidate;
3. perform the final cancellation check; and
4. make no further cancellation observation before replacement outcome classification completes.

Pre-replacement backup drift is `BackupConflict`; stage drift is `StagingConflict`.

Cancellation is honored throughout pre-replacement work with the established cancellation behavior.
If cancellation interrupts construction of a fixed artifact, the application preserves whatever
occupies that path; a later call applies the normal backup or candidate conflict rules. Cancellation
before the final replacement boundary remains `OperationCanceledException`.

### 5. Replace

Call the existing seam for:

```csharp
File.Replace(candidateStagePath, slotPath, null)
```

Because the candidate stage is adjacent to the slot, the design uses same-volume replacement. The
null backup argument creates no replacement-time backup, and therefore cannot overwrite the fixed
completed archive backup. On a successful replacement the candidate stage is consumed by the
official API semantics.

The replacement call is the cancellation boundary. Once it begins, cancellation is ignored until
the actual outcome has been classified.

### 6. Classify a returned replacement

If the replacement call returns, require all of the following without rollback:

- the live path is an ordinary file with bytes exactly equal to the generated candidate;
- the candidate-stage path is absent;
- the completed backup is ordinary and exactly unchanged.

If every check passes, return `AppliedWithBackupCreated` or `AppliedWithBackupPreserved` according to
the backup branch. Any failed check or ordinary verification I/O failure is
`PostReplaceVerificationFailed`, optionally retaining the internal cause. Never automatically roll
back.

### 7. Classify an expected thrown replacement

If the replacement seam throws `IOException` or `UnauthorizedAccessException`, classify the actual
post-state without observing cancellation:

- **Effective success:** live is the exact generated candidate, candidate stage is absent, and
  backup is ordinary and exactly unchanged. Return the applicable applied disposition.
- **Proven failure:** live remains the exact initial source, candidate stage remains the exact
  generated candidate, and backup is ordinary and exactly unchanged. Throw `ReplacementFailed`
  with the replacement exception as the internal cause.
- **Anything else:** throw `ReplacementOutcomeUnknown`, retain the replacement exception as the
  internal cause, and preserve every artifact.

An inability to complete classification is itself unknown. The method must not delete, move,
rewrite, or roll back any file while classifying.

`ReplacementFailed` intentionally leaves the fixed candidate stage in place so the same source and
same requested value can retry it. A later request generating different candidate bytes receives
`StagingConflict`. A6R4 has no cleanup API.

No catch-all replacement protocol is added. Exception kinds outside the documented valid-argument
`File.Replace` I/O and access failures retain their runtime type; they do not justify a new domain
member or recovery mechanism.

## State invariants

The implementation and tests must preserve these invariants:

1. `Unchanged` leaves all three artifact paths unobserved and untouched.
2. A changed operation never invokes replacement before a completed verified backup exists.
3. The completed backup is never passed to `File.Replace`, overwritten, or automatically deleted.
4. Backup staging and completed backup may not coexist at operation entry or pre-replacement.
5. Replacement uses only the fixed candidate stage and accepted live slot.
6. Success means exact bytes for the already kernel-verified candidate, not merely a returned system
   call.
7. Proven replacement failure preserves exact source, exact candidate stage, and exact backup.
8. Ambiguous post-state is never reported as success or proven failure.
9. No automatic rollback or reconciliation follows a returned or thrown replacement.
10. Every test uses generated synthetic saves in synthetic temporary directories; no private path,
    value, payload, or original save is accessed.

## Failure classification matrix

| Phase                             | Condition                                             | Result                          |
| --------------------------------- | ----------------------------------------------------- | ------------------------------- |
| Qualification                     | Non-Windows                                           | `UnsupportedPlatform`           |
| Qualification                     | Noncanonical or non-fully-qualified slot path         | `UnsupportedSlotPath`           |
| Source read/model                 | Invalid argument, limit, parse, or mutation condition | Existing exception type         |
| No-op                             | G6R3 produces no byte change                          | `Unchanged`                     |
| Backup                            | Fixed backup state is conflicting or invalid          | `BackupConflict`                |
| Candidate                         | Fixed candidate state is conflicting or invalid       | `StagingConflict`               |
| Pre-replace                       | Live path no longer proves exact initial source       | `SourceChanged`                 |
| Pre-replace                       | Ordinary I/O or cancellation outside a named state    | Existing exception type         |
| Replace throws expected exception | Exact unchanged source/stage/backup state             | `ReplacementFailed`             |
| Replace throws expected exception | Exact successful candidate/absent-stage/backup state  | Applied disposition             |
| Replace throws expected exception | Any other or unreadable state                         | `ReplacementOutcomeUnknown`     |
| Replace returns                   | Any required postcondition fails                      | `PostReplaceVerificationFailed` |

## Synthetic test plan

`AtlasGoldFileApplicationTests.cs` must provide comprehensive but proportional coverage using only
synthetic save bytes and test-owned temporary directories. Test seams may induce precise races and
replacement outcomes, but production seams are unchanged.

### Public surface and failures

- Assert the exact static method signature, default cancellation parameter, `ValueTask` result,
  disposition members and order, failure members and order, sealed exception, `Failure` property,
  absence of public exception constructors, and exact fixed messages.
- Assert that top-level domain messages contain no path, Gold value, or private payload. Inner I/O
  diagnostics retain normal runtime behavior.
- Assert established exception types for null/invalid arguments, reader limits, malformed content,
  G6R3 mutation refusal, ordinary pre-replacement I/O, and cancellation.

### Platform, names, and ordinary files

- Accept every exact leaf from `file1.rpgsave` through `file20.rpgsave`.
- Refuse relative paths, global/config names, zero-padded names, out-of-range slots, extensions or
  trailing variants, case variants, and aliases with `UnsupportedSlotPath`; verify directories,
  reparse points, and nonordinary parents/files retain existing validator behavior.
- Exercise `UnsupportedPlatform` through the internal `isWindows` overload and retain a real
  platform-conditional assertion.

### No-op and ordinary edits

- Prove a same-value call returns `Unchanged` after source read/modeling with zero artifact probes,
  creations, writes, flushes, moves, replacements, deletions, or reconciliation calls.
- Prove a first changed edit creates and verifies the archive backup before replacement, consumes
  the candidate stage, writes the exact requested value, and returns
  `AppliedWithBackupCreated`.
- Prove a repeated changed edit preserves the exact original completed backup and returns
  `AppliedWithBackupPreserved`.
- Delete the completed backup between successful edits and prove the next changed edit creates a new
  baseline from the then-current source, documenting that the older baseline is not recoverable.

### Fixed artifacts and durability

- Cover every backup-staging/completed-backup collision and invalid ordinary, pre-existing-backup
  parse/Gold, byte, and drift state as `BackupConflict`.
- Cover candidate-stage `CreateNew`, exact reuse after a proven failed replacement, byte mismatch,
  nonordinary entity, reparse point, and drift as either safe reuse or `StagingConflict`.
- Instrument exact write-through, write, disk-flush, close, reread, byte verification, and move
  ordering for backup and candidate creation.
- Prove no completed backup overwrite or automatic deletion call exists.

### Source stability and cancellation

- While the initial source handle is held, prove a cooperative writer is denied write sharing and a
  replacement-capable handle remains possible because delete sharing is allowed.
- Replace, remove, or change the live path between initial capture and pre-replacement reread and
  assert `SourceChanged` before the application's replacement seam is called.
- Cancel before source work, before artifact transitions, and at the final pre-replacement check;
  assert `OperationCanceledException` and safe artifact preservation.
- Signal cancellation from inside the replacement seam, both before and after an induced effective
  mutation, and prove classification completes without `OperationCanceledException`.

### Replacement classification

- Throw `IOException` and `UnauthorizedAccessException` before effective mutation; prove
  `ReplacementFailed`, exact source preservation, exact backup preservation, and retained exact
  candidate stage.
- Perform effective mutation and then throw each expected exception; prove the method recognizes
  exact success and returns the correct applied disposition.
- Induce partial and ambiguous states, including candidate live with stage present, source live with
  stage absent, third-content live, missing or nonordinary live, changed backup, invalid backup, and
  unreadable classification; assert `ReplacementOutcomeUnknown` and no cleanup.
- After a returned replacement, separately violate live bytes, stage absence, backup equality, and
  backup ordinary-file validity; assert `PostReplaceVerificationFailed` and no rollback.

### Real Windows integration and boundaries

- On Windows, exercise a real same-directory `File.Replace` in a synthetic temporary directory and
  prove exact live candidate bytes, absent candidate stage, exact archived source bytes, and no
  replacement-time backup.
- Prove source and backup preservation on the classified failure paths.
- Update `ProjectBoundaryTests.cs` for the new source/test inventory and assert the production public
  API remains library-only with no CLI/schema/request surface. The Git gate, not a runtime test,
  proves that the cumulative implementation diff changes only the three authorized paths.
- Assert the production application references only the three fixed adjacent artifacts and exposes
  no cleanup, multi-slot, global, journal, ledger, manifest, hash-name, GUID-name, attestation, Git,
  or private-execution surface.

## Documentation requirements

The implementation XML documentation must state:

- Windows-only and game-closed trusted-local prerequisites;
- exact canonical slot restriction;
- archive-first backup behavior and the three fixed artifact names;
- the backup-deletion reset limitation;
- intentional stage retention after proven replacement failure;
- cancellation behavior before and after the replacement boundary;
- absence of rollback and automatic cleanup; and
- that actual use requires a later persisted operation increment with explicit exact canonical
  slot/value confirmation.

No example may contain a private path, private value, or instruction to run A6R4 on a real save.

## Rejected alternatives

- **Candidate export:** Rejected because it delegates the riskiest manual replacement step to the
  user rather than bounding it in the reviewed library.
- **Hash or GUID artifact names and automatic stale-stage reconciliation:** Rejected because they
  add inventory and cleanup complexity without a current need.
- **Reduced journal:** Rejected because even a small journal creates torn-record handling, recovery,
  compatibility, and versioning lifecycle.
- **Historical rich slot/global transaction:** Rejected as disproportionate to one trusted-local
  single-slot Gold update.

## Gate sequence

All candidate identifiers are immutable full commit IDs. “Verified shared” means the exact commit is
available at the agreed shared repository location and independently reproducible; a moving branch,
working tree, path presence, or conversation claim is insufficient.

### P6R4 — governing-plan candidate

`P6R4` may include reviewed corrections descending from the initial plan candidate, but its
cumulative diff from G6R3 must contain exactly:

1. `src\private\app\celesphonia-modifier\docs\.copilot\README.md`;
2. `src\private\app\celesphonia-modifier\docs\.copilot\plans\atlas-v0-a6-gold-file-application.md`.

Required evidence:

- ancestry from `c7568573f3a8c6ecf89409ab40e5480dfaf01b18`;
- exact two-path cumulative inventory;
- Markdown formatting, Markdown lint, link/index review, privacy review, and `git diff --check`;
- holistic review of the full exact candidate against governing instructions, released G6R3
  boundaries, and this accepted risk model; and
- no implementation, private-access, or release claim merely from local candidate presence.

### R6R4 — plan-review activation

After an independent reviewer returns `No findings` for exact `P6R4`, create only:

`src\private\app\celesphonia-modifier\docs\.copilot\reviews\atlas-v0-a6-gold-file-application-plan-review.md`

The record must bind the full `P6R4` commit, base G6R3 commit, exact two-path inventory, review
instructions, evidence, findings and dispositions, validation results, and the decision that only
the bounded synthetic implementation is authorized. `R6R4` must descend from `P6R4`, and its diff
from `P6R4` must be exactly that one review record.

Only verified shared `R6R4` activates implementation. It grants no private read, private write, CLI,
operation, or execution authority.

### C6R4 — implementation candidate

The cumulative diff from exact `R6R4` to `C6R4` must contain exactly the three implementation paths
listed under **In-scope implementation**. Reviewed corrections may accumulate only in those paths.

Required evidence on Windows:

- targeted Atlas build and test project execution;
- the complete synthetic test matrix in this plan;
- real synthetic-directory `File.Replace` integration;
- repository Markdown, formatting, lint, and boundary checks applicable to changed files;
- `git diff --check`;
- exact three-path cumulative inventory and no CLI/project/schema/request/reader/kernel/seam changes;
- no access to private save data or Git-ignored content; and
- fresh independent review of the full exact `C6R4` against exact `R6R4`, with every finding
  adjudicated and resolved until `No findings`.

### G6R4 — release gate

After exact `C6R4` passes review and validation, create only:

`src\private\app\celesphonia-modifier\docs\.copilot\reviews\atlas-v0-a6-gold-file-application-release-gate.md`

The release record must bind exact `R6R4` and `C6R4`, the three-path inventory, Windows validation,
review result, residual risks, exclusions, and the library-only release decision. `G6R4` must
descend from `C6R4`, and its diff from `C6R4` must be exactly that one release-gate record.

Verified shared `G6R4` releases only the library API for later integration. Stop after `G6R4`. Do not
run it on private data, add an operation surface, or infer private execution authority.

## Acceptance criteria

A6R4 is complete only when:

1. exact `R6R4` activated the bounded three-path implementation;
2. exact `C6R4` implements the public contract and protocol without modifying released dependencies;
3. every proportional synthetic and real Windows synthetic-directory test passes;
4. independent review of the complete candidate returns `No findings`;
5. exact `G6R4` adds only the release-gate record and is verified shared;
6. the fixed backup protects the first changed baseline unless the user explicitly deletes it;
7. every returned disposition and named failure matches the actual classified state;
8. no rollback, cleanup service, journal, ledger, runtime Git check, attestation, transaction,
   private access, or CLI/schema/request surface is introduced; and
9. execution stops without a private run.

## Stop and resume

Stop and return to planning if implementation would require a new persistent protocol, recovery
path, multi-file guarantee, seam change, reader/kernel change, public failure, artifact name, threat
assumption, or authority not stated here.

On interruption, resume from the latest verified immutable gate:

- before `R6R4`, review only the exact two-path plan candidate;
- after `R6R4`, implement only the exact three authorized paths;
- after `C6R4`, perform only review, validation, corrections within those paths, and release gating;
- after `G6R4`, stop with no private execution.
