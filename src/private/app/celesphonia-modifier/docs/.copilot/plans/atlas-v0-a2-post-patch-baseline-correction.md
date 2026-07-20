# Atlas V0 A2 Post-Patch Baseline Correction

**Status:** Proposed correction; no execution or private-run authority

**Increment:** A2R2 - Post-Patch Baseline Correction

**Decision owner:** Project leader

**Implementation language:** Existing C# on the repository-pinned .NET 10 SDK

**Correction base:** `112b05d80712469100dd834ecca74fd2acba4639`

**Historical unchanged-source record:**
`9edbd57b4f44e76de321e06be81a581ed11b0017`

**Planned plan-review record:**
`../reviews/atlas-v0-a2-post-patch-baseline-plan-review.md`

**Planned release-gate record:**
`../reviews/atlas-v0-a2-post-patch-baseline-release-gate.md`

## 1. Decision and rationale

The Atlas corpus baseline is the installed file tree that was observed after an off-tree patch was
applied. Atlas identifies and freezes that baseline through its approved roots, selection rules,
private manifests, and per-file copy evidence.

The origin of each installed byte is not part of A2 intake identity. A patch package, installer
hash, or installation-history attestation cannot prove the resulting installed tree and is
unnecessary for the trusted-local, human-operated, read-only discovery and copy workflow.

The earlier official-patch amendment made a category error: it treated descriptive source history as
a supply-chain authorization problem. Its proposed implementation added package hashing, repeated
attestation, request and review receipts, a child-process launcher, terminal custody, new schemas,
and recovery state machines without changing the corpus, read-only operation, or copy-safety need.

The project leader rejected that expansion before it was committed. This plan restores proportional
governance and the unchanged original A2 implementation.

## 2. Authority and deletion

The patch-provenance amendment and its plan-review document are removed from the current tree. Their
Git history is sufficient provenance for the abandoned direction. Neither document has forward
authority.

The following remain governing:

- the finite A0 roots, rules, counts, aliases, decisions, privacy, and reopening conditions;
- the original A2 trusted-local-filesystem profile and accepted residual risks;
- read-only source access and copying only into protected Git-ignored storage;
- exact private manifest approval by the project leader;
- per-file held-handle copy, length, and digest evidence;
- locator redaction, strict contracts, lifecycle preflight, and no deletion;
- no live-save writes, decoding, semantic claims, or future writer authority; and
- the original reviewed tool-safety evidence;
- independent review and record-only release gates.

The original tool-safety record resumes private-run authority only after this correction's release
gate verifies that the reviewed source bytes are unchanged.

## 3. Baseline model

The active A2 baseline model is:

```text
approved observed roots
  + frozen selection rules
  + reviewed private manifest
  + copied-file fidelity evidence
```

Steam application `1786790`, public build `13624401`, and game version `1.05` remain repository-safe
descriptive identifiers from A0. A2 requires no patch metadata.

The baseline changes only when an A0 reopening condition changes or the observed private manifest
differs from the approved finite corpus. Package availability, package hash, installation sequence,
and later reconstruction of source history do not define baseline equality.

This model does not claim:

- that Steam plus a named patch can reproduce the installed tree;
- that a package hash proves installed-file identity;
- that all source files represent one simultaneous point in time;
- hostile-local-race resistance beyond `trusted-local-filesystem/v1`; or
- compatibility or write authority for a future save editor.

## 4. Scope

### In scope

- Correct the normative A0/A2 source-baseline wording.
- Delete the patch-provenance amendment and its plan review from the current tree.
- Reject and remove the complete uncommitted 23-path implementation candidate.
- Restore a clean worktree whose production, schema, and test files equal the historical A2 source.
- Re-run the unchanged original A2 validation and all 248 synthetic tests.
- Independently review the exact corrected candidate.
- Publish a record-only correction release gate before private discovery.

### Out of scope

- Any production, schema, project, package, lock, SDK, TFM, or test change.
- Any installer path, installer hash, package retention, or installation-history requirement.
- Any new CLI command, request, receipt, custody, launcher, state, or recovery contract.
- Any inspection of the installed game, live saves, retained installer, private workspace, or
  private request.
