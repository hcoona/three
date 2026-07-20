# Atlas V0 A2 Released-A0 Workspace Compatibility

**Lifecycle:** Active subordinate; acceptance evidence partially superseded by section 0,
implementation-governing only after verified shared `R2`, and released source-safety correction only
after verified shared `G`

**Status:** Gate-conditional; authority follows the exact verified shared tip, never document age or
presence

**Audience:** Project leader, implementers, independent reviewers, and future resumers

**Purpose:** Restore exact released-A0 workspace compatibility without reading or trusting opaque A0
content

**Increment:** A2R3 - Released-A0 Workspace Compatibility

**Decision owner:** Project leader

**Implementation language:** Existing C# on the repository-pinned .NET 10 SDK

**Correction base:** `5f1cf84d6de5966a40436ae16426415fe7d69231`

**Governing plans:**

- `atlas-v0-a0-research-contract.md`
- `atlas-v0-a2-intake-safety-plan.md`
- `atlas-v0-a2-post-patch-baseline-correction.md`

**Planned plan-review record:**
`../reviews/atlas-v0-a2-released-a0-workspace-compatibility-plan-review.md`

**Planned release-gate record:**
`../reviews/atlas-v0-a2-released-a0-workspace-compatibility-release-gate.md`

## 0. Approved proportionality amendment

The project leader approved this amendment after an independent minimality audit found the
production correction proportionate but the planned synthetic evidence significantly
overengineered. This section supersedes only conflicting acceptance-evidence breadth in sections 2,
3, 5, 6, 9, and 10. The exact-name correction, fail-closed census, privacy boundary, validation
commands, stop conditions, candidate paths, and release authority remain unchanged.

The focused new synthetic evidence is:

1. one discovery case with every exact retained A0 entry and nested opaque sentinels, proving
   successful admission without reading, opening, or enumerating descendant content;
2. one theory proving near-match rejection in the survey root, `intake`, and `copies`;
3. one theory proving wrong-type rejection at those three boundaries; and
4. one representative reparse-point rejection proving that the unchanged census guard still applies
   to an admitted A0 name.

The existing full suite remains the regression evidence for the shared census and later lifecycle
behavior. This increment does not require a lifecycle/rerun cross-product, synthetic legacy
inventory rows, generated-document binding assertions, a separate device case, or an exhaustive
all-I/O-operation guard. Test-only production layout properties are also unnecessary. These
omissions reduce redundant proof; they do not authorize any operation on retained A0 content.

The immutable evidence chain is now `B -> P -> R -> P2 -> P3 -> R2 -> I -> G`:

- `B`, `P`, and `R` retain the identifiers and historical evidence defined below;
- `P2` is the initial amendment candidate and direct child of `R`;
- `P3` is the direct child of `P2`, changes only this plan, and resolves the independent `P2`
  review findings;
- `R2` is the direct child of `P3` and changes only the existing plan-review record;
- `I` is the direct child of `R2`; and
- `G` is the direct child of `I`.

The amendment review binds `P2` and its findings, plus the exact `P3` commit, tree, plan blob,
cumulative changed path, dispositions, and final `No findings` result. Future references below to
the current plan candidate or plan-review record mean `P3` and `R2` respectively. The release record
binds all eight roles.

Use these ancestry checks instead of the conflicting four-role checks in section 9:

```powershell
$expectedB = "5f1cf84d6de5966a40436ae16426415fe7d69231"
$expectedP = "e322f635d3847e2fe738a2d97a939940b63d941e"
$expectedR = "e1d828315cda967dccaea1dbcc049a6814c4da55"
$expectedP2 = "81947411e32cf51ff6a194e62a46cdae7eccdacf"

$I = git rev-parse HEAD
$R2 = git rev-parse "$I^"
$P3 = git rev-parse "$R2^"
$P2 = git rev-parse "$P3^"
$R = git rev-parse "$P2^"
$P = git rev-parse "$R^"
$B = git rev-parse "$P^"

if ($B -cne $expectedB -or $P -cne $expectedP -or $R -cne $expectedR) {
  throw "The original plan chain is invalid."
}
if ($P2 -cne $expectedP2) { throw "The initial amendment candidate is invalid." }
```

Before `R2`, apply the same derivation from `P3` through `B`, require `P3^ == P2`, and require
`R..P3` to modify only this plan. Require `R2^ == P3` and `P3..R2` to modify only the existing
plan-review record. All existing tree, blob, upstream, index, worktree, path, HK, and formatting
checks still apply to their corresponding revised roles.

The current resume procedure supersedes conflicting steps in section 10:

1. verify and independently review the pushed `P3`;
2. update and independently review the existing plan-review record, then commit and verify `R2`;
3. implement the focused candidate and run the unchanged validation commands;
4. commit and verify `I`, then independently review its complete candidate until `No findings`;
5. commit and verify the record-only `G`; and
6. return the unchanged private request for one metadata-only discovery retry.

