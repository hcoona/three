# Atlas V0 A2 Repository-Hygiene Decoupling

**Lifecycle:** Active subordinate; planning-only before verified shared `R`

**Status:** Proposed scope; implementation blocked until plan review

**Increment:** A2R6 - Repository-Hygiene Decoupling

**Decision owner:** Project leader

**Implementation language:** Existing C# on the repository-pinned .NET 10 SDK

**Base:** `8a1cfd34a244c6a196528de75d641436f1c9e552`

## 1. Architectural correction

The one authorized A2R5 retry returned only:

```text
Safety check failed: private-workspace-policy.
```

The failure came from Atlas runtime validation of the repository's tracked
`.private\.gitignore`. The project leader rejected that coupling and selected complete separation:

- Atlas runtime safety validates requests, outputs, source roots, game files, save files, manifests,
  inventories, state, copies, and lifecycle evidence.
- Repository hygiene keeps private artifacts out of Git through the tracked policy, reviewed
  clean-worktree wrapper, hooks, CI, and release gates.

Parsing the repository's `.gitignore` during discovery, confirmation, copying, or cleanup validates
neither game data nor save data. It also makes otherwise reusable runtime code depend on the current
repository layout. A2R6 deletes that runtime responsibility rather than relaxing its parser.

The earlier `P` and `P2` commits explored comment-aware policy parsing. This `P3` supersedes that
direction before implementation. Git history preserves those planning records; no parser correction
is authorized.

## 2. Exact production scope

Delete all four runtime calls to `ValidatePrivateWorkspace`:

1. discovery;
2. confirmation;
3. trusted-local copy; and
4. cleanup preflight.

Delete the now-dead:

- `AtlasDiscovery.ValidatePrivateWorkspace` method;
- `AtlasWorkspaceLayout.PrivateGitIgnorePath` property; and
- `AtlasIoSeams.ReadAllText` seam, which exists only for that policy check.

Discovery moves directly from successful request preflight to
`DiscoveryCanonicalPaths`. `PrivateWorkspacePolicy = 8` and its fixed CLI mapping remain as legacy
public compatibility; new runtime execution no longer assigns that stage.

The tracked `.private\.gitignore` remains unchanged. The reviewed retry script's commit,
upstream, and clean-worktree checks remain unchanged. Repository hooks, CI, and release gates remain
unchanged.

## 3. Retained runtime safety

The correction does not remove or weaken:

- strict request parsing and canonical request-file validation;
- absolute DOS path, ready fixed-drive, ordinary file/directory, reparse-point, and device checks;
- canonical workspace-root construction and separator-aware containment;
- create-new and existing-output path checks;
- command workspace census and released-A0 evidence admission;
- game executable, definition-root, and save-root layout validation;
- source/workspace disjointness;
- manifest, inventory, state, digest, copy-plan, receipt, and lifecycle validation; or
- fixed, payload-free CLI diagnostics and generic fallback behavior.

Every actual runtime path used by an operation continues to pass its operation-specific canonical
and ordinary-path checks. The repository policy file is not an Atlas input or output and receives no
runtime access.

## 4. Exclusions

A2R6 does not:

- read, inspect, enumerate, hash, rewrite, or delete any real `.private` artifact;
- change the tracked repository policy or the reviewed request-preparation script;
- add or relax a Git ignore parser;
- change any game/save discovery rule, source scope, baseline, manifest, inventory, copy, state,
  cleanup, or JSON schema;
- remove path, source, workspace-census, digest, or copy-fidelity safety;
- add a package, project, harness, telemetry, logging, tracing, or private fixture; or
- authorize confirmation, copying, cleanup, deletion, private-content inspection, or live-save
  writes.

## 5. Exact repository candidates

`P3` replaces the superseded plan path with only this plan. `R` adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-repository-hygiene-decoupling-plan-review.md
```

`I` may change only:

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

`G` adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-repository-hygiene-decoupling-release-gate.md
```

The immutable chain is:

```text
B   8a1cfd34a244c6a196528de75d641436f1c9e552
P   d70f3816de7c35c463203afe05d2378d3ab26aea
P2  84472fd431fd9174d042f7ea01851aad5129a053
P3  <this corrected plan commit>
R   <review record>
I   <implementation>
G   <release gate>
```

Every role is the direct child of the preceding role. Each exact staged `R` and `G` blob must
receive independent `No findings`, be committed unchanged, and then be pushed and verified as the
clean shared branch tip. Every other role must also be pushed and verified before the next role
begins.

## 6. Acceptance evidence

The candidate is acceptable when:

1. no Atlas runtime command calls, reads, or validates the repository `.gitignore`;
2. `ValidatePrivateWorkspace`, `PrivateGitIgnorePath`, and the policy-only `ReadAllText` seam are
   absent;
3. discovery proceeds from request preflight directly to canonical-path validation;
4. the legacy `PrivateWorkspacePolicy` enum value and exact CLI token remain available but are not
   assigned by runtime discovery;
5. policy-specific production tests and synthetic fixture setup are removed;
6. focused tests prove discovery, confirmation, copy, and cleanup still reject invalid canonical,
   missing, wrong-type, reparse-backed, outside-workspace, unexpected-census, or unsafe source paths
   through their retained validators;
7. existing A2R3/A2R4/A2R5 token, fallback, command-isolation, privacy, and unchanged apphost smoke
   cases remain enabled;
8. the current A2 plan and index document the responsibility boundary without rewriting A2R5
   history;
9. locked restore, warning-as-error build, format, focused tests, full tests, unchanged apphost
   smoke, reference, ref-bound HK, and Git candidate-integrity checks pass;
10. a fresh GPT-5.6 Sol reviewer returns exact `No findings` for committed `I`; and
11. independent reviewers return exact `No findings` for staged `R` and `G` before those exact blobs
    are committed unchanged.

No new fault seam, diagnostic token, process test, or private fixture is required.

## 7. Stop, authority, and resume

Stop and return to planning if removing repository-policy access exposes an actual operation path
that lacks canonical, ordinary-path, containment, source, census, or create-new validation. Do not
restore runtime Git coupling to cover such a gap; fix the operation-specific validator instead.

This plan grants no private-run authority. After verified shared `G`, update only the reviewed
session script's commit binding to `G`, independently review the exact script, and return it to the
project leader for one metadata-only retry.
