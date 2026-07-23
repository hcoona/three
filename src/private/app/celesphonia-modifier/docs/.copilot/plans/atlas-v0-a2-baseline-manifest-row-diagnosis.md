# Atlas V0 A2 Baseline Manifest Row Diagnosis

**Lifecycle:** Historical supporting; completed at verified shared `G11`

**Status:** Completed; grants no current execution authority

**Increment:** A2R11 - Baseline Manifest Row Diagnosis

**Decision owner:** Project leader

**Purpose:** Identify which fixed released baseline-manifest inventory-row predicate requires
correction without publishing private values or authorizing a state change.

**Implementation language:** Session-only C#

**Base:** `c7300d9fbbe93b62262dc80a25aa1aa550b3e3fa`

**Governing sources:**

- `project-operating-model.md`;
- `atlas-v0-a2-intake-safety-plan.md`.

**Historical provenance:**

- `atlas-v0-a2-current-baseline-observation.md`; and
- `../reviews/atlas-v0-a2-current-baseline-observation-completion.md`.

The completed A2R10 sources establish the protected input class and grant no A2R11 authority.

**Dependencies:** Verified shared A2R10 `G10`, its protected observation report, unchanged released
Atlas source, preserved current inventory, and independent review of this plan and exact diagnostic
source.

**Planned plan-review record:**
`../reviews/atlas-v0-a2-baseline-manifest-row-diagnosis-plan-review.md`

**Planned completion record:**
`../reviews/atlas-v0-a2-baseline-manifest-row-diagnosis-completion.md`

## 1. Safe conclusion and claim

A2R10 completed one current-state observation. Its protected result supports the repository-safe
conclusion that the released baseline-manifest inventory-row relationship requires one narrower
diagnosis before A2 can continue. This statement reveals no path, hash, value, row content, or
predicate result.

A2R11 classifies only that released relationship. It does not claim the row is wrong, the validator
is wrong, or remediation is authorized. Its result selects a separately planned production
correction, private remediation, or authority clarification.

## 2. Scope

In scope:

- reuse the exact A2R10 session observer project and protected report directory;
- read the single protected A2R10 report and require its current-inventory fingerprint;
- locate and strictly read the single current canonical inventory;
- require its byte length and SHA-256 to match the A2R10 fingerprint;
- select rows with released purpose `ManifestRevision3Purpose`;
- classify cardinality and each predicate in `ValidateBaselineManifestArtifact`;
- write one create-new private diagnosis report;
- emit only the existing fixed report-recorded signal; and
- retain all private evidence under protected session state.

Out of scope:

- reading the request, manifest, backup, wrapper, game, saves, definitions, or generated Atlas
  outputs other than the current inventory;
- publishing a literal row field, alias, path, hash, count, value, or document fragment;
- changing production, tests, schemas, packages, tracked projects, or CLI behavior;
- modifying any existing private input or operational Atlas artifact;
- discovery, confirmation, copy, cleanup, repair, or private remediation;
- historical identity, cross-file atomicity, hostile-local defense, or one-shot authority; and
- deciding the final correction before the private diagnosis exists.

## 3. Diagnostic contract

The exact reviewed program gains a `--diagnose-manifest-row` mode that receives the public
repository root and a 32-character lowercase hexadecimal run identifier. It:

1. derives the protected session `files` root and observer project directory from the exact
   `a2r10-current-baseline-observer/bin/Release/<target-framework>/` location, validates both as
   ordinary non-reparse directories, and confines both source and diagnosis reports to that project
   directory;
2. enumerates only direct ordinary files named
   `a2r10-current-baseline-observation-<32-lowercase-hex>.json` and requires exactly one;
3. strictly parses that report as a JSON object with exactly case-sensitive members
   `schemaVersion`, `outcome`, `terminalStage`, and `files`, rejecting unknown, missing, duplicate,
   or differently cased members;
4. requires schema `atlas-a2-current-baseline-observation/v1`, outcome `valid` or `refused`, a
   released terminal-stage name, and a `files` array whose entries have exactly `role`,
   `byteLength`, and `sha256`;
5. requires each fingerprint role to be one of `request`, `baseline-manifest`,
   `current-inventory`, or `inventory-backup`, its byte length to be a nonnegative JSON integer, and
   its SHA-256 to be 64 lowercase hexadecimal characters, then requires exactly one
   `current-inventory` entry;
6. derives
   `src/private/app/celesphonia-modifier/.private/atlas-v0` from the normalized public repository
   root, validates it as an ordinary non-reparse directory, enumerates only its immediate ordinary
   non-reparse child directories, and requires exactly one child containing
   `intake/requests/discover.json`;
