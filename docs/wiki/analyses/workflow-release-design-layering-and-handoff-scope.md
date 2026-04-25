# Workflow Release Design Layering and Implementation Handoff Scope

## Purpose

This page records how the current workflow-release design corpus should be read
through a three-layer design model and what portion of that design is ready to
hand to implementation.

The goal is not to reopen the signed-off requirements or the planner-centric
architecture direction. The goal is to clarify which design decisions are
already settled, which bounded seams still belong to design, and which details
may be left to an experienced implementer.

## Three-Layer Framing

| Layer               | Main question                                                                                                 | Current primary pages                                                                                                                                                                                                                                                                                                                    | Current status                        |
| ------------------- | ------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------- |
| Upper-layer design  | What system are we building, what are its major responsibilities, and what principles are fixed?              | [Workflow Release Requirements Baseline](./workflow-release-requirements-baseline.md), [Workflow Release Design Direction](./workflow-release-design-direction.md), [Workflow Release Architecture Model](./workflow-release-architecture-model.md)                                                                                      | Settled for current scope             |
| Middle-layer design | What cross-component contracts must be frozen so implementation does not reinterpret the business rules?      | [Workflow Release Architecture Model](./workflow-release-architecture-model.md), [Workflow Release Descriptor Schema](./workflow-release-descriptor-schema.md), [Workflow Release Plan Shape](./workflow-release-plan-shape.md), [Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md) | Settled for current scope             |
| Lower-layer design  | How will the bounded components be realized in code, workflow files, scripts, receipts, and internal helpers? | No separate exhaustive page set yet                                                                                                                                                                                                                                                                                                      | Intentionally only partially designed |

## Current Assessment by Layer

### Upper-Layer Design

Upper-layer design is effectively closed in the current scope.

The doc set already fixes these top-level decisions:

- workflow release is planner-centric rather than workflow-centric;
- the system is split into control plane, planning layer, and execution layer;
- participation is descriptor-gated;
- one run may target multiple selected projects within one profile entry point;
- `buddy` and `official` are separate workflow entry points;
- canonical build semantics remain unified per declared variant;
- targets are project-declared rather than repo-defaulted.

No major upper-layer architecture gap remains in the current scope. The design
does not need another broad architecture pass before implementation.

### Middle-Layer Design

Middle-layer design is now fully sealed for current scope.

The current doc set already provides:

- the author-time file model and descriptor discovery rules;
- the shared target-instance catalog model;
- the authoritative `three.release.plan/v1alpha1` envelope and graph shape;
- stable build and publish fan-out granularity;
- planner-owned versus control-plane-owned versus executor-owned boundaries.

The final middle-layer seam decisions for current scope are now:

1. **Selected commit materialization**
    - Manual `workflow_dispatch` selects a branch or tag ref in the GitHub UI.
    - The control plane resolves that choice once to one exact `commit-sha` at
      run start.
    - All later planning, build, tag, and publish stages stay pinned to that
      same resolved SHA.
2. **Prior build-receipt durability and lookup**
    - Prior build-receipt lookup remains control-plane-owned.
    - Current scope relies on the platform's default GitHub Actions artifact
      retention window for those records.
    - Immutable proof reuse is therefore guaranteed only while the relevant
      records remain unexpired in that default window; after expiry, proof is
      unavailable and planning fails closed when that proof is required.
3. **Planner-time remote-observation auth model**
    - Planner-time destination observation uses public reads where possible.
    - For GitHub-hosted surfaces in current scope, the control plane may provide
      least-privilege read access through `GITHUB_TOKEN`.
    - Planner-time observation must not use publish credentials or approval-
      gated environment secrets.
4. **`official` trigger-role enforcement**
    - `official` requires an explicit early control-plane authorization check of
      the triggering actor's repository permission.
    - That check fails closed unless the actor has at least `maintain`.
    - This remains distinct from the later protected-environment approval gate.
5. **Multi-tag `ensure-tag` atomicity**
    - When one run requires more than one distinct project-scoped release tag,
      the control plane first prechecks the full required tag set.
    - If any already-existing required tag points elsewhere, the run fails
      before any new tag is created.
    - Only after that full precheck passes may the control plane create the
      missing required tags for the run.
