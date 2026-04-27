# Workflow Release Workflow and Executor Boundaries

## Purpose

This page defines the Group 3 design layer: control-plane workflow entry points,
reusable workflow and job seams, planner-to-executor contracts, and executor
limits on top of `three.release.plan/v1alpha1`.

## Design Summary

- `buddy` and `official` remain the only top-level `workflow_dispatch` entry
  workflows.
- Both entry workflows call the same shared orchestration contract with the
  selected profile and the raw dispatch envelope, but the topology partition
  decides whether each OIDC publish job is reusable-hosted with caller/top-level
  identity validation, reusable-hosted with reusable-workflow identity
  validation, or physically hosted by the entry workflow.
- In current scope, manual dispatch selects a trusted branch or tag ref; the
  control plane resolves it once to `commit-sha` at run start and later jobs stay
  pinned to that exact commit.
- The shared orchestration workflow normalizes that raw envelope into the
  authoritative planner-facing request for current scope, including
  `request-flags.force` for `buddy FORCE`.
- The orchestration contract consumes one frozen `three.release.plan/v1alpha1`
  and fans out at two logical granularities only: one build unit per
  `variant-id` and one publish unit per `publish-node-id`. Publish units are
  partitioned by the frozen target-instance `publish-topology` before they are
  routed to reusable-hosted publish jobs or back to the top-level entry workflow
  for entry-workflow-bound OIDC jobs.
- Build units emit per-variant build bundles plus machine-readable build
  receipts keyed by plan `artifact-id`.
- Publish units emit per-publish-node publish receipts keyed by plan
  `publish-node-id`.
- Approvals, concurrency, dry-run gating, tagging, permissions, runner or
  toolchain wiring, artifact transport, and final reporting remain control-plane
  responsibilities.
- Caller-workflow-bound OIDC publish paths such as npmjs keep registry
  validation tied to the caller/top-level workflow identity. In the current
  nested chain, `release-official.yml` invokes `release-orchestrate.yml`, which
  invokes `release-publish-node.yml`; grant `id-token: write` only to every
  active caller job in that chain that must pass OIDC capability onward and to
  the child reusable publish job that mints the token. Unrelated jobs remain
  least-privilege and do not receive OIDC permission.
- Reusable-workflow-bound OIDC publish paths such as RubyGems.org keep registry
  validation tied to the reusable publish workflow identity. In the current
  nested chain, the `release-official.yml` caller job invokes
  `release-orchestrate.yml`, whose publish caller job invokes
  `release-publish-node.yml`; because reusable workflows cannot elevate
  permissions, every active caller job in that chain that must pass OIDC
  capability onward plus the child reusable publish job that mints the token
  must declare `id-token: write`. Unrelated jobs remain least-privilege and do
  not receive OIDC permission.
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

| Boundary                      | Kind                           | Stable granularity       | Owns                                                                                                                                                                                                                                                                                                                                             |
| ----------------------------- | ------------------------------ | ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `buddy` entry workflow        | top-level workflow             | one `buddy` run          | manual dispatch inputs, profile selection, entry permissions, top-level concurrency wiring, caller/top-level identity for caller-workflow-bound OIDC publishes, physical hosting for entry-workflow-bound OIDC publishes, and final reporting                                                                                                    |
| `official` entry workflow     | top-level workflow             | one `official` run       | manual dispatch inputs, profile selection, explicit triggering-actor `maintain+` authorization, protected-environment approval wiring, top-level concurrency wiring, caller/top-level identity for caller-workflow-bound OIDC publishes, physical hosting for entry-workflow-bound OIDC publishes such as live PyPI publish, and final reporting |
| shared orchestration workflow | reusable workflow              | one selected-profile run | planning, selector derivation, reusable-safe side-effect sequencing, tag orchestration, artifact fan-out and fan-in, reusable-hosted publish fan-out including caller-workflow-bound selectors, and entry-workflow-bound publish selector handoff                                                                                                |
| `build-variant` unit          | reusable workflow              | one `variant-id`         | build-request materialization, ecosystem-specific build-executor selection, runner or tool wiring, and upload of one variant bundle plus build receipt                                                                                                                                                                                           |
| `publish-node` unit           | topology-specific publish path | one `publish-node-id`    | publish-request materialization, topology and family-specific publish-executor routing, download of referenced build bundles, and upload of one publish receipt, whether the concrete job is reusable-hosted or entry-workflow-hosted                                                                                                            |

