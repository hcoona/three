# Atlas V0 A0 Historical Authority Diagnosis

**Lifecycle:** Proposed active subordinate; plan-only before verified shared `R0R5`

**Status:** Diagnostic implementation and private reads blocked

**Increment:** A0R5 - Historical Authority Diagnosis

**Decision owner:** Project leader

**Decision:** Run one read-only, fixed-predicate replay of only the historical-authority gate to record
one outcome and identify its first contract group only on a controlled refusal, without reading a
runtime locator or current game tree.

**Base G0R4:** `9e93097ae56ed0728219b1e11936b0febf01e9f0`

**A0R4 source qualification:** `95154899e5ff1a88d2ec88346fff4525a0cf6c32`

**Normative governing sources:**

- `project-operating-model.md`;
- the corpus and privacy sections imported from `atlas-v0-a0-current-corpus-refresh.md`;
- the `trusted-local-filesystem/v1` profile defined by section 2 of
  `atlas-v0-a2-intake-safety-plan.md`; and
- project and documentation `AGENTS.md`.

**Historical evidence and technical provenance:**

- `atlas-v0-a0-no-candidate-stage-diagnosis.md`;
- `../reviews/atlas-v0-a0-no-candidate-stage-diagnosis-source-qualification.md`; and
- `../reviews/atlas-v0-a0-no-candidate-stage-diagnosis-completion.md`.

This plan establishes every A0R5 authority and retained technical contract. A0R4 supplies exact source
provenance and one fixed outer-gate result but grants no current execution authority.

**Planned plan-review record:**
`../reviews/atlas-v0-a0-historical-authority-diagnosis-plan-review.md`

**Planned source-qualification record:**
`../reviews/atlas-v0-a0-historical-authority-diagnosis-source-qualification.md`

**Planned completion record:**
`../reviews/atlas-v0-a0-historical-authority-diagnosis-completion.md`

## 1. Problem, claim, and minimum shape

A0R4 completed at verified shared `G0R4` with the fixed class
`historical-authority-refused`. Its marker consumed all A0R4 authority. The class identifies only the
outer gate; it does not identify a private cause or individual predicate.

The next useful evidence is the first existing contract group inside that gate. A repository-only source
review cannot select it because the consequential request and approved manifest are protected inputs.
Repeating A0R4 would be unauthorized and would still return only the same outer class.

A0R5 therefore:

- creates one new protected workspace and one new marker;
- reads only the two fixed historical input files after that marker;
- preserves the exact A0R4 historical validation order and semantics;
- maps eleven typed contract groups to fixed tokens;
- creates one matching fixed-class receipt or closes incomplete;
- never reads a runtime locator or any current game-tree metadata; and
- returns every branch to separately authorized replanning or source/authority adjudication.

A0R5 claims only one fixed current replay outcome. A controlled refusal identifies the first reviewed
historical contract group that does not complete. `historical-authority-ready` identifies completion of
all eleven groups, `diagnostic-internal-refused` identifies no group, and an incomplete attempt
identifies no result class.

No outcome claims:

- a private value or cause within a group;
- that A0R4 would now produce the same result;
- that a request, manifest, source contract, or authority artifact should change;
- that a candidate is valid or publishable; or
- that A2, A3, decoding, current-tree access, or any original-data write is authorized.

## 2. Exact historical inputs and retained contract

The utility derives exactly these two fixed paths without enumerating the protected workspace or reading
another historical artifact:

```text
request
  <repository-root>\src\private\app\celesphonia-modifier\.private\atlas-v0\
    survey-000001\intake\requests\discover.json
approved manifest
  <repository-root>\src\private\app\celesphonia-modifier\.private\atlas-v0\
    survey-000001\intake\corpus-intake-manifest.json
```

Both must be ordinary non-reparse files on a ready fixed local drive under
`trusted-local-filesystem/v1`.

The request is bounded JSON with one top-level object. Only these four fields are consequential:

```text
schemaVersion = atlas-intake-discovery-request/v1
expectedBaselineSha256 = <one lowercase SHA-256 digest>
expectedSteamAppId = 1786790
expectedBuildId = 13624401
```

