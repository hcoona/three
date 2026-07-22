# Atlas V0 A2 Approved-Manifest Authority Correction

**Lifecycle:** Active subordinate after verified shared `R`

**Status:** Planning candidate; implementation and private discovery are blocked

**Increment:** A2R8 - Approved-Manifest Authority Correction

**Decision owner:** Project leader

**Implementation language:** Existing C# on the repository-pinned .NET 10 SDK

**Base:** `904a14f66ac2fb6cd5f735cd6668a03123ab4ab3`

**Purpose:** Remove the duplicate public A0 corpus reconstruction and make the approved A0
manifest the sole corpus authority while retaining fail-closed live reconciliation and per-file
copy proof.

**Governing plans:**

- `atlas-v0-a0-research-contract.md`
- `atlas-v0-a2-intake-safety-plan.md`
- `atlas-v0-a2-post-patch-baseline-correction.md`

**Planned plan-review record:**
`../reviews/atlas-v0-a2-approved-manifest-authority-correction-plan-review.md`

**Planned release-gate record:**
`../reviews/atlas-v0-a2-approved-manifest-authority-correction-release-gate.md`

## 1. Problem

Released A0 revision 3 is a project-leader-approved manifest of the observed installed tree. It
already contains the save roots, save entries, source aliases, definition groups, selection rules,
group order, decisions, and complete terminal census.

The current A2 reader nevertheless reconstructs a second copy of that corpus in public C#:

- fixed save and definition counts;
- a fixed save-slot list;
- exact save-root and save-entry arrays;
- ten fixed definition-group identifiers;
- ten fixed definition selection rules;
- exact tuple record types and factories; and
- exact array-order validators.

That duplicate representation is not independent evidence. It is an alternate serialization of the
same human-approved decision. A harmless difference between the two representations blocks intake
before A2 can perform the safety work that matters: strict manifest validation, live enumeration,
path-set reconciliation, approval, fingerprinting, and verified copying.

A2R7 repaired one mismatch inside that duplicate reconstruction. Continuing tuple-by-tuple
compatibility corrections would preserve the wrong authority model and create more opportunities
for the two representations to diverge.

## 2. Decision

The released and approved A0 manifest is the sole authority for corpus-specific data. A2 must not
reconstruct or compare a second frozen corpus in production code.

A2 identifies the authorized baseline directly:

1. strict JSON must satisfy the existing `atlas-intake/v2` contract;
2. the survey alias must be `survey-000001`;
3. the manifest revision must be 3;
4. validation must be `manual-a0-review/v1`;
5. confirmation must be approved by the project-leader role;
6. the decision reference must be exactly
   `commit:3610d5e2a69073672bda665eed25a545a141c06b`;
7. the discovery request must bind the exact manifest bytes by SHA-256;
8. the same request must independently bind the exact inventory bytes by SHA-256; and
9. that inventory must contain exactly one strict baseline-manifest row for revision 3.

The approval commit is public governance identity. It replaces indirect corpus identity through a
second set of public tuples. The private manifest digest remains private and continues through the
state and copy lineage.

After that authority boundary, A2 validates the manifest's internal consistency and re-enumerates
the live sources against it. The manifest supplies the approved aliases, paths, group identifiers,
selection rules, group order, decisions, and counts. Live enumeration must prove that the current
tree still has exactly that approved membership and classification before any content read.

After project-leader approval of the pending manifest, A2 fingerprints every included source,
creates a new private copy, and proves source/copy length and SHA-256 equality. Those are the safety
and evidence properties A2 exists to provide.

## 3. Authority and validation layers

### 3.1 Protocol and target identity

Production code continues to own protocol facts:

- schema names and revisions;
- the fixed survey alias;
- Steam application ID `1786790` and build label `13624401`;
- the two supported save-root roles;
- supported save roles, definition decisions, and reason-code domains;
- source-alias and root-alias lexical formats;
- path, containment, reparse, and destination rules;
- request, state, inventory, receipt, and lifecycle contracts; and
- the trusted-local-filesystem profile.

These facts define what A2 can process. They are not a second copy of which files A0 approved.

### 3.2 Approved-manifest identity

Production code binds baseline revision 3 to the exact public A0 approval commit. It continues to
require the request's exact SHA-256 for the manifest bytes, the request's independent SHA-256 for
the complete inventory bytes, and exactly one baseline inventory row with the required artifact
class, purpose, custodian, empty lineage, lifecycle, status, qualification, and verification method.

