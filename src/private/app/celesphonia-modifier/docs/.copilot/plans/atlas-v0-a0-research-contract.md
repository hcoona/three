# Atlas V0 A0 Research Contract

**Status:** Scope approved; completion controlled by the independent release gate

**Decision:** Project leader approved the A0 scope on July 17, 2026

**Governing plan:** `atlas-v0-execution-plan.md`, Increment A0

> **Proposed A2 partial supersession**
> When the exact `atlas-v0-a2-intake-safety-plan.md` plan-review record is verified, that plan
> supersedes only this contract's forward-looking references to an A2 identity harness and
> preservation-snapshot requalification. All A0 scope, privacy, lifecycle, and reopening rules
> remain normative.

**Execution boundary:** Read-only discovery and documentation, except for the single
project-leader-authorized preservation snapshot in section 6.1; no decoding

## 1. Purpose

This contract freezes the finite Atlas V0 research scope, privacy rules, accounting units,
Agent-egress boundary, private artifact lifecycle, and handoff procedure before C# scaffolding
or private intake begins.

The exact discovery, definition, preservation, and provenance manifests are private. This
repository records only rules, counts, aliases, and safe summaries.

## 2. Starting point

- Persisted plan commit: `1a715130af30e1aafa9af41b9add4c555a399a3a`.
- Game baseline: Magical Girl Celesphonia v1.05, public Steam application ID `1786790`, public
  `buildid` `13624401`, database `versionId` `2444532`.
- Discovery root: the previously confirmed installed game root.
- Active save rule: the enabled relocation plugin selects `<install>\save`.
- Secondary save-root rule: inspect the standard `<install>\www\save` location and terminally
  classify every entry even when it is inactive.
- Discovery is read-only and does not compute or publish content hashes.

## 3. Finite live-save discovery manifest

The July 17, 2026 read-only discovery found 23 entries across two candidate save roots:

| Root                 | Class                 | Count | Decision |
| -------------------- | --------------------- | ----: | -------- |
| `<install>\save`     | Existing slot saves   |    19 | Include  |
| `<install>\save`     | `global.rpgsave`      |     1 | Include  |
| `<install>\save`     | `config.rpgsave`      |     1 | Include  |
| `<install>\save`     | `steam_autocloud.vdf` |     1 | Exclude  |
| `<install>\www\save` | `steam_autocloud.vdf` |     1 | Exclude  |
| Both roots           | Unexpected entries    |     0 | None     |

Present slots are 1 through 7 and 9 through 20. Slot 8 is absent. Sparse numbering is valid and
does not create an expected input.

The exact private manifest records both save roots and contains one entry for every encountered
directory entry. Each entry has one terminal intake decision:

- `include-save`;
- `exclude-steam-autocloud`;
- `exclude-nonsave`;
- `unsupported`;
- `unreadable`; or
- `scope-narrowed`.

The 21 included save inputs are the baseline denominator. The web-root save directory contains no
save input and is inactive for this baseline, but its excluded Steam metadata remains accounted
for. Any later difference in either save root creates a new discovery manifest and requires
confirmation before copy.

## 4. Finite installed-definition manifest

The RPG Maker MV deployment review contains 580 terminally classified definition candidates: 496
included and 84 excluded.

| Group                       | Selection rule                                                                             | Decision | Count | Purpose                                                        |
| --------------------------- | ------------------------------------------------------------------------------------------ | -------- | ----: | -------------------------------------------------------------- |
| Root package                | `<install>\package.json`                                                                   | Include  |     1 | Runtime and package identity                                   |
| Web package                 | `<install>\www\package.json`                                                               | Include  |     1 | Game entry identity                                            |
| Web entry                   | `<install>\www\index.html`                                                                 | Include  |     1 | Script-loading context                                         |
| Game data                   | `<install>\www\data\*.json`                                                                | Include  |   327 | Database, map, and plugin definitions                          |
| Engine scripts              | `<install>\www\js\*.js`                                                                    | Include  |     8 | RPG Maker and game bootstrap behavior                          |
| Plugin scripts              | `<install>\www\js\plugins\*.js`                                                            | Include  |   157 | Plugin-defined save semantics                                  |
| Codec reference             | `<install>\www\js\libs\lz-string.js`                                                       | Include  |     1 | Save compression oracle                                        |
| Non-semantic runtime libs   | Other `<install>\www\js\libs\*.js`                                                         | Exclude  |     5 | Rendering, media, and performance libraries                    |
| Auxiliary definition probes | `www\**\*.{json,csv,txt,xml,yaml,yml,xlsx}` minus every file selected by a preceding group | Exclude  |    44 | Asset notes, IDE state, and an unreferenced authoring workbook |
| Detached DLC probe          | `<install>\Celesphonia Cosplay DLC 2\**\*`                                                 | Exclude  |    35 | Unreferenced HTML and media gallery files                      |