Each occurs exactly once with its stated JSON type; the IDs are integers. All other request fields,
including every historical locator and output path, are inert regardless of absence, JSON type, or
value. Unknown members are inert.

The approved-manifest bytes must:

- hash to `expectedBaselineSha256`;
- strictly deserialize through the released Atlas `atlas-intake/v2` contract;
- equal their deterministic canonical reserialization;
- identify alias `survey-000001` and revision 3;
- use validation method `manual-a0`;
- have confirmation status `approved`, role `project-leader`, and decision reference
  `commit:3610d5e2a69073672bda665eed25a545a141c06b`;
- use only absent or nonempty lowercase ASCII letter, digit, and hyphen reason codes;
- classify every save entry exactly by filename role, slot, and decision; and
- assign every definition entry to the manifest's ordered first matching rule and decision.

The approved manifest remains the sole corpus-specific authority. The request supplies only the
minimum byte and public-game anchor above.

## 3. Scope and exclusions

In scope:

- persist and independently review this plan before implementation;
- derive one fresh protected C# utility from exact qualified A0R4 project and source bytes;
- retain A0R4 CLI, fixed-output, source-binding, Git, process-capture, marker, receipt, strict JSON, and
  historical-authority code needed by this increment;
- delete runtime-locator, current-tree, metadata census, alias allocation, candidate construction,
  candidate replay, and candidate publication machinery;
- express the existing historical sequence as eleven typed boundaries;
- publish one durable marker before either historical file is inspected;
- publish at most one strict fixed-class receipt;
- qualify exact source and assemblies before private access;
- execute at most one diagnostic attempt; and
- publish one result-safe completion record.

Out of scope:

- retrying or resuming A0R4;
- reading A0R4 marker, receipt, locator, binding, or any historical runtime state;
- reading any runtime locator, save root, definition root, executable, installed-file metadata, or
  source content;
- exposing a private historical-input path, filename, value, count, hash, difference, manifest field,
  exception, or individual predicate; repository-safe source identity remains required;
- parsing exception messages or creating per-value identifiers;
- changing released Atlas production source, schemas, packages, or tracked tests;
- publishing or deciding a candidate or refreshed manifest;
- correcting source or protected authority from the diagnostic result;
- A2 discovery, confirmation, copy, cleanup, or A3 parsing;
- telemetry, dumps, logs, network access, or Agent access to private inputs;
- hostile-local defenses beyond `trusted-local-filesystem/v1`; and
- any original-data write.

## 4. Protected workspace and source derivation

Only after verified shared `R0R5`, create a new protected Git-ignored A0R5 workspace containing exactly:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R5.csproj
Program.cs
state/
```

The project and source initially match the exact qualified A0R4 technical inputs before A0R5 edits:

```text
project
  ecfa6b2117fbbe0eda5d57f7968485eaef8f9a204a54950c7c43e59d6d120935
Program.cs
  4dfbb6a8813c3c24b11125a385a0bae3aaae164902962ba747c474a6850c5ea2
```

The new `state` directory begins empty. Do not copy A0R4 build output, source binding, locator, marker,
receipt, or any prior runtime artifact.

The implementation must remove, not bypass:

- every A0R4 state filename and authority contract;
- root-locator parsing and derivation;
- current-tree metadata and stability capture;
- save and definition enumeration;
- alias allocation;
- candidate construction, codec replay, staging, and publication; and
- synthetic fixtures or helpers that exist only for those deleted behaviors.

Retain only the smallest source, binding, Git, publication, historical parsing, manifest policy, and
synthetic-test machinery required by A0R5.

## 5. CLI, state, and fixed outputs

The utility has exactly two noninteractive modes:

```text
--test
  --repository-root <repository-root>
  --workspace-root <a0r5-workspace-root>
  --run-id <run-id>

--diagnose
  --repository-root <repository-root>
  --workspace-root <a0r5-workspace-root>
  --run-id <run-id>
