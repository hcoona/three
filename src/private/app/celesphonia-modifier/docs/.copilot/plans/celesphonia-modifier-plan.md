# Celesphonia Modifier: Product, UX, and Technical Plan

**Status:** Final v5 transaction-metadata and bootstrap-certification implementation plan; writable release remains gated by Phase 0 evidence  
**Target:** Private WinUI 3 desktop application for Windows  
**Verified baseline:** Magical Girl Celesphonia v1.05, Steam App ID 1786790, Steam build 13624401, database `versionId` 2444532  
**Plan date:** July 15, 2026

## 1. Executive direction

Celesphonia Modifier is a safety-first visual document editor for one local RPG Maker MV save slot at a time. It is not a raw variable editor, trainer, runtime mod, cloud client, or general-purpose RPG Maker tool.

The decisive product silhouette is a **document/session editor with a hierarchical browser**:

- A lightweight Start page discovers or validates an installation.
- A slot catalog identifies existing `fileN.rpgsave` documents.
- A three-region workspace combines a hierarchical Save Explorer, a flexible Detail region, and an optional Review region.
- At narrower widths, the same regions become explicit master/detail routes rather than squeezed or overlaid panes.
- Settings is one full-width page in the application frame, not a permanent navigation destination or separate window.

The product earns trust by preserving the complete source documents, qualifying each writable operation independently, coupling the selected slot with its current `global.rpgsave` preview, and using deterministic recovery rather than optimistic writes.

### 1.1 Non-negotiable product rules

1. Installation recognition and writable compatibility are separate decisions. A recognized unsupported installation may expose only bounded read-only catalog and diagnostic behavior.
2. Only release-qualified operations may write. Confirmation, warnings, backups, recognition, and manual path selection never substitute for missing evidence.
3. Slot and global documents are lossless. `config.rpgsave` receives the same read-only preservation contract before any future config editing.
4. Every writable catalog session owns one immutable `SessionBaseline`, established only by successful Open, successful explicit Reload, or the start of a newly confirmed Restore/Reconcile transaction. Save verifies against it and never recaptures or rebases it.
5. Every catalog write is a recoverable transaction over one existing slot and the current global document. Ordinary editing and Save require a preview-congruent Open/Reload baseline; RestoreSlot and ReconcilePair use their own proven recovery preconditions and may start from an incongruent pair where explicitly allowed.
6. The game must be closed, every current `ObservedParticipantTuple` comparison and volume-capability gate must pass, every future role must be authorized by an `ExpectedRoleConstraint`, and all operation-specific probes must pass immediately before replacement. OS-assigned volatile metadata is observed and profile-qualified, never predicted.
7. No live file is deleted before replacement. Stage and rollback files are same-volume siblings of the live files.
8. The baseline archive, ownership marker, and immutable manifest are created, flushed, reopened, and captured as complete actual observations before `Prepared` and before any replacement. Before any stage is created, a synchronized durable `StageBuildIntent` proves its candidate and expected-missing path; `Prepared` references only `ArchiveVerified` and `StageVerified` actual observations.
9. Stable authoritative and mirror journals live under the app-owned State root, separate from deletable backup payloads. A temporary live execution replica is required until durable retirement. Logical terminality, replica sync/retirement, resolution attempts, backup lifetime, and retention progress are independent state machines.
10. Recovery uses the authoritative valid journal prefix, complete actual `ObservedParticipantTuple` records, and versioned `ExpectedRoleConstraint` sets for future or projected roles. Hash equality alone never authorizes continuation, rollback, resolution, retirement, or deletion. Terminal logical states remain immutable through replica repair, retirement, Conflict resolution, and payload deletion.
11. Unexplained file changes are described as external local changes. The application never claims to detect remote Steam Cloud divergence.
12. A standalone document may become writable only by proving that its open handle is exactly the selected live slot, discarding it, and reopening the live slot/global pair through the catalog. Imported replacement is unsupported.
13. The MVP write profile is a versioned, release-qualified local fixed-NTFS profile with stable handle identities, flush behavior, `ReplaceFileW`, and single-link live participants.
14. The application has no raw map, event, interpreter, switch, variable, identity/reference, script, or plugin-object editor.
15. No telemetry or automatic background networking is performed.
16. No proprietary game art, logos, portraits, CGs, icon sheets, databases, or plugin files are shipped.

### 1.2 Release shape

Phase 0 may produce a useful read-only application. A write-capable MVP requires Gold to reach the E3 evidence class on the accepted compatibility fingerprint. Ordinary inventory quantities are optional and ship only if independently E3. Every other domain remains read-only unless its own capability clears the same gate.

## 2. Evidence baseline, users, and scope

### 2.1 Target users

1. **Returning players** who need a clear view of a save and a small number of proven corrections.
2. **Completion-oriented players** who value structured progression and collection summaries without unsafe broad editing.
3. **Save-format testers and maintainers** who need diagnostics, validation, backups, and deterministic recovery without a raw write surface.

The application assumes the user owns and has installed the game.

### 2.2 Jobs to be done

- Find the correct installation and active save directory without relying on localized UI text.
- Identify a save slot from safe preview metadata.
- Browse character, progression, exploration, combat, equipment, inventory, mission, collection, difficulty, and diagnostic state in plain language.
- Change only a proven concept while seeing constraints, coupling, evidence status, and impact.
- Validate the candidate before any disk change.
- Save without losing the previous slot/global pair and recover from interruption.
- Restore one historical slot without reverting unrelated current global entries.
- Diagnose unsupported or corrupt data without disclosing private save contents.

### 2.3 Verified game and save facts

- The verified installation uses RPG Maker MV 1.6.1 and likely NW.js 0.29.0 x86.
- The observed installation is Simplified Chinese while RPG Maker `System.locale` remains `ja_JP`; `System.locale` is not an installation-language or app-language detector.
- Active saves are under `<install>\save`, not `<install>\www\save`, because a save-path relocation plugin changes the location.
- The save set may contain `file1.rpgsave` through `file20.rpgsave`, `global.rpgsave`, `config.rpgsave`, game-generated `.bak` files, and `steam_autocloud.vdf`.
- The research corpus includes sparse slots, proving slot numbers are not contiguous and the catalog must enumerate actual files independently of global metadata.
- Files use RPG Maker MV LZ-String `compressToBase64`; there is no encryption, checksum, signature, or authentication layer.
- `config.rpgsave` and `global.rpgsave` decode to ordinary JSON.
- Slot saves are JsonEx object graphs using `@`, `@c`, `@a`, and `@r`; a late-game slot can contain thousands of identities, wrappers, and references.
- The verified JavaScript decoder provides byte-identical encode/decode vectors that the .NET codec must match.
- Required slot roots include `system`, `screen`, `timer`, `switches`, `variables`, `selfSwitches`, `actors`, `party`, `map`, `player`, and `saveParams`.
- Variables are heterogeneous. Property presence, order, wrappers, identities, references, unknown fields, and optional values are compatibility data.
- Research found potential leaves for resources, EXP, inventory, equipment, map state, switches, variables, events, quests, collections, and plugin state. Observation does not establish write authority.

### 2.4 MVP scope

- Installation discovery, validation, manual selection, and active-save-path resolution.
- Slot catalog for slots 1–20, including missing, corrupt, permission-blocked, and unsupported entries.
- Lossless open, no-op preservation, change tracking, semantic undo/redo, validation, operation-specific transactional Save/RestoreSlot/ReconcilePair, durable Conflict resolution, backup history, and crash-resumable fixed retention.
- Read-only domain summaries and redacted diagnostics.
- Gold editing as the required writable MVP capability after E3 qualification.
- Ordinary nonspecial inventory quantity editing only if independently E3.
- Optional stretch capabilities only when their individual E3 packets pass before code freeze; otherwise their views remain read-only without delaying MVP.
- Signed per-user installation and manual browser-based update discovery.

### 2.5 Non-goals

- Editing while the matching installed `Game.exe` is running.
- Runtime memory modification, code injection, Steam API integration, achievement submission, or cloud synchronization.
- Remote Steam Cloud status, conflict detection, or automatic resolution.
- New-slot synthesis, broad corruption repair, automatic graph merge, or optimistic support for unknown formats.
- Full save-set restoration in MVP.
- Config editing in MVP.
- Arbitrary map relocation, event manipulation, switch/variable editing, script execution, plugin-state editing, or JsonEx identity/reference editing.
- Broad progression, quest, title, collection, key-item, equipment, or Memory Engram editing without named E3 capabilities.

## 3. Installation binding, catalog, and standalone files

### 3.1 Discovery and installation recognition

Discovery answers only whether a folder is a recognized Magical Girl Celesphonia installation. It does not decide whether any save is writable.

Discovery order:

1. Read `HKLM\SOFTWARE\WOW6432Node\Kagura Games\Magical Girl Celesphonia`.
2. Discover Steam libraries and parse `appmanifest_1786790.acf`.
3. Ask the user to choose the game folder.

A candidate is recognized when accessible evidence identifies the title:

- `Game.exe`, `package.json`, and `www\data\System.json` exist as regular files beneath the canonical installation root;
- Steam App ID `1786790`, when Steam metadata is present, or verified package/database evidence identifies the same game;
- package entry point and database structure are consistent with this RPG Maker MV title.

Recognition does not require a supported build, known hash, writable adapter, E3 capability, unmodified plugin set, or known database `versionId`. A decoy or ambiguous folder is rejected. An unknown, newer, localized, or modded but positively identified installation remains recognized.

Discovery returns a `RecognizedInstallation` with canonical paths, recognition evidence, and warnings. Save-path resolution is separate:

- safely parse a recognized save-path plugin when possible;
- use an adapter-declared read-only fallback only when its boundary is qualified;
- if the active path cannot be established, retain recognition and allow a user-selected directory only for bounded read-only inspection.

Manual read-only selection is restricted to regular `global.rpgsave`, `config.rpgsave`, `file1.rpgsave` through `file20.rpgsave`, and game `.bak` candidates in the selected directory. Manual selection never creates a writable binding.

### 3.2 Read-only sessions, operation binding, and immutable baselines

| State | Meaning | Allowed behavior |
|---|---|---|
| Unrecognized | Game identity is absent or ambiguous. | Refuse catalog binding; show folder-validation help only. |
| Recognized, catalog unavailable | Game identity is known, but no safe save directory or bounded parse path is available. | Installation diagnostics and manual read-only directory selection. |
| Recognized read-only | Safe bounded cataloging is possible, but writable compatibility is unknown, newer, modded, incomplete, or failed. | Slot catalog, safe previews, read-only summaries, original-byte export, and redacted diagnostics. |
| Operation-capable installation | Recognition, adapter/fingerprint, paths, profiles, stores, and common safety gates are available. | Permit only an operation-specific `TransactionPlan` whose own preconditions pass. This state alone does not authorize Save, RestoreSlot, or ReconcilePair. |

`RecognizedInstallation` is never accepted directly by Save, RestoreSlot, ReconcilePair, or an edit operation. `IWritableBindingFactory` establishes common live-installation authority: recognized installation, resolved active save directory, compatible adapter/fingerprint, qualified live/backup volume profiles, app-owned transaction/backup/state stores, process/path/capacity facts, and the ability to capture complete actual participant observations and build profile-qualified future-role constraints. `ITransactionOperationPlanner` then proves one operation's preconditions; generic binding rules MUST NOT silently impose Save-only congruence on RestoreSlot or ReconcilePair.

Operation routing is explicit:

- `Save` requires a writable `Open`/`Reload` baseline, fresh slot/global `ObservedParticipantTuple` equality with that baseline, exact current-pair congruence, complete validation, and at least one E3 semantic slot edit.
- `RestoreSlot` may start from an incongruent current pair or one eligible unresolved terminal Conflict. It requires an explicitly selected verified editor backup slot, a fresh transaction-scoped `RestoreStart` baseline, a losslessly parseable current global, compatible historical slot, exact selected-entry derivation, and pre-resolution evidence preservation.
- `ReconcilePair/RepairSelectedGlobalEntry` may start from an incongruent pair when the current slot and current lossless global prove an exact selected-entry-only global repair.
- `ReconcilePair/AdoptCongruentCurrentPair` requires an already congruent, fully valid current pair and a linked unresolved terminal Conflict; it is a durable all-`NoOp` resolution transaction, not a Save no-op.

Unknown/newer/modded installations may expose a read-only catalog only when enumeration, decompression, parsing, graph resolution, and preview extraction remain inside the reviewed read-only resource profile; failures are isolated per file and no partial document becomes editable.

A transaction baseline is immutable. It is established only by successful Open, successful Reload, or the confirmed start of a new `RestoreSlot`/`ReconcilePair`. Open/Reload becomes Save-writable only after Save gates pass. Restore/Reconcile baselines are transaction-scoped and may capture an allowed incongruent pair, but never enable ordinary editing or Save.

Validation, Save, recovery inspection, conflict dismissal, navigation, focus changes, and watcher notifications never refresh a baseline. Each baseline has a random generation ID, establishment reason/time, adapter/fingerprint/capability IDs, selected slot, and complete immutable `ObservedParticipantTuple` values for slot/global.

An `ObservedParticipantTuple` is a complete actual observation captured from a specific role at a specific instant. It contains schema/version and actual role/presence; SHA-256 and byte length; volume and file identity kind/value; link count; reparse state/tag; capability profile ID/definition hash; metadata-vector version; actual normalized creation/last-write/change times and attributes; delete-pending/directory flags; profile-required owner/group/DACL/security digest; every other required metadata field; and normalized metadata digest. Inability to capture/revalidate any required field makes that operation unavailable. MVP writable participants require a qualified profile and link count one.

An `ExpectedRoleConstraint` is separate and describes a future or projected role: deterministic required content/hash/length, presence/path, volume/profile, role-identity relationships where the qualified Windows profile guarantees them, link count, reparse/security/attribute requirements as applicable, and predicates for OS-assigned fields. Volatile timestamps, including `ChangeTime`, are recorded in actual observations and checked only against qualified profile predicates; they are never populated with predicted equality values.

### 3.3 Catalog behavior

1. Load `global.rpgsave` for preview metadata.
2. Enumerate actual `fileN.rpgsave` files independently so corrupt or stale global data cannot hide recoverable slots.
3. Show all slot numbers 1–20. Missing slots are labeled **Empty** and are not synthesized.
4. Show only safe recognition metadata: slot number, a game-provided preview title/location when safely available, playtime, modified time, and actionable status.
5. Isolate decode, compatibility, permission, and limit failures per file under the read-only profile.
6. Open structurally valid but incompatible documents read-only with a precise support reason.
7. On Open/Reload, capture the complete immutable slot/global baseline and recompute exact current-pair preview congruence.
8. If global is corrupt/unsupported or the selected entry differs from the exact preview derived from the current slot, keep diagnostics available but disable editing and ordinary Save.
9. A successful Save consumes the baseline. The session enters `CommittedBaselineConsumed`; another edit or write requires **Reload saved files** or Close/Open.

### 3.4 Standalone files

A manually opened slot outside a writable catalog is diagnostics/export-only. It may decode within parser limits, validate structure/JsonEx, display redacted diagnostics, export original compressed bytes, and export a redacted report. It may not expose semantic editing, Save, Restore, Reconcile, or playable replacement.

A standalone document may be bound only when its open handle is exactly the selected live catalog slot:

- standalone and current live-slot SHA-256 are equal;
- file identity and volume identity are equal;
- current length and every qualified metadata field are equal; and
- the live participant has an allowed link count under the active volume profile.

If any equality fails, the standalone document is discarded/closed and cannot be adopted. The only transition to writable catalog mode is to open the selected live slot and current global through the catalog and establish a new successful `Open` baseline from those live handles.

Content equality without identity equality is insufficient. A copied file, even if byte-identical, is not imported or applied. Imported replacement, **Apply this file to slot**, and use of a standalone file as a Save/Restore candidate are unsupported in MVP. A hard link may share identity, but link count greater than one makes the participant read-only unless a future multiply-linked profile is explicitly qualified.

### 3.5 Exported copies

The command is **Export edited copy**. It is available only for a bound in-memory candidate, is explicitly non-playable, and is never accepted later as a Save or Restore input. Playable changes must be committed from the live-bound catalog session.

An exported copy must not target an active save set. Resolve the destination parent and existing file by handle and reject:

- any validated active save directory;
- any canonical path that becomes an active slot/global/config path;
- any symlink, junction, reparse-point, case, or `..` alias into an active save directory;
- an existing file with the same file or hard-link identity as an active save participant.

The same destination restrictions apply when a standalone diagnostic document exports its original compressed bytes.

The error text is:

> Copies cannot replace playable saves. Open the installation's save catalog and use Save or Restore slot so the slot and global preview are updated together.

Permission, disk, or conflict states may offer export only to a non-active directory. They never offer an unsafe copy into the live save set.

## 4. Compatibility, evidence, and impact policy

### 4.1 Writable compatibility fingerprint

`ISchemaAdapterRegistry` selects from a versioned `CompatibilityFingerprint`, not `system._versionId` alone.

| Component | Required evidence |
|---|---|
| Game identity | Steam App ID; Steam `buildid` when applicable; `Game.exe` file/product version and local SHA-256; `package.json` name, version, and main entry; RPG Maker/NW.js probes; installed-language evidence; resolved install root. |
| Database identity | `System.json` `versionId`; local hashes for operation-relevant files; adapter-defined semantic probes over nonlocalized IDs, types, EXP curves, learnings, equipment types, limits, note/meta tags, and every field used by the operation. |
| Plugin identity | Ordered enabled-plugin list, enabled flag, name, discovered version, local script hash, normalized parameter hash, and operation relevance. Save-path relocation is always relevant. |
| Save graph identity | Required roots, node types, wrapper shapes, identity/reference validity, required path types, duplicate-value relationships, and operation-specific graph probes. |
| Adapter capability | Capability ID, evidence-packet ID, required evidence class, fingerprint dependencies, allowed paths, validators, serializer requirements, and release status. |

Localized or byte-different data is accepted only when a versioned compatible probe proves every field used by the operation is semantically equivalent. Matching a version number, file name, or user confirmation is insufficient.

The baseline adapter manifest records Steam App ID 1786790, build 13624401, database `versionId` 2444532, executable/package identity, enabled plugin set/order/parameters, relevant table probes, and required slot/global graph shapes.

Full component hashes remain local compatibility material. They are not exported as stable identifiers.

### 4.2 Writable compatibility and operation decision

Writable compatibility is evaluated only after installation recognition. Common compatibility proves game/build, plugins/parameters, database dependencies, document structure/losslessness, adapter capability, volume profiles, paths, stores, and versioned transaction infrastructure. Final write authority is operation-specific:

| Operation/mode | Required current-pair condition | Baseline/source | Candidate/postcondition |
|---|---|---|---|
| `Save` | Exactly congruent and fully valid. | Unchanged writable Open/Reload baseline and at least one E3 semantic slot edit. | Slot from the semantic change set; global from post-edit slot plus current lossless global; allowlisted pair. |
| `RestoreSlot` | May be incongruent; current global remains losslessly safe. | Fresh `RestoreStart`; verified compatible editor backup slot; eligible Conflict target if resolving. | Congruent/valid candidate pair; selected current-global leaves only. |
| `ReconcilePair/RepairSelectedGlobalEntry` | May be incongruent but exactly diagnosable from current live pair. | Fresh `ReconcileStart`; no foreign/standalone/historical slot. | Slot `NoOp`, global `Replace`; selected entry only; congruent result. |
| `ReconcilePair/AdoptCongruentCurrentPair` | Already exactly congruent and valid. | Fresh `ReconcileStart`; required unresolved terminal Conflict linkage. | Both `NoOp`; verified archive, `Committed`, mirror `InSync`, durable resolution completion. |

Every operation also requires fresh complete actual observations at its race gates, exact equality to immutable observations where no filesystem mutation should have occurred, and satisfaction of profile-qualified constraints where a mutation has occurred or is projected. Capability/derivation rules, allowlists, volume profiles, and process/capacity/journal/ledger preconditions remain mandatory. A failed operation probe disables that operation; it does not become a generic rule that makes another operation impossible. There is no **Try anyway**, manual-path, warning, confirmation, timestamp-only, imported-file, or **Use current as baseline** bypass.

### 4.3 Unknown `@c` labels

Unknown but structurally valid `@c` values are opaque strings:

- preserve spelling, placement, order, wrapper shape, identities, and references;
- never resolve or instantiate them as CLR types;
- do not reject the whole document merely because a label is unfamiliar;
- disable only capabilities that require understanding that class or containing subgraph.

Malformed class-marker or wrapper structure remains a parser error.

### 4.4 Evidence confidence

| Class | Meaning | Production write policy |
|---|---|---|
| E0 — Unknown | Path, authority, coupling, or behavior is unknown or contradictory. | Read-only. |
| E1 — Structurally mapped | Paths and types are observed, but runtime authority or side effects are unproven. | Read-only. |
| E2 — Behavior verified | Controlled before/after saves and in-game checks prove behavior on one fingerprint, but release regression coverage is incomplete. | Test builds only. |
| E3 — Release-qualified | E2 plus exact dependency fingerprinting, boundary/coupling tests, lossless diff tests, committed-result validation, disposable-copy game smoke tests, and review approval. | Writable only on the qualified fingerprint. |

Only E3 operations are writable. Confirmation, backup availability, and user expertise do not promote E0–E2 evidence.

### 4.5 Impact and blast radius

| Tier | Meaning | UX after E3 qualification |
|---|---|---|
| I0 — None | Read-only inspection. | No confirmation. |
| I1 — Localized | Small reversible change with no known cross-domain effect. | Inline review and normal Save. |
| I2 — Coupled | Multiple leaves, database constraints, or a secondary document must stay synchronized. | Prominent review; explicit confirmation for decreases/removals or nonobvious consequences. |
| I3 — Broad | Character build, progression, irreversible choice, or many derived values may change. | Blocking confirmation and dedicated restore guidance; normally post-MVP. |
| I4 — Prohibited | Arbitrary graph/script/event manipulation or no safe semantic boundary. | Never writable. |

