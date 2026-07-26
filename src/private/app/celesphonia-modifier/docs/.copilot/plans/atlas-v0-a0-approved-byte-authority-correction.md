# Atlas V0 A0 Approved-Byte Authority Correction

**Lifecycle:** Proposed active subordinate; plan-only before verified shared `R0R6`

**Status:** Correction implementation and private reads blocked

**Increment:** A0R6 - Approved-Byte Authority Correction

**Decision owner:** Project leader

**Decision:** Remove current-serializer canonical reserialization equality only from consumption of the
immutable approved historical manifest; retain exact raw-byte digest binding, the strict released
contract, approval provenance, and every semantic policy; prohibit manifest rewriting, historical
serializer reconstruction, and compatibility fallback.

**Base G0R5:** `cd6ee62e8fe0b744bd8111959e21842e2de39a45`

**Normative governing sources:**

- `project-operating-model.md`;
- the corpus and privacy sections imported from `atlas-v0-a0-current-corpus-refresh.md`;
- the `trusted-local-filesystem/v1` profile defined by section 2 of
  `atlas-v0-a2-intake-safety-plan.md`; and
- project and documentation `AGENTS.md`.

**Historical evidence and technical provenance:**

- `atlas-v0-a0-approved-manifest-corpus-refresh.md`;
- `atlas-v0-a0-historical-authority-diagnosis.md`;
- `../reviews/atlas-v0-a0-historical-authority-diagnosis-source-qualification.md`; and
- `../reviews/atlas-v0-a0-historical-authority-diagnosis-completion.md`.

This plan establishes every A0R6 authority and retained technical contract. A0R5 supplies exact source
provenance and one fixed contract-group result but grants no current execution or correction authority.

**Planned plan-review record:**
`../reviews/atlas-v0-a0-approved-byte-authority-correction-plan-review.md`

**Planned source-qualification record:**
`../reviews/atlas-v0-a0-approved-byte-authority-correction-source-qualification.md`

**Planned completion record:**
`../reviews/atlas-v0-a0-approved-byte-authority-correction-completion.md`

## 1. Problem, adjudication, and minimum shape

A0R5 completed at verified shared `G0R5` with
`historical-manifest-canonical-refused`. Its marker consumed all A0R5 authority. The class identifies
only non-completion of the reviewed `ManifestCanonical` group and discloses no private cause, value,
difference, or individual predicate.

Repository-safe ideal-first and adversarial adjudications independently reached the same authority
model, and the project leader approved it:

| Concern                    | Authority                                                                |
| -------------------------- | ------------------------------------------------------------------------ |
| Exact identity/integrity   | SHA-256 of the original approved raw bytes                               |
| Authorization/provenance   | Exact approval envelope and original A0 decision reference               |
| Parseability and schema    | One strict released versioned `atlas-intake/v2` reader                   |
| Corpus and semantic policy | Complete manifest consistency, reason, save, and definition validations  |
| Producer normal form       | Canonical serialization for newly generated artifacts, not historical ID |

Once exact raw bytes are digest-bound and approved, equality with
`Serialize(Parse(approvedBytes))` adds no independent identity or authorization property. It instead
lets evolution of the current producer serializer revoke approval of unchanged historical bytes.
Rewriting the manifest would change its digest and invalidate the existing anchor and approval.

This conclusion does not infer why the bytes differ, whether an old serializer could reproduce them,
or whether any later policy group completes. Those facts remain unknown and are unnecessary.

A0R6 is therefore the minimum correction:

- partially supersede only the A0R3 historical-consumer canonical-equality requirement;
- preserve the original manifest bytes, digest anchor, approval, strict contract, and semantic policy;
- read the manifest once and use the same returned raw-byte buffer for digest and parsed policy checks;
- delete the A0R5 canonical boundary, result class, and serializer coupling rather than bypass them;
- keep generated marker, receipt, binding, authority, and any future candidate canonical;
- execute at most one new read-only replay of the corrected historical authority pipeline;
- create no runtime locator and access no current game tree; and
- return every branch to separately authorized planning.

No outcome claims that A0R3 would now produce a candidate, that later policy groups complete, that a
current corpus is approved, or that A2, A3, production change, or original-data writes are authorized.

## 2. Exact authority correction

For the immutable approved revision-3 manifest only, the authoritative object is the exact original
raw-byte sequence identified by the four-field request anchor and original approval provenance.

A0R6 must:

1. read the manifest exactly once after its marker is durable;
2. use the released `AtlasIntakeContracts.ReadManifestAsync` result, which returns both the strictly
   validated document and the exact bytes read;
