# Workflow Release Workflow and Executor Boundaries

## Purpose

This page defines the Group 3 design layer: control-plane workflow entry points,
reusable workflow and job seams, planner-to-executor contracts, and executor
limits on top of `three.release.plan/v1alpha1`.

## Design Summary

- `buddy` and `official` remain the only top-level `workflow_dispatch` entry
  workflows.
- Both entry workflows call one shared reusable orchestration workflow with the
  selected profile and the raw dispatch envelope.
- The orchestration workflow consumes one frozen `three.release.plan/v1alpha1`
  and fans out at two granularities only: one build unit per `variant-id` and
  one publish unit per `publish-node-id`.
- Build units emit per-variant build bundles plus machine-readable build
  receipts keyed by plan `artifact-id`.
- Publish units emit per-publish-node publish receipts keyed by plan
  `publish-node-id`.
- Approvals, concurrency, dry-run gating, tagging, permissions, runner or
  toolchain wiring, artifact transport, and final reporting remain control-plane
  responsibilities.
- Executors are thin consumers of plan-defined intent and must never re-plan,
  rediscover targets, or derive alternate publish identity.

## Boundary to Group 1 and Group 2

- Group 1 owns descriptor and shared target-instance catalog authoring.
- Group 2 owns the authoritative frozen `three.release.plan/v1alpha1` shape.
- This page owns only the workflow, job, and executor seams that consume that
  frozen plan.

Nothing here reopens descriptor discovery, target compatibility, plan graph
shape, or planner-owned resolved publish identity.

## Control-Plane Workflow Topology

### Top-Level Boundaries

| Boundary                      | Kind               | Stable granularity       | Owns                                                                                                                                                   |
| ----------------------------- | ------------------ | ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `buddy` entry workflow        | top-level workflow | one `buddy` run          | manual dispatch inputs, profile selection, entry permissions, and top-level concurrency wiring                                                         |
| `official` entry workflow     | top-level workflow | one `official` run       | manual dispatch inputs, profile selection, approval environment, entry permissions, and top-level concurrency wiring                                   |
| shared orchestration workflow | reusable workflow  | one selected-profile run | planning, selector derivation, approval and side-effect sequencing, tag orchestration, artifact fan-out and fan-in, and final reporting                |
| `build-variant` unit          | reusable workflow  | one `variant-id`         | build-request materialization, ecosystem-specific build-executor selection, runner or tool wiring, and upload of one variant bundle plus build receipt |
| `publish-node` unit           | reusable workflow  | one `publish-node-id`    | publish-request materialization, family-specific publish-executor selection, download of referenced build bundles, and upload of one publish receipt   |

The stable reusable boundaries are therefore:

1. profile entry workflow -> shared orchestration workflow;
2. shared orchestration workflow -> one build unit per `variant-id`;
3. shared orchestration workflow -> one publish unit per `publish-node-id`.

### Required Job Sequence Inside the Shared Orchestration Workflow

1. `plan` job
    - consumes the raw control-plane run envelope;
    - invokes the planner;
    - publishes the frozen `three.release.plan/v1alpha1` artifact;
    - derives the selected `variant-id` and `publish-node-id` sets for later fan-
      out.
2. `build` fan-out
    - runs exactly once per active `variant-id`;
    - produces one bundle and one build receipt per variant.
3. `approve` gate
    - stays in the control plane;
    - guards all external side effects;
    - may be bypassed for profiles that do not require approval.
4. `ensure-tag` job
    - stays in the control plane;
    - creates or verifies the repository tag exactly once per run when any
      selected publish node resolves to a GitHub Release publication.
5. `publish` fan-out
    - runs exactly once per selected `publish-node-id` whose plan
      `publish-disposition` is `publish`;
    - emits one publish receipt per publish node.
6. `report` job
    - aggregates plan metadata, build receipts, publish receipts, synthetic skip
      receipts, and GitHub job conclusions into the final operator-facing summary.

`approve`, `ensure-tag`, and `report` are ordinary control-plane jobs, not
executor boundaries.

### Active Build and Publish Set Derivation

The shared orchestration workflow must derive execution sets only from the frozen
plan:

- `active-publish-node-ids` are the selected publish nodes whose
  `publish-disposition` is `publish`.
