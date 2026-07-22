# Atlas V0 A2 Approved-Manifest Authority Correction Release Gate

**Lifecycle:** Active subordinate release-gate evidence after verified shared `G`

**Increment:** A2R8 - Approved-Manifest Authority Correction

**Outcome:** Approved A0 manifest established as sole corpus authority; one local metadata-only
discovery attempt authorized after verified shared `G`

**Final implementation review:** `No findings`

**Implementation reviewer:** `a2r8-i7-reviewer`

**Final release-record reviewer:** `a2r8-final-release-record-reviewer`

**Release-record review:** `No findings`

**Governing plan:**
`../plans/atlas-v0-a2-approved-manifest-authority-correction.md`

**Plan-review record:**
`atlas-v0-a2-approved-manifest-authority-correction-plan-review.md`

## 1. Immutable evidence chain

```text
B   904a14f66ac2fb6cd5f735cd6668a03123ab4ab3  7c939e80988556694d2c94f42717cebdabe7a0a8
P1  a62362b9fd905a6a157e79cf7903bff2503e0699  dca186550b7e7ce8c09a9205801ffd869d465a55
P2  0c8f1d5ec52856d0eaaa8307accb5b112c2ae07b  b3274c913c37d462ed2b4d6a247b5de6f24b97d8
P   0c8f1d5ec52856d0eaaa8307accb5b112c2ae07b  b3274c913c37d462ed2b4d6a247b5de6f24b97d8
R   0610a08bf2800c8ae186c1922a3a3e48e3cd9c9f  7d90c5649be833168cd9fcc2312f604be19287d0
I1  9c01ab40bcbe0660d90065a6c96d2bedc5be5805  2fbf60af49a144019658c670b6fe427d85deb83e
I2  b82c1c1336c79fbe9c932f378b3025968e467faa  3c8ed4ac35d94e65965c507157d9c2c2ee6de66b
I3  5dcf5a2df83f2bab8ca1ef20069bfafe1af0aa28  22c09a3e14c4813d720a95187dd460d249104ae0
I4  0a99cfeb2fa58968827a88f8c9d8393a32aad659  720418c189b9b39cb3e4023b507ba620e59a4d86
I5  d3a55c5dc9f22e20f64628d6914b6defb3f514db  c4d29b7db13c5b4ced7e6333fdb46621e8ef8a04
I6  8317909a35d83d4d897e1dbf474a5fc396111b72  d24183bf8cd181a1f4f13f76fba2c5c7cb6f4e78
I7  9c7a9ba7d2ef7ac20360aba9b00983ad4bc8b5e4  fb0e9cbfd14230af2a30d5a72918c7d4faa33d53
```

Each distinct role is the direct child of the preceding role. `P2` is final `P`. `I7` is final `I`.
The exact no-renames `R..I` path set is:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/
    AtlasDiscovery.cs
    AtlasIntakeContracts.cs
    TrustedLocalCopy.cs
  docs/.copilot/
    README.md
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/
    AtlasDiscoveryTests.cs
    AtlasIntakeContractTests.cs
    TrustedLocalCopyTests.cs
```

The parent-relative implementation paths were:

- `I1`: all seven cumulative paths;
- `I2`: discovery, trusted copy, and all three test paths;
- `I3`: intake contracts and intake contract tests;
- `I4`: discovery and all three test paths;
- `I5`: discovery tests;
- `I6`: discovery tests and intake contract tests; and
- `I7`: discovery and discovery tests.

The final `I` blobs are:

```text
AtlasDiscovery.cs
  7cbfcd87e6c6092360c5447aaaca068c6de5d9cc
AtlasIntakeContracts.cs
  9090871038f416c643d44a19fb7b824c09f01074
TrustedLocalCopy.cs
  65c01b1c499b1b1842b85d659cc6d8baeb7fee40
README.md
  7dcbd8e11604ba2ca20ed196ff40251667879280
AtlasDiscoveryTests.cs
  6e06a146c799e0c43a6d786f97d2b6e2c9c1b7eb
AtlasIntakeContractTests.cs
  8f2ebfccef9893fcf014fc736542069ab3018744
TrustedLocalCopyTests.cs
  512fd3163dca083cce17d9ce9f4bd8c5bb993b67
