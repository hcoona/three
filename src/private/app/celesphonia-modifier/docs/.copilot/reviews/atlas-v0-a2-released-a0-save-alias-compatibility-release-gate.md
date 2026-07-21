# Atlas V0 A2 Released-A0 Save-Alias Compatibility Release Gate

**Lifecycle:** Active subordinate release-gate evidence after verified shared `G`

**Increment:** A2R7 - Released-A0 Save-Alias Compatibility

**Outcome:** Released-A0 alias reconstruction corrected; one human-operated metadata-only discovery
retry authorized after verified shared `G`

**Final implementation review:** `No findings`

**Implementation reviewer:** `a2r7-i4-reviewer`

**Final release-record reviewer:** `a2r7-release-record-rereviewer`

**Release-record review:** `No findings`

**Governing plan:**
`../plans/atlas-v0-a2-released-a0-save-alias-compatibility.md`

**Plan-review record:**
`atlas-v0-a2-released-a0-save-alias-compatibility-plan-review.md`

## 1. Immutable evidence chain

```text
B   83bddad8ae4213253922e292023ed5163e18b614
P   38a4206b6f25b301227cbd4e624dd7aaf2ed290e
R   d9391027b4117c6139650e43bbfb53bf5eaa3114
I1  9b0432d372ce09fe6cd302bdb172aa072bbd0bea
I2  4cfb91b53f9d36979de8b97648274f5949d90d6c
I3  e9abad16d5a62e69dbd1fad356afdc2cbd5621e1
I4  6265c2bc2f05111c271da9684a42f0ace26b18bc
```

Every role is the direct child of the preceding role. Final implementation `I4` has tree
`2f638f93c2d357c0b0b1d9e8bd5539a0f5b014c8` and is designated `I`.

The exact no-renames `R..I` path set is:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/
    AtlasIntakeContracts.cs
  docs/.copilot/
    README.md
    plans/atlas-v0-a2-intake-safety-plan.md
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/
    AtlasDiscoveryTests.cs
    AtlasIntakeContractTests.cs
```

## 2. Correction and acceptance

The correction:

- constructs the 23 approved semantic save entries before allocating source aliases;
- orders roots by ordinal comparison;
- orders normalized relative paths by case-insensitive ordinal comparison with an ordinal
  tie-breaker;
- assigns `save-source-0001` through `save-source-0023` in that order;
- returns entries in source-alias order;
- retains strict index, alias, root, path, role, slot, and decision validation;
- locks every released alias, root, path, role, nullable slot, and decision in an explicit public
  regression test;
- uses independent literal role, decision, and entry-type expectations;
- proves synthetic discovery preserves every source alias and semantic field, including reason code,
  by root-and-path identity; and
- blocks private discovery before verified shared `G` and records the required post-success stop.

No alias permutation is tolerated. No save root, definition, schema, revision, request, discovery,
copy, receipt, inventory, lifecycle, safety-stage, or CLI algorithm changes. Only corrected
alias-derived values in synthetic manifests and downstream synthetic artifacts may differ. The
correction reads, rewrites, and reserializes no private A0 artifact.

## 3. Validation evidence

The code and test blobs in final `I` passed:

- locked restore with the repository-pinned .NET 10.0.300 SDK;
- warning-as-error build with zero warnings and zero errors;
- `AtlasIntakeContractTests` with 43 passed, zero failed, and zero skipped;
- `AtlasDiscoveryTests` with 81 passed, zero failed, and zero skipped;
- the full Microsoft.Testing.Platform suite with 276 passed, zero failed, and zero skipped;
- unchanged direct apphost smoke with 11 passed, zero failed, and zero skipped; and
- format verification and reference evaluation for all three projects.

The exact final cumulative candidate passed:

- `R..I` ref-bound HK;
- direct-parent ancestry and the exact five-path no-renames restriction;
- `git diff --check`;
- UTF-8 without BOM, LF-only, and Markdown lines of at most 100 characters;
- tree, SDK, upstream, index, and clean-worktree checks; and
- push verification as the shared branch tip.

Validation used only public code and synthetic temporary workspaces. It accessed no real private
request, workspace content, game, save, manifest, inventory, hash, listing, or generated A2 output.

## 4. Independent review and disposition

The exact `I1` review found two medium test-evidence gaps. `I2` replaced production-derived role,
decision, and entry-type expectations with independent literals and added reason-code preservation.
A cumulative independent review then returned `No findings`.

Candidate-integrity checks found two 101-character Markdown lines. Direct-child `I3` and `I4`
rephrased only those lines without changing their meaning. Fresh independent reviewer
`a2r7-i4-reviewer` reviewed exact pushed `R..I4` against the plan and returned `No findings`.

The first release-record review found one medium ambiguity about downstream behavior. This revision
limits unchanged claims to algorithms and explicitly permits the planned alias-derived synthetic
value changes.

Fresh independent reviewer `a2r7-release-record-rereviewer` reviewed this exact staged record and
returned `No findings`. Neither final reviewer authored its reviewed candidate or received private
evidence.

## 5. Gate decision

This record must be committed unchanged as direct-child `G`. The `I..G` diff may add only this file.
`G` must be pushed and verified for parent, path, reviewed blob, tree, upstream, index, and clean
worktree.

After verified shared `G`, update only the reviewed session script's commit binding, independently
review the exact script, and return it to the project leader for one human-operated metadata-only
discovery retry. This gate does not authorize confirmation, copying, decoding, cleanup, deletion,
private inspection, or live-save writes.

`Intake discovery completed.` permits review of the create-new private discovery outputs. Preserve
them and stop until a separately persisted and independently reviewed continuation plan binds `G`
and defines the next ancestry. `Approval required.` or any safety, request, I/O, cancellation, or
script failure stops without an A2 approval record and returns to the active diagnosis or recovery
route.
