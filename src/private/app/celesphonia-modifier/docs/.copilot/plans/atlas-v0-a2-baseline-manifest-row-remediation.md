# Atlas V0 A2 Baseline Manifest Row Remediation

**Lifecycle:** Active subordinate; planning-only before verified shared `R12`

**Status:** Read-only qualification source change blocked

**Increment:** A2R12 - Baseline Manifest Row Remediation

**Decision owner:** Project leader

**Purpose:** Qualify one deterministic purpose-only inventory correction and, only after explicit
protected approval, preserve the original bytes and apply that correction atomically.

**Implementation language:** Session-only C#

**Base:** `5955f8c59dd69de1f11a383f373eea59294b2a29`

**Governing sources:**

- `project-operating-model.md`; and
- `atlas-v0-a2-intake-safety-plan.md`.

**Historical provenance:**

- `atlas-v0-a2-current-baseline-observation.md`;
- `atlas-v0-a2-baseline-manifest-row-diagnosis.md`; and
- their completion records.

Completed A2R10 and A2R11 evidence may select this plan's private branch but grants no current
execution or write authority.

**Dependencies:** Verified shared A2R11 `G11`, its protected diagnosis, the protected A2R10
observation, unchanged released Atlas source, preserved current inventory, independent plan and
source review, and explicit project-leader approval before any remediation.

**Planned plan-review record:**
`../reviews/atlas-v0-a2-baseline-manifest-row-remediation-plan-review.md`

**Protected approval record:** One closed private record beneath the validated session project
directory; never committed

**Planned completion record:**
`../reviews/atlas-v0-a2-baseline-manifest-row-remediation-completion.md`

## 1. Safe conclusion and claim

A2R11 recorded a closed diagnosis for the same current inventory used by the released manifest-row
check. The repository-safe conclusion is only that current cardinality differs from the released
single-row expectation. No zero/multiple distinction, row content, alias, path, hash, or predicate
name is published.

A2R12 does not assume the inventory is repairable. It first recognizes one narrow deterministic
case: no released-purpose row exists, but exactly one row already has every other released baseline
property. Only that case can become eligible for changing `purpose` and nothing else. All other
states are ineligible and remain unchanged.

## 2. Scope and authority

Before explicit persisted approval, A2R12 may:

- reuse the exact existing session observer;
- read only the protected A2R10 and A2R11 reports and the selected current inventory;
- bind the inventory to the A2R10 current-inventory fingerprint;
- prove that current and released prior inventory are the same role;
- evaluate the closed eligibility rule;
- emit only `remediation-eligible\n` or `remediation-not-eligible\n`; and
- run synthetic tests and independent source review.

Qualification writes no file and changes no state.

Only after an eligible signal, explicit project-leader approval, and one protected approval record
may A2R12:

- serialize one replacement inventory whose sole logical change is the candidate row's `purpose`;
- preserve the exact original inventory through one protected create-new copy;
- use the released restart-safe inventory replacement helper;
- validate the replacement and protected original;
- remove the helper's exact transient workspace backup;
- write one protected create-new remediation report; and
- emit only `remediation-recorded\n` or `remediation-not-recorded\n`.

Out of scope:

- guessing among multiple released-purpose rows;
- changing an alias, class, lineage, lifecycle field, status, qualification, verification method,
  array order, or any noncandidate row;
- deleting, merging, or inventing an inventory entry;
- production, test, schema, package, tracked-project, or CLI changes;
- request, manifest, wrapper, game, save, definition, copy, or generated-output reads;
- discovery, confirmation, copy, cleanup, or retry authority; and
- hostile-local defense, historical identity, or simultaneous snapshot claims.

## 3. Source binding and deterministic eligibility

All modes derive the protected session project directory from the exact
`a2r10-current-baseline-observer/bin/Release/<target-framework>/` location and validate it as an
ordinary non-reparse directory. They strictly select and parse exactly one A2R10 observation report
and one A2R11 diagnosis report by their closed schemas and fixed filename patterns.

The A2R10 report must contain exactly one `current-inventory` fingerprint and no
`inventory-backup` fingerprint. It must have outcome `refused` and terminal stage `manifest-row`,
proving inventory transition completed and the current inventory was the released prior inventory
whose row check refused. The A2R11 report must bind the same current source and have outcome
`diagnosed`. The program locates the current inventory through the exact A2R11 workspace rule and
derives the workspace root, transient backup, and replacement staging paths from it.

