# Workflow Release Workflow and Executor Boundaries

## Purpose

This page defines the middle-layer workflow/executor boundary: control-plane
workflow entry points, reusable workflow and job seams, planner-to-executor
contracts, and executor limits on top of `three.release.plan/v1alpha1`.

## Design Summary

- `buddy` and `official` remain the only top-level `workflow_dispatch` entry
  workflows.
- Both active entry workflows pre-authorize and pre-resolve the dispatch
  selector, then call the same shared orchestration contract with normalized
  inputs plus the pinned `release_target_sha`. In the active split topology,
  registry-token-minting PyPI, npmjs, and RubyGems jobs are hosted inside
  `.github/workflows/release-orchestrate.yml` and bind registry trust to
  registry-specific GitHub environments: `pypi`, `npmjs`, and `rubygems`.
- Direct reusable `source=manual` callers may still leave `target` empty or
  provide a branch, tag, ref, or 40-hex commit SHA for `release-resolve` to pin
  once. Active `buddy.yml` and `official.yml` entry workflows instead pass their
  already-pinned target SHA into the reusable orchestration layer, and later jobs
  stay pinned to that commit.
- The shared orchestration workflow materializes normalized inputs into the
  authoritative planner-facing request for current scope, including the
  profile-neutral `request-flags.force` flag used by planner-authorized buddy
  `FORCE` replay and the active official reviewed `force_update_tag=true`
  release-tag retarget path.
- The orchestration contract consumes one frozen `three.release.plan/v1alpha1`
  and fans out at two logical granularities only: one build unit per
  `variant-id` and one publish unit per `publish-node-id`. Publish units are
  partitioned by the frozen target-instance `publish-topology` before they are
  routed to the concrete publish jobs in `release-orchestrate.yml`.
- Build units emit per-variant build bundles plus machine-readable build
  receipts keyed by plan `artifact-id`.
- Publish units emit per-publish-node publish receipts keyed by plan
  `publish-node-id`.
- Approvals, concurrency, dry-run gating, tagging, permissions, runner or
  toolchain wiring, artifact transport, and final reporting remain control-plane
  responsibilities.
- The prior entry-hosted PyPI/npmjs OIDC path is superseded. Configure PyPI and
  RubyGems.org trusted publishers for `.github/workflows/release-orchestrate.yml`;
  configure active official npmjs trusted publishing for
  `.github/workflows/official.yml` because npm validates the direct caller for
  `workflow_call`.
- Reusable-orchestration OIDC publish paths keep registry validation tied to the
  registry-specific trusted identity. The PyPI and RubyGems token-minting jobs
  are reusable-workflow-bound; the npmjs token-minting job runs in the reusable
  orchestrator but remains caller-workflow-bound. These jobs declare
  `id-token: write` and hard-code their registry environments; unrelated jobs
  remain least-privilege and do not receive OIDC permission.
- Prior build-receipt indexing and admissibility lookup for immutable proof
  reuse remain control-plane responsibilities.
- Planner-owned publish-destination lookup for remote-state-dependent planning
  is keyed by the frozen resolved publish identity, target snapshot, intended
  artifacts, projection data, and any desired target-side state.
- Current-scope planner-time destination observation uses public reads where
  possible and otherwise only least-privilege read access through
  `GITHUB_TOKEN`; it must not depend on publish credentials or approval-gated
  environment secrets.
- Current-scope immutable proof reuse relies on the default GitHub Actions
  artifact retention window; replay proof is guaranteed only while the relevant
  receipt records remain unexpired in that default window.
- Executors are thin consumers of plan-defined intent and must never re-plan,
  rediscover targets, or derive alternate publish identity, overwrite policy,
  or same-tag GitHub Release replacement policy.
- Neither workflow jobs after planning nor executors may query publish
  destinations to decide `skip-satisfied`, immutable-target same-identity
  handling, or replay classification; they consume the planner's frozen result.

## Boundary to Group 1 and Group 2

- Group 1 owns descriptor and shared target-instance catalog authoring.
- Group 2 owns the authoritative frozen `three.release.plan/v1alpha1` shape.
- This page owns only the workflow, job, and executor seams that consume that
  frozen plan.

Nothing here reopens descriptor discovery, target compatibility, plan graph
shape, planner-owned resolved publish identity, or the planner-owned
remote-observation seam used to classify remote-state-dependent reruns.

## Control-Plane Workflow Topology

### Top-Level Boundaries

| Boundary                      | Kind                           | Stable granularity       | Owns                                                                                                                                                                                                                                                    |
| ----------------------------- | ------------------------------ | ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `buddy` entry workflow        | top-level workflow             | one `buddy` run          | manual dispatch inputs, profile selection, entry permissions, `authorize-entry` release identity resolution, job-level `orchestrate` concurrency wiring, and final reporting                                                                            |
| `official` entry workflow     | top-level workflow             | one `official` run       | manual dispatch inputs, profile selection, explicit triggering-actor `maintain+` authorization, protected-environment approval wiring, `authorize-entry` release identity resolution, job-level `orchestrate` concurrency wiring, and final reporting   |
| shared orchestration workflow | reusable workflow              | one selected-profile run | planning, selector derivation, reusable-safe side-effect sequencing, tag orchestration, artifact fan-out and fan-in, and active split-topology publish jobs, including token-minting PyPI, npmjs, and RubyGems jobs with registry-specific environments |
| `build-variant` unit          | reusable workflow              | one `variant-id`         | build-request materialization, ecosystem-specific build-executor selection, runner or tool wiring, and upload of one variant bundle plus build receipt                                                                                                  |
| `publish-node` unit           | topology-specific publish path | one `publish-node-id`    | publish-request materialization, topology and family-specific publish-executor routing, download of referenced build bundles, and upload of one publish receipt from the concrete orchestration-hosted publish job                                      |

The stable workflow handoff boundaries are therefore:

1. profile entry workflow -> shared orchestration workflow;
2. shared orchestration workflow -> one build unit per `variant-id`;
3. shared orchestration workflow -> one orchestration-hosted publish unit per
   `publish-node-id`, including PyPI, npmjs, and RubyGems OIDC jobs that mint
   registry tokens from `.github/workflows/release-orchestrate.yml`;
