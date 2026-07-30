# Atlas V0 A5 Explicit Snapshot Corpus Survey

**Lifecycle:** Conditional: proposed governing plan before verified shared `R5R1`; active normative
after verified shared `R5R1`

**Increment:** A5R1 - Explicit Snapshot Corpus Survey Runner

**Decision owner:** Project leader

**Base:** Exact verified shared `G4R1`
`4b25e9c7161997f968d1becd3aa79486ea4a7ac0`

**Purpose:** Integrate the released A3 snapshot and reader with the released A4 structural scanner so
one explicitly selected finalized snapshot can be surveyed completely into deterministic private
artifacts, without rediscovering originals, interpreting semantics, editing, or writing save data.

> **No authority by presence**
> This file changes no active authority until its exact persisted candidate is independently reviewed,
> committed, pushed, and activated by verified shared `R5R1`. Git gates establish provenance only.
> They are never runtime authorization. Implementation, review, and release use synthetic data only.
> A real survey requires a later explicit user-supplied request naming the finalized snapshot receipt.

## 1. Context and threat model

Celesphonia Modifier is trusted, single-user local software. A5R1 addresses credible integration and
private-artifact failures:

- accidentally reopening or mutating original saves during survey;
- accepting an incomplete, changed, or malformed A3 snapshot;
- silently omitting a copied save from the corpus;
- decoding or scanning a document under the wrong role;
- writing partial or inconsistent private survey output;
- unexplained nondeterminism between runs over the same finalized snapshot;
- leaking private paths, hashes, counts, or structural observations into repository output or CLI
  diagnostics;
- unbounded aggregate work across the snapshot; and
- cancellation or interruption leaving ambiguous owned output.

The accepted environment trusts the local user, administrator, checkout, runtime, and selected
binaries. A5R1 does not address malicious replacement by those trusted actors and adds no runtime Git
or binary attestation, authorization ceremony, document SHA graph, r1/r2 protocol, multi-party
approval, inventory ledger, or persistent state machine.

Direct per-file SHA-256 values remain appropriate inside the private snapshot receipt and private
survey manifest because they verify copied input and persisted output bytes. They are data-integrity
facts, not authorization or provenance ceremony.

## 2. Relationship to prior plans

Before verified shared `R5R1`:

- released `G4R1` remains the implementation boundary;
- historical A5 in `atlas-v0-execution-plan.md` remains context without authority; and
- this candidate grants no filesystem, persistence, private-data, or real-survey authority.

After verified shared `R5R1`, this plan:

- supersedes only historical A5 survey-runner mechanics in
  `atlas-v0-execution-plan.md`;
- partially supersedes only historical A5 mechanics in `save-semantic-atlas-plan.md`;
- preserves every released A1 through A4 command and contract;
- reuses only a finalized A3 save snapshot, the A3 reader, and the A4 scanner;
- authorizes the repository-safe synthetic C5R1 implementation defined here; and
- leaves any actual private survey result, semantic correlation, editing, and writes outside C5R1 and
  G5R1.

Historical installed-definition correlation belongs to A6 or a later separately planned increment.
A5R1 neither reads nor aggregates the released A2R15 definition snapshot.

## 3. Outcome and claim

A successful A5R1 command may claim only that:

1. the caller explicitly selected one valid finalized A3 snapshot receipt;
2. the finalized snapshot was validated without reopening its recorded live source root;
3. every receipt entry was read, decoded, scanned under its filename-derived role, persisted, and
   terminally represented exactly once;
4. every persisted scan validated against the copied source document;
5. per-document and aggregate structural censuses reconciled;
6. the private manifest described the complete fixed-order output set; and
7. the incomplete output directory was promoted only after the complete candidate validated.

The claim is relative to the immutable selected A3 receipt entry set. It is not a claim that the
snapshot is current, that it covers every save ever created, that multiple snapshots were compared,
that any field has known meaning, or that any edit is safe.

A5R1 is all-or-nothing. It produces no accepted partial corpus, gap ledger, or successful subset.

## 4. Product surface and contracts

A5R1 adds one CLI command:

```text
snapshot-survey <request-path>
```

The command accepts one strict JSON request and performs the complete private-output operation in one
invocation. It never discovers a save root, installation, Steam library, definition tree, or ambient
snapshot.

A5R1 adds exactly two contracts:

- `atlas-snapshot-survey-request/v1`; and
- `atlas-snapshot-survey/v1`.

Their repository schemas are:

```text
docs/.copilot/schemas/atlas-v0/atlas-snapshot-survey-request.schema.json
docs/.copilot/schemas/atlas-v0/atlas-snapshot-survey.schema.json
```

### 4.1 Request

The request contains only:

- `schemaVersion`;
- `repositoryRoot`;
- `runId`; and
- `snapshotReceiptPath`.

