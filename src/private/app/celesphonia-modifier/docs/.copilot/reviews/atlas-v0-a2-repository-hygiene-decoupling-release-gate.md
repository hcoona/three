# Atlas V0 A2 Repository-Hygiene Decoupling Release Gate

**Lifecycle:** Active subordinate release-gate evidence after verified shared `G`

**Increment:** A2R6 - Repository-Hygiene Decoupling

**Outcome:** Repository policy removed from Atlas runtime; one metadata-only discovery retry
authorized after verified shared `G`

**Final implementation review:** `No findings`

**Implementation reviewer:** `a2r6-implementation-reviewer`

**Release-record reviewer:** `a2r6-release-record-reviewer`

**Release-record review:** `No findings`

**Governing plan:**
`../plans/atlas-v0-a2-repository-hygiene-decoupling.md`

**Plan-review record:**
`atlas-v0-a2-repository-hygiene-decoupling-plan-review.md`

## 1. Immutable evidence chain

```text
B   8a1cfd34a244c6a196528de75d641436f1c9e552
P   d70f3816de7c35c463203afe05d2378d3ab26aea
P2  84472fd431fd9174d042f7ea01851aad5129a053
P3  3f07c2480bad46baafef6317507a719d560e341b
R   e6cace22c226471c2414905d971a49ac5f87f585
I   dc5298bd2fa3fd7a2325e630849afcef33a4aaaa
```

Every role is the direct child of the preceding role. `P3` supersedes the parser-oriented `P` and
`P2` before implementation. Candidate `I` has tree
`2c4b24cf15501ee358959f402c4d41e518489ffa` and is the direct child of `R`.

The exact no-renames `R..I` path set is:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/
    AtlasDiscovery.cs
    AtlasIntakeContracts.cs
    PrivateArtifactLifecycle.cs
    TrustedLocalCopy.cs
  docs/.copilot/
    README.md
    plans/atlas-v0-a2-intake-safety-plan.md
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/
    AtlasDiscoveryTests.cs
    AtlasIntakeContractTests.cs
```

No `.private` path belongs to the candidate.

## 2. Correction and acceptance

The correction:

- removes repository `.gitignore` validation from discovery, confirmation, copy, and cleanup;
- deletes `ValidatePrivateWorkspace`, `PrivateGitIgnorePath`, and the policy-only `ReadAllText`
  seam;
- removes only policy-specific test seams, fixture setup, and assertions;
- moves discovery directly from request preflight to canonical-path validation;
- retains `PrivateWorkspacePolicy = 8` and its fixed CLI mapping only for compatibility; and
- leaves the tracked repository policy, reviewed script behavior, hooks, CI, and release gates
  unchanged.

Every actual runtime request, output, source, game/save, manifest, inventory, state, copy, and
lifecycle path remains covered by its operation-specific canonical, ordinary-path, fixed-drive,
reparse/device, containment, create-new, census, source-layout, digest, fidelity, or lifecycle
validation.

The correction changes no game/save discovery scope, baseline, request, manifest, inventory, state,
copy, cleanup, or JSON schema. It adds no package, project, diagnostic, harness, telemetry, tracing,
private fixture, or private-data access.

## 3. Validation evidence

The exact candidate passed:

- locked restore with the repository-pinned .NET 10.0.300 SDK;
- warning-as-error build with zero warnings and zero errors;
- `AtlasDiscoveryTests` with 81 passed, zero failed, and zero skipped;
- `AtlasIntakeContractTests` with 42 passed, zero failed, and zero skipped;
- the full Microsoft.Testing.Platform suite with 275 passed, zero failed, and zero skipped;
- unchanged direct apphost smoke with 11 passed, zero failed, and zero skipped;
- format verification and reference evaluation for all three projects;
- staged and exact `R..I` ref-bound HK; and
- exact direct-parent, eight-path, tree, upstream, remote, index, and clean-worktree checks.

Validation used only public code and synthetic temporary workspaces. It accessed no real private
request, workspace content, game, save, manifest, inventory, hash, listing, or generated private
output.

## 4. Independent review

Fresh GPT-5.6 Sol reviewer `a2r6-implementation-reviewer` reviewed exact committed `R..I` against
the accepted plan, source, tests, documentation, validation, privacy boundary, and release
authority. It traced all four removed call sites and their retained operation-specific validators
and returned exact `No findings`.

The release-record reviewer independently reviewed this exact staged record and returned
`No findings`. Neither reviewer authored its reviewed candidate or received private evidence.

## 5. Gate decision

This record must be committed unchanged as `G`, the direct child of `I`, with `I..G` adding only
this file. `G` must be pushed and verified for parent, path, reviewed blob, tree, upstream, remote,
index, and clean-worktree equality.

After verified shared `G`, update only the reviewed session script's commit binding, independently
review the exact script, and return it for one metadata-only discovery retry. This gate does not
authorize confirmation, copying, decoding, cleanup, deletion, private inspection, or live-save
writes.