4. profile entry workflow -> final report after orchestration-hosted publish
   receipts are available, or after the run has reached a reportable failure
   state.

The former entry-workflow-bound OIDC handoff boundary is superseded by the
active split topology. Valid active `pypi/pypi`, `npm/npmjs`, and RubyGems
official publish nodes are not handed back to `.github/workflows/official.yml`
for token minting; their token-requesting jobs live in
`.github/workflows/release-orchestrate.yml` and use `environment: pypi`,
`environment: npmjs`, or `environment: rubygems` respectively.

### Required Control-Plane Job Sequence

The selected top-level entry run owns the full sequence. The shared orchestration
workflow owns planning, build fan-out, tag orchestration, and all active publish
jobs, including token-minting PyPI, npmjs, and RubyGems jobs. The top-level
entry workflow waits for the orchestration result and performs final reporting.
The active entry workflows do not declare top-level workflow-level concurrency.
After `authorize-entry` resolves the canonical release identity, the entry
workflow's `orchestrate` reusable-workflow job declares job-level concurrency
with `group: ${{ needs.authorize-entry.outputs.release_group }}` and
`cancel-in-progress: false`. That resolved group is
`release/${project_id}/v${release_version}`. Lower-layer
YAML may split these logical steps across one or more reusable jobs, but it must
preserve the topology boundary described here.

1. `authorize-entry` job
    - stays in the control plane;
    - runs before planning;
    - in current scope for `official`, resolves the triggering actor's
      repository permission through the GitHub API and fails closed unless the
      actor has at least `maintain`;
    - is separate from the later protected-environment gates on live side-effect
      jobs, which still apply to `official` publication after planning and build
      fan-out.
2. `plan` job
    - consumes the raw control-plane run envelope;
    - runs against the exact `commit-sha` resolved once from the operator-
      selected dispatch context or non-empty `target` ref/SHA at dispatch time;
    - materializes the normalized planner request;
    - hosts planner execution;
    - during that planner execution, serves the control-plane-owned prior build
      receipt lookup/index seam the planner may use for immutable proof reuse;
    - during that planner execution, the planner performs any planner-owned
      publish-destination lookup, normalization, and classification needed for
      remote-state-dependent planning, with bounded retry and fail-closed
      behavior, before freezing the plan; current scope uses public reads where
      sufficient and otherwise only least-privilege `GITHUB_TOKEN` reads for
      GitHub-hosted surfaces, never publish credentials or approval-gated
      environment secrets;
    - if any selected publish node cannot be reduced to a planner-owned remote
      observation class after that bounded retry, fails the run at planning time
      without emitting a partial plan artifact;
    - publishes the frozen `three.release.plan/v1alpha1` artifact;
    - derives the selected `variant-id` and `publish-node-id` sets for later fan-
      out;
    - partitions active publish nodes by the frozen
      `target-instance-snapshot.capabilities.publish-topology` value before
      choosing concrete publish jobs or reusable workflows;
    - rejects or diagnoses obsolete entry-workflow-bound OIDC routing instead of
      returning entry-side publish selectors;
    - keeps active external OIDC publish partitions hosted by
      `release-orchestrate.yml`, with registry-specific environments on the jobs
      that mint tokens.
3. `build` fan-out
    - runs exactly once per active `variant-id`;
    - produces one bundle and one build receipt per variant.
4. `ensure-tag` job
    - stays in the control plane;
    - runs only when the frozen execution sets contain at least one active
      GitHub Release publish node;
    - for current-scope `official`, references the active `github-release`
      environment whenever the job can perform that live GitHub Release side
      effect;
    - when one run has more than one active GitHub Release publish node, first
      computes the full active required tag set and verifies every already-
      existing active required tag before creating any missing active required
      tag;
    - creates the tag when it does not already exist and at least one active
      GitHub Release publish node references it, but only after that full
      precheck passes for the run;
    - does not verify, create, or fail on tags referenced only by
      `skip-satisfied` GitHub Release nodes; planner observation plus synthetic
      skip receipts are the evidence for those satisfied no-side-effect nodes;
    - when the tag already exists, verifies that it already points to the
      expected selected commit/object for that run rather than treating tag
      existence alone as sufficient;
    - fails the run before publication if any active required existing tag points
      elsewhere, and in that case creates no new tags for the run;
    - must not automatically retarget or move an existing release tag; the active
      `official` `force_update_tag=true` path may retarget the release tag only
      after the full required-tag precheck passes;
    - does nothing when the active publish-node set contains no GitHub Release
      publication.
5. `publish` fan-out
    - runs exactly once per selected `publish-node-id` whose plan
      `publish-disposition` is `publish`;
    - for current-scope `official`, each live publish matrix job references the
      active release/registry environment (`github-release`, `pypi`,
      `npmjs-gate` / `npmjs`, or `rubygems` as applicable) before obtaining
      publish credentials or an OIDC trusted-publishing token;
    - schedules publish selectors inside the shared reusable orchestration
      workflow;
    - schedules live PyPI, npmjs, and RubyGems external OIDC publish nodes only
      through the token-minting jobs in `release-orchestrate.yml`; PyPI and
      RubyGems.org validate workflow `release-orchestrate.yml` plus environment
      `pypi` or `rubygems`, while npmjs validates the direct caller workflow
      `official.yml` plus environment `npmjs`;
    - emits one publish receipt per publish node.
6. `report` job
    - is scheduled by the top-level entry workflow after the shared orchestration
      call has reached a terminal state;
    - aggregates plan metadata, build receipts, publish receipts from every
      topology path, synthetic skip receipts, and GitHub job conclusions into the
      final operator-facing summary.

`ensure-tag` and `report` are ordinary control-plane jobs, not executor
boundaries. If a lower layer factors report rendering into a reusable workflow,
that call must happen after the shared orchestration invocation finishes and all
orchestration-hosted publish receipts have either been produced or failed. There
is no separate approval-only job in current scope; approval is realized through
the active release/registry environments attached to the live jobs that can
perform official external side effects. Dry-run or validation-only runs,
zero-target runs, and all-`skip-satisfied` runs do not attach those environments
or schedule the protected write-capable `ensure-tag` job because they have no
live external side effects.

### Planner Failure Consequences