## 1. Problem and evidence

The released A0 contract requires the private survey workspace to retain:

- `intake/private-provenance.json`;
- the approved preservation snapshot directory `copies/save-snapshot-20260717T210224Z`; and
- the top-level `decoded`, `evidence`, `agent-envelopes`, and `validation` directories.

The original A2 synthetic workspace contains none of those released-A0 entries.
`ValidateCommandWorkspaceCensus` consequently accepts only the synthetic A2 layout and rejects the
real released-A0 layout before live source-path validation. The CLI correctly reduces that rejection
to `Safety check failed.`, but the check makes the released A2 tool unusable with its approved A0
input.

This diagnosis uses only repository-safe contracts and source. It does not inspect the private
request, workspace, installed game, saves, hashes, manifests, inventory, or generated output.

## 2. Decision

A2 will recognize the exact retained A0 entries as opaque, pre-existing evidence:

- the intake census admits the exact `private-provenance.json` file;
- the survey-root census admits the exact four legacy directory names; and
- the copies census admits the exact approved preservation snapshot directory name.

The existing top-level census still requires each admitted entry to be ordinary and non-reparse.
Beyond that required top-level type and attribute check, A2 does not read, open, enumerate, hash,
write, move, delete, validate, promote, copy, decode, mutate, or request descendant metadata from
the private-provenance file, opaque A0 directories, or preservation snapshot.

Synthetic I/O-seam tests will fail if A2 performs any prohibited operation on the private-provenance
file or below an opaque directory. Generated A2 manifests, root maps, copy plans, state bindings,
receipts, and new inventory rows must not bind an opaque A0 path. Opaque sentinels and pre-existing
A0 inventory rows must remain byte- and field-unchanged and unqualified through state revisions 1–4
and idempotent reruns. This correction neither reclassifies nor regenerates them.

All A2-managed namespaces retain their existing exact census. Unknown siblings, near-match names,
unexpected revisions, unexpected copy directories, files where directories are required, and
reparse-backed entries remain safety failures.

## 3. Scope

### In scope

- Align the active A2 plan with the retained released-A0 workspace contract.
- Update the `.copilot` index with gate-dependent A2R3 lifecycle and navigation.
- Add repository-safe constants for the exact retained A0 names.
- Admit only those exact names at the existing top-level census boundaries.
- Add synthetic I/O-seam tests for the released-A0 layout, non-recursive opacity, wrong entry types,
  reparse/device rejection, and near-match rejection.
- Exercise discovery, confirmation, copy qualification, cleanup preflight, and idempotent reruns
  through state revisions 1–4 with the retained A0 entries present.
- Re-run the complete original A2 validation set plus the new tests.
- Independently review the exact plan and release candidates until `No findings`.
- Resume the already-approved metadata-only discovery only after the release record is committed and
  pushed.

### Out of scope

- Reading or changing any private file or directory.
- Inspecting the generated request or any live game/save content.
- Changing A0 roots, counts, selection rules, aliases, decisions, or baseline identity.
- Scanning or qualifying the preservation snapshot.
- Private confirmation, copying, decoding, cleanup, deletion, or live-save writes.
- New requests, schemas, commands, states, receipts, manifests, or inventory fields.
- Installer, package, patch, updater, or installation-history provenance.
- Relaxing the census for unknown names or recursively accepting arbitrary workspace content.

## 4. Exact candidate paths

The plan candidate adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/plans/
  atlas-v0-a2-released-a0-workspace-compatibility.md
```

The plan-review record child adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-released-a0-workspace-compatibility-plan-review.md
```

The implementation candidate may change only:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/
    AtlasDiscovery.cs
    AtlasIntakeContracts.cs
  docs/.copilot/
    README.md
  docs/.copilot/plans/
    atlas-v0-a2-intake-safety-plan.md
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/
    AtlasDiscoveryTests.cs
```

The release-gate record child adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-released-a0-workspace-compatibility-release-gate.md
```

No `.private` path belongs to any candidate.

The active A2 plan must name the exact retained A0 entries, define the opaque top-level boundary,
state that it applies to every A2 command and rerun, and preserve all original private authority
limits. The index must identify this plan and its plan-review record as active subordinate evidence,
state that A2R2 remains the private-run authority until `G`, and state that only metadata discovery
resumes after verified `G`. Neither document may imply that `I` alone passed the gate.

## 5. Git evidence chain

The correction uses these exact immutable roles:

- `B` is correction base `5f1cf84d6de5966a40436ae16426415fe7d69231`.
- `P` is the pushed plan commit descended directly from `B`; `B..P` adds only this plan.
- `R` is the direct child of `P`; it adds only the independently reviewed plan-review record, with
  the reviewed staged blob committed unchanged.
