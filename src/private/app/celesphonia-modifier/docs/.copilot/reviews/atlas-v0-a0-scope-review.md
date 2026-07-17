# Atlas V0 A0 Scope Review

**Status:** Awaiting project-leader decision

**Decision authority:** Project leader

**Governing contract:** `../plans/atlas-v0-a0-research-contract.md`

## 1. Decision requested

Approve or reject the finite Atlas V0 research boundary. This review does not ask the project
leader to inspect hundreds of files individually or verify implementation details. It asks
whether the selected corpus, exclusions, privacy controls, quality bar, and narrowing authority
are appropriate for the preliminary comprehensive survey.

Approval completes A0 and permits A1 to begin under its separately persisted plan and acceptance
criteria. Approval does not qualify the preservation snapshot, authorize decoding before A3,
accept any semantic claim, or authorize writes to original user data.

## 2. Save corpus

The frozen discovery denominator has 22 directory entries:

| Decision | Entries                 | Count |
| -------- | ----------------------- | ----: |
| Include  | Slot saves 1-7 and 9-20 |    19 |
| Include  | `global.rpgsave`        |     1 |
| Include  | `config.rpgsave`        |     1 |
| Exclude  | `steam_autocloud.vdf`   |     1 |

Slot 8 is absent and is not treated as a missing input. Sparse slot numbering is valid.

The preservation snapshot is `save-snapshot-20260717T210224Z`. It contains 21 read-only save
copies and excludes Steam cloud metadata. Its qualification remains
`preservation-unqualified`; A2 must perform the formal Windows identity, link, reparse, and copy
qualification checks.

**Decision point:** Is this frozen 21-save set the correct baseline for the first comprehensive
survey?

## 3. Installed-definition corpus

The finite definition candidate denominator has 544 files: 496 included and 48 excluded.

| Group                       | Decision | Count | Rationale                                                       |
| --------------------------- | -------- | ----: | --------------------------------------------------------------- |
| Package and web entry files | Include  |     3 | Identify runtime, package, and script-loading context           |
| `www\data\*.json`           | Include  |   327 | Covers databases, maps, and plugin-defined JSON inputs          |
| Top-level engine scripts    | Include  |     8 | Covers RPG Maker bootstrap, serialization, and storage behavior |
| Plugin scripts              | Include  |   157 | Covers game-specific saved fields and interpretation            |
| `lz-string.js`              | Include  |     1 | Provides the save compression compatibility oracle              |
| Other JavaScript libraries  | Exclude  |     5 | Rendering, media, and performance behavior                      |
| Auxiliary data probes       | Exclude  |    43 | Unreferenced asset notes and Visual Studio state                |

All six JavaScript libraries are loaded by `index.html`. Static dependency review found that the
five excluded libraries are used for rendering, video, or performance monitoring rather than
save encoding or semantics.

Dependency closure over all 165 included engine and plugin scripts found only JSON inputs rooted
under `www\data`, which is already fully included. No included script names a CSV, TXT, XML,
YAML, or YML input. The auxiliary probes are 41 text files adjacent to image assets and two
Visual Studio state files; none is referenced by included runtime code.

If later evidence links an excluded file to save encoding or semantics, the file must be reopened
through an explicit scope revision. It cannot be silently ignored.

Installed binaries, media, fonts, images, and other asset classes are outside the 544-candidate
definition universe under the frozen extension and root rules. The survey is comprehensive for
the approved save-semantic candidate universe, not for every byte in the installation.

**Decision point:** Is this 496-file inclusion set, with 48 explicit exclusions, broad enough for
the preliminary comprehensive survey?

## 4. Fingerprint evidence

Private compatibility fingerprints will cover:

- Steam App ID and observed build metadata;
- `Game.exe`;
- both package files;
- `www\data\System.json`;
- `www\js\plugins.js`;
- every included plugin script;
- the save-path relocation definition; and
- Atlas tool, schema, redaction-policy, and configuration digests.

Full hashes, paths, file identities, and plugin parameters remain private. Repository-safe
records use only survey and fingerprint aliases.

Fingerprint evidence identifies the exact researched environment. It does not establish that an
edit operation is safe or authorize writes.

**Decision point:** Is this fingerprint scope sufficient to identify the baseline and detect
later compatibility drift?

## 5. Privacy and Agent boundary

Private material remains under the Git-ignored
`src\private\app\celesphonia-modifier\.private\atlas-v0\` workspace. It includes exact paths,
private manifests, hashes, immutable save copies, and later decoded or evidence artifacts.

Private-derived canonical records must pass schema and redaction validation before entering
repository history. Agent envelopes must pass schema validation before Agent use, remain private,
and never enter Git. Human-authored safe summaries, such as this brief, are reviewed separately.
Raw or decoded saves, save values, private paths, hashes, installed source text, Steam metadata,
and uncontrolled prompts or transcripts are prohibited.

Agents may receive only a schema-validated envelope containing strict aliases, typed locator
segments, structural shapes, numeric source coordinates, enumerated relations, and evidence
tags. The schema does not provide free-form fields for paths, source excerpts, hashes, or save
values.

**Decision point:** Is this privacy boundary acceptable for the project?

## 6. Completeness and narrowing authority

The baseline quality bar is zero opaque structural gaps. Token, graph, and scanner-visitation
censuses must later reconcile independently. Unsupported, unreadable, excluded, or opaque
in-scope content blocks Atlas V0 unless the project leader explicitly approves a narrower scope.

No Agent agreement, heuristic, correlation, or source reference can silently promote a semantic
claim to accepted truth. Operation authority remains separate and requires exact operation-level
evidence.

**Decision point:** Should zero gaps remain the default, with the project leader retaining sole
authority to approve later narrowing?

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
- `../schemas/atlas-v0/preservation-snapshot-manifest.schema.json`

Private evidence:

- `survey-000001\intake\corpus-intake-manifest.json`
- `survey-000001\intake\private-artifact-inventory.json`
- `survey-000001\intake\private-provenance.json`
- the completed preservation snapshot and its manifest

The private intake has 21 included saves, 496 included definitions, 48 explicit exclusions, and
no unsupported, unreadable, scope-narrowed, or unresolved candidate status.

## 9. Recommendation

Approve A0. The denominator is finite, exclusions are explicit and reversible, private evidence
is isolated, Agent egress is structurally constrained, and later scope narrowing remains a human
decision.
