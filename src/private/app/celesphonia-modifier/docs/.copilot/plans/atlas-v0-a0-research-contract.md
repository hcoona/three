# Atlas V0 A0 Research Contract

**Status:** Awaiting project-leader corpus confirmation

**Governing plan:** `atlas-v0-execution-plan.md`, Increment A0

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
- Game baseline: Magical Girl Celesphonia v1.05, Steam App ID `1786790`, observed Steam build
  `13624401`, database `versionId` `2444532`.
- Discovery root: the previously confirmed installed game root.
- Active save rule: `<install>\save`, not `<install>\www\save`.
- Discovery is read-only and does not compute or publish content hashes.

## 3. Finite live-save discovery manifest

The July 17, 2026 read-only discovery found:

| Class                 | Count | Decision |
| --------------------- | ----: | -------- |
| Existing slot saves   |    19 | Include  |
| `global.rpgsave`      |     1 | Include  |
| `config.rpgsave`      |     1 | Include  |
| `steam_autocloud.vdf` |     1 | Exclude  |
| Unexpected entries    |     0 | None     |

Present slots are 1 through 7 and 9 through 20. Slot 8 is absent. Sparse numbering is valid and
does not create an expected input.

The exact private manifest must contain one entry for every live directory entry. Each entry has
one terminal intake decision:

- `include-save`;
- `exclude-steam-autocloud`;
- `exclude-nonsave`;
- `unsupported`;
- `unreadable`; or
- `scope-narrowed`.

The 21 included save inputs are the baseline denominator. Any later directory difference creates
a new discovery manifest and requires confirmation before copy.

## 4. Finite installed-definition manifest

The preliminary definition scope contains 496 files:

| Group           | Selection rule                       | Count | Purpose                               |
| --------------- | ------------------------------------ | ----: | ------------------------------------- |
| Root package    | `<install>\package.json`             |     1 | Runtime and package identity          |
| Web package     | `<install>\www\package.json`         |     1 | Game entry identity                   |
| Web entry       | `<install>\www\index.html`           |     1 | Script-loading context                |
| Game data       | `<install>\www\data\*.json`          |   327 | Database and map definitions          |
| Engine scripts  | `<install>\www\js\*.js`              |     8 | RPG Maker and game bootstrap behavior |
| Plugin scripts  | `<install>\www\js\plugins\*.js`      |   157 | Plugin-defined save semantics         |
| Codec reference | `<install>\www\js\libs\lz-string.js` |     1 | Save compression oracle               |

The other five current `www\js\libs\*.js` files are excluded because they implement rendering,
media, or platform behavior rather than save encoding or semantics. An observed dependency from
an included source may reopen one file through explicit scope review.

The exact private definition manifest records every included and excluded file. The pattern set
is frozen for this survey. New files or changed selection rules create a new manifest revision.

## 5. Fingerprint evidence scope

Private fingerprint evidence will use:

- Steam App ID and build metadata;
- `Game.exe`;
- both package files;
- `www\data\System.json`;
- `www\js\plugins.js`;
- every included plugin script;
- the save-path relocation definition; and
- the Atlas tool, schema, redaction-policy, and configuration digests.

Full hashes, source paths, file identities, and plugin parameters remain in private provenance.
Committed Atlas records use only safe survey and fingerprint aliases.

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
    discovery-manifest.json
    definition-manifest.json
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

### 6.1 Pre-A2 preservation snapshot

One preservation snapshot may be created before the A2 C# identity harness exists, solely to
freeze the current save state before later gameplay changes it. This is the only pre-A2 copy
exception and requires both a committed and pushed version of this contract and explicit
project-leader direction to preserve the current saves.

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
- atomically renames the completed directory to its immutable final name;
- removes only the newly created incomplete directory after any failure; and
- records qualification as `preservation-unqualified`.

The preservation snapshot cannot satisfy A2, cannot enter the Atlas corpus automatically, and
cannot establish writable compatibility. A2 must revalidate it with Windows file identity and
link/reparse checks or create a new qualified immutable copy.

The private completion manifest conforms to
`schemas/atlas-v0/preservation-snapshot-manifest.schema.json`. The final directory name plus that
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

The lifecycle classes are:

| Class                                 | Last use                                  | Disposition                                                                    |
| ------------------------------------- | ----------------------------------------- | ------------------------------------------------------------------------------ |
| Live discovery metadata               | A2 copy verification                      | Retain private manifest; never copy content into Git                           |
| Immutable save copies                 | A8 private repeatability review           | Delete after A8 acceptance                                                     |
| Included definition copies            | A6 source-correlation review              | Delete after A8 acceptance                                                     |
| Decoded save data                     | A6 private evidence and A8 privacy review | Delete after A8 acceptance                                                     |
| Private correlation evidence          | A8 evidence audit                         | Delete after A8 unless selected focused research retains it under a new policy |
| Agent envelopes                       | A8 Agent-egress audit                     | Delete after A8 acceptance                                                     |
| Private provenance and cleanup record | A8 acceptance                             | Retain through the Atlas snapshot appeal period defined before A8              |

No cleanup occurs merely because an earlier increment completes. A8 performs one final reviewed
cleanup after every private-evidence-dependent check.

## 8. Deny-by-default redaction policy

Policy ID: `atlas-redaction/v1`.

### 8.1 Locator segments

Only these segment classes may enter a committed raw locator:

- document-role tokens defined by the schema;
- numeric array indexes;
- JsonEx markers `@`, `@c`, `@a`, and `@r`;
- keys in a versioned schema-safe allowlist; and
- survey-local aliases for every other key.

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
- account metadata;
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

## 11. Test-data policy

Repository tests may use:

- hand-authored synthetic compressed and JsonEx fixtures;
- synthetic malformed and boundary fixtures;
- reference codec vectors that contain no game or user data;
- generated structural graphs; and
- redacted canonical-record fixtures.

Repository tests may not use:

- original or decoded user saves;
- copied game databases or scripts;
- real save values or names;
- private fingerprints or hashes; or
- generated artifacts that cannot be independently classified as safe.

## 12. Human confirmation gate

Before A0 can complete and before A1 or A2 begins, the project leader must confirm:

1. the 21-input save denominator and exclusion of `steam_autocloud.vdf`;
2. the 496-file installed-definition selection rules;
3. the fingerprint evidence scope;
4. the repository-local, fully Git-ignored private workspace;
5. the redaction and Agent-egress policies;
6. zero opaque structural gaps for the baseline; and
7. that later narrowing requires another explicit decision.

The confirmation approves research scope only. It does not authorize copying until the A2
identity checks exist, except for the preservation-only process in section 6.1 under separate
explicit project-leader direction. It does not authorize decoding, semantic claims, or writes.

## 13. Resume procedure

Another contributor resumes A0 by:

1. checking out the shared branch containing this contract;
2. reading `project-operating-model.md`, `save-semantic-atlas-plan.md`, and
   `atlas-v0-execution-plan.md`;
3. verifying the persisted plan commit and clean worktree;
4. locating the exact private manifests and preservation snapshots under the tracked project's
   ignored `.private\atlas-v0` workspace;
5. rerunning read-only discovery and comparing names, roles, group counts, and reparse status;
6. recording any difference as a new manifest revision; and
7. requesting project-leader confirmation before changing A0 to done.

Do not rely on conversation history or session task state as the only handoff.
