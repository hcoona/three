# Atlas V0 A2 Safe Failure-Stage Diagnostics

**Lifecycle:** Active subordinate; planning-only before verified shared `R`

**Status:** Approved scope; implementation blocked until plan review

**Increment:** A2R4 - Safe Failure-Stage Diagnostics

**Decision owner:** Project leader

**Audience:** Project leader, implementers, independent reviewers, and future resumers

**Purpose:** Identify the next metadata-discovery refusal boundary without exposing its payload

**Implementation language:** Existing C# on the repository-pinned .NET 10 SDK

**Base:** `b8fb8eb5f84f41e6e7bf98a7aea7f3e7fa8b69bd`

**Governing sources:** Active A2 intake-safety plan, A2R3 release gate, and project operating model

**Dependencies:** Existing Atlas library, CLI, synthetic workspace, and Microsoft.Testing.Platform

**Unresolved risk:** The next retry may reach a valid fixed stage but still require a separately
planned repository-safe diagnosis

**Plan-review record:**
`../reviews/atlas-v0-a2-safe-failure-stage-diagnostics-plan-review.md`

**Release-gate record:**
`../reviews/atlas-v0-a2-safe-failure-stage-diagnostics-release-gate.md`

## 1. Problem and decision

After verified A2R3, the user-operated metadata-only discovery rebuilt successfully and then emitted
only `Safety check failed.`. That proves a later `AtlasSafetyException` boundary failed, but the
current CLI intentionally collapses every safety refusal to the same bytes. Public code and that
single line cannot identify the failing boundary without guessing.

A2R4 adds a closed, payload-free stage to `intake-discover` safety failures:

```text
request-preflight
workspace-preflight
existing-state
baseline-inventory
live-source-preflight
corpus-reconciliation
publication
```

The CLI retains exit code 5 and emits exactly
`Safety check failed: <stage>.\n`. An absent or unknown stage retains the existing
`Safety check failed.\n` fallback. The implementation switches over fixed enum values and fixed byte
arrays; it never emits `Exception.Message`, enum formatting, paths, values, hashes, names, counts,
stack traces, or other dynamic text.

The library adds this exact public enum:

```csharp
public enum AtlasDiscoveryFailureStage
{
    Unspecified = 0,
    RequestPreflight = 1,
    WorkspacePreflight = 2,
    ExistingState = 3,
    BaselineInventory = 4,
    LiveSourcePreflight = 5,
    CorpusReconciliation = 6,
    Publication = 7,
}
```

`AtlasSafetyException` keeps its existing
`public AtlasSafetyException(string message)` constructor unchanged. It adds public read-only
property `AtlasDiscoveryFailureStage DiscoveryStage` and constructor
`public AtlasSafetyException(string message, AtlasDiscoveryFailureStage discoveryStage,
Exception? innerException = null)`. The message-only constructor yields `Unspecified`. This
additive public surface is required because the CLI is a separate assembly; no friend-assembly
access is added.

The seven stages identify execution location, not an A0 difference or private fact. Existing
root-set, denominator, selection-rule, public-build, unsupported/unreadable, and no-difference
categories remain separate and unchanged.

## 2. Scope

In scope:

- attach the current stage when `AtlasDiscovery.DiscoverAsync` propagates an
  `AtlasSafetyException`;
- map only those seven values to fixed CLI diagnostics when the invoked command is
  `intake-discover`;
- preserve the generic safety fallback for uncategorized failures and every other command;
- correct the A2R3 lifecycle text and document the fixed diagnostics and A2R4 gate;
- add focused synthetic and CLI tests; and
- independently review the exact committed implementation and release record.

Out of scope:

- reading or changing the private request, workspace, game, saves, manifests, inventory, or output;
- printing raw exception messages or adding per-throw identifiers;
- changing request, manifest, inventory, state, copy, cleanup, or JSON schemas;
- telemetry, logs, tracing, dumps, a diagnostic harness, new packages, or new projects;
- changing safety checks, recovery behavior, exit codes, success output, or execution authority; and
- confirmation, copying, decoding, cleanup, deletion, or live-save writes.

## 3. Stage boundaries

`DiscoverAsync` maintains one current stage without reordering its existing control flow:

1. request reading and workspace-layout construction use `request-preflight`;
2. private-workspace, canonical-path, and census checks use `workspace-preflight`;
3. idempotent existing-state validation uses `existing-state`;
4. baseline manifest reading, inventory loading, and discovery-alias resolution use
   `baseline-inventory`;
5. live source existence, type, containment, and profile checks use `live-source-preflight`;
6. the following baseline manifest-artifact lookup returns to `baseline-inventory`;
7. source enumeration and baseline reconciliation use `corpus-reconciliation`;
8. the following destination-artifact ordinal parsing returns to `baseline-inventory`; and
9. pending documents, inventory replacement, and state publication use `publication`.

Only `AtlasSafetyException` receives a stage. Request, approval, I/O, cancellation, and unexpected
exception mappings remain unchanged. The outer catch wraps only an `Unspecified` safety exception;
an already categorized exception propagates unchanged.

## 4. Exact repository candidates

`P` adds only this plan. `P2` and `P3` are consecutive plan-only children that resolve independent
review findings. `R` is the direct child of `P3` and adds only the plan-review record.

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

`G` adds only the release-gate record. The immutable chain is
`B -> P -> P2 -> P3 -> R -> I -> G`. Each role must be pushed and verified as the clean shared
branch tip before the next role proceeds.

## 5. Acceptance evidence

The candidate is acceptable when:

1. representative synthetic refusals prove all seven stage values, plus the baseline manifest-row
   lookup and destination-ordinal return transitions;
2. one CLI theory proves exact UTF-8 bytes and exit code 5 for all seven stages;
3. separate cases prove both `Unspecified` and an unknown enum value use the generic fallback;
4. an injected pre-categorized exception propagates without being recategorized;
5. stage-bearing safety exceptions injected into empty survey, confirm, copy, and cleanup remain
   generic;
6. injected private exception text and request paths are absent from both output streams;
7. the active A2 plan documents the discover-only fixed diagnostics and unchanged privacy boundary;
8. the index marks A2R3 released, adds the A2R4 plan and gate-dependent review navigation, and does
   not imply `I` grants private-run authority;
9. no test reads private data or adds a private fixture;
10. the original A2R3 compatibility tests and complete suite remain enabled and pass;
11. locked restore, build, format, tests, smoke, reference, ref-bound HK, and Git
    candidate-integrity checks pass; and
12. a fresh GPT-5.6 Sol reviewer returns exact `No findings` for the committed candidate.

Per-throw matrices, lifecycle cross-products, coverage work, performance work, and duplicate harness
tests are not required.

## 6. Stop, authority, and resume

Stop and return to planning if a stage requires dynamic output, exposes private information, changes
a safety decision, needs a schema or package, or cannot be tested synthetically.

This plan grants no private-run authority. After verified shared `G`, update only the reviewed
session script's commit binding to `G`, independently review the exact script, and return it to the
project leader for one metadata-only retry. The project leader reports only the fixed stage token.

Retry handoff is closed:

- success returns to the active A2 local-review procedure and reports only its approved aggregate
  result;
- a fixed stage token stops execution and scopes a new repository-safe diagnosis;
- the generic safety fallback stops execution and reopens only the categorization gap; and
- an approval, request, I/O, cancellation, or unexpected diagnostic follows the existing A2 stop
  policy without disclosing additional detail.

To resume: verify and independently review `P3`, commit the record-only `R`, implement and validate
the exact path set, commit and independently review `I`, then commit the independently reviewed
record-only `G`.
