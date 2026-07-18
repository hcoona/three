# Atlas V0 A2 Intake and Safety Plan

**Status:** Proposed execution plan; implementation requires an exact-plan independent review
record

**Increment:** A2 - Intake and Safety Harness

**Implementation language:** C# on the repository-pinned .NET 10 SDK

**Governing baseline:** `atlas-v0-execution-plan.md`, Increment A2

**Implementation diff base:** The verified A2 plan-review record commit

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

Private discovery requires the source-safety gate in section 16. Copying additionally requires the
human approval gate in section 10.

## 4. Scope

### In scope

- Existing Atlas library, CLI, and test projects.
- Metadata-only discovery for the two approved save-root roles.
- Metadata-only discovery for the frozen installed-definition rules.
- Existing `atlas-intake/v2` and `atlas-private-inventory/v1` contracts.
- Strict private request parsing with source-generated `System.Text.Json` metadata.
- Trusted-local copy creation and private fidelity evidence.
- Deterministic deny-by-default locator-key aliases.
- Non-mutating private lifecycle preflight.
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
  `surveyAlias`, `workspaceRoot`, `baselineManifestPath`, `expectedBaselineRevision`,
  `nextManifestRevision`, `manifestRevisionDirectory`, `saveRoots`, `definitionRoot`, and
  `inventoryPath`;
- `intake-confirm`, version `atlas-intake-confirmation-request/v1`: `schemaVersion`,
  `surveyAlias`, `workspaceRoot`, `pendingManifestPath`, `expectedPendingSha256`,
  `expectedPendingRevision`, `decisionCommit`, and `manifestRevisionDirectory`;
- `intake-copy`, version `atlas-intake-copy-request/v1`: `schemaVersion`, `surveyAlias`,
  `workspaceRoot`, `approvedManifestPath`, `expectedApprovedSha256`,
  `expectedApprovedRevision`, `decisionCommit`, `incompleteCopyPath`, `finalCopyPath`, and
  `inventoryPath`; and
- `cleanup-preflight`, version `atlas-cleanup-preflight-request/v1`: `schemaVersion`,
  `surveyAlias`, `workspaceRoot`, `inventoryPath`, `proposedMilestone`, and
  `reportOutputPath`.

`saveRoots` contains exactly two objects, one for each approved A0 location role. Each object has
only `locationRole` and `path`. Other path properties are strings. Revisions are positive integers.
Expected hashes are 64 lowercase hexadecimal characters. `decisionCommit` is a 40-character
lowercase Git object identifier. `proposedMilestone` is an existing inventory milestone and is
advisory because preflight cannot authorize deletion.

Manifest revisions are create-new files named
`corpus-intake-manifest.rNNNNNN.json` in one survey-local private revision directory. Revision
numbers increase by one without reuse. On the first A2 discovery, the baseline is the released A0
revision 3 manifest and the revision directory is empty. Later discovery uses the newest file in
that directory as its baseline. The next revision must equal the baseline plus one, and the target
must not exist. Confirmation applies the same rule. A new pending revision invalidates every earlier
approval for copying.

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

The global help adds the four command shapes and one short description per command to A1's existing
LF-terminated help. Its exact bytes are frozen in `AtlasCliApplicationTests`.

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

1. validates the request, path policy, A0 roots, and next create-new revision;
2. enumerates every immediate entry in both save roots;
3. applies the exact A0 save-role and terminal-decision rules;
4. enumerates the complete installed-definition universe under the frozen A0 rules;
5. terminally classifies every candidate;
6. assigns survey-local aliases without publishing private names or paths;
7. reconciles all root, group, included, excluded, unsupported, and unreadable counts;
8. writes one new `atlas-intake/v2` revision with confirmation `pending`; and
9. updates private live-discovery and planned-inventory records.

`steam_autocloud.vdf` is always `exclude-steam-autocloud`. A reparse-backed root or entry is
`unsupported` and stops A2. Any root, denominator, selection-rule, or terminal-policy difference
reopens A0; A2 cannot approve a narrowing itself.

## 10. Human-operated private approval

The pending manifest and its hash are never supplied to Copilot or a subagent. The project leader
performs the private phase:

1. creates the private request without placing its path in an Agent transcript;
2. runs the reviewed source candidate from a clean checkout;
3. opens the exact pending manifest in a local editor;
4. verifies its revision, decisions, counts, and private SHA-256;
5. reports only approved repository-safe counts and contract differences;
6. explicitly approves or rejects that survey alias and revision;
7. supplies the private expected SHA-256 only to the local confirmation request; and
8. runs confirmation only after the approval record commit is pushed.

The approval record at `../reviews/atlas-v0-a2-intake-approval.md` contains:

- survey alias and pending manifest revision;
- safe aggregate counts and public game-build identifiers;
- `trusted-local-filesystem/v1` and its accepted residual risks;
- the exact source-safety candidate commit and tree;
- the project leader's decision; and
- an explicit statement that no private path, name, hash, value, or source text is recorded.

The record is independently reviewed as an exact staged blob, committed unchanged as the only child
of the approved source-safety record, pushed, and verified for parent, path, content, and upstream.

`intake-confirm` requires the exact private SHA-256 reviewed by the project leader. It verifies that
the pending file is the newest revision, matches that digest and revision, and remains `pending`.
It then writes the next create-new revision, differing only in manifest revision and confirmation:
`approved`, `project-leader`, and the exact approval-record commit.

This is a trusted human-operated gate, not a claim that the CLI proves Git authority. Any later
discovery revision invalidates approval. The operator privately hashes the approved revision and
places that digest in the copy request.

## 11. Copy and qualification

`intake-copy` accepts only the newest approved manifest revision. Its private digest, revision,
survey alias, and decision commit must exactly match the request.

Before content access, it re-enumerates every source directory and requires exact agreement with
the approved manifest. For each included source, it:

1. applies the path policy and confirms unchanged discovery metadata;
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

Copies, private provenance, the inventory candidate, and a completion record are built inside one
owned, create-new `.incomplete` directory. After all checks pass, `Directory.Move` renames it to a
nonexistent sibling final directory. The canonical private inventory is then updated, and a
create-new completion marker is written and flushed in the final directory.

A snapshot is `a2-qualified` only when its final completion marker and canonical inventory agree.
Neither signal alone is usable. The operation does not claim an atomic transaction across those
signals. If interruption leaves an `.incomplete` directory or only one final signal, later tools
refuse it. Recovery requires human inspection and a separately approved retry or targeted removal;
A2 never guesses or recursively removes an unexpected final directory.

On an ordinary pre-rename failure, cleanup attempts to remove only the request-bound, owned,
non-reparse `.incomplete` directory. If removal fails, the command reports failure and leaves the
directory unusable for human inspection. It never removes a live source, prior snapshot, approved
manifest, canonical evidence, or unexpected final directory.

Private provenance records `trusted-local-filesystem/v1`, source-relative aliases, source metadata,
destination-relative aliases, lengths, and SHA-256 values. Definition copies use the same evidence.
The inventory uses `a2-qualified` only for save-copy entries because the existing schema limits that
property to save copies.

## 12. Accepted residual risks

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

## 13. Locator redaction

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

The private alias map is persisted per survey. Later operations reuse it and refuse any remap or
previously unseen key until a new approved mapping revision exists. Source keys never enter Git or
Agent input. Unknown or ambiguous kinds, conflicting mappings, range exhaustion, and attempted
literal keys are safety failures.

## 14. Lifecycle preflight

`cleanup-preflight` reads `atlas-private-inventory/v1` and creates a new private report. It performs
no deletion and no inventory update. For every artifact it reports the alias, status, last-use
milestone, planned disposition, and whether the proposed milestone would make it eligible.

The proposed milestone is not deletion authority. A2 does not need artifact paths because it does
not delete. Final cleanup, alias-to-path custody, deletion approval, and attestation are designed
and implemented in A8 under the then-current inventory and lifecycle authority.

## 15. Exact tracked implementation scope

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

Repository-safe implementation records:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  reviews/atlas-v0-a2-tool-safety-review.md
  reviews/atlas-v0-a2-intake-approval.md
