# Atlas V0 A2 Released-A0 Save-Alias Compatibility

**Lifecycle:** Active subordinate; planning-only before verified shared `R`

**Status:** Proposed correction; implementation blocked until plan review

**Audience:** Project leader, implementers, independent reviewers, and future resumers

**Purpose:** Reconstruct the released A0 save-alias assignment exactly without weakening source
identity or changing the released private baseline

**Increment:** A2R7 - Released-A0 Save-Alias Compatibility

**Decision owner:** Project leader

**Implementation language:** Existing C# on the repository-pinned .NET 10 SDK

**Base:** `83bddad8ae4213253922e292023ed5163e18b614`

**Governing plans:**

- `atlas-v0-a0-research-contract.md`
- `atlas-v0-a2-intake-safety-plan.md`
- `atlas-v0-a2-released-a0-workspace-compatibility.md`

**Planned plan-review record:**
`../reviews/atlas-v0-a2-released-a0-save-alias-compatibility-plan-review.md`

**Planned release-gate record:**
`../reviews/atlas-v0-a2-released-a0-save-alias-compatibility-release-gate.md`

## 1. Problem and bounded evidence

Bounded local diagnostics informed this plan with these repository-safe observations:

1. the released A0 save entries have the same complete multiset of root aliases, normalized relative
   paths, roles, slot numbers, and decisions as the public frozen corpus;
2. the released A0 save entries also have the same complete `save-source-*` alias set, but the
   aliases are assigned to different semantic entries than the public frozen factory expects; and
3. one predeclared candidate rule matched the entire released assignment and array order: save roots
   in root-alias ordinal order, entries within each root in case-insensitive ordinal relative-path
   order with an ordinal tie-breaker, sequential source aliases, and final array order by source
   alias.

The diagnostic did not enumerate alternative permutations or disclose a private value or mapping.
These observations are planning inputs, not release authority or substitutes for tracked synthetic
regression evidence. The matched rule is consistent with the public discovery implementation:
save-root enumeration orders file names with `StringComparer.OrdinalIgnoreCase`, and emitted
manifests order entries by `SourceAlias` with `StringComparer.Ordinal`.

The defect is therefore in the public compatibility reconstruction. The current
`CreateExactFrozenSaveEntryContracts` assigns aliases in numeric slot order before appending global,
config, and Steam metadata entries. That reconstruction does not match the released A0 identity.

## 2. Decision

Released A0 revision 3 remains immutable and authoritative. Its save aliases remain operational
identities used by discovery preservation, copy plans, destinations, receipts, and lineage. A2R7
must not ignore aliases, compare only semantic multisets, or edit the private manifest to fit the
current code.

A2R7 corrects only the public frozen save-entry factory:

1. construct the already-approved 23 semantic save-entry contracts without assigning source
   ordinals;
2. order them by `RootAlias` with `StringComparer.Ordinal`;
3. then order normalized `RelativePath` with `StringComparer.OrdinalIgnoreCase`;
4. use `StringComparer.Ordinal` as the deterministic casing tie-breaker;
5. assign `save-source-0001` through `save-source-0023` in that order; and
6. return the array in the same source-alias order.

This reproduces the released A0 alias-to-locator mapping while retaining the strict index, alias,
root, path, role, slot, and decision checks in `RequireExactSaveEntryContract`.

## 3. Exact scope

### In scope

- Correct `CreateExactFrozenSaveEntryContracts` in `AtlasIntakeContracts.cs`.
- Add one explicit public regression test for all 23 expected aliases, roots, paths, roles, slot
  numbers, and decisions.
- Add focused discovery assertions that compare every baseline and pending save entry by root/path
  identity and prove that its source alias and semantic fields are unchanged.
- Retain and run the existing exact-contract mutation cases that reject save-entry reordering,
  renumbering, alias swapping, and duplicate aliases.
- Update the active A2 plan's status and authority banner to block private retry until verified A2R7
  `G`, clarify the released A0 alias-allocation rule, and require a new continuation plan after a
  successful retry.