```

Every invocation requires one fresh, never-reused 32-character lowercase hexadecimal run ID. Unknown,
missing, duplicate, or extra arguments refuse before private reads.

The utility derives only:

```text
<workspace-root>\state\a0r5-historical-diagnostic-attempt.json
<workspace-root>\state\a0r5-historical-diagnostic-receipt.json
```

The marker is canonical JSON with exactly:

```text
schema = atlas-a0r5-historical-diagnostic-attempt/v1
toolRevision = atlas-a0r5/1
attemptId
sourceBindingsSha256
s0r5
```

It is create-new at its final path, flushed, strictly reloaded, and durable before either historical
file is inspected. A complete, partial, or zero-byte marker consumes A0R5. Any later invocation that
observes the marker returns only `diagnostic-refused`.

The receipt is canonical JSON with exactly:

```text
schema = atlas-a0r5-historical-diagnostic-receipt/v1
toolRevision = atlas-a0r5/1
attemptId
sourceBindingsSha256
s0r5
resultClass
```

It is create-new at its final path, flushed, strictly reloaded, and written only after one fixed class
is selected. A complete matching receipt is authoritative. A missing, partial, malformed, or
inconsistent receipt leaves only the consumed incomplete branch.

Every mode writes exactly one fixed stdout line, keeps stderr empty, and returns:

| Outcome                             | Stdout                                    | Exit |
| ----------------------------------- | ----------------------------------------- | ---: |
| Synthetic tests pass                | `test-passed`                             |    0 |
| Synthetic tests fail                | `test-failed`                             |    2 |
| Diagnostic preflight refuses        | `diagnostic-preflight-refused`            |    2 |
| Historical input safety refuses     | `historical-input-access-refused`         |    2 |
| Request document refuses            | `historical-request-document-refused`     |    2 |
| Released manifest contract refuses  | `historical-manifest-contract-refused`    |    2 |
| Raw manifest byte read refuses      | `historical-manifest-byte-access-refused` |    2 |
| Request public values refuse        | `historical-request-values-refused`       |    2 |
| Manifest-anchor binding refuses     | `historical-anchor-binding-refused`       |    2 |
| Manifest approval envelope refuses  | `historical-manifest-envelope-refused`    |    2 |
| Manifest canonical bytes refuse     | `historical-manifest-canonical-refused`   |    2 |
| Manifest reason-code policy refuses | `historical-manifest-reason-code-refused` |    2 |
| Save-entry policy refuses           | `historical-save-policy-refused`          |    2 |
| Definition-entry policy refuses     | `historical-definition-policy-refused`    |    2 |
| Every historical group completes    | `historical-authority-ready`              |    0 |
| Unexpected post-marker failure      | `diagnostic-internal-refused`             |    2 |
| Marker consumed or receipt unusable | `diagnostic-refused`                      |    2 |
| Unknown mode or arguments           | `operation-refused`                       |    2 |

No receipt-class token may escape without a matching complete receipt. Before marker, an unexpected
failure returns `diagnostic-preflight-refused` when the marker path is absent. After any marker path
exists, failure returns `diagnostic-refused` unless a complete matching internal-refusal receipt is
durable.

## 6. Typed historical pipeline

After the marker is durable, the utility executes these ordered typed boundaries:

| Boundary                   | Exact work                                                              |
| -------------------------- | ----------------------------------------------------------------------- |
| `InputAccess`              | Derive fixed paths and require both safe ordinary files.                |
| `RequestDocument`          | Read request bytes and parse the bounded four-field document.           |
| `ManifestContract`         | Strictly deserialize through the released Atlas manifest contract.      |
| `ManifestByteAccess`       | Read exact raw manifest bytes.                                          |
| `RequestValues`            | Validate request schema, digest shape, Steam app ID, and build ID.      |
| `AnchorBinding`            | Compare the anchor digest with exact manifest bytes.                    |
| `ManifestEnvelope`         | Validate schema, alias, revision, validation, and confirmation fields.  |
| `ManifestCanonical`        | Deterministically serialize and require exact original bytes.           |
| `ManifestReasonCode`       | Validate every optional reason code.                                    |
| `ManifestSavePolicy`       | Reclassify every save entry and require exact role, slot, and decision. |
| `ManifestDefinitionPolicy` | Apply ordered first-match rules and require exact group and decision.   |

The first typed boundary exception maps mechanically to its fixed result class. Mapping never examines
an exception message. Completion of all groups selects `historical-authority-ready`.

Unexpected exceptions after marker map only to `diagnostic-internal-refused`, and only after that class
is persisted and reloaded in a matching receipt. The utility discards historical bytes and parsed
objects when the process exits and creates no other output.

## 7. Terminal branches and interpretation

A0R5 has exactly two terminal private branches:

- **complete fixed-class diagnosis:** marker and one complete matching strict receipt exist; or
- **diagnostic incomplete:** marker exists without one complete matching strict receipt.

Both consume all A0R5 private-read authority. Neither authorizes retry, source correction, authority
correction, current-tree access, candidate publication, A2, A3, or original-data writes.

The result-safe completion may state only:

- complete or incomplete branch;
- one fixed result class when a complete receipt exists; and
- repository-safe source identity.

A controlled class may scope a future repository-safe source/authority adjudication to its named group.
Ready, internal, or incomplete outcomes permit only non-causal replanning from the declared class or
branch. No result automatically chooses or authorizes a correction.

## 8. Synthetic validation and source review

Before private execution, the exact utility must pass:

- formatting;
- warning-free Release build;
- two consecutive complete Release Rebuilds with byte-stable qualified outputs;
- the complete synthetic suite;
- exact project, source, utility assembly, linked Atlas assembly, and source-binding hashing; and
- independent full-source review with TP/FP adjudication until `No findings`.

Synthetic tests must prove:

- exact CLI, fixed stdout bytes, exit codes, and empty stderr;
- zero historical content, metadata, or enumeration access before the durable marker;
- complete, partial, and zero-byte marker consumption without retry-shaped output;
- absent marker after failed creation remains preflight-only;
- exact marker and receipt schemas, canonical bytes, binding, Git binding, and run-ID binding;
- no receipt-class output without one complete authoritative receipt;
- every typed boundary and first-refusal mapping without message parsing;
- unexpected post-marker failure requires a matching internal-refusal receipt;
- strict fixed historical paths and zero protected-workspace enumeration;
- exact four-field anchor behavior with every other request field inert;
- strict released-manifest contract, digest binding, envelope, canonical bytes, reason codes, save
  classification, and definition first-match policy;
- zero locator, current-tree, source-content, candidate, A2, or A3 access;
- zero access to A0R4 state and every other historical runtime artifact;
- exact clean shared `S0R5`, direct-parent/path topology, committed record blob, binding digest, current
  source and assembly hashes, and loaded runtime assembly identity;
- refusal before marker for missing, duplicate, malformed, stale, substituted, or mismatched authority;
- configured upstream absent, behind, ahead, or unequal to exact `S0R5`;
- working-tree record substitution cannot influence committed authority;
- concurrent process capture cannot deadlock on redirected output; and
- drive-root and non-root containment preserve repository/workspace separation.

The source reviewer receives the complete source, project, exact binaries, synthetic suite, and final
source binding. The reviewer receives no historical input, runtime locator, current tree, marker,
receipt, or result.

## 9. Source-qualification gate

After deterministic builds and synthetic validation pass, but before final source review, create one
immutable canonical single-line `source-bindings.json` beside the project and outside `state`. It has
schema `atlas-a0r5-source-bindings/v1`, tool revision `atlas-a0r5/1`, no extra fields, and exactly:

```text
schema
toolRevision
r0r5
projectRelativeName
projectSha256
programRelativeName
programSha256
utilityAssemblyRelativeName
utilityAssemblySha256
atlasAssemblyRelativeName
atlasAssemblySha256
```

Relative names are fixed to:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R5.csproj
Program.cs
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.Tests.dll
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.dll
```