Impact controls UX; it does not establish evidence.

### 4.6 Operation evidence packet

Every candidate operation has a reviewed packet containing:

- operation and capability IDs;
- current and required evidence classes;
- impact tier;
- exact fingerprint dependencies;
- authoritative and coupled paths;
- concrete allowed structural diff;
- preconditions, postconditions, and refusal conditions;
- controlled save-generation steps and semantic diffs;
- in-game verification, including a normal game save and reload;
- unit, property, corruption, transaction, and UI tests;
- reviewer, date, and evidence artifact locations.

The adapter exposes a capability only when its packet is E3 and its dependencies match the current fingerprint.

## 5. Lossless documents, bounded parsing, and privacy

### 5.1 Slot and global document contract

Both slot and global documents retain:

1. Original compressed bytes and baseline identity.
2. Decompressed source text.
3. An ordered syntax tree preserving property presence, order, array length/index occupancy, nulls, node types, untouched numeric/string lexemes, and unknown fields.
4. Raw source spans for untouched subtrees so they can be emitted without normalization.
5. JsonEx identity/reference tables for slots.
6. Typed projections that point into preserved nodes.
7. A semantic change set and exact mutation allowlist.
8. Adapter, capability, and validation metadata.

A semantic no-op returns the original compressed bytes and performs no replacement. Re-encoding an unchanged document is not an acceptable no-op.

Before encoding, compute a token/structure diff. Reject additions, deletions, reorders, type changes, identity/reference changes, array reshaping, or unknown-field changes outside the exact allowlist.

### 5.2 Global update contract

The current `global.rpgsave` is never rebuilt from a DTO and a historical global is never a single-slot participant. The adapter supplies finite selected-entry paths. Wildcards, root/array/entry replacement, edits to another entry, and normalization of unknown/untouched values are prohibited.

The allowlist intersects adapter paths, controlled game-produced evidence, and the planned operation's exact derivation. Entry creation remains blocked unless Phase 0 proves exact index, shape, order, omission/null, and game behavior.

- `Save`: slot `Replace`; global `Replace` or `NoOp`. Slot `NoOp` plus global `Replace` is invalid.
- `RestoreSlot`: `Replace/Replace`, `Replace/NoOp`, or proven `NoOp/Replace`. All-`NoOp` reports **Already matches** and creates no Restore transaction.
- `ReconcilePair/RepairSelectedGlobalEntry`: slot `NoOp`, global `Replace`, derived only from the current slot.
- `ReconcilePair/AdoptCongruentCurrentPair`: both `NoOp`, only as linked `ResolutionOnly` Conflict resolution.

Byte-identical candidates normalize to `NoOp`; replacement never exists only to change metadata. Direct preview editing remains unavailable.

### 5.3 Config contract

`config.rpgsave` is read-only in MVP. Diagnostic opening uses the same ordered, byte-preserving ordinary-JSON model and no-op behavior.

No MVP operation may emit a config candidate. Any operation requiring config or coupled switches remains unavailable.

Future config editing requires:

- its own E3 evidence gate;
- exact unknown-field preservation;
- proven synchronization with every coupled slot leaf, including switches 40/66 where applicable;
- an N-participant transaction design.

It cannot be added by inserting config into the two-document writer.

### 5.4 Versioned parser resource budgets and calibration

All limits come from a versioned `ParserLimitProfile`. Production profiles are compiled or signed release data and cannot be raised by normal users.

| Limit | Required enforcement |
|---|---|
| Compressed bytes | Check metadata and actual bytes while reading; reject before allocating beyond the cap. |
| Decompressed bytes | Count UTF-8 output incrementally and stop before appending beyond the cap. |
| Nesting depth | Reject before allocating the next object/array above the cap. |
| Node count | Count every scalar, object, array, and property value before materialization. |
| Identity/reference counts | Check before insertion; reject duplicates/dangling references independently. |
| Arrays | Cap array count, total elements, and largest array; grow storage in checked bounded increments. |
| Wall-clock | Apply separate cancellation deadlines to decode, parse, graph resolution, and validation. |
| Memory | Account for retained text, graph objects, tables, and temporary buffers; reject before excess allocation. |

Concrete limits remain non-production until measured on the minimum supported benchmark baseline and representative worst-case corpus.

#### Minimum benchmark baseline

| Dimension | Required baseline |
|---|---|
| CPU | x64 Intel Core i5-8250U-class or AMD Ryzen 5 3500U-class machine, with at least 4 physical cores and 8 logical processors, no faster than the selected reference machine. Record model, microcode, cores, and power plan. |
| RAM | 8 GiB installed. Loaded runs begin with at least 2 GiB available; the page file is not hosted on storage faster than the measured application volume. |
| Storage | Local NTFS SATA SSD. Do not use an NVMe-only baseline, RAM disk, network share, compressed/encrypted test folder, or warmed synthetic filesystem. Record model, firmware, free space, and allocation unit. |
| OS/runtime | Oldest Windows build claimed by the release, x64, fully patched; shipped self-contained .NET/Windows App SDK; Release `win-x64`; no debugger. If build 17763 remains supported, calibrate it or explicitly raise the support floor. |
| Security/background | Microsoft Defender real-time protection enabled. OS update, indexing rebuild, backup, and full-scan bursts invalidate and repeat the run rather than increasing limits. |
| Concurrent load | Run idle and standardized loaded profiles. The loaded profile occupies one logical processor and reserves memory until 2–3 GiB remains. Record generator/version, affinity, and achieved load. |

The published minimum hardware requirement is no lower than this baseline; a faster developer machine cannot substitute.

#### Representative worst-case corpus

For every recognized read-only and writable baseline, include the largest compressed/decompressed documents, deepest valid graph, highest node/identity/reference/array counts, widest object, largest scalar, slowest valid stage samples, combined near-limit cases, and hostile limit-plus-one/cancellation cases. Real corpus identities and hashes remain private; reports use opaque case IDs.

#### Measurement and approval

1. Build the exact signed-candidate Release `win-x64` output with only approved counters.
2. Reboot, settle startup activity, verify load/free-memory state, and record OS, runtime, app version, profile candidate, hardware, power plan, Defender, and corpus revision.
3. Run every case idle and loaded, with at least 5 cold-process and 30 warm-process measurements after one discarded warm-up.
4. Measure decode, parse, graph resolution, validation, and cleanup separately with a monotonic high-resolution clock.
5. Record allocations/GC counts and peak private bytes/working set; collect intrusive traces separately.
6. Record median, p95, maximum, variance, cancellation latency, retained-memory delta, paging, and GC anomalies; repeat invalid noisy series.
7. Select each cap above the largest supported case with explicit reviewed headroom demonstrated by boundary tests, not an unexplained multiplier.
8. Verify exactly-at-limit success and limit-plus-one refusal on the same baseline under standardized load.
9. Archive raw output, report, profile ID, corpus revision, and approval. Recalibrate after parser/codec/runtime, accepted fingerprint, or minimum-hardware changes, or significant regression.

No read-only or writable `ParserLimitProfile` is production until its benchmark artifact is approved. A writable release additionally requires the release candidate's profile ID to match the approved report. Slower-than-baseline results, paging failure, retained-memory regression, or missing loaded-profile evidence block release.

### 5.5 Parser boundary acceptance

- Exactly-at-limit inputs succeed for each independent dimension.
- Limit-plus-one inputs fail before the excess allocation or insertion and identify the stage and dimension.
- Tests cover compression/decompression bombs, deep empty containers, wide objects, scalar floods, identity/reference floods, duplicate/dangling identities, many small arrays, one huge array, integer overflow, cancellation, and memory exhaustion.
- Cleanup releases temporary buffers and no partial document becomes editable.
- Near-limit valid files round-trip without drift.
- Unknown valid `@c` labels round-trip; malformed marker shapes fail.

### 5.6 Logs and exported diagnostics

Full SHA-256 values exist only where locally required for baselines, transaction journals, backup verification, and compatibility records.

Routine logs, history UI, crash reports, and exported diagnostics omit:

- full hashes and hash prefixes;
- stable installation fingerprints;
- plugin/table hashes;
- source paths and file/volume IDs;
- installation record IDs;
- save values and preview strings;
- actor, account, Steam, machine, and user identifiers.

Exports normally contain only component match status, adapter/capability IDs, counts, stages, durations, state names, and sanitized error categories.

If one report needs internal correlation, generate a fresh in-memory report key and report-local HMAC labels. Never persist or reuse the key across reports. Paths are tokenized as `<install>`, `<save-dir>`, `<backup-root>`, and `<selected-output>`.

Diagnostics export is explicit, local, previewable, and scanned before writing.

### 5.7 Real saves and fixtures

- Real user saves never enter source control, CI, build/test artifacts, screenshots, issues, chat transcripts, or packages.
- Engineer-owned compatibility saves remain in an approved encrypted private corpus outside the repository, with named-user least privilege, access logging, 180-day access review, and deletion no later than 90 days after adapter support ends.
- A user-provided defect save requires case-specific consent, approved encrypted storage, and deletion within 30 days or within 7 days after a reviewed synthetic reproduction is accepted, whichever comes first.
- Deletion covers originals, working copies, exports, and recoverable copies under team control.

Sanitized fixture procedure:

1. Work only inside the approved private environment.
2. Minimize to the smallest reproducing graph.
3. Replace user/gameplay strings and scalar values with fictional generated values while preserving required types and boundaries.
4. Remove unrelated branches and regenerate timestamps, GUIDs, identities, and references.
5. Re-encode and verify the defect.
6. Scan decoded and encoded forms for personal data, paths, account/Steam identifiers, emails, URLs, credentials, secrets, source labels, and case-specific values.
7. Run the approved secret scanner and a policy check rejecting real-save signatures.
8. Require second-maintainer review before commit.

Fixture provenance records generator/version, seed when applicable, purpose, sanitizer, reviewer, and scan result. It contains no source hash or stable fingerprint of the real save.

## 6. Phase 0 write gates

Phase 0 is a read-only foundation and a write-capability proof. Field editing code may be prototyped in test builds, but production writing remains disabled until every required gate passes.

### 6.1 Codec and losslessness gate

- .NET LZ-String behavior matches verified RPG Maker MV vectors.
- Supported documents decode within calibrated limits.
- Unchanged slot, global, and diagnostic config documents return original compressed bytes.
- Allowed-leaf edits preserve all unknown data, order, wrappers, identities, references, and untouched lexemes.
- Malformed and adversarial inputs fail deterministically.

### 6.2 Recognition, compatibility, and binding gate

- Installation recognition and writable compatibility are implemented as separate types and commands.
- Recognized unknown/newer/modded installations can expose only bounded read-only catalogs/diagnostics.
- `IWritableBindingFactory` cannot accept manual selection, recognition, or confirmation as write authority.
- Exact/localized-compatible, unknown build, newer database, changed table/plugin/parameter, unsafe alias, and lookalike-folder tests pass.
- Standalone binding proves exact live identity or discards the document and reopens live; imported replacement is absent.

### 6.3 Operation-specific current and candidate preview gate

Phase 0 proves exact derivation and congruence for every supported selected-entry state:

1. Capture controlled disposable slot/global pairs immediately before and after a normal in-game save.
2. Vary one input at a time, including first/last slots, existing/absent entries, nulls, omissions, visibility, and index states.
3. Identify source paths, transformations, destinations, types, ordering, and values not derivable from the slot alone.
4. Implement pure adapter derivation over slot plus validated installation/database facts.
5. Prove current-pair congruence as a precondition for Save and all-`NoOp` adoption, not a generic prerequisite for RestoreSlot or global-repair reconciliation.
6. Prove RestoreSlot candidate congruence using the selected historical slot plus current lossless global.
7. Prove `RepairSelectedGlobalEntry` from the current live slot/global with slot `NoOp` and no unrelated diff.
8. Verify editor-written disposable saves in game, then normal game save/reload; retain only redacted evidence.

Ambiguous fields and entry creation remain read-only.

### 6.4 Transaction, recovery, resolution, and retention gate

- Every allowed/prohibited Save, RestoreSlot, and ReconcilePair disposition row is implemented and tested before archive or stage bootstrap.
- Journal schema v4 maps prior participant states without rewriting old bytes and keeps bootstrap, logical, participant, stable-replica, execution-retirement, resolution-attempt/draft-artifact, backup-set, transaction-retirement, and retention-operation projections separate.
- Every operation captures complete actual archive/marker/manifest observations before `Prepared`. For every `Replace` participant, synchronized `StageBuildIntent` must precede create-new stage materialization, and synchronized `StageVerified` must durably record the reopened stage's actual tuple before `Prepared`.
- A crash around `StageBuildIntent`, stage creation, or `StageVerified` has one deterministic bootstrap classification. A stage without a matching intent, or a changed/extra/reparse/hard-linked stage, becomes protected `BootstrapConflict`; it is never trusted or silently deleted.
- Recovery authorizes actions only when fresh complete observations satisfy exactly one applicable constraint set. Hash-only `B/C/X` descriptions are explanatory shorthand, and no future `ChangeTime` or other volatile OS metadata value is predicted.
- After every forward replacement or rollback, the implementation flushes/reopens, captures and durably replicates the complete actual role vector, validates it against the applicable constraints, and only then appends satisfaction or `RolledBack`.
- Stable State authoritative/mirror journals are never in deletable payload sets. The live execution journal is temporary and becomes deliberately absent only after durable crash-safe retirement.
- `Verified` is roll-forward-only to `Committed` or safely recorded `Conflict`. `Aborted` is impossible after any invocation intent/evidence, and all-`NoOp` resolution commits through `Verified`.
- Save never refreshes a baseline and enters `CommittedBaselineConsumed` after success.
- Restore/Reconcile use the pre-created baseline archive as `PreResolutionEvidence`; all-`NoOp` reconciliation writes every bootstrap/state/replica/attempt/completion/retirement record.
- Every readable resolution attempt closes durably, including pre-initialization cancellation/failure and terminal bootstrap/integrity block. Failed or blocked closure never resolves the original Conflict and retry requires the exact artifact-retirement/reclassification prerequisites; only `CommittedResolved` plus `ResolutionCompleted` resolves it.
- Versioned resolution, transaction-retirement, and retention ledgers have backward readers, ownership validation, hash chains, torn-final-record handling, corruption blocking, and restart failure injection.
- Pre-created sets receive durable outcome dispositions for cancellation, failure, Aborted, RolledBack, Conflict, integrity/version block, and success before cleanup/retention.
- Upgrade, repair, uninstall, downgrade, and clean-install tests preserve unresolved/version-blocked/integrity-blocked/divergent artifacts across every released schema.

### 6.5 Volume capability gate

- A versioned release profile qualifies filesystem/drive class, local/remote status, stable handle identities, link counts, metadata, flush behavior, capacity queries, and `ReplaceFileW` outcomes.
- The profile explicitly classifies each post-create/replace/rename/rollback field as deterministic equality, deterministic relationship, qualified predicate, or unsupported. Volatile OS-assigned timestamps, including `ChangeTime`, require observed-value predicates rather than predicted equality.
- MVP writes only on the qualified local fixed NTFS profile.
- Both live participants have link count exactly one at Open/Reload/RestoreStart and every race/recovery gate.
- Remote/SMB, removable, ReFS, FAT/exFAT, RAM-disk, unknown, reparse, multiply-linked, or unmatched participants remain read-only.

### 6.6 Operation capability gate

- Gold reaches E3 before any write-capable MVP release.
- Every other visible edit control corresponds to an independently qualified E3 capability.
- Release notes list only capabilities enabled by the shipped adapter.

## 7. Information architecture and workflows

### 7.1 Top-level pages

- `StartPage`
- `SlotPickerPage`
- `WorkspacePage`
- `SettingsPage`
- blocking `RecoveryPage` when unresolved journals exist
- read-only unsupported/diagnostics routes

The top-level `ShellFrame` owns navigation. There is no persistent `NavigationView` in MVP.

### 7.2 Workspace domains

- Overview
- Character
- Progression
- Exploration Status
- Combat & Skills
- Memory Engrams
- Equipment & Outfits
- Inventory & Currency
  - Ordinary Items
  - Special Items
  - Currency
- Missions & Titles
- Collections
- Difficulty
- Mature Status, optional and hidden by default
- Diagnostics

The hierarchy is shallow. Drag/drop and multi-selection are disabled.

### 7.3 Menus

**File**

- Open installation or slot (`Ctrl+O`)
- Close document
- Save (`Ctrl+S`)
- Export edited copy (`Ctrl+Shift+S`)
- Restore slot from backup
- Settings
- Exit

**Edit**

- Undo (`Ctrl+Z`)
- Redo (`Ctrl+Y`)
- Revert field
- Revert all changes

**View**

- Review (`Ctrl+H`)
- Validate (`Ctrl+Shift+V`)
- Find in current collection (`Ctrl+F`)

**Help**

- Safety Guide
- Export redacted diagnostics
- Check for updates
- About

At widths below 720 epx, the `MenuBar` collapses and a visible **Menu** command exposes the same hierarchy. Accelerators remain active.

### 7.4 First run

1. Explain that the matching game must be closed and every successful write creates a verified backup.
2. Run recognition discovery with specific progress text.
3. Show every recognized installation with separate **Recognized game** and **Write support** status.
4. Offer **Open saves read-only** when bounded cataloging is safe; offer writable actions only after `WritableCatalogBinding` exists.
5. If save-path resolution fails, retain diagnostics and allow a manually selected bounded read-only directory.
6. If no installation is recognized, offer **Choose game folder** and a validation checklist.
7. Surface unresolved recovery before normal opening or any write.
8. Before the first write, show nonblocking Steam Cloud education without claiming remote visibility.

### 7.5 Open and edit

1. Recognize the installation without requiring writable compatibility.
2. Resolve or safely select the save directory and build a bounded read-only catalog when possible.
3. Open the selected slot/current global read-only and evaluate fingerprint, adapter, capabilities, volume profiles, and exact current-pair preview congruence.
4. Establish one immutable `Open` baseline only after both handles and all required participant facts are captured successfully.
5. Create the common operation-capable `WritableCatalogBinding` after common installation/path/adapter/profile/store gates pass. Then expose Save, RestoreSlot, or ReconcilePair only when that operation planner proves its own preconditions; a failed Save congruence gate does not erase an otherwise eligible recovery operation.
6. Selecting an explorer node loads a typed detail view. Editable controls exist only for qualified operations.
7. Edits modify an in-memory semantic change set; coupled leaves are one operation and undo unit.
8. Inline validation is immediate; cross-document validation is debounced and available on demand.

Open may display an incongruent pair for diagnostics. Editing and Save remain disabled with **Preview mismatch — reconciliation required**, while only proven RestoreSlot or `RepairSelectedGlobalEntry` actions may be offered. File-watcher events and validation never refresh the baseline.

Status text is one of:

- `Unmodified`
- `3 unsaved changes`
- `Validating`
- `Ready to save`
- `Read-only: unsupported fingerprint`
- `Preview mismatch — reconciliation required`
- `Save blocked: 2 errors`
- `Committed — reload required`
- `Recovery required`

### 7.6 Validate, save, and reload

1. **Validate** runs structural, referential, invariant, compatibility, congruence, operation, volume, and transaction preflight checks without modifying disk or baseline.
2. **Save** is enabled only when changes exist, every operation is E3, current pair congruence holds, and no blocker exists.
3. Review summarizes concepts, coupling, evidence, and impact rather than raw paths.
4. I1 changes use normal Save. I2 decreases/removals or nonobvious consequences require explicit confirmation. I3 operations, if ever qualified, use a blocking dialog.
5. Section 11 writes the slot/current-global pair while revalidating the immutable baseline at every race gate.
6. Success enters `CommittedBaselineConsumed`, disables further editing/writing, and offers **Reload saved files** and **Close**.
7. **Reload saved files** is a complete reopen: resolve/discard unsaved state, reopen both live files, rebuild lossless documents, reselect compatibility/capabilities/profiles, verify congruence, then atomically replace the document and baseline generation.
8. Failed Reload leaves the prior baseline generation unchanged and writing disabled. Save never performs an implicit Reload or rebase.

### 7.7 External local change

If any live participant differs from the immutable baseline in hash, identity, volume, length, link count, or qualified metadata:

- stop before replacement;
- say **Changed outside this editor**;
- show file role and detected local time, not contents;
- offer **Reload from disk** as primary;
- offer **Export edited copy** only to a non-active directory;
- offer **Cancel**;
- never offer **Use current as baseline** or **Continue anyway**;
- never merge JsonEx graphs automatically.

The change may have come from the game, Steam synchronization, another tool, or manual activity. The application does not identify which producer caused it.

### 7.8 Restore one slot

**Restore slot from backup** plans `RestoreSlot`, never Save or import:

1. Select a verified historical slot from the editor backup catalog; historical global is never a participant.
2. Validate artifact/manifest against current installation, adapter, and selected slot.
3. Capture complete current live slot/global tuples as a transaction-scoped `RestoreStart`; the pair may be incongruent.
4. If resolving a Conflict, require one readable authoritative terminal unresolved Conflict with replica `InSync`; unrelated unresolved/version-blocked state still blocks.
5. Require current global to be losslessly parseable and safe enough to preserve every other entry/unknown field.
6. Derive the selected entry exactly from the historical slot, change only proven current-global leaves, and reject every unexpected diff.
7. Normalize identical participants to `NoOp`; allow `Replace/Replace`, `Replace/NoOp`, and proven `NoOp/Replace`. All-`NoOp` reports **Already matches** and routes linked adoption to ReconcilePair.
8. Before `Prepared`, create-new, flush, reopen, and capture complete actual observations for the current pair as the transaction baseline archive/`PreResolutionEvidence`, including its ownership marker and immutable manifest.
9. For each `Replace` participant, require synchronized `StageBuildIntent` before stage creation and durable `StageVerified` actual observation afterward; `Prepared` may reference only those verified stages.
10. Require a congruent, fully valid candidate pair; execute Section 11; enter `CommittedBaselineConsumed` on success and retire temporary recovery artifacts.

