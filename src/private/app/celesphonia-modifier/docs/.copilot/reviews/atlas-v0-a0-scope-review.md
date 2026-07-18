# Atlas V0 A0 Scope Review

**Status:** Approved

> **Lifecycle: Partially superseded**
> The identity/link qualification wording in section 2 and the immutable-copy wording in section 5
> remain historical evidence of the A0 decision. When the exact
> `../plans/atlas-v0-a2-intake-safety-plan.md` plan-review record is verified, its
> `trusted-local-filesystem/v1` profile replaces those forward-looking requirements. Git history
> preserves this record's originally approved bytes; this banner records only later lifecycle.

**Decision:** Project leader approved A0 on July 17, 2026

**Decision authority:** Project leader

**Governing contract:** `../plans/atlas-v0-a0-research-contract.md`

**Approved manifest:** `atlas-intake/v2`, `survey-000001`, revision 3

**Decision reference:** `commit:3610d5e2a69073672bda665eed25a545a141c06b`

## 1. Decision recorded

The project leader approved the finite Atlas V0 research boundary after reviewing the corpus,
exclusions, fingerprint scope, privacy controls, quality bar, residual uncertainty, and narrowing
authority.

Approval satisfies A0's human-confirmation criterion. A0 completes and A1 becomes eligible only
after an exact committed candidate passes the independent release gate and its repository-safe
record is persisted. Approval does not qualify the preservation snapshot, authorize decoding
before A3, accept any semantic claim, or authorize writes to original user data.

## 2. Save corpus

The frozen discovery denominator has 23 entries across both RPG Maker candidate save roots:

| Root                   | Decision | Entries                 | Count |
| ---------------------- | -------- | ----------------------- | ----: |
| Deployment-root `save` | Include  | Slot saves 1-7 and 9-20 |    19 |
| Deployment-root `save` | Include  | `global.rpgsave`        |     1 |
| Deployment-root `save` | Include  | `config.rpgsave`        |     1 |
| Deployment-root `save` | Exclude  | `steam_autocloud.vdf`   |     1 |
| Standard `www\save`    | Exclude  | `steam_autocloud.vdf`   |     1 |

Slot 8 is absent and is not treated as a missing input. Sparse slot numbering is valid.

The preservation snapshot is `save-snapshot-20260717T210224Z`. It contains 21 read-only save
copies and excludes Steam cloud metadata. Its qualification remains
`preservation-unqualified`; A2 must perform the formal Windows identity, link, reparse, and copy
qualification checks.

The enabled relocation plugin selects the deployment-root `save` directory. The standard
`www\save` directory contains no save input, but it remains inspected so a future path or content
change cannot be missed.

**Approved outcome:** The frozen 21-save set is the baseline for the first comprehensive survey.

## 3. Installed-definition corpus

The RPG Maker MV definition candidate denominator has 580 files: 496 included and 84 excluded.

| Group                       | Decision | Count | Rationale                                                       |
| --------------------------- | -------- | ----: | --------------------------------------------------------------- |
| Package and web entry files | Include  |     3 | Identify runtime, package, and script-loading context           |
| `www\data\*.json`           | Include  |   327 | Covers databases, maps, and plugin-defined JSON inputs          |
| Top-level engine scripts    | Include  |     8 | Covers RPG Maker bootstrap, serialization, and storage behavior |
| Plugin scripts              | Include  |   157 | Covers game-specific saved fields and interpretation            |
| `lz-string.js`              | Include  |     1 | Provides the save compression compatibility oracle              |
| Other JavaScript libraries  | Exclude  |     5 | Rendering, media, and performance behavior                      |
| Auxiliary data probes       | Exclude  |    44 | Asset notes, IDE state, and an unreferenced authoring workbook  |
| Detached DLC probe          | Exclude  |    35 | Unreferenced HTML and image gallery files                       |

All six JavaScript libraries are loaded by `index.html`. Static dependency review found that the
five excluded libraries are used for rendering, video, or performance monitoring rather than
save encoding or semantics.

RPG Maker-specific closure found:

- both package manifests have no injected or dependency scripts;
- all 14 scripts named by `index.html` are classified;
- every configured plugin file exists;
- seven unlisted plugin files remain included conservatively;
- no plugin subdirectory, other JavaScript location, or `node_modules` exists;
- all runtime data loads resolve to the included `www\data` JSON files;
- the single XLSX file under `www\data` is an unreferenced authoring workbook whose runtime JSON
  has a later timestamp; and
- the detached DLC folder is an HTML and image gallery with no runtime reference to its folder or
  entry point found.

If later evidence links an excluded file to save encoding or semantics, the file must be reopened
through an explicit scope revision. It cannot be silently ignored.