The source-qualification record contains exact `P0R5` and `R0R5`, initial A0R4 source derivation, final
source and assembly hashes, binding hash, validation outcomes, complete TP/FP dispositions, proof that
state remains empty, and only next action `diagnose-once`.

It contains one canonical authority object between:

```text
<!-- atlas-a0r5-source-authority:start -->
<!-- atlas-a0r5-source-authority:end -->
```

The object has exactly:

```text
schema = atlas-a0r5-source-authority/v1
r0r5
sourceBindingsSha256
projectSha256
programSha256
utilityAssemblySha256
atlasAssemblySha256
```

Before marker publication, `--diagnose` must verify:

- `HEAD` and configured upstream both equal exact `S0R5`, with clean tracked worktree;
- `S0R5` is the direct child of exact `R0R5` and adds only the source-qualification record;
- source authority bytes are loaded binary-safely from the verified `S0R5` Git blob, never the
  working-tree record;
- the record has one exact three-line authority block with canonical JSON;
- the block binds exact `R0R5` and the complete current source-binding bytes;
- current project, source, utility assembly, and linked Atlas assembly match every digest; and
- loaded utility and Atlas assembly paths match the bound files.

Any failure remains preflight-only when no marker exists. The exact staged qualification record
receives independent `No findings`, is committed unchanged as `S0R5`, and is pushed and verified before
any historical input is read.