- `I` is the direct child of `R` and the final pushed implementation candidate; `R..I` changes
  exactly the implementation paths in section 4.
- `G` is the direct child of `I`; it adds only the independently reviewed release-gate record, with
  the reviewed staged blob committed unchanged.

Before the next role proceeds, the current role must equal upstream and the index and tracked and
untracked worktree must be clean. The plan review binds `P`, its tree, plan blob, correction base,
reviewed sources, findings, dispositions, validation, and exact `No findings` decision. Any plan
change after review creates a new `P`.

Every plan-review iteration records the reviewer role or subagent identifier, an independence
attestation, findings, and dispositions. The final iteration records exact `No findings`; summary
prose without those per-iteration bindings cannot authorize `R`.

The release record binds:

- increment A2R3, outcome, and renewed metadata-only source-safety authority;
- `B`, `P`, `R`, and `I` commits and the `I` tree;
- governing plan path and persisted-plan commit;
- exact cumulative candidate path set;
- reviewer identity and independence attestation;
- every review iteration, finding disposition, and final exact `No findings`;
- validation commands and repository-safe outcomes;
- a statement that no private evidence was accessed or recorded; and
- the direct-parent, record-only path, unchanged-blob, upstream, index, and worktree checks.

`G` renews only the existing A2 metadata-only discovery authority. It is not final A2 completion and
does not authorize confirmation, copy, decoding, cleanup, deletion, or writes.

## 6. Acceptance criteria

The implementation candidate is acceptable only when all of the following are true:

1. The released A0 private-provenance filename, four legacy root directory names, and preservation
   snapshot directory name are represented by exact repository-safe constants.
2. A synthetic full A2 lifecycle and its idempotent reruns succeed with all exact retained A0
   entries and nested opaque sentinel content present through state revisions 1–4.
3. I/O-seam assertions prove that no descendant of a legacy directory or preservation snapshot is
   read, opened, enumerated, hashed, written, moved, deleted, or queried for metadata.
4. I/O-seam assertions prove that `private-provenance.json` receives only its required top-level
   type and attribute check and no read, open, hash, write, move, delete, or content validation.
5. Opaque sentinels and pre-existing A0 inventory rows remain unchanged and unqualified through all
   revisions and reruns.
6. Generated A2 document bindings and new inventory rows contain no opaque A0 relative path.
7. A near-match legacy root name, private-provenance filename, or preservation snapshot name remains
   a safety failure at every applicable phase.
8. An exact legacy name with the wrong file/directory type, or a reparse/device-backed top-level
   entry, remains a safety failure.
9. Unknown siblings and every A2-managed directory census remain rejected exactly as before.
10. The A2 plan and index contain the exact gate-dependent authority and navigation wording required
    by section 4.
11. No private source, request, workspace, manifest, inventory, hash, save, or output is read by an
    agent or test.
12. Every original A2 test remains present and enabled, with no deletion, rename, or skip added.
13. SDK detection, locked restore, warning-as-error build, formatting, complete
    Microsoft.Testing.Platform, filtered apphost smoke, project/package-reference, ref-bound HK,
    `git diff --check`, LF, BOM, line-length, candidate-path, ancestry, tree, upstream, index, and
    worktree checks pass.
14. The complete and filtered test runs report zero failures and zero skipped tests; the release
    record preserves their final passed counts.
15. The committed implementation candidate has an independent full-candidate review result of exact
    `No findings`.
16. The record-only release-gate commit is the implementation candidate's direct child, changes only
    the release record, and is pushed as the shared branch tip before private discovery is retried.

## 7. Stop conditions

Stop and return to planning if:

- compatibility requires reading private evidence or deriving an unrecorded private name;
- an admitted A0 entry overlaps an A2-managed output path;
- the fix would recursively trust arbitrary legacy content;
- a request, schema, state, inventory, copy, or cleanup contract must change;
- the observed failure persists after this exact compatibility correction; or
- review identifies a broader authority or safety-model disagreement.

Do not diagnose a later failure by disclosing raw private paths, hashes, names, granular counts, or
document bytes.

## 8. Dependencies, outputs, risks, and authority

Dependencies:

- released A0 contract and scope-review evidence;
- original A2 source and tool-safety evidence;
- A2R2 release gate at correction base `5f1cf84d6de5966a40436ae16426415fe7d69231`;
- repository-pinned .NET 10 SDK through `mise`; and
- Microsoft.Testing.Platform with xUnit v3.

This plan grants no implementation authority until its reviewed plan and plan-review record are
committed and pushed. The implementation and release records grant no confirmation, copy, decode,
cleanup, deletion, or write authority.

Only the project leader may retry the private metadata-only discovery after the release gate.

