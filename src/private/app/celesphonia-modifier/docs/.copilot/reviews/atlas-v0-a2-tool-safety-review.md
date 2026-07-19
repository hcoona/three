# Atlas V0 A2 Tool Safety Review

**Increment:** A2.1 - Trusted Local Intake Safety Harness

**Outcome:** Source-safety gate passed

**Final independent result:** `No findings`

**Source-safety candidate:** `e9927ef99f66f197930c44e98610909441f121ff`

**Candidate tree:** `3ccf579abd01ff043ad7b9d04c6f719da809408e`

**Implementation diff base:** `984a1375d510f4a293b4cf0c5984cffc99178fb1`

**Implementation-base tree:** `0598329738223dde1c811e0b92692e34fa0408ae`

**Approved plan commit:** `9fe0d708c1cb139060de931d773beb7c3bf02eac`

**Approved plan tree:** `e1e1b3ba20da47b5fb72bee82aea36f96496b03b`

**Governing plan:** `../plans/atlas-v0-a2-intake-safety-plan.md`

**Pinned SDK:** .NET SDK `10.0.300`

## 1. Exact-candidate binding

The final independent review examined the complete cumulative diff from the implementation base to
the source-safety candidate, not only the last remediation commit. The candidate equaled the shared
branch upstream, and its tracked worktree was clean.

The commit containing this record must:

1. use the source-safety candidate as its first parent;
2. change only this tool-safety record;
3. contain the independently reviewed staged blob unchanged; and
4. be pushed to the shared branch before private discovery begins.

Handoff verification compares the identifiers above with Git, checks the first-parent relationship,
confirms the single changed path and reviewed blob, requires the record commit to equal upstream,
and requires a clean tracked worktree.

Any tracked source, project, dependency, build-procedure, schema, test, or implementation-candidate
documentation change invalidates this gate and every downstream A2 private-run record. A2 must then
restart at A2.1.

This record authorizes the project leader to build and run the exact reviewed source from a clean
checkout for the human-operated metadata-only discovery in A2.2. It does not attest an untracked
binary digest, approve a pending manifest, authorize `intake-confirm`, or authorize copying.

## 2. Reviewer independence

Each iteration used a dedicated read-only `code-review` subagent that did not author the candidate:

| Iteration | Independent subagent    | Candidate  | Result         |
| --------: | ----------------------- | ---------- | -------------- |
|         1 | `atlas-a2-reviewer`     | `d78c6968` | 10 findings    |
|         2 | `atlas-a2-rereviewer`   | `ca2ad36f` | Seven findings |
|         3 | `atlas-a2-review-three` | `7abd7e11` | 10 findings    |
|         4 | `atlas-a2-review-four`  | `2a8cf407` | Six findings   |
|         5 | `atlas-a2-review-five`  | `1e90a6c8` | Four findings  |
|         6 | `atlas-a2-review-six`   | `81d9954e` | 10 findings    |
|         7 | `atlas-a2-review-seven` | `204a821f` | Nine findings  |
|         8 | `atlas-a2-review-eight` | `a9384cf5` | Two findings   |
|         9 | `atlas-a2-review-nine`  | `e9927ef9` | `No findings`  |

The final reviewer re-read the governing contracts and examined the entire 21-path implementation
candidate. Reviewers used only tracked repository content and synthetic test artifacts. They did
not inspect the installed game, live saves, the ignored private workspace, private requests,
private manifests, private hashes, copied content, or private source names.

## 3. Finding disposition

All 58 findings from iterations 1 through 8 were resolved, committed, pushed, and included in a
subsequent complete review.

### Iteration 1