The inventory schema has no per-row content digest. A2R8 must not invent one or compare the manifest
digest with the different digest of the inventory document. The authority proof is the conjunction
of both request-to-byte digest equalities, the canonical request paths, the strict inventory row,
and the exact approval commit. Tests must independently reject either digest mismatch and every
invalid baseline-row identity or lineage field.

No command discovers an ambient manifest, substitutes a digest, or normalizes and rewrites the
baseline.

### 3.3 Manifest self-consistency

The baseline reader derives census values from the document instead of comparing public constants.
It fails closed unless:

- `discoveredSaveDirectoryEntryCount` equals `saveEntries.Length`;
- `includedSaveCount` equals the number of included save entries;
- save-root observed counts equal their member-entry counts;
- save-root activity and decision agree with their entries;
- `discoveredDefinitionEntryCount` equals `definitionEntries.Length`;
- `includedDefinitionCount` equals the number of included definition entries;
- every definition group has a unique schema-valid identifier;
- every group selection rule satisfies the one supported grammar;
- every definition entry names an existing group;
- every group count equals its entry count;
- every entry decision equals its group decision;
- source aliases and normalized locators are unique; and
- all existing role, decision, file-type, path, and reparse invariants hold.

Group identifiers must implement the tracked schema's `^[a-z0-9-]+$` lexical constraint. A2R8 must
not replace that constraint with a fixed identifier vocabulary.

Selection-rule parsing must have one implementation shared by manifest validation and live
matching. Invalid separators, empty or dot segments, drive syntax, unsupported wildcard syntax,
embedded `**`, unsupported character classes, and malformed braces fail during manifest read.

### 3.4 Live reconciliation

Discovery continues to enumerate both save roots and the complete definition root before source
content access. It fails closed on:

- a missing approved path;
- a new unapproved path;
- a reparse-backed path;
- an unsupported or unreadable entry;
- changed save role, slot, decision, or root membership;
- changed definition group or decision;
- a selection-rule result that no longer covers the exact approved definition set;
- duplicate or case-colliding normalized paths; or
- any terminal-count or directory-entry-set mismatch.

First-match definition-group order remains semantic because the approved manifest supplies that
order. A2 preserves it but does not compare it with a public canonical order.

### 3.5 Pending and downstream preservation

The pending manifest must preserve all approved baseline identities and semantics:

- every save root field;
- every save-entry source alias, root alias, path, role, nullable slot, decision, reason code, type,
  and reparse flag;
- every definition-group identifier, selection rule, order, count, and decision; and
- every definition-entry source alias, path, group, decision, type, and reparse flag.

Only the already-defined revision, validation, and confirmation transitions may change.

Copy plans derive their entries from the included pending-manifest entries. Copy-plan validation
remains strict and structural but has no hard-coded corpus length. Copy receipts derive save and
definition counts from their entries, reconcile them with the approved manifest and copy plan, and
retain per-entry source length, last-write time, and SHA-256.

## 4. Required deletion

A2R8 is not a bypass around `RequireExactDefinitionGroupContract`. The obsolete architecture must
be removed completely.

Delete from production:

- the six exact save/definition corpus-count constants;
- the exact included-save slot list;
- all fixed definition-group ID constants;
- all fixed definition selection-rule constants;
- `ExactFrozenSaveRoots`;
- `ExactFrozenSaveEntries`;
- `ExactFrozenDefinitionGroups`;
- `CreateExactFrozenSaveEntryContracts`;
- `GetExactFrozenSaveRoots`;
- `GetExactFrozenSaveEntries`;
- `GetExactFrozenDefinitionGroups`;
- `ValidateExactManifestCorpus`;
- `RequireExactSaveRootContract`;
- `RequireExactSaveEntryContract`;
- `RequireExactDefinitionGroupContract`;
- `ExactSaveRootContract`;
- `ExactSaveEntryContract`;
- `ExactDefinitionGroupContract`; and
- every exact-count dependency in manifest, copy-plan, copy-receipt, and trusted-copy validation.

Delete from tests:

- production-derived frozen save, root, and definition fixtures;
- the full public save reconstruction test;
- the full synthetic A0 definition reconstruction;
- assertions against production exact-count constants;
- assertions against production group IDs or selection rules; and
- mutation tests whose only purpose is to enforce corpus array order or alias allocation.

