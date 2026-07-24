# Atlas V0 A0 Approved-Manifest Corpus Refresh

**Lifecycle:** Proposed active subordinate; plan-only before verified shared `R0R3`

**Status:** Utility preparation, private reads, and census blocked

**Increment:** A0R3 - Approved-Manifest Corpus Refresh

**Decision owner:** Project leader

**Decision:** Treat the approved A0 manifest as the sole corpus authority, reduce the historical
request to a baseline-byte anchor carrier, take the current installation root only from one fresh
protected runtime-locator document, and permit one metadata-only census after exact source
qualification.

**Base G0R2:** `1f9fbcd369d893e8de88cfe195512936e4815f01`

**Original A0 human decision:** `3610d5e2a69073672bda665eed25a545a141c06b`

**Original A0 release gate:** `5681fccc8af78a8253e5d995f90825ecd387350d`

**Normative governing sources:**

- `project-operating-model.md`;
- `atlas-v0-a2-approved-manifest-authority-correction.md`;
- the corpus, privacy, threat-model, metadata, selection, alias, and pending-codec sections imported
  from `atlas-v0-a0-current-corpus-refresh.md`; and
- project and documentation `AGENTS.md`.

**Historical evidence and technical provenance:**

- `../reviews/atlas-v0-a0-release-gate.md`;
- `../reviews/atlas-v0-a0-current-corpus-refresh-completion.md`;
- `../reviews/atlas-v0-a0-current-corpus-recovery-completion.md`; and
- the exact A0R2 source qualification recorded at `S0R2`
  `2e780d3a1f48c701cef2bbb00fc6f8702010ca2b`.

**Planned plan-review record:**
`../reviews/atlas-v0-a0-approved-manifest-corpus-refresh-plan-review.md`

**Planned source-qualification record:**
`../reviews/atlas-v0-a0-approved-manifest-corpus-refresh-source-qualification.md`

**Planned completion record:**
`../reviews/atlas-v0-a0-approved-manifest-corpus-refresh-completion.md`

## 1. Authority correction and outcome

A0R2 reached `historical-input-gate-refused` because its first private boundary validated the entire
historical discovery request together with the approved manifest. That result identifies no failed
field and authorizes no inference about one.

Independent design review of the released contracts establishes a narrower fact: the historical
request combines two different concerns:

1. the exact SHA-256 anchor and public identity for the approved baseline; and
2. old A2 execution locations, output destinations, inventory lineage, revision progression, and live
   source locators.

Only the first concern is needed to identify the approved manifest. The second describes one old A2
run and must not become current-corpus authority.

The project leader approved a complete authority pivot:

- the approved manifest supplies corpus policy, old membership, group semantics, and stable aliases;
- the historical request supplies only the minimum exact baseline-byte anchor and public game
  identity;
- one explicit current installation root stored in the protected A0R3 runtime-locator document
  supplies live locations;
- the utility derives the two fixed save-root roles and executable locator from that root; and
- no old request path, output, inventory, or revision-progression value participates in census
  authority.

A0R3's only private execution outcome is one complete pending current-corpus candidate or a
result-neutral no-candidate closure. It does not approve or finalize a candidate and grants no A2
authority.

## 2. Authority and input layers

### 2.1 Approved manifest

The existing private `atlas-intake/v2` revision-3 manifest is the sole corpus-specific authority. The
released reader must validate its complete strict canonical bytes and require:

- survey alias `survey-000001`;
- validation method `manual-a0`;
- approved confirmation by role `project-leader`;
- decision reference `commit:3610d5e2a69073672bda665eed25a545a141c06b`;
- complete internally consistent save roots, save entries, definition groups, definition entries,
  counts, classifications, aliases, rule order, and decisions; and
- exact canonical serialization equality.

The utility also proves that the original A0 human-decision and release-gate commits are reachable
from current `HEAD`.

The old manifest remains authoritative for:

- the two save-root roles and root aliases;
- save classification policy;
- definition-group identifiers, selection rules, first-match order, and decisions;
- source aliases for every prior locator identity;
- removed-alias nonreuse;
- supported schema, terminal-status, privacy, and lifecycle vocabulary; and
- the fixed survey alias and original A0 approval provenance.

It is not authority for current membership or counts. Those are recomputed from the current runtime
root by one metadata-only census.

### 2.2 Minimal historical anchor

The utility derives only the canonical historical request and manifest file locations beneath the
existing protected A0 workspace. It does not enumerate that workspace or read any other historical
artifact.

After the consuming census marker exists, the utility parses the request as one bounded JSON object
solely to extract an anchor. It does not deserialize or validate the released discovery-request
contract. Exactly these fields remain consequential:

```text
schemaVersion
expectedBaselineSha256
expectedSteamAppId
expectedBuildId
```

Each must occur exactly once with the expected JSON type. They must identify the released
discovery-request schema, one lowercase SHA-256 digest, Steam application 1786790, and build 13624401.
The computed SHA-256 of the exact canonical manifest bytes must equal `expectedBaselineSha256`. The
manifest itself independently supplies and validates survey alias `survey-000001` and revision 3.

These request fields are explicitly non-authoritative and must not be compared, copied into new
artifacts, or used to derive a live or output path:

```text
projectRoot
workspaceRoot
baselineManifestPath
surveyAlias
expectedBaselineRevision
nextManifestRevision
manifestRevisionDirectory
saveRoots
definitionRoot
gameExecutablePath
sourceRootMapOutputPath
inventoryPath
expectedInventorySha256
inventoryBackupPath
copyPlanOutputPath
stateRevisionDirectory
```

They are ignored regardless of presence, absence, JSON type, or value. Other unknown members are
ignored as well. The parser still enforces valid bounded JSON, one top-level object, and unique
occurrence of each consequential field. A0R3 must delete A0R2 validation and production coupling for
all inert members rather than retain a compatibility fallback.

### 2.3 Fresh runtime locator

After verified shared `R0R3`, the operator materializes the current installation root selected for
this run in:

```text
<a0r3-workspace-root>\root-locators.json
```

The file is protected, Git-ignored runtime input, not corpus policy, project-leader approval, or
baseline identity evidence. Its complete strict shape is:

```text
schema = atlas-a0r3-root-locators/v1
surveyAlias = survey-000001
definitionRoot = <absolute current installation root>
```

It has no optional or additional fields. It remains outside runtime `state`, and neither its bytes nor
its digest enters Git or subagent input. The fixed path and strict schema preserve the selected runtime input for handoff without elevating it
to corpus authority.

After the consuming census marker exists, the utility reads this fixed document, canonicalizes its
single root as one absolute ordinary DOS path on a ready fixed local drive, rejects device or reparse
traversal, and derives exactly:

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

All four locators must exist with the expected file or directory type and pass the same
`trusted-local-filesystem/v1` metadata safety checks before enumeration. No source content is opened.

The two derived roles must match the manifest's two root roles exactly. The manifest supplies their
root aliases; the runtime input supplies only their current locations.

No runtime locator, path, digest, filename, count, difference, or corpus payload enters Git, subagent
input, or process output.

## 3. Scope and exclusions

In scope:

- persist and independently review this authority correction before implementation;
- derive a new protected A0R3 C# utility from the exact A0R2 project and source;
- delete obsolete historical-request authority, A0R2 diagnostic machinery, and A0R2 decision/census
  coupling;
- strictly load the approved manifest through its minimum private byte anchor;
- accept one fixed protected runtime-locator document and derive fixed live locators;
- perform one stable metadata-only census of the two save roots and definition root;
- preserve approved policy and every surviving locator alias;
- deterministically allocate aliases only for newly selected locator identities;
- atomically publish at most one strict private pending candidate;
- independently review and qualify exact source and assemblies before private access; and
- publish one result-safe completion record.