Restore is blocked by incompatible backup slot, corrupt/unsupported current global, incomplete derivation, unqualified volume, unsafe journal/ledger state, failed evidence archive, or any allowlist/profile failure. Historical global wholesale restore is excluded.

### 7.9 Reconcile a current pair

`ReconcilePair` uses only the current live slot and current live global:

1. **Repair selected preview entry** (`RepairSelectedGlobalEntry`): slot `NoOp`, global `Replace`, `MutatingPair`. It may start incongruent when the current slot exactly proves a selected-entry-only repair and current global is losslessly safe. No slot stage exists.
2. **Keep congruent current pair** (`AdoptCongruentCurrentPair`): both `NoOp`, `ResolutionOnly`. The pair is already exactly congruent/valid and `resolvesTransactionId` names one eligible unresolved terminal Conflict.

Both modes capture `ReconcileStart`, preserve/verify both live participants in the archive-first baseline/`PreResolutionEvidence` before `Prepared`, and use a new immutable transaction. Reconcile never accepts a foreign/standalone/historical slot, replaces the slot, normalizes unrelated global data, or repairs an unparseable global.

All-`NoOp` adoption MUST NOT return through a no-change shortcut. It durably records mirrored `ResolutionAttemptOpened`, synchronized `ResolverInitializationIntent`, archive bootstrap/verification, `Prepared`, `SlotSatisfied(NoOp)`, `GlobalSatisfied(NoOp)`, `Verified`, authoritative `Committed`, transaction/resolution-ledger/execution synchronization, `ResolutionAttemptClosed(CommittedResolved)`, `ResolutionCompleted`, and terminal retirement. The old Conflict journal remains immutable; the resolution ledger carries its lifecycle.

### 7.10 Later destructive full save-set restore

Full save-set restore is a separately designed post-MVP workflow. It requires:

- a coherent snapshot manifest for every included slot, global, and any future config participant;
- a preview of missing and extra slots;
- a fresh backup of the entire current set;
- explicit typed confirmation and Steam education;
- an N-file journal and rollback design;
- controlled in-game verification.

It is never an option inside single-slot Restore or Reconcile.

## 8. WinUI 3 experience specification

### 8.1 Shell

The window root is a `Grid` with:

1. Windows App SDK custom `TitleBar`;
2. `MenuBar`, visible at widths of 720 epx and greater;
3. page-specific `CommandBar`;
4. document-level `InfoBar` host;
5. `ShellFrame`;
6. document status strip when a document is open.

The title bar contains app icon, app name, optional document title, and state such as **Read-only**. It contains no required commands. It has a 32 epx minimum height, trims before caption buttons, exposes the full title through UI Automation and tooltip, and preserves a usable drag region.

Initial window size is 1280×800 epx. Enforce a 640×480 epx minimum with `WM_GETMINMAXINFO`, converting to physical pixels at current DPI and updating after `WM_DPICHANGED`.

### 8.2 Three-region workspace and measured Context switcher

`WorkspacePage` uses one responsive `Grid`, not a `SplitView`. Each pane/control is instantiated once.

```text
WorkspaceGrid
├─ ExplorerPane
│  └─ TreeView
├─ DetailPane
│  ├─ BreadcrumbBar
│  └─ DetailPresenter
└─ ContextPane
   ├─ page header with Back/Close
   ├─ ContextModeSwitcherHost
   │  ├─ SelectorBar
   │  └─ visible-label ComboBox
   └─ ContextPresenter
```

| Window width | State | Layout | Context mode control |
|---|---|---|---|
| `>=1180` epx | `ExpandedThreePane` | Columns `296, 1, *, 1, 320`; Context may close to zero. | Measured capability; nominal 320 epx does not guarantee fit. |
| `1008–1179` epx | `WideTwoPane` | Columns `280, 1, *`; Context replaces Detail with **Back to editor**. | Measure actual replacement-region width. |
| `720–1007` epx | `MediumTwoPane` | Columns `248, 1, *`; Context replaces Detail; forms one column. | Measure actual replacement-region width. |
| `640–719` epx | `CompactSinglePane` | One `*`; exactly one region visible; Detail/Context have Back. | Measure actual full-width Context route. |

`VisualStateManager` positions Context; it never selects the mode control. Use `SelectorBar` only when real localized/text-scaled desired width plus padding/safety fits `ContextPresenter.ActualWidth` without clipping/scrolling; otherwise use visible-label **View** `ComboBox`. At 225% in the fixed 320 epx column, ComboBox is expected unless post-layout measurement proves fit.

### 8.2.1 Context mode-switcher capability

One `SelectedContextMode` (`Changes`, `Validation`, `Backups`) is the sole state and drives content. SelectorBar and ComboBox expose the same localized options; ComboBox has visible header **View**. Selected indices are not application state.

```text
selectorDesiredWidth = ceil(ContextModeSelector.DesiredSize.Width)
requiredWidth = selectorDesiredWidth + 12 + 12 + 8
availableWidth = floor(ContextPresenter.ActualWidth)
useSelectorBar = availableWidth > 0 and requiredWidth <= availableWidth
```

Measure the real SelectorBar after resources/text scale with unconstrained horizontal width. Character counts, language tables, overall window width, breakpoints, and nominal column width are prohibited proxies. The 8 epx margin covers rounding, focus visuals, and theme variance. Post-arrange clipping fails closed to ComboBox.

Coalesce one low-priority remeasurement after Context `Loaded`/`SizeChanged`, text-scale/resource/language changes, `ActualThemeChanged` including High Contrast, or movement among three-pane/two-pane/compact states. Measure only with `XamlRoot` and positive Context width.

Use an ordinary Grid and standard controls; no custom control/template, wrapping panel, or horizontal ScrollViewer. For inactive Selector measurement, preserve logical selection/focus; temporarily make it visible but opacity zero, disabled, non-hit-testable, non-tab-stop (including items), Raw accessibility view, and no live region; measure; then collapse the inactive control and restore interaction/UIA only to the chosen control. It never receives focus/input/access key/announcement.

Either control updates `SelectedContextMode`; the state synchronizes both controls under a guard; Context content changes once. Layout/resource/theme changes never change mode or navigation history.

### 8.3 Scroll ownership and breadcrumb presentation

Never wrap `ListView`, `GridView`, or `TreeView` in another `ScrollViewer`.

| Region | Sole vertical scroll owner | Horizontal behavior |
|---|---|---|
| Explorer | `TreeView` internal scroller | No horizontal page scrolling; labels wrap to two lines, then trim with UIA/tooltip full text. |
| Normal detail form | One `ScrollViewer` inside `DetailPresenter` | Disabled; labels wrap and controls stretch. |
| Inventory/collection detail | Body `ListView` | Disabled; header and filters stay outside the list. |
| Context mode | Active mode's `ListView` | Disabled; findings wrap. |
| Slot picker | Slot `ListView` | Disabled; templates reflow before metadata stops fitting. |
| Start, Settings, Recovery | One page-level `ScrollViewer` each | Disabled. |

`BreadcrumbBar` is a single-line horizontal navigation control and never wraps. Bind through `ItemsSource`, with the current location last. When space is insufficient, standard control behavior collapses leftmost items into an ellipsis; activating it opens the built-in flyout in path order. Do not add a horizontal scrollbar or custom wrapping template, replace the ellipsis, or reproduce the flyout.

Selecting an earlier item navigates; invoking the final item does nothing. `Enter`/`Space` opens the ellipsis flyout, arrows move, `Enter` activates, and `Esc` closes and restores focus.

When a complete path must remain visible or copyable, use a separate noninteractive wrapping `TextBlock` path summary below the breadcrumb, optionally with **Copy path**. The page heading and summary wrap inside the Detail scroller; they are not navigation controls.

At 640×480 epx and 225% text scaling, the breadcrumb stays one line with standard collapse, the heading/summary wrap, and no horizontal scrollbar appears. UIA names the breadcrumb **Current location**, exposes localized item names and full path summary, announces the new heading politely after navigation, and never includes raw filesystem paths or save values.

### 8.4 Command placement

At `>=1180` epx, primary workspace commands are **Open**, **Save**, **Undo**, **Redo**, **Validate**, and **Review**. **Export edited copy**, **Restore slot**, **Settings**, and less frequent commands are secondary.

At `720–1179` epx, keep **Save**, **Undo**, **Redo**, and **Review** primary when they fit. Move **Open** and **Validate** to overflow first.

Below 720 epx, primary commands are:

- **Menu**
- **Back**, only in Detail or Context
- **Save**, only for a writable active document
- standard **More** overflow

Responsive states change placement, not availability.

Keyboard:

- `Ctrl+O` Open
- `Ctrl+S` Save
- `Ctrl+Shift+S` Export edited copy
- `Ctrl+Z` Undo
- `Ctrl+Y` Redo
- `Ctrl+Shift+V` Validate
- `Ctrl+H` Review at the last-used context mode
- `Ctrl+F` Find in current collection
- `Alt+Left` Back
- `Esc` close a flyout/dialog before navigation

### 8.5 Slot list/card reflow

Use one virtualized single-selection `ListView`.

Metadata priority:

1. Slot number and safe preview label.
2. Status: Ready, Empty, Corrupt, Unsupported, Permission blocked, Open, or Unsaved changes.
3. Modified time.
4. Playtime.

| Width | Presentation |
|---|---|
| `>=1008` epx | Header plus row columns: Slot `72`, Preview `*`, Playtime `120`, Modified `164`, Status `152`. |
| `720–1007` epx | Header hidden; two-line full-width row/card. Status moves to its own line at high text scale. |
| `640–719` epx | One-column card: slot/preview, full status, modified time, playtime; each may wrap. |

Status is never color-only. Missing and unsupported slots remain focusable so their explanation can be read. The accessible item name includes all metadata even when the visual template condenses it. Arrow keys select; `Enter` opens only when allowed. Returning restores selected and first-visible slots.

### 8.6 Master/detail routes, mode-switcher focus, and input

Compact route stack:

```text
Explorer -> Detail -> Context
```

Selecting a domain opens Detail; Review/`Ctrl+H`/validation opens Context; Back restores exact prior state. Resizing never changes logical history. Store stable selected slot/node/detail/context-mode IDs, first-visible items, scroll offsets, and last-focused automation IDs.

General focus: Detail focuses heading/requested field; Back restores selected tree item; **Go to field** navigates/scrolls/focuses/announces; closing Context/Settings/dialog returns to invoker.

Before changing mode-control presentation, determine whether focus is inside the active control. If not, do not move it. Selector-to-Combo focuses `ContextModeCombo` after arrange. Combo-to-Selector closes the drop-down, selects the matching item, and focuses it after arrange. Unchanged presentation retains focus; Context-close restoration wins. Never focus the measurement-only selector.

- SelectorBar uses standard arrows/Enter/Space.
- ComboBox uses `Alt+Down`, F4, Enter, or Space to open; arrows move; Enter commits; Esc closes without leaving Context.
- Tab encounters exactly one mode control; F6 treats mode/list as one Context region.
- Both retain standard touch targets; Combo stretches to available width.

UIA names the active control with localized **Review view**; Combo retains visible **View** header. Inactive is collapsed outside measurement. Measurement-only Selector is Raw, disabled, nonfocusable, and silent. Presentation changes raise no mode announcement; user mode changes update the Context heading and raise exactly one polite announcement. Exactly one of `ContextModeSelector`/`ContextModeCombo` is in UIA Control view/tab order after layout.

| State | F6 order |
|---|---|
| Expanded, Context open | chrome -> Explorer -> Detail -> Context -> status -> chrome |
| Expanded, Context closed | chrome -> Explorer -> Detail -> status -> chrome |
| Two-pane Detail | chrome -> Explorer -> Detail -> status -> chrome |
| Two-pane Context | chrome -> Explorer -> Context -> status -> chrome |
| Compact Explorer | chrome -> Explorer -> status -> chrome |
| Compact Detail | chrome -> Detail -> status -> chrome |
| Compact Context | chrome -> Context header/mode/list -> status -> chrome |
| Settings | chrome/back -> Settings content -> chrome/back |

`Shift+F6` reverses order. Collapsed/measurement-only controls cannot receive focus.

### 8.7 Wireframes

#### Expanded workspace

```text
+------------------------------------------------------------------------------------------------------+
| [icon] Celesphonia Modifier — Slot 7                                                     _  □  X    |
+------------------------------------------------------------------------------------------------------+
| File   Edit   View   Help                                                                            |
| [Open] [Save] [Undo] [Redo] [Validate] [Review]                                            [...]     |
+------------------------------------------------------------------------------------------------------+
| ! Steam synchronization may later replace local files. [Learn more]                         [x]     |
+-----------------------+------------------------------------------------------+-----------------------+
| SAVE EXPLORER         | Inventory & Currency > Currency                      | REVIEW                |
|                       |                                                      | [Changes][Validation] |
| Overview              | Currency                                             | [Backups]             |
| Character             |                                                      |                       |
| Progression           | Gold                                                 | 1 unsaved change      |
| Exploration Status    | [                                      25,000 ]      |                       |
| Combat & Skills       | Range: 0–99,999,999                                  | • Gold                |
| Memory Engrams        | Updates both proven currency leaves.                 |   12,000 -> 25,000    |
| Equipment & Outfits   |                                                      |   Coupling: valid     |
| Inventory & Currency  | Other currencies                                     |                       |
|   Ordinary Items      | Read-only until separately qualified.                | Validation: ready     |
|   Special Items       |                                                      |                       |
|   Currency          > |                                                      | [Go to field]         |
| Missions & Titles     |                                                      |                       |
| Collections           |                                                      |                       |
| Difficulty            |                                                      |                       |
| Diagnostics           |                                                      |                       |
+-----------------------+------------------------------------------------------+-----------------------+
| Ready to save • 1 unsaved change • Verified backup will be created                                   |
+------------------------------------------------------------------------------------------------------+
```

#### Compact Detail

```text
+----------------------------------------------------------------+
| [icon] Celesphonia Modifier — Slot 7                  _  □  X  |
+----------------------------------------------------------------+
| [Menu] [Back] [Save]                                      [...]|
+----------------------------------------------------------------+
| ! Steam synchronization may later replace local files.     [x] |
+----------------------------------------------------------------+
| Inventory & Currency > Currency                                 |
|                                                                |
| Gold                                                           |
| [                                                    25,000 ]  |
| Range: 0–99,999,999                                            |
| Updates both proven currency leaves.                           |
|                                                                |
| Other currencies                                               |
| Read-only until separately qualified.                          |
|                                                                |
| [Review: 1 change]                                             |
|                         (Detail owns vertical scrolling)        |
+----------------------------------------------------------------+
| Ready to save • 1 unsaved change                              |
+----------------------------------------------------------------+
```

#### Compact Review page

```text
+----------------------------------------------------------------+
| [icon] Celesphonia Modifier — Slot 7                  _  □  X  |
+----------------------------------------------------------------+
| [Menu] [Back to editor] [Save]                            [...]|
+----------------------------------------------------------------+
| Review                                                         |
| View  [ Validation                                      v ]     |
|                                                                |
| Ready                                                          |
| Gold coupling and global preview checks passed.                |
| [Go to field]                                                  |
|                                                                |
|                         (Context list owns vertical scrolling)  |
+----------------------------------------------------------------+
| Save allowed                                                   |
+----------------------------------------------------------------+
```

The compact Context surface occupies the full workspace width. It is never a bottom sheet, drawer, popup, or partial overlay.

The Expanded mode row is conditional: measured fit renders `[Changes] [Validation] [Backups]`; failed fit renders `View [ selected mode v ]`. The wireframe does not promise SelectorBar at an expanded breakpoint. The Compact example shows ComboBox, but a wider compact route may render SelectorBar when independently measured fit succeeds.

### 8.8 Settings and language

`SettingsPage` is one full-width page in `ShellFrame`, opened from **File > Settings** or compact Menu/More. It has Back, one vertical `ScrollViewer`, a 760 epx maximum content width, and Toolkit `SettingsCard`/`SettingsExpander`.

| Setting | Control | Behavior |
|---|---|---|
| Remember recent paths | `ToggleSwitch` | Immediate; turning off does not delete existing entries. |
| Clear recent paths | `Button` | Confirmation names what is cleared. |
| Show Mature Status | `ToggleSwitch` | Immediate; if hidden while active, navigate to Overview. |
| Diagnostics detail | redacted-level `ComboBox` | Immediate; no full-value/full-path option. |
| Check for updates | `Button` | Opens the fixed HTTPS release page in the browser. |

MVP has no editable backup count, byte cap, **Apply retention**, bulk cleanup, or retention dialog. Section 11.12 selects the newest 20 valid committed editor sets subject to protection; Section 11.14 executes deletion only through the crash-resumable retention ledger. There is no MVP byte cap.

Recent-item records contain paths and app-owned UI state only. They do not persist save-derived preview labels, values, hashes, or account metadata.

App-owned language follows Windows preferences. `en-US` is the complete default/neutral fallback. Simplified Chinese resources use canonical qualifier `zh-Hans` and serve `zh-CN`, `zh-SG`, and other `zh-Hans-*` preferences through Windows matching. Unsupported languages and `zh-Hant` fall back to `en-US`. MVP has no language selector.

`GameDataLanguage` is independent: `en`, `zh-Hans`, `ja`, or `unknown`, inferred from installed translation/plugin manifests and database labels. `System.locale` is diagnostic-only.

All app-owned resources exist in `en-US`; all MVP commands, validation, recovery, privacy, install/update guidance, and safety-critical errors also exist in `zh-Hans`. Release cannot rely on English fallback for a missing safety string. Game-provided labels remain independent and are identified to accessibility as game-owned text when needed.

### 8.9 Accessible states and transaction truth model

Each async/data surface has one Loading, Content, Empty, or Blocking Error state. Nonblocking warnings use `InfoBar`. Visible live-region text is polite for progress/success and assertive only for requested-action/data-safety blocks; validation is debounced.

The UI truth model separates:

1. archive/stage `BootstrapState` and per-stage intent/verification state;
2. immutable transaction `LogicalState` from the stable authoritative chain;
3. transaction `StableReplicaSyncState` (`InSync`, `AuthoritativeAhead`, `MirrorMissing`, `AuthoritativeUnreadable`, `Divergent`);
4. `ResolutionLedgerReplicaState` with the same independently named health values;
5. `ExecutionReplicaState` (`RequiredInSync`, `RequiredLagging`, `RetirementPending`, `Retired`, `Blocked`);
6. resolution target state, each resolution-attempt state/closure outcome, and independent attempt-draft artifact state;
7. backup content/protection/lifetime/disposition;
8. transaction-retirement state;
9. retention-operation state;
10. fresh actual participant observations and their constraint-validation results.

No UI layer collapses these into one enum or infers one projection from another. `MirrorMissing` is always qualified as transaction-journal or resolution-ledger mirror absence. Valid `RetirementCompleted` makes temporary execution-journal absence expected and excludes it from replica-health warnings.

Friendly classifications use complete actual observations plus the applicable comparison mode, never hash-only authorization:

| Classification | Required fact | Visible wording |
|---|---|---|
| `BaselineVerified` | Fresh actual tuple exactly equals the immutable baseline observation because no mutation is expected. | **Opened version is live — verified at {local time}.** |
| `CandidateVerified` | Fresh actual tuple equals the durable post-forward observed record and satisfies the candidate-live `ExpectedRoleConstraint`. | **Edited candidate is live — verified at {local time}.** |
| `OtherVerified` | Any deterministic field/relationship or qualified OS-field predicate fails. | **A different version is live — automatic recovery will not overwrite it.** |
| `Missing` | A role constrained present is actually absent. | **The live file is missing.** |
| `Unreadable` | Role cannot be safely opened/read. | **The live file could not be read.** |
| `Unverified` | Required observation/profile/constraint fact is unavailable. | **The live file's current state could not be verified.** |

Rollback/stage/evidence/archive cards use complete actual observations and named constraint results. No raw hash, ID, path, tuple, or save value appears. **No replacement was attempted** requires zero invocation intent/evidence; **The save was reversed** requires terminal `RolledBack`, satisfaction of the qualified projected-restoration constraints, and durable actual restored observations; **Backup verified** requires `ArchiveVerified`; **Stage verified** requires a matching durable `StageBuildIntent` and `StageVerified`; **Transaction evidence was preserved** requires verified ownership and complete observations. Generic **originals intact**, **files intact**, **nothing changed**, **timestamps restored**, **safely reversed**, and unqualified **backup available** are prohibited.