Delete or supersede active documentation that assigns corpus authority to public reconstruction.
Do not edit A0, A2, A2R3, or A2R7 review and release evidence. Those historical records must remain
faithful to the decisions and implementation that existed when they were created. The active A2
plan and index must be corrected.

The removal check is repository-wide for active C# and active documentation. Renaming obsolete
symbols or moving their values into another helper does not satisfy this plan.

## 5. Replacement test architecture

Tests own a compact synthetic manifest. It must intentionally differ from the real A0 corpus:

- non-production but schema-valid root and source aliases;
- non-production group identifiers and valid selection rules;
- a small save census with included and excluded entries;
- a small definition census with included and excluded groups; and
- counts computed from the fixture arrays.

The fixture uses the real public A0 approval commit because that commit is the authority binding,
not corpus data. No test reads private files or embeds a private path, digest, listing, or value.
Every accepted synthetic case must have coherent canonical request paths, manifest digest,
inventory digest, and baseline inventory-row identity.

Hard-coded entry indexes tied to the old corpus layout must be replaced by fixture identities or
small, explicit fixture positions whose meaning is local to the test.

The definition fixture must also contain overlapping valid selection rules. Tests preserve their
first-match order, prove the expected membership in that order, and reject a reorder when it changes
the live file's approved group or decision.

## 6. Acceptance criteria

A2R8 is acceptable only when all criteria below pass.

### 6.1 Authority acceptance

1. A valid revision-3 manifest with the exact A0 approval commit is accepted without any public
   save, definition-group, selection-rule, entry-order, or corpus-count comparison.
2. Any other baseline decision commit fails before live enumeration.
3. A baseline with coherent request/inventory lineage, valid non-production group IDs, valid rules,
   unique aliases, and self-consistent non-production counts passes contract reading.
4. Reordered save roots, save entries, and disjoint definition groups pass when all relationships
   and live classifications remain valid.
5. Unique, schema-valid source-alias assignments are accepted and preserved.

### 6.2 Contract rejection

1. Unknown, missing, duplicate, or type-invalid JSON properties continue to fail.
2. Group IDs containing uppercase letters, underscores, whitespace, or other schema-invalid
   characters fail.
3. Invalid selection-rule grammar fails during manifest read, before enumeration.
4. Duplicate aliases, duplicate normalized paths, missing groups, group count mismatches,
   entry/group decision mismatches, and top-level count mismatches fail.
5. Invalid save roles, slots, root roles, activity, decisions, reason codes, paths, types, and
   reparse flags continue to fail.

### 6.3 Discovery rejection

1. Missing and new save or definition files fail.
2. Regrouped or reclassified definition files fail.
3. Reordering overlapping definition rules fails when first-match membership changes.
4. Changed save role, slot, root membership, or decision fails.
5. Reparse-backed or outside-root entries fail.
6. Directory-entry changes during discovery or copy fail.

### 6.4 Preservation and fidelity

1. Tests compare every baseline and pending save-root field.
2. Tests compare every baseline and pending save-entry field by root/path identity.
3. Tests compare every baseline and pending definition-group field in manifest order.
4. Tests compare every baseline and pending definition-entry field by normalized path identity.
5. Copy-plan entries equal the included approved-manifest identity set.
6. Receipt entries equal the copy-plan identity set and counts equal actual entry classes.
7. Every copied file has the source length and SHA-256 recorded in the receipt.
8. Every final copy matches its held source handle by length and SHA-256.
9. Existing create-new, no-overwrite, cancellation, rollback, inventory, state, and cleanup safety
   behavior remains unchanged.

### 6.5 Removal proof

Repository-wide searches over active C# and tests find no obsolete exact-corpus symbols or
production group/rule literals. Production contains no exact save/definition census constant. The
only remaining real A0 corpus counts or tuple descriptions may be historical documentation and
release evidence.

## 7. Exact repository candidates

`P` is the direct child of base `B` and changes only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a2-approved-manifest-authority-correction.md
    atlas-v0-a2-intake-safety-plan.md
```

`R` is the direct child of `P` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-approved-manifest-authority-correction-plan-review.md
```