The stable workflow handoff boundaries are therefore:

1. profile entry workflow -> shared orchestration workflow;
2. shared orchestration workflow -> one build unit per `variant-id`;
3. shared orchestration workflow -> one reusable-hosted publish unit per
   `publish-node-id`, including caller-workflow-bound selectors whose registry
   validates the caller/top-level workflow identity;
4. shared orchestration workflow -> profile entry workflow handoff for
   entry-workflow-bound publish selectors, followed by one entry-hosted publish
   unit per `publish-node-id`;
5. profile entry workflow -> final report after both reusable-hosted and
   entry-hosted publish receipts are available, or after the run has reached a
   reportable failure state.

The fourth boundary is not a third operator-facing entry. It is an entry-run
continuation inside the same `buddy` or `official` run, required only when the
frozen publish topology is `external-oidc-entry-workflow` and the job that
requests the OIDC token must be hosted by the top-level entry workflow file. For
a valid active `pypi/pypi` `official` publish node, the live publish job must
therefore be hosted by `.github/workflows/release-official.yml`, not by the
reusable `release-orchestrate.yml` workflow and not by the reusable
`publish-node` unit. Caller-workflow-bound selectors are different: they may run
inside reusable publish workflow jobs while the registry validates the
caller/top-level workflow identity.

### Required Control-Plane Job Sequence

The selected top-level entry run owns the full sequence. The shared orchestration
workflow owns the reusable-safe portion, including caller-workflow-bound publish
selectors that can run in reusable workflow jobs, and returns only
entry-workflow-bound publish selectors plus reusable-hosted receipt locations to
its caller. The top-level entry workflow then schedules those returned selectors
in entry-hosted jobs and performs final receipt aggregation after those jobs
complete. Lower-layer YAML may split these logical steps across one or more
reusable jobs, but it must preserve the topology boundary described here.

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
      selected branch/tag ref at dispatch time;
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
    - marks entry-workflow-bound OIDC publish partitions as entry-side selectors
      to be materialized and scheduled by the top-level entry workflow rather
      than by the called reusable orchestration workflow;
    - keeps caller-workflow-bound OIDC publish partitions reusable-hosted when
      the registry validates the caller/top-level workflow identity even though
      the publish command runs inside the called workflow.
3. `build` fan-out
    - runs exactly once per active `variant-id`;
    - produces one bundle and one build receipt per variant.
4. `ensure-tag` job
    - stays in the control plane;
    - verifies each distinct project-scoped release tag exactly once per run when
      any selected publish node resolves to a GitHub Release publication,
      including nodes already classified as `skip-satisfied`;
    - for current-scope `official`, references the protected GitHub `release`
      environment whenever the job can perform that live side effect;
    - when one run requires more than one distinct project-scoped release tag,
      first computes the full required tag set and verifies every already-
      existing required tag before creating any missing tag;
    - creates the tag when it does not already exist and at least one active
      GitHub Release publish node references it, but only after that full
      precheck passes for the run;
    - fails the run before publication when a required tag is missing but is
      needed only by `skip-satisfied` GitHub Release nodes, because those nodes
      are verification-only in current scope;
    - when the tag already exists, verifies that it already points to the
      expected selected commit/object for that run rather than treating tag
      existence alone as sufficient;
    - fails the run before publication if any required existing tag points
      elsewhere, and in that case creates no new tags for the run;
    - must not retarget or move an existing release tag in current scope;
    - does nothing when the selected publish-node set contains no GitHub Release
      publication.