Out of scope:

- diagnosing which A0R2 historical-request predicate refused;
- using the A0R1 unauthorized execution result or any A0R2 private payload;
- reading historical inventory, state, backups, copies, evidence, decoded output, validation output,
  provenance, or Agent envelopes;
- reading save, definition, executable, or installed-file content;
- changing root roles, definition rules, first-match order, decisions, classification, redaction,
  lifecycle, privacy, or threat model;
- discovering an ambient installation or accepting more than one definition root;
- candidate approval, decline, finalization, or final-byte approval;
- modifying released Atlas production source, CLI, schemas, packages, or tracked tests;
- A2R14, any A2 operation, production change, or original-data write; and
- hostile-local defense, simultaneous-tree snapshot claims, or installer/package provenance.

## 4. Protected workspace and source derivation

Only after verified shared `R0R3`, create a new protected Git-ignored A0R3 workspace. Copy exactly:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R3.csproj
Program.cs
state/
```

The project and source bytes initially match the exact qualified A0R2 technical inputs:

```text
project
  b333fcdd9c72b0c0b31ab02f3c0b0444cb82b6635f0f3222ba526f327dad2548
Program.cs
  4da1ff87fd26a8437aaea691d4f96ba4f25db48fa13fe1ccd2fbfc9b3f1a24dc
```

`state` begins empty. Do not copy A0R2 bindings, build output, attempt markers, receipts, decisions,
candidates, or any A0R1/A0R2 runtime artifact. Build output may appear only after the initial allowlist
and hashes pass.

After the initial allowlist passes, the operator creates `root-locators.json` for the exact current
installation root selected for this run. This is new A0R3 runtime input, not a copied historical
artifact. Source bindings may be created later beside it; neither file belongs in `state`.

The implementation must remove, not bypass:

- `--diagnose-census` and its result classes;
- `--record-diagnostic-decision`;
- `D0R2` parsing and census-decision authority;
- diagnostic and decision markers, receipts, and state allowances;
- validation or live-path use of every inert request field listed in section 2.2; and
- compatibility paths that can restore old request authority.

Retain only reusable metadata enumeration, stable-alias, bounded-codec, strict JSON, safe-path,
one-shot publication, Git/source-binding, and synthetic-test capabilities needed by A0R3.

## 5. CLI, state, and fixed output

The utility has exactly two noninteractive modes:

```text
--test
  --repository-root <repository-root>
  --workspace-root <a0r3-workspace-root>
  --run-id <run-id>

--census
  --repository-root <repository-root>
  --workspace-root <a0r3-workspace-root>
  --run-id <run-id>