All six library scripts are loaded by `index.html`. Static references confirm that the five
excluded libraries provide rendering, media, or performance behavior rather than save encoding
or semantics. An observed semantic dependency from an included source reopens an excluded
library through explicit scope review.

The RPG Maker MV deployment closure also established:

- both package manifests use only their expected HTML entry point and declare no injected,
  `node-main`, package dependency, or development dependency script;
- all 14 `index.html` script references exist and have an explicit include or exclude decision;
- `www\data` is flat and contains 327 runtime JSON files plus one unreferenced XLSX authoring
  workbook whose corresponding runtime JSON has a later timestamp;
- `plugins.js` has no configured plugin file missing, while all seven unlisted plugin files remain
  conservatively included;
- there are no plugin subdirectories, other JavaScript locations, or deployed `node_modules`;
- literal external data loads resolve only under `www\data`;
- the only non-built-in module-style reference is a rendering dependency inside an unlisted
  filter file; and
- the detached DLC folder contains only HTML and image files, with no runtime reference to its
  folder or entry point found.

No included script names a CSV, TXT, XML, YAML, YML, or XLSX runtime input. The 44 auxiliary
probes consist of 41 unreferenced text files adjacent to image assets, two unreferenced Visual
Studio state files, and the unreferenced XLSX authoring workbook.

The exact private definition manifest records all 580 included and excluded candidates. Standard
deployed binaries, runtime packages, CSS, fonts, audio, movies, and ordinary image assets remain
outside the definition candidate universe. New files, new external-load evidence, or changed
selection rules create a new manifest revision.

## 5. Fingerprint evidence scope

Private fingerprint evidence will use:

- the public Steam application ID and public `buildid`;
- `Game.exe`;
- every one of the 496 included definition files; and
- the Atlas tool, schema, redaction-policy, and configuration digests.

Full hashes, source paths, file identities, and plugin parameters remain in private provenance.
Committed Atlas records use only safe survey and fingerprint aliases.

The public application ID and public `buildid` identify the game build, not the player. Raw Steam
manifest contents, personal SteamID values, account IDs, cloud-account identifiers, profile
paths, and equivalent account metadata are unnecessary, must not be retained, and must never
enter repository records or Agent envelopes.

## 6. Private workspace

Use this stable repository-local but Git-ignored root:

```text
src\private\app\celesphonia-modifier\.private\atlas-v0\
```

The tracked `.private\.gitignore` excludes every private descendant. This location is convenient
for cross-session handoff but is not repository-safe: files under it must never be staged,
committed, attached to issues, or supplied to Agents outside the approved redacted envelope.

The survey directory layout is:

```text
<survey-alias>\
  intake\
    corpus-intake-manifest.json
    private-artifact-inventory.json
    private-provenance.json
  copies\
    saves\
    definitions\
  decoded\
  evidence\
  agent-envelopes\
  validation\
  cleanup\
```

Every manifest, copy, decoded output, evidence payload, Agent envelope, and provenance record in
this workspace is private. Repository-safe canonical outputs are reviewed here before being
copied into Git.

### 6.1 Preservation snapshot created before A2

One preservation snapshot was permitted before the A2 C# intake harness, solely to freeze the
then-current save state before later gameplay changed it. This was the only pre-A2 copy exception
and required both a committed and pushed version of this contract and explicit project-leader
direction.

The snapshot:

- requires the matching game process to be absent;
- copies only current `*.rpgsave` files;
- excludes `steam_autocloud.vdf`;
- records the complete source-directory entry set before and after capture;
- records private source length, last-write time, and SHA-256 before and after each copy;
- fails if the directory entry set or any included source changes during capture;
- verifies copied bytes against the source hash;
- marks copied save files read-only;
- captures into a unique timestamped `.incomplete` directory;
- writes a schema-valid private completion manifest only after every check passes;
- atomically renames the completed directory to its stable final name;
- removes only the newly created incomplete directory after any failure; and
- records qualification as `preservation-unqualified`.