3. compute the anchor digest from that returned byte buffer;
4. run envelope, reason-code, save, and definition policy against the document parsed from that same
   read; and
5. neither call `SerializeManifest` nor compare serialized bytes while accepting the historical input.

The correction removes only historical-input normal-form equality. It does not weaken:

- safe ordinary-file checks;
- bounded strict JSON and the released `atlas-intake/v2` contract;
- exact request schema, lowercase digest shape, Steam app ID, or build ID;
- digest equality against exact original manifest bytes;
- alias, revision, validation method, confirmation, role, or decision reference;
- manifest consistency or reason-code grammar;
- save filename role, slot, and decision reclassification;
- definition rule grammar, order, first match, group, or decision; or
- canonical output rules for newly produced Atlas documents and A0R6 control artifacts.

A digest mismatch, strict-reader refusal, invalid envelope, invalid policy, or changed raw byte remains
a refusal. There is no known-digest exception, alternate reader, dual representation, normalized
digest, historical serializer, fallback, migration, rewrite, or copied approval.

If a new canonical manifest is ever desired, it is a new manifest identity requiring a new digest
anchor and fresh approval. It is not remediation of the existing approved bytes.

## 3. Exact historical inputs and corrected pipeline

The utility derives exactly the same two fixed paths as A0R5 without enumerating the protected
workspace or reading another historical artifact:

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

The request remains a bounded top-level JSON object. Exactly these four typed, unique fields are
consequential:

```text
schemaVersion = atlas-intake-discovery-request/v1
expectedBaselineSha256 = <one lowercase SHA-256 digest>
expectedSteamAppId = 1786790
expectedBuildId = 13624401
```

Every other request member remains inert regardless of absence, JSON type, or value.

After the durable marker, the utility executes exactly these ordered typed boundaries:

| Boundary                   | Exact work                                                                       |
| -------------------------- | -------------------------------------------------------------------------------- |
| `InputAccess`              | Derive fixed paths and require both safe ordinary files.                         |
| `RequestDocument`          | Read request bytes and parse the bounded four-field document.                    |
| `ManifestDocument`         | Read once through the strict released contract; retain document and exact bytes. |
| `RequestValues`            | Validate request schema, digest shape, Steam app ID, and build ID.               |
| `AnchorBinding`            | Compare the request digest with the exact returned manifest-byte buffer.         |
| `ManifestEnvelope`         | Validate schema, alias, revision, validation, and confirmation fields.           |
| `ManifestReasonCode`       | Validate every optional reason code.                                             |
| `ManifestSavePolicy`       | Reclassify every save entry and require exact role, slot, and decision.          |
| `ManifestDefinitionPolicy` | Apply ordered first-match rules and require exact group and decision.            |

`ManifestDocument` combines A0R5's contract and raw-byte acquisition because one released read must
supply both the parsed object and exact identity bytes. This is deliberate deletion of read-to-read
substitution, not loss of either check.

The first typed boundary exception maps mechanically to its fixed result class without inspecting an
exception message. Completion of all nine groups selects `historical-authority-ready`.

## 4. Scope and exclusions

In scope:

- persist and independently review the approved-byte authority correction before implementation;
- derive one fresh protected C# utility from exact qualified A0R5 project and source bytes;
- retain only A0R5 CLI, fixed-output, source-binding, Git, process-capture, marker, receipt, strict JSON,
  corrected historical parsing, manifest policy, and synthetic-test machinery needed by A0R6;
- delete the canonical historical boundary, result, tests, and serializer coupling;
- replace the double manifest access with one released load returning document and exact bytes;
- publish one durable A0R6 marker before either historical file is inspected;
- publish at most one strict fixed-class receipt;
- qualify exact source and assemblies before private access;
- execute at most one diagnostic attempt; and
- publish one result-safe completion record.

Out of scope:

- retrying or resuming A0R5;
- reading A0R5 marker, receipt, binding, or other runtime state during A0R6 execution;
- inspecting or inferring the private canonical difference;
- rewriting, normalizing, migrating, replacing, or newly approving the historical manifest;
- adding a historical serializer, compatibility fallback, known-digest exception, or alternate reader;
- changing released Atlas production source, CLI, schemas, packages, or tracked tests;
- reading a runtime locator, save root, definition root, executable, installed-file metadata, or source
  content;
