# Atlas V0 A2 Intake and Safety Plan

**Status:** Active subject to the post-patch baseline correction gate

**Increment:** A2 - Intake and Safety Harness

**Implementation language:** C# on the repository-pinned .NET 10 SDK

**Governing baseline:** `atlas-v0-execution-plan.md`, Increment A2

**Implementation diff base:** The verified A2 plan-review record commit

> **Post-patch baseline correction**
> The approved source is the observed installed file tree after an off-tree patch was applied.
> `atlas-v0-a2-post-patch-baseline-correction.md` supersedes the inaccurate unmodified-installation
> wording and the later package-provenance amendment. The patch package and installation history are
> descriptive context, not intake identity or authorization evidence. The original A2 source remains
> unchanged, but private discovery requires the correction plan review and release gate.

## 1. Outcome

A2 creates a reusable, private intake path that:

1. discovers the complete approved save and installed-definition scopes before content access;
2. requires the project leader to review and approve exact private manifest bytes;
3. creates research snapshots under a trusted-local-filesystem profile;
4. proves per-file point-in-time byte fidelity without decoding content;
5. prevents literal dynamic locator keys from reaching canonical records; and
6. reports lifecycle eligibility without deleting retained research evidence.

A2 produces private research snapshots only. It does not establish E2 or E3 authority, qualify a
live-save writer, prove one simultaneous corpus point in time, or defend against a hostile local
actor or adversarial filesystem.

## 2. Approved trust-profile amendment

The project leader approved `trusted-local-filesystem/v1` instead of full Windows file-identity
proof.

The profile trusts:

- the user-controlled local Windows machine;
- fixed local drives selected by the operator;
- the approved game installation and Git-ignored private workspace;
- the absence of a malicious actor racing path or link changes during an operation; and
- normal Windows, .NET, and local-filesystem behavior.

The profile does not collect volume identifiers, stable file identifiers, link counts, final kernel
paths, reparse tags, or native interop evidence. It makes no source-identity or hard-link claim.

A qualified file means only that:

- its source was an ordinary file under an approved root when checked;
- the source handle denied concurrent write and delete sharing while that file was copied;
- the destination was newly created under the controlled workspace;
- the destination length and SHA-256 matched bytes read from that held source handle; and
- the normal operation completed with the required private records.

This is a per-file point-in-time guarantee. A source may change after its handle closes, so A2 does
not claim that every source represents one simultaneous state. The read-only destination attribute
is advisory, not immutable. Every later scanner must rehash a snapshot against private provenance
immediately before use.

Visible path indirection, outside-root paths, existing destinations, source changes while a handle
is held, directory-entry changes during the operation, copy mismatches, unapproved manifests, and
unclassified entries fail closed. Malicious post-check races remain an accepted residual risk.

This amendment conditionally supersedes only A0's forward-looking references to an A2 identity
harness and requalification of the preservation snapshot. It becomes effective only through the
verified A2 plan-review record. The pre-A2 snapshot remains permanently
`preservation-unqualified`; A2 creates new snapshots and never promotes it.

The finite A0 scope, terminal accounting, privacy, redaction, lifecycle, and approval rules remain
binding. A changed root, denominator, selection rule, or terminal policy reopens A0 and must pass
its approval and release gate before A2 resumes.

## 3. Entry conditions

A2 implementation may begin only when:

- the exact A1 release record
  `cdde3a0427765c9f2b969e3e678550e4f7d78edb` is reachable;
- that commit changes only `docs/.copilot/reviews/atlas-v0-a1-release-gate.md` from its reviewed
  implementation parent;
- this plan and its baseline amendments are committed and pushed;
- a fresh independent reviewer reports `No findings` for the exact plan candidate;
- the plan-review record is committed and pushed as the only child change;
- that record passes its parent, path, content, and upstream checks; and
- the tracked worktree is clean.

Private discovery requires the source-safety gate in section 17. Copying additionally requires the
human approval gate in section 10.

## 4. Scope

### In scope

- Existing Atlas library, CLI, and test projects.
- Metadata-only discovery for the two approved save-root roles.
- Metadata-only discovery for the frozen installed-definition rules.
- Existing `atlas-intake/v2` and `atlas-private-inventory/v1` contracts.
- New private root-map, intake-state, copy-plan, copy-receipt, and cleanup-report contracts.
- Strict private request parsing with source-generated `System.Text.Json` metadata.
- Trusted-local copy creation and private fidelity evidence.
- Deterministic deny-by-default locator-key aliases.
- Non-deleting private lifecycle preflight.
- Synthetic, repository-safe automated tests.
- One human-operated private A2 run after exact manifest approval.

### Out of scope

- Decoding, decompressing, or semantically scanning saves or definitions.
- Editing, replacing, renaming, deleting, or locking a live source.
- One simultaneous corpus snapshot.
- Full Windows identity, volume, link-count, final-path, or reparse-tag proof.
- Native interop, CsWin32, a Windows-specific target framework, or a new project.
- Network access, telemetry, private-data logging, or exception-detail output.
- WinUI, dependency injection, Generic Host, a database, or an Agent runtime.
- Promoting or scanning the pre-A2 preservation snapshot.
- Deleting retained corpus evidence or implementing final deletion in A2.
- Crash-atomic updates across multiple files or directories.
- Any real-save write or compatibility claim.
- A second intake survey or reusable predecessor chain; either requires a new approved plan.

Final deletion belongs to A8, when last-use authority exists. A2 only proves that lifecycle
eligibility can be computed without mutation.

## 5. Project and dependency boundaries

A2 preserves the existing three-project graph:

- `Hcoona.CelesphoniaModifier.Atlas` owns contracts, policy, and file operations.
- `Hcoona.CelesphoniaModifier.Atlas.Cli` owns argument matching, fixed process diagnostics,
  console streams, and dispatch.
