# Atlas V0 A2 Clean Workspace Rebuild

**Lifecycle:** Active subordinate; plan-only before verified shared `R13`

**Status:** Bootstrap and private execution blocked

**Increment:** A2R13 - Clean Workspace Rebuild

**Decision owner:** Project leader

**Decision:** Preserve the existing A2 workspace unchanged and create a fresh, isolated A2 workspace
and inventory lineage

**Purpose:** Produce one qualified research snapshot from the currently approved A0 manifest and
current installed tree without repairing, importing, or continuing the historical inventory.

**Implementation language:** Session-only C# plus the unchanged released Atlas CLI

**Base:** `661d6f62c56efcf0bb7a1d8fb220b44dad71ef56`

**Governing sources:**

- `project-operating-model.md`;
- `atlas-v0-a2-intake-safety-plan.md`; and
- `atlas-v0-a2-approved-manifest-authority-correction.md`.

**Historical provenance:**

- `atlas-v0-a2-current-baseline-observation.md`;
- `atlas-v0-a2-baseline-manifest-row-diagnosis.md`;
- `atlas-v0-a2-baseline-manifest-row-remediation.md`; and
- their review and completion records.

Historical A2R8 through A2R12 evidence records prior bounded work but neither selects this route nor
enters the new inventory. It grants no A2R13 execution authority. The clean rebuild is the
project-leader's outcome-independent decision and remains compatible with every protected A2R12
result.

**Dependencies:** Verified shared A2R12 `G12`, unchanged released A2R8 Atlas source, the current
strictly readable approved baseline manifest and discovery request, the approved
`trusted-local-filesystem/v1` profile, an absent fresh private project root, independent plan and
source review, and project-leader approval of exact pending manifest bytes before confirmation.

**Planned plan-review record:**
`../reviews/atlas-v0-a2-clean-workspace-rebuild-plan-review.md`

**Planned manifest-decision record:**
`../reviews/atlas-v0-a2-clean-workspace-manifest-decision.md`

**Planned completion record:**
`../reviews/atlas-v0-a2-clean-workspace-rebuild-completion.md`

## 1. Correction and claim

A2R12 closed without publishing its protected result or selected branch. A2R13 does not inspect,
publish, or infer that result. It implements the project leader's independent decision to stop using
the historical inventory as an operational predecessor while preserving every existing byte and
path. It adds no diagnostic or remediation.

A2R13 starts a new private project root beneath the existing Git-ignored project-private parent. The
released canonical workspace suffix remains:

```text
src/private/app/celesphonia-modifier/.private/atlas-v0/survey-000001
```

The source baseline manifest is copied byte-for-byte into that new workspace. A new inventory begins
with exactly one baseline-manifest custody row. A new discovery request retains the current request's
live source locators and fixed game labels while rebasing only its project, workspace, control-output,
and fresh-inventory bindings.

The target claim is narrow:

1. the historical workspace remains unchanged;
2. the new workspace begins from the exact current approved baseline-manifest bytes and a
   deterministic one-row inventory;
3. released discovery, confirmation, copy, and non-deleting lifecycle preflight complete under their
   existing contracts;
4. the final snapshot is qualified only by valid state revision 3 and is revalidated before later
   use; and
5. no live game, save, definition, or executable path is modified.

A2R13 makes no historical identity, simultaneous-corpus-snapshot, immutable-copy, semantic-correctness,
or live-save-write claim.

## 2. Scope and exclusions

In scope:

- one session-only utility in protected session state;
- strict read-only loading of the current canonical discovery request and its approved baseline
  manifest;
- one fresh private project root and canonical A2 workspace;
- one exact baseline-manifest byte copy;
- one deterministic fresh baseline inventory and discovery request;
- the unchanged released `intake-discover`, `intake-confirm`, `intake-copy`, and
  `cleanup-preflight` operations;
- one explicit project-leader review and decision over exact pending manifest bytes;
- private request, decision, state, inventory, receipt, snapshot, and lifecycle evidence;
- synthetic tests and independent source review; and
- repository-safe plan, decision, review, and completion records.

Out of scope:

- modifying, moving, deleting, repairing, copying wholesale, or importing the historical workspace or
  its inventory;
- reading the historical inventory, backups, states, copies, decoded data, evidence, validation, or
  agent envelopes;