7. derives that child's `intake/private-artifact-inventory.json`, validates it as an ordinary
   non-reparse file, and loads it through `AtlasIntakeContracts.ReadInventoryAsync`;
8. requires the returned byte length and SHA-256 to match the A2R10 fingerprint;
9. selects rows with the exact released baseline-manifest purpose; and
10. records the fixed predicate results in memory before one create-new report write.

The released terminal-stage names accepted in step 4 are `workspace-selection`, `request`, `layout`,
`baseline-manifest`, `inventory-transition`, `discovery-aliases`, `manifest-row`, `next-ordinal`,
and `complete`. Invalid UTF-8, malformed JSON, trailing JSON content, a non-object root, or any
failed requirement above is source refusal and never enters process output.

The diagnosis report is exactly one of these closed, case-sensitive JSON object shapes:

```json
{
    "schemaVersion": "atlas-a2-baseline-manifest-row-diagnosis/v1",
    "outcome": "source-refused"
}
```

```json
{
    "schemaVersion": "atlas-a2-baseline-manifest-row-diagnosis/v1",
    "outcome": "diagnosed",
    "cardinality": "zero"
}
```

```json
{
    "schemaVersion": "atlas-a2-baseline-manifest-row-diagnosis/v1",
    "outcome": "diagnosed",
    "cardinality": "multiple"
}
```

```json
{
    "schemaVersion": "atlas-a2-baseline-manifest-row-diagnosis/v1",
    "outcome": "diagnosed",
    "cardinality": "one",
    "mismatches": []
}
```

For cardinality `one`, `mismatches` contains each failed predicate at most once and in this fixed
order: `artifact-class`, `custodian-role`, `lineage`, `last-use`, `expiry`, `disposition`, `status`,
`qualification`, and `verification-method`. No other member, value, null, duplicate member, or
differently cased member is valid. The report contains no literal row value, alias, path, source
hash, content, or dynamic exception detail. An empty mismatch array means the released helper must
accept the same row. A nonempty array means the released helper must refuse it. Synthetic tests
prove both reconciliation directions and strict parsing of every report shape.

The report filename is
`a2r11-baseline-manifest-row-diagnosis-<run-id>.json` beneath the validated session project
directory. It uses create-new semantics. Standard output remains exactly
`observation-recorded\n` or `observation-not-recorded\n`; standard error remains empty.

## 4. Candidates and gates

The `P11` plan line starts as the direct child of A2R10 `G10`. Until final review releases `R11`,
every plan-only correction commit may change only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a2-baseline-manifest-row-diagnosis.md
    atlas-v0-a2-current-baseline-observation.md
    atlas-v0-a2-intake-safety-plan.md
```

Plan-review `R11` is the direct child of the final reviewed `P11` tip and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-baseline-manifest-row-diagnosis-plan-review.md
```

Only after verified shared `R11`, modify and rebuild the existing session observer source. Before
private diagnosis:

1. bind clean `HEAD` and upstream to exact `R11`;
2. prove released Atlas source remains unchanged from A2R8 `G`;
3. run the exact existing A2R10 self-tests and new A2R11 synthetic tests;
4. retain exact source, observer assembly, and Atlas assembly hashes; and
5. independently review the complete exact source until `No findings`.

Completion `G11` is the direct child of `R11` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-baseline-manifest-row-diagnosis-completion.md
```

It records public provenance, source hashes, validation, privacy, and that a diagnosis report was
recorded. It omits the report hash, outcome, cardinality, mismatch names, and future route.

## 5. Acceptance and handoff

A2R11 may enter private diagnosis only when:

1. `P11` and `R11` are reviewed, pushed, and verified;
2. the complete source builds with zero warnings and errors;
3. static review finds no input write, state-changing command, dynamic output, or unlisted read;
4. synthetic tests cover A2R10 report selection, schema, role cardinality, fingerprint mismatch,
   and source-refused field omission;
5. every cardinality and fixed predicate mismatch has exact synthetic coverage;
6. synthetic tests reconcile the fixed classification with the released helper;
7. report create-new, exact signal, and empty standard error are proved;
8. every finding receives `TP` or `FP` adjudication; and
9. a fresh independent reviewer returns `No findings`.

A2R11 closes when one final private diagnosis report is recorded, the record parses against the
closed schema, existing inputs remain unchanged, the result-free completion receives independent
`No findings`, and record-only `G11` is the clean shared tip.

If source selection, fingerprint binding, or report publication fails, retry with a fresh run
identifier is allowed because diagnosis is repeatable and read-only. A2R11 authorizes no action on
its result. The next plan starts only after verified shared `G11`.