Read-only qualification requires the exact `intake/inventory-backups` directory to exist as an
ordinary non-reparse directory and requires the transient backup and staging paths to be absent. It
loads the
current inventory through `AtlasIntakeContracts.ReadInventoryAsync` and requires its returned byte
length and SHA-256 to match A2R10.

Eligibility requires all of:

1. A2R11 cardinality is `zero`;
2. fresh evaluation also finds zero rows with purpose `ManifestRevision3Purpose`;
3. exactly one row matches all other released baseline predicates:
    - artifact class `LiveDiscoveryArtifactClass`;
    - custodian `ProjectLeaderRole`;
    - empty lineage;
    - last use `A2`;
    - expiry `after:A2`;
    - disposition `RetainPrivateDisposition`;
    - status `PresentArtifactStatus`;
    - null qualification; and
    - verification method `<IntakeManifestSchemaVersion>;r000003`;
4. replacing only that row's purpose with `ManifestRevision3Purpose` preserves every other row and
   field in memory;
5. `SerializeInventory` accepts the replacement; and
6. the released manifest-row helper refuses the prior inventory and accepts the replacement.

Any failed source, cardinality, uniqueness, serialization, or reconciliation requirement produces
only `remediation-not-eligible\n`, empty standard error, and a nonzero exit code. Eligibility
produces only `remediation-eligible\n`, empty standard error, and exit code zero. Qualification is
repeatable.

## 4. Approval and remediation

An eligible signal grants no write authority. The project leader must explicitly approve the exact
purpose-only correction. Only after that decision, the reviewed program may run an approval-record
mode with the remediation run identifier. It re-runs read-only qualification and writes exactly one
create-new protected record:

```json
{
    "schemaVersion": "atlas-a2-baseline-manifest-row-remediation-approval/v1",
    "decision": "approved",
    "scope": "baseline-manifest-purpose-only"
}
```

The approval filename is
`a2r12-baseline-manifest-row-remediation-approval-<32-lowercase-hex>.json` directly beneath the
validated session project directory. Standard output is exactly `approval-recorded\n` or
`approval-not-recorded\n`; standard error is empty. The record and signal remain protected and
cannot enter Git, subagent prompts, completion content, or future summaries.

Remediation re-runs every source and eligibility check. It creates replacement bytes only through
`AtlasIntakeContracts.SerializeInventory`. It uses:

```text
intake/inventory-backups/private-artifact-inventory.a2r12-remediation.json
```

as the transient workspace backup and `a2r12-remediation` as the released replacement phase. Before
inventory replacement, it publishes the exact A2R10-bound prior inventory bytes as:

```text
a2r12-baseline-manifest-row-remediation-original-<32-lowercase-hex>.json
```

directly beneath the validated session project directory. This protected original uses the same run
identifier as the approval and remediation reports. Before any write, the program:

1. requires the inventory path to remain the exact selected current inventory;
2. validates the workspace root and inventory as existing ordinary non-reparse paths;
3. requires the exact `intake/inventory-backups` directory to exist and validates it through
   `AtlasDiscovery.ValidateExistingOrdinaryDirectory`, workspace containment, and fixed-drive
   checks;
4. validates the exact backup and staging leaves through
   `AtlasDiscovery.ValidateCreateNewOutputFile` with the workspace root and restart-aware existing
   output allowance; and
5. requires the protected approval record for the same run identifier.

The fresh branch requires current bytes to match A2R10, the transient backup to be absent, and any
existing staging bytes to equal the derived replacement. It evaluates eligibility against current
bytes, atomically publishes the protected original, and calls
`AtlasDiscovery.EnsureInventoryReplaceAsync` with exact prior and replacement bytes.

Every restart branch loads the protected original through `ReadInventoryAsync`, binds its byte
length and SHA-256 to A2R10, re-evaluates eligibility against it, and derives the expected
replacement. It then recognizes only:

- current equals prior, transient backup absent, and staging absent or equal to replacement: resume
  the released helper;
- current equals replacement, transient backup equals prior, and staging absent: validate helper
  completion; or
- current equals replacement, transient backup and staging absent: continue after transient-backup
  cleanup.

Any other current, backup, or staging combination refuses without another state change.

If current equals the A2R10-bound protected original, the transient backup is absent, and the exact
ordinary replacement staging leaf exists but is not the complete expected replacement, the program
may delete only that staging leaf and let the released helper recreate it. This is the sole
replacement-staging recovery rule.

After replacement, A2R12 requires:

- current bytes equal the serialized replacement;
- protected original bytes equal the exact prior inventory;
- any transient backup bytes equal the protected original before targeted removal;
- current inventory has exactly one released-purpose row accepted by the released helper;
- protected original still satisfies the pre-repair eligibility rule;
- an object-level comparison differs only in the selected row's `purpose`; and
- no replacement staging or transient backup path remains.

For restart after replacement but before report publication, the program revalidates all
postconditions from the protected original and finishes the report without invoking replacement
again.

The only authorized operational deletions are:

- the exact ordinary incomplete replacement staging leaf under the proven-prior and absent-backup
  recovery rule; and
- the exact ordinary transient backup leaf after current replacement and protected-original
  equality are proved.

No retained operational artifact is created, so A2 inventory custody remains unchanged.

## 5. Private report and process contract

The remediation report filename is
`a2r12-baseline-manifest-row-remediation-<32-lowercase-hex>.json` directly under the validated
session project directory. It uses create-new semantics and the closed schema
`atlas-a2-baseline-manifest-row-remediation/v1`:

```json
{
    "schemaVersion": "atlas-a2-baseline-manifest-row-remediation/v1",
    "outcome": "remediated",
    "changedField": "purpose",
    "beforeSha256": "<64-lowercase-hex>",
    "afterSha256": "<64-lowercase-hex>",
    "originalSha256": "<64-lowercase-hex>"
}
```

The three hashes remain private. The report contains no path, alias, prior or replacement purpose,
row content, count, dynamic exception detail, or future route. `remediation-recorded` means only
that all postconditions passed and this report was flushed.

Approval, protected-original, and remediation-report publication each use an exact same-directory
`.staging` leaf. Publication writes and flushes complete bytes to a create-new staging file,
strictly validates them, then atomically moves the staging leaf to an absent final leaf. On restart,
an exact complete final leaf is accepted; an exact complete staging leaf is moved; an incomplete
ordinary program-owned staging leaf may be deleted and recreated; and any other final or staging
state refuses. No partially written file becomes a final protected record.

## 6. Candidates and gates

The `P12` plan line starts as the direct child of A2R11 `G11`. Until final review releases `R12`,
plan-only corrections may change only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a2-baseline-manifest-row-diagnosis.md
    atlas-v0-a2-baseline-manifest-row-remediation.md
    atlas-v0-a2-intake-safety-plan.md
```

Plan-review `R12` is the direct child of the final reviewed `P12` tip and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-baseline-manifest-row-remediation-plan-review.md
```

Only after verified shared `R12` may the existing session observer be extended, built, tested,
hashed, and independently reviewed. Only the read-only qualification mode may then run.

If qualification is ineligible or approval is declined, no approval record or remediation runs. If
approval is granted, its record remains protected and the public Git topology does not change.

After private execution, completion `G12` adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-baseline-manifest-row-remediation-completion.md
```

`G12` is always the direct child of `R12`. Its content and topology remain compatible with every
private eligibility, approval, and remediation result.

## 7. Acceptance and handoff

Before qualification:

1. exact `P12` and `R12` are reviewed, pushed, and verified;
2. `HEAD`, upstream, and the clean worktree equal `R12`;
3. released Atlas source remains unchanged from A2R8 `G`;
4. the exact source builds with zero warnings and errors;
5. existing A2R10 and A2R11 synthetic suites still pass;
6. synthetic tests cover every source refusal and both eligibility signals;
7. tests prove zero, one, and multiple purpose cardinality; zero, one, and multiple candidate
   cardinality; every non-purpose predicate; prior rejection; replacement acceptance; and only-one-
   field object difference;
8. path tests cover exact backup/staging containment, ordinary non-reparse components, fresh
   absence, restart presence, and outside/type/reparse refusal;
9. replacement tests cover fresh success, complete and incomplete replacement-staged restart,
   replaced-with-transient-backup restart, post-backup-cleanup restart, and every other
   current/backup/staging refusal without source mutation;
10. approval and remediation report tests cover exact schemas, create-new behavior, matching run
    identifiers, atomic publication, partial-staging recovery, fixed signals, and empty standard
    error;
11. every review finding receives `TP` or `FP` adjudication; and
12. a fresh independent source reviewer returns `No findings`.

A2R12 closes only when the selected branch's result-free completion record receives independent
`No findings`, is committed unchanged as `G12`, and is pushed as the clean shared tip. No discovery
retry follows without another separately persisted and reviewed plan.