- another A2R9-A2R12 diagnostic or row-level remediation;
- changing the approved corpus denominator, selection rules, aliases, terminal decisions, game
  labels, or live source locators;
- production, CLI, schema, package, tracked-project, or test-project changes;
- decoding or semantically scanning source or copied content;
- cleanup deletion, retention-policy execution, or final disposal;
- hostile-local defense, cross-volume atomicity, or adversarial-race proof;
- any original-data write or future editor write authority; and
- A3 parsing or scanning.

## 3. Authority and source binding

Before bootstrap, discovery, or protected decision recording, `HEAD`, upstream, and the clean
worktree must equal verified shared `R13`. Before any approved post-decision phase, they must equal
the verified shared approved `D13`. Released Atlas library and CLI source must equal A2R8 `G`
`4dc1572cc4439e6e5fade2827c3fa40230565ef2` in every phase.

Every session-utility mode receives:

1. the repository root; and
2. the bound 32-character lowercase hexadecimal run identifier.

Bootstrap requires that identifier's target to be fresh and absent. Every later mode uses the same
identifier. Decision recording additionally receives exactly `approved` or `declined`, selected
through the interactive project-leader decision. Approved request-preparation modes additionally
receive the exact verified 40-character `D13` commit. Cleanup-preflight request preparation has no
free milestone input; it always uses fixed milestone `A8`.

It derives:

- the historical canonical workspace and discovery-request path from the repository root and
  `survey-000001`;
- the current baseline-manifest path from the strictly loaded source request; and
- one absent target project root directly beneath the project-private parent, named from a fixed
  A2R13 prefix and the run identifier.

The utility validates the repository root, project-private parent, historical workspace, request, and
baseline manifest as ordinary non-reparse paths on fixed local drives. It strictly loads the current
request and manifest through released readers and requires:

- exact canonical historical project, workspace, request, manifest, inventory, backup, state,
  manifest-revision, root-map, and copy-plan bindings;
- manifest SHA-256 equality with the request;
- manifest revision 3;
- approved confirmation status;
- exact `survey-000001`, Steam app ID `1786790`, and build ID `13624401`; and
- the source request's two save roots, definition root, and executable path to pass the released
  discovery live-source preflight.

No historical inventory is opened. Failure of any source condition writes nothing and grants no
fallback authority.

## 4. Fresh bootstrap

The target project root and every descendant must initially be absent. The utility creates only that
new root and the exact canonical workspace directories required by released command census. It does
not copy optional historical A0 workspace entries.

The baseline manifest is written byte-for-byte with create-new semantics. The fresh inventory is
serialized by `AtlasIntakeContracts.SerializeInventory` and contains exactly:

```text
schemaVersion       atlas-private-inventory/v1
surveyAlias         survey-000001
artifactAlias       private-artifact-000001
artifactClass       live-discovery
purpose             intake-manifest:r000003
custodianRole       project-leader
lineageAliases      empty
lastUseMilestone    A2
expiryCondition     after:A2
plannedDisposition  retain-private
status              present
qualification       null
verificationMethod  atlas-intake/v2;r000003
```

The exact string values are the released constants; the table is descriptive and does not duplicate
runtime authority.

The fresh discovery request is produced by copying the strictly loaded current request and replacing
only:

- `projectRoot`;
- `workspaceRoot`;
- `baselineManifestPath`;
- `manifestRevisionDirectory`;
- `sourceRootMapOutputPath`;
- `inventoryPath`;
- `expectedInventorySha256`;
- `inventoryBackupPath`;
- `copyPlanOutputPath`; and
- `stateRevisionDirectory`.

All source locator objects, expected baseline digest and revision, next revision, survey alias, Steam
app ID, and build ID remain exactly equal. The request is serialized through the released contract.

The utility writes the manifest, inventory, and discovery request as create-new ordinary files,
flushes them, reloads them through released strict readers, verifies exact manifest bytes, verifies
the one-row inventory through the released manifest-row helper, and verifies object-level request
equality outside the listed rebased fields.

Bootstrap emits only `a2r13-bootstrap-recorded\n` or
`a2r13-bootstrap-not-recorded\n`; standard error is empty. A failed partial target remains protected
and unusable. It is never cleaned or reused; continuation requires a fresh run identifier and absent
target.