| State | Presentation/actions | Announcement |
|---|---|---|
| Archive bootstrap in progress | **Creating and verifying the baseline backup before changes.** | Polite progress. |
| `CancelledBeforePrepared`/`FailedBeforePrepared` | State no replacement was attempted, the bootstrap outcome, and cleanup/protection status. | Assertive facts. |
| `PreInitializationCanceled`/`PreInitializationFailed` | State resolver initialization did not reach a replacement-capable journal, no replacement/live mutation occurred, and show draft-artifact retirement status; original Conflict remains unresolved. | Assertive facts. |
| `IntegrityBlocked` attempt closure | **Resolution attempt closed because integrity could not be proven; evidence is protected and the original Conflict remains unresolved.** | Assertive; no retry until compatible recovery. |
| Pre-replacement incomplete ownership/observation proof | **Backup or stage preparation could not be verified; no automatic cleanup was performed.** | Assertive; name roles. |
| Any Other/missing/unreadable actual observation or failed constraint | **Changed outside this editor** or Recovery; no error-category safety inference. | Assertive. |
| Nonterminal after replacement/invocation | Recovery page with logical prefix, replica states, actual-observation/constraint cards, authorized actions. | Assertive. |
| `Verified` | **Both intended files and the verified baseline backup are ready; finishing commit.** Never offer rollback. | Assertive maintenance. |
| terminal + stable mirror lag | **Transaction is terminal — stable journal replica repair required.** Never rollback/downgrade. | Assertive maintenance. |
| terminal + retirement pending | **Transaction complete; temporary recovery files are being retired.** | Polite progress or assertive if blocked. |
| valid retired execution replica | **Temporary recovery journal retired — its absence is expected.** | No warning. |
| Divergent journals | **Journal replicas diverged — recovery required.** | Assertive; no auto-repair. |
| Unresolved Conflict | **Recovery requires a decision.** Offer only eligible Restore, global repair, or adoption. | Assertive; focus heading. |
| failed resolution closure | State exact outcome and **Original Conflict remains unresolved**; show retry prerequisites. | Assertive. |
| all-`NoOp` adoption completed | **Current pair adopted and prior Conflict resolved.** | Polite once after `ResolutionCompleted`. |
| Retention blocked | **Backup retention is blocked; no backup data was deleted automatically.** | Assertive when relevant. |
| Save success | **Reload saved files**/**Close**; `CommittedBaselineConsumed`. | Polite. |

Resolution wording is exact:

- **Resolution cancelled before initialization — no live mutation occurred; original Conflict remains unresolved**
- **Resolution failed before initialization — no live mutation occurred; original Conflict remains unresolved**
- **Resolution cancelled before changes — original Conflict remains unresolved**
- **Resolution failed before changes — original Conflict remains unresolved**
- **Resolution attempt aborted — retry available after cleanup**
- **Resolution attempt rolled back — retry available after cleanup**
- **Resolution attempt closed with an integrity block — evidence protected; original Conflict remains unresolved**
- **Resolution attempt created another Conflict — resolve the newest Conflict first**
- **Compatible version required to continue this resolution**
- **Conflict resolved by committed transaction**

Operation review shows operation/mode, dispositions, archive/stage proof, resolution target/attempt/draft state, transaction/resolution-ledger/execution replica state, retirement, and backup disposition without private save data. No imported slot replacement, global wholesale restore, generic Retry after replacement, or Keep for incongruent/unclassified pairs.

Use `ContentDialog` only for discard, Restore confirmation, or E3 impact confirmation. Recovery/retention are pages or inline status. Backups mode shows timestamp, slot, app/adapter status, validation, and transaction outcome only. Ledgers and game `.bak` files are never ordinary retention targets.

### 8.10 Recovery page, theme, motion, and UI Automation

`RecoveryPage` is a full `ShellFrame` page, never a dialog. Its root has one vertical `ScrollViewer` containing focusable Heading 1, nonclosable error `InfoBar`, summary, Slot/Global status cards, evidence, permitted actions, and a collapsed technical-details `Expander`. There is no nested scroller, fixed footer, horizontal scrollbar, or `ContentDialog` report.

Below 720 epx, card fields stack, action buttons are full-width and vertical, details are collapsed, and the shell strip says only **Recovery required**. At 640×480 epx with 225% text scaling, focus scrolls into view, all classifications/actions wrap, and nothing required is clipped or hover-only.

Primary action is derived from fresh classifications:

| Observation/constraint/ledger condition | Primary action |
|---|---|
| Nonterminal `Prepared`, exact unchanged pre-forward observations, zero intents/evidence | **Finish abort** / verified cleanup. |
| `SlotSatisfied`, durable slot post-forward observation satisfying its constraint and exact unchanged global pre-observation | **Resume global** only when all gates pass; otherwise authorized rollback. |
| `GlobalSatisfied`, durable intended observations satisfying both constraints and archive proof | **Resume verification**. |
| `Verified`, durable intended observations satisfying both constraints and archive proof | **Finish commit**; never rollback. |
| `RollbackPending`, qualified rollback prerequisites and current actual observations | **Resume rollback**. |
| Terminal Conflict, current pair exact congruent/valid, eligible linkage | **Keep congruent current pair** (new all-`NoOp` reconciliation). |
| Incongruent pair with exact current-slot selected-entry repair | **Repair selected preview entry**. |
| Eligible verified editor backup selected | **Restore selected backup**. |
| Terminal transaction authority with exact transaction-mirror prefix lag | **Repair transaction journal replica**. |
| Resolution ledger with exact mirror prefix lag | **Repair resolution ledger replica**. |
| Closed pre-initialization attempt with valid draft-retirement progress | **Finish resolution draft retirement**. |
| Terminal transaction with valid retirement intent/progress | **Resume temporary recovery-file retirement**. |
| `IntegrityBlocked`/`ProtectedIntegrityBlocked`, any Other/Missing/Unreadable/Unverified observation, failed constraint, divergent/authoritative-unreadable replica, or blocked ledger | Diagnostics/Support actions only; no automatic mutation or generic retry. |

The summary is one visible assertive live region raised after fresh classifications bind. Participant cards are named groups in slot/global order; archive/attempt/retirement evidence follows, then actions in matching tab order. Progress is polite; a new Conflict or retirement/retention block is assertive; terminal bootstrap/Aborted/RolledBack results are polite and focus the result heading. `Esc` cannot dismiss blocking Recovery and `Alt+Left` remains disabled.

- Mica window backdrop; neutral contiguous content surfaces.
- Semantic `{ThemeResource}` brushes; no hard-coded light/dark/warning colors.
- System Light, Dark, and High Contrast; no MVP theme selector.
- Platform accent for selection/focus.
- Standard controls and Fluent icons; no custom control subclass or replacement template in MVP.
- Minimum interactive target 40×40 epx at 100% text scale.
- Short opacity transition only; immediate changes when system animations are disabled.
- At least 40% localization expansion and RTL mirroring.
- Persistent labels, described-by help/error text, stable automation IDs, and visible focus.

Principal IDs include `CmdMenu`, `CmdBack`, `CmdSave`, `CmdReview`, `CmdMore`, `ExplorerTree`, `SlotList`, `ContextModeSelector`, `ContextModeCombo`, settings IDs, `BreadcrumbCurrentLocation`, `PathSummary`, `RecoveryPageHeading`, `RecoverySummaryLiveRegion`, `RecoverySlotStatus`, `RecoveryGlobalStatus`, `RecoveryEvidenceStatus`, `RecoveryResumeVerificationButton`, `RecoveryResumeRollbackButton`, `RecoveryResumeRetirementButton`, `RecoveryResumeAttemptDraftRetirementButton`, `RecoveryFinishCleanupButton`, `RecoveryKeepCurrentButton`, `RecoveryRepairPreviewButton`, `RecoveryRepairTransactionMirrorButton`, `RecoveryRepairResolutionLedgerButton`, `RecoveryChooseBackupButton`, `RecoveryExportDiagnosticsButton`, `RecoveryOpenFolderButton`, and `RecoveryTechnicalDetailsExpander`.

### 8.11 Combined responsive acceptance matrix

| Window epx | Text scale | Language/theme | Required workspace state | Context mode-control expectation |
|---|---:|---|---|---|
| 1280×800 | 100% | English / Light | Expanded; Context 320 epx | Measure; Selector only when `requiredWidth <= 320`. |
| 1280×800 | 225% | Simplified Chinese / Dark | Expanded; Context 320 epx | ComboBox expected unless recorded fit proof. |
| 1180×720 | 225% | English / Light | Exact three-pane boundary | ComboBox expected unless recorded fit proof. |
| 1179×720 | 225% | English / Dark | Context replaces Detail | Measure actual replacement width; do not reuse 320 result. |
| 1008×720 | 225% | Simplified Chinese / Light | Slot-table boundary | Measure actual presenter independently. |
| 1007×720 | 225% | Simplified Chinese / Dark | Slot-card boundary | Measure actual presenter independently. |
| 720×480 | 225% | English / Contrast | Exact two-pane boundary | Measure after Contrast resources/layout. |
| 719×480 | 225% | Simplified Chinese / Light | Compact Context route | Measure full route; breakpoint cannot force ComboBox. |
| 640×480 | 100% | English / Light | Minimum baseline | Measure actual compact width. |
| 640×480 | 225% | English / Contrast | Minimum/max scale/Contrast | No clipping or horizontal scroll. |
| 640×480 | 225% | Simplified Chinese / Dark | Minimum/max scale/localization | Preserve selection/focus. |

Each row records `selectorDesiredWidth`, `requiredWidth`, `availableWidth`, active presentation, selected logical mode, and focused automation ID before/after layout. Fail on inequality mismatch, lost selection/focus, duplicate tab stops/UIA controls, clipping, or horizontal scrolling.

| Context width | Scale | Language | Theme | Assertion |
|---:|---:|---|---|---|
| 320 | 100% | English | Light | Presentation equals measured fit. |
| 320 | 100% | Simplified Chinese | Dark | Presentation equals measured fit. |
| 320 | 200% | English | Contrast | Measured fit; no clipping. |
| 320 | 200% | Simplified Chinese | Light | Measured fit; no clipping. |
| 320 | 225% | English | Dark | ComboBox unless recorded proof fits. |
| 320 | 225% | Simplified Chinese | Contrast | ComboBox unless recorded proof fits. |
| 520 | 100% | English | Dark | Presentation equals measured fit. |
| 520 | 100% | Simplified Chinese | Contrast | Presentation equals measured fit. |
| 520 | 200% | English | Light | Presentation equals measured fit. |
| 520 | 200% | Simplified Chinese | Dark | Presentation equals measured fit. |
| 520 | 225% | English | Contrast | Presentation equals measured fit. |
| 520 | 225% | Simplified Chinese | Light | Presentation equals measured fit. |
| 720 | 225% | English | Light | Wider-route fit measured independently. |
| 720 | 225% | Simplified Chinese | Dark | Wider-route fit measured independently. |

Acceptance crosses below/at/above threshold, repeats 20 times without oscillation, preserves every mode across width/text/resource/theme/route changes, transfers focus only when focus was in replaced control, exposes one UIA Control-view element/tab stop, raises no presentation-only announcement, and raises one polite heading announcement for a user mode change. Queued measurement is ignored after close/navigation; subscriptions are disposed/re-registered once; reduced motion uses no animation.

All rows also prove 640×480 minimum, usable title bar, one vertical scroll owner, no horizontal scrollers, command/keyboard/F6 reachability, route/selection/scroll/unsaved-state preservation, focus restoration, semantic resources, and Light/Dark/Contrast accessibility. Recovery scenarios run at 640×480/225% in both languages with no bypass or preselected destructive action.

## 9. Domain treatment and operation gates

### 9.1 Domain presentation

| Domain | MVP treatment |
|---|---|
| Overview | Read-only slot, playtime, save count, preview, modified time, fingerprint, and adapter status. |
| Character | Read-only by default. HP/MP/SP is stretch only after E3. Transformation, states, and derived stats remain read-only. |
| Progression | Read-only. No broad progression editor. |
| Exploration Status | Read-only. Location and map/event state are never simple scalar edits. |
| Combat & Skills | Read-only. |
| Memory Engrams | Read-only in core MVP; Soul Ink and slot assignment require separate capabilities. |
| Equipment & Outfits | Read-only until named E3 operations exist. |
| Inventory & Currency | Gold is required E3 capability. Ordinary items are optional E3. Special/key/plugin items remain read-only. |
| Missions & Titles | Read-only; never claim Steam achievement effects. |
| Collections | Read-only until named repair capabilities exist. |
| Difficulty | Read-only unless Easy–Very Hard independently reaches E3. Hell is unavailable. |
| Mature Status | Hidden by default and read-only unless later privacy and evidence gates pass. |
| Diagnostics | Read-only, bounded, and redacted; no raw JSON editor. |

### 9.2 Candidate operation matrix

| Operation | Release position | Current / required evidence | Impact | Required gate |
|---|---|---|---|---|
| Gold | Core MVP | E1 / E3 | I2 | Prove zero, one, normal, maximum, increase, decrease, earn, spend, game save, and reload; validate `party._gold`, variable 215, integer/range, exact diff, and preview behavior. |
| Ordinary inventory quantity | Optional core | E1 / E3 | I2 | Only adapter-classified nonspecial items; prove add, remove-to-zero, use, buy/sell, maximum, reload, container shape, exclusions, and per-item limit. |
| Key/hidden/quest/plugin-special items | Post-MVP | E0 / E3 | I3 | Separate family capabilities proving acquisition/removal side effects, event dependencies, uniqueness, UI behavior, and plugin constraints. |
| HP/MP/SP current values | Stretch | E1 / E3 | I2 | Change current values only; prove alive/dead, zero HP, states, transformations, equipment/state maxima, level changes, save/reload, SP rules, and undo. |
| Easy–Very Hard difficulty | Stretch | E1 / E3 | I3 | Prove every transition, variables 4980/4978, switches 1994–1998, menu/combat behavior, save, and reload. |
| Hell entry/exit/downgrade | Not offered | E1 / E3 plus product approval | I4 in MVP | No MVP control. Backup or typed confirmation is insufficient. |
| Guided level/EXP | Deferred by default | E1 / E3 | I3 | Per actor/class evidence for EXP boundaries, skills, hooks, derived stats/resources, states/equipment effects, save, and reload. |
| Soul Ink | Stretch | E1 / E3 | I2 | Prove authoritative field, mirrors, earn/spend, cap, engram effects, UI, save, reload, and relevant plugins. |
| Other currency | Post-MVP | E0 / E3 | I2/I3 | Distinct named operation and packet. |
| Equipment/outfit/durability | Post-MVP | E0 / E3 | I3 | Prove slot types, restrictions, ownership, transfer, two-handed/dual-wield, plugin slots, durability, outfits, and derived resources. |
| Memory Engram inventory | Post-MVP | E0 / E3 | I3 | Prove acquisition/removal, uniqueness/stacking, plugin/database IDs, UI, and Soul Ink relationships. |
| Memory Engram assignment | Post-MVP | E0 / E3 | I3 | Prove slots, duplicates, actor/form restrictions, granted effects, and hooks. |
| Named story/quest/title/collection repair | Phase 3 | E0 / E3 | I3 | One transition per capability with prerequisites, consequences, menu/event behavior, next-event behavior, save/reload, and achievement review. |
| Raw variables/switches/map/event/interpreter/JsonEx/script/plugin objects | Never | E0 / N/A | I4 | No expert-mode or confirmation bypass. |

Any missed row-specific gate leaves the operation read-only.

### 9.3 Core validators

- Required root sections and expected node types exist.
- JsonEx identities are unique; every reference resolves; wrapper/class-marker shapes are valid.
- The complete compatibility fingerprint and operation dependencies match.
- Unknown properties and untouched source spans remain unchanged.
- Numeric inputs are finite integers and within adapter/database limits.
- Gold leaves agree and remain within the proven range.
- Inventory IDs exist, categories are ordinary when required, counts are nonnegative, and plugin/database limits pass.
- Difficulty coupling is validated whenever that capability exists.
- Actor level/EXP, skills, and derived resources are validated whenever that capability exists.
- Map ID, coordinates, tileset, events, interpreter, vehicles, followers, player movement, and transfer state remain unchanged.
- Global changes are limited to the selected slot's proven preview leaves.
- Candidates pass encode/decode/parse/resolve/validate before and after commit.

## 10. Technical architecture and repository integration

### 10.1 Project placement

- `src\private\app\celesphonia-modifier\CelesphoniaModifier.Domain`
- `src\private\app\celesphonia-modifier\CelesphoniaModifier.Application`
- `src\private\app\celesphonia-modifier\CelesphoniaModifier.Infrastructure`
- `src\private\app\celesphonia-modifier\CelesphoniaModifier.WinUI`
- matching test projects under `tests\private\app\celesphonia-modifier\`

### 10.2 Target frameworks and repository conventions

- Domain, Application, Infrastructure, and non-UI tests target `net10.0`.
- WinUI targets `net10.0-windows10.0.22000.0`, sets `TargetPlatformMinVersion` to `10.0.17763.0`, targets `x64`/`win-x64`, and is self-contained and unpackaged.
- WinUI UI tests target `net10.0-windows10.0.22000.0` and `win-x64`.
- Test projects use `Sdk="MSTest.Sdk"` and the repository-selected Microsoft.Testing.Platform runner.
- Use the SDK from `global.json`, CPM from root `Directory.Packages.props`, committed lock files, warnings as errors, and NBGV.
- Package versions appear only in `Directory.Packages.props`, alphabetically.
- Use an app-local `version.json` with path filters covering app and tests.
- Execute .NET commands through `mise exec -- dotnet`.

The repository root sets invariant globalization, so the WinUI project must override it:

```xml
<PropertyGroup Label="Globalization">
  <InvariantGlobalization>false</InvariantGlobalization>
  <DefaultLanguage>en-US</DefaultLanguage>
  <NeutralLanguage>en-US</NeutralLanguage>
  <SatelliteResourceLanguages>en-US;zh-Hans</SatelliteResourceLanguages>
</PropertyGroup>
```

App-owned resources are `Strings\en-US\Resources.resw` and `Strings\zh-Hans\Resources.resw`. `SatelliteResourceLanguages` limits publish output without stripping MRT Core resources or `resources.pri`. Non-UI projects may retain the repository default.

Add both Windows-only projects to `WindowsOnlyProjectReference` in `dirs.proj`:

```xml
<WindowsOnlyProjectReference Include="src\private\app\celesphonia-modifier\CelesphoniaModifier.WinUI\CelesphoniaModifier.WinUI.csproj">
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
</WindowsOnlyProjectReference>
<WindowsOnlyProjectReference Include="tests\private\app\celesphonia-modifier\CelesphoniaModifier.WinUI.UITests\CelesphoniaModifier.WinUI.UITests.csproj">
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
</WindowsOnlyProjectReference>
```

On non-Windows:

- exclude `@(WindowsOnlyProjectReference)` from source/test build traversal;
- retain `RestoreWindowsOnlyProjectsOnNonWindows`;
- pass `EnableWindowsTargeting=true`, each item runtime identifier, locked-mode settings, and parallel restore;
- restore Windows-only projects for lock validation but do not build or execute them.

Repository acceptance:

- Windows: locked restore, build, MTP tests, packaging tests, and UIA tests pass.
- Ubuntu: locked restore succeeds; non-Windows projects build/test; Windows-only projects restore but do not build/run.
- A clean checkout has no lock-file diff after locked restore.
- NBGV supplies application and installer file/product versions.
- MSBuild evaluation resolves `InvariantGlobalization=false`, `DefaultLanguage=en-US`, `NeutralLanguage=en-US`, and satellites `en-US;zh-Hans` for WinUI.
- Release smoke tests format under `en-US` and `zh-CN`; resource contracts, publish payload, installed payload, and UIA fallback pass for `en-US`, `zh-CN`, `zh-SG`, unsupported languages, and `zh-Hant`.

### 10.3 Layer responsibilities

- **Domain:** immutable baseline `ObservedParticipantTuple` values, future `ExpectedRoleConstraint` sets, operation/disposition plans, and independent bootstrap/logical/participant/replica/retirement/resolution/backup/retention models.
- **Application:** discovery/open/read-only/edit/validate; operation-specific planning; archive and stage bootstrap; post-mutation observation/constraint validation; transaction/recovery/replica synchronization; resolution-attempt closure; retirement/retention orchestration; session state and undo/redo.
- **Infrastructure:** recognition/path parsing; codecs/documents; process guard; observation/profile qualification; constraint projection/validation; durable stage building; replacement; stable journals and execution replica; archive manifests; resolution/transaction-retirement/retention ledgers; backups/settings/redacted logging.
- **WinUI:** shell/pages/view models/commands; measured responsive mode switcher; independent product-state projections; accessibility and pickers.

Use Generic Host DI and `ILogger` behind mandatory redaction. CommunityToolkit.Mvvm and Toolkit Settings controls require normal package review. Private apps disable reflection serialization. Use source-generated System.Text.Json for settings, manifests, journals, resolution/retention ledgers, diagnostics, and app metadata; save documents use purpose-built lossless representations.

### 10.4 Key interfaces

- `IGameInstallationLocator`, `IInstallationRecognizer`, `ISavePathResolver`
- `IReadOnlyCatalogFactory`, `IWritableBindingFactory`, `ISaveCatalogService`
- `ICompatibilityFingerprintBuilder`, `ISchemaAdapterRegistry`, `IOperationCapabilityRegistry`
- `ISaveCodec`, `IJsonExDocumentParser`, `ILosslessJsonDocumentParser`, `IEditableProjectionFactory`
- `IEditOperation`, `IGlobalPreviewDeriver`, `IPairCongruenceValidator`, `ISaveValidator`
- `IGameProcessGuard`, `ISessionBaselineService`, `IParserLimitProfileProvider`
- `IVolumeCapabilityDetector`, `IVolumeCapabilityProfileRegistry`
- `IFileIdentityReader`, `IFileLinkCountReader`, `IQualifiedMetadataReader`, `IObservedParticipantTupleReader`
- `ITransactionOperationPlanner`, `ITransactionPlanValidator`, `IExpectedRoleConstraintProjector`, `IRoleConstraintValidator`
- `IArchiveBootstrapService`, `IArchiveManifestWriter`, `IStageBuildService`, `IBackupDispositionStore`
- `ISaveTransactionWriter`, `ITransactionRecoveryService`, `ITransactionJournalReplicaCoordinator`
- `IStableTransactionJournalStore`, `IExecutionJournalReplica`, `IResolutionLedgerReplicaCoordinator`
- `IJournalVersionRegistry`, `IJournalReaderFactory`, `IJournalTransitionWriterFactory`
- `IBackupStore`, `IResolutionAttemptStore`, `IResolutionRecoveryService`, `IResolutionAttemptArtifactRetirementService`
- `ITransactionRetirementLedger`, `ITransactionRetirementService`
- `IRetentionService`, `IRetentionLedger`, `IRetentionRecoveryService`
- `IRedactedDiagnosticsExporter`

`ITransactionOperationPlanner` creates one immutable `TransactionPlan` containing operation/mode, dispositions, candidate sources, baseline/archive policy, allowed owned names, expected role constraints, and optional resolution linkage. `ITransactionPlanValidator` rejects every unlisted disposition or failed precondition before bootstrap. `IArchiveBootstrapService` creates/flushes/reopens and captures complete actual archive/manifest observations. `IStageBuildService` requires a replicated `StageBuildIntent` before create-new materialization, then flushes/reopens/validates and emits `StageVerified` with the actual stage tuple. `ISaveTransactionWriter` receives only actual verified observations plus explicit constraints and never predicts a future tuple.

`IGlobalPreviewDeriver` accepts the planned slot candidate (or current slot for global repair) plus validated installation facts and returns only proven selected-entry leaves. It never accepts historical global or foreign/standalone slot input. `IPairCongruenceValidator` compares every path/value/type/presence/null/visibility/index rule.

`IObservedParticipantTupleReader` captures every actual tuple field. `IExpectedRoleConstraintProjector` derives deterministic content, volume/profile, identity-relationship, link/reparse/security/attribute requirements and qualified predicates for OS-assigned fields; it cannot emit predicted volatile timestamps. `IRoleConstraintValidator` returns field-level predicate results against an actual observation. `ITransactionJournalReplicaCoordinator` synchronizes transaction authoritative/mirror plus the required execution replica; `IResolutionLedgerReplicaCoordinator` independently synchronizes the stable resolution-ledger pair. `IResolutionAttemptArtifactRetirementService` owns only pre-initialization attempt drafts. `ITransactionRecoveryService` authorizes from actual observations and exactly one applicable constraint set and delegates suffix repair, attempt closure, retirement, and retention to dedicated services. `IRetentionService` consumes durable backup dispositions and retirement facts rather than inferring safety from missing files. `ISessionBaselineService` creates generations only for Open/Reload or explicit Restore/Reconcile start; Save has no baseline-mutation API.

### 10.5 Versioned volume capability qualification

Every live and backup volume is matched to a versioned `VolumeCapabilityProfile`. Runtime observations do not grant writes; every required observation must match a release-qualified profile.

A profile records schema/profile ID, Windows build/test matrix, volume role, local/remote and drive class, filesystem name/flags, stable volume/file identity sources and scopes, link-count reliability, reparse behavior, metadata-vector version, per-field post-create/replace/rename/rollback classification, volatile-time predicates, flush behavior for every artifact role, `ReplaceFileW` behavior/error classification, same-volume method, capacity behavior, allowed encryption/compression/sparse/ACL/case/filter conditions, and explicit unsupported states. The journal records profile ID and definition hash. Phase 0 must measure these rules; the plan does not assume exact future Windows timestamp behavior.

MVP live writes require a tested local `DRIVE_FIXED` NTFS profile, nonremote path, reliable handle IDs/link count, link count exactly one, no participant reparse points, qualified metadata, successful file flush behavior, and failure-injected `ReplaceFileW` coverage. Remote/SMB, removable, ReFS, FAT/FAT32/exFAT, RAM-disk, unknown, read-only, multiply-linked, or unmatched volumes are read-only.

A backup-only profile may omit `ReplaceFileW` but must qualify create-new, no-reparse traversal, identities/link counts, capacity, file flush, and retention deletion. Link count is read at Open/Reload/RestoreStart and every save/recovery/retention race gate. Future multiply-linked support requires a separately approved profile; path enumeration is never claimed to find all hard links.

## 11. Pair transaction, backup, resolution, and recovery

### 11.1 Product state, authority, and required outcomes

The writer is a recoverable two-document transaction over one existing slot and the current global. It is not a filesystem-wide atomic transaction. Product state is the product of independent machines and evidence projections:

```text
SystemState(T) =
  BootstrapState(T)
× LogicalTransactionState(T)
× ParticipantExecutionState(T, Slot)
× ParticipantExecutionState(T, Global)
× StageBuildState(T, Slot)
× StageBuildState(T, Global)
× StableReplicaSyncState(T)
× ExecutionReplicaState(T)
× ResolutionLedgerReplicaState(installation(T))
× ResolutionTargetState(target(T))
× ResolutionAttemptState(attempt(T))
× ResolutionAttemptArtifactState(attempt(T))
× BackupSetState(backupSet(T))
× TransactionRetirementState(T)
× RetentionOperationState(retentionOp(backupSet(T)))
× ObservedRoleVector(T)
× ExpectedRoleConstraintSet(T)
```

No projection overwrites another. Logical state derives only from the valid authoritative transaction journal. Bootstrap progress, replica synchronization, execution-replica retirement, resolution-attempt closure/artifact lifetime, Conflict resolution, backup protection/deletion, retention progress, and later filesystem observations never rewrite a logical terminal state. Actual observations never become future expectations; future constraints never masquerade as captured facts.

Every safely classifiable started transaction reaches one immutable logical terminal outcome:

1. `CancelledBeforePrepared`: user cancellation before `Prepared`; no replacement invocation intent or replacement evidence exists, and bootstrap artifacts have a separate classified disposition.
2. `FailedBeforePrepared`: operational failure before `Prepared`; no replacement invocation intent or replacement evidence exists, and bootstrap artifacts have a separate classified disposition.
3. `BootstrapConflict`: archive, stage, journal-root, ownership, intent, or observation facts cannot be reconciled safely before `Prepared`.
4. `Aborted`: `Prepared` existed, but no replacement invocation could have occurred; fresh live observations exactly equal the immutable pre-forward observations and no contradictory artifact exists.
5. `RolledBack`: every replaced participant has a durable post-rollback actual observation satisfying the qualified projected-restoration constraint, required candidate evidence verifies, and every `NoOp` participant still exactly equals its pre-forward observation. It does not require equality to the original volatile timestamp values.
6. `Committed`: intended dispositions are satisfied by durable actual post-mutation observations, every applicable future-role constraint passes, the pair is congruent/valid, the pre-created archive/manifest verifies, and the authoritative terminal record is durable.
7. `Conflict`: complete facts cannot authorize automatic continuation, commit, or rollback; evidence is preserved.

`RecoveryVersionBlocked`, `ResolutionVersionBlocked`, and resolution-attempt `IntegrityBlocked` are recovery/lifecycle projections or closure outcomes, not replacement logical terminal states. They preserve the last readable logical prefix and block unsafe actions.

A terminal authoritative append freezes the terminal state and proof references: actual observation record hashes, constraint-set digests, archive/stage verification records, and relationship records. Later stable-mirror repair, execution-replica retirement, Conflict resolution, backup retention, or external live changes cannot change it. Durable history after payload deletion consists of the exact stable authoritative/mirror terminal journals, applicable resolution/retirement/retention records, and either the verified archive manifest or a retention `Completed` record linked to that manifest digest.

The writer never performs cross-volume replacement, deletes a live file first, silently overwrites an external change, imports a standalone slot, restores a historical global, or claims remote Steam Cloud detection.

### 11.2 Immutable plans, baselines, and operation preconditions

The writer accepts one immutable validated `TransactionPlan` from `ITransactionOperationPlanner`. It records operation/mode, dispositions, candidate sources, candidate hashes/lengths, derivation/allowlist/adapter evidence IDs, archive policy, optional resolution linkage, capability/profile IDs, finite allowed owned relative paths, baseline observations, and versioned `ExpectedRoleConstraint` sets. It contains no predicted final tuple or volatile timestamp. The writer cannot infer or change the plan after bootstrap begins.

Common gates include mutex ownership, closed game, qualified paths/profiles/capacity, compatible adapter/capabilities, safe app-owned State/Backup roots, no unrelated unresolved/version-blocked/integrity-blocked transaction or ledger condition, and fresh complete actual observation immediately before every mutation. An earlier observation is never treated as a lock. Where no filesystem mutation is expected, fresh actual tuples must exactly equal their immutable recorded observations; after a mutation, fresh actual tuples must satisfy the applicable constraint set and match the durable post-mutation observation record.

#### 11.2.1 `Save`

Save requires a writable immutable Open/Reload baseline; current slot/global actual observations exactly equal that baseline; current-pair congruence and validation; at least one E3 semantic slot edit; a slot candidate derived only from the in-memory change set; a global candidate derived only from the post-edit slot and current lossless global; and no resolution relationship. Save never captures a baseline, repairs an incongruent pair, imports bytes, or creates a resolution-only transaction.

#### 11.2.2 `RestoreSlot`

Restore replaces or confirms one selected slot from a verified editor backup. It may start from an incongruent current pair or one eligible unresolved terminal Conflict when the historical slot is compatible; current live slot/global are frozen as `RestoreStart` actual observations; current global can preserve all unrelated entries/unknown fields; the selected global entry is exactly derivable; and the current pair is preserved in the archive-first baseline.

A resolution target must be readable, authoritative, terminal `Conflict`, unresolved, and have transaction and resolution-ledger replicas `InSync`; it is the only unresolved transaction permitted. Historical global is never a participant. The candidate pair must be congruent and valid before any stage intent.

#### 11.2.3 `ReconcilePair`

Reconcile uses only current live slot/global:

- `RepairSelectedGlobalEntry`: slot `NoOp`, global `Replace`; may start incongruent; repairs only exactly derived selected-entry leaves.
- `AdoptCongruentCurrentPair`: both `NoOp`, `ResolutionOnly`; pair already congruent/valid and a target `Conflict` is required.

Reconcile cannot replace the slot, normalize unrelated global data, merge foreign/historical files, or repair corrupt/unparseable global. A linked target has the same readable authoritative terminal unresolved `Conflict` and replica requirements as Restore.

#### 11.2.4 Baseline archive content

Every operation creates one immutable baseline archive before `Prepared` and before any live replacement:

- `Save`: exact Open/Reload baseline slot and global bytes with their source actual observations;
- `RestoreSlot`: exact transaction-start `RestoreStart` slot and global bytes with their source actual observations;
- `ReconcilePair`: exact transaction-start `ReconcileStart` slot and global bytes with their source actual observations;
- all-`NoOp` adoption: the unchanged current pair.

For Restore/Reconcile this same pair is `PreResolutionEvidence`; no duplicate evidence pair is created later. The archive is never rewritten to add outcome data. Required rollback candidate evidence uses a separate immutable outcome manifest referencing the base manifest.

### 11.3 Stable storage roles, actual observations, and future constraints

Transaction journals and deletable backup payloads have different lifetimes and must not share a retention unit. For installation record `I`, transaction `T`, and resolution attempt `A`:

```text
%LOCALAPPDATA%\CelesphoniaModifier\State\I\Transactions\T\authoritative.ndjson
%LOCALAPPDATA%\CelesphoniaModifier\State\I\Transactions\T\mirror.ndjson
%LOCALAPPDATA%\CelesphoniaModifier\State\I\transaction-retirement-ledger.ndjson
%LOCALAPPDATA%\CelesphoniaModifier\State\I\resolution-ledger.authoritative.ndjson
%LOCALAPPDATA%\CelesphoniaModifier\State\I\resolution-ledger.mirror.ndjson
%LOCALAPPDATA%\CelesphoniaModifier\State\I\retention-ledger.ndjson

<live-save-directory>\.celesphonia-modifier.T.execution.ndjson
<live-save-directory>\.fileN.rpgsave.T.stage|rollback|evidence
<live-save-directory>\.global.rpgsave.T.stage|rollback|evidence

%LOCALAPPDATA%\CelesphoniaModifier\Backups\I\<slot>\<UTC>-T\
```

`authoritative.ndjson` is transaction logical authority. `mirror.ndjson` is its stable byte-for-byte replica. The resolution ledger has its own stable authoritative/mirror pair because `ResolutionAttemptOpened` must survive even when resolver transaction initialization never creates a transaction journal. All State files are app-owned and never members of a backup set. The live-side `execution.ndjson` is a temporary same-live-volume witness required while a transaction is active, unresolved, or not retired. Backup retention never deletes a State journal, ledger, or transaction directory. MVP does not compact/delete stable terminal journals; later compaction requires separate certification.

The random installation record ID contains no user/machine/path/account/save-derived data and is never logged/exported. State and Backup roots use qualified ownership/DACL, link count one, no-reparse traversal, complete actual observation capture, and compatible append-only readers.

A versioned `ObservedParticipantTuple` contains only actual facts captured for one role at one instant: actual presence; SHA-256/length; volume/file identity; link count; reparse facts; profile ID/hash; actual normalized creation/last-write/change times and attributes; owner/group/DACL/security digest; delete-pending/directory flags; every profile-required metadata field; and normalized metadata digest. Baseline, archive, stage, forward-result, rollback-result, evidence, quarantine, and retention observations all use this type.

A versioned `ExpectedRoleConstraint` contains only requirements for a future/projected role:

- exact deterministic presence/path, content hash/length, volume/profile, and role membership;
- exact identity relationships only where the accepted Windows/profile evidence qualifies them, such as a known stage identity becoming the live identity;
- exact or predicate-qualified link-count, reparse, security, attributes, compression/encryption/sparse, and other metadata requirements as the profile specifies;
- qualified predicates for OS-assigned values, including creation/last-write/change times, rather than guessed values;
- source observation IDs, constraint schema/profile digest, operation/phase, and failure classification.

If Phase 0 does not qualify a required field as equality, relationship, or predicate, the operation is unsupported. A constraint never omits a required field merely because Windows assigns it.

`Prepared` contains only knowable facts:

- concrete `ObservedParticipantTuple` values for existing live, stage, baseline archive, ownership marker, and immutable manifest objects;
- actual missing-role observations captured at the relevant race gate;
- profile-qualified forward and rollback `ExpectedRoleConstraint` sets derived from actual existing identities and deterministic candidate facts;
- future artifact specifications for evidence/output copies that do not yet exist.

`Prepared` must never contain an invented identity, link count, reparse observation, timestamp, ACL/security digest, metadata digest, or final tuple for a future live, rollback, evidence, or output object. Those facts are recorded only after create/mutation, flush, close, reopen, and complete actual capture.

For baseline identity `I_B` and verified candidate stage identity `I_C`, a qualified profile may constrain forward live identity to `I_C` and rollback identity to `I_B`, but only to the extent measured Windows semantics guarantee those relationships. Forward live content must be candidate hash/length; rollback content must be baseline hash/length. Rollback restoration requires baseline content and qualified baseline-role identity relationships, while volatile timestamps are newly observed and checked by rollback predicates. Failure of any deterministic field, relationship, or predicate is `Other`; zero or multiple applicable constraint-set matches is `Conflict`.

### 11.4 Per-volume capacity and durable writes

Live-volume reservation includes each `Replace` stage, worst-case rollback/evidence siblings and execution-journal suffix, 4 MiB allowance, then `max(16 MiB, 10%)` headroom. Backup/State reservation includes the baseline archive pair, ownership marker, manifest, stable transaction/resolution journal suffixes, resolution/retirement/retention records, allowance, and headroom. If roots share a volume, add requirements. Check preflight, before archive creation, before each `StageBuildIntent`, before `Prepared`, and at the final race gate; never subtract anticipated retention reclamation.

Each archive, stage, journal/ledger record, rollback/evidence file, manifest, and ownership marker is written, flushed, closed, reopened, and captured as a complete actual observation. Directory ownership/identity and no-reparse traversal are revalidated at every create, rename, quarantine, and delete gate. Stage creation is additionally prohibited until its matching `StageBuildIntent` is synchronized to every required transaction replica.

After a forward replacement, reopen live/rollback with writable handles, flush/close, capture the complete actual role vector, and append/replicate `ForwardMutationObserved` with the tuples and constraint-set digest. Then validate every actual tuple against the forward constraints and append/replicate `ForwardConstraintValidated`; only then may `ReplaceSatisfied`, `SlotSatisfied`, or `GlobalSatisfied` be appended. Rollback follows the same sequence with `RollbackMutationObserved`, `RollbackConstraintValidated`, and `RollbackSatisfied`. Guarantees are bounded by truthful Windows/device flush completion under the qualified profile.

### 11.5 Archive-and-stage bootstrap with durable candidate proof

Journal schema v4 uses one pre-`Prepared` bootstrap covering archive and stage creation. Bootstrap records use the same canonical bytes and three-replica protocol as logical transaction records. For a linked resolution, the stable resolution-ledger pair first receives `ResolutionAttemptOpened`; its immutable IDs and reserved roots appear in every later relationship record.

```text
BootstrapState =
  NotStarted | RootsReady | ArchiveBuilding | ArchiveVerified |
  StagesBuilding | ReadyForPrepared | Closed | BootstrapBlocked

StageBuildState =
  NotRequired | AwaitingIntent | IntentDurable |
  MaterializedUnverified | Verified | Blocked
```

Bootstrap order is normative:

1. If resolving, append identical `ResolutionAttemptOpened` bytes to the resolution-ledger authoritative/mirror pair; flush/reopen/verify both. This is the recoverable pre-initialization boundary. Immediately before proceeding to root creation, append/synchronize `ResolverInitializationIntent` with the reserved relative roots/names, profiles, ownership policy, and fresh expected-missing observations.
2. Create/validate the stable transaction directory, authoritative journal, stable mirror, and live execution replica; write/flush/verify compatible envelopes.
3. Append/replicate `TransactionStarted` and `ArchiveBuildStarted`, recording operation, transaction/baseline IDs, plan digest, backup-set relative path, source actual observations, finite allowed names, manifest schema, capacity/profile facts, and optional resolution IDs.
4. Create-new `archive-bootstrap.json` with ownership magic/version, transaction ID, allowed names, and source hashes/lengths. Flush/reopen/capture its actual tuple; append/replicate `ArchiveBootstrapOwned`.
5. Write create-new slot/global baseline files. After each, flush, close, reopen, capture its actual tuple, and append/replicate `ArchiveArtifactVerified(role, observedTuple)`.
6. Reopen current live participants and require exact equality with the transaction baseline observations. A mismatch closes bootstrap without `Prepared` or replacement.
7. Create-new immutable ownership/data manifest containing source observations, actual archive observations, relative names, profiles, lengths/hashes, and digest. Flush/reopen/capture its complete actual tuple.
8. Append/replicate `ArchiveVerified`, containing actual marker/archive/manifest observations and digest.
9. For each `Replace` participant, derive the final candidate bytes in memory and revalidate that the intended same-volume relative stage path is absent. Append/replicate `StageBuildIntent` containing transaction ID, plan digest, operation, participant role, candidate SHA-256/length, derivation/allowlist/adapter evidence IDs, intended relative path, volume profile ID/hash, and the fresh expected-missing observation proof. No stage create/open-for-create is allowed until this record is synchronized to authoritative, mirror, and execution replicas.
10. Revalidate the intended path is still missing; create-new the stage; write/flush/close/reopen it; capture its complete actual tuple; validate deterministic candidate content and all create-time profile constraints; append/replicate `StageVerified(intentHash, observedTuple, constraintResults)`.
11. Decode/parse/resolve/validate the candidate pair and require intended congruence. Reopen every archive/stage role and require exact equality with its recorded actual observation.
12. Append/replicate `Prepared`, referencing `ArchiveVerified`, every required `StageVerified`, the baseline actual observations, and the forward/rollback constraint-set digests.

No `ReplaceFileW` invocation is allowed unless `ArchiveVerified`, all required `StageVerified` records, and `Prepared` exist and authoritative, stable mirror, and required execution replica contain the synchronized mutation-enabling prefix.

Bootstrap recovery is deterministic:

- `StageBuildIntent` with the stage still missing may resume create/verification or close cancellation/failure without creating it.
- `StageBuildIntent` with a present stage but no `StageVerified` may append `StageVerified` only after no-follow reopen, exact candidate hash/length proof, complete actual capture, and all create-time constraints pass.
- A stage path present without its matching durable intent, a stage changed after verification, an unexpected extra role, reparse/multiply-linked object, wrong profile/path/identity relationship, or ambiguous ownership is terminal protected `BootstrapConflict`. It is never adopted by name, trusted by hash alone, or silently deleted.
- `ArchiveBuildStarted` without `Prepared` is an owned bootstrap, not an orphan. Recovery yields `CancelledBeforePrepared` for user cancellation with exact unchanged live observations; `FailedBeforePrepared` for a safely classified operational failure; `BootstrapConflict` for unsafe archive/stage/journal-root facts; or `RecoveryVersionBlocked` for unsupported semantics.
- An incomplete archive set may be deleted later only when its ownership marker, allowed names, and durable actual artifact observations prove ownership. Stage and draft artifacts follow their own retirement/protection records; archive cleanup authority never silently absorbs an unknown stage.

### 11.6 Versioned journals and replica synchronization

Transaction journals are append-only UTF-8 NDJSON hash chains; rewrite is prohibited and a torn partial final line is ignored. Stable envelopes include magic, schema, writer version, transaction ID, and framing/encoding ID. Canonical source-generated records include sequence/hash linkage, event/state, record time, operation/dispositions, actual observation references, constraint digests/results, sanitized facts/errors, and resolution IDs.

Schema v4 adds the observation/constraint split, `StageBuildIntent`, `StageVerified`, durable forward/rollback observation and validation records, resolution pre-initialization closures, integrity-blocked closures, and resolution-ledger stable mirroring. Every release reads every production schema. Older exact-tuple projections are interpreted only according to their original schema and are never upgraded by inventing missing constraint semantics. Unsupported required semantics enter the appropriate version-blocked state and preserve everything.

For every transaction record while execution is required:

1. append exact bytes to State authoritative; flush/reopen/verify; logical/bootstrap evidence may advance;
2. append identical bytes to State mirror; flush/reopen/verify;
3. append identical bytes to live execution replica; flush/reopen/verify.

Before stage creation, every forward/rollback `ReplaceFileW`, or any satisfaction transition, the enabling intent/observation/validation prefix must be synchronized to all required replicas. A crash after authoritative append cannot authorize the dependent action until exact-suffix repair completes.

The resolution ledger uses the same authoritative-then-mirror exact-byte, flush/reopen/verify protocol. `ResolutionAttemptOpened` is not considered durable enough to initialize the resolver until both copies are `InSync`. Resolution-ledger repair is independent of transaction-journal repair and never changes a target or attempt outcome.

```text
StableReplicaSyncState =
  InSync | AuthoritativeAhead | MirrorMissing |
  Divergent | AuthoritativeUnreadable

ExecutionReplicaState =
  RequiredInSync | RequiredLagging | RetirementPending | Retired | Blocked

ResolutionLedgerReplicaState =
  InSync | AuthoritativeAhead | MirrorMissing |
  Divergent | AuthoritativeUnreadable
```

`MirrorMissing` in transaction UI refers only to the unexpectedly absent stable transaction mirror; resolution-ledger health is named separately. Neither describes a retired execution replica. Exact-suffix repair copies authoritative raw bytes only to an absent/exact-prefix mirror or required execution replica; it never reserializes, truncates, merges, votes, or changes logical/attempt state.

A longer/divergent/post-terminal mirror blocks mutation/retirement. Missing/unreadable/corrupt authority sets `AuthoritativeUnreadable`; other replicas remain diagnostic only. Deliberate execution absence requires valid `RetirementCompleted`. Once transaction authority is terminal, no later transaction record is appended; repair/resolution/retirement/retention are external.

### 11.7 Dispositions and independent state machines

`ParticipantDisposition` is `Replace`/`NoOp`; `TransactionDisposition` is `MutatingPair`/`ResolutionOnly`.

| Operation | Slot | Global | Transaction | Allowed |
|---|---|---|---|---|
| Save | Replace | Replace | MutatingPair | Yes. |
| Save | Replace | NoOp | MutatingPair | Yes. |
| Save | NoOp | Replace | any | No; use Reconcile. |
| Save | NoOp | NoOp | any | No transaction. |
| RestoreSlot | Replace | Replace | MutatingPair | Yes. |
| RestoreSlot | Replace | NoOp | MutatingPair | Yes. |
| RestoreSlot | NoOp | Replace | MutatingPair | Yes; selected entry only. |
| RestoreSlot | NoOp | NoOp | any | No; **Already matches**; linked adoption uses Reconcile. |
| ReconcilePair | NoOp | Replace | MutatingPair | Yes; global repair. |
| ReconcilePair | NoOp | NoOp | ResolutionOnly | Yes; linked adoption. |
| ReconcilePair | Replace | either | any | Prohibited. |
| any unlisted combination | any | any | any | Prohibited before bootstrap. |

Byte-identical candidates normalize to `NoOp`; replacement is never metadata-only.

Bootstrap progress remains separate from logical state. `BootstrapState=BootstrapBlocked` may support logical `BootstrapConflict`, but it is not itself a logical terminal append and does not describe backup lifetime or attempt closure.

| Logical state | Meaning | Allowed next states |
|---|---|---|
| `Initializing` | Plan accepted; independent bootstrap may be in progress. | `Prepared`, `CancelledBeforePrepared`, `FailedBeforePrepared`, `BootstrapConflict` |
| `CancelledBeforePrepared` | User cancelled before `Prepared`; no invocation exists. | Terminal |
| `FailedBeforePrepared` | Operational failure before `Prepared`; no invocation exists. | Terminal |
| `BootstrapConflict` | Bootstrap/archive/stage/root facts are unsafe. | Terminal blocked |
| `Prepared` | Actual archive/stage observations, constraints, plan, and required replicas verify. | `SlotSatisfied`, `RollbackPending`, `Aborted`, `Conflict` |
| `SlotSatisfied` | Slot `NoOp` equality or durable validated post-forward observation is proven. | `GlobalSatisfied`, `RollbackPending`, `Aborted`, `Conflict` |
| `GlobalSatisfied` | Global disposition is proven by equality or durable validated post-forward observation. | `Verified`, `RollbackPending`, `Conflict` |
| `Verified` | Intended actual observations satisfy constraints; pair, validation, and archive proof pass. | `Committed`, `Conflict` |
| `Committed` | Authoritative terminal record is durable. | Terminal |
| `RollbackPending` | An invocation may have occurred and qualified rollback is authorized. | `RolledBack`, `Conflict` |
| `RolledBack` | Replace roles have durable validated restored observations; `NoOp` roles remain exact. | Terminal |
| `Aborted` | `Prepared` existed; zero intents/evidence and exact unchanged observations are proven. | Terminal |
| `Conflict` | Complete facts cannot authorize automatic action. | Terminal; resolution uses a new transaction |

Per-participant execution is separate:

```text
Pending | NoOpSatisfied | ReplaceIntentDurable |
ForwardMutationObserved | ForwardConstraintValidated | ReplaceSatisfied |
RollbackIntentDurable | RollbackMutationObserved |
RollbackConstraintValidated | RollbackSatisfied
```

A `NoOp` participant has no stage, stage intent, forward/rollback intent or call, rollback file, or candidate-evidence sibling. It becomes satisfied only after a fresh exact comparison to its immutable pre-forward actual observation.

Let `I(T)` be all durable forward/rollback invocation intents across valid replicas. Intent is the conservative invocation boundary:

```text
LogicalState(T) = Aborted  => I(T) = empty and no replacement evidence exists
I(T) is not empty          => LogicalState(T) is not Aborted
Aborted transition allowed => exact unchanged pre-forward/NoOp observations are proven
                              and no contradictory artifact exists
```

Within a safely classifiable, noncommitting prepared path, no possible invocation leads to `Aborted`; inability to prove unchanged facts leads to `Conflict`. This excludes pre-`Prepared` cancellation/failure and the sole successful no-invocation case: all-`NoOp` `ResolutionOnly` follows `Prepared -> SlotSatisfied(NoOp) -> GlobalSatisfied(NoOp) -> Verified -> Committed`.

- `Prepared -> Aborted` requires zero intents in every valid replica, no replacement evidence, and exact unchanged observations.
- `SlotSatisfied(NoOp) -> Aborted` is allowed only with pending global `Replace`, zero intents/evidence, and exact unchanged observations.
- `SlotSatisfied(Replace)`, `GlobalSatisfied`, `Verified`, and `RollbackPending` can never transition to `Aborted`.
- `Verified` can never transition to `RollbackPending` or `RolledBack`; it is roll-forward-only to `Committed` or safely recorded `Conflict`.
- Any transaction with an invocation intent terminates only `Committed`, `RolledBack`, or `Conflict`.
- Append validators reject any guard violation or post-terminal logical record.

### 11.8 Unified operation algorithm

1. Validate operation-specific preconditions/dispositions; freeze plan, baseline actual observations, candidate facts, constraint sets, and optional resolution linkage.
2. If resolving, append/flush/verify identical `ResolutionAttemptOpened` bytes in both stable resolution-ledger replicas; then append/synchronize `ResolverInitializationIntent` immediately before any resolver root or artifact is created.
3. Create stable transaction journals and execution replica; append/replicate `TransactionStarted` and `ArchiveBuildStarted`.
4. Create/flush/reopen/capture the baseline archive and manifest; revalidate live baseline; append/replicate `ArchiveVerified`.
5. For each `Replace` participant, synchronize `StageBuildIntent` to all transaction replicas before create-new stage materialization; flush/reopen/capture/validate it and append/replicate `StageVerified` with its actual tuple.
6. Decode/parse/resolve/validate and require intended congruence; adoption validates the unchanged pair. Build forward/rollback constraints without predicted volatile metadata.
7. Append/replicate `Prepared`; require transaction stable `InSync`, execution `RequiredInSync`, and resolution-ledger `InSync` when linked.
8. Reopen lives and recheck process, capacity, profiles, exact unchanged observations, and operation gates.
9. Satisfy Slot then Global. `NoOp` uses fresh exact comparison to the pre-forward observation. `Replace` synchronizes invocation intent to all three transaction replicas, reruns the race gate, calls `ReplaceFileW`, flushes/reopens/captures the complete actual role vector, replicates `ForwardMutationObserved`, validates it against the forward constraints, replicates `ForwardConstraintValidated`, and only then records replacement/participant satisfaction.
10. Prove the intended pair using durable actual observations and constraint results, then prove congruence, validation, allowed diff, and pre-created archive; append/replicate `Verified`.
11. Revalidate commit-time proof; append/flush authoritative `Committed`; copy the exact terminal suffix to stable mirror and required execution replica.
12. For linked success, append `ResolutionAttemptClosed(CommittedResolved)` to both resolution-ledger replicas and then durable `ResolutionCompleted`.
13. Enter `CommittedBaselineConsumed` and start Section 11.15 retirement. There is no post-`Verified` archive-copy phase.

Pre-`Prepared` cancellation/failure uses bootstrap terminal outcomes, closes any linked attempt with the phase-appropriate outcome, assigns archive/stage dispositions, and retires or protects temporary artifacts when safe. After `Prepared` but before any intent, exact unchanged proof may use `Aborted`. After any intent, use guarded rollback/`Conflict`, never `Aborted`. After `Verified`, commit, append `Conflict` if safely recordable, or remain explicitly blocked; never abort/rollback.

All-`NoOp` `ResolutionOnly` has no stage/`StageBuildIntent`/invocation/rollback/evidence sibling, but archive bootstrap, `Prepared`, both satisfaction states, `Verified`, authoritative `Committed`, replica synchronization, `ResolutionAttemptClosed(CommittedResolved)`, durable `ResolutionCompleted`, and retirement are mandatory.

### 11.9 Conditional rollback and replacement identity

Rollback is available only before `Verified` and after at least one invocation may have occurred. Handle `Replace` participants in reverse order. Before each rollback call, synchronize rollback intent to all three transaction replicas, rerun the current-observation and qualified-profile gates, and reopen every `NoOp` participant to require exact equality with its pre-forward observation.

Forward constraints require deterministic candidate content/hash/length and qualified role relationships. Rollback constraints require deterministic baseline content/hash/length and qualified restoration relationships. Volume/profile, role identity where the accepted Windows semantics qualify it, link count, reparse/security/attributes, and other deterministic metadata remain exact or relationship-constrained as applicable. OS-assigned volatile fields are newly observed and must satisfy profile predicates; rollback never predicts or claims restoration of original `ChangeTime`, creation time, last-write time, or another volatile value.

After each rollback call, flush/reopen/capture all affected actual roles; append/replicate `RollbackMutationObserved`; validate the projected-restoration constraint; append/replicate `RollbackConstraintValidated`; then append `RollbackSatisfied`. `RolledBack` requires these durable actual restored observations and successful qualified constraint results for every replaced role, plus exact unchanged observations for every `NoOp` role.

Other/missing/unreadable live data, failed deterministic relationship or predicate, absent/contradictory observation evidence, profile change, swapped/duplicated/extra roles, or zero/multiple constraint-set matches becomes `Conflict` when safe. Never overwrite an external current file.

Windows behavior references: <https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-replacefilew> and <https://learn.microsoft.com/windows/win32/api/fileapi/ns-fileapi-by_handle_file_information>. Exact field behavior is not asserted by this plan; it remains Phase 0 profile-qualified.

### 11.10 Observation-and-constraint recovery classification

Recovery validates transaction and resolution-ledger replicas independently, derives bootstrap/logical/participant/attempt projections, preserves terminal prefixes, captures fresh complete actual tuples, verifies ownership/manifests/relationships/profiles, and authorizes only when one applicable constraint set matches.

| Durable prefix/projection | Fresh observation/ledger fact | Required result |
|---|---|---|
| `ResolutionAttemptOpened`, no resolver transaction envelope | Reserved roots absent and no invocation/live-mutation evidence | Close `PreInitializationCanceled` or `PreInitializationFailed` under Section 11.13; original Conflict remains unresolved. |
| transaction start before archive | Owned roots; no invocation possible | Resume or close pre-`Prepared` outcome; ambiguous ownership blocks. |
| `ArchiveBuildStarted`, before `ArchiveVerified` | Marker/artifacts match allowed names and recorded actual observations | Resume verified archive artifact or close; no replacement. |
| `StageBuildIntent`, stage missing | Expected-missing proof remains valid | Resume create/verify or close cancellation/failure. |
| `StageBuildIntent`, stage present, no `StageVerified` | Exact candidate hash/length and create-time constraints pass after complete capture | Append/replicate `StageVerified`; otherwise `BootstrapConflict`. |
| stage present without matching intent, or verified stage changed/extra/reparse/hard-linked | Ownership/constraint facts unsafe | `BootstrapConflict`; protect stage and related set; never trust/delete automatically. |
| authoritative ahead | Destination absent or exact byte prefix | Exact suffix repair; dependent create/mutation/satisfaction forbidden until required sync. |
| `Prepared`, zero invocation intents | Exact unchanged pre-forward observations and no replacement evidence | Append `Aborted`; all-NoOp ResolutionOnly continues to commit. |
| `Prepared`, any invocation intent | Pre-forward still present | Never `Aborted`; guarded rollback classification or `Conflict`. |
| `ForwardMutationObserved`, validation absent | Recorded actual tuple available and applicable constraint unambiguous | Append validation then satisfaction if it passes; otherwise `Conflict`. |
| `SlotSatisfied` | Durable validated slot result; global exactly unchanged | Continue only if all gates pass; otherwise qualified rollback/`Conflict`. |
| `GlobalSatisfied` | Durable actual intended pair satisfies constraints | Validate/archive proof, append `Verified`, then commit. |
| `Verified` | Durable actual intended pair/constraints/archive proof pass | Append `Committed`; on safe proof failure append `Conflict` or remain blocked. Never abort/rollback. |
| `RollbackPending` or rollback observation without validation | Durable actual rollback result available | Validate projected-restoration constraints; append `RolledBack` only after every role passes. |
| nonterminal | Hash match but deterministic field/relationship or OS-field predicate differs | `Other`/`Conflict`. |
| terminal + stable exact-prefix lag | Terminal remains immutable | Exact suffix repair; no downgrade. |
| valid `RetirementCompleted` | Execution/temp artifacts absent | Expected `Retired`; exclude from health gating. |
| payload absent + retention `Completed` | Stable history links manifest digest | Expected deletion; terminal unchanged. |
| closed failed/blocked resolution attempt | Attempt artifact state and retry prerequisites classifiable | Keep original Conflict unresolved; retire/protect artifacts and expose retry only when Section 11.13 permits. |

Hash-only `B/C/X` is explanatory shorthand, never authorization. Adoption/global repair/Restore remain separate new transactions under their operation-specific gates.

### 11.11 Recovery and history UX

Surfaces show independent bootstrap/stage proof, logical state, transaction and resolution-ledger replica health, execution-replica/retirement, resolution target, resolution attempt/closure, attempt-draft artifact state, backup disposition/protection/lifetime, retention, and fresh Slot/Global observation classifications. They show no raw paths, IDs, hashes, tuples, constraints, timestamps, or save values.

Actions are operation-specific: **Restore selected backup**, **Repair selected preview entry**, **Keep congruent current pair**, **Repair transaction journal replica**, **Repair resolution ledger replica**, **Resume temporary recovery-file retirement**, **Finish resolution draft retirement**, or diagnostics/open folder. Generic Retry after replacement, import, wholesale global restore, and Keep for unclassified pairs are absent. Retry is shown only after the prior attempt is durably closed and every required draft/transaction artifact retirement or compatible reclassification is complete. All-NoOp adoption reports durable Conflict resolution. A valid retired execution journal is **Temporary recovery journal retired — its absence is expected**, never `MirrorMissing`.

### 11.12 Backup-set state, dispositions, protection, and fixed retention

Backup state is independent:

```text
BackupSetState = { Content, Protection, Lifetime, Disposition }
Content = NotCreated | Building | BaseContentVerified |
          OutcomeContentVerified | ContentBlocked
Protection = Protected | Unprotected
Lifetime = Active | RetentionEligible | Retiring | Retired | Deleted
Disposition =
  PendingOutcome | CommittedBackup | EphemeralPreInitialization |
  EphemeralCancelled | EphemeralFailedBootstrap | EphemeralAborted |
  RolledBackAttempt | UnresolvedConflictEvidence |
  ResolvedConflictEvidence | IntegrityBlockedEvidence |
  VersionBlockedEvidence
```

The immutable manifest is never rewritten; disposition is recorded in the retirement ledger or, when no resolver transaction/archive exists, in the mirrored resolution ledger and referenced by any later cleanup intent.

| Outcome | Backup-set disposition and cleanup/protection |
|---|---|
| `Committed` | `CommittedBackup`; user-restorable when compatible; newest-20 selection. |
| `PreInitializationCanceled`/`PreInitializationFailed` | `EphemeralPreInitialization`; normally `Content=NotCreated`; retry only after attempt-draft state is `ExpectedMissing` or `DraftRetired`. |
| `CancelledBeforePrepared` | `EphemeralCancelled`; eligible after transaction retirement and exact ownership proof. |
| `FailedBeforePrepared` | `EphemeralFailedBootstrap`; eligible after transaction retirement and exact ownership proof. |
| `Aborted` | `EphemeralAborted`; eligible after retirement because zero invocation and exact unchanged observations are proven. |
| `RolledBack` | `RolledBackAttempt`; protect at least 24 hours and while newest for the slot. |
| unresolved `Conflict` | `UnresolvedConflictEvidence`; protected indefinitely. |
| resolved `Conflict` | `ResolvedConflictEvidence`; eligible after `ResolutionCompleted`, retirement, and 24-hour/newest protection. |
| `BootstrapConflict` or attempt `IntegrityBlocked` | `IntegrityBlockedEvidence`; protected until a compatible signed reader durably reclassifies integrity and completes required artifact retirement. |
| transaction/attempt version block | `VersionBlockedEvidence`; protected until compatible safe reclassification. |

A failed or blocked resolution attempt never changes the target Conflict's backup disposition to resolved. Pre-initialization and ordinary canceled/failed/Aborted/RolledBack closures return toward retry only through their independent attempt-artifact and transaction-retirement states. A child `Conflict` set is protected until that child is resolved. Integrity/version blocks protect every referenced set and draft artifact.

If closure occurs before payload creation, the reserved backup-set ID still receives its durable disposition with `Content=NotCreated`; no payload-retention operation is needed. If any payload/marker was created, deletion requires the exact ownership evidence and retirement sequence described below. Attempt drafts outside a backup set are governed by Section 11.13, not silently treated as backup payload.

MVP retains newest 20 valid `CommittedBackup` sets per installation, no byte cap/settings. Protect every nonterminal/unretired transaction, unresolved Conflict, open attempt, closed attempt awaiting draft/transaction retirement, integrity/version-blocked attempt, unknown schema, active selection, retirement quarantine, required 24-hour/newest-per-slot set, future pinned/installer-required set, and game `.bak`.

Eligibility requires validated non-reparse Backup root, supported ownership evidence, terminal/attempt/retirement relationships, exact remaining actual observations, unique nonescaping paths, no extra content, and durable eligible disposition. Protection may leave more than 20. Sort committed candidates by `committedUtc`, then transaction ID ordinal. Delete only through Section 11.14. State journals/ledgers and attempt-draft artifacts are never backup-retention candidates.

### 11.13 Versioned Conflict resolution and complete attempt closure

```text
%LOCALAPPDATA%\CelesphoniaModifier\State\I\resolution-ledger.authoritative.ndjson
%LOCALAPPDATA%\CelesphoniaModifier\State\I\resolution-ledger.mirror.ndjson
```

Both files use app-owned-root validation, stable envelopes, canonical records, hash chains, flush/reopen verification, backward readers, torn-final-record handling, and exact-suffix repair. The authoritative/mirror pair must be `InSync` before opening an attempt, initializing a resolver, closing an attempt, completing a target, or starting a retry.

```text
ResolutionTargetState =
  UnresolvedReady | AttemptOpen | AttemptClosedAwaitingArtifactRetirement |
  ChildConflictBlocked | IntegrityBlockedByAttempt |
  CommittedAwaitingCompletion | Resolved | ResolutionVersionBlocked

ResolutionAttemptState =
  OpenedPreInitialization | ResolverInitializing | ResolverPrepared |
  ResolverExecuting | ResolverCommittedAwaitingClosure |
  Closed | AttemptVersionBlocked

ResolutionAttemptArtifactState =
  ExpectedMissing | OwnedDraftPresent | DraftRetirementPending |
  DraftQuarantined | DraftRetired | ProtectedIntegrityBlocked
```

`ResolutionAttemptOpened` records attempt ID, target transaction/Conflict hash, resolver transaction/bootstrap ID, operation/mode, plan digest, baseline/archive/backup-set IDs, versions/profiles, finite reserved relative roots/names, actual proof that each reserved path is missing, live baseline observation hashes, and start time. IDs are immutable/nonreusable; self/cycles, concurrent attempts, non-Conflict targets, and duplicate completion are prohibited. `AttemptOpen` is true only while the latest readable attempt lacks a closure; a failed/blocked closure immediately stops it from being `AttemptOpen` even if artifacts remain protected.

Before resolver initialization creates a State transaction directory, execution journal, backup root, or other draft, both resolution-ledger replicas must contain `ResolverInitializationIntent` with the exact reserved paths, profiles, ownership policy, and expected-missing observations. Actual created roots are captured by `ResolverDraftObserved` or by the first compatible transaction envelopes. Unknown artifacts without this intent are never adopted.

Every readable/classifiable attempt receives exactly one `ResolutionAttemptClosed`:

| Outcome | Required proof | Original resolved? | Retry effect |
|---|---|---:|---|
| `PreInitializationCanceled` | User cancellation before a replacement-capable resolver journal; no invocation/live-mutation evidence; live observations exactly unchanged; every reserved draft path classified. | No | After draft state is `ExpectedMissing` or `DraftRetired`. |
| `PreInitializationFailed` | Crash/operational failure before a replacement-capable resolver journal; same no-mutation and draft-classification proof. | No | After draft state is `ExpectedMissing` or `DraftRetired`, unless integrity blocked. |
| `CancelledBeforePrepared` | Resolver bootstrap cancellation; zero intent/evidence; exact unchanged live observations. | No | After resolver transaction retirement. |
| `FailedBeforePrepared` | Resolver bootstrap failure terminal; zero intent/evidence; safe facts. | No | After retirement unless blocked. |
| `Aborted` | Resolver terminal `Aborted`; exact unchanged proof. | No | After retirement. |
| `RolledBack` | Resolver terminal `RolledBack`; durable actual restored/evidence observations satisfy qualified restoration constraints. | No | After retirement and protection. |
| `Conflict` | Resolver terminal `Conflict`. | No | Resolve newest child Conflict first. |
| `IntegrityBlocked` | Resolver terminal `BootstrapConflict`, unknown/tampered draft, contradictory ownership/observation/constraint evidence, or another classifiable integrity failure. | No | No retry until compatible reclassification and all required draft/transaction artifact retirement complete. |
| `VersionBlocked` | Ledger readable, but resolver/bootstrap semantics unsupported. | No | Compatible reclassification/retirement required. |
| `CommittedResolved` | Resolver `Committed`, transaction and resolution replicas `InSync`, archive/stage/observation/constraint relationships valid, and required execution prefix synchronized. | Not until completion | Append target completion. |

Pre-initialization closure requires proving that no forward/rollback invocation intent exists in either resolution-ledger replica, any discovered transaction journal, or allowed live-side names; that no replacement/evidence role exists; and that fresh live actual observations exactly equal the attempt-open snapshot. `PreInitializationCanceled` additionally requires a durable user-cancellation request; otherwise a safe crash or operational initialization failure closes `PreInitializationFailed`. If any required fact is unavailable or contradictory, close `IntegrityBlocked` when the ledger is writable and the failure is classifiable; otherwise enter `ResolutionVersionBlocked` without unsafe mutation.

Attempt-draft artifacts have their own crash-safe lifecycle in the mirrored resolution ledger. A known intent-owned draft may use `AttemptDraftRetirementIntent -> AttemptDraftQuarantined -> AttemptDraftRetired`; each record lists actual tuples, allowed names, same-volume quarantine path, and expected absences. Both original/quarantine, neither when presence is required, changed/extra/reparse/hard-linked content, or an artifact without `ResolverInitializationIntent` yields `ProtectedIntegrityBlocked`. Draft retirement never changes the attempt closure or target Conflict.

`ResolutionCompleted` is a separate durable target record containing target/attempt/resolver IDs and hashes, archive/manifest proof, actual post-mutation observation and constraint-validation references, transaction and resolution-ledger replica proof, required execution-prefix or later retirement proof, completion time, and `ResolvedByRestore|ResolvedByGlobalRepair|ResolvedByAdoption`. The original Conflict is resolved iff one matching `CommittedResolved` closure and one valid `ResolutionCompleted` exist. Backward readers map older success-only records into this model without synthesizing new metadata facts.

Startup first repairs/classifies the resolution-ledger pair, then examines every `AttemptOpen` idempotently:

1. no resolver envelope: prove no mutation and classify every reserved/draft path, then append `PreInitializationCanceled` only for a durable cancellation request, otherwise `PreInitializationFailed`, or `IntegrityBlocked` when integrity proof fails;
2. readable nonterminal resolver: run normal transaction bootstrap/recovery, then close when it reaches a terminal outcome;
3. readable terminal resolver: append the one matching missing closure;
4. `CommittedResolved` without completion: revalidate all relationships and append only the missing `ResolutionCompleted`;
5. closed attempt with draft/transaction artifacts: resume the independent retirement path without reopening the attempt.

A new attempt requires a readable non-success closure, target still unresolved, transaction and resolution-ledger replicas `InSync`, attempt-draft state `ExpectedMissing` or `DraftRetired`, resolver transaction retirement complete when one exists, no unresolved child Conflict, and no version/integrity/retirement/retention block. `PreInitializationCanceled`, `PreInitializationFailed`, `CancelledBeforePrepared`, `FailedBeforePrepared`, `Aborted`, and `RolledBack` may become retryable under those guards. `Conflict` requires resolving the child. `IntegrityBlocked` requires a compatible signed reader to append durable safe reclassification and complete retirement; `VersionBlocked` requires compatible recovery. Neither is a generic Retry.

All-NoOp adoption follows `Verified -> Committed`, then `ResolutionAttemptClosed(CommittedResolved)`, then durable `ResolutionCompleted`; it never uses `Aborted`. Open attempts, closed attempts awaiting artifact retirement, child Conflict, integrity/version block, or divergent resolution-ledger replicas block normal upgrade/uninstall and protect every referenced artifact.

### 11.14 Crash-resumable backup retention ledger

```text
%LOCALAPPDATA%\CelesphoniaModifier\State\I\retention-ledger.ndjson
```

It is outside deletable sets, append-only, versioned, hash-chained, flushed, and never compacted in MVP. It deletes backup payload sets only; stable journals, State ledgers, live execution artifacts, attempt drafts, and retirement quarantines are outside its authority.

```text
RetentionOperationState =
  Idle | IntentDurable | SourceRenamed | Deleting |
  ReadyToRemove | Completed | StoppedBeforeDestruction |
  RetentionRecoveryBlocked
```

`RetentionIntent` records policy/version, set/transaction/slot, ownership-evidence kind/hash (immutable manifest, or for an incomplete pre-`Prepared` set the verified bootstrap marker plus allowed-name/per-artifact observation records), source/tombstone paths, complete actual observation snapshot, count/bytes, disposition/protection, transaction-retirement, attempt-closure/attempt-artifact references, and proof no relationship protects the set.

| Record | Meaning |
|---|---|
| `Renamed` | Source same-parent renamed and every actual observation revalidated. |
| `ReadyToRemove` | Listed payload and its manifest/bootstrap ownership file deleted; tombstone empty. |
| `Completed` | Empty tombstone removed; payload deletion is durably explained. |
| `StoppedBeforeDestruction` | Revalidation failed before rename; content preserved. |
| `RetentionRecoveryBlocked` | Post-rename facts unsafe; remainder preserved. |

Execution: revalidate; append/flush Intent; rename to `.deleting.<operationId>`; append `Renamed`; delete only files whose fresh actual tuples exactly equal the recorded retention snapshot; accept missing files only as an exact snapshot subset after `Renamed`; delete the immutable manifest or bootstrap ownership marker last; append `ReadyToRemove`; remove empty directory; append `Completed`.

Startup closes the rename gap: Intent-only with source present resumes/stops; Intent-only with source absent and exact tombstone present recognizes the completed rename from directory identity and every actual observation, then appends `Renamed`; `Renamed` resumes deletion; `ReadyToRemove` removes/completes. Both paths, neither before rename proof, changed/extra/reparse/multiply-linked content, or mismatch becomes `RetentionRecoveryBlocked`.

Revalidate eligibility immediately before intent, rename, and each delete. Never delete State roots, attempt drafts, unresolved/unknown/version/integrity-blocked evidence, active selections, retirement quarantine, or game `.bak`. Valid `Completed` explains absent payload without changing logical state, attempt closure, or replica health.

### 11.15 Crash-safe terminal transaction retirement

```text
%LOCALAPPDATA%\CelesphoniaModifier\State\I\transaction-retirement-ledger.ndjson
```

The ledger is append-only, versioned, hash-chained, flushed/reopened per record, and outside live temporary artifacts/deletable payloads. Transaction retirement removes only the temporary execution replica and transaction-owned stage/rollback/evidence siblings. Stable transaction/resolution journals and State ledgers remain. Pre-initialization attempt drafts without a resolver transaction use the separate Section 11.13 draft-retirement machine.

```text
TransactionRetirementState =
  NotEligible | Eligible | IntentDurable | ArtifactsQuarantined |
  ArtifactsDeleted | RetirementCompleted | RetirementBlocked
```

Eligibility requires supported terminal transaction authority; stable transaction journals exact/`InSync`; execution replica at the exact terminal prefix; no open/unknown attempt referencing selected artifacts; durable attempt closure when linked; durable disposition plus either a verified archive/manifest or, for pre-`Prepared` closure, verified bootstrap ownership/allowed-name/per-artifact observations; all terminal observation/constraint references valid; and no version/integrity/ownership/retention/replica block. For an original `Conflict`, valid `ResolutionCompleted` and explicit removable dispositions are required. Unresolved Conflict, `BootstrapConflict` without compatible reclassification, open or integrity/version-blocked attempt, divergent stable journals, or authoritative loss is not eligible.

`RetirementIntent` records schema/ID, transaction, immutable terminal state/hash, stable journal actual tuples/prefix hash, execution actual tuple, every temporary sibling actual tuple and its `StageBuildIntent`/`StageVerified` or mutation-evidence relationship, archive/manifest or bootstrap ownership evidence, disposition, resolution/attempt-artifact status, quarantine path, and expected absences.

| Record | Meaning |
|---|---|
| `ArtifactsQuarantined` | Every present temporary role was same-volume renamed into hidden quarantine and verified by fresh actual observation. |
| `ArtifactsDeleted` | All listed quarantined files deleted; quarantine empty. |
| `RetirementCompleted` | Empty quarantine removed; execution/temp artifacts durably retired. |
| `RetirementBlocked` | Facts diverged; remaining data preserved. |

Execution: append Intent; create `.celesphonia-modifier.T.retiring`; require each exact recorded temporary role at original or quarantine, never both; rename/reobserve; append `ArtifactsQuarantined`; delete only matching quarantined files; append `ArtifactsDeleted`; remove empty quarantine; append `RetirementCompleted`.

Intent-only restart reconciles exact original/quarantine locations from actual observations; both, neither where presence is required, unexpected content, or mismatch blocks. `ArtifactsQuarantined` resumes deletion; `ArtifactsDeleted` removes empty/absent quarantine and completes. Every crash boundary is idempotently classifiable.

After valid `RetirementCompleted`, `ExecutionReplicaState=Retired`; execution/stage/rollback/evidence absence is deliberate and excluded from `MirrorMissing`, replica-health gating, and lifecycle missing-artifact errors. Durable terminal proof remains stable exact journals, retained actual-observation/constraint records, retirement, and either present archive/manifest or matching retention `Completed`. History remains available across restart, upgrade/uninstall preflight, and later schema readers.

## 12. Verification and acceptance

### 12.1 Codec, parser, and document tests

- Golden cross-implementation LZ-String vectors; ordered JSON/JsonEx forward/back references, wrappers, optional/unknown fields; byte-preserving no-op and exact allowlist edits.
- Full profile schema, hostile corpus, cleanup, cancellation, exactly-at-limit, and limit-plus-one matrix.
- Approved cold/warm idle/loaded benchmark matrix on Section 5.4 hardware, with profile/report/corpus identity and regression thresholds.
- No production profile without approved minimum-hardware evidence; resource-limit failures leave no editable partial document.

### 12.2 Recognition, compatibility, standalone, and capability tests

- Recognition is tested independently from writable compatibility, including recognized unknown/newer/localized/modded installs and unrecognized lookalikes.
- Bounded read-only catalogs isolate unsafe, malformed, over-limit, and permission failures per file and never expose edit, Save, Restore, Reconcile, or edited-copy commands.
- Exact/localized-compatible fingerprints; changed database/plugins/parameters; dependent/independent unknown `@c`; and proof that no missing capability exposes an enabled edit control.
- An actual live standalone handle may close/reopen through the catalog. A byte-identical copy with different identity and a matching-name/time copy cannot bind. A hard link with link count greater than one is read-only. Imported replacement is absent.
- Current selected-entry mismatch disables editing/Save but does not suppress operation-specific recovery tests: RestoreSlot and exact current-slot global repair are offered only when their separate preconditions pass; adoption remains unavailable until congruent.

### 12.3 Transaction, replica, resolution, and retention failure injection

Inject termination/errors after every create, flush, reopen, journal/ledger append on each replica, archive/stage/replacement/rollback operation, observation capture, constraint validation, quarantine/rename/delete, and every TOCTOU/capacity/permission boundary. Compare implementation behavior to a pure product-state model whose projections remain independent.

Operation, archive, and stage timing:

1. Execute every allowed operation/disposition row and exact event sequence; reject every prohibited row before bootstrap.
2. For every allowed operation, create the baseline archive files, ownership marker, and immutable manifest before `Prepared`; assert `ArchiveVerified` contains only complete actual observations.
3. For each `Replace` participant, assert no stage create/open-for-create occurs before synchronized `StageBuildIntent` records transaction/plan digest, operation, role, candidate hash/length, derivation/allowlist/adapter evidence IDs, intended relative path, profile, and fresh expected-missing proof.
4. Assert `StageVerified` is appended only after write/flush/close/reopen, complete actual capture, candidate validation, and create-time constraint validation; `Prepared` references every required `StageVerified` and no unverified stage.
5. Crash after envelopes, `TransactionStarted`, `ArchiveBuildStarted`, ownership-marker verification, each archive file/verification record, source revalidation, manifest verification, `ArchiveVerified` on each replica, each `StageBuildIntent` replica append, stage create/write/flush/reopen, `StageVerified` on each replica, and `Prepared`; classify exactly.
6. Recover intent-with-missing-stage by resume or safe closure; recover intent-with-present-unverified-stage only by full verification; stage-without-intent, changed verified stage, extra role, reparse, hard link, wrong profile/path, or ambiguous ownership enters protected `BootstrapConflict` and is never trusted or silently deleted.
7. Change live source during archive/stage creation; close without `Prepared` or replacement.
8. Assert disposition/cleanup rules for Committed, pre-initialization closure, cancellation, pre-Prepared failure, Aborted, RolledBack, unresolved/resolved Conflict, and integrity/version block.
9. Restore/Reconcile use the one baseline archive as `PreResolutionEvidence`; all-`NoOp` adoption archives the unchanged pair before `Prepared` and creates no stage intent.

State, observation, and constraint invariants:

10. Validate the complete bootstrap, stage-build, logical, participant, observation, and constraint-validation transition tables for every operation/disposition.
11. Reject `Verified -> Aborted`, `Verified -> RollbackPending`, `Verified -> RolledBack`, `GlobalSatisfied -> Aborted`, `RollbackPending -> Aborted`, and `SlotSatisfied(Replace) -> Aborted`.
12. Reject any `Aborted` chain containing any forward/rollback invocation intent or replacement evidence in any valid replica.
13. Allow `Prepared -> Aborted` only with zero intents/evidence and exact unchanged pre-forward observations; allow `SlotSatisfied(NoOp) -> Aborted` only for pending global Replace under the same proof.
14. All-`NoOp` resolution reaches `Verified -> Committed`; it never reaches `Aborted`.
15. After each `ReplaceFileW`, assert flush/reopen and durable `ForwardMutationObserved` on all required replicas precede `ForwardConstraintValidated`, and both precede participant/satisfaction transitions. Apply the analogous ordering to rollback.
16. Exact deterministic content/hash/length, volume/profile, qualified role identities, link count, reparse, security, attributes, and other profile-classified fields must pass. Byte-identical content with a failed deterministic relationship or predicate is `Other`.
17. Vary actual creation/last-write/`ChangeTime` across qualified post-create/replace/rename/rollback outcomes. Assert the implementation records the actual values, applies only versioned profile predicates, and never requires a predicted future timestamp or original timestamp restoration.
18. `RolledBack` requires durable post-rollback actual observations plus successful projected-restoration constraints and exact unchanged `NoOp` observations; reject any implementation that defines it by equality to the original volatile timestamp tuple.
19. Swapped/duplicated/multiply-linked/extra/missing roles conflict even with matching bytes; every OS result is accepted only when exactly one applicable constraint set matches.
20. Terminal bootstrap/Committed/RolledBack/Aborted/Conflict values and proof hashes remain immutable through later live changes, resolution, retirement, and retention.

Transaction and resolution replicas:

21. Crash after each authoritative, stable-mirror, and execution append for every bootstrap/logical/observation/validation/terminal record; logical state and enabled repair/mutation match Section 11.
22. No stage creation occurs until `StageBuildIntent` exists identically in all three transaction replicas; no forward/rollback `ReplaceFileW` occurs until its invocation intent exists identically in all three.
23. No satisfaction/terminal transition occurs until the post-mutation actual observation and constraint-validation records are synchronized to all required transaction replicas.
24. Exact-suffix repair copies original raw bytes only; longer/divergent/different-envelope/post-terminal mirror and unreadable authority preserve/block.
25. Crash between authoritative and mirror appends for `ResolutionAttemptOpened`, `ResolverInitializationIntent`, every attempt closure, draft-retirement record, and `ResolutionCompleted`; resolution-ledger exact-suffix repair restores `InSync` without changing outcomes.
26. `MirrorMissing` applies only to the named stable transaction or resolution mirror. Required execution absence/lag uses execution-replica states.
27. Crash before/after `RetirementIntent`, quarantine creation, every artifact rename, `ArtifactsQuarantined`, each delete, `ArtifactsDeleted`, directory removal, and `RetirementCompleted`; startup follows exact resume rules.
28. After valid `RetirementCompleted`, execution/stage/rollback/evidence absence is expected and excluded from replica-health/lifecycle gating.
29. Original/quarantine both present, both absent before proof, observation mismatch, extra file, hard link, reparse, or unresolved relationship enters `RetirementBlocked` without deletion.
30. Stable terminal journals plus actual-observation/constraint records, retirement completion, and archive or retention completion continue to prove history after payload deletion.

Resolution attempts:

31. Crash after `ResolutionAttemptOpened` on either resolution-ledger replica and before resolver initialization. With all reserved paths missing, no invocation/live mutation, and exact unchanged live observations, close idempotently as `PreInitializationCanceled` only when a durable cancellation request exists, otherwise `PreInitializationFailed`; never leave a classifiable attempt `AttemptOpen`.
32. Inject crashes before/after `ResolverInitializationIntent`, each draft root creation, `ResolverDraftObserved`, resolver journal envelope, `TransactionStarted`, `ArchiveBuildStarted`, every `StageBuildIntent`, stage creation, `StageVerified`, archive creation step, and `Prepared`; classify attempt, transaction/bootstrap, draft artifacts, backup disposition, and retry eligibility independently.
33. Unknown/tampered pre-initialization draft, resolver `BootstrapConflict`, contradictory ownership/observation/constraint evidence, or other classifiable integrity failure closes `IntegrityBlocked`, protects/quarantines artifacts, and leaves the original Conflict unresolved.
34. Assert every readable/classifiable attempt receives exactly one closure among `PreInitializationCanceled`, `PreInitializationFailed`, cancellation/failure, Aborted, RolledBack, Conflict, IntegrityBlocked, VersionBlocked, or CommittedResolved. Closed failed/blocked attempts are never `AttemptOpen`.
35. Only `CommittedResolved` plus durable `ResolutionCompleted` marks the original Conflict resolved.
36. Crash after resolver `Committed`, after `CommittedResolved`, and before `ResolutionCompleted`; startup appends only missing idempotent records after full relationship/archive/stage/observation/constraint and replica revalidation.
37. Pre-initialization/cancellation/failure/Aborted/RolledBack permits retry only after attempt-draft and resolver-transaction retirement prerequisites pass. Conflict requires resolving the child. IntegrityBlocked requires compatible safe reclassification and retirement. VersionBlocked requires compatible recovery.
38. Crash across `AttemptDraftRetirementIntent`, quarantine, each rename/delete, and `AttemptDraftRetired`; both/neither/unexpected/tampered draft facts enter `ProtectedIntegrityBlocked` without reopening the attempt.
39. Upgrade/repair/uninstall and clean-install recovery distinguish open attempt, closed awaiting draft/transaction retirement, child Conflict, IntegrityBlocked, VersionBlocked, and resolved target; every blocked state preserves all references.
40. Unknown/corrupt resolution authority or divergent resolution-ledger replicas protect all references and block resolution/retention/lifecycle.

Backup retention and lifecycle:

41. Validate State/Backup/ledger roots against reparse, owner/DACL, link count, identity, profile, and escaping paths.
42. Crash before/after retention Intent, rename, `Renamed`, each deletion, `ReadyToRemove`, directory removal, and `Completed`; startup closes the source-absent/exact-tombstone rename gap.
43. Both paths, neither before rename proof, unknown extra, reparse, hard link, or observation/relationship mismatch enters `RetentionRecoveryBlocked`.
44. Retention never selects State journals/ledgers, attempt drafts, transaction-retirement quarantine, unresolved/protected/version/integrity-blocked data, game `.bak`, or active selections.
45. Retention deletion never produces stable `MirrorMissing` and never changes logical terminal state or attempt closure.
46. Test every released transaction/archive-bootstrap/archive-manifest/resolution/attempt-draft/retirement/retention schema at every state and torn-record position.
47. Upgrade/repair/uninstall classifies every bootstrap, logical, transaction-replica, resolution-ledger-replica, execution-retirement, attempt, attempt-artifact, backup-disposition, transaction-retirement, and retention state.
48. Controlled end-to-end vectors cover Save, Restore rows, global repair, all-`NoOp` adoption, forward observation/constraint validation, rollback without timestamp-equality assumptions, every failed/blocked resolution closure, successful retry, retirement, payload deletion, restart, and later lifecycle preflight.

### 12.4 Application and UI tests

- View-model/command tests cover every operation/disposition plus independent bootstrap/stage, logical, participant, actual-observation/constraint, transaction-replica, resolution-ledger-replica, execution-retirement, resolution-target/attempt/draft, backup, transaction-retirement, retention, and version/integrity-block projections.
- Operation review accurately shows archive/stage verification, slot/global Replace/NoOp, target/attempt closure, attempt-draft state, transaction/resolution-ledger/execution replica health, retirement, and backup disposition; all-`NoOp` reconciliation reports durable resolution.
- String tests prohibit safety claims without complete actual-observation plus constraint proof, forbid unqualified `MirrorMissing` and timestamp-restoration claims, and distinguish failed/blocked attempt closure from original Conflict resolution.
- Recovery pages render complete friendly classifications and only eligible Restore/global-repair/adoption/named-suffix-repair/transaction-retirement/attempt-draft-retirement actions; no generic Retry/import/global-wholesale action.

Measured Context-switcher tests:

1. Every Section 8.11 row captures real post-resource DesiredSize, applies 32 epx spacing, uses Context ActualWidth, and matches the inequality.
2. At 320 epx/225% in both languages, ComboBox is expected unless captured proof and visual inspection show fit; no clipping/horizontal scrollbar.
3. Cross below/at/above threshold and repeat 20 times without oscillation.
4. Preserve Changes/Validation/Backups across 320/520/720 widths, text scale 100/200/225%, en-US/zh-Hans resource refresh, Light/Dark/Contrast, and three-pane/two-pane/compact routes; retain Context scroll anchor/content instance/history.
5. Selector-to-Combo and open-Combo-to-Selector transfer focus exactly as specified; focus outside switcher remains unchanged; Tab/F6 see one active control.
6. Standard keyboard/touch behavior and target/focus visuals pass for both controls.
7. Narrator/UIA expose **Review view**, visible **View** header for Combo, selected mode, and one active Control-view element/automation ID. Measurement-only Selector is Raw/silent/nonfocusable; presentation-only switch emits no announcement; user mode change emits one polite heading announcement and no duplicate selection event.
8. Initial/queued/coalesced/stale measurement, close/navigation, subscription disposal/re-entry, reduced motion, semantic resources, and High Contrast pass.

General UI/build/packaging tests still cover semantic undo/redo, UIA IDs/live regions/accelerators/focus/list/dialog order, breadcrumbs, all responsive rows including 640×480/225% bilingual Recovery, globalization/resource fallback, publish payload, signed installer, lifecycle matrices, and absence of proprietary imagery or game-derived values in artifacts.

### 12.5 Controlled in-game acceptance

On disposable copies of the exact supported fingerprint:

- complete Phase 0 derivation matrix;
- for every shipped semantic operation, editor save -> catalog preview -> game load -> intended state -> normal game save/reload -> editor validation;
- interrupt between slot/global replacement and prove deterministic actual-observation/constraint recovery followed by successful game load/save;
- create external local changes during Save and prove Conflict preservation without source attribution;
- cover Save global `NoOp`, Restore `Replace/NoOp` and `NoOp/Replace`, Reconcile global repair, all-`NoOp` Conflict adoption, transaction/resolution-ledger/execution replica repair, pre-initialization and integrity-blocked attempt closure, eligible retry, terminal/draft retirement, payload retention deletion, and post-resolution load/save;
- restore a historical slot after other entries advance and prove unrelated entries/unknown fields unchanged;
- reject incongruent adoption and every foreign/historical slot input to Reconcile;
- prove lifecycle proceeds only after required attempts close, attempt drafts and resolver transactions retire, Conflicts resolve, transaction/resolution stable replicas are `InSync`, and transaction/retention operations complete; retired execution absence and retention-deleted payloads remain expected.

## 13. Packaging, updates, and source trust

### 13.1 MVP distribution

Ship one signed per-user Inno Setup installer containing the self-contained unpackaged `win-x64` publish output. A portable ZIP is not primary. Packaging verifies Authenticode, NBGV versions, SBOM, locked dependencies, `resources.pri`, and exactly the approved `en-US`/`zh-Hans` resource payload.

### 13.2 Recovery-aware install, upgrade, repair, and uninstall

Sign application/installer with trusted timestamp; install per-user under `%LOCALAPPDATA%\Programs\CelesphoniaModifier`; publish publisher identity/hash. Update, repair, and uninstall share signed read-only readers for every released transaction/archive/manifest, observation/constraint, stage-intent/verification, resolution/attempt-draft, transaction-retirement, and retention schema.

Before binary/registration mutation they require the app closed; validate only app-owned roots without following reparse points; classify bootstrap, logical terminality, transaction-journal replica sync, resolution-ledger replica sync, execution retirement, resolution targets/attempt closures/draft artifacts, backup dispositions, transaction retirement, retention recovery, and schema support; and log only redacted counts/versions/categories.

| Condition | Upgrade/update | Same-version repair | Uninstall |
|---|---|---|---|
| Every transaction/bootstrap terminal; transaction and resolution stable replicas `InSync`; execution replicas `Retired`; attempts closed; attempt drafts `ExpectedMissing`/`DraftRetired`; no unresolved Conflict/version/integrity block; ledgers readable and no retirement/retention in progress | Proceed. | Proceed. | Proceed; preserve backups/settings unless separately selected. |
| Readable nonterminal transaction, `AttemptOpen`, unresolved Conflict, or child Conflict | Block before mutation; offer **Open recovery**/**Cancel**. | Restore same signed binaries only; preserve state/force Recovery. | Block. |
| Closed failed attempt awaiting attempt-draft or resolver-transaction retirement | Block until the applicable retirement completes; original Conflict remains unresolved and becomes retryable only afterward. | Repair binaries only; preserve all state. | Block. |
| Closed `IntegrityBlocked`, `BootstrapConflict`, `ProtectedIntegrityBlocked`, or version-blocked attempt | Only an explicitly compatible signed upgrade under a compatibility manifest may proceed after its recovery binary is installed/launchable; preserve all evidence. | Repair binaries only; preserve all data. | Block. |
| Transaction, attempt-draft, or retention retirement operation in progress | Block until verified resume reaches its completed state or safe pre-destruction stop. | Repair binaries only; preserve ledger/quarantine/tombstone. | Block. |
| Terminal transaction or resolution ledger `AuthoritativeAhead`/stable `MirrorMissing` exact-repairable | Block normal lifecycle until exact suffix repair reaches `InSync`. | May repair binaries but not transaction/resolution data; launch Recovery. | Block. |
| Valid `RetirementCompleted` with execution replica absent | Expected; do not gate as missing replica. | Proceed if all other gates pass. | Proceed if all other gates pass. |
| Payload absent with matching retention `Completed` | Expected historical deletion; do not reclassify transaction/replica health. | Proceed if all other gates pass. | Proceed if all other gates pass. |
| Divergent/authoritative-unreadable transaction or resolution journal, `RetirementBlocked`, or `RetentionRecoveryBlocked` | Block with no override except an explicitly compatible signed recovery upgrade. | Repair binaries only; preserve all state. | Block. |
| Resolved Conflict with valid `CommittedResolved`, `ResolutionCompleted`, both stable replica sets `InSync`, and required retirements complete | Proceed. | Proceed. | Proceed. |