- publishing or deciding a current-corpus candidate or refreshed manifest;
- A2 discovery, confirmation, copy, cleanup, A3 parsing, or WinUI work;
- telemetry, dumps, logs, network access, or Agent access to private inputs;
- hostile-local defenses beyond `trusted-local-filesystem/v1`; and
- any original-data write.

## 5. Protected workspace and source derivation

Only after verified shared `R0R6`, create a new protected Git-ignored A0R6 workspace containing exactly:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R6.csproj
Program.cs
state/
```

The project and source initially match the exact qualified final A0R5 technical inputs before A0R6
edits:

```text
project
  1ca7bef4b35025d2228f54d6521fe2d84466df27d2fcf1783545286154a91703
Program.cs
  9f8a812c131ee3c26a4cc6736571987687cbe698e10c2820ac4dac7f3b12becc
```

The new `state` begins empty. Do not copy A0R5 build output, source binding, marker, receipt, or any
runtime artifact.

The implementation must remove, not bypass:

- every A0R5 state filename and authority contract;
- `ManifestCanonical`, `HistoricalManifestCanonicalRefused`, and
  `historical-manifest-canonical-refused`;
- separate path-based manifest-reader and second raw-byte-read seams;
- every historical-input use of `SerializeManifest`;
- synthetic fixtures or helpers that exist only for those deleted behaviors; and
- all A0R5 run, source-binding, authority, and Git identities.

No runtime-locator, current-tree, candidate, or A0R4/A0R5 runtime-state machinery may be reintroduced.

## 6. CLI, state, and fixed outputs

The utility has exactly two noninteractive modes:

```text
--test
  --repository-root <repository-root>
  --workspace-root <a0r6-workspace-root>
  --run-id <run-id>

--diagnose
  --repository-root <repository-root>
  --workspace-root <a0r6-workspace-root>
  --run-id <run-id>