Planning is request-atomic. When the planner fails remote-state classification
for any selected publish node, the orchestration workflow must treat that as a
planner failure for the whole selected profile run:

- no frozen plan artifact is published;
- no build fan-out starts, even for nodes whose remote state would otherwise
  have classified cleanly;
- no approval, tag creation or verification, or publish fan-out runs;
- no executor receives a request for the failed run, so executors never
  reinterpret remote-query failures as retryable publish actions.

The diagnostics path stays control-plane-owned. The plan job must surface
structured planner diagnostics sufficient to identify the failing node and
remote-classification phase, and the workflow must expose those diagnostics in
the operator-facing run summary or final report path. When planning fails before
plan publication, there are no build receipts, publish receipts, or synthetic
skip receipts to aggregate.

### Planner Diagnostics Contract

When planning fails after request normalization has begun, the plan job must
surface one or more logical `planner-diagnostic` objects for control-plane
reporting. Pre-planner control-plane input rejections that happen after workflow input
normalization has begun, such as invalid `validation-build` combinations, must
use the same diagnostic object shape and registered `REQ_*` request-code
vocabulary. Active `official` force is valid only for the reviewed
`force_update_tag` release-tag retarget path. Current scope freezes only the
minimum machine-facing structure needed for stable cross-component handoff; it
does not define a full error taxonomy, renderer, or stack-trace model.

Each `planner-diagnostic` must contain at least these fields:

| Field                                                    | Meaning                                                                                                                                       |
| -------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `api-version: three.release.planner-diagnostic/v1alpha1` | contract version                                                                                                                              |
| `kind: planner-diagnostic`                               | diagnostic type                                                                                                                               |
| `code`                                                   | stable planner-defined diagnostic code for the failure class                                                                                  |
| `message`                                                | concise human-readable summary                                                                                                                |
| `phase`                                                  | current-scope phase discriminator: `query`, `normalization`, `classification`, or `validation`                                                |
| `scope-kind`                                             | affected scope: `request`, `project`, or `publish-node`                                                                                       |
| `project-id`                                             | required when the failing scope is one project or one publish node; omitted for whole-request failures that cannot be narrowed to one project |
| `publish-node-id`                                        | required when one publish node has already been materialized and identified; otherwise omitted                                                |
| `target-instance-snapshot-id`                            | required when one target instance has already been identified for the failing path; otherwise omitted                                         |
| `resolved-publish-identity`                              | required when the failing path already resolved an external publish identity; otherwise omitted                                               |
| `blocking`                                               | whether this diagnostic blocks plan emission; current-scope planner-failure diagnostics that abort the run use `true`                         |
| `details`                                                | extensible mapping for additional machine-readable context; may be empty, but any extra adapter-specific fields must remain nested under here |

The control plane may render or aggregate those diagnostics however it wants,
but it must preserve the frozen fields above when passing diagnostics between
planner hosting, workflow reporting, and test fixtures. Lower-layer choices
such as error-string formatting, nested exception rendering, or destination-
specific debug payload shape remain implementation-owned as long as they stay
inside `details` or outside this contract entirely.

### Planner Request Materialization

Before invoking the planner, the shared orchestration workflow must materialize
one logical planner request from normalized inputs and a pinned target SHA. The
active `buddy.yml` and `official.yml` entry workflows pass that pinned SHA after
entry authorization and resolution; direct reusable `source=manual` callers may
still pass an empty, branch, tag, ref, or 40-hex selector for `release-resolve`
to pin once before planner request materialization. The planner request has
exactly these current-scope fields:

| Field                   | Meaning                                                                                                                                                                                                                                                                                                     |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `profile`               | Selected entry workflow profile, `buddy` or `official`.                                                                                                                                                                                                                                                     |
| `commit-sha`            | The pinned release commit. Empty `target` uses the GitHub UI dispatch ref/commit; non-empty `target` is resolved once from the supplied branch, tag, ref, or 40-hex SHA. All later control-plane jobs and executors must stay pinned to this SHA while workflow code remains from the trusted dispatch ref. |
| `requested-project-ids` | The single project selected by the active `project` workflow input after entry normalization. The plural array shape is the current planner-request implementation contract, not multi-project operator dispatch.                                                                                           |
| `request-flags.force`   | Boolean force request flag. In active `official` runs, `true` authorizes the reviewed `force_update_tag` release-tag retarget path. In `buddy` runs, `true` remains the planner-owned exceptional overwrite request. Raw GitHub input names remain control-plane-owned and are not the planner contract.    |

The planner request is the authoritative planner-facing release-input model for
current scope. Actor, run id, run attempt, approval state, and concurrency groups
remain outside that object. The active workflows hard-code dry-run and
validation-build behavior to `false`; those modes are historical/future-only
operator capabilities. Whole-release rerun equivalence for planner-owned
behavior is based on the normalized single-project request, which is why the
resulting plan serializes normalized request flags in `envelope.request-flags`,
emits the resolved single project as `envelope.selected-project-ids`, and uses
the resolved project plus those flags in `envelope.plan-id`.

Direct reusable manual callers may expose optional `target` to the shared
resolve layer. When `target` is empty, the control plane uses the current
checkout/ref context and pins that commit. When `target` is non-empty, the
control plane resolves the supplied branch, tag, ref, or 40-hex commit SHA once,
before planning, and serializes the resolved commit as the planner request
`commit-sha`; in other words, it records the resolved commit as the planner request `commit-sha`. The active entry workflows perform this target
authorization and pinning before the reusable orchestration call, so the reusable
layer receives their pinned SHA rather than the raw operator selector. All
downstream workflow jobs and executors consume that pinned SHA rather than
re-resolving the raw selector.

### Active Build and Publish Set Derivation

The shared orchestration workflow must derive execution sets only from the frozen
plan:

- `active-publish-node-ids` are the selected publish nodes whose
  `publish-disposition` is `publish`.
- `active-variant-ids` are the distinct variants reachable from the full desired
  artifact set referenced by those active publish nodes: the union of each
  active node's `publish-node.artifact-ids`.
- Selected publish nodes whose `publish-disposition` is
  `skip-satisfied` do not invoke a publish executor and do not force a
  build. The control plane instead emits a synthetic skip receipt for reporting.
  For GitHub Release nodes, the planner's prior remote observation plus that
  skip receipt are the satisfied-state evidence; `ensure-tag` is not scheduled
  solely to cover skip-satisfied tags.