- `Hcoona.CelesphoniaModifier.Atlas.Tests` owns direct and apphost tests.

Production projects retain zero project-local package references. A2 uses target-framework BCL
APIs, including `System.Text.Json` and `System.Security.Cryptography`. It does not change Central
Package Management, lock files, target frameworks, root traversal, or telemetry controls.

The library does not read console or environment state, start processes, access the network, depend
on the CLI, or infer an installed-game location. Every root and operation parameter comes from an
explicit private request.

Small internal file-operation seams may default directly to BCL behavior and permit deterministic
fault tests. They are not public abstractions, a general filesystem layer, or dependency injection.

## 6. Private request contracts

Every command accepts one non-empty request-file path. The path may be private and is never echoed.
The operator stores requests under the Git-ignored workspace and invokes them locally.

Every request is one UTF-8 JSON object with:

- the exact command-specific version below;
- all listed properties exactly once;
- no unknown properties, comments, trailing comma, or trailing JSON value;
- a maximum JSON depth of 32;
- explicit absolute paths, versions, revisions, and expected private hashes; and
- no value derived from the current directory, profile, registry, Steam, or environment.

Duplicate properties are rejected before deserialization with `Utf8JsonReader`. A malformed or
contract-invalid request is an argument-validation failure.

The exact command contracts are:

- `intake-discover`, version `atlas-intake-discovery-request/v1`: `schemaVersion`,
  `surveyAlias`, `projectRoot`, `workspaceRoot`, `baselineManifestPath`, `expectedBaselineSha256`,
  `expectedBaselineRevision`, `nextManifestRevision`, `manifestRevisionDirectory`, `saveRoots`,
  `definitionRoot`, `gameExecutablePath`, `sourceRootMapOutputPath`, `inventoryPath`,
  `expectedInventorySha256`, `inventoryBackupPath`, `copyPlanOutputPath`, `stateRevisionDirectory`,
  `expectedSteamAppId`, and `expectedBuildId`;
- `intake-confirm`, version `atlas-intake-confirmation-request/v1`: `schemaVersion`,
  `surveyAlias`, `projectRoot`, `workspaceRoot`, `discoveredStatePath`,
  `expectedDiscoveredStateSha256`,
  `pendingManifestPath`, `sourceRootMapPath`, `copyPlanPath`, `decisionCommit`,
  `manifestRevisionDirectory`, `stateRevisionDirectory`, `inventoryPath`,
  `expectedInventorySha256`, and `inventoryBackupPath`;
- `intake-copy`, version `atlas-intake-copy-request/v1`: `schemaVersion`, `surveyAlias`,
  `projectRoot`, `workspaceRoot`, `approvedStatePath`, `expectedApprovedStateSha256`,
  `approvedManifestPath`,
  `sourceRootMapPath`, `copyPlanPath`, `decisionCommit`, `incompleteCopyPath`, `finalCopyPath`,
  `stateRevisionDirectory`, `inventoryPath`, `expectedInventorySha256`, and
  `inventoryBackupPath`; and
- `cleanup-preflight`, version `atlas-cleanup-preflight-request/v1`: `schemaVersion`,
  `surveyAlias`, `projectRoot`, `workspaceRoot`, `qualifiedStatePath`,
  `expectedQualifiedStateSha256`,
  `stateRevisionDirectory`, `inventoryPath`, `expectedInventorySha256`, `inventoryBackupPath`,
  `proposedMilestone`, and `reportOutputPath`.

`saveRoots` contains exactly two objects, one for each approved A0 location role. Each object has
only `locationRole` and `path`. Other path properties are strings. Revisions are positive integers.
Expected hashes are 64 lowercase hexadecimal characters. `decisionCommit` is a 40-character
lowercase Git object identifier. `proposedMilestone` is an existing inventory milestone and is
advisory because preflight cannot authorize deletion.

`expectedSteamAppId` is exactly `1786790`; `expectedBuildId` is exactly `13624401`. They are frozen
descriptive labels for the A0 starting point and are bound through every state and receipt. They do
not claim that Steam or a package can reconstruct the observed installed tree.

Manifest revisions are create-new files named
`corpus-intake-manifest.rNNNNNN.json` in one survey-local private revision directory. Revision
numbers increase by one without reuse. A2 accepts released A0 revision 3 as its only baseline,
publishes pending revision 4, and publishes approved revision 5. Any other predecessor or additional
revision stops A2. An existing target is handled only by the recovery matrix in section 12.

Discovery preserves every baseline `rootAlias` by location role and every `sourceAlias` by its
root-or-group identity plus relative path. New aliases append after the greatest existing ordinal
in deterministic ordinal path order. An existing locator may never receive a new alias.

The source-root map uses `atlas-source-root-map/v1` and contains `schemaVersion`, `surveyAlias`,
discovery revision, public application and build identifiers, two `rootAlias`/absolute-path save
bindings, one absolute definition-root binding, and the absolute game-executable path.

Discovery also publishes `atlas-copy-plan/v1`. The plan contains `schemaVersion`, survey and
discovery revisions, and one entry per included manifest source. Each entry contains `sourceAlias`,
reserved `destinationArtifactAlias`, artifact class, and canonical destination-relative path.

Artifact aliases use one monotonic cursor above the greatest ordinal in either the bound inventory
or any copy-plan reservation. Discovery reserves destinations after allocating its control aliases,
in manifest `sourceAlias` order. Confirmation and later phases advance beyond both inventoried
aliases and reservations. Destination aliases enter the inventory only on successful copy
publication.

