# Atlas V0 A0 No-Candidate Stage Diagnosis

**Lifecycle:** Proposed active subordinate; plan-only before verified shared `R0R4`

**Status:** Diagnostic implementation and private reads blocked

**Increment:** A0R4 - No-Candidate Stage Diagnosis

**Decision owner:** Project leader

**Decision:** Run one read-only, fixed-class replay of the A0R3 in-memory pipeline to record one current
outcome and identify the first boundary only on a controlled refusal, without publishing a candidate or
diagnosing a private cause.

**Base G0R3:** `16fb700497b401823b4393a9280558f430871e92`

**A0R3 source qualification:** `884834cf64f0749b6840423c5d03dbd012e51a66`

**Normative governing sources:**

- `project-operating-model.md`;
- the corpus, privacy, threat-model, metadata, selection, alias, and bounded-codec sections imported
  from `atlas-v0-a0-current-corpus-refresh.md`; and
- project and documentation `AGENTS.md`.

**Historical evidence and technical provenance:**

- `atlas-v0-a0-approved-manifest-corpus-refresh.md`;
- `../reviews/atlas-v0-a0-approved-manifest-corpus-refresh-source-qualification.md`; and
- `../reviews/atlas-v0-a0-approved-manifest-corpus-refresh-completion.md`.

Sections 1 through 9 of this plan establish every A0R4 authority and retained technical contract. The
historical A0R3 plan supplies source provenance but grants no current authority.

**Planned plan-review record:**
`../reviews/atlas-v0-a0-no-candidate-stage-diagnosis-plan-review.md`

**Planned source-qualification record:**
`../reviews/atlas-v0-a0-no-candidate-stage-diagnosis-source-qualification.md`

**Planned completion record:**
`../reviews/atlas-v0-a0-no-candidate-stage-diagnosis-completion.md`

## 1. Problem, claim, and limits

A0R3 completed at verified shared `G0R3` on its result-neutral no-candidate branch. Its final marker
consumed that census authority. A0R3 cannot be retried, diagnosed under its old authority, corrected
from private details, or treated as A2 or A3 authority.

The next critical path still requires a qualified A2 snapshot before private A3 acceptance. Starting
A3 against only synthetic inputs would not satisfy its corpus acceptance criteria and would not unblock
the product. A bounded A0R4 diagnosis is therefore the smallest useful next increment.

A0R4 claims only one fixed replay outcome from one new read-only observation. A controlled refusal
identifies the first reviewed outer gate or in-memory pipeline boundary that does not complete. An
internal refusal identifies no boundary, `diagnostic-ready` identifies completion of every reviewed
boundary, and an incomplete attempt identifies no result class. A0R4 does not claim:

- the private cause inside that boundary;
- that the current filesystem equals the moment observed by A0R3;
- that A0R3 would now succeed or fail at the same boundary;
- that a candidate is valid, approved, or publishable;
- that the approved manifest or runtime corpus should change; or
- that A2, A3, decoding, semantic scanning, or any original-data write is authorized.

When a fixed result class exists, it may scope later repository-safe source analysis or a separately
planned authority correction. An incomplete attempt permits only branch-safe, non-causal replanning.
No outcome selects or authorizes a correction by itself.

## 2. Authority and input boundaries

### 2.1 Historical authority

The utility derives exactly these two fixed historical paths without enumerating the protected
workspace or reading another historical artifact:

```text
request
  <repository-root>\src\private\app\celesphonia-modifier\.private\atlas-v0\
    survey-000001\intake\requests\discover.json
approved manifest
  <repository-root>\src\private\app\celesphonia-modifier\.private\atlas-v0\
    survey-000001\intake\corpus-intake-manifest.json
```

The approved private revision-3 manifest remains the sole corpus-specific authority. The request is
valid bounded JSON with one top-level object and contributes only this four-field baseline-byte anchor:

```text
schemaVersion = atlas-intake-discovery-request/v1
expectedBaselineSha256 = <one lowercase SHA-256 digest>
expectedSteamAppId = 1786790
expectedBuildId = 13624401
```

Each consequential field occurs exactly once with its stated JSON type; the two IDs are JSON integers.
Every other request field remains inert regardless of absence, JSON type, or value. Unknown fields are
also inert. A0R4 must not restore the deleted A0R2 or released-request execution bindings.

The exact approved-manifest bytes must hash to `expectedBaselineSha256`, strictly deserialize through
the released Atlas manifest contract, and equal the deterministic canonical reserialization. The
manifest must satisfy exactly:

```text
schemaVersion = atlas-intake/v2
surveyAlias = survey-000001
manifestRevision = 3
validation.method = manual-a0
confirmation.status = approved
confirmation.confirmedByRole = project-leader
confirmation.decisionReference =
  commit:3610d5e2a69073672bda665eed25a545a141c06b
```

Every optional reason code is either absent or nonempty lowercase ASCII letters, digits, and hyphens.
Every save entry must equal filename classification for role, slot number, and decision. Every
definition entry must equal the group and decision selected by the manifest's ordered first matching
definition rule. All other complete manifest validation remains supplied by the active released
`AtlasIntakeContracts` implementation and the normative policy sections imported above.

### 2.2 Runtime locator

A new protected `root-locators.json` is materialized beside the A0R4 project after exact source
qualification. It is new A0R4 runtime input rather than copied A0R3 state. Its bounded strict JSON
object has each required field exactly once, no optional or additional fields, and exactly:

```text
schema = atlas-a0r4-root-locators/v1
surveyAlias = survey-000001
definitionRoot = <absolute current installation root>
```

The locator is not corpus authority, project-leader approval, or evidence that the tree is unchanged.
The utility canonicalizes `definitionRoot` as one absolute ordinary DOS path on a ready fixed local
drive, rejects device or reparse traversal, and derives exactly:

```text
definition root
  <definition-root>
deployment-root-save
  <definition-root>\save
web-root-save
  <definition-root>\www\save
game executable
  <definition-root>\Game.exe
```

All four locators must exist with the expected file or directory type and satisfy
`trusted-local-filesystem/v1` metadata safety before enumeration. The two derived save roles must
exactly match the manifest roles and take aliases only from that manifest. The runtime locator supplies
no alias or corpus-policy value.

### 2.3 Private-read boundary

Before its durable diagnostic marker, the utility may inspect only:

- exact CLI syntax;
- repository and workspace metadata;
- Git authority and topology;
- exact source bindings and runtime assembly identity; and
- the empty protected A0R4 state directory.

It must not read or probe the historical request, approved manifest, runtime-locator document, current
game tree, A0R3 marker, A0R3 state, or any other private artifact before the marker.

After the marker, it may read only the historical request, approved manifest, fixed runtime locator,
and metadata required by the reviewed in-memory A0R3 pipeline. It never opens save, definition,
executable, or installed-file content.

## 3. Scope and exclusions

In scope:

- persist and independently review this plan before implementation;
- derive one fresh protected C# utility from exact qualified A0R3 project and source bytes;
- retain the A0R3 authority, path, metadata, census, codec, and synthetic-test behavior needed for
  read-only replay;
- map the existing historical, locator, and five `PipelineBoundary` locations to fixed result classes;
- publish one durable attempt marker before any private read;
- publish at most one strict fixed-class diagnostic receipt;
- suppress candidate staging and publication unconditionally;
- qualify exact source and assemblies before private access;
- execute at most one diagnostic attempt; and
- publish one result-safe completion record.

Out of scope:

- retrying or resuming A0R3;
- reading A0R3 runtime state or candidate paths;
- exposing a private path, filename, count, hash, entry, difference, manifest value, exception, or
  predicate;
- parsing exception messages or creating per-throw identifiers;
- modifying released Atlas production source, CLI, schemas, packages, or tracked tests;
- publishing, reviewing, deciding, finalizing, or approving a corpus candidate;
- changing approved root roles, rules, first-match order, decisions, classification, redaction,
  lifecycle, privacy, or threat model;
- A2 discovery, confirmation, copy, cleanup, or A3 parsing;
- source-content reads, telemetry, dumps, logs, network access, or Agent access to private inputs;
- hostile-local defense or simultaneous-filesystem-snapshot claims; and
- any original-data write.

## 4. Protected workspace and source derivation

Only after verified shared `R0R4`, create a new protected Git-ignored A0R4 workspace containing exactly:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R4.csproj
Program.cs
state/
```

The project and source initially match the exact A0R3 qualified technical inputs before A0R4 edits:

```text
project
  75b2e6ddbfdabdf8103bfc39c70eb4ff9b21f89d1b386f723d631d0ff67b764b
Program.cs
  9ac6f4292cd52376b25cb7d5330a31aa5428b51391c12e8cf14e12d7c400097d