5. `publish` fan-out
    - runs exactly once per selected `publish-node-id` whose plan
      `publish-disposition` is `publish`;
    - for current-scope `official`, each live publish matrix job references the
      protected GitHub `release` environment before obtaining publish
      credentials or an OIDC trusted-publishing token;
    - schedules reusable-hosted publish selectors inside the shared reusable
      orchestration workflow, including caller-workflow-bound selectors whose
      registry trust policy validates the caller/top-level workflow identity;
    - returns entry-workflow-bound OIDC publish selectors to the top-level entry
      workflow so those publish jobs are physically hosted by the workflow
      identity configured in the external registry;
    - must not schedule a live PyPI publish node through either the reusable
      `publish-node` unit or the reusable `release-orchestrate.yml` workflow in
      first delivery, because PyPI Trusted Publishing is configured against the
      top-level entry workflow identity;
    - emits one publish receipt per publish node.
6. `report` job
    - is scheduled by the top-level entry workflow after the shared orchestration
      call and any entry-hosted publish jobs have reached terminal states;
    - aggregates plan metadata, build receipts, publish receipts from every
      topology path, synthetic skip receipts, and GitHub job conclusions into the
      final operator-facing summary.

`ensure-tag` and `report` are ordinary control-plane jobs, not executor
boundaries. If a lower layer factors report rendering into a reusable workflow,
that call must happen after entry-hosted publishes complete; the first shared
orchestration invocation must not try to aggregate receipts that can only be
created later by entry-hosted jobs. There is no separate approval-only job in
current scope; approval is
realized through the protected `release` environment attached to the live jobs
that can perform official external side effects. Dry-run or validation-only
runs, zero-target runs, and all-`skip-satisfied` runs do not attach this
environment because they have no live external side effects.

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
reporting. Pre-planner control-plane input rejections that happen after workflow
input normalization has begun, such as invalid `official` `force` or invalid
`validation-build` combinations, must use the same diagnostic object shape and
registered `REQ_*` request-code vocabulary. Current scope freezes only the
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

Before invoking the planner, the shared orchestration workflow must normalize
the raw dispatch envelope into one logical planner request with exactly these
current-scope fields:

| Field                   | Meaning                                                                                                                                                                                                                                             |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `profile`               | Selected entry workflow profile, `buddy` or `official`.                                                                                                                                                                                             |
| `commit-sha`            | The exact commit being released, resolved once from the operator-selected branch/tag ref at dispatch time. All later control-plane jobs and executors must stay pinned to this SHA.                                                                 |
| `requested-project-ids` | User-selected project scope, normalized to unique lexicographic order before planning. Omitted or empty means all in-scope releasable projects. If explicitly non-empty, every id must resolve to an in-scope releasable project or planning fails. |
| `request-flags.force`   | Boolean overwrite request flag. In `v1alpha1`, `true` is valid only for `buddy`; `profile: official` with `true` is invalid. Raw GitHub input names remain control-plane-owned and are not the planner contract.                                    |

The planner request is the authoritative planner-facing release-input model for
current scope. Actor, run id, run attempt, approval state, concurrency groups,
and dry-run remain outside that object. Dry-run therefore does not participate
in whole-release rerun identity. The planner, not the control plane or
executors, resolves `selected-project-ids` from that request, normalizes the
resolved set to unique lexicographic order, and serializes that resolved set in
the plan. Whole-release rerun equivalence for planner-owned behavior is based on
that normalized request after project-scope resolution, which is why the
resulting plan serializes normalized request flags in
`envelope.request-flags` and uses resolved project scope plus those flags in
`envelope.plan-id`.

Current scope does not add an arbitrary-SHA manual selector to
`workflow_dispatch`. Manual entry selects a trusted branch or tag ref in the
GitHub UI, and the control plane resolves `commit-sha` from that chosen ref once
at run start.

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
- `active-publish-selectors` are topology partitions of active publish nodes.
  Each selector contains the closed publish-node-id set for one frozen
  `publish-topology` value, such as `github-token`,
  `external-oidc-entry-workflow`, `external-oidc-caller-workflow`, or
  `external-oidc-reusable-workflow`. Empty partitions may be omitted or
  represented explicitly by the lower-layer execution-set file, but mixed
  topology runs must never be scheduled by target-family guessing after this
  derivation.