## 5. Discovery and manifest decision

After bootstrap and exact source review, the released CLI runs `intake-discover` once against the new
request. Its normal restart-safe behavior may resume only that same new workspace. It may read live
metadata but does not read source content or modify a live source.

Discovery must produce and strictly validate:

- pending manifest revision 4;
- source-root map;
- copy plan;
- state revision 1;
- the discovered inventory and exact prior-inventory backup; and
- complete reconciliation with the approved A0 corpus and current directory entries.

No confirmation or copy follows automatically. The project leader must review the exact pending
manifest bytes locally. A session-only decision mode then writes one protected create-new decision
record binding the exact pending-manifest SHA-256 to `approved` or `declined`.

A repository-safe decision record may state only that one protected candidate was approved or
declined. It contains no path, hash, count, filename, corpus entry, difference, or private value. A
fresh independent privacy review must return `No findings` before that record is committed.

If approved, the decision-record commit is the `decisionCommit` used by both confirmation and copy.
If declined, A2R13 performs no confirmation, copy, or preflight and proceeds only to result-safe
completion.

## 6. Confirmation, copy, and lifecycle preflight

Only an approved protected decision whose manifest hash still matches may authorize the session
utility to create the canonical confirmation request. The utility derives every path from the new
workspace, binds current strict-reader hashes, and uses the exact approved decision commit. The
released CLI then runs `intake-confirm`.

After valid state revision 2, the utility creates the canonical copy request from current strict
outputs and the same decision commit. The released CLI runs `intake-copy`. Copy is the first phase
that reads source content. It reads each live source through the released held-handle procedure and
writes only create-new destinations beneath the new incomplete snapshot before released atomic
promotion.

Successful copy requires:

- exact source-to-copy length and SHA-256 equality for every included entry;
- one provisional verified inventory row per copied source;
- a complete receipt agreeing with the approved manifest and inventory;
- no incomplete copy path;
- valid state revision 3 as the sole snapshot qualification signal; and
- no source modification, rename, or deletion.

The utility then creates the canonical cleanup-preflight request with fixed proposed milestone `A8`.
Released `cleanup-preflight` publishes state revision 4 and its report but deletes nothing. The
report must contain one correct released lifecycle result for every valid state-3 inventory row and
zero invalid rows; rows are not required to be eligible for human deletion review.

Any released-command refusal stops the sequence. No custom correction, retry with changed inputs,
private-state edit, or cleanup follows without another persisted and reviewed plan.

## 7. Session utility and synthetic evidence

The session-only C# project lives beneath protected Copilot session state, references the released
Atlas project, and uses the existing test friend-assembly name only to call released internal
serializers, readers, layout helpers, and validators. It supports:

- `--self-test`;
- `--bootstrap`;
- `--record-manifest-decision`;
- `--prepare-confirm`;
- `--prepare-copy`; and
- `--prepare-cleanup-preflight`.

Every mode has a fixed result-neutral process contract and writes only its declared create-new
private outputs. Synthetic tests use only owned temporary directories and cover:

- strict source request and manifest binding;
- absent target and containment checks;
- exact manifest byte copy;
- exact fresh inventory shape and released-helper acceptance;
- request rebase allowlist and all-field equality outside it;
- missing, malformed, substituted, existing, file-as-directory, directory-as-file, outside,
  non-fixed, and reparse-backed path refusal;
- partial-bootstrap refusal and fresh-run isolation;
- every protected decision shape and manifest-hash binding;
- approval-before-confirmation enforcement;
- confirmation, copy, and preflight request derivation;
- matching decision commit through confirmation and copy;
- create-new behavior; and
- fixed stdout, empty stderr, and exit codes.

Before private bootstrap, the exact source must format cleanly, build warning-free in Release, pass
all synthetic tests, and receive full independent source review with `No findings`. Reviewers receive
only source, plans, and repository-safe released Atlas code. They receive no private file, path, hash,
manifest, request, inventory, decision, output, count, or result.

The reviewed project file, source files, session assembly, and linked Atlas assembly receive exact
SHA-256 bindings retained in protected session state. Their nonprivate SHA-256 values are also
persisted in the result-safe completion record. Every resumed private phase must verify those exact
source and assembly bindings before execution.

## 8. Git candidates and release topology