```

The new `state` directory begins empty. Do not copy A0R3 build output, source bindings, root locator,
attempt marker, candidate paths, or any A0R1/A0R2/A0R3 runtime artifact.

The implementation must remove, not bypass:

- the A0R3 `--census` mode;
- candidate staging and final publication;
- candidate-success output;
- A0R3 marker and state-file names;
- A0R3 Git/source authority contracts; and
- any compatibility path that could resume or inspect A0R3 state.

Retain only reusable authority parsing, safe-path checks, metadata traversal, stable aliases, bounded
codec replay, fixed output, one-shot publication, source binding, and synthetic-test capabilities
required by A0R4.

## 5. CLI, state, and fixed outputs

The utility has exactly two noninteractive modes:

```text
--test
  --repository-root <repository-root>
  --workspace-root <a0r4-workspace-root>
  --run-id <run-id>

--diagnose
  --repository-root <repository-root>
  --workspace-root <a0r4-workspace-root>
  --run-id <run-id>
```

Every invocation requires one fresh, never-reused 32-character lowercase hexadecimal run ID. Unknown,
missing, duplicate, or unexpected arguments refuse without private reads.

The utility derives only:

```text
<workspace-root>\state\a0r4-diagnostic-attempt.json
<workspace-root>\state\a0r4-diagnostic-receipt.json
```

The attempt marker is canonical JSON with exactly:

```text
schema = atlas-a0r4-diagnostic-attempt/v1
toolRevision = atlas-a0r4/1
attemptId
sourceBindingsSha256
s0r4
```

It is create-new, written directly to its final path, flushed, strictly reloaded, and durable before
any private read. A complete, partial, or zero-byte final marker consumes A0R4 diagnostic authority.
Any later invocation that observes the marker returns only `diagnostic-refused`; it never returns a
retry-shaped preflight outcome.

The receipt is canonical JSON with exactly:

```text
schema = atlas-a0r4-diagnostic-receipt/v1
toolRevision = atlas-a0r4/1
attemptId
sourceBindingsSha256
s0r4
resultClass
```

It is create-new at its final path, flushed, strictly reloaded, and written only after one fixed class
is selected. A complete receipt is authoritative for the result class. A missing, partial, malformed,
or inconsistent receipt leaves only the consumed diagnostic-incomplete branch and never authorizes a
retry or inferred result.

Every mode writes exactly one fixed stdout line, keeps stderr empty, and returns:

| Outcome                             | Stdout                           | Exit |
| ----------------------------------- | -------------------------------- | ---: |
| Synthetic tests pass                | `test-passed`                    |    0 |
| Synthetic tests fail                | `test-failed`                    |    2 |
| Diagnostic preflight refuses        | `diagnostic-preflight-refused`   |    2 |
| Historical authority refuses        | `historical-authority-refused`   |    2 |
| Runtime locator refuses             | `runtime-locator-refused`        |    2 |
| Save metadata boundary refuses      | `save-metadata-refused`          |    2 |
| Definition metadata refuses         | `definition-metadata-refused`    |    2 |
| Stability boundary refuses          | `stability-refused`              |    2 |
| Candidate construction refuses      | `candidate-construction-refused` |    2 |
| Candidate replay refuses            | `candidate-replay-refused`       |    2 |
| Unexpected diagnostic failure       | `diagnostic-internal-refused`    |    2 |
| In-memory replay completes          | `diagnostic-ready`               |    0 |
| Marker consumed or receipt unusable | `diagnostic-refused`             |    2 |
| Unknown mode or arguments           | `operation-refused`              |    2 |

Unexpected exceptions map only to `diagnostic-internal-refused`. No dynamic exception, enum, path,
field, count, hash, partial result, or private value reaches either output stream.

## 6. Diagnostic pipeline

After the marker is durable, the utility executes these ordered boundaries:

1. load and validate the four-field historical anchor and complete approved manifest;
2. load and validate the fixed protected runtime locator and derived locators;
3. capture the first complete directory-entry identity snapshot;
4. classify both save roots non-recursively;
5. traverse definitions using ordered file-only first-match rules;
6. capture the second identity snapshot and require exact stability;
7. construct one pending candidate in memory; and
8. serialize, strictly reload, and deterministically replay the in-memory candidate.

The first two steps map to `historical-authority-refused` and `runtime-locator-refused`. The exact
existing A0R3 `PipelineBoundary` values map mechanically:

| Existing boundary       | Result class                     |
| ----------------------- | -------------------------------- |
| `SaveMetadata`          | `save-metadata-refused`          |
| `DefinitionMetadata`    | `definition-metadata-refused`    |
| `Stability`             | `stability-refused`              |
| `CandidateConstruction` | `candidate-construction-refused` |
| `CandidateReplay`       | `candidate-replay-refused`       |

The utility maps by typed boundary, never by exception text. Completion of all boundaries selects
`diagnostic-ready`.

Candidate bytes exist only in memory until discarded. The utility does not invoke A0R3 candidate
publication, create a staging path, create a candidate path, expose a candidate digest, or retain
decoded or structural content.

## 7. Terminal branches and interpretation

A0R4 has exactly two terminal private branches:

- **complete fixed-class diagnosis:** marker and one complete strict receipt exist; or
- **diagnostic incomplete:** marker exists without one complete strict receipt.

Both consume all A0R4 private-read authority. Neither authorizes another diagnostic, A0R3 census,
candidate publication, source correction, authority correction, A2, A3, or original-data write.

The result-safe completion may state only:

- complete or incomplete branch;
- one fixed result class when a complete receipt exists; and
- repository-safe source identity.

It must not state or infer a private cause. A future plan may use a fixed controlled-refusal boundary
to scope repository-safe source analysis. An internal, ready, or incomplete result permits only
non-causal replanning from its declared class or branch. Any private reread, changed authority, or
correction requires a new persisted and independently reviewed increment.

## 8. Synthetic validation and source review

Before private execution, the exact utility must pass:

- formatting;
- warning-free Release build;
- two consecutive complete Release Rebuilds with byte-stable qualified outputs;
- the complete synthetic suite;
- exact project, source, utility assembly, linked Atlas assembly, and source-binding hashing; and
- independent full-source review with TP/FP adjudication until `No findings`.

Synthetic tests must prove:

- exact CLI and fixed-output bytes, exit codes, and empty stderr;
- zero private content, metadata, or enumeration access before the durable marker;
- complete, partial, and zero-byte marker consumption with no retry-shaped output;
- exact marker and receipt schema, source binding, Git binding, and run-ID binding;
- clean shared `S0R4`, exact commit topology and changed path, one strict source-authority block, exact
  binding digest, current source and assembly hashes, and loaded runtime assembly identity;
- refusal before marker publication for every missing, duplicate, malformed, stale, substituted, or
  mismatched source-authority or source-binding condition;
- refusal before marker publication when the configured upstream is absent, behind, ahead, or not
  exactly `S0R4`;
- every fixed class and first-boundary mapping without message parsing;
- unexpected exceptions map only to `diagnostic-internal-refused`;
- diagnostic-ready suppresses all candidate and staging publication;
- receipt publication occurs only after classification;
- complete receipt reload and deterministic canonical equality;
- missing, partial, malformed, or substituted receipt never produces an authoritative class;
- no second invocation after any consumed marker;
- strict four-field historical anchor behavior and complete approved-manifest validation;
- exact fixed runtime-locator derivation and safety;
- save classification, file-only definition traversal, first-match behavior, alias continuity, and
  before-and-after stability;
- bounded candidate construction, serialization, strict reload, and replay only in memory;
- zero source-content reads;
- zero access to A0R3 state and every other historical runtime artifact; and
- zero candidate, staging, decision, A2, or A3 operation.

The source reviewer receives the complete source, project, exact binaries, tests, and source binding.
The reviewer receives no runtime locator, historical private input, current tree, A0R3 state, receipt,
or result.

## 9. Source-qualification gate

After deterministic builds and synthetic validation pass, but before source review, create one
immutable canonical single-line `source-bindings.json` beside the project and outside `state`. It has
schema
`atlas-a0r4-source-bindings/v1`, tool revision `atlas-a0r4/1`, no extra fields, and exactly:

```text
schema
toolRevision
r0r4
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
Hcoona.CelesphoniaModifier.Atlas.A0R4.csproj
Program.cs
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.Tests.dll
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.dll
```

The result-safe source-qualification record contains exact `P0R4` and `R0R4`, initial A0R3 source
derivation, final source and assembly hashes, binding hash, validation outcomes, complete TP/FP
dispositions, proof that state remains empty, and only the next action `diagnose-once`.

It contains one canonical authority object between:

```text
<!-- atlas-a0r4-source-authority:start -->
<!-- atlas-a0r4-source-authority:end -->
```

The object has exactly:

```text
schema = atlas-a0r4-source-authority/v1
r0r4
sourceBindingsSha256
projectSha256
programSha256
utilityAssemblySha256
atlasAssemblySha256
```

Before marker publication, `--diagnose` must strictly verify all of:

- `HEAD` and its configured upstream are both the exact `S0R4` commit, with clean tracked worktree;
- `S0R4` is the direct child of exact `R0R4` and changes only the source-qualification record path;
- the record contains exactly one authority block with canonical JSON and no unknown or duplicate
  fields;
- the authority block binds exact `R0R4` and the SHA-256 digest of the complete current
  `source-bindings.json` bytes;
- the binding document is canonical, has exactly the declared schema and fields, and binds exact
  `R0R4`;
- current project, `Program.cs`, utility assembly, and linked Atlas assembly bytes match every bound
  digest; and
- the currently loaded utility and Atlas assemblies resolve to and match the bound assembly files.

Any failure remains a preflight-only `diagnostic-preflight-refused` with no marker and no private read.
The utility must not trust a working-tree review record or extract authority from conversation,
artifact presence, filenames, or unverified text.

The exact staged record receives independent review until `No findings`, is committed unchanged as
`S0R4`, and is pushed and verified before the locator is materialized or any private read occurs.

## 10. Git candidates

Plan candidate `P0R4` is the direct child of exact `G0R3` and changes exactly:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a0-approved-manifest-corpus-refresh.md
    atlas-v0-a0-current-corpus-recovery.md
    atlas-v0-a0-current-corpus-refresh.md
    atlas-v0-a0-no-candidate-stage-diagnosis.md
    atlas-v0-a2-intake-safety-plan.md
```