Installed binaries, runtime packages, CSS, fonts, audio, movies, and ordinary image assets are
outside the 580-candidate
definition universe under the frozen extension and root rules. The survey is comprehensive for
the approved save-semantic candidate universe, not for every byte in the installation.

**Approved outcome:** The 496-file inclusion set and 84 explicit exclusions define the preliminary
comprehensive survey.

## 4. Fingerprint evidence

Private compatibility fingerprints will cover:

- the public Steam application ID and public `buildid`;
- `Game.exe`;
- every one of the 496 included definition files; and
- Atlas tool, schema, redaction-policy, and configuration digests.

Full hashes, paths, file identities, and plugin parameters remain private. Repository-safe
records use only survey and fingerprint aliases.

The public application ID and public `buildid` identify the game build, not the player. Raw Steam
manifest contents, personal SteamID values, account IDs, cloud-account identifiers, and
equivalent profile metadata are neither needed nor retained.

Fingerprint evidence identifies the exact researched environment. It does not establish that an
edit operation is safe or authorize writes.

**Approved outcome:** The fingerprint scope is sufficient to identify the approved baseline and
detect later compatibility drift.

## 5. Privacy and Agent boundary

Private material remains under the Git-ignored
`src\private\app\celesphonia-modifier\.private\atlas-v0\` workspace. It includes exact paths,
private manifests, hashes, immutable save copies, and later decoded or evidence artifacts.

Private-derived canonical records must pass schema and redaction validation before entering
repository history. Operational and private-derived Agent envelopes must pass schema validation
before Agent use, remain private, and never enter Git. Hand-authored synthetic conformance vectors
may be committed under the contract's test-data policy because they contain no private-derived
data and are never used for Agent execution. Human-authored safe summaries, such as this brief,
are reviewed separately. Raw or decoded saves, save values, private paths, hashes, installed source
text, personal SteamID values, account identifiers, profile or cloud metadata, and uncontrolled
prompts or transcripts are prohibited. The approved public application ID and build identifier
remain allowed game identifiers.

Agents may receive only a schema-validated envelope containing strict aliases, typed locator
segments, structural shapes, numeric source coordinates, enumerated relations, and evidence
tags. The schema does not provide free-form fields for paths, source excerpts, hashes, or save
values.

**Approved outcome:** The privacy boundary is accepted for Atlas V0.

## 6. Completeness and narrowing authority

The baseline quality bar is zero opaque structural gaps. Token, graph, and scanner-visitation
censuses must later reconcile independently. Unsupported, unreadable, excluded, or opaque
in-scope content blocks Atlas V0 unless the project leader explicitly approves a narrower scope.

No Agent agreement, heuristic, correlation, or source reference can silently promote a semantic
claim to accepted truth. Operation authority remains separate and requires exact operation-level
evidence.

**Approved outcome:** Zero gaps remains the default. Only the project leader may approve later
narrowing through a persisted scope revision.

## 7. What remains uncertain after approval

A0 approval does not establish that:

- the C# codec or JsonEx graph reader works;
- every copied save can be decoded;
- a field's meaning or write authority is known;
- any edit operation is safe;
- the installed game will remain unchanged; or
- an excluded file can never become relevant.

Those uncertainties are addressed by later increments and their acceptance gates. Any
installation or directory change creates a new manifest revision rather than changing this
baseline silently.

## 8. Evidence available

Repository-safe evidence:

- `../plans/atlas-v0-a0-research-contract.md`
- `../plans/atlas-v0-execution-plan.md`
- `../schemas/atlas-v0/corpus-intake-manifest.schema.json`
- `../schemas/atlas-v0/private-artifact-inventory.schema.json`
- `../schemas/atlas-v0/agent-egress-envelope.schema.json`
- `../schemas/atlas-v0/test-data/agent-egress-envelope.valid.json`
- `../schemas/atlas-v0/test-data/agent-egress-envelope.invalid-*.json`
- `../schemas/atlas-v0/preservation-snapshot-manifest.schema.json`

Private evidence:

- `survey-000001\intake\corpus-intake-manifest.json`
- `survey-000001\intake\private-artifact-inventory.json`
- `survey-000001\intake\private-provenance.json`
- the completed preservation snapshot and its manifest

The private intake has 21 included saves, 496 included definitions, 84 explicit exclusions, and
no unsupported, unreadable, scope-narrowed, or unresolved candidate status.

## 9. Decision

The A0 scope is approved. The denominator is finite, exclusions are explicit and reversible,
private evidence is isolated, Agent egress is structurally constrained, and later scope narrowing
remains a human decision. Phase completion is controlled by the exact-candidate independent
release gate.