The `P13` plan line begins as the direct child of A2R12 `G12`. Until `R13`, plan-only corrections may
change only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a2-clean-workspace-rebuild.md
    atlas-v0-a2-intake-safety-plan.md
```

Plan-review `R13` is the direct child of the final reviewed `P13` plan-line tip and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-clean-workspace-rebuild-plan-review.md
```

If discovery produces a pending manifest, decision candidate `D13` is the direct child of `R13` and
adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-clean-workspace-manifest-decision.md
```

Completion `G13` adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-clean-workspace-rebuild-completion.md
```

`G13` is the direct child of `D13` when a pending manifest reached decision; otherwise it is the
direct child of `R13`. Every record must be reviewed as an exact staged blob and committed unchanged.

## 9. Acceptance criteria

A2R13 accepts one of three terminal branches:

- **safe refusal:** bootstrap or discovery refuses under the reviewed procedure; no decision,
  confirmation, copy, or preflight follows;
- **declined:** discovery produces a strict state-1 chain, the project leader declines the exact
  pending manifest, protected and reviewed decision evidence is recorded, and no confirmation, copy,
  or preflight follows; or
- **approved completion:** the project leader approves the exact pending manifest and the released
  sequence produces valid states 2, 3, and 4 plus one qualified snapshot.

A2R13 completes only when:

1. exact `P13` and record-only `R13` are independently reviewed, committed, pushed, and verified;
2. the repository equals clean shared `R13` before bootstrap, discovery, or decision recording and
   equals clean shared approved `D13` before every approved post-decision phase;
3. released Atlas source equals A2R8 `G`;
4. the session utility satisfies section 7, receives independent `No findings`, and every private
   phase verifies its persisted exact source and assembly bindings;
5. the historical workspace has no write, move, rename, or deletion;
6. bootstrap creates one fresh isolated workspace with exact source manifest bytes, the exact one-row
   baseline inventory, and an allowlist-rebased request;
7. released discovery either stops safely with no downstream phase or produces a strictly valid
   state-1 chain;
8. confirmation and copy never run without an exact protected approval and reviewed `D13`;
9. a declined branch records its decision and performs no confirmation, copy, or preflight;
10. an approved branch produces valid states 2, 3, and 4 through unchanged released commands;
11. on the approved branch, every included source has exact copy fidelity and provenance, and no live
    source changes;
12. on the approved branch, the final snapshot is qualified solely by valid state 3 and immediately
    rehashes successfully before handoff;
13. on the approved branch, lifecycle preflight uses milestone `A8`, reports one correct released
    result for every valid state-3 inventory row and zero invalid rows, and performs no deletion;
14. no private data enters Git, subagent input, or retained process output;
15. every review finding is adjudicated `TP` or `FP`, every TP is resolved, and any material FP
    receives independent concurrence;
16. the exact result-safe completion record identifies the accepted branch only at the
    repository-safe level and receives independent `No findings`; and
17. verified `G13` is pushed as the clean shared branch tip with the required parent and path set.

## 10. Stop conditions and handoff

Stop without fallback when:

- the historical request or manifest is missing, malformed, noncanonical, unapproved, substituted, or
  inconsistent;
- the current live locator set or fixed game labels differ from the source request;
- the target project root or any descendant already exists;
- bootstrap would require importing any historical inventory or optional workspace artifact;
- released discovery changes the approved denominator, selection rule, alias, or terminal decision;
- exact pending manifest bytes are not approved;
- any phase requires a production or tracked-code change;
- a live source is unsupported, unreadable, changed while held, or unexpectedly reparse-backed;
- a private value reaches Git, Agent input, or process output;
- the new snapshot or lifecycle evidence does not satisfy section 9; or
- any independent finding remains unresolved.

To resume:

1. verify `G12`, then the exact current `P13`/`R13` chain, upstream, and clean worktree;
2. verify released Atlas source equality with A2R8 `G`;
3. verify the exact persisted session source and assembly hashes before using the reviewed utility;
4. continue only the current declared phase for the bound run identifier;
5. stop at the manifest-decision gate until the project leader decides exact bytes;
6. require clean shared `D13` before any approved post-decision phase;
7. never infer approval from this plan, conversation history, or artifact presence; and
8. release only the independently reviewed result-safe `G13` record.
