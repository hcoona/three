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

| Layer               | Main question                                                                                                 | Current primary pages                                                                                                                                                                                                                                                                                                                    | Current status                                |
| ------------------- | ------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------- |
| Upper-layer design  | What system are we building, what are its major responsibilities, and what principles are fixed?              | [Workflow Release Requirements Baseline](./workflow-release-requirements-baseline.md), [Workflow Release Design Direction](./workflow-release-design-direction.md), [Workflow Release Architecture Model](./workflow-release-architecture-model.md)                                                                                      | Settled for current scope                     |
| Middle-layer design | What cross-component contracts must be frozen so implementation does not reinterpret the business rules?      | [Workflow Release Architecture Model](./workflow-release-architecture-model.md), [Workflow Release Descriptor Schema](./workflow-release-descriptor-schema.md), [Workflow Release Plan Shape](./workflow-release-plan-shape.md), [Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md) | Mostly settled, with bounded pre-handoff gaps |
| Lower-layer design  | How will the bounded components be realized in code, workflow files, scripts, receipts, and internal helpers? | No separate exhaustive page set yet                                                                                                                                                                                                                                                                                                      | Intentionally only partially designed         |

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

Middle-layer design is largely in place, but the implementation handoff is not
yet fully sealed.

The current doc set already provides:

- the author-time file model and descriptor discovery rules;
- the shared target-instance catalog model;
- the authoritative `three.release.plan/v1alpha1` envelope and graph shape;
- stable build and publish fan-out granularity;
- planner-owned versus control-plane-owned versus executor-owned boundaries.

Before the implementation handoff can be treated as fully complete, design
still needs to freeze these bounded seam items:

1. **Selected commit materialization**
    - Define how the selected commit is chosen from the dispatch request and how
      every planning, build, tag, and publish step is guaranteed to operate on
      that same commit.
2. **Prior build-receipt durability and lookup**
    - Define where prior build receipts or proof records live, how long they must
      remain available, and what lookup contract the planner may rely on for
      immutable-target replay handling.
3. **Planner-time remote-observation auth model**
    - Define what permission model or credentials planner-time destination
      queries use before approval and publish jobs run.
4. **`official` trigger-role enforcement**
    - Define how the `maintain+` trigger requirement for `official` is enforced at
      workflow entry or early control-plane execution time.
5. **Closed current-scope vocabularies for artifact typing**
    - Explicitly close the valid current-scope value sets for `role`,
      `kind-family`, and `concrete-kind` so schema validation can be implemented
      without guesswork.

These are not broad redesign items. They are cross-component contracts that
should be frozen by design rather than discovered during implementation.

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

### Still design-owned before handoff is complete

The implementation handoff should not yet be treated as fully closed until the
five seam items in the middle-layer section are written back into the normative
design pages.

### Explicitly left to implementation

The current handoff does **not** attempt to freeze every workflow line, script,
or helper API. That lower-layer realization work may be delegated to an
experienced implementer after the bounded seam items above are resolved.

## Summary Judgment

For the current workflow-release initiative, the design package is best
understood as:

- **upper-layer design:** closed;
- **middle-layer design:** mostly closed, but still missing a small number of
  implementation-critical seam contracts;
- **lower-layer design:** intentionally only partially authored.

That means the next design task is not a fresh architecture round. It is a
small handoff-hardening pass that freezes the remaining cross-layer contracts
before implementation begins in earnest.

## Related Pages

- [Workflow Release Requirements Baseline](./workflow-release-requirements-baseline.md)
- [Workflow Release Design Direction](./workflow-release-design-direction.md)
- [Workflow Release Architecture Model](./workflow-release-architecture-model.md)
- [Workflow Release Descriptor Schema](./workflow-release-descriptor-schema.md)
- [Workflow Release Plan Shape](./workflow-release-plan-shape.md)
- [Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md)