Plan-review `R0R4` is the direct child of the final reviewed plan-line tip and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-no-candidate-stage-diagnosis-plan-review.md
```

Source qualification `S0R4` is the direct child of `R0R4` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-no-candidate-stage-diagnosis-source-qualification.md
```

Completion `G0R4` is the direct child of `S0R4` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-no-candidate-stage-diagnosis-completion.md
```

Every candidate is independently reviewed as an exact staged blob and committed unchanged.

## 11. Acceptance criteria

A0R4 completes only when:

1. exact plan and record-only `R0R4` receive independent `No findings`, are committed, pushed, and
   verified;
2. the fresh protected workspace starts only from exact A0R3 project/source and empty state;
3. census and candidate-publication machinery is deleted rather than bypassed;
4. fixed classes map only the two outer gates and five existing typed pipeline boundaries;
5. exact source passes section 8 and exact `S0R4` is independently reviewed, committed, pushed, and
   verified;
6. exactly one consuming marker is durable before any private read;
7. the run produces one complete strict fixed-class receipt or the diagnostic-incomplete branch;
8. no retry occurs after any marker bytes exist;
9. no candidate, staging artifact, decision, A2 operation, A3 operation, or source-content read occurs;
10. no private detail or causal inference reaches Git, process output, or any subagent;
11. exact result-safe `G0R4` receives independent `No findings` and becomes the verified clean shared
    tip; and
12. continuation returns to a separately persisted plan scoped by the fixed class when one exists, or
    by the incomplete branch alone without causal inference.

## 12. Stop conditions

Stop before implementation unless exact clean shared `R0R4` is verified.

Stop before marker publication unless exact clean shared `S0R4`, source bindings, repository,
workspace, CLI, and empty-state preconditions pass without private reads.

A preflight-only refusal with no marker bytes may be corrected and reinvoked with a fresh run ID. This
is not a private diagnostic retry. After any marker bytes exist, do not retry.

Stop and return to planning if:

- a result requires dynamic output or message parsing;
- the current A0R3 boundaries cannot isolate the first refusing layer;
- private detail would be needed to choose or explain a class;
- source content would need to be opened;
- the approved manifest, locator shape, policy, or threat model must change;
- candidate bytes would need to be persisted;
- source must be corrected after the consuming diagnostic; or
- any independent finding remains unresolved.

## 13. Ordered resume procedure

1. Review this exact plan candidate holistically with planning-drift and TP/FP adjudication until
   `No findings`.
2. Commit and push the exact plan candidate, then add and independently review only the plan-review
   record as `R0R4`.
3. Under exact clean shared `R0R4`, create the fresh protected A0R4 workspace and verify exact initial
   A0R3 source hashes plus empty state.
4. Implement only sections 2 through 9; format, rebuild twice, run the synthetic suite, bind exact
   source, and complete independent full-source review until `No findings`.
5. Independently review the exact source-qualification record, commit it unchanged as `S0R4`, push it,
   and verify clean shared state plus empty protected state.
6. Materialize the new protected runtime locator, invoke `--diagnose` once with a fresh run ID, and do
   not retry after any marker bytes exist.
7. Record only complete or incomplete branch plus the fixed result class when available.
8. Independently review the result-safe completion, commit it unchanged as `G0R4`, and push it.
9. Return to a new plan; do not correct source, repeat private reads, publish a candidate, or start A2
   or A3 under A0R4 authority.