- `active-publish-selectors` are topology partitions of active publish nodes.
  Each selector contains the closed publish-node-id set for one frozen
  `publish-topology` value, such as `github-token` or the active
  release-orchestration external OIDC topology. Empty partitions may be omitted
  or represented explicitly by the lower-layer execution-set file, but mixed
  topology runs must never be scheduled by target-family guessing after this
  derivation.
- Each active publish selector also carries or implies its required workflow host
  and registry identity validation. In the active split topology, PyPI, npmjs,
  and RubyGems selectors are consumed by `release-orchestrate.yml` jobs that
  mint tokens under `pypi`, `npmjs`, and `rubygems` environments. They are not
  returned to the entry workflow for OIDC token minting. Superseded
  entry-workflow-bound selector handling must not be used as an active
  scheduling instruction.

This keeps rerun skip logic planner-owned rather than executor-owned and keeps
trusted-publisher scheduling a middle-layer control-plane contract.

### Dry-Run Boundary

The active `buddy.yml` and `official.yml` dispatch surfaces do not expose dry-run
or validation-build inputs, and the active orchestration path treats both as
`false`. Historical dry-run or validation-only modes stay outside the planner
request and are future-only until a successor workflow contract reintroduces
them. If a future dry-run build execution emits any `build-result` artifact, that
receipt must be validation-only and must not be reused as immutable-registry
digest proof for later immutable same-identity classification on a live plan.

## What Consumes the Frozen Plan

| Consumer                      | Required frozen input                                                                                                                                                                                         | Granularity rule                                      |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------- |
| shared orchestration workflow | full `release-plan` envelope and graph                                                                                                                                                                        | one per selected single-project profile run           |
| one build unit                | owning `envelope.projects[project-id]` snapshot, one `graph.variants[variant-id]`, and that variant's `graph.artifacts[*]`                                                                                    | one build executor invocation per `variant-id`        |
| one publish unit              | owning `envelope.projects[project-id]` snapshot, one `graph.publish-nodes[publish-node-id]`, its referenced `graph.target-instance-snapshots[*]`, and the referenced `graph.artifacts[*]` plus build receipts | one publish executor invocation per `publish-node-id` |
| top-level entry report job    | full plan plus all build receipts, orchestration-hosted publish receipts, and synthetic skip receipts                                                                                                         | one per selected single-project profile run           |

A publish unit may consume artifacts from multiple variants only when the frozen
publish node already references them and the frozen target-instance contract
allows that aggregation. Executors do not widen that set. Under the narrowed
current-scope PyPI contract, that means each PyPI publish unit is single-variant
and carries exactly one wheel and zero or one sdist.

## Job-to-Job Handoff Boundaries

### Reusable Workflow Inputs

| Boundary                      | Required input                                                                                                                               | Required output                           |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------- |
| orchestration -> build unit   | immutable plan artifact plus one `variant-id` selector                                                                                       | one variant bundle plus one build receipt |
| orchestration -> publish unit | immutable plan artifact plus one orchestration-hosted `publish-node-id` selector and the referenced build bundles or receipts                | one publish receipt                       |
| entry workflow -> report job  | immutable plan artifact, diagnostics, tag results, build receipts, skip receipts, orchestration-hosted publish receipts, and job conclusions | one final operator-facing report          |

Reusable workflow boundaries carry selectors and immutable artifacts for the
active publish units. The executor boundary inside each unit is narrower and
uses a materialized request object. For external OIDC publishing, configure the
registry trusted-publisher policy for the identity that the registry validates:
`.github/workflows/release-orchestrate.yml` plus `pypi` or `rubygems` for
PyPI/RubyGems.org, and `.github/workflows/official.yml` plus `npmjs` for active
official npmjs publishing.
Do not use the superseded entry-hosted PyPI/npmjs handoff as current guidance.

### Build Executor Contract

Each build unit must materialize one logical `build-request` object for its
executor with these exact top-level fields:

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
catalog. Any checkout or workspace materialization used by a build unit must be
pinned to `build-request.commit-sha`; a build unit must not follow a moving
branch head after planning has begun.

The `project` snapshot carried by that request includes the planner-frozen
`resolved-version` for the selected run. Whenever the built outputs carry a
project-scoped version identity in package metadata, installer metadata, or any
other contractual version-bearing field, the build executor must preserve that
exact frozen version rather than silently substituting a divergent manifest- or
tool-derived value. This is mandatory for the single current-scope
`nbgv-python` special-support path, where the planner resolved
`project.resolved-version` from the selected commit's checked-in
`pyproject.toml` `[project].version`.

In that contract, each `artifact-id` is a planner-defined fulfillment slot for
one semantic output obligation. It is not a frozen filename, path, bundle
layout, or command recipe. The planner owns the exact requested key set,
artifact tuples, ownership, `produced-from-artifact-ids`, and publish-node
consumption. The build executor owns realization only: it must fulfill each
requested `artifact-id` exactly once by mapping it to one concrete file inside
the variant bundle.

Build executors do not receive `publish-node` snapshots or other publish-layer
target context. When a
publish target family needs planner-owned remote-member keys for immutable
multi-member registry classification, those keys live on the publish-node layer
rather than in the build executor contract.

Each build unit must emit one logical `build-result` object with these exact
top-level fields:

| Field                                              | Meaning                                                          |
| -------------------------------------------------- | ---------------------------------------------------------------- |
| `api-version: three.release.build-result/v1alpha1` | contract version                                                 |
| `kind: build-result`                               | result type                                                      |
| `plan-id`, `project-id`, `variant-id`              | receipt identity                                                 |
| `artifacts[artifact-id].bundle-relative-path`      | where the produced file lives inside the uploaded variant bundle |
| `artifacts[artifact-id].sha256`                    | strong content digest for the produced file                      |
| `artifacts[artifact-id].byte-size`                 | byte size of the produced file                                   |

