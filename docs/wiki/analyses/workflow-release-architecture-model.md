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

The architecture distinguishes three related request representations:

1. **Control-plane run envelope** — raw GitHub Actions runtime context such as
   `workflow_dispatch` inputs, actor, run identifiers, approval state, and
   orchestration state.
2. **Planner request** — the control-plane-normalized planner-facing input for
   the current scope: `profile`, `commit-sha`, normalized
   `requested-project-ids`, and normalized `request-flags.force`. Omitted or
   empty `requested-project-ids` means all in-scope releasable projects; an
   explicit non-empty set must resolve completely or planning fails.
3. **Plan envelope** — the planner's normalized, authoritative header for the
   computed release plan, containing the resolved request summary rather than
   raw workflow runtime state.

Only the third one belongs to the declarative plan. The control plane owns raw
input spelling and normalization into the planner request; the planner owns the
resolved request summary and all request-dependent publish decisions that appear
inside the emitted plan. The emitted plan freezes `selected-project-ids` in
unique lexicographic order, and that resolved set remains part of
`envelope.plan-id` rather than being recomputed later from raw workflow input
spelling.

The plan itself has two top-level parts:

1. **Envelope** — resolved request summary, profile, commit, selected projects,
   and plan metadata.
2. **Graph** — normalized ID-based objects and their relationships.

`Request / Scope` stays in the envelope rather than becoming an executable graph
node. The control-plane run envelope is an input to planner-request
materialization, the normalized planner request is the planner's authoritative
input contract, and the plan envelope is planner output.

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
- variant semantic identity, meaning the variant's full dimensions map rather
  than any local authoring handle
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
   `executable`, `sbom`, or `hook-config`.

For raw runnable outputs in the `binary` family, `executable` is the
single general executable concrete kind and covers both CLI executables
and desktop GUI executables such as .NET `WinExe` outputs.

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
one publish node. For GitHub Release, that also means one run may target
multiple distinct project-scoped release tags and therefore multiple distinct
GitHub Release objects when different projects share the same commit.

### Target Model

The publish side separates several distinct concepts:

- **target family** — business category such as `github-release`, `nuget`,
  `pypi`, `npm`, or `rubygems`;
- **target instance** — the concrete publication destination such as `nuget.org`,
  `github-packages-nuget`, `github-packages-npm`, or
  `github-packages-rubygems`;
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

In the current signed-off scope, family-to-contract compatibility is one-to-one:
`github-release` -> `github-release-assets`, `nuget` -> `nuget-publish`,
`pypi` -> `pypi-publish`, `npm` -> `npm-publish`, and `rubygems` ->
`rubygems-publish`. The descriptor schema makes that mapping author-time
mandatory and pairs it with closed family-specific destination shapes for the
shared catalog.

Rules:

- contract defines the allowed publication structure rather than the exact
  realized combination for every project;
- contract distinguishes required deliverables from optional companion
  artifacts;
- contract compatibility is expressed in terms of allowed roles and concrete
  artifact structures;
- if a contract needs aggregate role checks, it may use local role-set
  constraints, but there is no global role-family taxonomy.

The descriptor layer now closes the current-scope role/kind tuple patterns and
aggregate compatibility rules for these contracts so author-time static
validation is deterministic.

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

Projection stays in the publish layer but, in the current architecture,
covers only descriptor-owned target-side naming, labeling, and display data,
including:

- target-side naming transforms;
- scoped package names;
- release asset labels;
- other destination-side display projections.