Normal upgrade/downgrade requires target reader support for every observed schema and constraint semantics; generic upgrade over unsupported state is blocked. No Ignore/Force/cleanup exists. Stable transaction/resolution history and all State ledgers are preserved byte-for-byte. Referenced live/archive/stage/draft artifacts remain protected unless their durable retirement/retention completion already proves deliberate absence. Installer failure rolls back installer-owned changes only. Clean install discovering preserved state launches Recovery and closes any classifiable pre-initialization attempt before offering a retry.

Test every released schema and every product-state projection across clean install, update, repair, uninstall, reinstall, downgrade, cancellation, crash, and power loss. A blocked action stops before the first binary, shortcut, registration, journal, ledger, or recovery-artifact mutation.

### 13.3 Manual updates

MVP has no update feed query, startup network call, downloader, self-updater, or background agent.

**Help > Check for updates** opens a fixed HTTPS release page in the user's default browser. The user downloads and runs a newer signed installer manually; manual download never bypasses Section 13.2 recovery/version preflight.

An in-app updater is deferred until a separate design proves signed manifests, anti-rollback policy, publisher/transport verification, staged installation, running-process behavior, post-install verification, failure rollback, retained installer, resource integrity, and full recovery lifecycle testing.

### 13.4 Source licensing

Independently reimplement public format facts, algorithms, and general patterns. Do not copy expressive source, comments, tests, resources, or distinctive structure without license approval.