`repositoryRoot` and `snapshotReceiptPath` are explicit absolute paths. `runId` is exactly 32
lowercase hexadecimal characters and chooses only the derived private survey workspace. The request
contains no source save root, file selection, output override, semantic option, Git identity, binary
identity, authorization token, or configurable resource limit.

The request is limited to 64 KiB, JSON depth 8, 256 tokens, 32,768 UTF-16 code units per string, and
20 ASCII characters per numeric token. Unknown or duplicate properties, comments, trailing commas,
explicit `null`, and invalid path or run identifiers are rejected before use.

### 4.2 Private survey manifest

The canonical private manifest contains:

- `schemaVersion`;
- fixed-order `documents`; and
- aggregate `totals`.

Each document contains only:

- canonical copied-save relative path;
- closed document role;
- fixed derived scan relative path;
- copied-source byte length and SHA-256 from the validated A3 receipt;
- persisted scan byte length and SHA-256;
- the complete A4 structural census.

`totals` contains document count, copied-source bytes, canonical scan bytes, and checked sums of every
A4 census field.

The manifest contains no repository root, live save root, snapshot path, output path, run ID, time,
machine identity, semantic label, scalar value, property name, class string, or raw JsonEx identity.
Because it contains private file hashes and structural counts, it remains under `.private/` and never
enters Git.

Manifest entries use the A3 receipt's fixed canonical order. The scan relative path is the canonical
copied-save name plus `.structural-scan.json`.

The manifest uses UTF-8 without BOM, no insignificant whitespace, fixed property order, and one final
LF. Parsing is strict and bounded; reserialization must reproduce the exact bytes. Maximum manifest
size is 256 KiB.

## 5. Snapshot validation and closed corpus

The request must name the exact final receipt path:

```text
<repositoryRoot>/src/private/app/celesphonia-modifier/.private/
  atlas-save-snapshot/<snapshotRunId>/save-snapshot/save-snapshot-receipt.json
```

`snapshotRunId` must satisfy the released A3 identifier rule. Existing path components from the
repository root through the final snapshot root must be ordinary non-reparse directories.

A5R1 extracts a reusable validation-only boundary from the released A3 final-candidate validator. It:

- strictly reads the receipt;
- validates the fixed final layout and receipt binding;
- permits only the receipt plus canonical save leaves directly under the final snapshot root;
- verifies every copied file is ordinary and non-reparse;
- verifies fixed ordering, uniqueness, length, and SHA-256 for every receipt entry; and
- returns detached validated entry facts and contained copied-file paths.

Validation never uses `receipt.SaveRoot` as an input path and never accesses any live source. The
field remains part of the released A3 receipt and is parsed only to preserve that contract.

Every receipt entry is in scope. A5R1 has no include list, exclude list, representative subset, or
scope-narrowing switch. An empty, missing, extra, duplicated, changed, or unsupported entry refuses
the entire survey.

The document role is derived only from the canonical copied-save name:

- `global.rpgsave` -> `global-save`;
- `config.rpgsave` -> `config-save`; and
- `file1.rpgsave` through `file20.rpgsave` -> `slot-save`.

## 6. Survey processing and reconciliation

Documents are processed sequentially in receipt order. For each copied save, A5R1:

1. opens only the copied snapshot file for read with no write sharing;
2. reads it within the released A3 compressed-byte limit while computing length and SHA-256;
3. requires those facts to match the validated receipt;
4. runs the released A3 lossless reader;
5. runs the released A4 scanner under the derived role;
6. writes canonical A4 bytes to a create-new file beneath the exact incomplete survey root;
7. flushes, reopens, and parses the persisted scan against the same A3 result; and
8. records the verified private integrity facts and census for the manifest.

The runner does not retain more than one source byte buffer, A3 result, A4 result, and canonical scan
buffer at a time. It does not follow references beyond released A4 behavior and does not create
cross-document structure or semantic links.

Checked aggregate arithmetic sums document count, source bytes, canonical bytes, and every census
field. The aggregate manifest must equal the exact sum of its document entries. Every document entry
must equal the canonical scan it names, and every scan must validate against its copied source.

## 7. Private layout and recovery

The request derives one fixed output workspace:

```text
<repositoryRoot>/src/private/app/celesphonia-modifier/.private/
  atlas-snapshot-survey/<runId>/
```

Its only owned roots are:

```text
survey.incomplete
survey
```

Each candidate root may contain only:

- one scan file for every validated snapshot entry; and
- `snapshot-survey-manifest.json`.

No request field can redirect output. Repository, application, private parent, workspace, and present
candidate components must satisfy the same ordinary-directory and containment principles as A3.

Recovery is directory-based:

- **Final absent, incomplete absent:** create a new incomplete survey.
- **Valid final present:** return success without changing it.
- **Valid incomplete present, final absent:** promote it and return success.
- **Invalid cleanable incomplete present, final absent:** delete only allowlisted ordinary files,
  remove the exact now-empty incomplete root nonrecursively, and restart.
- **Both roots present, invalid final, unexpected child, subdirectory, reparse, unsupported entry, or
  ambiguous path:** refuse unchanged.

A candidate is valid only when the snapshot still validates, every expected scan is present with no
extra child, every scan parses against its copied source, and the exact canonical manifest reconstructed
from those results matches the persisted manifest.

No original path participates in creation, cleanup, validation, or promotion. A reader or scanner
failure never invalidates, alters, renames, or deletes the finalized A3 snapshot.

## 8. Determinism and repeatability

For one unchanged finalized snapshot and released toolchain:

- receipt order fixes processing and manifest order;
- copied-save names fix roles and output names;
- A4 fixes observation preorder and scan bytes;
- manifest property and entry order is fixed;
- survey run ID and paths do not enter persisted candidate bytes; and
- no time, random value, locale, machine name, or ambient enumeration enters output.

Two successful runs using different A5 run IDs over the same snapshot receipt must produce
byte-identical corresponding scan files and manifest. This is local private repeatability, not
cross-version or independent-implementation reproducibility.

## 9. Limits, cancellation, and diagnostics

A5R1 preserves the released A3 and A4 per-document defaults and adds fixed aggregate defaults:

- at most 22 documents;
- at most 2,000,000 total observations;
- at most 512 MiB total canonical scan bytes; and
- at most 256 KiB canonical manifest bytes.

Tests may inject smaller aggregate limits through an internal seam. The request cannot weaken or
expand limits.

Cancellation is checked during request and receipt reads, snapshot hashing, source reads, A3 reading,
A4 traversal and serialization, output writes, output verification, aggregate reconciliation,
manifest processing, recovery cleanup, and promotion. Caller cancellation surfaces through the
existing cancellation exit.

The CLI emits only fixed payload-free messages:

- success after a valid final survey exists;
- existing invalid-argument behavior for malformed invocation or request;
- existing cancellation behavior;
- existing I/O failure behavior;
- existing safety failure behavior for invalid snapshot, reader rejection, scanner rejection,
  aggregate refusal, or invalid output state; and
- existing unexpected failure behavior only for an unclassified defect.

No diagnostic contains a path, hash, file count, census, locator, observation, source value, or
exception text.

## 10. Implementation and acceptance evidence

C5R1 may change only the minimum:

- A3 final-snapshot validation extraction;
- A5 request, manifest, runner, limits, and canonical JSON;
- CLI routing and fixed help/success text;
- the two schemas;
- focused synthetic tests; and
- project-boundary inventories.

Synthetic tests cover:

- one global, config, and slot document and a mixed 22-entry maximum corpus;
- exact receipt order, role derivation, scan naming, entry closure, and aggregate census;
- validation without access to the receipt-recorded live `saveRoot`;
- missing, extra, duplicate, changed, corrupt, wrong-case, reparse, directory, and out-of-root snapshot
  or output cases;
- reader and scanner rejection leaving the final snapshot unchanged;
- persisted scan reopen and source-bound parse;
- exact canonical manifest bytes, schema execution, strict parsing, and mutation rejection;
- two different survey run IDs producing byte-identical complete candidates;
- aggregate observation, canonical-byte, checked-arithmetic, request, and manifest limits;
- cancellation before work and during snapshot validation, source reading, scanning, writing,
  verification, manifest creation, cleanup, and promotion;
- every recovery branch, including refusal of non-cleanable incomplete state;
- CLI help, success, invalid request, cancellation, I/O, safety, and unexpected results;
- no private facts in CLI output or repository fixtures; and
- preservation of all released tests and commands.

Acceptance requires:

1. targeted and full Atlas tests pass;
2. Release build has no warnings or errors;
3. formatting, schemas, HK, and diff checks pass;
4. every synthetic snapshot entry is accounted for exactly once;
5. persisted scan and manifest candidates validate from disk, not only in memory;
6. repeatability produces exact matching candidate bytes;
7. final review returns `No findings`; and
8. no real save, snapshot, definition, game installation, or ignored private content is accessed.

## 11. Explicit exclusions and deferred private execution

A5R1 excludes:

- creating a real A3 snapshot during planning, implementation, review, or release;
- accessing an existing real snapshot during planning, implementation, review, or release;
- live save discovery, registry, Steam, installation, or definition access;
- selecting a subset from a snapshot;
- multiple snapshots or cross-snapshot comparison;
- cross-document presence, variation, aliasing, or semantic correlation;
- installed-definition denominators;
- partial success, gap ledgers, scope narrowing, or content-based redaction decisions;
- committed private manifests, scans, receipts, hashes, counts, or paths;
- automatic cleanup or artifact inventory protocols;
- editing, encoding, rewriting, backup, restore, transactions, or original-data writes;
- WinUI, networking, telemetry, databases, installers, or distribution; and
- runtime attestation, authorization ceremony, document SHA graphs, or persistent protocol state.

After verified shared `G5R1`, a real survey still requires the user to explicitly supply the absolute
path to one finalized A3 receipt in a request. If no finalized snapshot exists, the user must
separately supply the absolute live save root to the released A3 `save-snapshot` command first.
Neither path may be inferred from the repository, installation, environment, or prior private state.

The real private run and its repository-safe completion evidence are a separate A5R2 execution. It
may use the released command unchanged but must not publish private paths, hashes, scans, values, or
structural counts.

## 12. Review policy

The independent holistic reviewer judges the complete exact candidate only for credible:

- reopening or mutating originals;
- snapshot-validation, corpus-closure, role, reconciliation, or persistence defects;
- privacy leakage;
- nondeterminism;
- malformed-contract or resource-bound failures;
- cancellation or recovery defects;
- regression of released behavior; or
- maintainability problems that create one of those risks.

Findings based only on malicious-owner substitution, runtime Git or binary attestation,
authorization ceremony, document SHA graphs, r1/r2 state, inventories, multi-party approvals,
semantics, editing, writes, or other explicit deferrals are out of scope.

After two consecutive review rounds with structural findings, return to the ideal minimal design
instead of continuing incremental hardening. Stop review after the complete candidate and recorded
dispositions receive `No findings`.

## 13. Git gates

The A5R1 gates establish release provenance only. Runtime code never inspects commits, records, Git
state, source hashes, or binary hashes.

### P5R1 - plan candidate

`P5R1` is the direct child of exact `G4R1`
`4b25e9c7161997f968d1becd3aa79486ea4a7ac0` and changes exactly:

```text
src/private/app/celesphonia-modifier/docs/.copilot/README.md
src/private/app/celesphonia-modifier/docs/.copilot/plans/
  atlas-v0-a5-explicit-snapshot-corpus-survey.md
  atlas-v0-execution-plan.md
  save-semantic-atlas-plan.md
```

An independent general-purpose GPT-5.6 reviewer examines the exact four-document candidate
holistically until `No findings`. The candidate is then committed and pushed unchanged.

### R5R1 - plan-review activation record

`R5R1` is the direct child of exact `P5R1` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a5-explicit-snapshot-corpus-survey-plan-review.md
```

The record binds the exact base and candidate, reviewed path set, reviewer identity and independence,
review iterations, TP/FP dispositions, repository-safe validation, and final `No findings`. The exact
staged record receives independent `No findings`, is committed unchanged, pushed, and verified.

Verified shared `R5R1` activates this plan and authorizes only synthetic C5R1.

### C5R1 - implementation candidate

`C5R1` is the direct child of exact `R5R1`. It contains only the minimum library, CLI, two schemas,
automated tests, and project-boundary changes required by this plan. It uses synthetic repository-safe
data only and is committed and pushed before holistic implementation review.

Review covers the complete exact candidate, acceptance evidence, privacy and resource boundaries,
regression risk, and recorded dispositions until `No findings`.

### G5R1 - release record

`G5R1` is the direct child of exact reviewed `C5R1` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a5-explicit-snapshot-corpus-survey-release-gate.md
```

The release record binds the exact candidate, governing plan and R5R1 record, reviewed paths,
validation outcomes, findings and dispositions, reviewer independence, and final `No findings`. The
exact staged record receives independent `No findings`, is committed unchanged, pushed, and verified.

`G5R1` releases the survey runner only. It does not claim that a real private survey occurred.

## 14. Stop conditions and handoff

Stop and return to planning if implementation requires:

- reading a live save root or using `receipt.SaveRoot` as an input path;
- accepting an unfinalized or partially valid snapshot;
- selecting fewer than all receipt entries;
- multiple snapshots, definitions, semantic correlation, or cross-document variation;
- output outside the exact derived private workspace;
- partial accepted results or gap records;
- recursive deletion or cleanup of an unexpected child;
- configurable runtime limits;
- a third A5R1 contract;
- private data in Git or CLI diagnostics;
- real saves, game files, installations, definitions, or ignored private content;
- editing, encoding, save writing, backup, restore, or transactions; or
- authorization ceremony, runtime attestation, document SHA graphs, or persistent protocol state.

Resume implementation only from verified shared `R5R1`. After verified shared `G5R1`, ask the user
for the exact finalized snapshot receipt path before planning or performing A5R2 private execution.
