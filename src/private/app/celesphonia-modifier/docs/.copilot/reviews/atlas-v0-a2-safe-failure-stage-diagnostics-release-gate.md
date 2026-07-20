# Atlas V0 A2 Safe Failure-Stage Diagnostics Release Gate

**Lifecycle:** Active subordinate release-gate evidence after verified shared `G`

**Increment:** A2R4 - Safe Failure-Stage Diagnostics

**Outcome:** Fixed, payload-free discovery-stage diagnostics complete; one metadata-only discovery
retry authorized after verified shared `G`

**Final implementation review:** `No findings`

**Implementation reviewer:** `a2r4-implementation-reviewer`

**Release-record reviewer:** `a2r4-release-record-reviewer`

**Release-record review:** `No findings`

**Governing plan:**
`../plans/atlas-v0-a2-safe-failure-stage-diagnostics.md`

**Plan-review record:**
`atlas-v0-a2-safe-failure-stage-diagnostics-plan-review.md`

## 1. Immutable evidence chain

The reviewed chain is:

```text
B   b8fb8eb5f84f41e6e7bf98a7aea7f3e7fa8b69bd
P   5355b462f83396c3bfabd793b8e05c160b7e1c78
P2  fe30270cd4c6457db49d61ab49961e236c961c06
P3  8a3935b9355dd067cf651aab53c9b21ae6773f1a
P4  a48a5ce8123064fa882fb51248285080bc9359d5
R   4da9acc622154c5ba4cb87d067070472b0128c66
P5  6b93f6ee7a720a11674e53f39ced5b1140655840
P6  ca0cab69357e112bc3136209b7567b26a7fff1f0
R2  8d9936372e6fb539ac6ed805e5cc802fded76acb
I   ca0b34aec5a804ff49b61c9d592d600b9cd6098a
```

Every role is the direct child of the preceding role. Candidate `I` has tree
`6a0a0c24724810ad7bfb4913ce1739f42bcd6602` and is the direct child of `R2`.
The amendment record binds the required direct apphost smoke assertion to the final plan.

The exact no-renames `R2..I` path set is:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/
    AtlasDiscovery.cs
    AtlasIntakeContracts.cs
  Hcoona.CelesphoniaModifier.Atlas.Cli/
    AtlasCliApplication.cs
  docs/.copilot/
    README.md
    plans/atlas-v0-a2-intake-safety-plan.md
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/
    AtlasCliApplicationTests.cs
    AtlasDiscoveryTests.cs
    AtlasProcessSmokeTests.cs
```

No `.private` path belongs to the candidate.

## 2. Correction and acceptance

The correction:

- adds the seven fixed discovery-stage values and a backward-compatible safety-exception API;
- tracks the approved discovery boundaries without reordering checks or changing safety decisions;
- wraps only uncategorized discovery safety exceptions and preserves categorized exceptions;
- emits fixed stage bytes only for `intake-discover`, with exit code 5 unchanged;
- keeps the generic safety line for unspecified or unknown stages and every other command; and
- prints no exception text, path, name, value, hash, count, or stack trace.

Synthetic evidence proves all seven reachable boundaries, the post-live baseline manifest-row
transition, fallback behavior, categorized propagation, command isolation, output privacy, and the
direct apphost `request-preflight` bytes. Exact source review retains the invariant-protected
destination-ordinal transition as `baseline-inventory` without adding a fault seam.

The correction changes no request, manifest, inventory, state, copy, cleanup, or JSON schema. It
adds no package, project, telemetry, tracing, diagnostic harness, private fixture, or private-data
access.

## 3. Validation evidence

The exact committed candidate passed:

- the repository-pinned .NET 10.0.300 SDK and locked restore;
- warning-as-error build with zero warnings and zero errors;
- `AtlasDiscoveryTests` with 81 passed, zero failed, and zero skipped;
- `AtlasCliApplicationTests` with 61 passed, zero failed, and zero skipped;
- the full Microsoft.Testing.Platform suite with 273 passed, zero failed, and zero skipped;
- direct apphost smoke with 11 passed, zero failed, and zero skipped;
- `dotnet format --verify-no-changes` for the library, CLI, and tests;
- project-reference and package-reference evaluation for all three projects;
- exact direct-parent and eight-path checks, ref-bound HK, and `git diff --check`; and
- committed LF, BOM, Markdown line-length, tree, upstream, index, and worktree checks.

Validation used only public code and synthetic temporary workspaces. It accessed no private request,
installed game, save, manifest, inventory, hash, source name, preservation content, or generated
private output.

## 4. Independent review

Fresh GPT-5.6 Sol reviewer `a2r4-implementation-reviewer` reviewed the complete exact `R2..I`
candidate against the amended plan, public contracts, source, tests, validation, privacy boundary,
and release authority. It returned exact `No findings`.

The amendment reviewers independently required and then accepted the minimal smoke-test path
expansion. The release-record reviewer independently reviewed this complete staged record and its
public bindings and returned exact `No findings`. No reviewer authored its reviewed candidate or
received private evidence.

## 5. Gate decision

This record must be committed unchanged as `G`, the direct child of `I`, with `I..G` adding only
this file. `G` must then be pushed and verified for parent, path, staged-to-committed blob, tree,
upstream, index, and clean-worktree equality.

After verified shared `G`, update only the reviewed session script's commit binding to `G`,
independently review the exact script, and return it to the project leader for one metadata-only
discovery retry. This gate does not authorize confirmation, copying, decoding, cleanup, deletion,
private-content inspection, or writes to installed game or save data.
