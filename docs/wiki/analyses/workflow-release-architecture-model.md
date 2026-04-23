# Workflow Release Architecture Model

## Purpose

This page records the architecture-layer design decisions that were settled
after the requirements phase signoff. It still stays above descriptor schema,
YAML layout, and executor API details.

## Architecture Summary

The release system is now designed as a **planner-centric architecture** with
three major layers:

1. **Control plane** — GitHub workflow entry points, approvals, concurrency,
   dispatch envelope, job orchestration, and artifact passing.
2. **Planning layer** — repo-owned planning logic that reads descriptors,
   validates the request, expands project scope, and emits a fully materialized
   declarative release plan.
3. **Execution layer** — reusable build, packaging, and publication executors
   that consume the precomputed plan.

The planner output is a **fully materialized declarative plan** rather than a
partially expanded graph.

## Plan Top-Level Shape

The architecture distinguishes two different envelopes:

1. **Control-plane run envelope** — raw GitHub Actions runtime context such as
   `workflow_dispatch` inputs, actor, run identifiers, approval state, and
   orchestration state.
2. **Plan envelope** — the planner's normalized, authoritative header for the
   computed release plan, containing the resolved request summary rather than
   raw workflow runtime state.

Only the second one belongs to the declarative plan.

The plan itself has two top-level parts:

1. **Envelope** — resolved request summary, profile, commit, selected projects,
   and plan metadata.
2. **Graph** — normalized ID-based objects and their relationships.

`Request / Scope` stays in the envelope rather than becoming an executable graph
node. The control-plane run envelope is an input to planning; the plan envelope
is planner output.

## Normalized Graph Core

The graph uses a normalized model for the core reusable objects:

- `variant`
- `artifact`
- `publish-node`
- `target-instance-snapshot`

These are the only first-class ID-addressable architecture objects at this
layer. Smaller structures remain inline value objects, including:

- target-side projection;
- capability subfields;
- destination-contract internal constraints;
- display metadata and other presentation-only fields.

At this layer, `destination-contract` is a reusable named type outside the
normalized graph core. Catalog target instances declare one, and plan
target-instance snapshots carry the resolved contract structure inline for
execution.

`project` is an architecture-level owning scope rather than a first-class graph
entity at this layer. The plan envelope carries the resolved project set, and
project-scoped graph objects carry `project-id` as an ownership anchor.

## Variant and Artifact Model

### Variant

A `variant` is a **runner-constrained canonical output family**. It is defined
by a declarative set of dimensions rather than by a hand-written slug.

Rules:

- only dimensions that change the canonical output family belong in variant
  identity;
- TFM is not automatically a variant dimension; it only becomes one when it
  changes the artifact family;
- `buddy` / `official` do not directly enter variant identity;
- if profile-specific differences truly change the produced artifacts, they must
  be expressed through explicit production-flavor or variant dimensions.

### Artifact Production

Artifact production is **variant-centric**, not target-centric.

Rules:

- one variant may produce multiple artifacts;
- one artifact belongs to exactly one variant;
- there is no explicit multi-variant bundle artifact in the current scope;
- multi-platform publication is modeled as multiple sibling variants whose
  artifacts may later be published together.

### Artifact Identity

Artifact identity is based on:

- `project-id`
- `variant-id`
- `kind-family`
- `concrete-kind`
- `logical-artifact-role`

Artifact identity explicitly excludes publish-time final naming such as:

- target-side package renames;
- npm scope rewrites;
- GitHub Release asset labels;
- other destination-side display projections.

### Artifact Typing

Artifacts use two type layers:

1. **Kind family** — cross-ecosystem abstraction such as `package`, `binary`,
   `installer`, `archive`, or `metadata`.
2. **Concrete kind** — specific artifact form such as `nuget`, `snupkg`,
   `wheel`, `sdist`, `npm-package`, `browser-zip`, `sources-zip`,
   `cli-binary`, `sbom`, or `hook-config`.

`logical-artifact-role` is modeled separately from artifact kind and is
mandatory for every artifact. Role remains a single-layer taxonomy at the
current architecture level.

## Publish Model

### Publish Node

One `publish-node` represents **one publication intent**.

Rules:

- one publish node targets exactly one target-instance snapshot;
- one publish node may consume one or more artifacts;
- one artifact may be consumed by zero or more publish nodes;
- one publish node may consume only artifacts whose `project-id` matches the
  owning `project-id` of that publish node;
- one publish node may consume artifacts from multiple variants only when the
  destination contract inherited from its referenced target-instance snapshot
  allows that aggregation shape;
- one publish node belongs to exactly one project.