- Enforced the exact authorized one-shot survey, revisions, and public corpus counts.
- Replaced line-presence Git-ignore checks with an exact fail-closed private-workspace policy.
- Reopened and strictly validated staged and replacement documents before promotion.
- Replaced revision-only completion with phase-specific custody-chain validation.
- Added deterministic discovery and confirmation recovery after inventory replacement.
- Validated recovery roots, exact copy census, receipts, hashes, lengths, and attributes before use.
- Bound receipts to the approved manifest alias, survey, digests, and exact corpus counts.
- Completed inventory-v1 validation and aligned cleanup-report schema and runtime behavior.
- Made locator alias maps immutable, closed, deterministic, and resistant to literal bypasses.
- Added publication, replacement, finalization, recovery, and schema-agreement tests.

### Iteration 2

- Removed an unauthorized test-helper path and restored the exact planned path boundary.
- Required canonical revision directories and rejected unauthorized revision artifacts.
- Validated fresh-copy output conflicts and the complete copy set before finalization.
- Enforced required and non-null JSON fields at every relevant nesting level.
- Corrected short relative-path and colon validation.
- Allowed global locator maps to redact mapped subsets while rejecting unseen keys.
- Expanded the deterministic fault, recovery, census, and nested schema test matrix.

### Iteration 3

- Evaluated definition candidates from frozen rules rather than baseline extension hints.
- Validated every bound source root before enumeration or access.
- Prohibited fresh copying after inventory-replacement evidence exists.
- Enforced staged-versus-final receipt ordering during recovery.
- Rejected leftover inventory staging after a completed replacement.
- Rejected null collection elements through contract exceptions rather than null dereferences.
- Rejected trailing empty relative-path segments.
- Distinguished absent optional state properties from schema-forbidden explicit nulls.
- Classified whitespace CLI request paths as invalid arguments.
- Rewrote strict-JSON tests as isolated, BOM-less, single-fault mutations.

### Iteration 4

- Implemented frozen A0 brace alternation, recursive matching, and ordered exclusion semantics.
- Validated request-file syntax and reparse safety before reading request bytes.
- Rejected pre-existing inventory backups before any fresh source access.
- Preserved potentially recoverable evidence on cancellation or indeterminate recovery I/O.
- Required the complete retained A0 baseline lifecycle tuple before state revision 1.
- Added deterministic cleanup-failure coverage through the bounded deletion seam.

### Iteration 5

- Preserved semantic definition-rule order through pending, approved, and copy revalidation.
- Required the exact A0 installation, executable, and two save-root relationships.
- Replaced recursive link-following census with reparse-safe top-directory traversal.
- Classified a missing canonical request file as the required I/O failure.

### Iteration 6

- Bound the exact public A0 slot, root, definition-group, rule, decision, count, and order tuples.
- Returned valid completed discovery without traversing removed or changed live sources.
- Recomputed and checked confirmation-recovery alias reservations and cursor advancement.
- Bound every state role to its exact canonical relative path.
- Derived cleanup results from the shared first-match lifecycle precedence.
- Rejected superscript Windows `COM` and `LPT` device-name forms.
- Corrected a lifecycle test so it reached the intended qualification invariant.
- Added required predecessor-state lineage to state revisions 3 and 4.
- Made snapshot rehashing incremental and cancellation-aware before publication.
- Made the exact-path boundary test recurse while pruning generated build outputs.

### Iteration 7

- Replaced intake binding roles in the locator redactor with the four schema-authorized roles.
- Bound every save source alias to the exact frozen A0 public contract.
- Recomputed discovery, copy, and preflight recovery aliases from predecessor cursors.
- Classified present but corrupt states as safety refusals rather than approval requirements.
- Recovered a valid inner staged receipt without reopening live sources.
- Cleared read-only attributes only on validated, request-owned partial evidence before cleanup.
- Propagated caller cancellation through A2 success-output writes.
- Restored the complete A1 cancellation and stream-failure precedence test matrix.
- Corrected the final-receipt ordering test so it reached the intended guard.

### Iteration 8

- Bound completed state-1 validation to exact baseline-to-pending definition identity.
- Independently recomputed completed-state aliases from predecessor inventory reservations.

### Iteration 9

- The reviewer reported `No findings`.

## 4. Reviewed repository paths