Save destinations are `saves/<sourceAlias>.rpgsave`. Definition destinations are
`definitions/<sourceAlias><lowercase-source-extension>`. Source aliases are unique, allowed
extensions are frozen by A0, and any duplicate destination is a safety failure.

The one-shot state sequence uses create-new files:

- `atlas-intake-state/v1`, revision 1, phase `discovered`;
- revision 2, phase `approved`;
- revision 3, phase `qualified`; and
- revision 4, phase `preflighted`.

Each state contains its predecessor-state digest, public identifiers, inventory digest, and
role-to-document digest bindings. Revision 1 binds the A0 baseline, pending manifest,
source-root map, copy plan, and discovery inventory. Revision 2 binds revision 1, approved
manifest, approval-record commit, and confirmation inventory. Revision 3 binds revision 2, copy
receipt, final inventory, and final relative snapshot root. Revision 4 binds revision 3, cleanup
report, and final A2 inventory. Revision 1 has no predecessor.

Every state also binds each retained request and inventory backup created by its phase. Each
artifact binding contains artifact alias, role, survey-relative path, and SHA-256. State filenames
are exactly `intake/states/atlas-intake-state.r000001.json` through
`intake/states/atlas-intake-state.r000004.json`.

Other canonical survey-relative paths are:

- `intake/corpus-intake-manifest.json` for released A0 revision 3;
- `intake/manifest-revisions/corpus-intake-manifest.r000004.json` and `.r000005.json`;
- `intake/source-root-map.json` and `intake/copy-plan.json`;
- `intake/requests/{discover,confirm,copy,cleanup-preflight}.json`;
- `intake/inventory-backups/private-artifact-inventory.<phase>.json`, where `<phase>` is
  `discovered`, `approved`, `qualified`, or `preflighted`;
- `copies/snapshot-a2-000001.incomplete` and `copies/snapshot-a2-000001`;
- `copies/snapshot-a2-000001/copy-receipt.json`; and
- `cleanup/a2-preflight.json`.

Every request path must equal its corresponding canonical path after normalization. The fixed state
paths and state artifact bindings let later increments locate and rehash retained evidence without
adding paths to the inventory schema.

`projectRoot` is the repository root. `workspaceRoot` must equal the full-path result of joining it
with `src\private\app\celesphonia-modifier\.private\atlas-v0\<surveyAlias>`. The tool also requires
the parent `.private\.gitignore` to be an ordinary non-reparse file whose effective rules include
`*` and `!.gitignore`. Every request tests this invariant before creating output.

Requests and operational outputs are private. Their C# types and synthetic examples are
repository-safe; no real request or path enters Git.

## 7. Path policy

Every request path must:

1. use an absolute drive-letter DOS path such as `C:\...`;
2. have no UNC or device prefix and no colon after the drive designator;
3. normalize through `Path.GetFullPath` to the same drive;
4. resolve to a ready `DriveInfo` whose `DriveType` is `Fixed`; and
5. satisfy ordinal-ignore-case, separator-aware containment under its declared approved root.

Containment compares full paths after trimming trailing separators and then appending exactly one
separator to the root. Prefix-like siblings do not match. Source roots may equal their declared
roots; child outputs may not equal the workspace root.

Before an operation uses a path, it reads `FileAttributes` for every existing component from the
drive root through the leaf. Any `ReparsePoint` fails closed. A required source file must not have
`Directory`, `Device`, or `ReparsePoint`. Missing components are allowed only where that operation
explicitly creates a new output.

These BCL checks reject ordinary visible junctions and symbolic links. They do not close a hostile
time-of-check/time-of-use race, which remains accepted by section 2.

All source roots are pairwise disjoint from the workspace: neither may contain the other. On a fresh
copy attempt, the incomplete and final paths are nonexistent siblings under one survey-local
`copies` parent. On restart, the recovery matrix is inspected before fresh-attempt nonexistence
checks. The final path must be absent immediately before renaming a complete `.incomplete`
directory.

The incomplete leaf is the final leaf plus `.incomplete`. Every manifest relative path is canonical,
contains no root, drive, empty segment, `.` segment, or `..` segment, and combines under exactly one
bound source root.

## 8. CLI contract

A2 adds:

```text
celesphonia-atlas intake-discover <request-file>
celesphonia-atlas intake-confirm <request-file>
celesphonia-atlas intake-copy <request-file>
celesphonia-atlas cleanup-preflight <request-file>
```

Each command accepts `-h` or `--help` in place of `<request-file>`. Existing A1 forms remain
accepted. Matching is ordinal and case-sensitive. No abbreviation, response file, directive,
wildcard, expansion, implicit path, or extra argument is accepted.

Command help is exactly:

```text
Usage: celesphonia-atlas <command> <request-file>

Options:
  -h, --help  Show help.
```

The global help is exactly this LF-terminated text:

```text
Usage:
  celesphonia-atlas empty-survey
  celesphonia-atlas intake-discover <request-file>
  celesphonia-atlas intake-confirm <request-file>
  celesphonia-atlas intake-copy <request-file>
  celesphonia-atlas cleanup-preflight <request-file>

Commands:
  empty-survey       Write a deterministic empty Atlas survey.
  intake-discover    Discover the approved Atlas intake scope.
  intake-confirm     Confirm an approved Atlas intake manifest.
  intake-copy        Create qualified Atlas research snapshots.
  cleanup-preflight  Report private-artifact cleanup eligibility.

Options:
  -h, --help  Show help.
```

`empty-survey -h` and `empty-survey --help` continue to emit the exact A1 section 9.1 help bytes.
Only root help changes to the global A2 text above.

Success output is one fixed LF-terminated line:

| Command             | Standard output                  |
| ------------------- | -------------------------------- |
| `intake-discover`   | `Intake discovery completed.`    |
| `intake-confirm`    | `Intake confirmation completed.` |
| `intake-copy`       | `Intake copy completed.`         |
| `cleanup-preflight` | `Cleanup preflight completed.`   |

Terminal standard-error diagnostics and exit codes are:

| Code | Diagnostic             |
| ---: | ---------------------- |
|    1 | `Unexpected failure.`  |
|    2 | `Invalid arguments.`   |
|    3 | `Operation canceled.`  |
|    4 | `I/O failure.`         |
|    5 | `Safety check failed.` |
|    6 | `Approval required.`   |

Missing or inaccessible requests, sharing violations, and failed output operations are I/O
failures. Invalid JSON, schema versions, properties, values, and path syntax are argument failures.
Unapproved or superseded manifests are approval failures. Profile, containment, reparse,
classification, source-change, and fidelity refusals are safety failures.

A2 preserves A1 section 9.3 precedence. Argument and help handling precede cancellation and file
access. A valid operation receives the caller token. Caller-related cancellation maps to 3;
foreign or unsolicited cancellation maps to 1. Diagnostic-write failure maps to 4. Operation
results 5 and 6 are handled before the generic unexpected-failure mapping. Streams never receive a
private path, source name, hash, value, count, exception message, or stack trace.

## 9. Discovery

`intake-discover` reads metadata only and never opens source contents. It:

1. validates the request, baseline manifest digest, path policy, A0 roots, and next revision;
2. enumerates every immediate entry in both save roots;
3. applies the exact A0 save-role and terminal-decision rules;
4. enumerates the complete installed-definition universe under the frozen A0 rules;
5. terminally classifies every candidate;
6. preserves or assigns aliases under section 6;
7. reconciles all root, group, included, excluded, unsupported, and unreadable counts;
8. publishes the source-root map, copy plan, and pending `atlas-intake/v2` revision;
9. safely updates the inventory with discovery and control-artifact entries; and
10. publishes intake-state revision 1 last as the authoritative discovery-completion signal.

`steam_autocloud.vdf` is always `exclude-steam-autocloud`. A reparse-backed root or entry is
`unsupported` and stops A2. Any root, denominator, selection-rule, or terminal-policy difference
reopens A0; A2 cannot approve a narrowing itself.

## 10. Human-operated private approval

The pending manifest and private binding documents are never supplied to Copilot or a subagent. The
project leader performs the private phase:

1. creates the private request without placing its path in an Agent transcript;
2. confirms that the requested roots are the intended observed installation and save locations;
3. runs the reviewed source candidate from a clean checkout;
4. opens the exact pending manifest, source-root map, copy plan, and discovered state in a local
   editor;
5. verifies their revisions, roots, mappings, decisions, counts, bindings, and private SHA-256
   values;
6. reports only approved repository-safe aggregate counts and contract differences;
7. explicitly approves or rejects that survey alias and revision;
8. supplies the discovered-state SHA-256 only to the local confirmation request; and
9. runs confirmation only after the approval record commit is pushed.

The approval record at `../reviews/atlas-v0-a2-intake-approval.md` contains:

- survey alias and pending manifest revision;
- safe aggregate counts and public game-build identifiers;
- the public game-build identifiers as descriptive A0 labels;
- `trusted-local-filesystem/v1` and its accepted residual risks;
- the exact source-safety candidate commit and tree;
- the project leader's decision; and
- an explicit statement that no private path, name, hash, value, or source text is recorded.

The record is independently reviewed as an exact staged blob, committed unchanged as the only child
of the approved source-safety record, pushed, and verified for parent, path, content, and upstream.

`intake-confirm` verifies state revision 1 and every document digest it binds. It writes manifest
revision 5, differing from revision 4 only in revision and confirmation: `approved`,
`project-leader`, and the exact approval-record commit. It updates the control-artifact inventory
and publishes state revision 2 last. Copy trusts only revision 2 and revalidates every referenced
document.

This is a trusted human-operated gate, not a claim that the CLI proves Git authority. The operator
privately hashes state revision 2 and places only that digest in the copy request. State revision 2
binds the approved-manifest digest; the copy command rehashes the manifest and compares it with that
binding.

During copy, the tool hashes `Game.exe` under the same held-handle stability rules as copied files.
Together with every included definition hash and the public identifiers, this establishes the
game-content portion of A0 fingerprint evidence. Atlas tool, schema, redaction-policy, and
configuration digests remain outside A2 and are added to the final Atlas snapshot under A8.

This one-shot A2 run establishes the first qualified game-content fingerprint. A changed root-role
set, selected relative-path corpus, denominator, selection rule, or terminal policy reopens A0.
Byte changes within the same approved finite corpus are captured by the new private fingerprint and
do not by themselves reopen A0. Comparing a later installation requires a separately approved
intake plan. No private fingerprint enters the approval record or process output.

## 11. Copy and qualification

`intake-copy` accepts only state revision 2. Manifest, source-root-map, copy-plan, survey, decision,
revision, public identifier, and digest values must match that state. The root map supplies the only
absolute source paths and the copy plan supplies the only artifact and destination mapping; no
ambient lookup or array-order pairing is permitted.

Before content access, it re-enumerates every source directory and requires exact agreement with
the approved manifest. For each included source, it:

1. applies the path policy and confirms the manifest's relative path, entry type, reparse status,
   classification, and complete directory census still match;
2. opens the source with `FileAccess.Read` and `FileShare.Read`;
3. captures length and last-write metadata from the held source;
4. creates a destination with `FileMode.CreateNew` and `FileShare.None`;
5. streams bytes once while computing private SHA-256;
6. calls `FileStream.Flush(flushToDisk: true)` before closing the destination;
7. independently reopens and hashes the destination;
8. confirms destination length and hash match the held source bytes;
9. confirms the source handle length and path metadata did not change while held; and
10. marks the destination read-only.