```

No other production, test, project, package, lock, schema, traversal, configuration, or repository
path changes occur in A2. A newly required path stops implementation and requires a reviewed plan
revision.

## 16. Tests and execution stages

All automated tests use only synthetic paths, manifests, bytes, keys, inventories, and temporary
directories. They never use the installed game, a real save, copied game content, or `.private`.

Direct tests cover:

- strict JSON shape, duplicate rejection, versions, values, revisions, and private digest binding;
- DOS-path normalization, containment, fixed-drive policy, and component reparse refusal;
- complete discovery accounting and every A0 terminal rule;
- denial of stale, pending, rejected, superseded, or digest-mismatched manifests;
- per-file copy fidelity, destination read-only state, directory reconciliation, and final signals;
- deterministic injected sharing, short-read, flush, move, hash, cancellation, and cleanup failures;
- refusal to use an incomplete or mismatched final snapshot;
- no live-source deletion, modification, decode, or scan;
- stable two-pass locator aliases and every literal bypass;
- non-mutating lifecycle eligibility;
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

The project leader runs metadata-only discovery under section 10. Copilot receives only safe counts
and contract differences and reconciles them against A0. Any denominator or policy change reopens
A0.

Stop for explicit approval of the exact local pending manifest. Independently review, commit, push,
and verify the repository-safe approval record. The project leader then runs `intake-confirm`
locally. Rejection, an unsupported source, or a new pending revision stops copying.

### A2.3 Human-operated copy and preflight

The project leader privately hashes the newest approved revision, creates the copy request, runs
`intake-copy`, and reports only repository-safe acceptance aggregates. Do not decode or scan the
snapshots.

Run cleanup preflight, but perform no deletion.

### A2.4 Release

Create a final repository-safe candidate containing the approved tracked scope and records. A fresh
independent subagent reviews the complete cumulative candidate and safe acceptance evidence. Resolve
every finding and repeat until exact `No findings`.

Persist `../reviews/atlas-v0-a2-release-gate.md` as a reviewed staged blob and unchanged record-only
child. Verify and push it before marking A2 complete.

## 17. Acceptance criteria

A2 is accepted only when:

1. the implementation matches the exact tracked scope and three-project boundary;
2. production projects retain zero project-local package references;
3. requests are strict, explicit, private, duplicate-free, and free of ambient discovery;
4. metadata-only discovery precedes every source-content read;
5. exactly 21 save inputs and 496 definitions are approved, or a reopened A0 releases a new scope;
6. every source has exactly one terminal status and Steam cloud metadata is excluded;
7. the project leader approves exact pending manifest bytes before confirmation or copy;
8. the approved manifest is the newest create-new revision and its private digest matches;
9. live content is read only by copy and is never decoded or scanned;
10. path checks reject every tested reparse, outside-root, non-fixed, malformed, or existing output;
11. every destination uses create-new semantics and matches source length and SHA-256;
12. every included source produces one qualified final copy or the operation fails;
13. all 21 save copies and 496 definition copies have matching private provenance;
14. source metadata remains stable while each handle is held and directory entry sets remain stable;
15. qualification requires both a final completion marker and canonical inventory agreement;
16. a failed or interrupted intake leaves no usable snapshot and removes no unowned path;
17. later-use revalidation is required because read-only snapshots are not immutable;
18. locator redaction cannot emit or remap a literal source key;
19. lifecycle preflight is non-mutating and reports every inventoried artifact;
20. no private path, hash, name, value, source text, request, or count enters process output or Git;
21. every command result follows the declared fixed bytes, exit code, and A1 precedence;
22. locked restore, build, formatting, targeted tests, apphost checks, and HK pass;
23. source-safety review reports `No findings` before private discovery;
24. cleanup preflight succeeds with zero unexplained artifacts and performs zero deletions;
25. final independent review reports `No findings`; and
26. the verified release record is the record-only child and shared branch tip.

## 18. Stop conditions

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
- implementation needs a new package, project, schema, tracked path, or final deletion;
- private execution would use a changed or dirty source candidate;
- validation is nondeterministic without the bounded internal fault seam; or
- any independent finding remains unresolved.

## 19. Validation and records

Run .NET commands from the repository root through `mise exec -- dotnet`.

The implementation candidate must pass:

- locked restore and warning-free build of the test project;
- `dotnet format --verify-no-changes` for all three projects;
- the complete A2 tests through Microsoft.Testing.Platform;
- direct apphost smoke commands;
- evaluated project-reference and package-reference checks;
- exact no-renames cumulative tracked-path comparison;
- ref-bound HK checks;
- `git diff --check`;
- committed-file line-length and LF checks; and
- candidate tree, ancestry, upstream, and clean-worktree checks.

No retained command transcript may contain a private path, hash, manifest, request, provenance,
inventory, source name, count, or copy.

The plan-review record path is `../reviews/atlas-v0-a2-plan-review.md`. It binds:

- the final plan commit and tree;
- the exact A1 release record and its verification;
- all reviewed and governing paths;
- the trusted-local-filesystem decision;
- every review iteration and disposition;
- plan validation outcomes; and
- the implementation diff base.

Every plan-review, source-safety, approval, and release record follows sections 16 and 17 of
`project-operating-model.md`: independently review its exact staged blob, commit it unchanged as a
record-only child, push it, and verify parent, path, content, and upstream.

## 20. Private outputs and handoff

Expected private outputs are:

- create-new pending and approved manifest revisions;
- request documents and live-discovery metadata;
- save and definition snapshots;
- private provenance, inventory, completion signals, and locator maps;
- cleanup-preflight report; and
- unusable incomplete evidence retained only when removal fails.

All remain under the protected Git-ignored Atlas workspace and follow A0 lifecycle milestones.

To resume A2:

1. read all applicable `AGENTS.md` files and the governing plans;
2. verify the A2 plan-review record, exact diff base, upstream, and clean worktree;
3. identify the first incomplete stage;
4. do not enter the private phase without the exact source-safety record;
5. leave private request creation, execution, manifest review, and hashing to the project leader;
6. do not copy without the verified approval record and newest approved private revision; and
7. infer neither private state nor authority from conversation history.