Copying or closely porting GPL-labeled prior editor code is blocked pending explicit review of exact files, license/version, distribution obligations, compatibility, notices/source offer, and reviewer approval.

The release gate includes SBOM, dependency, attribution, and source-provenance review with zero unreviewed copied-source findings.

## 14. Delivery roadmap

### Phase 0 — Safe read-only foundation and write proof

Deliver separate recognition/read-only/common operation-binding types; approved parser profiles; lossless documents and exact Save/Restore/Reconcile derivation; immutable baseline `ObservedParticipantTuple` capture; versioned `ExpectedRoleConstraint` projection/validation; qualified fixed-NTFS field predicates; schema v4 archive-and-stage bootstrap with `StageBuildIntent`/`StageVerified` and prior-schema mapping; stable transaction and resolution authoritative/mirror journals plus temporary execution replica; independent product-state models; complete resolution-attempt closure and draft retirement; crash-safe terminal retirement; backup retention ledger; recovery-aware installer library; fixture/license governance; and the WinUI skeleton.

Exit only when every disposition row passes; unsupported installs remain bounded read-only; import bypass is absent; parser boundaries are deterministic; operation preconditions are proven; every archive and required stage verifies before `Prepared`; no stage exists without prior durable candidate proof; every post-mutation actual observation is durably recorded and constraint-validated before satisfaction; volatile OS metadata uses measured predicates rather than predicted equality; no false Aborted/Verified/RolledBack path exists; replica/attempt/retirement/retention crash tests preserve history; lifecycle readers classify retired/deleted-payload histories; diagnostics/fixtures pass; and no semantic operation is enabled below E3.