The source handle closes after its own file completes. The operation then re-enumerates every source
directory and requires unchanged entry sets. Later source-content mutation is an accepted
trusted-local risk and does not alter the completed per-file snapshot.

Copies and a staged copy receipt are built inside one owned, create-new `.incomplete` directory.
After all checks pass, `Directory.Move` performs an ordinary same-volume rename to the nonexistent
sibling final directory. The canonical inventory is safely replaced with a request-bound backup,
and the validated staged receipt is published as the create-new `copy-receipt.json` completion
signal in the final directory.

A snapshot is `a2-qualified` only when valid state revision 3 validates its final copy receipt and
canonical inventory. State revision 3 is the sole qualification signal. Any `a2-qualified`
inventory property and receipt that exist before state revision 3 are provisional prerequisites and
must not be consumed as qualification. The operation does not claim an atomic transaction across
files. If state revision 3 exists and validates, persisted qualification is authoritative even when
interruption or standard-output failure prevents a success message.

A partial or mismatched `.incomplete` directory requires human inspection and a separately approved
fresh run or targeted removal. A complete request-owned directory with matching captured receipt is
recoverable only under the matrix in section 12. A2 never guesses or recursively removes an
unexpected final directory.

On an ordinary pre-rename failure, cleanup attempts to remove only the request-bound, owned,
non-reparse `.incomplete` directory. If removal fails, the command reports failure and leaves the
directory unusable for human inspection. It never removes a live source, prior snapshot, approved
manifest, canonical evidence, or unexpected final directory.

Private provenance records `trusted-local-filesystem/v1`, source-relative aliases, source metadata,
destination-relative aliases, lengths, and SHA-256 values. Definition copies use the same evidence.
The inventory uses `a2-qualified` only for save-copy entries because the existing schema limits that
property to save copies.

The copy receipt uses `atlas-copy-receipt/v1`. It contains the survey alias, receipt alias, profile,
copy-request digest, approved-state digest, approved manifest digest, source-root-map digest,
copy-plan digest, decision reference, approved-manifest artifact alias, final relative copy root,
public identifiers, private game-executable SHA-256, exact save and definition counts, and one entry
per copy-plan source. Each entry contains destination artifact alias, source alias, artifact class,
destination-relative path, source length, source-last-write UTC value, and SHA-256.

Receipt, qualified state, and inventory agree only when every copy-plan and receipt entry has
exactly one inventory entry with the same destination artifact alias and class, status `present`,
the approved manifest revision-5 artifact alias as its only lineage member, and verification method
`trusted-local-filesystem/v1;receipt:<receipt-alias>`; save copies additionally have
`a2-qualified`. No destination entry exists before copy. Successful copy adds each destination as
`present`, adds only the control entries declared below, and changes no unrelated entry. Definitions
are qualified by agreement, not by the save-only inventory property.

## 12. Private document publication

Every create-new private JSON output uses one publication procedure:

1. create a request-bound staging file in the final file's directory;
2. serialize the complete UTF-8 document, flush it to disk, and close it;
3. reopen it, parse it strictly, validate all contract invariants, and compute SHA-256;
4. move it to a nonexistent final filename; and
5. retain no success claim if any step fails.

A staging file is never a revision or completion signal. Deterministic control-document staging may
be validated and promoted by the same request. A staged copy receipt is captured point-in-time
evidence: repair may validate and promote those exact bytes but never regenerate them. A final
create-new revision is complete bytes but remains unusable unless all state and digest checks pass.

The revision-managed canonical inventory uses a different bounded procedure because its v1 schema
has no revision field. The request binds the current inventory digest and a nonexistent unique
backup path. The operation writes and validates a staging replacement, calls `File.Replace` with
that backup path, then reopens and validates the canonical file. It makes no crash-durability claim.
If the canonical file or expected backup state is invalid, all tools refuse both and require human
recovery. Backups remain inventoried private evidence through A2 release.

Each phase publishes its state revision last. Re-running the same phase request before that state
exists is idempotent: the command validates each existing final output against deterministically
regenerated bytes, creates only missing outputs, recognizes an already completed inventory replace
when the canonical digest equals the calculated replacement and the backup digest equals the
request-bound prior inventory, and then publishes the state. Any other digest is a safety refusal.
An existing valid phase state returns success without writing. A later phase without its required
predecessor state is an approval refusal.

The recovery matrix is:

- valid state for the requested or a later phase: return that command's fixed success without write;
- matching deterministic control staging or final output, with no phase state: validate and
  continue;
- missing deterministic control output, with no phase state: regenerate it from the same request;
- complete request-owned `.incomplete` directory with every planned copy and staged receipt:
  validate destination bytes and staged receipt, rename the directory to final, replace inventory,
  publish the unchanged receipt, then publish state revision 3 without rereading sources;
- final directory with every planned copy and staged receipt, before inventory replacement:
  validate destination bytes, then continue without rereading sources;
- replaced inventory before receipt publication: require canonical SHA-256 to equal the calculated
  replacement digest and backup SHA-256 to equal the request-bound prior inventory digest, then
  publish the captured staged receipt;
- published receipt before state revision 3: validate receipt, copies, replacement inventory, and
  prior-inventory backup, then publish state revision 3;
- incomplete copy set, missing receipt evidence, or missing final copy artifact: safety refusal
  pending exact human removal and a fresh copy run;
- mismatching staging file, final output, inventory, backup, or state: safety refusal;
- unowned staging or incomplete artifact: safety refusal with no removal; and
- invocation whose required earlier state is absent: approval refusal.