- Update the `.copilot` index to mark A2R6 released, add its release gate, supersede its retry
  authority, and add A2R7 lifecycle and gate navigation.
- Run the focused and complete Atlas validation set.
- Independently review the persisted plan, implementation candidate, and release record until each
  exact candidate receives `No findings`.

### Out of scope

- Reading, rewriting, regenerating, normalizing, or reserializing the released private A0 manifest.
- Ignoring `SourceAlias`, accepting alias permutations, or changing exact array-order enforcement.
- Changing any save root, relative path, role, slot, decision, count, definition, or selection rule.
- Changing JSON schemas, manifest revisions, digest bindings, request contracts, CLI diagnostics, or
  safety stages.
- Changing discovery enumeration or any copy-plan, destination, receipt, inventory, or lifecycle
  algorithm beyond the necessary alias-derived consequences of the corrected frozen baseline.
- Migrating or reinterpreting any revision 4/5 manifest, state, plan, receipt, or copied artifact.
- Adding packages, projects, telemetry, logging, tracing, private fixtures, or a runtime
  compatibility fallback.
- Authorizing confirmation, copy, cleanup, deletion, decoding, semantic research, or live-save
  writes.

## 4. Repository candidates and immutable chain

`P` adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/plans/
  atlas-v0-a2-released-a0-save-alias-compatibility.md
```

`R` adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-released-a0-save-alias-compatibility-plan-review.md
```

`I` may change only:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/
    AtlasIntakeContracts.cs
  docs/.copilot/
    README.md
    plans/atlas-v0-a2-intake-safety-plan.md
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/
    AtlasIntakeContractTests.cs
    AtlasDiscoveryTests.cs
```

`G` adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-released-a0-save-alias-compatibility-release-gate.md
```

The immutable chain is:

```text
B   83bddad8ae4213253922e292023ed5163e18b614
P   <persisted plan>
R   <plan-review record>
I1  <initial implementation>
...
In  <final implementation candidate, named I>
G   <release-gate record>
```

`P` is the direct child of `B`, and `R` is the direct child of `P`. Every implementation or review
remediation commit is the direct child of the preceding implementation candidate. The final
reviewed implementation commit is `I`, and `G` is its direct child. Every candidate must be pushed
and verified as the clean shared branch tip before review or successor work begins. The exact
staged `R` and `G` record must receive independent `No findings` before being committed unchanged.

## 5. Acceptance evidence

The implementation candidate is acceptable only when:

1. the frozen save-entry factory returns exactly 23 entries in the released A0 root/path order;
2. all 23 expected alias, root, path, role, nullable slot, and decision tuples are explicit in a
   regression test;
3. discovery compares every baseline and pending save entry by root/path identity and proves exact
   source-alias and semantic-field preservation;
4. `RequireExactSaveEntryContract` retains strict source-alias and array-order comparison;
5. existing save-entry order, alias-renumber, alias-swap, and alias-duplicate mutations remain
   rejected;
6. the corrected synthetic baseline passes strict manifest reading and discovery preserves each
   baseline alias by root/path identity;
7. the active A2 status and authority banner blocks private retry until verified A2R7 `G`;
8. no save-root, definition, schema, revision, request, discovery, copy, receipt, inventory,
   lifecycle, safety-stage, or CLI algorithm changes; only corrected alias-derived values in
   synthetic manifests and downstream synthetic artifacts may differ;
9. the `.copilot` index marks A2R6 released, links its release gate, states that A2R7 supersedes its
   retry authority, and adds gate-conditional A2R7 plan and review navigation;
10. locked restore, warning-as-error build, format verification, focused contract/discovery tests,
    the full test suite, unchanged apphost smoke, reference evaluation, ref-bound HK, and Git
    candidate-integrity checks pass;
11. a fresh independent reviewer examines the exact committed `I` against this plan and returns
    `No findings`; and
12. independent reviewers return `No findings` for the exact staged `R` and `G` records before those
    blobs are committed unchanged.

All tests use public code and synthetic temporary workspaces. No validation step may read the real
private manifest, installed game, saves, generated request, or A2 output.