- Any A0 corpus change, new discovery result, confirmation, copy, cleanup, or private acceptance.
- Compatibility fingerprints, supported patch matrices, save writing, rollback, release signing,
  distribution, or updater provenance.

## 5. Exact plan candidate

The correction plan candidate may change only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a0-research-contract.md
    atlas-v0-a2-intake-safety-plan.md
    atlas-v0-a2-post-patch-baseline-correction.md
    atlas-v0-a2-patch-provenance-amendment.md (deleted)
  reviews/
    atlas-v0-a2-patch-provenance-plan-review.md (deleted)
```

The plan-review record child may add only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-post-patch-baseline-plan-review.md
```

The release-gate record child may add only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-post-patch-baseline-release-gate.md
```

## 6. Execution procedure

Commit and push `D` while retaining the rejected candidate residue:

1. stage only the reviewed plan-review record;
2. verify the staged path and blob, then commit and push the record unchanged;
3. verify `D` parent, path, blob, tree, and upstream equality;
4. explicitly defer only the clean-worktree gate; and
5. stop if any residue path is staged or falls outside the literal lists below.

The exact modified tracked residue is:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Cli/
    AtlasCliApplication.cs
    AtlasCliOperations.cs
  Hcoona.CelesphoniaModifier.Atlas/
    AtlasDiscovery.cs
    AtlasIntakeContracts.cs
    PrivateArtifactLifecycle.cs
    TrustedLocalCopy.cs
  docs/.copilot/schemas/atlas-v0/
    copy-receipt.schema.json
    intake-state.schema.json
    source-root-map.schema.json
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/
    AtlasCliApplicationTests.cs
    AtlasDiscoveryTests.cs
    AtlasIntakeContractTests.cs
    AtlasProcessSmokeTests.cs
    PrivateArtifactLifecycleTests.cs
    ProjectBoundaryTests.cs
    TrustedLocalCopyTests.cs
```

The exact untracked residue is:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Cli/AtlasFixedOutputLauncher.cs
  Hcoona.CelesphoniaModifier.Atlas/AtlasRequestPreparation.cs
  docs/.copilot/schemas/atlas-v0/
    request-preparation-receipt.schema.json
    request-review-receipt.schema.json
    request-terminal-custody.schema.json
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasRequestPreparationTests.cs
```

After provisional `D` verification:

1. require `git diff --cached --quiet`;
2. require porcelain status to equal exactly the 16 modified and six untracked paths above;
3. restore the 16 tracked paths literally from `HEAD`;
4. remove the six untracked files individually with `Remove-Item -LiteralPath`;
5. preserve the committed correction `README.md`;
6. require empty porcelain status, completing `D`'s clean-worktree gate;
7. compare the historical source path set in section 7 with the historical commit;
8. run the original A2 validation from the clean worktree;
9. obtain a fresh independent `No findings` review of exact committed candidate `S`; and
10. prepare, review, commit, push, and verify the record-only release-gate child `G`.

The cleanup procedure must name every file literally. It must not use recursive deletion,
wildcards, `git reset --hard`, or broad checkout commands.

Run the cleanup from the repository root with these literal arrays:

```powershell
$projectRoot = "src\private\app\celesphonia-modifier"
$testRoot = "tests\private\app\celesphonia-modifier"
$library = "$projectRoot\Hcoona.CelesphoniaModifier.Atlas"
$cli = "$projectRoot\Hcoona.CelesphoniaModifier.Atlas.Cli"
$schemas = "$projectRoot\docs\.copilot\schemas\atlas-v0"
$tests = "$testRoot\Hcoona.CelesphoniaModifier.Atlas.Tests"