```

Every invocation requires one fresh, never-reused 32-character lowercase hexadecimal run ID. Unknown,
missing, duplicate, or unexpected arguments refuse without private reads.

The utility derives only:

```text
<workspace-root>\state\a0r3-census-attempt.json
<workspace-root>\state\a0r3-current-corpus-manifest-candidate.staging.json
<workspace-root>\state\a0r3-current-corpus-manifest-candidate.json
```

Before the census marker, preflight may inspect only CLI syntax, repository and workspace metadata,
Git authority, exact source bindings, and an empty protected state directory. It must not read the
historical request, manifest, root-locator document, runtime root, or current-tree metadata.

The census marker is canonical JSON with exactly:

```text
schema = atlas-a0r3-census-attempt/v1
toolRevision = atlas-a0r3/1
attemptId
sourceBindingsSha256
s0r3
```

It is create-new, written directly to its final path, flushed, strictly reloaded, and durable before
any private read. `attemptId` equals the invocation run ID; source and Git fields equal exact qualified
inputs. A complete or partial final marker consumes the attempt. No census retry is permitted after
the marker exists.

Every mode writes exactly one fixed stdout line, keeps stderr empty, and returns:

| Outcome                        | Stdout                     | Exit |
| ------------------------------ | -------------------------- | ---: |
| Synthetic tests pass           | `test-passed`              |    0 |
| Synthetic tests fail           | `test-failed`              |    2 |
| Census preflight refuses       | `census-preflight-refused` |    2 |
| Census publishes candidate     | `candidate-published`      |    0 |
| Marked census has no candidate | `census-refused`           |    2 |
| Unknown mode or arguments      | `operation-refused`        |    2 |

Unexpected exceptions map to the same applicable fixed refusal. No exception message, path, field,
count, hash, or partial result reaches output.

## 6. Metadata-only census

After the marker is durable, the utility executes this fixed pipeline:

1. load the minimum historical anchor and approved manifest;
2. load the fixed protected runtime-locator document and validate its root plus derived locators;
3. capture complete directory-entry identities before census;
4. enumerate and classify both save roots non-recursively;
5. traverse the definition root and apply the manifest's ordered file-only selection rules;
6. capture directory-entry identities again and require exact stability;
7. build one pending revision-3 manifest in memory;
8. serialize to the create-new staging path, flush, strictly reload, and require deterministic replay
   equality;
9. atomically move the validated staging file without overwrite to the final candidate path.

The successful same-directory move is the durable publication boundary. There is no post-move
operation required to establish success.

Save and definition selection, classification, traversal, counts, aliases, normalization, collision
handling, and terminal rules remain exactly those imported from A0R1 and corrected to released
file-only semantics by A0R1C1/A0R2.

Ordinary directories are traversal nodes, never definition candidates. A required device- or
reparse-backed traversal, unsupported or unreadable selected entry, ambiguous classification,
duplicate or case collision, unstable metadata, malformed authority input, or codec mismatch refuses
without a candidate.

The census never opens source content. It makes only per-entry metadata observations and no
simultaneous-filesystem-snapshot claim.

## 7. Candidate and terminal branches

On success, exactly one create-new private candidate exists:

```text
a0r3-current-corpus-manifest-candidate.json
```

The candidate:

- uses `atlas-intake/v2`, survey `survey-000001`, and revision 3;
- uses validation method `manual-a0`;
- has pending confirmation with no approver or decision reference;
- preserves all approved policy and every surviving source alias;
- omits absent prior locators and includes every current selected locator;
- allocates new aliases monotonically and deterministically without reuse; and
- contains only metadata-derived membership and counts.

A0R3 has two terminal private branches:

- **candidate published:** the marker and final candidate path exist; or
- **no candidate:** the marker exists without one complete strict candidate, whether because of
  controlled refusal, interruption, or an incomplete staging artifact.

Only a staging file that already passed strict reload and deterministic replay can be atomically moved
to the create-new final path. Final-path presence is therefore success evidence even if the process is
interrupted immediately after the move. A staging artifact is never a candidate. It may remain after
interruption as result-neutral evidence of the consumed attempt and is never resumed or promoted by a
later invocation.

Both consume A0R3 census authority. Neither authorizes a retry, candidate decision, final manifest,
A2 operation, or source correction based on private details.

The result-safe completion may state only the branch and repository-safe source identity. It must not
publish or infer private paths, filenames, counts, hashes, differences, entries, failed fields,
exception text, or corpus content.

If a candidate exists, a future separately persisted increment may define exact project-leader review,
approval or decline, decision-commit binding, deterministic finalization, and final-byte approval.
A0R3 does not pre-authorize that increment.

## 8. Synthetic validation and source review

Before private execution, the exact utility must pass:

- formatting;
- warning-free Release build;
- the complete synthetic suite;
- two consecutive complete Release Rebuilds with byte-stable qualified outputs;
- exact project, source, utility assembly, linked Atlas assembly, and binding-file hashing; and
- independent full-source review with TP/FP adjudication until `No findings`.

Synthetic tests must prove:

- the exact CLI and fixed-output contract;
- zero private reads before the census marker;
- direct-final marker durability and no retry after complete or partial marker publication;
- exact marker schema, source binding, Git binding, and run-ID binding;
- exact clean shared `S0R3` and exact source bindings before private access;
- reachability of the original A0 human decision and release gate;
- strict minimum-anchor parsing and exact manifest digest equality;
- mutation of every consequential anchor field refuses;
- absence, type mutation, and value mutation of every inert request field do not change accepted
  authority or runtime locators;
- no inert request field is read after parsing for type completeness or used by production logic;
- the fixed protected root-locator document is the only live-locator source;
- exact derivation and safety validation of both save roots and `Game.exe`;
- complete approved-manifest internal validation without public corpus reconstruction;
- save classification and file-only definition traversal;
- stable before-and-after metadata sets;
- preserved aliases, removed-alias nonreuse, and deterministic new allocation;
- duplicate, case-collision, unsupported, unreadable, device, reparse, and instability refusal;
- bounded candidate serialization, strict reload, and deterministic replay;
- create-new staging, same-directory atomic non-overwriting publication, final strict reload, and no
  staging or partial-file acceptance;
- zero source-content reads;
- zero access to all other historical A0/A2 artifacts; and
- zero use of A0R1 or A0R2 execution results.

The review examines the complete source, project, exact binaries, source bindings, tests, and deletion
of obsolete authority. Review does not receive private inputs or runtime state.

## 9. Source-qualification gate

After source review returns `No findings`, create one immutable canonical single-line
`source-bindings.json` beside the project file and outside `state`. It has schema
`atlas-a0r3-source-bindings/v1`, tool revision `atlas-a0r3/1`, no extra fields, and exactly this ordered
contract:

```text
schema
toolRevision
r0r3
projectRelativeName
projectSha256
programRelativeName
programSha256
utilityAssemblyRelativeName
utilityAssemblySha256
atlasAssemblyRelativeName
atlasAssemblySha256
```

Relative names are fixed exactly to:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R3.csproj
Program.cs
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.Tests.dll
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.dll
```