- Each active publish selector also carries or implies its required workflow host
  and registry identity validation: reusable-orchestration-hosted for
  `github-token`, `external-oidc-caller-workflow`, and
  `external-oidc-reusable-workflow` selectors, and top-level-entry-hosted for
  `external-oidc-entry-workflow` selectors. Caller-workflow-bound selectors may
  run inside the called reusable publish workflow while the registry validates the
  caller/top-level workflow identity; they are not returned to the entry workflow
  merely because the trusted identity is the caller. Entry-workflow-bound
  selectors are consumed outside the called reusable orchestration workflow even
  though their `publish-request.json` materialization, artifact inputs, and
  `publish-result.json` receipt shape remain identical. A valid first-delivery
  `pypi/pypi` official publish node is a member of the
  `external-oidc-entry-workflow` selector partition and is scheduled by that
  topology selector, not by target-family special casing.

This keeps rerun skip logic planner-owned rather than executor-owned and keeps
trusted-publisher scheduling a middle-layer control-plane contract.

### Dry-Run Boundary

Dry-run or validation-only mode stays in the raw control-plane run envelope, not
in the plan. In current scope it must suppress side effects:

- it may run planning and any non-publishing validation steps;
- it must not create tags;
- it must not invoke live publish executors.

Because dry-run stays outside the planner request, toggling it does not change
whole-release rerun identity. Whether a dry run also performs build execution is
an implementation choice, but
that choice must not change the stable workflow or executor contracts defined
here. If dry-run build execution emits any `build-result` artifact, that receipt
is validation-only and must not be reused as immutable-registry digest proof for
later immutable same-identity classification on a live plan.

## What Consumes the Frozen Plan

| Consumer                                | Required frozen input                                                                                                                                                                                         | Granularity rule                                      |
| --------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------- |
| shared orchestration workflow           | full `release-plan` envelope and graph                                                                                                                                                                        | one per selected profile run                          |
| top-level entry workflow publish bridge | full `release-plan`, topology-partitioned entry-workflow-bound publish selectors, and build receipt or bundle references                                                                                      | one per selected profile run                          |
| one build unit                          | owning `envelope.projects[project-id]` snapshot, one `graph.variants[variant-id]`, and that variant's `graph.artifacts[*]`                                                                                    | one build executor invocation per `variant-id`        |
| one publish unit                        | owning `envelope.projects[project-id]` snapshot, one `graph.publish-nodes[publish-node-id]`, its referenced `graph.target-instance-snapshots[*]`, and the referenced `graph.artifacts[*]` plus build receipts | one publish executor invocation per `publish-node-id` |
| top-level entry report job              | full plan plus all build receipts, publish receipts from every topology path, and synthetic skip receipts                                                                                                     | one per selected profile run                          |

A publish unit may consume artifacts from multiple variants only when the frozen
publish node already references them and the frozen target-instance contract
allows that aggregation. Executors do not widen that set. Under the narrowed
current-scope PyPI contract, that means each PyPI publish unit is single-variant
and carries exactly one wheel and zero or one sdist.

## Job-to-Job Handoff Boundaries

### Reusable Workflow Inputs

| Boundary                                       | Required input                                                                                                                                        | Required output                           |
| ---------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------- |
| orchestration -> build unit                    | immutable plan artifact plus one `variant-id` selector                                                                                                | one variant bundle plus one build receipt |
| orchestration -> reusable-hosted publish unit  | immutable plan artifact plus one reusable-hosted `publish-node-id` selector and the referenced build bundles or receipts                              | one publish receipt                       |
| orchestration -> entry workflow publish bridge | immutable plan artifact, topology-partitioned entry-workflow-bound publish selectors, and the referenced build bundle or receipt artifact names       | entry workflow schedules publish units    |
| entry workflow -> entry-hosted publish unit    | immutable plan artifact plus one entry-workflow-bound `publish-node-id` selector and the referenced build bundles or receipts from the bridge handoff | one publish receipt                       |
| entry workflow -> report job                   | immutable plan artifact, diagnostics, tag results, build receipts, skip receipts, publish receipts from all topology paths, and job conclusions       | one final operator-facing report          |