Implementation candidates `I1..In` form a direct-child chain from `R` and may change only:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/
    AtlasDiscovery.cs
    AtlasIntakeContracts.cs
    TrustedLocalCopy.cs
  docs/.copilot/
    README.md
    plans/atlas-v0-a2-intake-safety-plan.md
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/
    AtlasDiscoveryTests.cs
    AtlasIntakeContractTests.cs
    TrustedLocalCopyTests.cs
```

Final release gate `G` is the direct child of final implementation `I` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-approved-manifest-authority-correction-release-gate.md
```

No rename is allowed in `B..G`. No private artifact, generated output, or session script is a
repository candidate.

## 8. Immutable chain and review

The required chain is:

```text
B = 904a14f66ac2fb6cd5f735cd6668a03123ab4ab3
P = reviewed plan candidate
R = record-only plan-review child
I1..In = implementation and review-remediation children
I = final implementation candidate
G = reviewed record-only release-gate child
```

Before implementation:

1. review the exact staged `B..P` candidate independently;
2. iterate the candidate until a fresh reviewer reports `No findings`;
3. commit and push `P`;
4. create the plan-review record with exact commit, tree, path, and review evidence;
5. independently review that staged record until `No findings`;
6. commit and push record-only `R`; and
7. verify ancestry, paths, blobs, upstream equality, index, and clean worktree.

Every implementation candidate must receive a fresh independent cumulative `R..In` review. Findings
are fixed in a new direct-child candidate. Release is blocked until the reviewer reports
`No findings`.

The release-gate record must bind exact `B`, `P`, `R`, every `I` iteration, final tree, changed
paths, validation results, removal searches, reviewer identities, and the final disposition. Review
the exact staged record independently until `No findings`, then commit it unchanged as `G`.

## 9. Validation

Run all .NET commands from the repository root through `mise exec -- dotnet`.

Required validation includes:

1. locked restore for the Atlas test project;
2. warning-as-error build with one MSBuild node;
3. format verification for the library, CLI, and test projects;
4. focused `AtlasIntakeContractTests`;
5. focused `AtlasDiscoveryTests`;
6. focused `TrustedLocalCopyTests`;
7. the complete Atlas test suite;
8. the existing direct apphost smoke tests;
9. project and package reference evaluation;
10. ref-bound HK for the exact cumulative candidate;
11. `git diff --check`;
12. UTF-8 without BOM, LF-only, and Markdown line length at most 100 characters;
13. exact path and no-renames checks;
14. direct-parent ancestry and tree checks; and
15. upstream, clean-index, and clean-worktree checks.

Validation uses only public source and synthetic temporary workspaces. It must not read the real
game, save directory, private A0 manifest, private inventory, private request, or generated A2
artifact.

## 10. Documentation disposition

The active A2 plan must state that:

- A0 manifest bytes and their approval commit are corpus authority;
- public code validates protocol and consistency rather than reconstructing the corpus;
- live reconciliation proves the current source set still matches the approved manifest;
- historical released counts remain unchanged in historical evidence but are neither active
  requirements nor production constants, and A2R8 publishes no private census; and
- private retry remains blocked until verified shared A2R8 `G`.

The `.copilot` index must mark A2R7 as historical released correction, add A2R8 lifecycle
navigation, and state that A2R8 supersedes A2R7 private-retry authority.

Historical records remain unchanged. The A2R8 release gate explains that the old design was safe in
its reviewed scope but is no longer the active authority model.

## 11. Private-run boundary

This plan authorizes no private command. Do not run or rebind the session discovery wrapper before
verified shared A2R8 `G`.

After verified shared `G`:

1. rebind the reviewed session wrapper to exact `G`;
2. independently review the exact wrapper;
3. run one metadata-only discovery attempt locally;
4. preserve all existing private inputs and outputs; and
5. stop at either the fixed success token or the first fixed failure token.

This correction does not authorize confirmation, copy, cleanup, deletion, decoding, semantic
research, or live-save writes. Any successful discovery still requires a separately persisted and
reviewed continuation plan before project-leader approval or copying.

## 12. Stop conditions

Stop without release if:

- the exact A0 approval commit cannot serve as the direct baseline authority;
- safe validation requires another private corpus mirror;
- shared selection-rule parsing cannot preserve the current matching semantics;
- live reconciliation no longer proves exact path-set and classification equality;
- any copy or receipt proof becomes weaker;
- an obsolete corpus constant or fixture remains active;
- a required change falls outside the declared path set;
- validation or independent review has findings; or
- private data would enter source, tests, diagnostics, Git, or agent context.