All hashes are lowercase SHA-256 and `r0r3` is exact. The document binds source identity only; it
contains no runtime locator or private corpus value.

The result-safe source-qualification record contains:

- exact `P0R3` and `R0R3`;
- verified A0R2 initial-source derivation;
- final project, source, utility assembly, linked Atlas assembly, and binding-file hashes;
- format, build, byte-stability, test, and source-review outcomes;
- reviewer provenance and every TP/FP disposition;
- proof that runtime state remains empty; and
- the exact permitted next action: one A0R3 census attempt.

It contains one unique, marker-adjacent, canonical single-line JSON authority block between the exact
literal delimiters:

```text
<!-- atlas-a0r3-source-authority:start -->
<!-- atlas-a0r3-source-authority:end -->
```

The object has exactly:

```text
schema = atlas-a0r3-source-authority/v1
r0r3
sourceBindingsSha256
projectSha256
programSha256
utilityAssemblySha256
atlasAssemblySha256
```

The utility strictly parses that tracked block and matches it to `source-bindings.json`, current files,
runtime assembly identity, Git topology, clean shared state, and empty protected state before marker
publication.

The exact staged record receives independent review until `No findings`, then is committed unchanged
and pushed as `S0R3`. File presence, local hashes, conversation, or source review alone grant no
private-read authority.

## 10. Git candidates

Plan candidate `P0R3` is the direct child of exact `G0R2` and changes exactly:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a0-current-corpus-refresh.md
    atlas-v0-a0-current-corpus-recovery.md
    atlas-v0-a0-approved-manifest-corpus-refresh.md
    atlas-v0-a2-intake-safety-plan.md
```

Plan-review `R0R3` is the direct child of the final reviewed plan-line tip and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-approved-manifest-corpus-refresh-plan-review.md
```