The preservation snapshot cannot satisfy A2, cannot enter the Atlas corpus automatically, and
cannot establish writable compatibility. It remains permanently `preservation-unqualified`. A2
creates new qualified read-only snapshots under its separately approved trust profile and never
promotes or requalifies this preservation snapshot.

The private completion manifest conforms to
`../schemas/atlas-v0/preservation-snapshot-manifest.schema.json`. The final directory name plus that
manifest is the completion marker; an `.incomplete` directory is never a usable snapshot.

## 7. Private artifact lifecycle

Every private artifact inventory entry records:

- survey-local alias;
- artifact class;
- purpose;
- custodian role;
- source and derived lineage aliases;
- last-use milestone;
- expiry condition;
- planned disposition;
- current status; and
- verification method.

An artifact alias identifies one custody object: preserved bytes, a directory snapshot, or one
revision-managed logical record at a canonical private path. A revision-managed record retains its
alias when its manifest revision changes; separately retained historical bytes receive a new alias.

`lineageAliases` lists only direct inventoried inputs or predecessors from which the entry was
derived. Direction is derived artifact to source or successor revision to predecessor revision.
The list is empty when no inventoried predecessor exists. Self-reference, dependent references,
and cycles are prohibited.

The lifecycle classes are:

| Class                                 | Last use                                  | Disposition                                                                                    |
| ------------------------------------- | ----------------------------------------- | ---------------------------------------------------------------------------------------------- |
| Live discovery metadata               | A2 copy verification                      | Retain private manifest; never copy content into Git                                           |
| Qualified read-only save snapshots    | A8 private repeatability review           | Delete during A8 final cleanup, before release review                                          |
| Included definition snapshots         | A6 source-correlation review              | Delete during A8 final cleanup, before release review                                          |
| Decoded save data                     | A6 private evidence and A8 privacy review | Delete during A8 final cleanup, before release review                                          |
| Private correlation evidence          | A8 evidence audit                         | Delete during A8 final cleanup unless a new approved policy retains it                         |
| Agent envelopes                       | A8 Agent-egress audit                     | Delete during A8 final cleanup, before release review                                          |
| Private provenance and cleanup record | A8 final cleanup                          | Retain through the Atlas snapshot appeal period, whose duration is defined before A8 execution |

No cleanup occurs merely because an earlier increment completes. A8 performs one final reviewed
cleanup after every private-evidence-dependent check and before the A8 release candidate is
committed for independent review. The cleanup attestation is part of that candidate, so A8
acceptance never depends on a post-acceptance deletion.

## 8. Deny-by-default redaction policy

Policy ID: `atlas-redaction/v1`.

### 8.1 Locator segments

Only these segment classes may enter a committed raw locator:

- document-role tokens defined by the schema;
- numeric array indexes;
- JsonEx markers `@`, `@c`, `@a`, and `@r`;
- keys in the versioned schema-safe allowlist; and
- survey-local aliases for every key not present in that allowlist.

The schema-safe key allowlist is `atlas-schema-key-allowlist/v1`. It contains zero keys. Therefore,
no source key may enter a committed locator or Agent envelope literally under v1; every key uses a
`schema-key-NNNNNN` or `dynamic-key-NNNNNN` survey-local alias.

A future non-empty allowlist requires a persisted contract revision, explicit project-leader
approval, a corresponding closed schema representation, and positive and negative conformance
vectors. Absence of an allowlist entry always means aliasing, never an operator judgment that a key
looks safe.

Unknown or dynamic keys never pass through literally. They receive an alias such as
`dynamic-key-000001`. Alias assignment is deterministic within the survey and uses a private
mapping. It does not publish a hash of the source key.

### 8.2 Values and source material

Committed outputs may contain:

- types and structural shapes;
- counts and bounded traits;
- safe aliases;
- evidence tags;
- relative safe source coordinates; and
- project-authored summaries.

Committed outputs may not contain:

- save scalar or string values;
- user-authored map or object keys;
- decoded documents;
- private paths or hashes;
- copied game text or source excerpts;
- personal SteamID values, account IDs, cloud-account identifiers, or other account metadata;
- narrative state; or
- prompt or model transcripts containing prohibited material.