### Phase 1 — Small writable MVP

Gold is the only required semantic edit; ordinary inventory quantity is optional.

Deliver complete pages; Gold editor; semantic review/validation/undo; Save, RestoreSlot, both ReconcilePair modes; archive-and-stage bootstrap; three-replica active transaction recovery; stable transaction/resolution history; failed/blocked-attempt closure and guarded retry; terminal/draft retirement; fixed 20-set payload retention; complete en-US/zh-Hans resources; signed recovery-aware installer/manual browser update.

Exit only when Gold passes every fingerprint/language/game cycle; all allowed Save/Restore/Reconcile paths, all-`NoOp` adoption, every pre-initialization/failed/integrity-blocked resolution closure and eligible retry, exact suffix repair, observation/constraint recovery, retirement, payload deletion, restart, and lifecycle preflight succeed end-to-end; changed dependencies disable capability; races block safely; Windows build/MTP/UIA and Ubuntu locked restore pass; unresolved/version/integrity/replica/retirement/retention states preserve data and gate lifecycle correctly; retired execution and deleted payloads are not false health failures; measured Context matrices pass; privacy/license/SBOM have no blockers.

### Phase 1 stretch

HP/MP/SP, Soul Ink, and Easy–Very Hard difficulty may ship only if their independent E3 packets pass before code freeze. Guided level/EXP is deferred by default because of I3 impact. Hell remains unavailable.

