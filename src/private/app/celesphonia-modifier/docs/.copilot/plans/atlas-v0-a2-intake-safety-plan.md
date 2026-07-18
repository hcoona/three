# Atlas V0 A2 Intake and Safety Plan

**Status:** Proposed execution plan; implementation requires an exact-plan independent review
record

**Increment:** A2 - Intake and Safety Harness

**Implementation language:** C# on the repository-pinned .NET 10 SDK

**Governing baseline:** `atlas-v0-execution-plan.md`, Increment A2

**Implementation diff base:** The verified A2 plan-review record commit

## 1. Outcome

A2 creates a reusable, private, read-only intake path that:

1. discovers the complete approved save and installed-definition scopes before copying;
2. requires human approval of the exact private discovery manifest;
3. creates new research copies under a trusted-local-filesystem profile;
4. verifies copy fidelity and source stability without decoding or interpreting content;
5. prevents literal dynamic locator keys from reaching canonical records; and
6. enforces private-artifact lifecycle rules without deleting evidence before its last use.

A2 produces research copies only. It does not establish E2 or E3 authority, qualify a live-save
writer, or prove behavior under a hostile local actor or adversarial filesystem.

## 2. Approved trust-profile amendment

The project leader approved `trusted-local-filesystem/v1` instead of the earlier full Windows
file-identity proof.

This profile trusts:

- the user-controlled local Windows machine;
- ordinary fixed local filesystems selected by the operator;
- the approved game installation and Git-ignored private workspace roots;
- the absence of a malicious actor racing path or link changes during intake; and
- normal Windows and .NET file-creation semantics.

It does not collect or require volume identifiers, stable file identifiers, link counts, final
kernel paths, reparse tags, or hand-written native interop. It uses only BCL file APIs.

The profile still rejects visible reparse points, directories where regular files are required,
existing destination entries, source changes, directory-entry changes, copy-hash mismatches,
unapproved manifests, and paths outside the approved roots.

Creating a destination with `FileMode.CreateNew` under the controlled workspace is accepted as
sufficient evidence that A2 created a new ordinary directory entry. A source may itself have other
hard links; A2 reads it only and makes no claim about its identity. A malicious post-check link or
path race is an accepted residual risk for this personal research profile.

This amendment supersedes only the full identity, volume, link-count, and final-path proof
previously assigned to A2. The finite A0 scope, privacy boundary, terminal accounting, manifest,
redaction, lifecycle, human approval, and no-live-scan requirements remain binding.

## 3. Entry conditions

A2 implementation may begin only when:

- A1's release-record commit is reachable from the shared branch;
- this plan and its baseline amendments are committed and pushed;
- a fresh independent reviewer reports `No findings` for the exact plan candidate;
- the plan-review record is committed and pushed as the only child change;
- that record passes its parent, path, content, and upstream checks; and
- the tracked worktree is clean.

Private discovery may begin only after the implementation safety candidate defined in section 15
receives an independent `No findings` result. Copying may begin only after the human approval gate
in section 10.

## 4. Scope

### In scope

- Existing Atlas library, CLI, and test projects.
- Read-only discovery for the two approved save-root roles.
- Read-only discovery for the approved installed-definition selection rules.
- Existing `atlas-intake/v2` and `atlas-private-inventory/v1` contracts.
- Strict private request parsing with source-generated `System.Text.Json` metadata.
- Trusted-local copy creation, private fidelity evidence, and source-stability checks.
- Deterministic deny-by-default locator-key aliases.
- Private lifecycle preflight and final-cleanup behavior.
- Synthetic, repository-safe automated tests.
- One private A2 run after exact human manifest approval.

### Out of scope

- Decoding or decompressing saves.
- Parsing JsonEx graphs or extracting semantic facts.
- Using live files as scanner, codec, or semantic inputs.
- Editing, replacing, renaming, deleting, or locking a live source.
- Full Windows file identity, volume identity, link-count, final-path, or reparse-tag proof.
- Native interop, CsWin32, a Windows-specific target framework, or a new project.
- Network access, telemetry, logging private paths, or emitting exception details.
- WinUI, dependency injection, Generic Host, a database, or an Agent runtime.
- Reusing a pre-A2 preservation snapshot as qualified input.
- Executing final cleanup on retained A2 evidence before its declared last-use milestone.
- Any real-save write or compatibility claim.