- `active-variant-ids` are the distinct variants reachable from the artifacts
  referenced by those active publish nodes.
- Selected publish nodes whose `publish-disposition` is
  `skip-immutable-satisfied` do not invoke a publish executor and do not force a
  build. The control plane instead emits a synthetic skip receipt for reporting.

This keeps rerun skip logic planner-owned rather than executor-owned.

### Dry-Run Boundary

Dry-run or validation-only mode stays in the raw control-plane run envelope, not
in the plan. In current scope it must suppress side effects:

- it may run planning and any non-publishing validation steps;
- it must not create tags;
- it must not invoke live publish executors.

Whether a dry run also performs build execution is an implementation choice, but
that choice must not change the stable workflow or executor contracts defined
here.

## What Consumes the Frozen Plan

| Consumer                      | Required frozen input                                                                                                                                                                                         | Granularity rule                                      |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------- |
| shared orchestration workflow | full `release-plan` envelope and graph                                                                                                                                                                        | one per selected profile run                          |
| one build unit                | owning `envelope.projects[project-id]` snapshot, one `graph.variants[variant-id]`, and that variant's `graph.artifacts[*]`                                                                                    | one build executor invocation per `variant-id`        |
| one publish unit              | owning `envelope.projects[project-id]` snapshot, one `graph.publish-nodes[publish-node-id]`, its referenced `graph.target-instance-snapshots[*]`, and the referenced `graph.artifacts[*]` plus build receipts | one publish executor invocation per `publish-node-id` |
| report job                    | full plan plus all build and publish receipts                                                                                                                                                                 | one per selected profile run                          |

A publish unit may consume artifacts from multiple variants only when the frozen
publish node already references them and the frozen target-instance contract
allows that aggregation. Executors do not widen that set.

## Job-to-Job Handoff Boundaries

### Reusable Workflow Inputs

| Boundary                      | Required input                                                                                           | Required output                           |
| ----------------------------- | -------------------------------------------------------------------------------------------------------- | ----------------------------------------- |
| orchestration -> build unit   | immutable plan artifact plus one `variant-id` selector                                                   | one variant bundle plus one build receipt |
| orchestration -> publish unit | immutable plan artifact plus one `publish-node-id` selector and the referenced build bundles or receipts | one publish receipt                       |

The reusable workflow boundary carries selectors and immutable artifacts.
The executor boundary inside each unit is narrower and uses a materialized
request object.

### Build Executor Contract

Each build unit must materialize one logical `build-request` object for its
executor with at least these fields:

| Field                                               | Source                              |
| --------------------------------------------------- | ----------------------------------- |
| `api-version: three.release.build-request/v1alpha1` | control-plane materialization       |
| `kind: build-request`                               | control-plane materialization       |
| `plan-id`, `profile`, `commit-sha`                  | plan envelope                       |
| `project` snapshot                                  | `envelope.projects[project-id]`     |
| `variant` snapshot                                  | `graph.variants[variant-id]`        |
| `artifacts` map keyed by `artifact-id`              | all artifacts owned by that variant |

A build executor may read checked-out repository files and manifests referenced
by that request, but it must not re-read descriptors or the shared target
catalog.

Each build unit must emit one logical `build-result` object with at least these
fields:

| Field                                              | Meaning                                                          |
| -------------------------------------------------- | ---------------------------------------------------------------- |
| `api-version: three.release.build-result/v1alpha1` | contract version                                                 |
| `kind: build-result`                               | result type                                                      |
| `plan-id`, `project-id`, `variant-id`              | receipt identity                                                 |
| `artifacts[artifact-id].bundle-relative-path`      | where the produced file lives inside the uploaded variant bundle |

Every `artifact-id` declared in the corresponding `build-request.artifacts` map
must appear exactly once in the `build-result` map. The build executor owns file
production; the control plane owns artifact upload and later download.

### Publish Executor Contract

Each publish unit must materialize one logical `publish-request` object for its
executor with at least these fields:

| Field                                                 | Source                                                                             |
| ----------------------------------------------------- | ---------------------------------------------------------------------------------- |
| `api-version: three.release.publish-request/v1alpha1` | control-plane materialization                                                      |
| `kind: publish-request`                               | control-plane materialization                                                      |
| `plan-id`, `profile`                                  | plan envelope                                                                      |
| `project` snapshot                                    | `envelope.projects[project-id]`                                                    |
| `publish-node` snapshot                               | `graph.publish-nodes[publish-node-id]`                                             |
| `target-instance-snapshot`                            | referenced `graph.target-instance-snapshots[*]`                                    |
| `artifacts` map keyed by `artifact-id`                | frozen artifact metadata from the plan plus resolved file paths from build results |

The publish unit must create that request only when the selected publish node has
`publish-disposition: publish`. For
`publish-disposition: skip-immutable-satisfied`, the control plane emits the
receipt directly without invoking a publish executor.

Each publish unit must emit one logical `publish-result` object with at least
these fields:

| Field                                                | Meaning                                                                                       |
| ---------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `api-version: three.release.publish-result/v1alpha1` | contract version                                                                              |
| `kind: publish-result`                               | result type                                                                                   |
| `plan-id`, `project-id`, `publish-node-id`           | receipt identity                                                                              |
| `target-instance-snapshot-id`                        | the destination slot that was acted on                                                        |
| `resolved-publish-identity`                          | copied from the publish node for traceability                                                 |
| `outcome: published`                                 | successful live publication                                                                   |
| `evidence`                                           | small family-specific receipt data such as returned URL or registry identifier when available |

Skip receipts for immutable-target replay stay control-plane-authored and do not
pass through the publish executor contract.

## Control-Plane Ownership Rules

The following concerns are explicitly control-plane-owned:

- **approvals**: only the control plane decides whether and when approval is
  required;
- **concurrency**: only the entry workflow or orchestration workflow sets the
  duplicate-run concurrency key, using the already frozen workflow-entry-point
  plus commit rule;
- **tagging**: the planner resolves the final `release-tag`, but the control
  plane creates or verifies the Git tag once per run before any GitHub Release
  publication;
- **runtime wiring**: runner selection, tool installation, permissions,
  credential injection, and environment selection stay in workflow jobs and
  wrappers rather than inside executors;
- **artifact transport**: upload, download, naming, and retention of build
  bundles and receipts stay in the control plane;
- **orchestration**: matrix fan-out, dependency ordering, rerun wiring, and
  failure aggregation stay in the control plane;
- **reporting**: only the control plane assembles final summaries across multiple
  build and publish units.

## Current-Scope Executor Routing

Current-scope routing is grounded in the actual monorepo and the accepted Group 1
schema:

- build units select an ecosystem-specific build executor from
  `project.ecosystem`, currently .NET, Python, Node.js, or Ruby;
- publish units select a target-family-specific publish executor from
  `target-instance-snapshot.family`, currently `github-release`, `nuget`,
  `pypi`, `npm`, or `rubygems`.

This routing happens after planning and consumes only frozen plan data plus the
checked-out source tree.

## What Explicitly Stays Out of Executors

Executors must not own any of the following:

- descriptor discovery, schema validation, or shared-catalog loading;
- project selection, target selection, target compatibility checks, or publish-
  node construction;
- version derivation, `release-tag` derivation, or final package-name derivation;
- approval handling, concurrency handling, dry-run policy, or cancellation
  policy;
- Git tag creation, multi-job artifact transport, or final run reporting;
- immutable-target replay decisions beyond honoring the already frozen plan
  disposition;
- combining multiple publish nodes into one alternate publish transaction;
- inventing artifacts, variants, or destination-side projections that are not in
  the request they were given.

Executors are allowed to:

- read the checked-out repository files named by the build or publish request;
- perform the one build or publish action represented by that request;
- return structured receipts for the control plane to aggregate.

## Outcome

With these boundaries, the cross-layer seam is now explicit:

descriptor -> frozen plan -> per-variant build request -> per-publish-node
publish request -> aggregated control-plane report.

The remaining work is implementation of the frozen boundaries, not more design
about where planning stops and execution starts.

## Related Pages

- [Workflow Release Design Direction](./workflow-release-design-direction.md)
- [Workflow Release Architecture Model](./workflow-release-architecture-model.md)
- [Workflow Release Descriptor Schema](./workflow-release-descriptor-schema.md)
- [Workflow Release Plan Shape](./workflow-release-plan-shape.md)