6. **Package-registry identity conformance**
    - Current-scope package identity sources and equivalence rules are frozen per
      target family: NuGet explicit `PackageId`, PyPI normalized
      `[project].name`, npm descriptor override or `package.json` `name`, and
      RubyGems evaluated `Gem::Specification.name`.
    - For each live package-registry publish member, the concrete produced file
      is validated against the owning publish node's frozen
      `resolved-publish-identity`.
    - That validation uses the target family's canonical equivalence rules and
      fails closed on mismatch before live upload.
    - The validation is a publish-time conformance check, not a fresh executor-
      owned derivation of package identity.
7. **`nbgv-python` version-authority special support**
    - `nbgv-python` is the only current-scope exception to the normal
      build-system-integrated NBGV contract.
    - Its project-scoped version identity is resolved from the selected
      commit's checked-in `pyproject.toml` `[project].version` through an
      explicit descriptor-declared special-support path.
    - The planner freezes that resolved version into the project snapshot, and
      later build and publish stages must fail closed on any mismatch.

With these contracts written back into the normative pages, middle-layer design
no longer has any implementation-blocking open seam in current scope.

### Lower-Layer Design

Lower-layer design does not need to be exhaustively authored before
implementation for an experienced programmer.

After the middle-layer seam items above are frozen, the following details may
remain implementation-owned as long as they stay within the documented
contracts:

- repo file layout for planner, validators, and workflow helpers;
- internal module, class, function, and script decomposition;
- exact reusable-workflow file names and internal job wiring details that do
  not change the published control-plane boundaries;
- concrete action selection, shell wrappers, and helper command structure;
- receipt file locations, temporary directories, and logging structure;
- language-specific helper APIs and local refactoring choices.

In other words, the remaining lower-layer work should be implementation detail,
not backdoor architecture design.

## Implementation Handoff Scope

### Ready to hand off now

The current design package is already ready to hand off in these areas:

- the signed-off business rules and first-delivery acceptance scope;
- the planner-centric top-level architecture;
- the author-time descriptor and shared target-instance catalog model;
- the frozen plan shape and normalized graph model;
- the control-plane, planner, build-unit, and publish-unit responsibility split.

### Explicitly left to implementation

The current handoff does **not** attempt to freeze every workflow line, script,
or helper API. That lower-layer realization work may be delegated to an
experienced implementer within the now-frozen middle-layer contracts.

### Pre-lower-layer handoff review

A final middle-layer review found no blocking upper-layer or middle-layer gap
that should delay lower-layer design. The remaining work should therefore be
treated as low-level handoff tracking rather than another business-rule or
architecture pass.

Before detailed workflow files, scripts, or executor internals are authored, the
lower-layer design should carry forward these handoff guardrails:

| Handoff item                       | Status in middle-layer design                                                                                                   | Lower-layer responsibility                                                                                                              |
| ---------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| Acceptance traceability            | Requirements and middle-layer contracts are frozen, but there is no separate trace table from acceptance scenarios to tests.    | Map each acceptance scenario to the frozen descriptor, plan, workflow-boundary, receipt, and reporting contracts it proves.             |
| Planner diagnostic codes           | `planner-diagnostic.code` is a stable field, while the current middle layer intentionally avoids a full error taxonomy.         | Define the concrete minimum diagnostic-code vocabulary or planner-owned code-registration rule used by tests and reporting.             |
| Dry-run build behavior             | Dry-run is outside rerun identity and must suppress tags and live publish, while build execution is explicitly left optional.   | Choose the concrete dry-run build policy and ensure any validation-only build receipts cannot satisfy live immutable-proof lookup.      |
| Receipt lookup and artifact layout | Immutable-proof admissibility, provenance, and default-retention limits are frozen, but storage layout and index shape are not. | Design the concrete artifact names, receipt transport, lookup/index layout, and provenance attachment without changing proof semantics. |

These items do not reopen the middle-layer contracts. They are the first
low-level design checkpoints needed to keep implementation traceable to the
waterfall handoff.

## Summary Judgment

For the current workflow-release initiative, the design package is best
understood as:

- **upper-layer design:** closed;
- **middle-layer design:** closed;
- **lower-layer design:** intentionally only partially authored.

That means the next work is implementation, not another middle-layer design
pass. Lower-layer realization details remain intentionally implementation-owned
within the frozen contracts above.

## Related Pages

- [Workflow Release Requirements Baseline](./workflow-release-requirements-baseline.md)
- [Workflow Release Design Direction](./workflow-release-design-direction.md)
- [Workflow Release Architecture Model](./workflow-release-architecture-model.md)
- [Workflow Release Descriptor Schema](./workflow-release-descriptor-schema.md)
- [Workflow Release Plan Shape](./workflow-release-plan-shape.md)
- [Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md)