## 10. Git candidates

Plan candidate `P0R5` is the direct child of exact `G0R4` and changes exactly:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a0-historical-authority-diagnosis.md
    atlas-v0-a0-no-candidate-stage-diagnosis.md
    atlas-v0-a2-intake-safety-plan.md
```

Plan-review `R0R5` is the direct child of the final reviewed plan-line tip and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-historical-authority-diagnosis-plan-review.md
```

Source qualification `S0R5` is the direct child of `R0R5` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-historical-authority-diagnosis-source-qualification.md
```

Completion `G0R5` is the direct child of `S0R5` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-historical-authority-diagnosis-completion.md
```

Every candidate is independently reviewed as an exact staged blob and committed unchanged.

## 11. Acceptance criteria

A0R5 completes only when:

1. exact plan and record-only `R0R5` receive independent `No findings`, are committed, pushed, and
   verified;
2. the protected workspace starts only from exact qualified A0R4 project/source and empty state;
3. locator, current-tree, candidate, and obsolete A0R4 machinery is deleted rather than bypassed;
4. eleven typed boundaries preserve existing historical validation order and semantics;
5. exact source passes section 8 and exact `S0R5` is independently reviewed, committed, pushed, and
   verified;
6. exactly one consuming marker is durable before either historical file is inspected;
7. the run produces one complete matching fixed-class receipt or the incomplete branch;
8. no retry occurs after any marker path exists;
9. no runtime locator, current-tree metadata, source content, candidate, A2, or A3 operation occurs;
10. no private detail or causal inference reaches Git, process output, or a subagent;
11. exact result-safe `G0R5` receives independent `No findings` and becomes the clean shared tip; and
12. continuation returns to a separately persisted plan scoped only by the fixed class or incomplete
    branch.

## 12. Stop conditions

Stop before implementation unless exact clean shared `R0R5` is verified.

Stop before marker publication unless exact clean shared `S0R5`, source binding, repository, workspace,
CLI, and empty-state preconditions pass without private reads.

A preflight refusal with no marker path may be corrected and reinvoked with a fresh run ID. This is not
a private diagnostic retry. After any marker path exists, do not retry.

Stop and return to planning if:

- a result requires dynamic output, message parsing, or an individual private predicate;
- preserving historical semantics requires released production changes;
- a runtime locator or current-tree read would be needed;
- the request/manifest authority model, privacy, or threat model must change;
- source must be corrected after the consuming diagnostic; or
- any independent finding remains unresolved.

## 13. Ordered resume procedure

1. Review this exact plan holistically with planning-drift and TP/FP adjudication until `No findings`.
2. Commit and push the exact plan, then add and independently review only the plan-review record as
   `R0R5`.
3. Under exact clean shared `R0R5`, create the fresh protected A0R5 workspace and verify exact initial
   A0R4 source hashes plus empty state.
4. Implement only sections 2 through 9; delete every locator/current-tree/candidate path; format,
   rebuild twice, run synthetic tests, and bind exact source.
5. Independently review the complete exact source, project, binaries, tests, and binding until
   `No findings`.
6. Independently review the exact source-qualification record, commit it unchanged as `S0R5`, push it,
   and verify clean shared state plus empty protected state.
7. Invoke `--diagnose` once with a fresh run ID; do not create a locator and do not retry after any
   marker path exists.
8. Record only complete or incomplete branch plus the fixed class when available.
9. Independently review the completion, commit it unchanged as `G0R5`, and push it.
10. Return to a new plan; do not correct source, repeat private reads, access the current tree, publish
    a candidate, or start A2 or A3 under A0R5 authority.