```

Every invocation requires one fresh, never-reused 32-character lowercase hexadecimal run ID. Unknown,
missing, duplicate, or extra arguments refuse before private reads.

The utility derives only:

```text
<workspace-root>\state\a0r6-approved-byte-attempt.json
<workspace-root>\state\a0r6-approved-byte-receipt.json
```

The marker is canonical JSON with exactly:

```text
schema = atlas-a0r6-approved-byte-attempt/v1
toolRevision = atlas-a0r6/1
attemptId
sourceBindingsSha256
s0r6
```

It is create-new at its final path, flushed, strictly reloaded, and durable before either historical
file is inspected. A complete, partial, or zero-byte marker consumes A0R6. Any later invocation that
observes the marker returns only `diagnostic-refused`.

The receipt is canonical JSON with exactly:

```text
schema = atlas-a0r6-approved-byte-receipt/v1
toolRevision = atlas-a0r6/1
attemptId
sourceBindingsSha256
s0r6
resultClass
```

It is create-new, flushed, strictly reloaded, and written only after one fixed class is selected. A
complete matching receipt is authoritative. A missing, partial, malformed, or inconsistent receipt
leaves only the consumed incomplete branch.

Every mode writes exactly one fixed stdout line, keeps stderr empty, and returns:

| Outcome                                    | Stdout                                    | Exit |
| ------------------------------------------ | ----------------------------------------- | ---: |
| Synthetic tests pass                       | `test-passed`                             |    0 |
| Synthetic tests fail                       | `test-failed`                             |    2 |
| Diagnostic preflight refuses               | `diagnostic-preflight-refused`            |    2 |
| Historical input safety refuses            | `historical-input-access-refused`         |    2 |
| Request document refuses                   | `historical-request-document-refused`     |    2 |
| Released manifest document refuses         | `historical-manifest-document-refused`    |    2 |
| Request public values refuse               | `historical-request-values-refused`       |    2 |
| Manifest-anchor binding refuses            | `historical-anchor-binding-refused`       |    2 |
| Manifest approval envelope refuses         | `historical-manifest-envelope-refused`    |    2 |
| Manifest reason-code policy refuses        | `historical-manifest-reason-code-refused` |    2 |
| Save-entry policy refuses                  | `historical-save-policy-refused`          |    2 |
| Definition-entry policy refuses            | `historical-definition-policy-refused`    |    2 |
| Every corrected historical group completes | `historical-authority-ready`              |    0 |
| Unexpected post-marker failure             | `diagnostic-internal-refused`             |    2 |
| Marker consumed or receipt unusable        | `diagnostic-refused`                      |    2 |
| Unknown mode or arguments                  | `operation-refused`                       |    2 |

No receipt-class token may escape without a matching complete receipt. Before marker, an unexpected
failure returns `diagnostic-preflight-refused` when the marker path is absent. After any marker path
exists, failure returns `diagnostic-refused` unless a complete matching internal-refusal receipt is
durable.

## 7. Terminal branches and interpretation

A0R6 has exactly two terminal private branches:

- **complete fixed-class result:** marker and one complete matching strict receipt exist; or
- **diagnostic incomplete:** marker exists without one complete matching strict receipt.

Both consume all A0R6 private-read authority. Neither authorizes retry, source or authority correction,
current-tree access, candidate work, A2, A3, or original-data writes.

The result-safe completion may state only:

- complete or incomplete branch;
- one fixed result class when a complete receipt exists; and
- repository-safe source identity.

A controlled refusal may scope later repository-safe adjudication to its named group.
`historical-authority-ready` means only that the corrected historical gate completed; it does not
authorize or predict census or candidate success. Ready, internal, or incomplete outcomes permit only
non-causal replanning. No result automatically selects a correction.

## 8. Synthetic validation and source review

Before private execution, the exact utility must pass:

- formatting;
- warning-free Release build;
- two consecutive complete Release Rebuilds with byte-stable qualified outputs;
- the complete synthetic suite;
- exact project, source, utility assembly, linked Atlas assembly, and source-binding hashing; and
- independent full-source review with TP/FP adjudication until `No findings`.

Synthetic tests must prove:

- exact CLI, literal fixed stdout bytes, exit codes, and empty stderr;
- zero historical content, metadata, or enumeration access before the durable marker;
- complete, partial, and zero-byte marker consumption without retry-shaped output;
- absent marker after failed creation remains preflight-only;
- exact literal marker, receipt, binding, and authority canonical vectors;
- no receipt-class output without one complete authoritative receipt;
- all nine literal typed boundaries and exact first-refusal mappings without message parsing;
- unexpected post-marker failure requires a matching internal-refusal receipt;
- strict fixed historical paths and zero protected-workspace enumeration;
- exact four-field request behavior with every other member inert;
- one manifest read supplies both strict released document and exact digest buffer;
- a synthetic non-normal-form but strict, digest-bound, approved, policy-valid manifest completes;
- digest mismatch, malformed strict JSON, invalid envelope, reason code, save policy, and definition
  policy each refuse in the correct group;
- historical acceptance never invokes `SerializeManifest`, rewrites bytes, or creates a normalized copy;
- generated control-document canonicalization remains exact and unchanged in purpose;
- exact per-channel access allowlists reject every extra read, metadata access, enumeration, or write;
- zero locator, current-tree, source-content, candidate, A2, A3, A0R4, or A0R5 runtime-state access;
- exact clean shared `S0R6`, direct-parent/path topology, committed record blob, binding digest, current
  source and assembly hashes, and loaded runtime assembly identity;
- configured upstream absent, behind, ahead, or unequal to exact `S0R6`;
- working-tree record substitution cannot influence committed authority;
- concurrent text and binary process capture under both stdout-first and stderr-first pressure; and
- drive-root and non-root containment preserve repository/workspace separation.

The source reviewer receives the complete source, project, exact binaries, synthetic suite, and final
source binding. The reviewer receives no historical input, A0R5 state, marker, receipt, or result.

## 9. Source-qualification gate

After deterministic builds and synthetic validation pass, but before final source review, create one
immutable canonical single-line `source-bindings.json` beside the project and outside `state`. It has
schema `atlas-a0r6-source-bindings/v1`, tool revision `atlas-a0r6/1`, no extra fields, and exactly:

```text
schema
toolRevision
r0r6
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
Hcoona.CelesphoniaModifier.Atlas.A0R6.csproj
Program.cs
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.Tests.dll
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.dll
```

The source-qualification record contains exact `P0R6` and `R0R6`, initial A0R5 source derivation, final
source and assembly hashes, binding hash, validation outcomes, complete TP/FP dispositions, proof that
state remains empty, and only next action `diagnose-once`.

It contains one canonical authority object between:

```text
<!-- atlas-a0r6-source-authority:start -->
<!-- atlas-a0r6-source-authority:end -->
```

The object has exactly:

```text
schema = atlas-a0r6-source-authority/v1
r0r6
sourceBindingsSha256
projectSha256
programSha256
utilityAssemblySha256
atlasAssemblySha256
```

Before marker publication, `--diagnose` must verify:

- `HEAD` and configured upstream both equal exact `S0R6`, with clean tracked worktree;
- `S0R6` is the direct child of exact `R0R6` and adds only the source-qualification record;
- source authority bytes are loaded binary-safely from the verified `S0R6` Git blob, never the
  working-tree record;
- the record has one exact three-line authority block with canonical JSON;
- the block binds exact `R0R6` and the complete current source-binding bytes;
- current project, source, utility assembly, and linked Atlas assembly match every digest; and
- loaded utility and Atlas assembly paths match the bound files.

Any failure remains preflight-only when no marker exists. The exact staged qualification record
receives independent `No findings`, is committed unchanged as `S0R6`, and is pushed and verified before
any historical input is read.

## 10. Git candidates

Plan candidate `P0R6` is the direct child of exact `G0R5` and changes exactly:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a0-approved-byte-authority-correction.md
    atlas-v0-a0-approved-manifest-corpus-refresh.md
    atlas-v0-a0-historical-authority-diagnosis.md
    atlas-v0-a2-intake-safety-plan.md
```