Every `artifact-id` declared in the corresponding `build-request.artifacts` map
must appear exactly once in the `build-result` map, and the result key set must
match the request key set exactly. Variant bundles may contain incidental
executor-owned files, but only the files mapped in the receipt by `artifact-id`
are contractual release artifacts and later publishable. The build executor owns
file production; the control plane owns artifact upload and later download.
For immutable-registry proof reuse, only live/non-dry-run `build-result` receipts
that were successfully produced by the relevant build unit for the same current
planner-frozen immutable-proof member binding
(`publish-node-id`, `artifact-id`,
`resolved-publish-identity.package-name`,
`resolved-publish-identity.version`) are admissible; matching `plan-id` alone
is not sufficient. Including the immutable resolved `{ package-name, version }`
identity in that binding keeps receipt lookup version-sensitive for future or
remaining immutable package-registry families. The planner-frozen
`projection.final-distribution-filenames-by-artifact-id` map still serves only
remote-member matching and classification; it is not the proof-binding key.
Overall workflow-run success is not required. No other prior-receipt proof
source is admissible for that seam, matching `artifact-id` alone is not
sufficient, and dry-run or other-binding receipts must not satisfy immutable
same-identity proof requirements. For any given immutable-proof member binding,
the admissible receipt set must collapse to one digest; if multiple admissible
receipts exist with differing digests, the planner must treat receipt digest
proof as unavailable and fail closed for immutable registry classification that
depends on that proof.

The executor-authored `build-result` object remains the receipt payload. Receipt
admissibility metadata for immutable proof reuse—such as whether the producing
live build unit successfully emitted the receipt, live versus dry-run or
validation-only status, and producing run identity/attempt—belongs to the
control plane's receipt transport and lookup/index seam, not to required
`build-result` fields.

Current scope intentionally does not freeze executor commands or bundle-
internal layout beyond receipted bundle-relative paths. Even where a publish
node later carries planner-frozen final distribution filenames for immutable
multi-member registry matching, those values remain publish-layer matching keys
rather than a redefinition of `artifact-id` or a general bundle-layout recipe.
Bundle-internal realization details remain executor-owned.

### Publish Executor Contract

Each publish unit must materialize one logical `publish-request` object for its
executor with these exact top-level fields:

| Field                                                 | Source                                                                                                                  |
| ----------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| `api-version: three.release.publish-request/v1alpha1` | control-plane materialization                                                                                           |
| `kind: publish-request`                               | control-plane materialization                                                                                           |
| `plan-id`, `profile`, `commit-sha`, `publish-node-id` | plan envelope plus the selected graph publish-node key                                                                  |
| `project` snapshot                                    | `envelope.projects[project-id]`                                                                                         |
| `publish-node` snapshot                               | `graph.publish-nodes[publish-node-id]`; its embedded `publish-node-id` must match the top-level key                     |
| `target-instance-snapshot`                            | referenced `graph.target-instance-snapshots[*]`                                                                         |
| `artifacts` map keyed by `artifact-id`                | frozen artifact metadata from the plan plus receipt-proved file path, digest, and size from build results               |
| Historical `github-release-asset-attestations`        | Superseded GitHub Release-only attestation bundle; active attestation gates run separately in `release-orchestrate.yml` |

The active split topology uses the generic `publish-request.json` /
`publish-result.json` contract for package-registry publish nodes. Active GitHub
Release publication is different: `release-create-github-release.yml` uploads a
`github-release-result.json` receipt file in a run-, attempt-, and digest-scoped
`release-github-release-result-v1-<run-id>-<attempt>-<digest>` artifact, and
attestation gates run separately in `release-orchestrate.yml`. For PyPI, npmjs,
and RubyGems, the registry-specific OIDC difference is the hard-coded environment
on the token-minting job (`pypi`, `npmjs`, or `rubygems`) and the matching
trusted publisher configuration: `.github/workflows/release-orchestrate.yml` for
PyPI/RubyGems.org and `.github/workflows/official.yml` for active official
npmjs publishing.

The publish unit must create that request only when the selected publish node has
`publish-disposition: publish`. For `publish-disposition: skip-satisfied`, the
control plane emits the receipt directly without invoking a publish executor.
Within that request, `publish-node.artifact-ids` is the node's full
planner-owned desired member set and, whenever `publish-disposition: publish`,
the exact set the executor may upload. The executor must not widen, narrow, or
rediscover that set by its own destination preflight classification. Build
fan-out likewise continues to follow the node's full
`publish-node.artifact-ids` set.
For current-scope npm/NuGet/PyPI/RubyGems package-registry nodes,
`publish-node.projection.final-distribution-filenames-by-artifact-id` must
cover every full `artifact-id` in the node, including singleton nodes, and its
values must be unique within that publish node. When it covers an `artifact-id`
for a live publish node, the publish executor must upload that target-side member
under exactly the frozen filename. It may satisfy the rule either by uploading a
bundle member whose basename already matches or by staging/renaming to that exact
filename before upload, but any filename mismatch must fail closed. Those
filenames remain
publish-layer remote-member matching keys only: they do not redefine
`artifact-id` and they do not replace receipt-owned bundle-relative paths from
the build side. For current-scope PyPI specifically, planners project upload filenames without
invoking Hatchling or hashing generated distributions. PyPI exact-satisfied
replay classification requires planned filename slots plus producer-bound
build-result/publish proof digests that match observed PyPI SHA-256 digests;
missing producer-bound digest evidence, missing observed digest evidence, or
digest conflict remains non-exact. Npm exact classification likewise compares
producer-bound tarball digests against registry evidence such as
`dist.integrity`; no comparable producer-bound/remote algorithm means the
existing version is non-exact rather than exact-satisfied. RubyGems
exact-satisfied replay requires the producer-bound SHA-256 for the matching
planned filename to equal the RubyGems `sha` value. Missing producer-bound
digest evidence, missing observed `sha`, or any digest mismatch must not
classify as exact-satisfied. Compatibility final-distribution digest projection
fields may appear in older plans but are not current authority.
For current-scope package-registry publish nodes, live publication also requires
one family-specific package-identity conformance check per uploaded
`artifact-id`. The publish executor must read the concrete package metadata from
the receipted file and verify that it is consistent with the node's frozen
`resolved-publish-identity` under that target family's canonical equivalence
rules before any live upload starts for that node. This is a conformance check,
not fresh identity derivation: the serialized plan identity remains
authoritative, and the executor must not substitute alternate package identity
from the file, manifest, or destination. Any mismatch must fail closed before
the node performs live upload. For current-scope `nbgv-python`, that check also
enforces that built Python metadata matches the same planner-frozen project
version that was derived from the selected commit's checked-in
`pyproject.toml` and used for release-tag and package-identity planning.
The current-scope conformance contract is:

| Family     | Artifact metadata source                                                                                  | Required name equivalence                                                                                                              | Required version equivalence                                                                           |
| ---------- | --------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `nuget`    | The embedded `.nuspec` metadata inside each `.nupkg` or `.snupkg` member.                                 | The metadata `id` must equal `publish-node.resolved-publish-identity.package-name` under NuGet's case-insensitive package-ID identity. | The metadata `version` must equal the frozen version under NuGet normalized package-version identity.  |
| `pypi`     | Wheel `METADATA` and sdist `PKG-INFO` or equivalent core metadata produced by the selected build backend. | The metadata `Name` must equal the frozen package name after PyPI / PEP 503 normalization.                                             | The metadata `Version` must equal the frozen version under normalized Python package version identity. |
| `npm`      | The packed npm package's `package/package.json`.                                                          | The metadata `name` must exactly equal the frozen npm package name after npm package-name validation.                                  | The metadata `version` and frozen version must resolve to the same canonical `node-semver` version.    |
| `rubygems` | The built gem's RubyGems specification metadata.                                                          | The gem metadata `name` must exactly equal the frozen gem name after RubyGems name validation.                                         | The gem metadata `version` must equal the frozen version under RubyGems `Gem::Version` equality.       |

If the publish executor cannot read the required metadata, if the metadata is
missing, or if the family-specific equivalence check cannot be completed
unambiguously, the node must fail closed before live upload.
The PyPI row is a conformance contract for descriptor validation, planning,
build receipts, GitHub Release evidence, and live publication through the
topology path frozen in the referenced target-instance snapshot.
When the publish node also contains `publish-mode: overwrite-mutable` or
`publish-mode: replace-authoritative`, the executor must honor that frozen mode;
it must not infer overwrite or replacement behavior by re-reading raw dispatch
inputs or by probing the destination for alternate policy.
`overwrite-mutable` remains limited to planner-authorized buddy `FORCE` replay.
`replace-authoritative` is the active same-tag official prerelease-to-release
promotion policy. Active GitHub Release publication may create a missing release
and upload planned assets, may accept an exact existing release, or, when the
planner serializes `publish-mode: replace-authoritative`, may converge the
existing same-tag prerelease to the frozen official asset set by deleting stale
or changed assets and uploading missing or changed planned assets before
clearing prerelease. Any `replace-authoritative` request that reaches an already
release-state remote object, a draft, the wrong channel, or a prerelease desired
state must fail closed. For GitHub Release, the active workflow must upload or
verify each receipted file under the frozen asset name for its `artifact-id`; it
must not use the bundle-relative path or produced basename as a fresh target-side
name. It must not reinterpret that path as a mere state flip or as an additive
merge with the prior buddy asset set. When a
`mutable-prerelease` destination already contains the same frozen
`resolved-publish-identity`, the executor must still follow the serialized mode
exactly: only planner-authorized buddy `FORCE` replay arrives as
`overwrite-mutable`; same-tag GitHub Release prerelease-to-release promotion
arrives as `replace-authoritative`; and every other same-identity mutable replay
case fails during planning and therefore must not reach the executor. For
`replace-authoritative`, the final release-state convergence mutation, including
clearing `prerelease`, must occur after asset replacement completes. When the
frozen publish node also carries GitHub Release
`desired-publish-state.release-state`, the executor must honor that state
exactly rather than inferring prerelease versus release from the workflow
profile or from tag existence alone. By contrast, when the same release tag
already exists and already matches the frozen desired release state, asset
names, and asset labels, the planner must serialize `publish-disposition:
skip-satisfied`, so the executor never receives a live publish request for that
rerun case. The inverse same-tag release to prerelease transition is
planner-invalid because official release state is frozen; the planner must
reject that request before job materialization, so a publish executor never
receives a live GitHub Release demotion request. The executor may call the
destination only to perform that already selected publish action; it must not do
its own preflight destination query to re-decide satisfaction, promotion,
overwrite policy, or immutable-target same-identity handling.

Each package-registry publish unit, regardless of workflow host topology, must
emit one logical `publish-result` object with these exact top-level fields.
Active GitHub Release publication is excluded from this generic result shape and
instead emits the `github-release-result.json` file in a run-, attempt-, and
digest-scoped `release-github-release-result-v1-<run-id>-<attempt>-<digest>`
artifact:

| Field                                                | Meaning                                                                                       |
| ---------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `api-version: three.release.publish-result/v1alpha1` | contract version                                                                              |
| `kind: publish-result`                               | result type                                                                                   |
| `plan-id`, `project-id`, `publish-node-id`           | receipt identity                                                                              |
| `target-instance-snapshot-id`                        | the destination slot that was acted on                                                        |
| `resolved-publish-identity`                          | copied from the publish node for traceability                                                 |
| `outcome: published`                                 | successful live publication                                                                   |
| `evidence`                                           | small family-specific receipt data such as returned URL or registry identifier when available |

Skip receipts for planner-owned satisfied reruns stay control-plane-authored
and do not pass through the publish executor contract.

### Synthetic Skip Receipt Contract

When a selected publish node has `publish-disposition: skip-satisfied`, the
control plane must emit a distinct logical `skip-result` object rather than
reusing `publish-result`. This keeps planner-owned satisfied-rerun reporting
separate from executor-authored live publication receipts.

Each `skip-result` must contain these exact top-level fields:

| Field                                             | Meaning                                                                                              |
| ------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| `api-version: three.release.skip-result/v1alpha1` | contract version                                                                                     |
| `kind: skip-result`                               | synthetic skip receipt type                                                                          |
| `plan-id`, `project-id`, `publish-node-id`        | receipt identity                                                                                     |
| `target-instance-snapshot-id`                     | the destination slot whose live publish work was skipped                                             |
| `resolved-publish-identity`                       | copied from the publish node for traceability                                                        |
| `outcome: skip-satisfied`                         | planner-owned satisfied-rerun outcome                                                                |
| `reason-source: planner`                          | records that the skip decision came from planner-time classification rather than executor-side logic |
| `evidence`                                        | optional small family-specific or planner-summary data that helps explain the satisfied state        |

