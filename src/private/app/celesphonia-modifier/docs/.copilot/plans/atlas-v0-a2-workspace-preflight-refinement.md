# Atlas V0 A2 Workspace-Preflight Refinement

**Lifecycle:** Active subordinate; planning-only before verified shared `R`

**Status:** Proposed scope; implementation blocked until plan review

**Increment:** A2R5 - Workspace-Preflight Refinement

**Decision owner:** Project leader

**Implementation language:** Existing C# on the repository-pinned .NET 10 SDK

**Base:** `fe8bab7484ff0d5d14b95e8615e538c2a8f073ab`

## 1. Observed fact and outcome

The one authorized A2R4 retry returned only:

```text
Safety check failed: workspace-preflight.
```

That token proves request reading and workspace-layout construction completed. It does not
distinguish the three existing calls inside the workspace-preflight boundary:

1. private-workspace policy validation;
2. discovery canonical-path validation; or
3. command workspace-census validation.

The reviewed request writer constructs paths deterministically, but public evidence cannot prove
private `.gitignore` bytes, filesystem types, reparse state, source containment, or the complete
private workspace census. A2R5 therefore adds one fixed token per existing call boundary. It does
not inspect, report, or infer any private payload.

## 2. Exact scope

Append these members without renumbering any existing public value:

```csharp
public enum AtlasDiscoveryFailureStage
{
    // Existing values 0 through 7 remain unchanged.
    PrivateWorkspacePolicy = 8,
    DiscoveryCanonicalPaths = 9,
    CommandWorkspaceCensus = 10,
}
```

`WorkspacePreflight = 2` remains public and retains its existing CLI mapping for compatibility. New
discovery execution no longer assigns it.

`DiscoverAsync` replaces its single workspace-preflight assignment with these transitions, without
reordering any check:

```text
PrivateWorkspacePolicy
  -> ValidatePrivateWorkspace
DiscoveryCanonicalPaths
  -> ValidateDiscoveryCanonicalPaths
CommandWorkspaceCensus
  -> ValidateCommandWorkspaceCensus
ExistingState
  -> existing A2R4 control flow
```

The exact new diagnostics are:

```text
Safety check failed: private-workspace-policy.
Safety check failed: canonical-paths.
Safety check failed: workspace-census.
```

They remain LF-terminated fixed UTF-8 bytes on standard error with exit code 5. No enum-name
formatting, exception message, path, name, value, hash, count, stack trace, or runtime data may
reach either output stream.

## 3. Exclusions

A2R5 does not:

- read or change the private request, workspace, installed game, saves, manifests, inventory, or
  generated output;
- change a validator, safety decision, trusted-local profile, request writer, or execution script;
- change a request, manifest, inventory, state, copy, cleanup, or JSON schema;
- add a package, project, harness, telemetry, logging, tracing, dump, or private fixture;
- split canonical-path or census validation into per-check identifiers; or
- authorize confirmation, copying, decoding, cleanup, deletion, private-content inspection, or
  live-save writes.

## 4. Exact repository candidates

`P` adds only this plan. `R` adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-workspace-preflight-refinement-plan-review.md
```

`I` may change only:

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
```

`G` adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-workspace-preflight-refinement-release-gate.md
```

The immutable chain is `B -> P -> P2 -> R -> I -> G`. `P2` changes only this plan to resolve plan
review. Each exact staged `R` and `G` blob must receive independent `No findings`, be committed
unchanged, and then be pushed and verified as the clean shared branch tip. Every other role must
also be pushed and verified as the clean shared branch tip before the next role begins.

## 5. Acceptance evidence

The candidate is acceptable when:

1. a discover-level synthetic invalid private-workspace policy produces
   `PrivateWorkspacePolicy`;
2. the existing noncanonical revision-directory refusal produces
   `DiscoveryCanonicalPaths`;
3. the existing unexpected revision artifact or released-A0 near-match refusal produces
   `CommandWorkspaceCensus`;
4. the existing CLI theory proves the three new exact byte sequences and exit code 5;
5. the legacy `WorkspacePreflight` value still emits its A2R4 token;
6. `Unspecified` and unknown values still use the generic fallback;
7. empty survey, confirm, copy, and cleanup remain generic for every stage-bearing exception;
8. private exception text and request paths remain absent from both streams;
9. the current A2 plan and index document the A2R5 gate without rewriting A2R4 history;
10. existing A2R3/A2R4 tests, unchanged process smoke, and the complete suite remain enabled;
11. locked restore, warning-as-error build, format, focused tests, full tests, smoke, reference,
    ref-bound HK, and Git candidate-integrity checks pass;
12. a fresh GPT-5.6 Sol reviewer returns exact `No findings` for committed `I`; and
13. independent reviewers return exact `No findings` for the staged `R` and `G` records before
    those exact blobs are committed unchanged.

Per-throw matrices, canonical-path subcategories, census-boundary subcategories, and new process
tests are not required.

## 6. Stop, authority, and resume

Stop and return to planning if a boundary requires dynamic output, private inspection, a changed
safety decision, a schema or package, or a new harness.

This plan grants no private-run authority. After verified shared `G`, update only the reviewed
session script's commit binding to `G`, independently review the exact script, and return it to the
project leader for one metadata-only retry.

Retry handoff is closed:

- success returns to the active A2 local-review procedure;
- a new fixed token stops execution and scopes a repository-safe diagnosis of only that call;
- the legacy or generic fallback reopens only the categorization gap; and
- any other fixed diagnostic follows the existing A2 stop policy.