The cumulative candidate changed exactly these 21 authorized paths:

- the Atlas library directory, containing exactly `AtlasDiscovery.cs`,
  `AtlasIntakeContracts.cs`, `LocatorSegmentRedactor.cs`, `PrivateArtifactLifecycle.cs`, and
  `TrustedLocalCopy.cs`;
- the Atlas CLI directory, containing exactly `AtlasCliApplication.cs` and
  `AtlasCliOperations.cs`;
- `../README.md`;
- `../schemas/atlas-v0/`, containing exactly `cleanup-preflight-report.schema.json`,
  `copy-plan.schema.json`, `copy-receipt.schema.json`, `intake-state.schema.json`, and
  `source-root-map.schema.json`; and
- the Atlas test directory, containing exactly `AtlasCliApplicationTests.cs`,
  `AtlasDiscoveryTests.cs`, `AtlasIntakeContractTests.cs`, `AtlasProcessSmokeTests.cs`,
  `LocatorSegmentRedactorTests.cs`, `PrivateArtifactLifecycleTests.cs`,
  `ProjectBoundaryTests.cs`, and `TrustedLocalCopyTests.cs`.

No project, package, lock, target-framework, root build, or test-runner configuration changed.
Project-boundary tests recursively reject undeclared source, test, schema, project, and dependency
paths.

## 5. Source-safety scope and residual trust

The reviewed source implements the approved `trusted-local-filesystem/v1` model for the released
A0 survey and public corpus:

- 23 save-root entries: 21 included save files and two excluded Steam metadata entries;
- 580 definition candidates: 496 included files and 84 exclusions;
- Steam application `1786790`; and
- public build `13624401`.

The model trusts the project leader's fixed local Windows machine, ordinary local filesystem,
runtime, BCL, pinned SDK, locked dependencies, build tools, and honest user-controlled workspace.
It rejects malformed, nonlocal, outside-root, reparse-backed, ambiguous, or unexpected paths and
artifacts within the approved model.

The source establishes per-file point-in-time fidelity for a private research snapshot. It does not
establish a simultaneous corpus state, immutable storage, hostile local-race defense, Windows file
identity, volume or link-count proof, crash-atomic publication, or evidence for a future live-save
writer.

## 6. Acceptance evidence

The exact candidate provides:

1. strict request and output contracts with duplicate, unknown, trailing, missing, null, version,
   revision, vocabulary, path, count, digest, lineage, and role validation;
2. exact one-shot A0 survey, corpus, rule, root, decision, count, order, and alias binding;
3. deterministic pending and approved manifest publication with request-byte and predecessor
   custody;
4. canonical root maps, copy plans, receipts, inventory transitions, and four intake-state
   revisions;
5. state revision 3 as the sole qualified-copy signal after complete receipt, inventory, copy,
   digest, length, attribute, and path revalidation;
6. non-deleting cleanup preflight and the exact state-revision-4 custody transition;
7. validation-before-use for request files, source roots, revision directories, snapshots, and
   recovery artifacts;
8. point-in-time copying through read-only source opens, create-new destinations, disk flush,
   private SHA-256, independent destination reopening, and advisory read-only attributes;
9. phase-specific recovery that preserves captured evidence and never regenerates point-in-time
   evidence after inventory or receipt publication;
10. deterministic locator aliasing that emits only reviewed typed aliases and document-role tokens;
11. fixed CLI grammar, bytes, exit codes, privacy, cancellation, and stream-failure precedence;
12. BCL-only production code with no project-local package additions; and
13. synthetic tests for strict contracts, path policy, corpus accounting, publication seams,
    recovery combinations, injected failures, apphost behavior, and project boundaries.

## 7. Validation evidence

The following PowerShell procedure ran from the repository root against the exact candidate bytes:

```powershell
$projectRoot = "src\private\app\celesphonia-modifier"
$testRoot = "tests\private\app\celesphonia-modifier"
$library = "$projectRoot\Hcoona.CelesphoniaModifier.Atlas\" +
  "Hcoona.CelesphoniaModifier.Atlas.csproj"
$cli = "$projectRoot\Hcoona.CelesphoniaModifier.Atlas.Cli\" +
  "Hcoona.CelesphoniaModifier.Atlas.Cli.csproj"
$tests = "$testRoot\Hcoona.CelesphoniaModifier.Atlas.Tests\" +
  "Hcoona.CelesphoniaModifier.Atlas.Tests.csproj"

mise exec -- dotnet --version
mise exec -- dotnet restore $tests --locked-mode -v:minimal
mise exec -- dotnet build $tests --no-restore /m:1 -warnaserror -nologo -v:minimal
mise exec -- dotnet format $library --no-restore --verify-no-changes --verbosity minimal
mise exec -- dotnet format $cli --no-restore --verify-no-changes --verbosity minimal
mise exec -- dotnet format $tests --no-restore --verify-no-changes --verbosity minimal
mise exec -- dotnet test --project $tests --no-build --no-restore --verbosity minimal
mise exec -- dotnet test --project $tests --no-build --no-restore --verbosity minimal `
  --filter-class '*AtlasProcessSmokeTests'
mise exec -- dotnet msbuild $library `
  '-getItem:ProjectReference,PackageReference' -nologo
mise exec -- dotnet msbuild $cli `
  '-getItem:ProjectReference,PackageReference' -nologo
mise exec -- dotnet msbuild $tests `
  '-getItem:ProjectReference,PackageReference' -nologo
```

The procedure produced these outcomes:

- .NET SDK version was `10.0.300`.
- Locked restore reported all projects up to date and changed no tracked file.
- The warning-as-error build completed with zero warnings and zero errors.
- Format verification succeeded separately for the library, CLI, and test projects.
- The .NET 10 Microsoft.Testing.Platform run passed all 248 tests with zero failures or skips.
- The filtered direct apphost process run passed all 11 tests.
- Evaluated `ProjectReference` items were empty for the library, the library only for the CLI, and
  the library plus CLI for the test project.
- Evaluated production `PackageReference` items contained no project-local additions. The test
  project contained the exact approved direct test package set; only the repository-injected build
  packages also appeared in evaluation.
- Git-diff whitespace checks and the ref-bound EditorConfig, typo, JSON, Markdown, and commit-message
  hooks passed on each committed candidate.
- The cumulative no-renames diff contained exactly the 21 paths in section 4.
- The diff contained zero project, package, lock, target, property, or SDK changes.
- The candidate commit equaled upstream, and locked restore left the tracked worktree clean.

The build and tests validate source behavior under the trusted toolchain. They do not bind or attest
the digest of an untracked local executable.

## 8. Private-evidence statement

A2.1 created and accessed no private evidence. Implementation, testing, review, and record
preparation did not read or modify:

- the installed game;
- a live or copied save;
- the ignored private workspace;
- an A0 private artifact;
- a private request, manifest, path, hash, or source name; or
- a personal Steam identifier.

The candidate and this record contain only tracked repository content, synthetic test data,
repository-safe aliases, approved aggregate counts, public game-build identifiers, Git object
identifiers, and generated build outputs.

## 9. Gate decision and next authority

A2.1 passes the source-safety gate only when the commit containing this record satisfies section 1
and is pushed to the shared branch.

After that verification, the project leader may build and run the exact reviewed source from a
clean checkout to perform metadata-only `intake-discover`. Copilot and subagents must not receive or
inspect the private request path, pending manifest, source-root map, copy plan, state bytes, hashes,
source names, installed game, live saves, or private workspace.

Discovery does not authorize confirmation or copying. The project leader must report only the
repository-safe aggregates and difference categories allowed by the plan, then stop for explicit
approval of the exact local pending manifest. The independently reviewed and verified intake
approval record is required before `intake-confirm`; valid state revision 2 is additionally required
before `intake-copy`.