Reusable workflow boundaries carry selectors and immutable artifacts, including
caller-workflow-bound selectors when the registry validates the caller/top-level
workflow identity. Entry-hosted publish boundaries carry the same logical selector
and artifact inputs but are physically scheduled by the top-level workflow file so
registry OIDC claims name the configured publisher workflow. The executor
boundary inside each unit is narrower and uses a materialized request object.
When a caller-workflow-bound selector is implemented through `workflow_call`,
npmjs-style trusted publishing still validates the caller/top-level workflow
identity. In the current official nested chain, `release-official.yml` invokes
`release-orchestrate.yml`, which invokes `release-publish-node.yml`. Every
active caller job in that chain that must pass OIDC capability onward must
declare `id-token: write`, and the child reusable publish job that mints the
token must also declare `id-token: write`; this must not be generalized to
unrelated jobs.

When a reusable-workflow-bound selector is implemented through `workflow_call`,
RubyGems.org-style trusted publishing validates the reusable publish workflow
identity. In the current official nested chain, the `release-official.yml`
caller job invokes `release-orchestrate.yml`, whose publish caller job invokes
`release-publish-node.yml`. Because reusable workflows cannot elevate
permissions above their caller jobs, every active caller job in that chain that
passes OIDC capability onward must declare `id-token: write`, and the child
reusable publish job that mints the token must also declare `id-token: write`;
this must not be generalized to unrelated jobs.

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
For planner-time immutable-registry proof reuse, only live/non-dry-run
`build-result` receipts that were successfully produced by the relevant build
unit for the same current planner-frozen immutable-proof member binding
(`publish-node-id`, `artifact-id`,
`resolved-publish-identity.package-name`,
`resolved-publish-identity.version`) are admissible; matching `plan-id` alone
is not sufficient. Including the immutable resolved `{ package-name, version }`
identity in that binding keeps proof lookup version-sensitive for all
immutable package-registry families, including current-scope single-member
npm/RubyGems nodes. The planner-frozen
`projection.final-distribution-filenames-by-artifact-id` map still serves only
remote-member matching and classification; it is not the proof-binding key.
Overall workflow-run success is not required. No other proof source is
admissible, matching `artifact-id` alone is not sufficient, and dry-run or
other-binding receipts must not satisfy immutable same-identity proof
requirements. For any given immutable-proof member binding, the admissible
receipt set must collapse to one digest; if multiple admissible receipts exist
with differing digests, the planner must treat digest proof as unavailable and
fail closed for immutable registry classification that depends on that proof.

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

Each publish unit, regardless of whether it is reusable-hosted or entry-hosted,
must materialize one logical `publish-request` object for its executor with these
exact top-level fields:

| Field                                                 | Source                                                                                                    |
| ----------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `api-version: three.release.publish-request/v1alpha1` | control-plane materialization                                                                             |
| `kind: publish-request`                               | control-plane materialization                                                                             |
| `plan-id`, `profile`, `commit-sha`                    | plan envelope                                                                                             |
| `project` snapshot                                    | `envelope.projects[project-id]`                                                                           |
| `publish-node` snapshot                               | `graph.publish-nodes[publish-node-id]`                                                                    |
| `target-instance-snapshot`                            | referenced `graph.target-instance-snapshots[*]`                                                           |
| `artifacts` map keyed by `artifact-id`                | frozen artifact metadata from the plan plus receipt-proved file path, digest, and size from build results |

Entry-hosted publish units are not a forked executor contract. The top-level
entry workflow downloads the same frozen plan artifact and referenced
`build-result`/bundle artifacts, materializes the same `publish-request.json`
shape, and uploads the same `publish-result.json` receipt as a reusable-hosted
unit. The only intentional difference is physical workflow identity for OIDC
token minting. For first-delivery PyPI, the `official` entry workflow hosts this
unit so PyPI sees the configured `.github/workflows/release-official.yml`
publisher identity while the executor still consumes the frozen `pypi/pypi`
publish node, planner-frozen filenames, and receipt-proved build files.