Staging names are deterministic canonical output names plus `.<phase>.staging`. Repair never
overwrites, renames, or deletes an existing final. The exact same request bytes are required. After
a later state exists, an earlier command recognizes completion through the state chain and does not
recreate historical outputs.

The five new private schemas are:

- `atlas-source-root-map/v1`;
- `atlas-intake-state/v1`;
- `atlas-copy-plan/v1`;
- `atlas-copy-receipt/v1`;
- `atlas-cleanup-preflight/v1`.

Their tracked JSON Schemas define closed local document shapes, property types, canonical relative
path syntax, and alias patterns. BCL-only C# validators enforce alias uniqueness, census,
predecessors, calculated digests, lineage, and every cross-field or cross-document invariant.

The cleanup report contains its schema version, survey alias, bound inventory digest, proposed
milestone, and one result per artifact.

Every retained A2 artifact receives an inventory entry in the state-bound inventory:

- manifest revisions: `live-discovery`, A2, `retain-private`;
- request documents: `private-evidence`, A8, `delete`;
- root map, copy plan, intake state, receipt, and inventory backups: `private-provenance`, A8,
  `retain-private`;
- save snapshots: `save-copy`, A8, `delete`;
- definition snapshots: `definition-copy`, A6, `delete`; and
- cleanup-preflight report: `cleanup-record`, A8, `retain-private`.

Aliases allocate monotonically in operation order. Control artifacts name their direct predecessor
or input aliases in `lineageAliases`. New A2 entries use `custodianRole: project-leader`, status
`present`, the lifecycle values above, and an `expiryCondition` token
`after:<last-use-milestone>`.

Retained manifest revisions 3, 4, and 5 and state revisions 1 through 4 each have distinct artifact
aliases.

Manifest revision 4 names revision 3 as its direct predecessor; revision 5 names revision 4. Each
state after revision 1 names the prior state. No revision self-references. Every retained inventory
backup receives its own alias.

Within discovery, new aliases allocate in this fixed order: request, manifest revision 4, root map,
copy plan, state revision 1, inventory backup, then destination reservations in `sourceAlias` order.
Within later phases, allocation order is request, manifest revision when present, receipt or
preflight report, state revision, then inventory backup. Released A0 manifest revision 3 keeps its
existing alias; every new retained byte sequence gets a new alias.

## 13. Accepted residual risks

The profile does not defend against:

- malicious local path or link races between BCL checks;
- a compromised filesystem, kernel, runtime, or trusted toolchain;
- hidden source hard links;
- source changes after each held handle closes;
- post-verification private-workspace mutation; or
- multi-file crash atomicity or durability beyond explicit file flushes and normal behavior.

These risks are accepted only for private research snapshots. Later consumers rehash files against
private provenance before use. A future real-save writer, recovery system, or external product must
establish its own measured transaction profile and may not cite A2 qualification as evidence.

## 14. Locator redaction

`LocatorSegmentRedactor` accepts typed segments, never an untyped locator string. It permits literal
output only for:

- schema-defined document-role tokens;
- numeric array indexes; and
- JsonEx markers `@`, `@c`, `@a`, and `@r`.

The literal-key allowlist remains empty. A complete survey uses a two-pass process: collect all
distinct schema and dynamic keys, sort each population ordinally, then assign aliases starting at
one:

- `schema-key-NNNNNN`; or
- `dynamic-key-NNNNNN`.

A2 implements the redactor and synthetic tests only. No real key is read, and no private map or map
schema is created in A2. The first scanning increment defines and persists the map contract, reuses
it later, and refuses remaps or unseen keys until a new approved mapping revision exists. Source
keys never enter Git or Agent input. Unknown or ambiguous kinds, conflicting mappings, range
exhaustion, and attempted literal keys are safety failures.

## 15. Lifecycle preflight

`cleanup-preflight` requires valid state revision 3, reads its bound
`atlas-private-inventory/v1`, and creates a new private report. It performs no deletion or workspace
census. It strictly validates every bound inventory row and reports one of:

- `blocked-status` unless status is `last-use-complete` or `deletion-pending`;
- `blocked-disposition` unless disposition is `delete`;
- `blocked-before-last-use` when the proposed milestone precedes last use;
- `indeterminate-expiry` unless expiry is exactly `after:<last-use-milestone>`; or
- `eligible-for-human-review` when none of the preceding results applies.

Milestone order is `A2` through `A8`, then `post-A8-appeal`. The first matching result wins in the
order above. An invalid inventory row fails the command rather than producing eligibility.

After publishing the report, preflight adds exactly one `cleanup-record` inventory entry, safely
replaces the inventory, and publishes state revision 4 last. The replacement adds exactly four
entries: preflight request, cleanup report, state revision 4, and preflight inventory backup. No
prior row changes.

The preflight backup preserves the historical state-3 inventory bytes. State revision 4 binds its
alias, canonical survey-relative path, and digest. After preflight, state-3 qualification rehashes
that backup when the canonical inventory digest no longer equals the digest in state 3.

The proposed milestone is not deletion authority. A2 does not need artifact paths because it does
not delete. Final cleanup, alias-to-path custody, deletion approval, and attestation are designed
and implemented in A8 under the then-current inventory and lifecycle authority.

## 16. Exact tracked implementation scope

New library files:

```text
src/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.Atlas/
  AtlasDiscovery.cs
  AtlasIntakeContracts.cs
  LocatorSegmentRedactor.cs
  PrivateArtifactLifecycle.cs
  TrustedLocalCopy.cs
```

New CLI file:

```text
src/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.Atlas.Cli/
  AtlasCliOperations.cs
```

Modified production file:

```text
src/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.Atlas.Cli/
  AtlasCliApplication.cs
```

New test files:

```text
tests/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.Atlas.Tests/
  AtlasDiscoveryTests.cs
  AtlasIntakeContractTests.cs
  LocatorSegmentRedactorTests.cs
  PrivateArtifactLifecycleTests.cs
  TrustedLocalCopyTests.cs
```

Modified test files:

```text
tests/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.Atlas.Tests/
  AtlasCliApplicationTests.cs
  AtlasProcessSmokeTests.cs
  ProjectBoundaryTests.cs
```

Repository-safe implementation-candidate documentation:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
```

New private-contract schemas:

```text
src/private/app/celesphonia-modifier/docs/.copilot/schemas/atlas-v0/
  copy-plan.schema.json
  cleanup-preflight-report.schema.json
  copy-receipt.schema.json
  intake-state.schema.json
  source-root-map.schema.json
```

No other production, test, project, package, lock, schema, traversal, configuration, or repository
path changes occur in A2. A newly required path stops implementation and requires a reviewed plan
revision.

Record-only commits are outside the implementation candidate and may change exactly one listed
path:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-plan-review.md
  atlas-v0-a2-tool-safety-review.md
  atlas-v0-a2-intake-approval.md
  atlas-v0-a2-release-gate.md
```

The source-safety candidate compares from the plan-review record and contains only the production,
test, schema, and implementation-candidate documentation paths above. The final release candidate
uses the same base and adds only the tool-safety and intake-approval record paths.

The release-record child changes only `atlas-v0-a2-release-gate.md`. The plan-review record precedes
and is the base of all three comparisons; it is not repeated in their diffs.

## 17. Tests and execution stages

All automated tests use only synthetic paths, manifests, bytes, keys, inventories, and temporary
directories. They never use the installed game, a real save, copied game content, or `.private`.

Direct tests cover:

- strict JSON shape, duplicate rejection, versions, values, revisions, and private digest binding;
- schema/DTO agreement and item census for the five schema-governed output contracts;
- strict-reader and DTO tests for all four request contracts;
- DOS-path normalization, containment, fixed-drive policy, and component reparse refusal;
- complete discovery accounting and every A0 terminal rule;
- denial of stale, pending, rejected, superseded, or digest-mismatched manifests;
- root-map, intake-state, copy-plan, artifact, lineage, and destination continuity;
- per-file copy fidelity, destination read-only state, directory reconciliation, and state signals;
- deterministic injected sharing, short-read, flush, move, hash, cancellation, and cleanup failures;
- interruption at each private-document publication and inventory-replacement seam;
- provisional receipt/inventory states, sole state-3 qualification, and standard-output failure
  after qualification;
- successful restart before rename, after rename, after inventory replacement, and after receipt
  publication, asserting no source opens and exact rename-inventory-receipt-state ordering;
- refusal to use an incomplete or mismatched final snapshot;
- no live-source deletion, modification, decode, or scan;
- stable two-pass locator aliases and every literal bypass;
- non-deleting lifecycle eligibility and the exact state-4 inventory transition;
- exact CLI grammar, help, bytes, precedence, result mapping, and path non-disclosure; and
- exact project and dependency manifests.

Apphost tests cover synthetic success, usage, approval, safety, and I/O results. Cancellation-token
mapping is tested directly; A2 makes no new claim that apphost tests synthesize a Windows console
cancel signal.

### A2.1 Implement without private access

Implement and validate the exact tracked scope. Do not inspect the installed game, live saves,
private workspace, or A0 private artifacts.

Commit and push a source-safety candidate. Its record binds the exact commit and tree, pinned SDK,
locked dependency graph, build command, tests, and residual trusted-toolchain assumption. A fresh
independent subagent reviews its cumulative diff until exact `No findings`.

Persist `../reviews/atlas-v0-a2-tool-safety-review.md` as a reviewed staged blob and unchanged
record-only child. Verify its parent, path, content, and upstream. It authorizes the project leader
to build and run the exact reviewed source from a clean checkout; it does not attest an untracked
binary digest and does not authorize copying.

Any tracked source, project, dependency, or build-procedure change invalidates this gate and every
downstream A2 private-run record. A2 restarts at A2.1.

### A2.2 Human-operated discovery and approval

The project leader runs metadata-only discovery under section 9 and follows the private procedure in
section 10. Copilot receives only safe counts and contract differences and reconciles them against
A0. Any denominator or policy change reopens A0.

Stop for explicit approval of the exact local pending manifest. Independently review, commit, push,
and verify the repository-safe approval record. The project leader then runs `intake-confirm`
locally. Rejection, an unsupported source, or any unexpected revision stops copying.

### A2.3 Human-operated copy and preflight

The project leader privately hashes intake-state revision 2, creates the copy request, runs
`intake-copy`, and reports only repository-safe acceptance aggregates. Do not decode or scan the
snapshots.

Run cleanup preflight, but perform no deletion.

### A2.4 Release

Treat the verified intake-approval record commit as the final repository-safe candidate. Relative
to the plan-review base, it contains the implementation-candidate paths plus only the tool-safety
and intake-approval records defined in section 16.

A fresh independent subagent reviews that complete cumulative candidate and safe acceptance
evidence. Resolve every finding and repeat until exact `No findings`.

Persist `../reviews/atlas-v0-a2-release-gate.md` as a reviewed staged blob and unchanged record-only
child. Verify and push it before marking A2 complete.

## 18. Acceptance criteria

A2 is accepted only when:

1. the implementation matches the exact tracked scope and three-project boundary;
2. production projects retain zero project-local package references;
3. requests are strict, explicit, private, duplicate-free, and free of ambient discovery;
4. metadata-only discovery precedes every source-content read;
5. exactly 21 save inputs and 496 definitions from released A0 revision 3 are approved;
6. every source has exactly one terminal status and Steam cloud metadata is excluded;
7. the project leader approves exact pending manifest bytes before confirmation or copy;
8. intake-state revision 2 binds approved manifest revision 5 and every private input digest;
9. live content is read only by copy and is never decoded or scanned;
10. path checks reject every tested reparse, outside-root, non-fixed, malformed, or existing output;
11. every destination uses create-new semantics and matches source length and SHA-256;
12. every included source produces one provisional verified copy before state revision 3;
13. all 21 save copies and 496 definition copies have matching private provenance;
14. source metadata remains stable while each handle is held and directory entry sets remain stable;
15. valid state revision 3 is the sole qualification signal and requires receipt/inventory
    agreement;
16. interruption before state revision 3 leaves an unusable partial state; valid state revision 3
    remains authoritative if process success is not reported; no failure removes an unowned path;
17. later-use revalidation is required because read-only snapshots are not immutable;
18. locator redaction cannot emit or remap a literal source key;
19. lifecycle preflight deletes nothing, reports every state-3 inventory artifact, adds exactly its
    request, report, state-4, and inventory-backup entries, and publishes state revision 4;
20. no private path, hash, name, value, source text, request, or unapproved count enters process
    output or Git; section 20 lists the only repository-safe aggregate counts;
21. every command result follows the declared fixed bytes, exit code, and A1 precedence;
22. locked restore, build, formatting, targeted tests, apphost checks, and HK pass;
23. source-safety review reports `No findings` before private discovery;
24. cleanup preflight reports every valid state-3 inventory row, reports zero invalid rows,
    performs zero deletions, and publishes valid state revision 4;
25. final independent review reports `No findings`; and
26. the verified release record is the record-only child and shared branch tip.

## 19. Stop conditions

Stop A2 and revise or reopen the governing increment when:

- a root, denominator, selection rule, or terminal policy differs from released A0;
- a source is unsupported, unreadable, unclassified, or unexpectedly reparse-backed;
- a concrete source requires identity proof beyond the approved profile;
- the workspace is not on a fixed trusted local drive;
- a source cannot deny concurrent write and delete sharing while held;
- source metadata while held or directory entries change;
- an incomplete or mismatched final signal exists and has not received human disposition;
- private information reaches Git, Agent input, or process output;
- redaction requires guessing or remapping a literal key;
- lifecycle eligibility is ambiguous;
- implementation needs a new package, project, unplanned schema, tracked path, or final deletion;
- private execution would use a changed or dirty source candidate;
- validation is nondeterministic without the bounded internal fault seam; or
- any independent finding remains unresolved.

Any A0 reopening ends this one-shot plan's authority. A2 resumes only through a revised, persisted,
independently approved A2 plan with a new revision and state sequence.

## 20. Validation and records

Run .NET commands from the repository root through `mise exec -- dotnet`.

The implementation candidate must pass:

- locked restore and warning-free build of the test project;
- `dotnet format --verify-no-changes` for all three projects;
- the complete A2 tests through Microsoft.Testing.Platform;
- direct apphost smoke commands;
- evaluated project-reference and package-reference checks;
- exact no-renames tracked-path comparisons for the source-safety candidate, final release
  candidate, and release-record child defined in section 16;
- ref-bound HK checks;
- `git diff --check`;
- committed-file line-length and LF checks; and
- candidate tree, ancestry, upstream, and clean-worktree checks.

No retained command transcript may contain a private path, hash, manifest, request, provenance,
inventory, source name, granular count, or copy. Repository-safe aggregate acceptance counts are
recorded in review documents, not emitted by a command.

The only counts permitted in Git are: save denominator 23, included saves 21, excluded Steam
metadata 2, definition denominator 580, included definitions 496, excluded definitions 84, copy
successes 21 and 496, copy failures 0, unsupported/unreadable 0, invalid inventory rows 0, and
deletions 0. `Invalid inventory rows` is the strict validation result for the state-3-bound
inventory, not a workspace census. The only safe difference categories are root set, denominator,
selection rule, public build, unsupported/unreadable, or no difference. Every name and every other
count is private.

The plan-review record path is `../reviews/atlas-v0-a2-plan-review.md`. It binds:

- the final plan commit and tree;
- the exact A1 release record and its verification;
- all reviewed and governing paths;
- the trusted-local-filesystem decision;
- every review iteration and disposition;
- plan validation outcomes; and
- the implementation diff base.

The reviewed plan candidate contains this plan, the A0 contract, A0 scope-review lifecycle banner,
Atlas execution plan, Save Semantic Atlas plan, and `.copilot` index. No other path belongs to the
plan candidate.

Every plan-review, source-safety, approval, and release record follows sections 16 and 17 of
`project-operating-model.md`: independently review its exact staged blob, commit it unchanged as a
record-only child, push it, and verify parent, path, content, and upstream.

## 21. Private outputs and handoff

Expected private outputs are:

- create-new pending and approved manifest revisions;
- request documents, live-discovery metadata, intake states, and copy plans;
- save and definition snapshots;
- private receipts, inventory, and completion signals;
- cleanup-preflight report; and
- unusable incomplete evidence retained only when removal fails.

All remain under the protected Git-ignored Atlas workspace and follow A0 lifecycle milestones.

To resume A2:

1. read all applicable `AGENTS.md` files and the governing plans;
2. verify the A2 plan-review record, exact diff base, upstream, and clean worktree;
3. identify the first incomplete stage;
4. do not enter the private phase without the exact source-safety record;
5. leave private request creation, execution, manifest review, and hashing to the project leader;
6. do not copy without the verified approval record and valid intake-state revision 2; and
7. infer neither private state nor authority from conversation history.