$trackedResidue = @(
  "$cli\AtlasCliApplication.cs"
  "$cli\AtlasCliOperations.cs"
  "$library\AtlasDiscovery.cs"
  "$library\AtlasIntakeContracts.cs"
  "$library\PrivateArtifactLifecycle.cs"
  "$library\TrustedLocalCopy.cs"
  "$schemas\copy-receipt.schema.json"
  "$schemas\intake-state.schema.json"
  "$schemas\source-root-map.schema.json"
  "$tests\AtlasCliApplicationTests.cs"
  "$tests\AtlasDiscoveryTests.cs"
  "$tests\AtlasIntakeContractTests.cs"
  "$tests\AtlasProcessSmokeTests.cs"
  "$tests\PrivateArtifactLifecycleTests.cs"
  "$tests\ProjectBoundaryTests.cs"
  "$tests\TrustedLocalCopyTests.cs"
)

$untrackedResidue = @(
  "$cli\AtlasFixedOutputLauncher.cs"
  "$library\AtlasRequestPreparation.cs"
  "$schemas\request-preparation-receipt.schema.json"
  "$schemas\request-review-receipt.schema.json"
  "$schemas\request-terminal-custody.schema.json"
  "$tests\AtlasRequestPreparationTests.cs"
)

git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
  throw "Staged residue is not allowed."
}