Source qualification `S0R3` is the direct child of `R0R3` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-approved-manifest-corpus-refresh-source-qualification.md
```

Completion `G0R3` is the direct child of `S0R3` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-approved-manifest-corpus-refresh-completion.md
```

Every candidate is independently reviewed as an exact staged blob and committed unchanged.

## 11. Acceptance criteria

A0R3 completes only when:

1. exact `P0R3` and record-only `R0R3` receive independent `No findings`, are committed, pushed, and
   verified;
2. the new protected workspace starts from only the exact qualified A0R2 project/source bytes and an
   empty state directory;
3. obsolete full-request, diagnostic, decision, and `D0R2` authority is deleted without a compatibility
   fallback;
4. the approved manifest is the sole corpus-specific authority and the historical request contributes
   only the four anchor fields in section 2.2;
5. one fixed protected root-locator document is the sole source of current locators;
6. exact source passes section 8 and exact `S0R3` receives independent `No findings`, is committed,
   pushed, and verified;
7. exactly one consuming census marker is durably published before any private read;
8. the marked census produces exactly one complete strict pending candidate or the result-neutral
   no-candidate branch;
9. no retry occurs after the marker;
10. no private path, filename, count, hash, entry, difference, field failure, content, exception text,
    or causal inference enters Git, subagent input, or process output;
11. no candidate decision, finalization, final approval, A2 operation, production change, or
    original-data write occurs; and
12. exact result-safe `G0R3` receives independent `No findings` and becomes the verified clean shared
    tip with the required parent, path set, and committed blob.

## 12. Stop conditions

Stop before implementation unless exact clean shared `R0R3` is verified.

Stop before marker publication unless exact clean shared `S0R3`, source bindings, CLI, repository,
workspace, and empty-state preconditions all pass without private reads.

A refusal before any marker bytes exist consumes no private attempt because preflight performs no
private read. Correct the preflight-only condition and reinvoke with a fresh run ID. This is not a
census retry.

After any complete or partial final census marker exists, do not retry. On no candidate, author only
result-safe completion. On candidate publication, preserve the exact candidate without reviewing,
deciding, or finalizing it under A0R3.

Any need to:

- use a historical request execution field;
- change the approved manifest's policy;
- accept another runtime-root shape;
- disclose a private failure detail;
- inspect source content;
- correct source after a consuming private attempt;
- repeat the census; or
- approve or finalize a candidate

returns to a separately persisted and independently reviewed plan.

## 13. Ordered resume procedure

1. Verify clean shared `G0R2`, stage only the five `P0R3` paths, run complete independent plan review
   with TP/FP adjudication until `No findings`, commit the exact candidate, and push it.
2. Author the record-only plan review against exact committed `P0R3`, independently review it until
   `No findings`, commit it unchanged as the direct child `R0R3`, and push it.
3. Under exact clean shared `R0R3`, create the fresh protected A0R3 workspace, verify its initial
   allowlist and A0R2 source hashes, have the operator materialize the fixed `root-locators.json` for
   the current installation root selected for this run, then implement only sections 2 through 9.
4. Format, rebuild, run the synthetic suite, prove byte stability, bind exact source and assemblies,
   and complete independent full-source review until `No findings`.
5. Author and independently review the exact source-qualification record, commit it unchanged as
   `S0R3`, push it, and verify clean shared state plus empty protected runtime state.
6. Invoke `--census` with fresh repository-root, workspace-root, and run-ID arguments. A preflight-only
   refusal may be corrected and reinvoked with a new run ID only while no marker bytes exist. The
   utility reads the fixed protected root-locator document only after its marker is durable. Do not
   retry after any final marker bytes exist.
7. Record only candidate-present or no-candidate status, author the result-safe completion, review it
   until `No findings`, commit it unchanged as `G0R3`, and push it.
8. If a candidate exists, return to planning for candidate review and decision. If none exists, return
   to planning without diagnosing or repeating the consumed census.