Repository-safe outputs are this plan, its plan-review record, the bounded C# and active-plan
correction, synthetic tests, validation outcomes, and the release-gate record. The correction
creates no private output. The existing private request remains project-leader-controlled and
unchanged.

The unresolved risk is that a later independent safety check may fail after this deterministic
census mismatch is removed. That possibility does not authorize broader compatibility or private
inspection; section 7 remains the stop boundary.

## 9. Validation procedure

From the repository root:

```powershell
$projectRoot = "src\private\app\celesphonia-modifier"
$testRoot = "tests\private\app\celesphonia-modifier"
$library = "$projectRoot\Hcoona.CelesphoniaModifier.Atlas\Hcoona.CelesphoniaModifier.Atlas.csproj"
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

The validation uses only synthetic temporary workspaces.

Immediately after `P` is committed and pushed, and before authoring `R`, verify:

```powershell
$B = "5f1cf84d6de5966a40436ae16426415fe7d69231"
$P = git rev-parse HEAD
$planPath = "src/private/app/celesphonia-modifier/docs/.copilot/plans/" +
  "atlas-v0-a2-released-a0-workspace-compatibility.md"

if ((git rev-parse "$P^") -cne $B) { throw "The plan parent is invalid." }
$planChanges = @(git diff --no-renames --name-status $B $P)
if ($planChanges.Count -ne 1 -or $planChanges[0] -cne "A`t$planPath") {
  throw "The plan candidate path set is invalid."
}
$planTree = git rev-parse "$P^{tree}"
$planBlob = git rev-parse "${P}:$planPath"
$upstream = git rev-parse "@{upstream}"
if ($LASTEXITCODE -ne 0 -or $upstream -cne $P) {
  throw "The plan candidate is not the shared branch tip."
}
$status = @(git status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) {
  throw "The plan candidate worktree is not clean."
}
```

The independent plan review and staged record review bind `$B`, `$P`, `$planTree`, `$planBlob`, and
the exact staged record blob. After committing `R`, verify `R^ == P`, `P..R` adds only the record,
the committed record blob equals the reviewed staged blob, and `R` is the clean shared branch tip.
No implementation begins before those checks pass.

After `I` is committed, verify the immutable candidate:

```powershell
$expectedB = "5f1cf84d6de5966a40436ae16426415fe7d69231"
$I = git rev-parse HEAD
$R = git rev-parse "$I^"
$P = git rev-parse "$R^"
$B = git rev-parse "$P^"

if ($B -cne $expectedB) { throw "The correction base is invalid." }
if ((git rev-parse "$P^") -cne $B) { throw "The plan parent is invalid." }
if ((git rev-parse "$R^") -cne $P) { throw "The plan-review parent is invalid." }
if ((git rev-parse "$I^") -cne $R) { throw "The implementation parent is invalid." }
git --no-pager diff --check $R $I
if ($LASTEXITCODE -ne 0) { throw "Git rejected the candidate diff." }
mise exec -- hk check --check --no-progress --from-ref $R --to-ref $I
if ($LASTEXITCODE -ne 0) { throw "HK rejected the candidate." }

$upstream = git rev-parse "@{upstream}"
if ($LASTEXITCODE -ne 0 -or $upstream -cne $I) {
  throw "The implementation candidate is not the shared branch tip."
}
$status = @(git status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) {
  throw "The implementation candidate worktree is not clean."
}
```

Validate the exact `R..I` path set against section 4. Read each committed candidate blob rather than
the moving worktree, reject UTF-8 BOM or CRLF, and reject Markdown lines longer than 100 characters.
C# line layout is governed by the successful `dotnet format --verify-no-changes` result.

Repeat direct-parent, exact-path, staged-blob, tree, upstream, index, and worktree checks for
`I..G`. No check may be deferred past the next role.

## 10. Resume procedure

1. Verify the correction base, upstream, and clean worktree.
2. Review this plan against the released A0 and A2 contracts.
3. Commit and push unchanged `P`; immediately run section 9's post-`P` checks.
4. Add and independently review the staged plan-review record, commit and push `R`, then immediately
   verify the `P..R` direct-parent, path, blob, tree, upstream, index, and worktree gates.
5. Mark A2R3 in progress and implement only the exact candidate paths.
6. Run section 9's SDK, restore, build, format, test, smoke, and reference commands.
7. Commit and push `I`; immediately run section 9's post-`I` candidate checks.
8. Independently review the complete committed `I` until exact `No findings`.
9. Add and independently review the staged release record, commit and push `G`, then immediately
   verify the `I..G` direct-parent, path, blob, tree, upstream, index, and worktree gates.
10. Return the unchanged private request to the project leader for one metadata-only discovery
    retry.

If another `Safety check failed.` result occurs, stop at the failed boundary and create a new
repository-safe diagnosis; do not broaden this correction speculatively.
