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

The plan has two top-level parts:

1. **Envelope** — request context, profile, commit, selected projects, and plan
   metadata.
2. **Graph** — normalized ID-based objects and their relationships.

`Request / Scope` stays in the envelope rather than becoming an executable graph
node.

## Normalized Graph Core

The graph uses a normalized model for the core reusable objects:

- `variant`
- `artifact`
- `publish-node`
- `target-instance`

These are the only first-class ID-addressable architecture objects at this
layer. Smaller structures remain inline value objects, including:

- target-side projection;
- capability subfields;
- destination-contract internal constraints;
- display metadata and other presentation-only fields.

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
- `artifact-kind`
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

- one publish node targets exactly one target instance;
- one publish node may consume one or more artifacts;
- one artifact may be consumed by zero or more publish nodes;
- one publish node may consume artifacts from multiple variants;
- one publish node belongs to exactly one project.

This preserves multi-project dispatch as one run containing multiple
project-scoped publication intents, rather than combining multiple projects into
one publish node.

### Target Model

The publish side separates several distinct concepts:

- **target family** — business category such as `github-release`, `nuget`,
  `pypi`, `npm`, `rubygems`, or `github-packages`;
- **target instance** — the concrete publication destination such as `nuget.org`
  or `npmjs`;
- **destination contract** — the protocol-shaped publication structure;
- **target instance capability** — static destination-specific constraints;
- **target-side projection** — target-side naming, labeling, and presentation.

### Destination Contract

Destination contracts are modeled by publish protocol / family rather than by
hosting platform.

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

- one project owns many variants;
- one variant belongs to exactly one project;
- one variant produces many artifacts;
- one artifact belongs to exactly one variant;
- one project owns many publish nodes;
- one publish node belongs to exactly one project;
- one publish node targets exactly one target instance;
- one target instance may be referenced by many publish nodes across many
  projects;
- one publish node may consume many artifacts;
- one artifact may be consumed by many publish nodes.

## Control Plane vs Plan Model

These concerns stay outside the declarative plan graph and remain in the control
plane:

- workflow dispatch envelope;
- approvals;
- concurrency and duplicate-run cancellation;
- job orchestration;
- artifact passing and runtime wiring.

The plan expresses release intent. The control plane expresses execution
governance.

## Deliberately Deferred

The following are still deferred to later design layers:

- descriptor schema and file syntax;
- exact plan JSON/YAML object shape;
- exact reusable-workflow and job layout;
- executor interfaces and invocation contracts;
- exact target-instance catalog format;
- exact projection field schema.