## 5. Project and dependency boundaries

A2 preserves the existing three-project graph:

- `Hcoona.CelesphoniaModifier.Atlas` owns reusable intake policy and file operations.
- `Hcoona.CelesphoniaModifier.Atlas.Cli` owns exact argument matching, fixed process diagnostics,
  console streams, and operation dispatch.
- `Hcoona.CelesphoniaModifier.Atlas.Tests` owns direct and apphost tests.

Production projects remain free of project-local packages. A2 uses BCL APIs, including
`System.Text.Json` and `System.Security.Cryptography`, from the target framework. It does not change
Central Package Management, lock files, target frameworks, root traversal, or telemetry controls.

The library does not read console or environment state, start processes, access the network, depend
on the CLI, or infer the installed-game location. Every private root and operation parameter comes
from an explicit private request.

## 6. Private request boundary

Each mutating-private-workspace command accepts one non-empty request-file path. The request path
may be private and must never be echoed. Request documents:

- use a command-specific version string;
- reject unknown JSON properties;
- contain explicit input, output, survey, manifest, and inventory paths;
- name expected schema versions and manifest revisions;
- contain no default derived from the current directory, user profile, registry, Steam, or
  environment variables; and
- remain under the Git-ignored private workspace.

The version strings are:

- `atlas-intake-discovery-request/v1`;
- `atlas-intake-confirmation-request/v1`;
- `atlas-intake-copy-request/v1`; and
- `atlas-cleanup-request/v1`.

Requests and operational outputs are private. Their C# contracts and synthetic tests are
repository-safe; no real request or path enters Git.

## 7. Exact CLI surface

A2 adds these accepted command shapes:

```text
celesphonia-atlas intake-discover <request-file>
celesphonia-atlas intake-confirm <request-file>
celesphonia-atlas intake-copy <request-file>
celesphonia-atlas cleanup-preflight <request-file>
celesphonia-atlas cleanup-final <request-file>
```

Each command also accepts `-h` or `--help` in place of `<request-file>`. Existing A1 command and
help forms remain accepted. Matching is ordinal and case-sensitive. No option abbreviation,
response file, directive, wildcard, environment expansion, implicit path, or additional argument
is accepted.

Standard output contains only fixed, LF-terminated success text. Standard error contains only one
fixed diagnostic:

- `Invalid arguments.` with exit code `2`;
- `Operation canceled.` with exit code `3`;
- `I/O failure.` with exit code `4`;
- `Safety check failed.` with exit code `5`;
- `Approval required.` with exit code `6`; or
- `Unexpected failure.` with exit code `1`.

Private paths, source names, hashes, values, exception text, and counts are never written to process
streams. Detailed operational evidence remains in the private output documents named by the
request.

Argument and help handling precede cancellation and file access. Caller-requested cancellation
retains A1 semantics. A safety or approval refusal is not collapsed into success or generic I/O.

## 8. Discovery contract

`intake-discover` performs metadata-only discovery and never opens file contents.

It:

1. validates the request and approved root roles;
2. rejects a root that is missing, unreadable, outside the request, or visibly reparse-backed;
3. enumerates every immediate save-root entry before any copy;
4. applies the exact A0 save-role and terminal-decision rules;
5. enumerates the complete installed-definition candidate universe under the frozen selection
   rules;
6. terminally classifies every candidate;
7. assigns aliases without publishing private paths;
8. reconciles root, group, included, excluded, unsupported, and unreadable counts;
9. writes a new `atlas-intake/v2` revision with `confirmation.status` equal to `pending`; and
10. leaves every live file and the private workspace unchanged except for the new pending manifest
    and private discovery evidence.

`steam_autocloud.vdf` is always `exclude-steam-autocloud`. A reparse-backed root or entry is
`unsupported` and blocks copy unless the user approves a persisted scope narrowing. Any difference
from the previously approved denominator creates a new pending revision and cannot inherit prior
approval.

## 9. Human review and confirmation

The pending private manifest is not supplied to a subagent. The user reviews the exact local
manifest through a local editor or repository-safe tool output that does not expose its contents to
an Agent.