Failure of a stretch packet does not delay core MVP and does not weaken the read-only presentation.

### Phase 2 — Constrained domain additions

Evaluate named equipment, outfit, durability, Memory Engram, config, additional-currency capabilities, extra volume profiles, multiply-linked support, and configurable retention one at a time.

Config requires an N-participant transaction design. Each feature requires its own evidence/profile/design, exact dependencies, validators, failure injection, accessibility coverage, and installer regression testing. None is enabled by warning or confirmation.

### Phase 3 — Named progression repairs

Add only specifically named, evidence-backed repairs for story, quests, titles, collections, or related state. There is no generic progression, switch, variable, achievement, map, or event editor.

Each transition requires its own E3 packet and product/achievement-impact approval.

### Phase 4 — Optional peer tools

Compare, richer backup browsing, or batch diagnostics may become peer destinations only if usage research proves frequent independent workflows. Then reconsider persistent `NavigationView` or multi-document `TabView`.

Batch writes, full save-set restore, arbitrary expert editing, and automatic updates are not implied.

## 15. Decision gates, research, and open questions

### 15.1 Fixed decisions

| Decision | MVP direction | Evidence required to change |
|---|---|---|
| Shell | Start + catalog + document editor; no persistent NavigationView | Frequent independent peer workflows. |
| Document count | One open slot | Proven compare/multi-slot need and isolated safety. |
| Recognition/write support | Separate; operation-capable binding is not operation authorization | Reviewed binding redesign. |
| Operation preconditions | Save, RestoreSlot, and ReconcilePair use their own proven preconditions | New safety review and full matrices. |
| Session baseline | Immutable actual observations; Open/Reload/new Restore/Reconcile only; Save never rebases | Separate transaction review. |
| Dispositions | Exact Section 11.7 matrix, including Restore/Reconcile slot NoOp + global Replace and linked all-NoOp adoption | Schema/algorithm/evidence review. |
| Participant evidence | `ObservedParticipantTuple` is complete actual fact; `ExpectedRoleConstraint` is future/projected requirement; hashes never authorize alone | New qualified profile/version. |
| Windows metadata | Deterministic fields/relationships compare exactly; volatile OS-assigned timestamps are recorded and checked by qualified predicates, never predicted or claimed restored | New measured profile and full failure matrix. |
| Archive/stage timing | Archive actual observations precede `Prepared`; every stage has synchronized `StageBuildIntent` before creation and durable `StageVerified` before `Prepared` | New certified write/recovery design. |
| Journal replicas | Stable transaction authoritative/mirror, stable resolution-ledger authoritative/mirror, and required temporary execution replica; terminal/draft retirement is external and durable | New durable replica design. |
| State machines | Bootstrap/stage, logical, participant, transaction/resolution replica, execution, resolution target/attempt/draft, backup, transaction-retirement, and retention projections remain distinct | Backward-compatible reviewed product-state redesign. |
| Conflict resolution | Immutable Conflict, exactly one closure per readable/classifiable attempt including pre-initialization and integrity block, and success-only durable `ResolutionCompleted` | Backward-compatible reviewed schema. |
| Retention | Fixed 20-set payload selection after terminal retirement; State journals/ledgers and attempt drafts are never payload | Later previewed configurable policy/compaction design. |
| Writable volume | Qualified local fixed NTFS, stable IDs/flush/ReplaceFileW, single-link, and per-field constraint semantics | New profile/full failure matrix. |
| Current pair | Congruent before editing/Save/adoption; Restore/global repair use their own candidate preconditions | No warning bypass. |
| Global handling | Current lossless global; selected-entry leaves only; historical global excluded | Separate full-set design. |
| Standalone files | Diagnostics/export-only; exact-live reopen only | Imported replacement unsupported. |
| Context mode control | Measured SelectorBar capability with visible-label ComboBox fallback, independent of breakpoint | Future standard reflow control with no clipping/selection/accessibility regression. |
| Raw editing/Hell | Unavailable | Separate E3/product safety approval. |
| Language | Windows preference; en-US neutral and zh-Hans shipped | Future localization decision. |
| Packaging/updates | Signed per-user Inno; manual browser update with full state preflight | Validated deployment/updater redesign. |
| Game art | Never shipped | No exception. |

### 15.2 Tracked research tasks

Open research is never a release exception. Each task has an owner, evidence artifact, and dependent capability.

1. Catalog save-path relocation variants and safe read-only fallback cases.
2. Maintain preview derivation/congruence evidence for every supported entry state.
3. Recalibrate parser profiles for parser/runtime/fingerprint/minimum-hardware changes.
4. Determine localized-compatible database/plugin semantic probes.
5. Qualify or reject ordinary inventory families and per-item limits.
6. Research HP/MP/SP, difficulty, Soul Ink, level/EXP, equipment, Memory Engrams, progression, and mature fields as independent packets.
7. Validate privacy-safe preview/recent-item fields.
8. Design N-participant config and destructive full-set transactions only as separate later projects.
9. Evaluate additional filesystem/remote/removable and multiply-linked profiles only after MVP.
10. Design configurable retention preview/caps and any State-journal/ledger compaction only after fixed-policy/append-only evidence and separate certification; never weaken ownership, terminal history, or crash recovery.
11. During Phase 0, measure each required NTFS/Windows build combination for create, `ReplaceFileW`, rename, rollback, flush, identity relationships, link/reparse/security/attribute behavior, and actual creation/last-write/`ChangeTime` ranges or relationships. Classify every field as equality, relationship, predicate, or unsupported; do not infer unmeasured behavior.

Steam synchronization tests may document observed local file changes and user-visible Steam behavior. They do not create remote Cloud visibility or an automatic conflict-resolution capability.

### 15.3 Open questions that do not relax MVP gates

- Whether Windows build 17763 remains the support floor after minimum-machine and metadata-profile calibration; if it is not measured, raise the floor before release.
- Whether additional language resources, qualified volume profiles, pinned backups, retention controls, or certified State-history compaction justify post-MVP scope; archive/stage timing, observation/constraint separation, state separation, Aborted/Verified/RolledBack invariants, and terminal evidence are not open questions.
- Whether a recovery-preserving maintenance uninstall mode is supportable; normal uninstall remains blocked for unresolved, open-attempt, integrity-blocked, or unretired recovery.

## 16. Product-plan definition of done

Implementation may begin with Phase 0 when the team accepts:

- separate recognition, bounded read-only, common operation binding, and operation-specific plan validation;
- immutable baselines and exact Save/RestoreSlot/ReconcilePair preconditions/dispositions;
- lossless slot/current-global handling and no imported/historical-global shortcut;
- archive-first creation/flush/reopen/complete actual observation of baseline payload and immutable ownership manifest before `Prepared`;
- durable `StageBuildIntent` before every stage creation and durable `StageVerified` actual observation before `Prepared`, with deterministic bootstrap recovery for every crash boundary;
- complete `ObservedParticipantTuple` records for actual roles and separate `ExpectedRoleConstraint` sets for future/projected roles, with no predicted volatile timestamp or future final tuple;
- durable post-forward/post-rollback observation and constraint-validation records before satisfaction/terminal transitions; `RolledBack` uses qualified restoration constraints plus actual restored observations, not original timestamp equality;
- schema v4/prior-schema readers and distinct bootstrap/stage, logical, participant, transaction/resolution replica, execution, resolution target/attempt/draft, backup, transaction-retirement, and retention machines;
- immutable terminality; no `Verified -> Aborted|RollbackPending|RolledBack`; no `Aborted` after any invocation intent/evidence; all-`NoOp` `Verified -> Committed`;
- stable app-owned transaction and resolution authoritative/mirror history separate from deletable payloads, exact suffix repair, crash-safe execution/draft retirement, and durable terminal evidence after payload deletion;
- exactly one durable closure per readable/classifiable resolution attempt, including `PreInitializationCanceled`, `PreInitializationFailed`, and `IntegrityBlocked`; failed/blocked closures leave the original Conflict unresolved and retry only after explicit prerequisites;
- explicit archive/stage/draft disposition/protection for cancellation, pre-Prepared failure, Aborted, RolledBack, Conflict, resolution, and version/integrity block;
- fixed 20-set payload retention through the crash-resumable retention ledger, including rename-gap recovery;
- measured Context SelectorBar/ComboBox capability with shared selection/focus/UIA state;
- qualified local fixed-NTFS/single-link profile, measured per-field equality/relationship/predicate rules, parser/evidence/privacy gates, resources, packaging, and recovery-aware lifecycle;
- Gold-first writable MVP with every other semantic domain independently gated.

A production release additionally requires approved parser/profile measurements; every operation/disposition/bootstrap/state/observation/constraint/replica/attempt/draft/retirement/retention crash vector; controlled game tests; complete responsive/Context/UIA matrices; bilingual publish/install verification; and every applicable gate in Sections 6, 8, 9, 11, 12, and 13. There is no hash-only authorization, predicted future metadata tuple, stage without prior durable proof, satisfaction before durable actual observation, false Aborted/RolledBack path, classifiable attempt left open, integrity-blocked attempt presented as retryable/resolved, retired execution false-positive, unresolved Conflict falsely resolved, terminal history lost to payload deletion, or deletion without valid retirement/retention proof.

## 17. Revision closure

This v5 plan integrates the prior product/WinUI/transaction reviews and the final metadata, stage-bootstrap, and resolution-attempt certification findings as one normative design. The selected protocol separates actual observations from future constraints, requires durable candidate proof before stage creation, closes every readable/classifiable resolution attempt, and keeps bootstrap, logical, replica, attempt, backup, retirement, and retention lifecycles independent. Obsolete claims of exact future Windows timestamp tuples or exact timestamp restoration have been removed.

| Final finding | Normative closure |
|---|---|
| Impossible exact future metadata tuples | Sections 1.1, 3.2, 4.2, 6.4–6.5, 8.9–8.10, 10.3–10.5, 11.1–11.4/11.7–11.10, 12.3–12.5, 14–16 separate `ObservedParticipantTuple` from `ExpectedRoleConstraint`, durably record actual mutation results, and make volatile timestamps profile predicates. |
| Durable candidate proof before stage creation | Sections 1.1, 6.4, 10.4, 11.3–11.8, 12.3, 14–16 require synchronized `StageBuildIntent`, create-new/flush/reopen validation, `StageVerified`, `Prepared` references, and protected `BootstrapConflict` for unknown/tampered stages. |
| Complete resolution-attempt bootstrap closure | Sections 6.4, 8.9–8.10, 11.1/11.6/11.10–11.15, 12.3–12.5, 13.2, 14–16 define `PreInitializationCanceled`, `PreInitializationFailed`, `IntegrityBlocked`, draft-artifact retirement/protection, startup closure, retry eligibility, and lifecycle gates. |
| Holistic independent state models | Sections 8.9, 11.1/11.5–11.15, 12.3–12.4, 13.2, 14–16 keep bootstrap/stage, logical, participant, transaction/resolution replicas, execution, attempt/draft, backup, retirement, and retention projections distinct. |
| Operation-specific preconditions and complete dispositions | Sections 3.2, 4.2, 5.2, 6.3–6.4, 7.8–7.9, 10.4, 11.2/11.7–11.8, 12.3. |
| Slot NoOp + global Replace and all-NoOp resolution | Sections 5.2, 7.8–7.9, 11.7–11.8; all-NoOp writes the full durable resolution sequence. |
| Stable journals and deliberate execution/draft retirement | Sections 1.1, 8.9, 10.3–10.4, 11.3/11.6/11.10–11.15, 12.3, 13.2. |
| Backup disposition and crash-resumable retention | Sections 6.4, 11.12/11.14–11.15, 12.3, 13.2, 14–16. |
| Cross-version terminal history after payload deletion | Sections 11.1/11.6/11.10/11.14–11.15, 12.3, 13.2, 16. |
| Measured Context mode control | Sections 8.2/8.6–8.11, 12.4, 14–16. |
| Privacy | No private save values, paths, hashes, IDs, or proprietary assets are added to UI, logs, exported artifacts, fixtures, or this plan. |