`skip-result` is control-plane-authored reporting data only. It does not imply
that any publish executor ran, and it must not be treated as evidence of a live
publish attempt in receipt lookup, retry routing, or executor success metrics.

### Closed Executor Handoff JSON Shapes

The executor handoff objects above use closed `v1alpha1` shapes. The snippets
below are shape notation: angle-bracket values stand for concrete JSON values,
and copied plan snapshots must keep the complete object shape defined by
`release-plan.json`.

`build-request.json`:

```text
{
    "api-version": "three.release.build-request/v1alpha1",
    "kind": "build-request",
    "plan-id": "<plan-id>",
    "profile": "buddy|official",
    "commit-sha": "<40-hex-sha>",
    "project": <envelope.projects[project-id] object>,
    "variant": <graph.variants[variant-id] object>,
    "artifacts": {
        "<artifact-id>": <graph.artifacts[artifact-id] object>
    }
}
```

`build-request.artifacts` must contain exactly the artifact IDs listed by
`variant.artifact-ids`. The `project`, `variant`, and artifact snapshot values
are copied from the frozen plan without adding executor-local fields.

`build-result.json`:

```text
{
    "api-version": "three.release.build-result/v1alpha1",
    "kind": "build-result",
    "plan-id": "<plan-id>",
    "project-id": "<project-id>",
    "variant-id": "<variant-id>",
    "artifacts": {
        "<artifact-id>": {
            "bundle-relative-path": "<normalized-relative-path>",
            "sha256": "<lowercase-64-hex-digest>",
            "byte-size": 123
        }
    }
}
```

Each `build-result.artifacts[artifact-id]` entry is closed to the three fields
shown above. `bundle-relative-path` is relative to the uploaded variant bundle,
uses normalized forward slashes, and must not be absolute or contain `.` or `..`
path segments. `byte-size` is a non-negative integer.

`publish-request.json`:

```text
{
    "api-version": "three.release.publish-request/v1alpha1",
    "kind": "publish-request",
    "plan-id": "<plan-id>",
    "profile": "buddy|official",
    "commit-sha": "<40-hex-sha>",
    "publish-node-id": "<publish-node-id>",
    "project": <envelope.projects[project-id] object>,
    "publish-node": <graph.publish-nodes[publish-node-id] object>,
    "target-instance-snapshot": <graph.target-instance-snapshots[target-instance-snapshot-id] object>,
    "artifacts": {
        "<artifact-id>": {
            "artifact": <graph.artifacts[artifact-id] object>,
            "input-path": "<publish-workspace-relative-path>",
            "bundle-relative-path": "<build-result artifact bundle-relative-path>",
            "sha256": "<build-result artifact sha256>",
            "byte-size": 123
        }
    }
}
```

`publish-request.artifacts` must contain exactly the artifact IDs listed by
`publish-node.artifact-ids`. `artifact` is the frozen plan artifact snapshot.
`input-path` is the publish-unit workspace-relative path materialized by the
control plane after downloading the owning variant bundle; before invoking the
publish executor, the control plane must verify that the file at `input-path`
matches the carried `sha256` and `byte-size`. The receipt-derived
`bundle-relative-path`, `sha256`, and `byte-size` fields use the same validation
rules as `build-result`.
For active GitHub Release publication, attestation gates are separate
`release-orchestrate.yml` jobs rather than checks performed by the GitHub Release
upload executor. The GitHub Release path publishes through
`release-create-github-release.yml`, which verifies staged asset names, sizes,
and SHA-256 digests against any existing release assets before mutating assets
and emits the `github-release-result.json` receipt file in a run-, attempt-, and
digest-scoped `release-github-release-result-v1-<run-id>-<attempt>-<digest>`
artifact. Generic `publish-result.json` receipts remain the package-registry
executor contract, not the active GitHub Release receipt shape.

`publish-result.json`:

```text
{
    "api-version": "three.release.publish-result/v1alpha1",
    "kind": "publish-result",
    "plan-id": "<plan-id>",
    "project-id": "<project-id>",
    "publish-node-id": "<publish-node-id>",
    "target-instance-snapshot-id": "<target-instance-snapshot-id>",
    "resolved-publish-identity": <publish-node.resolved-publish-identity object>,
    "outcome": "published",
    "evidence": {}
}
```

`skip-result.json`:

```text
{
    "api-version": "three.release.skip-result/v1alpha1",
    "kind": "skip-result",
    "plan-id": "<plan-id>",
    "project-id": "<project-id>",
    "publish-node-id": "<publish-node-id>",
    "target-instance-snapshot-id": "<target-instance-snapshot-id>",
    "resolved-publish-identity": <publish-node.resolved-publish-identity object>,
    "outcome": "skip-satisfied",
    "reason-source": "planner",
    "evidence": {}
}
```

For both publish and skip results, `evidence` is required and may be `{}`. It is
the only extensibility object in these result shapes. Evidence must not contain
secrets, credentials, OIDC tokens, or unpublished request payloads; it must not
be required to decide replay, skip, overwrite, or promotion semantics. Current
scope allows only small JSON-serializable registry facts, such as target URLs,
registry object IDs, asset IDs keyed by `artifact-id`, or package/version page
URLs. Contract tests should assert the closed root shape and the presence of a
JSON object at `evidence`, while family-specific evidence keys remain optional
unless a later acceptance fixture explicitly registers them.

## Control-Plane Ownership Rules

The following concerns are explicitly control-plane-owned:

- **approvals**: only the control plane decides whether and when approval is
  required; in current scope, `official` approval is wired through active
  release/registry environments such as `github-release`, `pypi`, `npmjs-gate`,
  `npmjs`, and `rubygems`, attached directly to the live jobs that can perform
  external side effects. Administrator bypass
  stays a native environment capability when enabled, and the environment is not
  attached for no-side-effect runs such as dry-run or validation-only runs,
  zero-target runs, and all-`skip-satisfied` runs;
- **triggering-actor authorization**: only the control plane decides whether the
  triggering actor is allowed to start the selected profile; in current scope,
  `official` must fail before planning unless the triggering actor has at least
  repository `maintain`, and this check stays distinct from later approval;