$expected = @(
  $trackedResidue | ForEach-Object { " M " + $_.Replace("\", "/") }
  $untrackedResidue | ForEach-Object { "?? " + $_.Replace("\", "/") }
)
$actual = @(git status --porcelain=v1)
if ($LASTEXITCODE -ne 0) {
  throw "Git status failed."
}
if ((($actual | Sort-Object) -join "`n") -ne
    (($expected | Sort-Object) -join "`n")) {
  throw "Unexpected worktree residue."
}

git restore --source=HEAD --worktree -- $trackedResidue
if ($LASTEXITCODE -ne 0) {
  throw "Tracked cleanup failed."
}
foreach ($path in $untrackedResidue) {
  Remove-Item -LiteralPath $path
}
$remaining = @(git status --porcelain=v1)
if ($LASTEXITCODE -ne 0) {
  throw "Git status failed."
}
if ($remaining.Count -ne 0) {
  throw "The worktree is not clean."
}
```

## 7. Git evidence chain

The correction uses these exact roles:

- `B` is correction base `112b05d80712469100dd834ecca74fd2acba4639`.
- `C` is the pushed correction-plan candidate descended from `B`. `B..C` changes exactly the six
  plan-candidate paths in section 5.
- `D` is the immediate child of `C`. It adds only the independently reviewed
  `atlas-v0-a2-post-patch-baseline-plan-review.md` blob unchanged.
- `S` equals `D`. It is the corrected committed source candidate because no source byte changes.
- `G` is the immediate child of `S`. It adds only the independently reviewed
  `atlas-v0-a2-post-patch-baseline-release-gate.md` blob unchanged.

Every role must be pushed and equal the shared upstream before the next role proceeds. `D` first
passes parent, path, staged-blob, tree, and upstream verification while the exact residue is
retained. Literal cleanup then completes its clean-worktree gate before validation or source
review. `G` must pass every gate without a deferral.

Any source, schema, project, package, lock, SDK, TFM, test, or unplanned documentation change
between `C` and `G` invalidates the chain.

### 7.1 Exact unchanged-source set

Only these 20 historical production, schema, and test paths participate in source-byte equality:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Cli/
    AtlasCliApplication.cs
    AtlasCliOperations.cs
  Hcoona.CelesphoniaModifier.Atlas/
    AtlasDiscovery.cs
    AtlasIntakeContracts.cs
    LocatorSegmentRedactor.cs
    PrivateArtifactLifecycle.cs
    TrustedLocalCopy.cs
  docs/.copilot/schemas/atlas-v0/
    cleanup-preflight-report.schema.json
    copy-plan.schema.json
    copy-receipt.schema.json
    intake-state.schema.json
    source-root-map.schema.json
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/
    AtlasCliApplicationTests.cs
    AtlasDiscoveryTests.cs
    AtlasIntakeContractTests.cs
    AtlasProcessSmokeTests.cs
    LocatorSegmentRedactorTests.cs
    PrivateArtifactLifecycleTests.cs
    ProjectBoundaryTests.cs
    TrustedLocalCopyTests.cs
```

After cleanup, run:

```powershell
$historical = @(
  "$cli\AtlasCliApplication.cs"
  "$cli\AtlasCliOperations.cs"
  "$library\AtlasDiscovery.cs"
  "$library\AtlasIntakeContracts.cs"
  "$library\LocatorSegmentRedactor.cs"
  "$library\PrivateArtifactLifecycle.cs"
  "$library\TrustedLocalCopy.cs"
  "$schemas\cleanup-preflight-report.schema.json"
  "$schemas\copy-plan.schema.json"
  "$schemas\copy-receipt.schema.json"
  "$schemas\intake-state.schema.json"
  "$schemas\source-root-map.schema.json"
  "$tests\AtlasCliApplicationTests.cs"
  "$tests\AtlasDiscoveryTests.cs"
  "$tests\AtlasIntakeContractTests.cs"
  "$tests\AtlasProcessSmokeTests.cs"
  "$tests\LocatorSegmentRedactorTests.cs"
  "$tests\PrivateArtifactLifecycleTests.cs"
  "$tests\ProjectBoundaryTests.cs"
  "$tests\TrustedLocalCopyTests.cs"
)
$historicalCommit = "9edbd57b4f44e76de321e06be81a581ed11b0017"
$readme = "$projectRoot\docs\.copilot\README.md"
$C = git rev-parse "HEAD^"

git diff --exit-code --no-renames $historicalCommit HEAD -- $historical
if ($LASTEXITCODE -ne 0) {
  throw "Committed historical source differs."
}
git diff --exit-code HEAD -- $historical
if ($LASTEXITCODE -ne 0) {
  throw "Worktree historical source differs."
}
git diff --exit-code $C HEAD -- $readme
if ($LASTEXITCODE -ne 0) {
  throw "README differs from the correction candidate."
}
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
  throw "The index is not clean."
}
$remaining = @(git status --porcelain=v1)
if ($LASTEXITCODE -ne 0) {
  throw "Git status failed."
}
if ($remaining.Count -ne 0) {
  throw "The worktree is not clean."
}
```

## 8. Acceptance criteria

The correction passes only when:

1. `B..C` changes exactly the six paths in section 5;
2. the plan states that the observed post-patch file tree is the baseline;
3. no package or installation-history evidence is required for A2;
4. a fresh independent plan reviewer reports exact `No findings`;
5. `D` is the record-only child of `C`, provisionally verified before literal cleanup and fully
   verified afterward;
6. the rejected 23-path implementation is absent from the worktree and Git history;
7. the 20 historical source paths in section 7 are byte-identical to `9edbd57b`;
8. locked restore and warning-free build pass;
9. `dotnet format --verify-no-changes` passes for the library, CLI, and tests;
10. Microsoft.Testing.Platform reports exactly 248 passed, zero failed, and zero skipped;
11. the filtered direct-apphost smoke suite reports exactly 11 passed;
12. evaluated project and package references match the historical record;
13. candidate-path HK, LF, line-length, and `git diff --check` gates pass;
14. no private or original data is accessed during correction;
15. a fresh independent source reviewer reports exact `No findings`;
16. `G` is the verified record-only child of `S` and resumes the original A2 private-run
    authority; and
17. the branch equals upstream with a clean tracked and untracked worktree.

## 9. Stop conditions

Stop and return to planning if:

- restoring the rejected candidate would remove any path outside its exact 23-path boundary;
- a historical source, schema, test, project, package, lock, SDK, or TFM byte differs;
- the unchanged suite does not report exactly 248 passing tests;
- any A0 root, rule, count, alias, decision, or private-manifest expectation changes;
- private discovery or any private artifact access would be required to validate the correction;
- review finds that package provenance controls an actual A2 hazard not already covered by the
  observed baseline and copy-fidelity model; or
- any independent finding remains unresolved.

## 10. Outputs and handoff

Repository-safe outputs:

- this correction plan and its plan-review record;
- the unchanged-source validation result;
- the final record-only correction release gate; and
- exact public Git commit, tree, parent, path, and test-count evidence.

There is no private output.

To resume, verify `C` and provisional `D`, require the exact 16 modified and six untracked residue
paths in section 6 with nothing staged, then continue at the literal cleanup step. Do not inspect or
execute any private request before verified `G`.