Every emitted field and locator segment must pass structural validation. A content search is a
secondary defense, not the redaction proof.

## 9. Structural accounting units

Three independently produced censuses must reconcile.

### 9.1 Token census

An independent JSON token pass counts:

- object containers;
- array containers;
- object properties;
- array elements;
- scalar values; and
- JsonEx marker-property occurrences.

### 9.2 Graph census

Graph materialization counts:

- object, array, and scalar occurrence nodes;
- property edges;
- array-element edges;
- identity definitions;
- reference edges;
- unresolved references; and
- rejected malformed structures.

### 9.3 Scanner census

The scanner counts:

- locator-level observations by node kind;
- identity observations;
- reference observations;
- variation records; and
- explicit gaps.

Canonical structural coverage does not aggregate multiple locators. Aggregation belongs only to
generated views. A test-only independent traversal and skipped-subtree fault injection must
prove that an omitted property, element, identity, or reference causes reconciliation failure.

The acceptable opaque-gap count for the confirmed baseline is zero.

## 10. Agent egress contract

Agents receive only a schema-validated envelope containing:

- safe survey and fingerprint aliases;
- redacted observation references;
- typed locator patterns;
- structural shapes;
- safe source coordinates;
- deterministic extracted source facts;
- evidence tags; and
- the requested bounded review task.

Agents do not receive raw or decoded saves, values, private paths, private hashes, raw installed
source, copied game text, or unrestricted private-workspace access.

Logs retain only envelope alias, schema version, Agent identity, model identity, status, and
non-sensitive size or timing metadata. The annotation population and maximum pass count are
frozen before Agent execution.

Operational and private-derived Agent envelopes remain private and never enter Git. Hand-authored
synthetic Agent-envelope conformance vectors are not operational envelopes, contain no
private-derived data, and may be committed only under the test-data policy below.

## 11. Test-data policy

Repository tests may use:

- hand-authored synthetic compressed and JsonEx fixtures;
- synthetic malformed and boundary fixtures;
- reference codec vectors that contain no game or user data;
- generated structural graphs; and
- redacted canonical-record fixtures; and
- hand-authored synthetic Agent-envelope conformance vectors.

Repository tests may not use:

- original or decoded user saves;
- copied game databases or scripts;
- real save values or names;
- private fingerprints or hashes; or
- generated artifacts that cannot be independently classified as safe.

## 12. Human confirmation record

The project leader approved:

1. the 21-input save denominator, both inspected save roots, and two excluded
   `steam_autocloud.vdf` entries;
2. the 496 included definition files, 84 explicit exclusions, and frozen selection rules;
3. the fingerprint evidence scope;
4. the repository-local, fully Git-ignored private workspace;
5. the redaction and Agent-egress policies;
6. zero opaque structural gaps for the baseline; and
7. that later narrowing requires another explicit decision.

This approval satisfies A0's required human confirmation and approves research scope only. A0
completes, and A1 becomes eligible to begin, only after the independent release gate reviews an
exact committed candidate and its repository-safe record is persisted. Approval does not authorize
copying until the separately approved A2 intake checks exist, except for the historical
preservation-only process in section 6.1 under separate explicit project-leader direction. It does
not authorize decoding, semantic claims, or writes.

## 13. A0 release handoff and reopening

After the independent release gate passes, another contributor continues from completed A0 by:

1. checking out the shared branch containing this contract and
   `../reviews/atlas-v0-a0-release-gate.md`;
2. reading `project-operating-model.md`, `save-semantic-atlas-plan.md`, and
   `atlas-v0-execution-plan.md`;
3. verifying the persisted plan commit and clean worktree;
4. locating the approved `atlas-intake/v2` revision 3 manifest and preservation snapshot under the
   tracked project's ignored `.private\atlas-v0` workspace;
5. treating the preservation snapshot as permanently `preservation-unqualified`; and
6. beginning A1 only from its separately committed and pushed execution plan.

The contributor verifies the release record's candidate commit, tree, first-parent relationship,
and changed-path restriction before relying on the A0 completion claim.

Reopen A0 if the installed game, either candidate save root, corpus denominator, redaction policy,
privacy boundary, schema, or approval record changes. A reopened A0 records a new manifest
revision, obtains any required project-leader confirmation, and passes the independent increment
release gate again before returning to `done`.

Do not rely on conversation history or session task state as the only handoff.