In the current signed-off author-time scope, the descriptor layer narrows this
to closed family-specific shapes for GitHub Release asset labels and npm
published package-name override. GitHub Release prerelease versus release is
not projection here; it is planner-owned desired target-side state carried as
`desired-publish-state.release-state` in the plan. See
[Planner-Owned Publish Intent](#planner-owned-publish-intent) below and
[Workflow Release Plan Shape](./workflow-release-plan-shape.md).
Broader projection vocabularies remain deferred.

### Planner-Owned Publish Intent

Planner-owned publish intent is broader than destination identity alone. In the
current architecture it consists of:

- resolved artifact membership for the publish node;
- resolved external publish identity;
- any family-specific desired target-side state;
- resolved target-side projection data;
- planner-derived publish disposition and live publish mode.

In current scope, the planner resolves version identity per selected project
from that project's build-system-integrated NBGV result at the selected commit.
The release model assumes every in-scope ecosystem exposes that NBGV result
through its native build tooling rather than through a separate workflow-owned
version source. For GitHub Release, the planner then derives the external
publish identity as the project-scoped tag
`release/<project.id>/v<nbgv-version>`. This matches the repositories existing
release-tag shape, including observed tags such as `release/nbgv-python/v2.0.0`,
`release/steam-account-history-to-csv/v1.1.1`, and
`release/hexo-renderer-asciidoc/v3.1.0-beta.11.g3f78566`, and it matches the
root `version.json` allowance for `^refs/tags/release/.+/v.+$`. Different
projects on the same commit therefore remain different GitHub Release objects
when their `project.id` values differ, because current-scope GitHub Release
identity is the project-scoped release tag.

Descriptor-owned projection data remains distinct from planner-owned desired
target-side state even though both are serialized into the plan for execution.
In current scope, GitHub Release is the only family that needs explicit desired
target-side state beyond identity: `buddy` resolves to `prerelease`, and
`official` resolves to `release` for the same project-scoped `release-tag`. For
same-tag prerelease-to-release promotion, the frozen `artifact-ids` plus
`projection.asset-labels-by-artifact-id` are the authoritative final official
asset set and labels for that tag, so the promotion model is not a state-only
flip. A project-scoped version identity becomes official-frozen only when that same
project-scoped tag has already succeeded through the project's official GitHub
Release publication; there is no second freeze tag or alternate tag family.
That keeps same-tag already-satisfied replay, same-tag prerelease to release
promotion, same-tag release to prerelease demotion rejection, and buddy `FORCE`
rejection against official-frozen versions planner-owned instead of leaving
executors to infer intent from workflow profile names or remote tag
observations.

### Planner-Owned Remote Observation Seam

Some current-scope publish decisions are remote-state-dependent. Before the
planner can freeze `publish-disposition` or `publish-mode`, it may need a
destination lookup for the exact publish intent already resolved for one
publish node.

That lookup is keyed by the planner-frozen local intent snapshot for that node:

- `resolved-publish-identity`;
- the referenced target-instance snapshot;
- the intended artifact membership;
- resolved target-side projection data such as asset labels;
- any family-specific desired target-side state.

At the architecture layer, the output of that lookup is an ephemeral
**normalized remote observation** for one target family. This means the
planner's loss-limited normalization of the remote facts needed to classify the
already resolved publish intent. It is planner working state, not persisted plan
state. Raw remote responses stay outside the plan, and the persisted plan keeps
only planner conclusions such as `publish-disposition`, `publish-mode`,
`desired-publish-state`, and resolved projection or identity data.

Ownership rules for this seam are:

- the planner owns publish-destination querying, bounded retry, normalization,
  and classification for remote-state-dependent planning;
- the control plane may host planner execution and separately verify required
  Git tags, but it must not query publish destinations to decide satisfied
  reruns, replay policy, or promotion policy;
- publish executors may call destinations only to perform the already frozen
  publish request; they must not perform independent preflight classification or
  reinterpret partial remote matches.

Current-scope guardrails for this seam are:

- for immutable targets, a partial remote match against the frozen intent is not
  planner-completable; the planner must fail for human intervention;
- for GitHub Release, `skip-satisfied` requires an exact match of the release
  state plus the required asset set and asset labels;
- remote query failures use bounded retry and then fail closed.

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
- artifact passing and runtime wiring;
- release-tag mutation policy and existing-tag verification.

For `official`, current-scope control-plane approval is a GitHub protected
environment with required reviewers and self-review prevention enabled.
Administrator bypass, when the environment still allows it, remains a native
GitHub control-plane capability rather than a planner or executor concern.

The plan expresses release intent through a normalized plan envelope and graph,
including planner-derived per-publish-node publish versus satisfied-skip
dispositions, any family-specific desired target-side publish state, and, for
live publish nodes, the planner-frozen create-only, overwrite-mutable, or
replace-authoritative publish mode. `envelope.plan-id` is the authoritative
whole-release rerun identity for the normalized current-scope request summary:
the selected profile, selected commit, resolved selected-project scope, and
normalized request flags such as `force`. The control plane expresses execution
governance through the raw run envelope and workflow runtime state. For any
required project-scoped GitHub Release tag, current-scope control-plane
verification is: create the tag when absent; otherwise confirm that the existing
tag already points to the selected commit/object for that run; fail before
publication if it points elsewhere; and never retarget or move an existing
release tag automatically.

## Later-Layer Boundary Page

The architecture-layer separation remains the same, but the next design layer
now defines the concrete control-plane workflow, job, and executor seams in
[Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md).

Broader projection vocabularies beyond the closed current-scope descriptor and
plan shapes remain deferred.

Descriptor schema, file syntax, and shared target-instance catalog authoring are
now defined in [Workflow Release Descriptor Schema](./workflow-release-descriptor-schema.md).
The exact `three.release.plan/v1alpha1` object shape is now defined in
[Workflow Release Plan Shape](./workflow-release-plan-shape.md).