```

## 2. Correction and acceptance

The correction:

- makes approved A0 revision 3 the only corpus-specific authority;
- binds it to `commit:3610d5e2a69073672bda665eed25a545a141c06b` and `manual-a0`;
- derives counts from approved arrays and decisions without fixed production census values;
- retains nonempty manifest, copy-plan, and receipt array schema guarantees;
- validates group identifiers lexically without a fixed vocabulary;
- shares one definition selection-rule parser between validation and live matching;
- preserves approved ordering, aliases, paths, rules, roles, decisions, and semantic fields;
- preserves first-match definition group semantics;
- rejects missing, new, duplicate, case-colliding, regrouped, or reclassified live entries;
- requires exact case-insensitive live and approved save identity-set equality;
- reconciles every definition file and directory outside exact protocol-owned exclusions;
- reruns complete reconciliation before copying;
- derives copy identities and counts only from approved included entries; and
- retains receipt and final-file length, timestamp, SHA-256, and held-source fidelity proof.

The production implementation no longer contains the six private-corpus count constants, frozen
save or definition tuples, fixed definition groups or rules, corpus factories or accessors, exact
corpus validators, or downstream exact-count assumptions.

Tests use compact nonproduction save and definition corpora. Their paths, rules, group identifiers,
source aliases, counts, included and excluded cases, and overlapping first-match cases do not
reconstruct the historical production corpus.

## 3. Validation evidence

Final `I` passed:

- locked restore with the repository-pinned .NET 10.0.300 SDK;
- warning-as-error build with zero warnings and zero errors;
- format verification for the library, CLI, and test projects;
- `AtlasIntakeContractTests` with 43 passed, zero failed, and zero skipped;
- `AtlasDiscoveryTests` with 90 passed, zero failed, and zero skipped;
- `TrustedLocalCopyTests` with 35 passed, zero failed, and zero skipped;
- the full Microsoft.Testing.Platform suite with 286 passed, zero failed, and zero skipped;
- direct apphost smoke with 11 passed, zero failed, and zero skipped;
- project and package reference evaluation with 6 passed, zero failed, and zero skipped;
- exact cumulative `R..I` ref-bound HK over seven files;
- `git diff --check`;
- UTF-8 without BOM, LF-only, and Markdown lines of at most 100 characters;
- direct-parent ancestry, exact seven-path, modified-only, and no-renames checks; and
- final tree, pushed upstream, clean-index, and clean-worktree checks.

Repository-wide active C# searches returned no obsolete exact-corpus symbol, fixed production group
identifier, fixed production selection rule, or semantically equivalent recursive production rule.
Searches over the active index and A2 plan found no remaining duplicate-authority claim.

Validation used only public code and synthetic temporary workspaces. It accessed no real private
request, workspace, game, save, manifest, inventory, wrapper, generated A2 output, path, hash,
listing, token, disposition, or document content.

## 4. Independent review and disposition

1. `a2r8-i1-reviewer` found one high-severity gap: unmatched live definition entries were skipped.
   `I2` added complete file and directory reconciliation during discovery and before copy.
2. `a2r8-i2-reviewer` found one medium-severity gap: fixed-count deletion also removed required
   nonempty-array validation. `I3` restored structural nonempty checks without a fixed census.
3. `a2r8-i3-reviewer` reviewed exact cumulative `R..I3` and returned `No findings`.
4. `a2r8-release-record-reviewer` found one high-severity issue in the first staged gate: the main
   synthetic fixture retained exact production rule `www/data/*.json`. The gate was withdrawn and
   deleted without a release commit. `I4` moved definitions under `synthetic-corpus/` and permitted
   only ancestors required to reach exact protocol exclusions.
5. `a2r8-i4-reviewer` found one medium-severity issue: a standalone parser fixture reconstructed the
   former recursive production rule through normalized case and separators. `I5` replaced its root,
   extensions, group, and aliases with synthetic values.
6. `a2r8-i5-reviewer` found one medium-severity issue: other authorized standalone fixtures retained
   low production source ordinals. `I6` moved every such valid fixture to nonproduction ranges.
7. `a2r8-i6-reviewer` found one medium-severity save gap: duplicate case variants could replace a
   missing same-decision save while preserving counts. `I7` added unique live identities and exact
   approved-set equality, with a direct regression test.
8. Fresh GPT-5.6 Sol reviewer `a2r8-i7-reviewer` reviewed exact pushed `R..I7` against the governing
   plan, source, tests, validation, removal proof, privacy boundary, and release authority. It
   returned exact `No findings`.

Fresh GPT-5.6 Sol reviewer `a2r8-final-release-record-reviewer` independently reviewed this exact
staged record and returned `No findings`. No reviewer authored its reviewed candidate or received
private evidence.

## 5. Gate decision

This exact reviewed record must be committed unchanged as direct-child `G`. The `I..G` diff may add
only this file. `G` must be pushed and verified for parent, path, reviewed blob, tree, upstream,
remote, index, and clean-worktree equality.

After verified shared `G`, rebind only the reviewed session wrapper's commit identity, independently
review the exact wrapper, and run exactly one local metadata-only discovery attempt. Preserve all
private inputs and outputs and stop at the first fixed token.

This gate does not authorize confirmation, copying, cleanup, deletion, decoding, semantic research,
private inspection, or live-save writes. A successful discovery still requires a separately
persisted and independently reviewed continuation plan before project-leader approval or copying.