Before approval, Copilot may present only repository-safe aggregates and named contract
differences. The user must explicitly approve or reject the exact survey alias and manifest
revision.

Approval is persisted in
`../reviews/atlas-v0-a2-intake-approval.md`. That repository-safe record contains:

- survey alias and manifest revision;
- safe aggregate counts and public game-build identifiers;
- the trusted-local-filesystem profile identifier and accepted residual risks;
- the implementation safety candidate commit and tree;
- the user's approval decision; and
- a statement that no private path, hash, name, value, or source text is recorded.

`intake-confirm` requires a private request that binds the pending manifest bytes to the approval
record's full commit identifier. It writes a new approved manifest revision with
`confirmedByRole: project-leader` and the exact `decisionReference`. It never invents approval,
changes discovery decisions, or copies a file.

Any discovery change after approval supersedes the approval and returns to `pending`.

## 10. Copy and qualification contract

`intake-copy` accepts only an approved `atlas-intake/v2` manifest whose decision reference resolves
to the exact A2 intake-approval commit.

For every included save and definition, it:

1. verifies that the source and destination remain under their approved roots;
2. rejects visible reparse points on the approved roots, controlled workspace path, source entry,
   incomplete destination, or any existing descendant used by the operation;
3. captures the complete source-directory entry sets before opening content;
4. opens each source read-only with `FileShare.Read`, denying concurrent write and delete sharing;
5. captures private length, last-write, attributes, and request-relative provenance;
6. creates each destination with `FileMode.CreateNew`, write access, and `FileShare.None`;
7. streams bytes once from the held source handle to the destination while computing private
   SHA-256;
8. flushes destination file data before closing it;
9. reopens the destination read-only and independently recomputes length and SHA-256;
10. confirms source handle length and path metadata remain unchanged;
11. marks the verified destination read-only;
12. re-enumerates and reconciles every approved source directory;
13. writes private provenance and inventory updates only after every source passes; and
14. atomically renames the owned survey-local `.incomplete` directory to its final copy directory.

The operation fails closed on sharing violations, short reads, source or directory changes,
destination existence, hash or length mismatch, unsupported entry type, cancellation, or any
unclassified source.

Failure cleanup may remove only the newly created, request-bound incomplete directory after proving
that it is inside the controlled workspace and is not reparse-backed. It never removes a live
source, prior completed copy, approved manifest, or retained evidence.

The private inventory records `a2-qualified` for save copies and names
`trusted-local-filesystem/v1` in its verification method. Definition-copy provenance records the
same profile without adding the save-only qualification property.

## 11. Accepted residual risks

The trusted-local profile does not defend against:

- a malicious local actor changing links or paths between BCL checks;
- a compromised filesystem or kernel;
- hidden hard-link relationships on source files;
- post-verification mutation by an actor with access to the private workspace; or
- durability claims beyond normal file flush and filesystem behavior.

These risks are accepted only for private Atlas research copies. Any future real-save writer,
recovery system, or external product must establish its own measured filesystem and transaction
profile and may not cite A2 qualification as evidence.

## 12. Locator redaction

`LocatorSegmentRedactor` accepts typed segments, never an untyped string locator.

It permits literal output only for:

- schema-defined document-role tokens;
- numeric array indexes; and
- JsonEx markers `@`, `@c`, `@a`, and `@r`.

The schema-safe literal-key allowlist remains empty. Schema and dynamic key inputs are assigned
separate, deterministic, survey-local aliases by ordinal sorting of distinct private keys:

- `schema-key-NNNNNN`; or
- `dynamic-key-NNNNNN`.

The private alias map contains the source keys and never enters Git or Agent input. Canonical output
contains only typed aliases. Unknown segment kinds, ambiguous key kinds, duplicate conflicting
mappings, exhausted alias ranges, or a literal key are safety failures.

## 13. Lifecycle and cleanup

`cleanup-preflight` reads a private inventory and produces a private, non-mutating eligibility
report. It names each artifact alias, current status, last-use milestone, planned disposition, and
reason it is eligible or blocked.

`cleanup-final` accepts an explicit private request containing:

- the expected inventory revision;
- the current approved milestone;
- the exact artifact aliases approved for deletion;
- the controlled survey root; and
- the output attestation path.