The publish unit must create that request only when the selected publish node has
`publish-disposition: publish`. For `publish-disposition: skip-satisfied`, the
control plane emits the receipt directly without invoking a publish executor.
Within that request, `publish-node.artifact-ids` is the node's full
planner-owned desired member set and, whenever `publish-disposition: publish`,
the exact set the executor may upload. The executor must not widen, narrow, or
rediscover that set by its own destination preflight classification. Build
fan-out likewise continues to follow the node's full
`publish-node.artifact-ids` set.
For current-scope NuGet/PyPI nodes,
`publish-node.projection.final-distribution-filenames-by-artifact-id` must
cover every full `artifact-id` in the node, including singleton nodes. When it
covers an `artifact-id` for a live publish node, the publish executor must upload that
target-side member under exactly the frozen filename. It may satisfy the rule
either by uploading a bundle member whose basename already matches or by
staging/renaming to that exact filename before upload, but any filename
mismatch must fail closed. Those filenames remain
publish-layer remote-member matching keys only: they do not redefine
`artifact-id` and they do not replace receipt-owned bundle-relative paths from
the build side. For current-scope PyPI specifically, executors must not invoke
Hatchling again to decide upload filenames; planner-time Hatchling computation
already froze the authoritative result into the plan.
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
`replace-authoritative` is the planner-authorized same-tag GitHub Release
promotion path: the executor must converge the release identified by the frozen
`resolved-publish-identity.release-tag` to the node's full official publish
intent, including `desired-publish-state.release-state`, `artifact-ids`, and
`projection.asset-names-by-artifact-id` plus
`projection.asset-labels-by-artifact-id`, and it may delete, re-upload, or
recreate target-side assets as needed to do so. For GitHub Release, the executor
must upload or stage each receipted file under the frozen asset name for its
`artifact-id`; it must not use the bundle-relative path or produced basename as
a fresh target-side name. It must not reinterpret that path as a mere state flip
or as an additive merge with the prior buddy asset set. When a
`mutable-prerelease` destination already contains the same frozen
`resolved-publish-identity`, the executor must still follow the serialized mode
exactly: only planner-authorized buddy `FORCE` replay arrives as
`overwrite-mutable`; same-tag GitHub Release prerelease-to-release promotion
arrives as `replace-authoritative`; and every other same-identity mutable replay
case fails during planning and therefore must not reach the executor. When the
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

Each publish unit, regardless of workflow host topology, must emit one logical
`publish-result` object with these exact top-level fields:

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
  required; in current scope, `official` approval is wired through the GitHub
  protected `release` environment with required reviewers and self-review
  prevention, attached directly to the live jobs that can perform external side
  effects (`ensure-tag` and live `publish` matrix jobs). Administrator bypass
  stays a native environment capability when enabled, and the environment is not
  attached for no-side-effect runs such as dry-run or validation-only runs,
  zero-target runs, and all-`skip-satisfied` runs;
- **triggering-actor authorization**: only the control plane decides whether the
  triggering actor is allowed to start the selected profile; in current scope,
  `official` must fail before planning unless the triggering actor has at least
  repository `maintain`, and this check stays distinct from later approval;
- **concurrency**: only the entry workflow or orchestration workflow sets the
  duplicate-run concurrency key, using the already frozen workflow-entry-point
  plus commit rule;
- **selected-commit pinning**: only the control plane resolves the operator-
  selected branch/tag ref into the authoritative `commit-sha`, and every later
  planner, build, publish, and tag job must stay pinned to that same commit;
- **cancellation**: manual operator cancellation and ordinary platform
  cancellation use native GitHub workflow cancellation semantics and ordinary
  cancelled status; current scope does not adopt repo-defined duplicate-run
  auto-cancellation, and duplicate same-entry same-commit runs are serialized
  with `cancel-in-progress: false`;
- **tagging**: the planner resolves the final project-scoped `release-tag`,
  but the control plane creates or verifies each distinct selected Git tag once
  per run only when the selected plan contains at least one GitHub Release
  publish node, and it does so before any GitHub Release publication; if a
  required tag already exists, verification must confirm that it already points
  to the expected selected commit/object for that run, mismatches are hard
  failures, and current scope does not allow retargeting or moving existing
  release tags;
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
  `target-instance-snapshot.capabilities.publish-topology`, including whether the
  concrete job is reusable-orchestration-hosted with caller or reusable workflow
  identity validation, or top-level-entry-hosted, then select a
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
per-publish-node publish selectors -> reusable-hosted (including
caller-workflow-bound) or entry-hosted `publish-request` / `publish-result` ->
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