Plan-review `R0R6` is the direct child of the final reviewed plan-line tip and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-approved-byte-authority-correction-plan-review.md
```

Source qualification `S0R6` is the direct child of `R0R6` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-approved-byte-authority-correction-source-qualification.md
```

Completion `G0R6` is the direct child of `S0R6` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-approved-byte-authority-correction-completion.md
```

Every candidate is independently reviewed as an exact staged blob and committed unchanged.

## 11. Acceptance criteria

A0R6 completes only when:

1. exact plan and record-only `R0R6` receive independent `No findings`, are committed, pushed, and
   verified;
2. the protected workspace starts only from exact qualified A0R5 project/source and empty state;
3. the canonical historical boundary and double-read/serializer machinery are deleted, not bypassed;
4. one released manifest load supplies both strict document and exact digest bytes;
5. the nine typed boundaries preserve every retained identity, approval, and semantic policy;
6. exact source passes section 8 and exact `S0R6` is independently reviewed, committed, pushed, and
   verified;
7. exactly one consuming marker is durable before either historical file is inspected;
8. the run produces one complete matching fixed-class receipt or the incomplete branch;
9. no retry occurs after any marker path exists;
10. no manifest rewrite, private-difference inspection, runtime locator, current-tree metadata, source
    content, candidate, A2, or A3 operation occurs;
11. no private detail or causal inference reaches Git, process output, or a subagent;
12. exact result-safe `G0R6` receives independent `No findings` and becomes the clean shared tip; and
13. continuation returns to a separately persisted plan scoped only by the fixed class or incomplete
    branch.

## 12. Stop conditions

Stop before implementation unless exact clean shared `R0R6` is verified.

Stop before marker publication unless exact clean shared `S0R6`, source binding, repository, workspace,
CLI, and empty-state preconditions pass without private reads.

A preflight refusal with no marker path may be corrected and reinvoked with a fresh run ID. This is not
a private diagnostic retry. After any marker path exists, do not retry.

Stop and return to planning if:

- the approved bytes cannot be strictly read once and reused for both parsed semantics and digest;
- removal would weaken a retained schema, approval, or semantic policy;
- a result requires dynamic output, message parsing, or an individual private predicate;
- preserving retained semantics requires released production changes;
- a runtime locator, current-tree read, manifest rewrite, historical serializer, or fallback is needed;
- source must be corrected after the consuming diagnostic; or
- any independent finding remains unresolved.

## 13. Ordered resume procedure

1. Review this exact plan holistically with planning-drift and TP/FP adjudication until `No findings`.
2. Commit and push the exact plan, then add and independently review only the plan-review record as
   `R0R6`.
3. Under exact clean shared `R0R6`, create the fresh protected A0R6 workspace and verify exact initial
   A0R5 source hashes plus empty state.
4. Implement only sections 2 through 9; delete canonical/double-read machinery; format, rebuild twice,
   run synthetic tests, and bind exact source.
5. Independently review the complete exact source, project, binaries, tests, and binding until
   `No findings`.
6. Independently review the exact source-qualification record, commit it unchanged as `S0R6`, push it,
   and verify clean shared state plus empty protected state.
7. Invoke `--diagnose` once with a fresh run ID; do not create a locator and do not retry after any
   marker path exists.
8. Record only complete or incomplete branch plus the fixed class when available.
9. Independently review the completion, commit it unchanged as `G0R6`, and push it.
10. Return to a new plan; do not correct source, repeat private reads, access the current tree, publish
    a candidate, or start A2 or A3 under A0R6 authority.