- **concurrency**: each top-level entry workflow leaves top-level
  workflow-level concurrency unset. Its `authorize-entry` job first resolves
  `release_group` as `release/${project_id}/v${release_version}`, then the
  job-level `orchestrate` call uses that tag-scoped group with
  `cancel-in-progress: false`; the reusable GitHub Release mutation job also
  declares tag-scoped job-level concurrency. Child matrix rows, package-registry
  publish jobs, and report jobs do not reuse the entry `release_group`;
- **selected-commit pinning**: only the control plane chooses the authoritative
  `commit-sha` for release content. Empty `target` uses the GitHub UI dispatch
  ref/commit; non-empty `target` resolves a branch, tag, ref, or 40-hex SHA
  exactly once. Every later planner, build, publish, and tag job must stay
  pinned to that same release commit while workflow code remains from the
  trusted dispatch ref;
- **cancellation**: manual operator cancellation and ordinary platform
  cancellation use native GitHub workflow cancellation semantics and ordinary
  cancelled status; current scope does not adopt repo-defined in-progress
  duplicate-run auto-cancellation. Same-release-group `orchestrate` jobs across
  the active `buddy` and `official` entry workflows are serialized with
  `cancel-in-progress: false`; GitHub may still cancel and replace an older
  pending run when a newer pending run from either entry workflow enters the
  same resolved release group;
- **tagging**: the planner resolves the final project-scoped `release-tag`,
  but the control plane creates or verifies each distinct selected Git tag once
  per run only when the selected plan contains at least one GitHub Release
  publish node, and it does so before any GitHub Release publication; if a
  required tag already exists, verification must confirm that it already points
  to the expected selected commit/object for that run; mismatches are hard
  failures except for the reviewed active `official` `force_update_tag=true`
  path, which may retarget the release tag only after the full required-tag
  precheck passes;
- **runtime wiring**: runner selection, tool installation, permissions,
  credential injection, publish-topology partitioning, topology-specific publish
  selector handoff, and environment selection stay in workflow jobs and wrappers
  rather than inside executors; current-scope planner-time remote observation
  uses public reads where sufficient and otherwise only least-privilege
  `GITHUB_TOKEN` reads for GitHub-hosted surfaces, never publish credentials or
  approval-gated environment secrets;
- **artifact transport**: upload, download, naming, and retention of build
  bundles and receipts stay in the control plane; current-scope prior-build
  receipt lookup relies on the platform's default GitHub Actions artifact
  retention window, so immutable proof reuse is guaranteed only while the
  relevant receipt records remain unexpired in that default window;
- **prior build-receipt lookup/index**: only the control plane may attach
  workflow-run provenance to `build-result` receipts and look up admissible
  prior build receipt records for immutable proof reuse;
- **orchestration**: matrix fan-out, dependency ordering, rerun wiring, and
  failure aggregation stay in the control plane;
- **planner-request normalization**: only the control plane maps raw dispatch
  inputs into the planner-facing request contract;
- **planner hosting, not planner-side remote classification**: the workflow may
  host planner execution, but publish-destination querying, normalized remote
  observation, and rerun classification remain planner-owned; only required Git
  tag verification stays as separate control-plane logic;
- **reporting**: only the control plane assembles final summaries across multiple
  build and publish units, and it also surfaces planner-time remote-
  classification failures when planning ends before any executor work starts.

## Current-Scope Executor Routing

Current-scope routing is grounded in the actual monorepo and the accepted Group 1
schema:

- build units select an ecosystem-specific build executor from
  `project.ecosystem`, currently .NET, Python, Node.js, or Ruby;
- publish units first select a topology-specific publish path from
  `target-instance-snapshot.capabilities.publish-topology`, including the
  registry-specific environment for active release-orchestration OIDC jobs, then
  select a
  target-family-specific publish executor from
  `target-instance-snapshot.family`, currently `github-release`, `nuget`,
  `pypi`, `npm`, or `rubygems`.

This routing happens after planning and consumes only frozen plan data plus the
checked-out source tree.

## What Explicitly Stays Out of Executors

Executors must not own any of the following:

- descriptor discovery, schema validation, or shared-catalog loading;
- project selection, target selection, target compatibility checks, or publish-
  node construction;
- project-scoped NBGV version derivation, `release-tag` derivation, GitHub
  Release desired-state derivation, or final package-name derivation;
- approval handling, concurrency handling, dry-run policy, or cancellation
  policy;
- Git tag creation, multi-job artifact transport, or final run reporting;
- publish replay-satisfaction decisions, `FORCE` eligibility decisions, or
  overwrite or replacement policy beyond honoring the already frozen plan
  `publish-disposition` and `publish-mode`;
- publish-destination querying, normalized remote observation, or remote-state
  classification to decide whether a request should be skipped, promoted,
  rejected, or treated as an immutable same-identity case;
- combining multiple publish nodes into one alternate publish transaction;
- inventing artifacts, variants, or destination-side projections that are not in
  the request they were given.

Executors are allowed to:

- read the checked-out repository files named by the build or publish request,
  as long as that checkout remains pinned to the request's `commit-sha`;
- perform the one build or publish action represented by that request;
- return structured receipts for the control plane to aggregate.

## Outcome

With these boundaries, the cross-layer seam is now explicit:

descriptor -> frozen plan -> per-variant build request -> topology-partitioned
per-publish-node publish selectors -> orchestration-hosted
`publish-request` / `publish-result` ->
aggregated control-plane report.

Current scope now also freezes entry authorization, selected-commit pinning,
planner-time remote-observation auth, and default-window immutable-proof reuse.
The remaining work is implementation of the frozen boundaries, not more design
about where planning stops and execution starts.

## Related Pages

- [Workflow Release Design Direction](./workflow-release-design-direction.md)
- [Workflow Release Architecture Model](./workflow-release-architecture-model.md)
- [Workflow Release Descriptor Schema](./workflow-release-descriptor-schema.md)
- [Workflow Release Plan Shape](./workflow-release-plan-shape.md)
- [Workflow Release OIDC Publish Topology Research](./workflow-release-oidc-publish-topology.md)
- [Workflow Release Low-Level Design](./workflow-release-low-level-design.md)