## 6. Stop conditions and compatibility boundary

Stop and return to planning if:

- reproducing the released mapping requires changing any semantic save-entry field;
- the single proved ordering rule does not produce a strict synthetic manifest accepted by the exact
  validator;
- any downstream behavior requires alias permutation tolerance rather than exact identity
  preservation;
- any persisted A2 revision 4/5 state, plan, receipt, or copy is found and would need
  reinterpretation;
- any candidate path outside section 4 is required; or
- any private artifact must be inspected or changed.

The public discovery control flow admits the baseline manifest before publishing revision 4, a root
map, copy plan, state, inventory backup, or destination reservation. A2R7 makes no claim about the
current private workspace state and grants no authority to inspect it. Existing create-new checks
and workspace census remain the runtime guard against conflicting persisted outputs.

The matched rule proves compatibility with the frozen released revision-3 corpus only. It does not
establish a historical or general-purpose alias allocator for other corpora, revisions, platforms,
or case variants.

## 7. Dependencies, outputs, and authority

Dependencies:

- verified shared A2R6 release `B`;
- the released A0 and active A2 plans;
- the public exact-corpus implementation and synthetic test harness; and
- the repository-pinned .NET 10 SDK, MISE, HK, Git, and configured internal package sources.

Repository-safe correction outputs are this plan, its review record, the bounded C# correction and
tests, direct documentation updates, and the release-gate record. A2R7 itself creates no private
output.

This plan grants implementation authority only after verified shared `R`. It grants no private-run
authority. After verified shared `G`, update only the reviewed commit binding in the existing
`run-atlas-a2-discovery.ps1` session script, independently review the exact script, and return it to
the project leader for the human-operated discovery governed by the active A2 plan.

The post-`G` routes are:

- `Intake discovery completed.` permits only the active A2 local review of the pending manifest,
  root map, copy plan, discovered state, updated inventory, inventory backup, and destination
  reservations. Preserve those create-new outputs and stop. A new persisted and independently
  reviewed A2 continuation plan must bind current `G` and define the approval-record parent,
  cumulative path set, and release ancestry before creating
  `../reviews/atlas-v0-a2-intake-approval.md`.
- `Approval required.` or any safety, request, I/O, cancellation, or script failure stops without an
  A2 approval record. Continue only through a new repository-safe diagnosis or the active A2
  recovery rule applicable to that exact outcome.

Neither route extends the `B -> P -> R -> I1 -> ... -> I -> G` correction chain. The session script
remains project-leader-operated because successful discovery can create private A2 outputs.

## 8. Resume procedure

From the repository root:

1. verify `HEAD`, upstream, and a clean worktree at the latest chain role;
2. verify direct-parent ancestry and changed-path restrictions from section 4;
3. before implementation, require pushed `P`, independent plan `No findings`, and verified shared
   `R`;
4. implement only the `I` path set and run:

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
    mise exec -- dotnet test --project $tests --no-build --no-restore --verbosity minimal `
      --filter-class '*AtlasIntakeContractTests'
    mise exec -- dotnet test --project $tests --no-build --no-restore --verbosity minimal `
      --filter-class '*AtlasDiscoveryTests'
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

5. after each implementation candidate, require ancestry from `R`, the cumulative exact section 4
   path set, `git diff --check R HEAD`, and
   `mise exec -- hk check --check --no-progress --from-ref R --to-ref HEAD`;
6. verify the committed candidate blobs have no UTF-8 BOM or CRLF, Markdown lines do not exceed
   100 characters, the SDK matches `global.json`, upstream equals the candidate, and index/worktree
   are clean;
7. push and independently review each `I1..In` candidate, resolve every finding in a direct-child
   implementation commit, and designate the first `No findings` candidate as final `I`;
8. create, independently review, commit, push, and verify record-only `G` as the direct child of
   final `I`;
9. update only the reviewed commit binding in `run-atlas-a2-discovery.ps1`, independently review the
   exact session script, then return it to the project leader for one human-operated retry; and
10. follow the exact success or refusal route in section 7 without creating an undefined correction
    acceptance record.