It deletes only artifacts whose inventory status and last-use milestone permit deletion, whose
paths remain inside the controlled non-reparse survey root, and whose aliases exactly match the
request. It records success or a classified block for every requested alias and atomically updates
the inventory and attestation.

A2 implements and tests final cleanup but does not run it on retained A2 corpus artifacts. Those
artifacts remain governed by the A0 lifecycle through A8.

## 14. Exact tracked implementation scope

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

Modified production files:

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

Repository-safe governance outputs created during implementation:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  reviews/atlas-v0-a2-tool-safety-review.md
  reviews/atlas-v0-a2-intake-approval.md
```

No other production, test, project, package, lock, schema, traversal, configuration, or repository
path changes in A2. If another tracked path becomes necessary, stop and revise this plan before
implementation continues.

## 15. Test and evidence plan

Tests use only hand-authored synthetic paths, manifests, bytes, keys, inventories, and temporary
directories. They never use the installed game, a real save, copied game content, or private
artifacts.

Direct tests cover:

- strict request JSON versions, required properties, unknown-property rejection, and malformed
  input;
- complete discovery accounting, sparse slots, both root roles, mandatory Steam metadata
  exclusion, definition groups, reparse observations, and pending confirmation;
- denial of copy for pending, rejected, superseded, changed, or mismatched manifests;
- ordinary trusted-local copy success, private hash fidelity, read-only destination, and completion
  rename;
- destination existence, sharing failure, source mutation, directory mutation, reparse,
  cancellation, short read, hash mismatch, and incomplete cleanup;
- no live source deletion, modification, decode, or scan;
- deterministic schema and dynamic key aliasing plus every literal-bypass failure;
- lifecycle eligibility, early-deletion refusal, exact-alias deletion, reparse refusal, inventory
  update, and attestation;
- exact CLI grammar, help, exit precedence, fixed bytes, path non-disclosure, and operation
  injection;
- direct cancellation-token propagation; and
- exact project file and dependency manifests.

Apphost tests cover one synthetic success and representative usage, safety, approval, cancellation,
and I/O failures without private input.

## 16. Execution stages

### A2.1 Implement without private access

Implement the exact tracked scope and run all repository-safe checks. Do not inspect the installed
game, live saves, the private workspace, or A0 private artifacts.

Commit and push an implementation safety candidate. A fresh independent subagent reviews its full
diff against this plan. Resolve every finding and repeat until `No findings`.

Persist `../reviews/atlas-v0-a2-tool-safety-review.md` as the only child change. That verified
record authorizes private A2 discovery with the reviewed tool bytes; it does not authorize copying.

### A2.2 Discover and obtain human approval

Run metadata-only discovery with the reviewed tool. Keep the exact manifest private. Reconcile it
against the A0 denominator and present only repository-safe aggregates and differences to the user.

Stop for explicit user approval of the exact local manifest. Persist the safe approval record and
run `intake-confirm`. Any rejection, unexpected entry, unsupported source, or changed denominator
stops copying.

### A2.3 Create and qualify copies

Run `intake-copy` only against the exact approved manifest and reviewed tool. Record private
provenance, copy hashes, inventory updates, and qualification evidence. Do not decode or scan the
copies.

Run cleanup preflight, but do not run final cleanup on retained corpus artifacts.

### A2.4 Release A2

Create a final repository-safe candidate containing the approved tracked scope and records. Validate
the complete cumulative diff from the A2 plan-review record.

A fresh independent subagent reviews the full exact candidate and safe aggregate acceptance
evidence. Resolve every finding and repeat until `No findings`.

Persist `../reviews/atlas-v0-a2-release-gate.md` as the only child change, verify it, push it, and
only then mark A2 complete.

## 17. Acceptance criteria

A2 is accepted only when:

1. the implementation matches the exact tracked scope and three-project boundary;
2. production projects retain zero project-local package references;
3. every request is strict, explicit, private, and free of ambient path discovery;
4. metadata-only discovery precedes content access or copy;
5. every source receives exactly one terminal status;
6. Steam cloud metadata is always excluded;
7. the exact private manifest receives explicit persisted human approval before copy;
8. live content is read only by the copy operation and never decoded or scanned;
9. trusted-local preflight rejects every visible reparse, outside-root, existing-destination, or
   unsupported condition;
10. every destination uses create-new semantics and passes independent private length and SHA-256
    verification;
11. source metadata and directory entry sets remain stable across copy;
12. failed intake leaves no usable partial corpus and removes only its owned incomplete directory;
13. locator redaction cannot emit a literal source key;
14. lifecycle preflight is non-mutating and final cleanup cannot delete before last use;
15. no private path, hash, value, name, source text, or request enters process output or Git;
16. cancellation and every classified failure produce the declared fixed diagnostic and exit code;
17. all targeted tests, locked restore, build, formatting, and apphost checks pass;
18. the implementation safety review reports `No findings` before private discovery;
19. the final independent review reports `No findings`; and
20. the release record is the verified record-only child of the accepted candidate and equals the
    shared branch tip.

## 18. Stop conditions

Stop A2 and revise the plan when:

- a required source is unsupported, unreadable, unclassified, or unexpectedly reparse-backed;
- the discovery denominator or approved selection rule changes without a new human decision;
- a copy requires full file-identity proof to distinguish a concrete observed alias;
- the controlled workspace is not a trusted local filesystem;
- the source cannot be held against concurrent write and delete access;
- source or directory state changes during copy;
- a private hash, path, name, value, source excerpt, or account identifier reaches Git or process
  output;
- redaction requires guessing that a literal key is safe;
- cleanup eligibility is ambiguous;
- a new package, project, schema, or tracked path is required;
- private execution would use code not covered by the tool-safety record;
- any validation is nondeterministic or cannot be reproduced with synthetic data; or
- any independent finding remains unresolved.

## 19. Validation

Run .NET commands from the repository root through `mise exec -- dotnet`.

The implementation candidate must pass:

- locked restore of the test project;
- warning-free build of the test project;
- `dotnet format --verify-no-changes` for the library, CLI, and test projects;
- the complete A2 test set through Microsoft.Testing.Platform;
- direct apphost smoke commands;
- evaluated project-reference and package-reference boundary checks;
- exact no-renames comparison of the cumulative tracked path set;
- ref-bound HK checks;
- `git diff --check`;
- committed-file line-length and LF checks;
- candidate tree, ancestry, upstream, and clean-worktree checks; and
- repository-safe inspection of the approval and tool-safety records.

No private path, hash, manifest, request, provenance payload, inventory payload, or copy may appear
in the command transcript retained as repository evidence.

## 20. Plan and release records

The plan candidate includes this plan, its `.copilot` index entry, and the two approved baseline
amendments. It receives fresh-context independent review until exact `No findings`.

The plan-review record path is `../reviews/atlas-v0-a2-plan-review.md`. It binds:

- the final plan commit and tree;
- the A1 release-record baseline;
- all reviewed and governing paths;
- the trusted-local-filesystem user decision;
- every plan-review iteration and disposition;
- plan validation outcomes; and
- the implementation diff base decision.

The implementation and release records follow sections 16 and 17 of
`project-operating-model.md`. Every record is prepared and independently reviewed as an exact staged
blob before it becomes authoritative, then committed unchanged as a record-only child and pushed.

## 21. Private outputs and lifecycle

Expected private outputs:

- discovery and approved intake manifest revisions;
- request documents;
- live-discovery metadata;
- copy hashes and source-stability evidence;
- save and definition copies;
- private provenance and inventory revisions;
- locator alias maps;
- cleanup preflight; and
- incomplete-operation evidence retained only when needed for diagnosis.

All remain under the protected Git-ignored Atlas workspace. Existing A0 lifecycle milestones and
dispositions remain authoritative. Newly created incomplete artifacts are removed on failed
operations only when ownership and containment checks pass.

Expected repository-safe outputs are limited to the exact tracked scope, plan/review/release
records, safe aliases, aggregate counts, public build identifiers, and non-sensitive outcomes.

## 22. Handoff

To resume A2:

1. read the root, project, and documentation `AGENTS.md` files;
2. read this plan, the A0 contract, Atlas execution plan, operating model, and A1 release record;
3. verify the A2 plan-review record, implementation diff base, upstream, and clean worktree;
4. identify the first incomplete A2 stage;
5. do not enter the private phase without the exact tool-safety record;
6. do not copy without the exact human approval record and approved private manifest; and
7. do not infer private state or authority from conversation history.