This preserves multi-project dispatch as one run containing multiple
project-scoped publication intents, rather than combining multiple projects into
one publish node.

### Target Model

The publish side separates several distinct concepts:

- **target family** — business category such as `github-release`, `nuget`,
  `pypi`, `npm`, or `rubygems`;
- **target instance** — the concrete publication destination such as `nuget.org`,
  `npmjs`, `github-packages-nuget`, or `github-packages-npm`;
- **destination contract** — a reusable named protocol-shaped publication type;
- **target instance capability** — static destination-specific constraints;
- **target-side projection** — target-side naming, labeling, and presentation.

At this layer, two closely related objects must be distinguished:

- **catalog target-instance** — a repo-level shared catalog entity with stable
  identity and static capability declaration;
- **plan target-instance snapshot** — the execution-authoritative snapshot of a
  referenced catalog target-instance inside one fully materialized plan.

Project descriptors do not inline full target-instance definitions. Instead,
they declare project-owned target usage by referencing catalog target instances
and adding project-specific publication-intent details such as projection or
enablement. This keeps target declaration project-owned without turning shared
target instances into repo-wide default mappings. The shared catalog is the
planning-time authority only; projects still opt in by their own declarations.

Cardinality and identity boundaries at this layer are:

- one target family contains many target instances;
- one target instance belongs to exactly one target family;
- one destination contract may be shared by many target instances;
- one target instance declares exactly one destination contract;
- one publish node references exactly one plan target-instance snapshot;
- one publish node inherits destination-contract structure from its referenced
  target-instance snapshot rather than selecting a different contract
  independently.
- in the current signed-off scope, each target family maps to one concrete
  protocol-shaped destination contract, though many target instances may share
  that contract; this is a current-scope simplification rather than a universal
  architectural invariant.

When the planner emits a fully materialized plan, the referenced catalog
target-instance data needed for execution is snapshotted into the plan as
authoritative plan state. Execution consumes that frozen plan snapshot and does
not re-read the repo catalog out of band.

### Destination Contract

Destination contracts are modeled by publish protocol / family rather than by
hosting platform. At this layer they are reusable named types outside the
normalized graph core: catalog target instances declare one, and plan
target-instance snapshots carry the resolved contract structure inline for
execution.

Examples:

- `nuget-publish`
- `npm-publish`
- `pypi-publish`
- `rubygems-publish`
- `github-release-assets`

Rules:

- contract defines the allowed publication structure rather than the exact
  realized combination for every project;
- contract distinguishes required deliverables from optional companion
  artifacts;
- contract compatibility is expressed in terms of allowed roles and concrete
  artifact structures;
- if a contract needs aggregate role checks, it may use local role-set
  constraints, but there is no global role-family taxonomy.

### Target Instance Capability

Target instance capability is a **static declaration** of the destination rather
than a per-run mutable object.

The minimum capability set in the current scope is:

- mutability model;
- name uniqueness scope;
- version uniqueness rule;
- profile coexistence rule;
- credential posture.

These are lightly grouped for reasoning purposes into:

- identity / mutability capabilities;
- credential / auth capabilities.

Planner logic reads and applies these capabilities; it does not rewrite them
per request.

### Target-Side Projection

Projection stays in the publish layer and captures how the target sees the
publication, including:

- target-side naming transforms;
- scoped package names;
- release asset labels;
- prerelease / release presentation differences;
- other destination-side display or version projections.

## Ownership and Cardinality

The current architecture-level ownership and cardinality rules are:

- the plan graph is the union of multiple project-scoped subgraphs plus shared
  target-instance snapshots derived from the repo catalog at planning time;
- one project owns many variants;
- one variant belongs to exactly one project;
- one variant produces many artifacts;
- one artifact belongs to exactly one variant;
- one project owns many publish nodes;
- one publish node belongs to exactly one project;
- one publish node targets exactly one target-instance snapshot;
- one target-instance snapshot may be referenced by many publish nodes across
  many projects within the same plan;
- one publish node may consume many artifacts;
- one artifact may be consumed by many publish nodes.

## Control Plane vs Plan Model

These concerns stay outside the declarative plan graph and remain in the control
plane:

- control-plane run envelope;
- approvals;
- concurrency and duplicate-run cancellation;
- job orchestration;
- artifact passing and runtime wiring.

The plan expresses release intent through a normalized plan envelope and graph.
The control plane expresses execution governance through the raw run envelope
and workflow runtime state.

## Deliberately Deferred

The following are still deferred to later design layers:

- descriptor schema and file syntax;
- exact plan JSON/YAML object shape;
- exact reusable-workflow and job layout;
- executor interfaces and invocation contracts;
- exact target-instance catalog format;
- exact projection field schema.
