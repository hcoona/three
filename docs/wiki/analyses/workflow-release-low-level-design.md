# Workflow Release Low-Level Design

## 1. Document Governance and Baseline Status

Status: this page is the post-topology-rebaseline low-level implementation
handoff baseline for workflow release. It supersedes earlier low-level
workflow-release drafts for current-scope implementation guidance while keeping
the frozen requirements, high-level architecture, descriptor schema, plan shape,
and workflow/executor boundary contracts unchanged.

The target reader is one experienced senior programmer who can choose efficient
internal structure without reinterpreting the release design. This page freezes
the concrete realization seams that affect correctness, testability, external
registry configuration, and acceptance evidence, but it intentionally does not
freeze every helper, internal module, private API, internal script, composite
action, or command-line wrapper. Those details remain implementation-owned only
while they stay below the frozen workflow identity, data-contract, permission,
routing, readiness, and evidence boundaries named here.

Low-level rebaseline changes may reorder and clarify this page before
implementation starts, including incompatible changes to lower-layer guidance.
They must not silently reopen upstream requirements or middle-layer contracts. If
a later consistency review finds a contradiction that cannot be solved inside the
low-level layer, the upstream decision must be escalated rather than rewritten
here.

### Rebaseline Skeleton

The accepted rebaseline order is:

1. document governance and reordering skeleton;
2. frozen upstream contracts;
3. workflow identity and filename contract;
4. topology routing core;
5. release-orchestrate-hosted publish path;
6. publish executor design;
7. registry adapter partitioning;
8. permissions and environment;
9. external setup and readiness;
10. acceptance traceability;
11. implementation-owned boundaries;
12. consistency review.

The topology rebaseline has completed against this skeleton. The sections below
are the normative low-level implementation baseline for current scope; successor
rebaseline work may reuse the skeleton as a review order but must not treat this
page as an incomplete draft.

### Low-Level Design Summary

| Area                      | Low-level decision                                                                                                                                                                                                                                                                                                                          |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Workflow files            | Use stable checked-in workflow filenames because OIDC trusted-publisher policies match workflow identity.                                                                                                                                                                                                                                   |
| Entry authorization       | Explicitly check `buddy` as `write+` and `official` as `maintain+` before planning; serialize each entry workflow without auto-cancellation.                                                                                                                                                                                                |
| Planner host              | Expose the planner through a repo-owned CLI contract on Ubuntu; current .NET metadata collection runs inside `prepare-release-plan` on Ubuntu with a trusted NBGV CLI.                                                                                                                                                                      |
| Request and receipt files | Serialize all cross-job machine data as UTF-8 JSON with LF line endings and stable `api-version` plus `kind`.                                                                                                                                                                                                                               |
| Dry-run builds            | Active dispatch surfaces do not expose dry-run or validation-build inputs; any future no-side-effect validation path needs a successor contract.                                                                                                                                                                                            |
| Build proof lookup        | Publish small attempt-scoped proof artifacts for immutable package bindings and GitHub Release asset content bindings so future planner runs can discover and validate admissible proofs.                                                                                                                                                   |
| Dispatch SHA pinning      | Pin the release commit before orchestration: empty `target` uses the GitHub UI dispatch ref/commit, while non-empty `buddy` targets must resolve to a commit reachable from a checked-in buddy-authorized ref before orchestration; later stages stay pinned to that release SHA while workflow code remains from the trusted dispatch ref. |
| Tag orchestration         | Create lightweight release tags and verify existing tags by peeling annotated tags to the selected commit.                                                                                                                                                                                                                                  |
| External setup            | Require active registry environments (`pypi`, `npmjs-gate`, `npmjs`, `rubygems`), registry trusted-publisher policies, and explicit external-registry live enablement before official OIDC registry publication.                                                                                                                            |
| Diagnostics               | Use a small registered planner diagnostic-code vocabulary plus a registration rule for new codes.                                                                                                                                                                                                                                           |
| Diagnostics artifact      | Serialize planner diagnostics through one closed container object rather than a raw array, NDJSON stream, or ad hoc log file.                                                                                                                                                                                                               |
| Execution sets            | Materialize matrix selectors in one closed JSON object so zero-target and all-skip runs have deterministic workflow behavior; dry-run / validation-build selectors are future-only.                                                                                                                                                         |
| Failure reporting         | Treat success and skip receipts as positive evidence only; failed jobs are summarized from job conclusions plus missing expected receipts, while cancellation reporting is best-effort.                                                                                                                                                     |
| Registry adapters         | Keep remote observation in planner adapters, live mutation in publish executors, and package metadata conformance in publish executors before upload.                                                                                                                                                                                       |
| Descriptor targets        | Freeze the first-delivery per-project `buddy`/`official` target baseline in this page; target choice is not implementation-owned.                                                                                                                                                                                                           |
| Acceptance                | Maintain a trace table from each acceptance scenario to descriptors, plans, receipts, registry evidence, and workflow conclusions.                                                                                                                                                                                                          |

## 2. Frozen Upstream Contracts and Non-Reopened Seams

This page consumes the signed-off requirements, high-level architecture, and
middle-layer contracts as fixed input. Low-level realization may still make
incompatible internal choices before implementation starts, but those choices
must remain below the frozen seams named here. They must not change descriptor
schema, plan shape, workflow/executor boundaries, topology mapping, or business
rules.

Frozen requirements and high-level architecture:

- Participation is descriptor-gated. Workflow release must not infer releasable
  projects from directory structure alone, and a project without a valid
  descriptor is skipped or rejected according to the frozen discovery rules.
- `buddy` and `official` are the only current-scope profiles and operator entry
  points. `buddy` is a `write+` day-to-day delivery path; `official` is a
  `maintain+` repository-maintenance path with protected-environment approval for
  live side effects.
- The architecture is planner-centric, with a fixed split between control plane,
  planning layer, and execution layer. Workflows orchestrate and enforce control
  gates, the planner computes release intent, and executors only carry out
  materialized build or publish requests.
- Dry-run and validation-only runs must have no live tag or external publication
  side effects. Validation-build receipts are validation evidence only and are
  not admissible as immutable publication proof.
- Every non-zero publish profile includes GitHub Release. The active split
  first-delivery registry scope includes PyPI, npmjs, and RubyGems.org through
  `release-orchestrate.yml`, and .NET GitHub Release asset builds are active
  through `release-build-dotnet.yml`; NuGet registry targets are not modeled as
  releasable until reviewed NuGet target catalog instances and publish routing
  are added.
- `buddy` and `official` must not publish the same package name to the same
  registry, except for targets explicitly marked `same-name-allowed` after an
  active workflow path exists. In the current split topology, active external
  registry smoke projects publish to GitHub Packages in `buddy` where supported
  and to their public registries in `official`; NuGet registry smoke targets are
  deferred rather than falsely modeled as releasable.
- Prior old-design GitHub Packages acceptance evidence is not sufficient for any
  same-identity `buddy`/`official` GitHub Packages smoke path. Such a path must
  not be carried forward until an active publish workflow exists and fresh
  post-change evidence is captured.
- `nbgv-python` is the only current-scope special version authority. Its version
  identity comes from the selected commit's checked-in `pyproject.toml`
  `[project].version`; other current-scope projects use the descriptor-declared
  build-system NBGV authority.

Frozen authoring and planner contracts:

- The only release authoring files are project descriptors at
  `src/**/three.release.yml` and the target-instance catalog at
  `eng/release/target-instances.yml`.
- The planner emits one `three.release.plan/v1alpha1` artifact with the frozen
  envelope and normalized graph shape. That plan is execution-authoritative for
  descriptor-owned data, catalog snapshots, resolved publish identity, desired
  publish state, replay/overwrite classification, and publish disposition.
- Manual dispatch pins the release `commit-sha` before orchestration. Empty
  `target` uses the GitHub UI dispatch ref/commit; non-empty `target` resolves a
  branch, tag, ref, or 40-hex SHA exactly once. Planning, build, tag, and publish
  work must stay pinned to that exact release commit SHA and must not follow a
  moving target ref later in the run, while workflow code remains from the
  trusted dispatch ref.
- A target-instance `capabilities.publish-topology` value is frozen into the plan
  target-instance snapshot. Later workflow routing derives concrete publish paths
  from that planned topology rather than recomputing registry topology.
- Planner-owned remote observation classifies destination state before plan
  emission. It uses public reads where possible and otherwise only
  least-privilege `GITHUB_TOKEN` reads for GitHub-hosted surfaces; it never uses
  publish credentials or approval-gated environment secrets.

Frozen workflow and executor boundaries:

- The control plane fans out at exactly two execution granularities: one build
  unit per `variant-id` and one logical publish unit per `publish-node-id`.
- One logical publish or skip result remains keyed by each `publish-node-id`,
  even when topology routes the concrete publish job through a hosted
  orchestration, caller-workflow-bound, reusable-workflow-bound, GitHub-token, or
  superseded entry-hosted path.
- Executors are thin consumers of materialized requests. They must not re-read
  release descriptors or `eng/release/target-instances.yml`, rediscover targets,
  query publish destinations for replay classification, or derive alternate
  publish identity, topology, overwrite policy, or same-tag GitHub Release
  replacement policy.
- Package-registry publish executors validate produced package metadata against
  the planner-frozen `resolved-publish-identity` before upload and fail closed on
  mismatch.
- `ensure-tag` runs only when the frozen execution sets contain at least one
  active GitHub Release publish node. It verifies the active required tag set
  before creating any missing tags. In the non-force path, it creates none if any
  existing active required tag points elsewhere and does not retarget tags. When
  the active workflow receives `force_update_tag=true`, the reviewed force path
  may retarget the release tag; the same normalized request force flag also
  enables only the planner-authorized buddy mutable-prerelease replay modes
  described below. All-skip and zero-target paths skip this protected
  write-capable job; planner observation and skip receipts are their evidence.
- Current-scope immutable proof reuse is limited to unexpired GitHub Actions
  artifacts under the platform retention window, subject to the frozen proof
  admissibility rules.

## 3. Workflow Identity and Filename Contract

Current-scope workflow files must use these stable checked-in paths for the active split release topology:

| File                                                  | Trigger or call shape               | Stable responsibility                                                                                                           | External registry filename role                                                                                                                                              |
| ----------------------------------------------------- | ----------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `.github/workflows/buddy.yml`                         | `workflow_dispatch`                 | `buddy` entry workflow for manual prerelease-oriented releases.                                                                 | Stable entry contract; not a trusted-publisher filename for current external registries.                                                                                     |
| `.github/workflows/official.yml`                      | `push` tags and `workflow_dispatch` | `official` entry workflow for tag-driven or manual official releases.                                                           | Stable entry contract; npmjs trusted-publisher filename for active official publication because npm validates the direct caller for `workflow_call`.                         |
| `.github/workflows/release-orchestrate.yml`           | `workflow_call`                     | Shared orchestrator that resolves the request, applies channel policy, builds, publishes, attests, and creates GitHub Releases. | Trusted-publisher filename for the current reusable-workflow OIDC jobs that mint PyPI and RubyGems.org tokens; also hosts the caller-workflow-bound npmjs token-minting job. |
| `.github/workflows/release-resolve.yml`               | `workflow_call`                     | Resolves tag or manual inputs to project, version, package directory, target commit, and release metadata.                      | Internal resolution contract; must not be configured as an external publisher.                                                                                               |
| `.github/workflows/release-build-python.yml`          | `workflow_call`                     | Builds one Python package distribution for the resolved project.                                                                | Internal build contract; must not be configured as an external publisher.                                                                                                    |
| `.github/workflows/release-build-node-pack.yml`       | `workflow_call`                     | Packs one Node package distribution for GitHub Packages and, when enabled, npmjs.                                               | Internal build/pack contract; must not be configured as an external publisher.                                                                                               |
| `.github/workflows/release-build-dotnet.yml`          | `workflow_call`                     | Builds active .NET GitHub Release asset packages on the runner required by each active variant's OS/RID dimensions.             | Internal build contract; must not be configured as an external publisher.                                                                                                    |
| `.github/workflows/release-build-ruby-gem.yml`        | `workflow_call`                     | Builds one Ruby gem distribution for the resolved project.                                                                      | Internal build contract; must not be configured as an external publisher.                                                                                                    |
| `.github/workflows/release-build-wxt.yml`             | `workflow_call`                     | Builds one WXT/web-extension artifact for the resolved project.                                                                 | Internal build contract; must not be configured as an external publisher.                                                                                                    |
| `.github/workflows/release-create-github-release.yml` | `workflow_call`                     | Verifies the preexisting release tag, then creates or updates the GitHub Release and uploads the resolved release assets.       | Internal GitHub-token contract; no external trusted publisher exists.                                                                                                        |
| `.github/workflows/release-prepare-release-notes.yml` | `workflow_call`                     | Prepares release notes consumed by GitHub Release publication.                                                                  | Internal release-support contract; must not be configured as an external publisher.                                                                                          |

The earlier descriptor-topology filenames `.github/workflows/release-build-variant.yml`
and `.github/workflows/release-publish-node.yml` are superseded in this split
release topology. They are deleted workflow contracts and must not be required by
operator checklists or new trusted-publisher configuration.

The low-level contract is the checked-in workflow file path; for registry-facing
workflows, it also includes the registry-visible filename derived from that path.
Implementation-owned scripts, helper action versions, command wrappers, and
executor internals are intentionally outside this frozen filename contract.
Renaming or replacing an active file above is a coordinated workflow contract
change, not a harmless refactor. When the changed file is currently configured in
an external trusted-publisher policy, the migration additionally requires
coordinated registry-policy updates and evidence refresh. In current scope, that
additional registry-policy migration constraint applies to
`release-orchestrate.yml` for PyPI and RubyGems.org trusted publishing, and to
`official.yml` for active npmjs trusted publishing.

Only filenames that an external registry may store in a trusted-publisher policy
are registry-facing identity contracts. In the active split topology, PyPI and
RubyGems.org token-minting jobs run inside `release-orchestrate.yml` with
registry-specific protected environments (`pypi` and `rubygems`). npmjs
token-minting also runs inside `release-orchestrate.yml`, but npm validates the
direct caller workflow name for `workflow_call`; active official npmjs trusted
publishing therefore stores `official.yml` plus environment `npmjs`.
`buddy.yml`, `release-resolve.yml`, `release-build-python.yml`,
`release-build-node-pack.yml`, `release-build-dotnet.yml`,
`release-build-ruby-gem.yml`, `release-build-wxt.yml`, and
`release-create-github-release.yml` remain stable workflow contracts, but they
must not be entered into current external trusted-publisher registry policies
unless a registry-specific topology explicitly names them.

PyPI trusted publishing must be configured to repository `hcoona/three`,
workflow filename `release-orchestrate.yml`, and the `pypi` GitHub Actions
environment for first-delivery live PyPI publication. PyPI must not be
configured to trust `official.yml`, `release-publish-node.yml`, or the deleted
variant/publish workflow filenames in this topology.

npmjs trusted publishing must be configured to repository `hcoona/three`,
workflow filename `official.yml`, and the `npmjs` GitHub Actions environment for
active official publication. The reusable orchestrator job that runs
`npm publish` requests the OIDC token, but npm validates the direct caller
workflow filename for `workflow_call`. If buddy npmjs live publication is
enabled later, configure a separate `buddy.yml` trusted publisher and grant that
caller `id-token: write`.

RubyGems.org trusted publishing must be configured to repository `hcoona/three`,
workflow filename `release-orchestrate.yml`, and the `rubygems` GitHub Actions
environment. The deleted `release-publish-node.yml` workflow is not part of the
active RubyGems.org trusted-publisher identity.

GitHub Release and GitHub Packages publication use `GITHUB_TOKEN` authority.
They do not have an external trusted-publisher policy and therefore do not add an
external registry workflow filename beyond the stable workflow contracts above.
GitHub Release asset attestation is the only current-scope `github-token` path
that also grants `id-token: write`; that OIDC permission is scoped to
`actions/attest-build-provenance` provenance signing in the orchestrator
attestation jobs and is not an external registry credential.

## 4. Topology Routing Core

### Entry Workflow Inputs

Both active entry workflows expose the same operator-facing manual dispatch input
shape. The operator-facing `project` input is normalized into the
planner-request `requested-project-ids` array with exactly one entry for current
manual runs. The older `dry-run`, `validation-build`, `force`, and
`canary-override-non-public-ref` model is superseded and is not available on the
active `buddy.yml` / `official.yml` dispatch surface.

| Input              | Type    | Owner          | Meaning                                                                                                                                                                                                                                                                   |
| ------------------ | ------- | -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `project`          | string  | entry workflow | Required workspace package name. Python uses `pyproject.toml` project name; Node uses `package.json` name; release tags must match `<project>`.                                                                                                                           |
| `version`          | string  | entry workflow | Required package version validated by ecosystem rules: Python PEP 440 subset, Node SemVer 2.0.0, or Ruby dotted version with suffix segments as allowed.                                                                                                                  |
| `target`           | string  | entry workflow | Optional branch, tag, ref, or 40-hex commit selector for the release/tag; empty uses the GitHub UI workflow ref/dispatch commit.                                                                                                                                          |
| `force_update_tag` | boolean | entry workflow | Operator-facing force input normalized to `request-flags.force`. For `official`, it authorizes only reviewed release-tag retargeting; for `buddy`, it authorizes planner-owned mutable-prerelease partial replay/overwrite under planner constraints. Default is `false`. |

For active orchestration calls, dry-run and validation-build behavior is fixed to
false unless a future workflow revision reintroduces explicit inputs and
documents the corresponding no-side-effect contract.

When `target` is empty, the selected commit comes from the GitHub UI workflow
ref/dispatch context, and the control plane pins that dispatch commit once as
the exact `commit-sha` at run start. When `target` is provided, it is an explicit
branch, tag, ref, or 40-hex commit selector that the control plane resolves once
and pins to the resulting commit for the rest of the run. The `buddy` entry
workflow is not arbitrary-branch publication: after resolving either the empty
dispatch-context target or a non-empty selector, it requires the target commit
to be reachable from a checked-in buddy-authorized branch ref in
`eng/release/buddy-target-refs.yml` for the selected project/channel. Raw SHAs,
tags, or non-allowlisted branch names are therefore rejected unless reachability
from an authorized buddy ref proves that they are already part of that governed
publication line.

In current scope, the workflow execution ref is a trusted workflow-identity input:
external trusted-publisher policies match stable workflow identity and
environment, so release workflows must be dispatched only from trusted refs: the
default branch, or a branch or tag protected by repository rules and allowed by
the active entry workflow and registry-environment policies. The release target
ref is separate: the optional `target` input may select a branch, tag, ref, or
40-hex commit to release, and the control plane resolves it exactly once before
planning. All later planning, build, tag, and publish work uses that pinned
release target commit and must not follow a moving ref. Arbitrary unprotected
workflow-code dispatch remains out of scope for live release workflows. Running
workflow code from an arbitrary untrusted ref would require a successor
workflow-entry design; releasing a separate target ref through the trusted
workflow is the active `target` contract, not a successor-only feature.

The active entry workflows (`buddy.yml` and `official.yml`) resolve raw
dispatch target/ref selectors before their concurrency-controlled orchestration
job starts. They pass the pinned `release_target_sha` to
`release-orchestrate.yml`, so the active normal flow does not let orchestration
or later publish jobs re-resolve a moving branch head. `release-orchestrate.yml`
is reusable only behind those active entry workflows for `official` and `buddy`
channels: it authenticates the OIDC reusable-workflow caller claim and requires
`official` to originate from `official.yml` and `buddy` to originate from
`buddy.yml` at the selected workflow ref. Custom allowlisted channels remain
governed by CODEOWNERS wildcard coverage for checked-in workflow files and are
not a bypass for the reserved `official` or `buddy` profiles.

Direct reusable callers are outside the active `official` and `buddy` entry
contract. If an allowlisted custom channel invokes `release-orchestrate.yml`
directly, orchestration delegates to `release-resolve.yml` to resolve and pin the
provided selector before downstream jobs run; the active entry workflows instead
perform that resolution first and pass the pinned `release_target_sha`.

Input normalization rules:

1. Validate `project` as one exact workspace package name and reject empty or
   ambiguous values before materializing the planner request.
2. Validate `version` with the selected ecosystem's active version rules.
3. Resolve `target` to the exact commit used for planning, build, tag, and
   publish work; an empty `target` means the selected dispatch commit. For
   `buddy`, a non-empty target must also be reachable from a checked-in
   authorized ref for the selected project/channel.
4. Preserve `force_update_tag` as the only active operator-facing force-style
   input and normalize it to planner `request-flags.force`. For `official`, that
   flag authorizes only reviewed release-tag retargeting. For `buddy`, it
   authorizes planner-owned mutable-prerelease partial replay with
   `publish-mode: overwrite-mutable` under the planner's remote-state constraints.
   It is not a dry-run, validation-build, immutable-target bypass, identity-check
   bypass, or descriptor/catalog policy bypass.
5. Normalize the exact resolved `project` value to a single-entry
   `requested-project-ids` array for the planner request, and reject unsupported
   legacy inputs such as `dry-run`, `validation-build`, `force`, and
   `canary-override-non-public-ref` if they appear on a manually dispatched run
   or wrapper request.
6. For `official`, reject the run before planner execution unless the selected
   GitHub ref and resolved release tag match the active public-release policy for
   the selected project. `buddy` is not restricted by that official-only public-ref
   guard.

If a pre-planner input rule rejects the run after workflow input normalization
has begun, the control plane must write `planner-diagnostics.json` using the same
`planner-diagnostic` contract and registered `REQ_*` code vocabulary used by
planner-hosted request validation. It must not emit a partial plan.

### Entry Authorization and Duplicate-Run Concurrency

`authorize-entry` is a control-plane gate and runs before planner execution for
both profiles.

Current-scope authorization policy:

| Profile    | Required current-attempt actor repository permission | Approval behavior                                              |
| ---------- | ---------------------------------------------------- | -------------------------------------------------------------- |
| `buddy`    | `write` or higher                                    | no extra approval                                              |
| `official` | `maintain` or higher                                 | active registry environments on live registry side-effect jobs |

The implementation must perform the permission check explicitly through the
GitHub API rather than relying only on the workflow dispatch UI. For reruns, the
actor being checked is the user who triggered the current attempt, not merely the
original dispatch actor; the original dispatch actor may still be retained as
audit/report metadata. If the permission check cannot resolve the current
attempt actor's effective repository permission, or if the resolved permission is
below the selected profile's threshold, the run fails closed before planning.
Authorization failures must write
`planner-diagnostics.json` using the planner-diagnostic file contract with
`REQ_ACTOR_UNAUTHORIZED`, must not emit a partial plan, and must still be
available to the final report path whenever GitHub Actions schedules that path.
The same pre-planner gate must verify that the selected workflow ref is a trusted
ref under the rule above. If the selected ref is not trusted or its protection
status cannot be determined, the run fails closed with
`REQ_UNTRUSTED_WORKFLOW_REF` before planning and before any write token, OIDC
token, or protected environment can be used.

Because those two gates run before `release-orchestrate.yml` is invoked, the
top-level entry workflow owns a minimal pre-orchestration failure-report path. On
`REQ_ACTOR_UNAUTHORIZED`, `REQ_UNTRUSTED_WORKFLOW_REF`, or an equivalent
pre-orchestration input rejection, that path uploads the diagnostics artifact,
emits `release-report.json` with `plan.plan-id`,
`plan.selected-project-ids`, and `artifacts.plan-artifact-name` set to `null`,
and renders the same operator-facing summary format used after orchestration. It
must not call `release-orchestrate.yml`, derive execution sets, request the
active registry environments, mint an OIDC token, or obtain write
permissions. This path is a reporting bridge only; it does not create a second
planner or a second orchestration contract.

Current scope does not adopt native in-progress duplicate-run auto-cancellation.
Each active entry workflow resolves raw `inputs.target` once in
`authorize-entry`. After that job succeeds, the entry workflow starts only the
`orchestrate` caller job under a job-level `concurrency` lock with
`cancel-in-progress: false` and an active dynamic group derived from the resolved
release identity:

- canonical group shape: `release/${project_id}/v${release_version}`;
- checked-in workflow expression: `${{ needs.authorize-entry.outputs.release_group }}` for the `orchestrate` job in both `buddy.yml` and `official.yml`.

The older literal groups `three-release:buddy` and `three-release:official` are
superseded.

These active keys serialize only runs that collide on the same release identity,
so different releases can occupy different concurrency groups while the same
release's active orchestration stays serialized across `buddy.yml` and
`official.yml`. The lock starts after `authorize-entry`; pre-orchestration
entry authorization and its failure-report bridge are outside the protected
window. Final reporting is covered only to the extent it is part of the called
`release-orchestrate.yml` job graph; the top-level pre-orchestration report job
does not declare the release group. GitHub Actions native concurrency is not a
FIFO queue: at most one running job and one pending job may exist per group, and
a newly queued job in the same dynamic group replaces any older pending job in
that group. Current scope accepts that platform pending-replacement behavior
before the orchestrate job starts; it is reported as ordinary GitHub
cancellation and is not a repo-defined supersession protocol for an already
running release.

Child reusable workflows, matrix rows, publish jobs, and report jobs must not
declare the entry workflow's release concurrency group. They are covered by the
single entry `orchestrate` caller job's job-level concurrency and by normal job
dependencies inside the called workflow. Current scope defines no separate
SHA-keyed external lock/queue release concurrency mechanism beyond the active
dynamic project/version release-tag job group.
Adding an explicit lock/queue requires a successor design that analyzes GitHub
Actions pending-run cancellation, ordering, and failure-cleanup semantics against
the chosen primitive.

### Orchestration Job Realization

The selected entry workflow and the shared orchestration workflow together
implement the middle-layer job sequence with these concrete data handoffs:

| Job                  | Physical host                                                                                                                     | Required inputs                                                                                                                                                          | Required outputs                                                                                                                                                                                                      |
| -------------------- | --------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `authorize-entry`    | top-level entry workflow before invoking `release-orchestrate.yml`                                                                | GitHub event context, selected profile, and resolved normalized dispatch context                                                                                         | Authorization conclusion and authorized normalized run metadata consumed by orchestration.                                                                                                                            |
| `validate-authoring` | `prepare-release-plan` in the shared orchestration workflow before planner execution                                              | Pinned target checkout at `commit-sha`, normalized planner request, descriptors, and shared catalog                                                                      | Diagnostics-only failure, or `dotnet-planner-metadata-input.json` when .NET metadata is required.                                                                                                                     |
| `.NET metadata`      | `prepare-release-plan` on Ubuntu, after authoring validation and before final planner execution                                   | Pinned target checkout at `commit-sha`, `dotnet-planner-metadata-input.json`, and the trusted NBGV CLI installed from nuget.org                                          | `dotnet-planner-metadata.json` observation file consumed by the planner in the same job.                                                                                                                              |
| `plan`               | shared orchestration workflow                                                                                                     | Pinned checkout at `commit-sha`, normalized planner request, prior proof lookup service, raw dry-run controls, external live-enable map, and any required metadata files | Frozen plan, `execution-sets.json`, and synthetic skip-result artifacts, or diagnostics.                                                                                                                              |
| `build`              | shared orchestration workflow calling the reusable build unit                                                                     | Plan artifact, one `variant-id` per matrix row                                                                                                                           | Variant bundle, `build-result`, and optional immutable-proof artifacts.                                                                                                                                               |
| `ensure-tag`         | control-plane job before publish fan-out                                                                                          | Frozen plan plus active GitHub Release publish nodes                                                                                                                     | `tag-result.json` tag verification or creation evidence when active GitHub Release nodes exist.                                                                                                                       |
| `publish`            | shared orchestration for active hosted selectors; superseded entry-workflow selectors are not current-scope active routes         | Plan artifact, one logical publish node per selected matrix row, referenced build receipts, and a materialized registry or GitHub Release publish request.               | Package-registry `publish-result.json` receipts keyed by `publish-node-id`, and GitHub Release result receipts keyed by release/tag/asset evidence while remaining correlated to logical publish nodes in the report. |
| `report`             | top-level entry workflow after orchestration-hosted publish jobs complete, or the entry workflow's pre-orchestration failure path | Plan, diagnostics, tag results, build results, skip results, package-registry publish results, GitHub Release result receipts, job conclusions                           | Final operator summary and `release-report.json`.                                                                                                                                                                     |

In current-scope first delivery, execution-set derivation is an implementation
detail of the `plan` job, not a separate reportable workflow job. This keeps the
closed `release-report.json.jobs` shape aligned with the workflow. The produced
selectors must still be serialized as machine-readable JSON rather than
reconstructed from ad hoc shell output in later jobs.

`validate-authoring` is the only pre-plan phase that may reject malformed
descriptors or catalog data with `DESC_*` or `CATALOG_*` diagnostics. The current
.NET metadata phase runs inside `prepare-release-plan` only after that validation
succeeds and consumes only the closed `dotnet-planner-metadata-input.json`
manifest, not raw descriptor discovery state. If authoring validation fails, .NET
metadata collection is skipped and the workflow reports the normalized
diagnostics path instead of a metadata helper failure. If the validated metadata
input cannot be evaluated by the trusted NBGV CLI on Ubuntu, the failure is a
metadata-observation failure for the listed validated project, not a
descriptor-schema or descriptor-static-validation decision.
No separate validated-authoring state crosses workflow jobs in current scope:
`dotnet-planner-metadata-input.json` is the only serialized validate-to-metadata
handoff. The final planner execution consumes the pinned target checkout,
normalized planner request, and required metadata files; it must not depend on an
ad hoc validate-to-plan state file or invent a second authoring-validation
handoff.

Every planner, build, tag, and publish job that materializes the source tree must
check out the exact resolved `commit-sha` with enough Git history and tags for
NBGV to compute the same project version that the planner froze. In current
scope, that means non-shallow history and tags, equivalent to `fetch-depth: 0`
plus tag fetching for `actions/checkout`, rather than a default shallow checkout.
If a job cannot materialize the selected commit with NBGV-compatible history, it
must fail closed instead of computing a fallback version.

`execution-sets.json` is the authoritative low-level routing contract for build
and publish fan-out. Later jobs must consume this file rather than re-deriving
publish routes from target family, registry name, executor type, shell
conditionals, or workflow-local side lists. Its top-level shape is closed:

```json
{
    "api-version": "three.release.execution-sets/v1alpha1",
    "kind": "execution-sets",
    "plan-id": "...",
    "dry-run": false,
    "publish-intent-node-ids": [],
    "active-variant-ids": [],
    "active-publish-node-ids": [],
    "active-publish-selectors": {
        "github-token": [],
        "external-oidc-entry-workflow": [],
        "external-oidc-caller-workflow": [],
        "external-oidc-reusable-workflow": []
    },
    "skip-satisfied-publish-node-ids": [],
    "selected-github-release-publish-node-ids": [],
    "active-github-release-publish-node-ids": []
}
```

The selector fields are derived as follows:

1. `publish-intent-node-ids` contains every selected publish node whose frozen
   `publish-disposition` is `publish`. Active dispatches do not expose dry-run
   suppression.
2. `active-publish-node-ids` equals `publish-intent-node-ids` for active live
   dispatches. A future dry-run input would need a successor contract before it
   could alter this set.
3. `active-variant-ids` contains the distinct variants reachable from
   `publish-intent-node-ids` for active live dispatches. Historical
   validation-build behavior is superseded.
4. `active-publish-selectors` partitions every `active-publish-node-ids` member
   by the planner-frozen
   `target-instance-snapshot.capabilities.publish-topology` value on that
   publish node. The control plane must not guess topology from target family,
   registry family, target-instance ref, executor implementation, or credential
   posture after the planner has emitted the plan. The key set is frozen to
   exactly:
    - `github-token`;
    - `external-oidc-entry-workflow`;
    - `external-oidc-caller-workflow`;
    - `external-oidc-reusable-workflow`.

    In current active catalog entries, PyPI and RubyGems.org trusted publishing
    use `external-oidc-reusable-workflow` hosted from
    `release-orchestrate.yml`; npmjs uses
    `external-oidc-caller-workflow`, with npm's trusted-publisher identity bound
    to the direct `official.yml` caller and environment `npmjs` while the publish
    command still runs in `release-orchestrate.yml`.
    `external-oidc-entry-workflow` is retained only as a superseded/historical
    selector value and must not be selected for active current-scope targets.

    Each active publish node appears in exactly one of those arrays. Unsupported
    or unmapped frozen topology values fail the topology gate before fan-out;
    supported empty partitions are still serialized as `[]`.

5. `skip-satisfied-publish-node-ids` contains every selected publish node whose
   frozen `publish-disposition` is `skip-satisfied`; the `plan` job uses this set
   to emit synthetic skip receipts immediately after publishing
   `execution-sets.json` and before any build, tag, or publish fan-out starts.
6. `selected-github-release-publish-node-ids` is the subset of all selected
   publish nodes whose target family is `github-release`, including
   `skip-satisfied` nodes.
7. `active-github-release-publish-node-ids` is the subset of
   `active-publish-node-ids` whose target family is `github-release`; `ensure-tag`
   may create missing tags only for this active subset.

Empty arrays are first-class workflow outcomes, not missing outputs. A build or
publish matrix with an empty corresponding selector is skipped by the control
plane, and the `report` job still runs from the serialized selectors, available
receipts, diagnostics, and job conclusions when the workflow has not been
cancelled before the platform can schedule that job. Empty arrays are meaningful
for zero-target selections, all-`skip-satisfied` selections, and any run where a
particular topology partition has no active members. Dry-run and validation-build
empty-selector semantics are future-only until such inputs are reintroduced.

Topology changes only the physical host that runs the live publish side effect.
It does not change the logical `publish-node-id`, the planner-frozen
`publish-disposition`, the synthetic skip-receipt semantics, the
`publish-request.json` materialization rules, or the standard `publish-result`
contract. Report aggregation therefore treats publish and skip receipts from all
topology paths as receipts for the same logical publish-node graph.

At fan-out time, the control plane routes from `execution-sets.json` at a high
level:

- `github-token`, `external-oidc-caller-workflow`, and
  `external-oidc-reusable-workflow` remain on the shared orchestration path.
  Current PyPI, npmjs, and RubyGems token-minting / publish jobs are hosted from
  `.github/workflows/release-orchestrate.yml`; the topology still controls which
  workflow identity the registry validates. PyPI and RubyGems.org use the
  reusable-workflow identity, while npmjs uses the caller-workflow-bound
  `official.yml` identity with environment `npmjs`.
- `external-oidc-entry-workflow` is a superseded historical selector for
  top-level-entry-hosted publication. It is not an active current-scope routing
  mode while the active trusted-publishing path is
  either `external-oidc-reusable-workflow` / `release-orchestrate.yml`
  (PyPI/RubyGems.org) or `external-oidc-caller-workflow` via `official.yml`
  (npmjs).

This section defines only the routing contract. Registry-specific publish job
details belong to the `release-orchestrate.yml` hosted publish path design, and
publish executor internals remain executor-owned.

Current scope does not use a separate `approve` job and does not use an active
`environment: release` publish gate. Active registry side effects use the
registry-specific environment model in `release-orchestrate.yml`: PyPI publishes
from `pypi`, npmjs uses `npmjs-gate` for human approval and `npmjs` for OIDC
token scoping, and RubyGems publishes from `rubygems`. All-skip GitHub Release
selections do not run `ensure-tag` and therefore do not attach a protected
registry environment for tag verification. Their skip-satisfied evidence comes
from planner observation and skip-result receipts.
This keeps the environment claim on OIDC-backed external trusted publishing jobs
aligned with the registry-side trusted-publisher configuration.

### Dry-Run and Validation Build Policy

The active `buddy.yml` and `official.yml` dispatch surfaces do not expose
`dry-run` or `validation-build`. Active orchestration calls therefore treat
dry-run and validation-build as hard-coded `false`, and no current operator path
may rely on them for no-side-effect validation.

The older dry-run / validation-build policy is superseded. If a future workflow
revision reintroduces those inputs, it must define a fresh no-side-effect
contract before runbooks or acceptance tests depend on it.

### Planner CLI Boundary

The planner should be invoked through a repo-owned CLI with subcommands that
mirror stable workflow seams:

| Subcommand               | Required behavior                                                                                                                                                              |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `validate-authoring`     | Load and validate all in-scope descriptors plus the shared catalog without emitting a plan; emit `dotnet-planner-metadata-input.json` when validated .NET metadata is needed.  |
| `plan`                   | Consume one normalized planner request and emit either one plan JSON file or planner diagnostics.                                                                              |
| `compute-pypi-filenames` | For the narrowed PyPI path, invoke the selected build backend tooling during planning and return/freeze exact final distribution filenames and SHA-256 digests by artifact ID. |
| `render-summary`         | Convert plan, diagnostics, and receipts into compact Markdown for the workflow summary.                                                                                        |

The implementation may merge these commands into one binary or script entry
point as long as the workflow still treats the file outputs as the stable
contract.

The planner job runs on Ubuntu and uses the repository's `mise` tool boundary to
install and invoke the ecosystem tools needed for planner-owned normalization.
Current scope keeps that Ubuntu planner as the single policy engine. .NET
metadata is collected in the same `prepare-release-plan` job on Ubuntu by
installing the trusted NBGV CLI from nuget.org into an isolated tool path and
feeding it the validated `dotnet-planner-metadata-input.json` manifest. The
metadata phase is started only from that input, which is produced after
descriptor and catalog validation, so the helper cannot become the first
component to reject malformed descriptor authoring. The planner must not directly
load unsupported .NET projects as a fallback for missing helper evidence. Current
scope still allows the Ubuntu planner host to call `uv`/Hatchling for PyPI
filename computation, `pnpm`/`npm` for npm package metadata, Ruby/Bundler or
RubyGems evaluation for gem metadata, and the trusted NBGV CLI for .NET metadata.
These calls are planner-owned observations, not release builds; actual C# release
builds are routed to Windows, Ubuntu, or macOS build units from the active
variant dimensions, and publish credentials or approval-gated secrets remain
unavailable to the planner. If a required tool or helper output is unavailable or
cannot be normalized into the frozen contract, the planner fails closed with
diagnostics rather than falling back to static guesses.

The CLI must fail closed:

- invalid descriptors anywhere in current scope block all planning;
- remote observation errors after bounded retry block plan emission;
- no partial plan file is written on blocking planner failure;
- machine-readable diagnostics are written before returning a non-zero exit code
  whenever request normalization has begun.

### Planner Diagnostic Codes

The middle-layer contract freezes the diagnostic object shape but not the code
vocabulary. Current scope should start with this minimum code registry:

| Code                            | Phase            | Scope                  | Meaning                                                                                                                                                 |
| ------------------------------- | ---------------- | ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `REQ_INVALID_INPUT`             | `validation`     | `request`              | Control-plane release input could not be normalized, including workflow dispatch inputs and release enablement variables.                               |
| `REQ_FORCE_FOR_OFFICIAL`        | `validation`     | `request`              | Superseded. Official `request-flags.force` is now reserved for the reviewed `force_update_tag` release-tag retarget path.                               |
| `REQ_PROJECT_NOT_FOUND`         | `validation`     | `project`              | An explicitly requested project ID was not an in-scope releasable project.                                                                              |
| `DESC_SCHEMA_INVALID`           | `validation`     | `project`              | A project descriptor failed file-schema validation.                                                                                                     |
| `DESC_STATIC_INVALID`           | `validation`     | `project`              | Descriptor passed syntax but failed static repo validation.                                                                                             |
| `CATALOG_SCHEMA_INVALID`        | `validation`     | `request`              | The shared target-instance catalog failed schema or static validation.                                                                                  |
| `CATALOG_REF_NOT_FOUND`         | `validation`     | `project`              | A descriptor target reference did not resolve to exactly one catalog target instance.                                                                   |
| `VERSION_AUTHORITY_FAILED`      | `normalization`  | `project`              | The planner could not resolve the project-scoped version identity.                                                                                      |
| `DOTNET_METADATA_FAILED`        | `normalization`  | `project`              | The trusted NBGV-based .NET metadata helper could not evaluate or emit required metadata for a validated .NET project manifest.                         |
| `PUBLISH_IDENTITY_CONFLICT`     | `normalization`  | `project`              | Resolved package-registry identities violate current-scope profile coexistence rules after deferred metadata resolution.                                |
| `PYPI_FILENAME_COMPUTE_FAILED`  | `normalization`  | `publish-node`         | Planner-time PyPI filename computation failed or produced an unexpected member set.                                                                     |
| `REMOTE_QUERY_FAILED`           | `query`          | `publish-node`         | Destination query failed after bounded retry.                                                                                                           |
| `REMOTE_NORMALIZATION_FAILED`   | `normalization`  | `publish-node`         | Raw destination state could not be normalized for the target family.                                                                                    |
| `REMOTE_CLASSIFICATION_FAILED`  | `classification` | `publish-node`         | Normalized destination state could not be reduced to one remote-observation class.                                                                      |
| `IMMUTABLE_PROOF_UNAVAILABLE`   | `classification` | `publish-node`         | Required prior build digest proof was absent, expired, ambiguous, or conflicting.                                                                       |
| `IMMUTABLE_PARTIAL_UNSUPPORTED` | `classification` | `publish-node`         | Same-identity immutable remote state was a proved partial subset, which current scope fails closed.                                                     |
| `REMOTE_CONFLICTING`            | `classification` | `publish-node`         | Same-identity remote state conflicts with the frozen publish intent.                                                                                    |
| `OFFICIAL_FROZEN_VERSION`       | `classification` | `project`              | A `buddy FORCE` request targeted a project/version already frozen by official GitHub Release.                                                           |
| `REQ_ACTOR_UNAUTHORIZED`        | `validation`     | `request`              | The triggering actor did not have the required repository permission for the selected profile.                                                          |
| `REQ_UNTRUSTED_WORKFLOW_REF`    | `validation`     | `request` or `project` | The selected workflow ref was not a trusted protected release ref, or an `official` ref did not match the selected project NBGV `publicReleaseRefSpec`. |
| `REQ_EXTERNAL_TARGET_DISABLED`  | `validation`     | `publish-node`         | A selected live official external OIDC registry target was not present in the live-enable allowlist.                                                    |
| `REQ_EXTERNAL_TOPOLOGY_BLOCKED` | `validation`     | `publish-node`         | A selected live official external OIDC registry target cannot run through the current workflow topology.                                                |
| `PLAN_INTERNAL_INVARIANT`       | `validation`     | `request`              | Planner detected an impossible internal state after validation should have prevented it.                                                                |

New planner diagnostic codes may be added by implementation, but every new code
must be registered in this page or in a successor registry before tests depend
on it. Free-form adapter messages belong in `details`, not in the `code` field.

`planner-diagnostic` fields are closed:

| Field                         | Required rule                                                                                             |
| ----------------------------- | --------------------------------------------------------------------------------------------------------- |
| `api-version`                 | Always required; value is `three.release.planner-diagnostic/v1alpha1`.                                    |
| `kind`                        | Always required; value is `planner-diagnostic`.                                                           |
| `code`                        | Always required; one registered diagnostic code.                                                          |
| `message`                     | Always required; concise human-readable text.                                                             |
| `phase`                       | Always required; one current-scope phase value.                                                           |
| `scope-kind`                  | Always required; `request`, `project`, or `publish-node`.                                                 |
| `project-id`                  | Required when `scope-kind` is `project` or `publish-node`; omitted otherwise.                             |
| `publish-node-id`             | Required when `scope-kind` is `publish-node` and a plan publish node was materialized; omitted otherwise. |
| `target-instance-snapshot-id` | Required when one target instance was identified for the failing path; omitted otherwise.                 |
| `resolved-publish-identity`   | Required when external publish identity was resolved for the failing path; omitted otherwise.             |
| `blocking`                    | Always required; current-scope diagnostics that abort plan emission use `true`.                           |
| `details`                     | Always required; empty object when there is no extra machine context.                                     |

Conditional fields are omitted when not applicable; they are not serialized as
`null`. For `REQ_EXTERNAL_TARGET_DISABLED` and
`REQ_EXTERNAL_TOPOLOGY_BLOCKED`, no plan artifact is published, so
`publish-node-id` must still be present whenever the current-scope topology or
live-enable gate runs after the in-memory plan has materialized the affected
publish node. `project-id`, `target-instance-snapshot-id`, and
`resolved-publish-identity` must also be present for those current-scope gate
failures. A future pre-node validation gate that fails before any publish node ID
exists must use a non-`publish-node` diagnostic scope instead of omitting the
field from a materialized publish-node diagnostic.
For `REQ_EXTERNAL_TARGET_DISABLED`, `details` must also include the required
enablement token. For `REQ_EXTERNAL_TOPOLOGY_BLOCKED`, `details` must identify
the blocked registry family and the unsupported workflow topology.

`planner-diagnostics.json` is not a raw array, NDJSON stream, or rendered log.
It is one closed container object:

```json
{
    "api-version": "three.release.planner-diagnostics/v1alpha1",
    "kind": "planner-diagnostics",
    "diagnostics": [
        {
            "api-version": "three.release.planner-diagnostic/v1alpha1",
            "kind": "planner-diagnostic",
            "code": "REQ_INVALID_INPUT",
            "message": "...",
            "phase": "validation",
            "scope-kind": "request",
            "blocking": true,
            "details": {}
        }
    ]
}
```

When this artifact is emitted for a failed run, `diagnostics` must be non-empty.
The control plane may render those diagnostics into Markdown, but downstream
jobs and tests consume only the JSON container and its logical diagnostic
objects.

### File Formats

All cross-job files should be JSON, not YAML, even when examples in middle-layer
pages are written in YAML for readability. JSON avoids YAML parser differences in
workflow shell, PowerShell, Node, and Python helpers.

Required file naming inside artifacts:

| Logical object          | File name                            |
| ----------------------- | ------------------------------------ |
| Planner request         | `planner-request.json`               |
| .NET metadata input     | `dotnet-planner-metadata-input.json` |
| .NET planner metadata   | `dotnet-planner-metadata.json`       |
| Frozen plan             | `release-plan.json`                  |
| Planner diagnostics     | `planner-diagnostics.json`           |
| Build request           | `build-request.json`                 |
| Build result            | `build-result.json`                  |
| Tag result              | `tag-result.json`                    |
| Publish request         | `publish-request.json`               |
| Publish result          | `publish-result.json`                |
| Skip result             | `skip-result.json`                   |
| Execution sets          | `execution-sets.json`                |
| Entry publish handoff   | `entry-publish-handoff.json`         |
| Immutable proof wrapper | `immutable-proof.json`               |
| Final run report data   | `release-report.json`                |

Every JSON file must use:

- UTF-8 without a byte-order mark;
- LF line endings;
- deterministic object key ordering where produced by repo-owned tooling;
- top-level `api-version` and `kind` for every contract object;
- no secrets, tokens, API keys, or raw OIDC tokens.

The cross-job JSON contracts are closed at the top level unless the defining
section names an extensibility field. Implementations must not add arbitrary
root-level fields that downstream jobs or tests could accidentally start
depending on. Current-scope extensibility fields are:

| Object family            | Extensibility field | Rule                                                             |
| ------------------------ | ------------------- | ---------------------------------------------------------------- |
| Planner diagnostics      | `details`           | Adapter-specific machine context belongs under `details`.        |
| Publish and skip results | `evidence`          | Small family-specific receipt evidence belongs under `evidence`. |

The boundary documents define complete request and result object shapes for the
current `v1alpha1` handoff. In particular, `planner-request`,
`dotnet-planner-metadata-input`, `dotnet-planner-metadata`, `build-request`,
`build-result`, `tag-result`, `entry-publish-handoff`,
`publish-request`, `publish-result`, and `skip-result` must not grow extra
root-level fields during implementation unless an extensibility field is named
above or in the object's defining section. New root-level machine fields require
a successor contract update before tests or workflows depend on them.

Before workflow jobs exchange these files, implementation must add executable
contract coverage for the closed cross-job JSON shapes. That coverage may be JSON
Schema, typed fixture validation, or an equivalent repo-owned test harness, but
it must include golden valid fixtures and representative closed-shape rejection
cases for plan, request, metadata, result, selector, proof, diagnostics, and
report files.
The `release-plan.json` fixtures must be derived from the authoritative shape in
[Workflow Release Plan Shape](./workflow-release-plan-shape.md). The test harness
choice remains implementation-owned; the field set does not.

Treat this executable contract coverage as the first implementation milestone
for workflow data exchange. For objects whose complete `v1alpha1` field set is
assembled from this page plus the workflow-boundary tables, the implementation
must codify the combined field set before wiring dependent jobs. No workflow,
executor, renderer, or test may depend on an ad hoc root-level field that has not
been registered in this page or a successor contract.

`planner-request.json` is the control-plane-authored planner input file. Its
closed current-scope shape is:

```json
{
    "api-version": "three.release.planner-request/v1alpha1",
    "kind": "planner-request",
    "profile": "buddy",
    "commit-sha": "...",
    "requested-project-ids": ["..."],
    "request-flags": {
        "force": false
    }
}
```

`requested-project-ids` contains the exact active `project` input after
workspace/package resolution. Current manual entry workflows normalize to a
single-entry array; future multi-project dispatch would require a separate entry
contract update. In `v1alpha1`, `request-flags` has the exact key set shown
above.

`dotnet-planner-metadata-input.json` is the validated authoring handoff from
descriptor/catalog validation to the .NET metadata phase inside
`prepare-release-plan`. Its closed current-scope shape is:

```json
{
    "api-version": "three.release.dotnet-planner-metadata-input/v1alpha1",
    "kind": "dotnet-planner-metadata-input",
    "commit-sha": "...",
    "projects": {
        "hjg-pngcs": {
            "descriptor-path": "src/public/lib/Hjg.Pngcs/three.release.yml",
            "primary-manifest-path": "src/public/lib/Hjg.Pngcs/Hjg.Pngcs.csproj",
            "requires-package-id": true
        }
    }
}
```

The `projects` map contains only current-scope .NET descriptors that already
passed file-schema validation and author-time static repo validation. Its values
are normalized paths and booleans from validated authoring state, not raw
descriptor snippets. `requires-package-id` is true exactly when the validated
.NET descriptor participates in at least one current-scope NuGet-shaped package
artifact (`.nupkg` or `.snupkg`) or at least one NuGet-family package-registry
target; it is false for app, installer, zero-target, and other .NET descriptors
that do not produce or publish NuGet-shaped package artifacts. The .NET metadata
helper must not rediscover descriptors, accept additional project paths,
reinterpret target usage, recompute `requires-package-id`, or downgrade a
descriptor validation failure into a metadata evaluation failure.

`dotnet-planner-metadata.json` is a pre-final-plan observation file authored by
the trusted NBGV-based metadata phase in `prepare-release-plan` for current-scope
.NET projects. Its closed current-scope shape is:

```json
{
    "api-version": "three.release.dotnet-planner-metadata/v1alpha1",
    "kind": "dotnet-planner-metadata",
    "commit-sha": "...",
    "projects": {
        "hjg-pngcs": {
            "descriptor-path": "src/public/lib/Hjg.Pngcs/three.release.yml",
            "primary-manifest-path": "src/public/lib/Hjg.Pngcs/Hjg.Pngcs.csproj",
            "resolved-version": "1.2.3",
            "package-id": "IO.Github.Hcoona.Pngcs"
        }
    }
}
```

The `projects` keys are descriptor-owned `project.id` values from the validated
.NET metadata input manifest only. `resolved-version` is required for every
listed project and must come from the selected commit's build-system-integrated
NBGV result. `package-id` is required if and only if the corresponding metadata
input entry has `requires-package-id: true`; entries with
`requires-package-id: false` omit it. When `requires-package-id` is true, the
trusted NBGV-based metadata helper must evaluate a non-empty MSBuild `PackageId`
from the selected commit's project manifest and include it as `package-id`. If
the evaluated value is empty, missing, or cannot be normalized, the helper or planner reports
`DOTNET_METADATA_FAILED` for that project and no plan is emitted. The helper must
use the validated metadata input manifest to locate current-scope .NET project
manifests and must not rediscover descriptors, decide selected projects, target
usage, publish disposition, replay policy, or profile behavior. The Ubuntu
planner remains the owner of release policy: it validates this metadata against
the normalized planner request and frozen descriptor/catalog contracts, then
fails closed with diagnostics when a required .NET entry is missing, comes from a
different `commit-sha`, cannot be normalized, or conflicts with descriptor-owned
source paths.

`tag-result.json` is the control-plane-authored positive evidence file for a
successful `ensure-tag` job. Its closed current-scope shape is:

```json
{
    "api-version": "three.release.tag-result/v1alpha1",
    "kind": "tag-result",
    "plan-id": "...",
    "commit-sha": "...",
    "tags": [
        {
            "release-tag": "release/project/v1.2.3",
            "outcome": "verified",
            "expected-commit-sha": "...",
            "peeled-commit-sha": "..."
        }
    ]
}
```

`tags` must cover every distinct required GitHub Release tag for active publish
nodes in the run. Each `outcome` is either `verified` for an existing tag that
peeled to the selected commit or `created` for a newly created lightweight tag.
`expected-commit-sha` is the selected commit, and `peeled-commit-sha` is the
commit observed after verification or creation. Current scope does not define
failed tag-result files; tag failures are reported from the `ensure-tag` job
conclusion plus a missing positive `tag-result`. When there are no active GitHub
Release publish nodes, no `ensure-tag` job runs and no `tag-result.json` is
required.

`release-report.json` is the control-plane-authored final report data consumed by
`render-summary`. Its closed current-scope shape keeps the legacy dry-run field
hard-coded to `false` for compatibility; it is not an active dispatch input:

```json
{
    "api-version": "three.release.report/v1alpha1",
    "kind": "release-report",
    "run": {
        "repository": "hcoona/three",
        "workflow": "...",
        "run-id": 123,
        "run-attempt": 1,
        "head-sha": "...",
        "profile": "buddy",
        "dry-run": false,
        "validation-build": false,
        "canary-override-non-public-ref": false,
        "conclusion": "success"
    },
    "plan": {
        "plan-id": "...",
        "selected-project-ids": ["CircularList"]
    },
    "artifacts": {
        "plan-artifact-name": "...",
        "planner-diagnostics-artifact-name": null,
        "dotnet-planner-metadata-input-artifact-name": null,
        "dotnet-planner-metadata-artifact-name": null,
        "execution-sets-artifact-name": "...",
        "entry-publish-handoff-artifact-name": "...",
        "tag-result-artifact-name": null,
        "build-result-artifact-names": [],
        "github-release-result-artifact-names": [],
        "publish-result-artifact-names": [],
        "skip-result-artifact-names": []
    },
    "jobs": {
        "authorize-entry": { "conclusion": "success" },
        "validate-authoring": { "conclusion": "success" },
        "dotnet-metadata": { "conclusion": "skipped" },
        "plan": { "conclusion": "success" },
        "build": {
            "conclusion": "skipped",
            "failed-variant-ids": []
        },
        "ensure-tag": { "conclusion": "skipped" },
        "publish": {
            "conclusion": "skipped",
            "failed-publish-node-ids": []
        }
    },
    "counts": {
        "selected-projects": 1,
        "active-variants": 0,
        "active-publish-nodes": 0,
        "published-nodes": 0,
        "skipped-publish-nodes": 0
    }
}
```

Current scope defines no root-level extension field for `release-report.json`.
New root-level report fields require a successor contract update before
workflows, renderers, or tests depend on them.

Artifact-name nullability is closed:

| Field                                                   | Nullability rule                                                                                                                                                        |
| ------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `artifacts.plan-artifact-name`                          | `null` whenever no plan succeeded; after successful plan publication, the frozen plan artifact name.                                                                    |
| `artifacts.planner-diagnostics-artifact-name`           | `null` only when no diagnostics artifact exists; otherwise the diagnostics artifact name.                                                                               |
| `artifacts.dotnet-planner-metadata-input-artifact-name` | `null` when no validated .NET metadata input artifact was emitted; otherwise the metadata input artifact name.                                                          |
| `artifacts.dotnet-planner-metadata-artifact-name`       | `null` when no .NET metadata observation artifact was emitted; otherwise the metadata observation artifact name.                                                        |
| `artifacts.execution-sets-artifact-name`                | `null` whenever no plan succeeded; after successful selector serialization, the execution-set artifact name.                                                            |
| `artifacts.entry-publish-handoff-artifact-name`         | `null` whenever no plan succeeded; after successful selector serialization, the `release-entry-publish-handoff-v1-<run-id>-<attempt>-<safe-id(plan-id)>` artifact name. |
| `artifacts.tag-result-artifact-name`                    | `null` when `ensure-tag` did not emit positive tag evidence; otherwise the tag-result artifact name.                                                                    |
| `artifacts.build-result-artifact-names`                 | Empty array when no positive build-result artifacts exist; otherwise sorted artifact names.                                                                             |
| `artifacts.github-release-result-artifact-names`        | Empty array when no positive GitHub Release result receipt artifacts exist; otherwise sorted artifact names.                                                            |
| `artifacts.publish-result-artifact-names`               | Empty array when no positive publish-result artifacts exist; otherwise sorted artifact names.                                                                           |
| `artifacts.skip-result-artifact-names`                  | Empty array when no synthetic skip-result artifacts exist; otherwise sorted artifact names.                                                                             |

`dotnet-planner-metadata-input-artifact-name` and
`dotnet-planner-metadata-artifact-name` are both `null` when the validated
authoring state contains no current-scope .NET projects requiring metadata. If
.NET metadata is required but either artifact is not positively emitted, the
missing artifact is reported through the relevant job conclusion and diagnostics;
the report must not synthesize a metadata artifact name or infer metadata from
descriptor contents. `entry-publish-handoff-artifact-name` is
non-null after a successful active `release-orchestrate.yml` plan because the
workflow uploads `entry-publish-handoff.json` alongside the frozen plan and
execution sets. If the handoff was required but not emitted, the report
summarizes the plan or artifact-upload failure from job conclusions and
diagnostics, and downstream jobs must not recover a missing bridge from
target-family or selector guesses.

Because execution-set derivation is part of the `plan` job in current-scope first
delivery, `release-report.json.jobs` intentionally has no
`derive-execution-sets` entry. If a later design splits execution-set derivation
into a standalone workflow job, the report schema must be updated before that job
can become observable by tests or renderers.

For planner failure before plan emission, `plan.plan-id`,
`plan.selected-project-ids`, and `artifacts.plan-artifact-name` are `null`, while
`artifacts.planner-diagnostics-artifact-name` identifies the diagnostics artifact
for pre-planner or planner failures. This also covers the external OIDC
live-enable readiness gate, where the `plan` job may have computed plan and
execution-set data in memory but suppresses both artifacts before any live side
effect can be scheduled. That field may be `null` only when no diagnostics
artifact can exist, such as cancellation before the platform persists the
diagnostic path. `run.conclusion` uses GitHub job conclusion spelling such as
`success`, `failure`, or `cancelled`. Job-level conclusions under `jobs` use the
same spelling and may also use `skipped` for jobs that did not run because their
serialized selector set was empty or their prerequisite path was suppressed.

Successful `tag-result`, `build-result`, `publish-result`, and `skip-result`
files are positive evidence only. Current scope does not define failed tag,
failed build, failed publish, or failed skip receipt files. For non-cancelled
runs, the `report` job must run after success, failure, and skipped matrix paths,
then summarize failure from the serialized execution sets, job conclusions, and
any missing expected positive receipts. For cancelled runs, in-run report
generation is best-effort only because GitHub Actions cancellation can prevent a
not-yet-started final job from being scheduled. The authoritative cancellation
evidence is therefore the native GitHub cancelled conclusion plus any positive
receipts already persisted before cancellation. A completed positive receipt
remains valid evidence of a side effect that happened before a later job failed
or the workflow was cancelled.

Matrix aggregation is deterministic for non-cancelled runs:

1. The expected build row set is `execution-sets.active-variant-ids`; the expected
   publish row set is `execution-sets.active-publish-node-ids`.
2. `jobs.build.conclusion` and `jobs.publish.conclusion` are `skipped` only when
   their expected row set is empty or unavailable because planning failed before
   `execution-sets.json` was published.
3. For a non-empty expected row set, the aggregate conclusion is `success` only
   when every expected row has exactly one matching positive receipt and no row
   concluded unsuccessfully.
4. The aggregate conclusion is `cancelled` when the workflow or any expected row
   is cancelled before the report can prove either success or failure for every
   expected row.
5. Otherwise the aggregate conclusion is `failure`; this includes a non-empty
   expected row set that GitHub skipped because an earlier prerequisite failed.
6. `failed-variant-ids` and `failed-publish-node-ids` contain every expected ID
   whose row concluded unsuccessfully or whose positive receipt is missing in a
   non-cancelled run, sorted lexicographically. They are empty for `success`,
   `skipped`, and best-effort `cancelled` summaries.

### Artifact Naming and Retention

GitHub Actions artifact names are the lookup key available to later jobs and
runs through the Actions artifact API. The control plane should therefore use
deterministic artifact names with a short hash suffix instead of embedding raw
plan IDs that may contain slashes or long strings. Run-local transport artifacts
are attempt-scoped so re-running the same workflow run cannot collide with
artifacts from a prior attempt. Immutable proof artifacts keep the binding hash
immediately after the stable prefix and add the producing run and attempt as a
suffix; future proof lookup discovers candidate proof artifacts by that binding
prefix, then validates the repeated binding and provenance inside
`immutable-proof.json`.

Define:

```text
safe-id(input) = first 24 lowercase hex chars of SHA-256 over the UTF-8 input
```

Current-scope artifact names:

| Artifact                   | Name pattern                                                                                            |
| -------------------------- | ------------------------------------------------------------------------------------------------------- |
| Frozen plan                | `release-plan-v1-<run-id>-<attempt>-<safe-id(plan-id)>`                                                 |
| Planner diagnostics        | `release-planner-diagnostics-v1-<run-id>-<attempt>`                                                     |
| .NET metadata input        | `release-dotnet-planner-metadata-input-v1-<run-id>-<attempt>`                                           |
| .NET planner metadata      | `release-dotnet-planner-metadata-v1-<run-id>-<attempt>`                                                 |
| Execution sets             | `release-execution-sets-v1-<run-id>-<attempt>-<safe-id(plan-id)>`                                       |
| Entry publish handoff      | `release-entry-publish-handoff-v1-<run-id>-<attempt>-<safe-id(plan-id)>`                                |
| Variant bundle             | `release-build-bundle-v1-<run-id>-<attempt>-<safe-id(plan-id + "\n" + variant-id)>`                     |
| Build result               | `release-build-result-v1-<run-id>-<attempt>-<safe-id(plan-id + "\n" + variant-id)>`                     |
| Tag result                 | `release-tag-result-v1-<run-id>-<attempt>-<safe-id(plan-id)>`                                           |
| Publish result             | `release-publish-result-v1-<run-id>-<attempt>-<safe-id(plan-id + "\n" + publish-node-id)>`              |
| Skip result                | `release-skip-result-v1-<run-id>-<attempt>-<safe-id(plan-id + "\n" + publish-node-id)>`                 |
| Immutable proof            | `release-immutable-proof-v1-<safe-id(binding-json)>-<run-id>-<attempt>`                                 |
| GitHub Release asset proof | `release-github-release-asset-proof-v1-<safe-id(github-release-asset-binding-json)>-<run-id>-<attempt>` |
| GitHub Release result      | `release-github-release-result-v1-<run-id>-<attempt>-<safe-id(github-release-result-binding-json)>`     |
| Final report               | `release-report-v1-<run-id>-<attempt>`                                                                  |

`binding-json` is the canonical JSON serialization of:

```json
{
    "publish-node-id": "...",
    "artifact-id": "...",
    "package-name": "...",
    "version": "..."
}
```

For this hash input, canonical JSON means the exact UTF-8 byte sequence produced
with this member order, double-quoted JSON strings, colon separators with no
spaces, comma separators with no spaces, and no trailing newline:

```text
{"publish-node-id":"...","artifact-id":"...","package-name":"...","version":"..."}
```

The values are copied from the current frozen plan after the planner has applied
the target family's package-name and version canonicalization rules. Implementers
must not use manifest spelling, destination spelling, or executor-discovered
metadata as `binding-json` input.

`github-release-asset-binding-json` is the canonical JSON serialization of:

```json
{
    "publish-node-id": "...",
    "artifact-id": "...",
    "release-tag": "...",
    "asset-name": "..."
}
```

For this hash input, canonical JSON uses the same byte-level rules as
`binding-json`. The release tag and asset name values are the planner-frozen
GitHub Release projection values, not remote API spellings discovered during
lookup.

The immutable proof artifact must include `immutable-proof.json`, and that file
must repeat the full binding fields. The repeated fields are required so a hash
collision or accidental name reuse cannot silently satisfy proof lookup.
When a future planner needs proof for a binding, the control-plane lookup lists
unexpired artifact records whose names start with
`release-immutable-proof-v1-<safe-id(binding-json)>-`, then applies the
admissibility checks below. The suffix is provenance, not part of the binding.
If two rerun attempts produce proofs for the same binding, both remain discoverable
and must collapse to one digest before proof is usable.

Immutable proof artifacts are emitted by the build-unit control-plane wrapper
after the executor has produced `build-result.json` and after the control plane
has uploaded the corresponding build-result and bundle artifacts. The build
executor still receives only its `build-request` and does not receive
publish-node snapshots. For each active live build result, the wrapper reads the
frozen plan and emits one immutable proof artifact for each immutable
package-registry publish-node/artifact binding that references an artifact
fulfilled by that build result. Historical or future dry-run / validation-build
units must not emit admissible immutable proof artifacts; if implementation
persists any diagnostic wrapper for such runs, proof lookup must ignore it
through explicit report metadata.

The workflow should not extend artifact retention just to satisfy immutable proof
reuse. If an artifact is expired or missing, the proof is unavailable and the
planner fails closed when proof is required.

### Immutable Proof Wrapper

`immutable-proof.json` is control-plane-authored wrapper metadata around an
executor-authored `build-result`. It is not a replacement for `build-result`.

Minimum shape:

```json
{
    "api-version": "three.release.immutable-proof/v1alpha1",
    "kind": "immutable-proof",
    "binding": {
        "publish-node-id": "...",
        "artifact-id": "...",
        "package-name": "...",
        "version": "..."
    },
    "plan-id": "...",
    "project-id": "...",
    "variant-id": "...",
    "build-result-artifact-name": "...",
    "build-result-artifact-id": 123,
    "bundle-artifact-name": "...",
    "run": {
        "repository": "hcoona/three",
        "workflow": "...",
        "run-id": 123,
        "run-attempt": 1,
        "head-sha": "...",
        "live": true,
        "dry-run": false,
        "validation-only": false
    },
    "artifact": {
        "bundle-relative-path": "...",
        "sha256": "...",
        "byte-size": 123
    }
}
```

Group 7 does not allow root extensions on `immutable-proof.json`. Planner proof
lookup rejects wrappers with unknown root fields before admissibility checks, and
must ignore a proof unless all of these checks pass:

1. artifact exists and is not expired;
2. `run.live` is true;
3. `run.dry-run` and `run.validation-only` are false;
4. `run.head-sha` matches the selected `commit-sha`;
5. binding equals the current planner-frozen immutable-proof member binding;
6. referenced `build-result` artifact name/id resolves to a closed receipt input
   for the current plan, project, and variant, and that receipt maps the same
   `artifact-id` to the same digest and byte size;
7. all admissible proof artifacts for the same binding collapse to one digest.

If multiple admissible proofs for one binding have different digests, proof is
unavailable. The planner must not pick the newest proof to break the tie.

### GitHub Release Asset Content Proof

GitHub Release `exact-satisfied` is based on GitHub Artifact Attestations, not on
asset names or labels alone. For every live, non-dry-run GitHub Release asset,
separate language-specific attestation jobs in `release-orchestrate.yml` run
`actions/attest-build-provenance` against the built artifacts before any
dependency-gated GitHub Release upload path runs when attestation generation is
enabled. The GitHub Release executor then performs tag-verification-only
pre-mutation checks of the already-created release tag, including
`gh release create --verify-tag` for new releases, plus post-upload attestation
verification and proof generation inside `release-create-github-release.yml`:
after it creates or converges the release and uploads the planned assets, it
matches the staged assets to the producer receipts and release result, reads the
corresponding GitHub Artifact Attestations, generates GitHub Release asset proof
wrappers, uploads the proof artifact, and persists proof sidecars on the release.
Upload success without verified proof generation fails closed. Active `buddy`
GitHub Release publishing is unsupported and fails before release mutation while
`buddy` attestations remain disabled. This remains fail-closed for mixed-target
descriptors: disabling a selected `buddy` `github-release/public` target because
attestations are disabled must fail the plan even when registry targets are also
selected.
The attestation invocation uses `subject-name` equal to the planner-frozen release
asset name and `subject-digest` equal to the uploaded file's `sha256:<hex>`
digest. In the active split topology, the language-specific attestation/provenance
jobs in `release-orchestrate.yml` request only read permissions plus
`id-token: write` and `attestations: write`; they do not mutate GitHub Releases.
The separate GitHub Release mutation jobs call
`release-create-github-release.yml` with `contents: write`, `actions: read`, and
`attestations: read`; they do not receive `id-token` or `attestations: write`.

The planner must freeze the full attestation signer workflow identity in each
GitHub Release publish node as `attestation.signer-workflow`. This is the value
passed to `gh attestation verify --signer-workflow`, not a bare workflow filename.
For the current active split topology, GitHub Release asset provenance is
minted by the language-specific `attest-*-enabled` jobs inside the reusable
orchestrator, so the frozen value is
`hcoona/three/.github/workflows/release-orchestrate.yml`. If a successor topology
moves GitHub Release attestation to an entry workflow or another reusable
workflow, the plan must freeze that topology's full signer workflow identity
before any GitHub Release asset proof is admissible.

The GitHub Release executor also emits a
`release-github-release-asset-proofs-v1-<run-id>-<attempt>-<binding-digest>`
artifact containing generated proof wrapper JSON files for successfully uploaded
and attested assets, then uploads matching `release-github-release-asset-proof-v1-*`
sidecar files to the release. These wrapper and sidecar proofs are required
corroborating evidence for later exact-satisfaction lookup. Exact satisfaction
for a later run requires both a verified GitHub Artifact Attestation and
admissible current wrapper/sidecar proof evidence for each planned asset. If
wrapper or sidecar evidence is missing, expired, inadmissible, or inconsistent,
proof is unavailable and the planner must fail closed or classify the same-tag
state as non-exact through the replay matrix. If the attestation has been
deleted, cannot be fetched, or fails verification, proof is unavailable.

Minimum shape:

```json
{
    "api-version": "three.release.github-release-asset-proof/v1alpha1",
    "kind": "github-release-asset-proof",
    "binding": {
        "publish-node-id": "...",
        "artifact-id": "...",
        "release-tag": "...",
        "asset-name": "..."
    },
    "plan-id": "...",
    "project-id": "...",
    "variant-id": "...",
    "run": {
        "repository": "hcoona/three",
        "workflow": "...",
        "run-id": 123,
        "run-attempt": 1,
        "head-sha": "...",
        "live": true,
        "dry-run": false,
        "validation-only": false
    },
    "artifact": {
        "bundle-relative-path": "...",
        "sha256": "...",
        "byte-size": 123
    },
    "attestation": {
        "predicate-type": "https://slsa.dev/provenance/v1",
        "subject-name": "...",
        "subject-digest": "sha256:...",
        "signer-workflow": "hcoona/three/.github/workflows/release-orchestrate.yml",
        "source-repository": "hcoona/three",
        "source-digest": "...",
        "attestation-id": "...",
        "attestation-url": "..."
    }
}
```

Planner lookup for GitHub Release asset content proof is read-only:

1. query the frozen `release-tag` and match the planner-frozen `asset-name`;
2. download the remote asset and compute its SHA-256 digest and byte size;
3. verify the downloaded file with `gh attestation verify` against repository
   `hcoona/three`, predicate type `https://slsa.dev/provenance/v1`, the
   planner-frozen `attestation.signer-workflow` for the publish node, and the
   selected `commit-sha` as source digest;
4. require the verified statement to contain a subject whose name equals the
   planner-frozen asset name and whose digest equals the downloaded file digest;
5. require the downloaded byte size to equal the remote asset size reported by the
   GitHub Release API and the admissible current
   `github-release-asset-proof.json` wrapper/sidecar proof evidence required for
   that planned asset;
6. require all admissible wrappers and attestations for the same
   `github-release-asset-binding-json` to collapse to one digest and byte size.

If any required check fails, or if multiple admissible proofs for the same binding
collapse to different digests or sizes, the asset is not content-equivalent. The
planner must not treat it as `exact-satisfied`; it must classify the same-tag
state through the replay matrix or fail closed when it cannot safely classify it.

### Build Executor Realization

Build executors are selected from `project.ecosystem`.

| Ecosystem | Runner requirement                                              | Tool boundary                            | Notes                                                                                  |
| --------- | --------------------------------------------------------------- | ---------------------------------------- | -------------------------------------------------------------------------------------- |
| `dotnet`  | Windows, Ubuntu, or macOS from active variant OS/RID dimensions | `mise`, `dotnet`, PowerShell when needed | Windows remains the default for package variants without explicit platform dimensions. |
| `python`  | Ubuntu                                                          | `mise`, `uv`, Hatch build backend        | `nbgv-python` must preserve the planner-frozen `pyproject.toml` version.               |
| `node`    | Ubuntu                                                          | `mise`, `pnpm`, npm CLI                  | Pack exactly one npm tarball per planned npm artifact.                                 |
| `ruby`    | Ubuntu                                                          | `mise`, RubyGems, Bundler when needed    | Build exactly one `.gem` per planned RubyGems artifact.                                |

For .NET package variants, the current repository-wide MSBuild configuration
sets `IncludeSymbols=true`, `DebugType=portable`, and
`SymbolPackageFormat=snupkg` in the root `Directory.Build.props`. The `src/`
layer marks library roots packable and app, lab, and sample roots nonpackable,
with explicit project-level overrides still able to opt out. Therefore current
packable .NET library release builds are expected to produce one `.nupkg` and
one `.snupkg` when the descriptor declares the corresponding symbol artifact;
nonpackable .NET app release builds do not implicitly produce `.snupkg` package
artifacts.

For first-delivery `.NET` app artifacts with
`kind-family: binary` and `concrete-kind: executable`, the contractual artifact
shape is one receipted executable file per `artifact-id`, not a directory. The
build executor must use project-specific `dotnet publish` settings or packaging
steps that produce one single-file executable for each planned RID, with no
sidecar files required to satisfy the receipted artifact. If a future .NET app
requires a directory layout, support files, or an archive, it must model that as a
separate concrete artifact kind or successor descriptor contract rather than
stretching `binary/executable`.

`qidian-novel-downloader` publishes standalone `binary/executable` artifacts in
first delivery. `image-occlusion-editor` keeps its WinUI publish output as an
executor-internal input to the Inno Setup packaging step, not as a descriptor
artifact, because the WinUI publish directory is not a standalone single-file
executable artifact. Its first-delivery GitHub Release target publishes only the
installer.

`image-occlusion-editor` acceptance must use a test-harness-owned packaging
evidence artifact or log, not a `build-result` extension, to prove that Inno Setup
consumed the executor-internal WinUI publish output. That evidence is scoped to
acceptance fixtures only: it must identify the packaging command or script,
selected RID, installer artifact ID, and normalized internal publish-output path
used as Inno Setup input, and it must not be consumed by planner classification,
publish execution, proof lookup, or release-report aggregation.

Executors must materialize every requested `artifact-id` exactly once in the
`build-result`. A variant bundle may contain incidental files, but only files
listed by `artifact-id` in `build-result.json` are contractual release artifacts.

For package artifacts, build executors should prefer ecosystem-native pack
commands that produce the same package file later uploaded by publish executors:

- `.NET`: `dotnet pack` or project-specific `dotnet publish` plus packaging
  steps for binaries or installers.
- Python: current-scope Hatchling build through the repo's `uv`/`mise` tool
  boundary.
- Node: `npm pack --json` or equivalent workspace-aware packaging through pnpm
  plus npm.
- Ruby: `gem build`.

The exact command wrappers remain implementation-owned, but the output receipt is
not.

## 5. Entry-Hosted Publish Path

The active split topology hosts external-registry publish jobs directly in
`.github/workflows/release-orchestrate.yml`. The job that mints each registry
OIDC token is scoped by a registry-specific GitHub Actions environment: `pypi`
for PyPI, `npmjs` for npmjs.org, and `rubygems` for RubyGems.org.

The shared orchestration workflow owns planning, build fan-out, tag-gating,
registry publish fan-out, attestation, GitHub Release creation, and production of
the authoritative execution sets. Deleted descriptor-topology publish workflows
must not be required for this split topology.

The current high-level handoff is:

1. `official.yml` or `buddy.yml` performs entry authorization and invokes
   `release-orchestrate.yml` with tag-derived or manual release controls.
2. `release-orchestrate.yml` emits the frozen plan, `execution-sets.json`,
   synthetic skip receipts, build receipts and bundles, tag evidence, and
   publish results for every active hosted publish/token-minting node.
3. Active PyPI and RubyGems trusted-publishing jobs run inside
   `release-orchestrate.yml` under the `external-oidc-reusable-workflow`
   topology. Active npmjs publishing also runs inside `release-orchestrate.yml`,
   but its catalog topology is `external-oidc-caller-workflow`: npm validates
   the direct caller workflow `.github/workflows/official.yml` with environment
   `npmjs`. All three paths consume the same frozen plan, referenced build
   receipts and bundles, and materialized `publish-request.json` contract used by
   the shared publish executor.
4. Each hosted publish job emits the standard `publish-result.json` receipt and
   uploads it using the standard publish-result artifact contract. The logical key
   remains the original `publish-node-id`; the physical host is not part of
   publish identity.

Active orchestration emits `entry-publish-handoff.json` as a
control-plane-authored bridge that records the entry-workflow publish inputs
derived from the same finalized plan and execution sets. The current shape is:

```json
{
    "api-version": "three.release.entry-publish-handoff/v1alpha1",
    "kind": "entry-publish-handoff",
    "plan-id": "...",
    "commit-sha": "...",
    "plan-artifact-name": "...",
    "execution-sets-artifact-name": "...",
    "entry-publish-node-ids": ["..."],
    "publish-inputs-by-node-id": {
        "publish-node/...": {
            "target-instance-snapshot-id": "...",
            "build-result-artifact-names": ["..."],
            "build-bundle-artifact-names": ["..."]
        }
    }
}
```

`entry-publish-node-ids` must equal
`execution-sets.active-publish-selectors.external-oidc-entry-workflow` in the
same order, and `publish-inputs-by-node-id` had to contain exactly one key for
every entry publish node and no others. Even when no entry-hosted publish nodes
are active, the successful plan uploads this handoff artifact so report
aggregation can cite the exact bridge contract used by the run.

For active PyPI and npmjs official publication, the job that requests the
registry trusted-publishing OIDC token and performs the live upload runs in
`.github/workflows/release-orchestrate.yml`. PyPI uses environment `pypi` and
trusts the reusable workflow identity. npmjs uses environment `npmjs` but trusts
the direct caller workflow identity (`official.yml`) for `workflow_call`; both
the official caller job and the reusable publish job grant `id-token: write`.

Hosted publication does not create a separate request or receipt schema. It uses
the same `publish-request.json` and `publish-result.json` contracts, artifact
naming rules, plan identity, build-result references, and failure semantics for
all active publish paths. Shared helpers may materialize or validate those files,
but they must not re-read descriptors, reclassify registry state, recompute
topology, or change the `release-orchestrate.yml` workflow identity that mints an
external OIDC token.

Report aggregation waits for the publish fan-out results produced inside
orchestration. The report consumes the union of hosted publish results, synthetic
skip receipts, diagnostics, and job conclusions. A missing expected hosted
`publish-result.json` is reported for the corresponding logical
`publish-node-id`.

Detailed publish executor internals remain in Section 6, registry adapter
behavior remains in Section 7, permissions and environment rules remain in
Section 8, external trusted-publisher setup remains in Section 9, and acceptance
evidence remains in Section 10.

## 6. Publish Executor Design

Publish executors are selected from `target-instance-snapshot.family` and are
thin realizers of planner-authored intent. Their only authoritative inputs are
the materialized `publish-request.json`, the frozen plan data referenced by that
request, referenced build receipts and bundles, and workflow-provided credential
or token material appropriate to the already selected topology. An executor must
not rediscover release descriptors, re-read `eng/release/target-instances.yml`,
recompute topology, decide `skip-satisfied` or replay behavior, reclassify
overwrite or replacement policy, or derive an alternate publish identity.

### Topology Host Model

The topology decides which workflow identity hosts the publish command and where
any registry OIDC token is minted. It does not change the logical publish unit:
package-registry rows emit `publish-result.json`, while GitHub Release rows emit
`github-release-result.json` as a GitHub Release receipt keyed by release tag,
target commit, and planned assets.

| Topology path                     | Physical publish host                                                                                               | Token or authority boundary                                                                   | Executor contract                                                                                         |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `github-token`                    | Reusable publish job on the shared orchestration path; currently reusable-hosted per `execution-sets.json` routing. | Uses GitHub-provided `GITHUB_TOKEN`; no external OIDC token is minted.                        | Execute the planned GitHub-hosted publication with the same request and result contracts as other paths.  |
| `external-oidc-entry-workflow`    | Superseded top-level entry workflow job. Not active for current-scope targets.                                      | Historical external registry token mint in the entry workflow identity.                       | Deferred/historical only; active registry trusted publishing uses the caller or reusable topology rows.   |
| `external-oidc-caller-workflow`   | Command may run in a reusable publish workflow directly called by the workflow filename configured in the registry. | Registry validates the direct caller workflow identity for the trusted-publisher policy.      | Preserve the direct-caller identity boundary while consuming the standard materialized request.           |
| `external-oidc-reusable-workflow` | Reusable publish workflow job.                                                                                      | Registry trusts the reusable workflow identity where the registry supports that policy shape. | Mint and use the external token only inside the reusable workflow identity selected by the plan topology. |

Shared scripts, composite actions, or libraries may be used by hosted publish
paths. That code reuse must not move the live upload, credential request, or OIDC
token minting step across the workflow identity boundary selected by topology.

### Request Consumption and Guardrails

For each active publish node, workflow routing materializes one
`publish-request.json` for one logical `publish-node-id`. The executor consumes
that request as an instruction, not as a discovery seed. It may validate that
referenced plan slices, build receipts, package files, and bundle digests are
internally consistent, but validation failures must stop the publish rather than
falling back to source-tree or registry discovery.

Before any live upload starts, each package-registry publish executor must:

1. locate the receipted file for every planned `artifact-id` in the publish node;
2. apply only planner-frozen final distribution filenames from the target
   projection;
3. read package metadata from the concrete file that will be uploaded;
4. verify package name and version against the planner-frozen
   `publish-node.resolved-publish-identity` under the family equivalence rules;
5. fail closed if metadata cannot be read, normalized, or compared
   unambiguously.

The metadata check is a pre-upload safety gate. A mismatch between produced
package metadata and `resolved-publish-identity` is never a reason to rewrite the
package, pick a different target, or ask the registry which identity would be
accepted.

Publish executors must not perform destination preflight queries to decide
whether to skip, overwrite, promote, or reconcile. Any destination call before
upload must be strictly necessary to carry out the already frozen publish action,
such as obtaining a short-lived trusted-publishing credential.

### Result Contract

Package-registry topology paths emit exactly one positive `publish-result.json`
for a successful live publish of a logical node. Active GitHub Release
publication emits `github-release-result.json` in a run/attempt-scoped
`release-github-release-result-v1-<run-id>-<attempt>-<safe-id(binding)>`
artifact instead. The binding is the canonical JSON object containing `tagName`
and `targetSha`, which prevents rerun reports from downloading a stale fixed-name
receipt from an earlier attempt. Its receipt schema records `tagName`,
`targetSha`, `releaseId`, `releaseExisted`, and an `assets` array of planned asset names,
sizes, and SHA-256 digests. GitHub Release reporting maps the receipt back to
the plan through that tag, target commit, and asset set rather than through a
`publish-node-id` field. Physical host, called workflow filename, runner
operating system, helper implementation, or token-minting location do not create
a new logical publish identity. Failure reporting therefore treats a missing
hosted result as a missing positive receipt for the same planned GitHub Release
mutation, regardless of the active or historical topology label that selected the
physical publish host.

Registry-specific upload commands, evidence fields, and target-side conflict
handling are partitioned to Section 7. Permission grants and environment binding
belong to Section 8. External trusted-publisher setup belongs to Section 9.
Acceptance scenarios and required proof artifacts belong to Section 10.

## 7. Registry Adapter Partitioning

Registry adapters are partitioned by target surface, not only by source
ecosystem. Each surface below freezes the credential posture, required publish
topology, planner-owned observation, executor-owned mutation, identity
conformance rule, and live mutation boundary for current scope. Permission and
environment mechanics remain in Section 8, owner-side setup remains in Section
9, and acceptance traceability remains in Section 10.

Planner adapters may observe public registry state, or GitHub-hosted state with
least-privilege `GITHUB_TOKEN` reads where this section explicitly allows it.
They must not use live publish credentials, request trusted-publishing
credentials, or mutate the destination. Publish executors receive the
planner-frozen request and may mutate only the one planned target surface for
the one `publish-node-id` after artifact metadata conformance succeeds.

### PyPI

Credential posture:

- PyPI uses external trusted publishing with GitHub Actions OIDC; no long-lived
  PyPI API token is a current-scope credential.
- First delivery includes live official PyPI publication. The PyPI trusted
  publisher must be configured for repository `hcoona/three`, workflow filename
  `release-orchestrate.yml`, and environment `pypi`.

Required publish topology:

- The `pypi/pypi` target requires the active split PyPI publish job in
  `release-orchestrate.yml`.
- The OIDC token request and upload command run in the `publish-python` job under
  the `pypi` environment.
- If an active official PyPI node is routed through a deleted publish workflow or
  a non-`pypi` environment, the control plane or executor must fail closed before
  upload. That is a topology/configuration error, not an alternate PyPI publish
  path.

Planner adapter responsibilities:

- Resolve package identity from `[project].name` and serialize the PEP 503
  normalized name.
- Use Python packaging normalized version identity for remote comparison.
- Use the PyPI JSON API to observe existing release files and their SHA-256
  digests for the resolved project and version.
- Project exact current-scope final distribution filenames from metadata without
  invoking the build backend or hashing generated outputs. Current scope is one
  wheel plus an optional sdist from one variant; extra distribution members are
  out of scope unless the descriptor and plan model them explicitly.

Publish executor responsibilities:

- Request the PyPI trusted-publishing credential only in the orchestrator-hosted live
  publish job, shortly before upload.
- Upload only the planner-frozen wheel and optional sdist members under the exact
  planner-frozen filenames.
- Emit one `publish-result.json` for the planned `publish-node-id` after
  successful upload, including the uploaded distribution filenames and a PyPI
  project or release URL as registry evidence.

Identity conformance:

- Before requesting or using the live upload credential, read wheel `METADATA`
  and sdist `PKG-INFO` from the concrete files that will be uploaded.
- Compare normalized package name and version to the planner-frozen
  `resolved-publish-identity`.
- Fail closed on unreadable metadata, ambiguous normalization, extra planned
  members, missing planned members, or any name/version mismatch.

Live mutation boundary:

- The executor may create the planned PyPI release files only through the
  supported PyPI upload flow. It must not delete files, replace files, reconcile
  target-side conflicts, create alternate project names, or treat target-side
  file presence as a fresh skip decision.
- First-delivery PyPI readiness failures, including missing live enablement or
  missing/mismatched trusted publisher setup, surface as live readiness or
  publish failures described in Sections 8 and 9 rather than as planner remote
  observation results.
- Official PyPI OIDC canary run
  [25522559257](https://github.com/hcoona/three/actions/runs/25522559257)
  reached orchestrator-hosted PyPI trusted publishing and was rejected upstream because
  PyPI does not allow the planned local-version identifier
  `1.0.0b256+gf0e0c47` on public PyPI. Classify that run as a workflow failure,
  not a successful publication: the PyPI project still returned 404 after the
  run, while GitHub tag/release/proof side effects had already been created.
  Treat the failure as positive evidence that the official orchestrator-hosted OIDC
  path reached PyPI, then keep the official non-public-ref guard described in
  Section 4. The break-glass canary override may intentionally reproduce this
  upstream rejection for dedicated `hcoona-release-smoke-*` projects from a development ref until a
  true public-ref canary is used; do not change Python version normalization in
  response to this canary result.
- The official Python smoke full-success PyPI acceptance run is deferred until
  all other validation is complete and these workflow changes are merged to
  `main`. After merge, run it from a proper NBGV public release ref so PyPI sees
  a public-release version rather than a development-ref local version. The
  earlier break-glass runs are OIDC path evidence only, and the passed buddy
  Python smoke does not substitute for final official PyPI publish success.

### npmjs

Credential posture:

- npmjs uses npm trusted publishing with GitHub Actions OIDC for packages whose
  npmjs trusted publisher is configured. No long-lived npm automation token is a
  current-scope credential for npmjs live official publication.
- npm trusted publishing is package-scoped on npmjs.com; package owner-side
  enablement is required before the live official target is allowed to run.

Required publish topology:

- The `npm/npmjs` target requires the active split npmjs publish job in
  `release-orchestrate.yml`.
- npmjs validates the direct caller workflow filename and optional environment
  configured on npmjs.com for `workflow_call`. Configure workflow filename
  `official.yml` and environment `npmjs` for active official publication.
- The concrete `npm publish` command and OIDC token request run in the
  `publish-node-npmjs` job under the `npmjs` environment. The `npmjs-gate`
  environment is a human approval gate only and is not the npm trusted-publisher
  environment.

Planner adapter responsibilities:

- Resolve the package identity from an explicit descriptor target projection
  override when present; otherwise use `package.json` `name`.
- Freeze the final packed tarball filename in
  `projection.final-distribution-filenames-by-artifact-id` for immutable replay
  comparison.
- When the planner can compute the final tarball bytes from current build
  metadata, freeze algorithm-qualified comparable digests, including SHA-512, in
  `projection.final-distribution-digests-by-artifact-id`; npmjs exact replay may
  only skip when at least one shared planned/registry digest algorithm matches.
  A legacy planned SHA-256 alone is not comparable with normal npm
  `dist.integrity` SHA-512 evidence.
- Require current-scope npmjs package names to match the produced package
  identity. Do not project an unscoped npmjs package into an owner-scoped GitHub
  Packages name unless a target-specific artifact or transform receipt explicitly
  models the rewritten package contents, digest, and metadata.
- Use npm package metadata, such as `npm view --json`, to observe the exact
  package version and algorithm-qualified digest evidence from `dist.integrity`.

Publish executor responsibilities:

- Publish the receipted tarball to npmjs using trusted publishing from the
  orchestrator-hosted `publish-node-npmjs` job in `release-orchestrate.yml`.
- Verify the tarball's `package/package.json` name and version before upload.
- Preserve the tarball digest represented by the build receipt; do not rewrite
  `package.json`, scope, version, provenance configuration, or packed contents in
  the publish job.

Identity conformance:

- The upload identity is the final npm package name and version inside the
  tarball, after applying only the planner-frozen descriptor override rules.
- The executor must fail closed if the tarball package name or version differs
  from `resolved-publish-identity`.

Live mutation boundary:

- The executor may create exactly the planned npmjs package version. It must not
  unpublish, deprecate, dist-tag repair, publish under an alternate scoped name,
  or retry with token-based authentication after OIDC failure.
- npmjs target-side conflicts after planning are hard publish failures with
  registry evidence, not reasons to recompute disposition.

### NuGet.org

Credential posture:

- NuGet.org registry publication is deferred in the active split topology. The
  checked-in descriptors and target catalog must not declare `nuget/nuget-org`
  or `nuget/github-packages` as releasable targets until an active dotnet/NuGet
  build and publish path is restored.
- No long-lived NuGet API key is a current-scope credential. NuGet trusted
  publishing remains the expected future credential posture, but future support
  must add the workflow path and tests before reintroducing NuGet target
  instances.

Required publish topology:

- No active NuGet registry publish topology is available. `release-orchestrate.yml`
  currently hosts PyPI, npmjs, RubyGems.org, GitHub Release, and supported
  GitHub Packages paths only.
- A successor NuGet implementation must choose and verify the concrete topology
  before adding target instances or descriptor targets; do not model NuGet
  targets as releasable ahead of that workflow support.

Planner adapter responsibilities:

- Resolve package identity from evaluated `PackageId`; do not fall back to
  `AssemblyName`, project file name, package file name, or directory name.
- Use NuGet normalized version identity for remote version comparison.
- For `.nupkg`, use the NuGet V3 service index plus PackageBaseAddress and
  registration resources for public NuGet.org observation where possible.
- For `.snupkg`, use only a documented and tested symbol-package observation path
  before enabling live NuGet.org publication. The ordinary package content API
  documents `.nupkg` content, while symbol packages are published to NuGet's
  symbol-server path and can undergo asynchronous validation.

Publish executor responsibilities:

- Current active workflows have no NuGet publish executor invocation. Existing
  NuGet metadata and executor helpers are future-facing test assets, not active
  target availability.
- A future implementation must push exactly the planned `.nupkg` and, only when
  modeled and enabled, the planned `.snupkg` member.
- Future publish failures must report the NuGet response if a target-side
  conflict, validation delay, or symbol-package problem occurs; do not attempt
  reconciliation.

Identity conformance:

- Read the `.nupkg` package metadata and compare `PackageId` plus normalized
  version to `resolved-publish-identity`.
- If a `.snupkg` member is modeled, verify that it corresponds to the same
  planner-frozen package identity and version before upload.

Live mutation boundary:

- Current active workflows may not create NuGet.org or GitHub Packages NuGet
  package versions. Descriptors may keep `.nupkg` and `.snupkg` artifacts for
  GitHub Release evidence only.
- Future NuGet support must not push an unmodeled `.snupkg`, substitute a
  different package source, retry with a long-lived API key, delete package
  versions, or derive package identity from file names.

### RubyGems.org

Credential posture:

- RubyGems.org uses trusted publishing with GitHub Actions OIDC; no long-lived
  RubyGems API key is a current-scope credential for RubyGems.org live official
  publication.
- Trusted publisher setup is gem-owner-side and may be configured for an existing
  gem or pending publisher before first gem creation.

Required publish topology:

- RubyGems.org uses `external-oidc-reusable-workflow` where configured.
- For the current same-repository reusable publish topology, RubyGems.org trusts
  workflow filename `release-orchestrate.yml` with repository `hcoona/three` and
  environment `rubygems`; separate workflow-repository owner/name fields remain
  blank unless a future cross-repository reusable workflow is introduced.
- The active permission boundary is the `publish-ruby-rubygems` job in
  `release-orchestrate.yml`. That job requests the RubyGems.org token under the
  `rubygems` environment and declares `id-token: write`; unrelated jobs must
  not.

Planner adapter responsibilities:

- Resolve package identity from evaluated `Gem::Specification.name`.
- Compare versions with RubyGems `Gem::Version`.
- Resolve release versions through build-system-integrated NBGV for every Ruby
  project in current scope; the gemspec must fail closed when NBGV cannot provide
  `SemVer2` rather than falling back to a static source-tree version.
- Freeze the gem file name in
  `projection.final-distribution-filenames-by-artifact-id` for immutable replay
  comparison.
- When the planner can compute the final gem bytes from current build metadata,
  freeze the comparable SHA-256 in
  `projection.final-distribution-sha256-by-artifact-id`; RubyGems.org exact
  replay may only skip when this planned digest matches RubyGems `sha` evidence.
- Use the RubyGems.org API for version and digest observation.

Publish executor responsibilities:

- Consume only the receipted `.gem` file from the build unit's referenced build
  receipt and publish it to RubyGems.org from the reusable workflow identity.
  It must not rebuild the gem or rediscover package artifacts.
- Request the RubyGems.org trusted-publishing credential only inside the
  reusable-hosted publish job selected by topology.
- Emit registry evidence containing at least the gem name, version, and
  RubyGems.org version or gem URL after successful upload.

Identity conformance:

- Read the built gem specification from the concrete `.gem` file and compare its
  name and `Gem::Version` to the planner-frozen `resolved-publish-identity`.
- Fail closed if gemspec evaluation, metadata reading, normalization, or
  comparison is ambiguous.

Live mutation boundary:

- The executor may create exactly the planned RubyGems.org gem version. It must
  not yank versions, push under an alternate gem name, rewrite the gemspec in the
  publish job, or fall back to API-key authentication after OIDC failure.

### GitHub Release

Credential posture:

- GitHub Release uses `GITHUB_TOKEN`; it has no external OIDC trusted-publisher
  policy and no registry-side workflow filename beyond the stable workflow files
  in Section 3. GitHub Release asset attestation still uses GitHub Actions OIDC
  to obtain the Sigstore signing certificate for
  `actions/attest-build-provenance`; that token is for the attestation proof
  path, not for GitHub Release mutation authority.

Required publish topology:

- GitHub Release uses the `github-token` topology. Current routing may host the
  command in the reusable publish job, but the authority remains GitHub's token
  for the repository.

Planner adapter responsibilities:

- Query releases by the frozen `release-tag`.
- Normalize release state as `prerelease` or `release`.
- Normalize the asset set by asset name, label, digest, and size, then compare it
  to the planner-frozen `projection.asset-names-by-artifact-id` and
  `projection.asset-labels-by-artifact-id` maps plus admissible GitHub Artifact
  Attestation-backed content proof for each planned `artifact-id`, verified
  against the publish node's frozen `attestation.signer-workflow`.
- Classify exact matches, same-tag prerelease-to-release
  `partial-authoritative` promotions, generic same-tag partials, and same-tag
  conflicts according to the replay matrix.
- Treat missing, unavailable, or mismatched remote asset digest/size evidence as
  not exact-satisfied. A same-name asset with the right label but unproved content
  equivalence may enter the same-tag partial/conflict matrix, but it is never a
  skip condition.

Publish executor responsibilities:

- `create-only`: create the release for the already verified tag and upload the
  exact planned asset set under the planner-frozen asset names.
- `overwrite-mutable`: converge the mutable prerelease to the frozen `buddy`
  intent when the planner authorized normalized `request-flags.force`.
- `replace-authoritative`: for the planner-authorized official same-tag
  prerelease-to-release promotion path, converge the existing prerelease's asset
  names, labels, sizes, and SHA-256 digests to the producer-bound official intent before
  clearing prerelease.
- Active GitHub Release publication emits `github-release-result.json` in the
  run/attempt-scoped `release-github-release-result-v1-<run-id>-<attempt>-...`
  artifact instead of package-registry `publish-result.json`.
- The `release-orchestrate.yml` attestation jobs generate GitHub Actions build
  provenance for produced asset bytes and verify that those provenance jobs
  completed when attestation is enabled.
- The `.github/workflows/release-create-github-release.yml` upload path verifies
  the staged GitHub Release asset attestations before release mutation, generates
  `github-release-asset-proof` sidecars after upload, persists those proofs on
  the release, and verifies the live uploaded assets against the persisted proof
  evidence before finalization.
- Existing GitHub Releases are accepted only when live asset names, labels,
  sizes, and SHA-256 digests exactly match the planned assets, unless the
  planner serialized `publish-mode: replace-authoritative` for an official
  same-tag prerelease-to-release promotion. In that path, mismatched, missing, or
  extra live assets are deleted or uploaded until the remote asset set matches
  the frozen official intent, and only then may the executor promote the release
  state.

Identity conformance:

- The GitHub Release identity is the planner-frozen `release-tag`, release state,
  asset names, asset labels, and content-equivalent asset evidence. It is not a
  package metadata identity.
- The executor must not derive alternate release asset names from bundle-relative
  paths, produced filenames, or executor-local packaging output.

Live mutation boundary:

- The executor may create or converge only the planned release and planned assets
  for the already verified tag. It may delete and recreate assets only when the
  plan mode authorizes an overwrite or authoritative replacement.
- It must not use release asset presence as a fresh skip decision and must never
  retarget tags itself; tag creation, verification, and any reviewed
  `force_update_tag=true` retarget remain the `ensure-tag`
  control-plane responsibility.

### GitHub Packages

Credential posture:

- GitHub Packages uses `GITHUB_TOKEN` with GitHub package permissions. It has no
  external OIDC trusted-publisher policy, no external registry workflow filename,
  and no owner-side trusted publisher setup.

Required publish topology:

- GitHub Packages package-registry targets use the `github-token` topology.
- The package publish command may be reusable-hosted, but it must use the
  materialized request for the planned GitHub Packages target and must not borrow
  topology or credentials from npmjs, PyPI, NuGet.org, or RubyGems.org.

Planner adapter responsibilities:

- Use GitHub-hosted package reads with least-privilege `GITHUB_TOKEN` only when
  public observation is insufficient.
- Preserve the source ecosystem identity rules for package metadata:
  NuGet `PackageId`, npm descriptor override or `package.json` `name`, and
  evaluated RubyGems gemspec name.
- Model GitHub Packages target projection explicitly. Do not infer npm owner
  scoping, package renames, or package-content rewrites from the destination host.
- For GitHub Packages NuGet, treat `.snupkg` support as a verification point. If
  the implementation cannot publish and observe `.snupkg` members with the same
  fail-closed replay guarantees as `.nupkg`, current scope must not require live
  GitHub Packages `.snupkg` publication.

Publish executor responsibilities:

- Configure the matching ecosystem client for GitHub Packages using
  `GITHUB_TOKEN` and publish exactly the modeled package-registry members.
- Verify package metadata from the concrete artifact before upload using the same
  family equivalence rules as the external registry for that ecosystem.
- For first-delivery `hjg-pngcs`, do not publish GitHub Packages NuGet package
  members. Keep `.nupkg` and `.snupkg` artifacts as GitHub Release evidence
  unless a real package-registry path is explicitly brought into scope.

Identity conformance:

- The executor must compare the concrete package metadata to the
  planner-frozen `resolved-publish-identity` and target projection, including
  final distribution filenames for immutable registry targets. A GitHub Packages
  host requirement does not permit unmodeled package renaming.
- The projected `npm/github-packages` path for `hexo-renderer-asciidoc` is
  explicitly modeled with a scoped buddy identity
  `@hcoona/hexo-renderer-asciidoc`, while its official npmjs identity remains
  the unscoped `hexo-renderer-asciidoc`. Live validation of that non-typical
  unscoped-official/scoped-buddy npm shape uses
  `hcoona-release-smoke-npm-dual`, not the real Hexo package.

Live mutation boundary:

- The executor may create only the planned GitHub Packages package version and
  planned members. It must not delete package versions, rewrite package contents,
  publish unmodeled sidecar files, or route through external OIDC credentials.
- Target-side conflicts after planning are hard publish failures with GitHub
  Packages evidence, not reasons to recompute disposition.

## 8. Permissions and Environment

Permissions are a job-level least-privilege contract. Release workflows must not
use a broad workflow-level write token such as workflow-level
`contents: write`, `packages: write`, or `id-token: write`. A workflow-level
baseline may be read-only or empty, but every write or OIDC capability must be
declared on the exact job path that needs it. Jobs that call reusable workflows
must also remember that GitHub can maintain or reduce `GITHUB_TOKEN`
permissions across the call boundary, but the called workflow cannot elevate
permissions that the caller job did not grant.

Minimum job-level permission intents are:

| Job group                                      | Minimum permission intent                                                                                                                                                                                                                                                                                        |
| ---------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Authorization, report, skip, and pure planning | `contents: read` only, unless the job has a narrower documented read need.                                                                                                                                                                                                                                       |
| Immutable proof lookup                         | Add `actions: read` only to the job that downloads or lists proof artifacts.                                                                                                                                                                                                                                     |
| Planner GitHub-hosted remote observation       | Add only the required read scopes, such as `packages: read`, to the planning job that performs that read.                                                                                                                                                                                                        |
| Tag verification only                          | `contents: read`; do not grant tag write permission when a standalone tag check expects all required tags to already exist. The `.github/workflows/release-create-github-release.yml` `create-release` job verifies the preexisting tag before GitHub Release mutation, but it does not create or retarget tags. |
| Tag creation or retargeting                    | `contents: write`, scoped only to the active `.github/workflows/release-orchestrate.yml` `ensure-tag` job when `workflow_release_control.py ensure-tags` may create missing active release tags or execute the reviewed `force_update_tag=true` retarget path.                                                   |
| GitHub Release publication                     | `contents: write`, `actions: read`, and `attestations: read`, scoped to the active `.github/workflows/release-create-github-release.yml` `create-release` job that verifies the preexisting tag, creates or converges GitHub Release assets, and verifies post-upload asset attestations.                        |
| Attestation gates                              | `attestations: write` and attestation-scoped `id-token: write`, scoped only to the separate `release-orchestrate.yml` attestation jobs that create provenance.                                                                                                                                                   |
| GitHub Packages publication                    | `packages: write`, scoped only to the matching GitHub Packages publish job; add `contents: read` if required.                                                                                                                                                                                                    |
| External trusted publishing with GitHub OIDC   | `id-token: write`, scoped only according to the external registry topology rules below; do not combine with unrelated write jobs.                                                                                                                                                                                |
| External OIDC registry publication artifacts   | Add only the read permissions needed to download the planned artifacts and receipts before minting external registry credentials.                                                                                                                                                                                |

`id-token: write` placement is topology-specific:

- `external-oidc-entry-workflow`: superseded/historical only. No current active
  target uses this topology after NuGet registry targets were deferred. It must
  not be used to justify entry-workflow OIDC grants for active PyPI, npmjs, or
  RubyGems.org publication; those registries follow the caller- or
  reusable-workflow rules below. Planning, build, tag, report, skip, GitHub
  Release, and GitHub Packages jobs must not receive an external-registry OIDC
  grant for an unrelated publish node.
- `external-oidc-caller-workflow`: grant the OIDC capability along the active
  caller-workflow-bound path only. In current scope, npmjs uses this topology
  because npm validates the direct `workflow_call` caller configured in the
  trusted publisher: `.github/workflows/official.yml` with environment `npmjs`.
  Grant `id-token: write` only to the `official.yml` orchestrate caller job that
  must pass the OIDC capability onward and to the `release-orchestrate.yml`
  `publish-node-npmjs` job that requests the OIDC token. This is not a
  workflow-wide grant; unrelated matrix entries and unrelated jobs must not
  receive it. Buddy does not live-publish npmjs.
- `external-oidc-reusable-workflow`: the entry workflow caller job may grant
  `id-token: write` only as the reusable-call upper bound required for documented
  called OIDC publish/provenance jobs; that caller grant must not request or mint
  a registry token. Actual registry-token minting remains restricted to the
  active reusable-orchestrator publish job selected by the plan. In the active
  split topology, PyPI uses `publish-python` under `pypi`, and RubyGems.org uses
  `publish-ruby-rubygems` under `rubygems`; both token-requesting jobs live in
  `release-orchestrate.yml`. npmjs is intentionally excluded from this reusable
  topology because its trusted publisher is caller-workflow-bound. Unrelated
  orchestration jobs, unrelated matrix entries, planning, build, tag, report,
  skip, GitHub Release, and GitHub Packages jobs must not receive an actual
  external-registry OIDC token-minting grant.
- `github-token`: GitHub Packages paths do not grant `id-token: write` and use
  `GITHUB_TOKEN` only, with `packages: write` scoped to the live mutation job
  that needs that authority. GitHub Release paths use `GITHUB_TOKEN` with
  `contents: write` in the active `.github/workflows/release-orchestrate.yml`
  `ensure-tag` job for tag creation or reviewed retargeting when active GitHub
  Release publish nodes exist, and with `contents: write`, `actions: read`, and
  `attestations: read` in the active
  `.github/workflows/release-create-github-release.yml` `create-release` job for
  preexisting-tag verification, release mutation, artifact access, and
  post-upload proof generation. The `create-release` job must not create or
  retarget tags. The separate `release-orchestrate.yml` language attestation jobs
  grant
  `attestations: write` plus attestation-scoped `id-token: write` solely to run
  `actions/attest-build-provenance` for release assets before GitHub Release
  upload. GitHub Release mutation jobs must not carry attestation write or OIDC
  permissions, and attestation jobs must not mutate releases or mint external
  registry credentials.

Planner-time remote observation must never run in a publish-credential context.
Planner adapters may use public registry reads and the least-privilege
`GITHUB_TOKEN` read permissions described above, but they must not:

- request external OIDC tokens;
- run inside publish jobs solely to obtain registry trust;
- access approval-gated active registry environment secrets;
- use long-lived publish credentials; or
- turn trusted-publisher readiness probing into planner remote observation.

`official` repository authorization and protected-environment approval are
distinct gates. The entry workflow must first verify that the actor has
`maintain+` repository permission for `official`, while `buddy` continues to
require `write+`. Passing that authorization check does not approve deployment:
for `official`, each live side-effect job still waits for the protected
active registry environment before the selected run can mutate PyPI, npmjs, or
RubyGems. Current-scope `buddy` live jobs must not attach a generic protected
`release` environment as an approval gate.

For `official`, active registry environments attach directly to the jobs that can
perform live registry side effects. PyPI uses `pypi`, npmjs uses `npmjs-gate` for
human approval and `npmjs` for OIDC token scoping, and RubyGems uses `rubygems`.
No current active publish path uses `environment: release`.

External trusted-publisher policies must be configured for the
topology-specific workflow identity that each registry validates and for the
matching active registry environment. The operational setup checklist in Section
9 remains the owner-side source of truth for those policies, while Section 10 defines the
acceptance evidence that proves the resulting jobs, permissions, environments,
and receipts behaved as intended.

## 9. External Setup and Readiness

External account setup is an operations readiness responsibility, not a planner
or workflow-automation responsibility. A single senior implementer may own the
handoff, but that ownership means verifying the repository environment, registry
owner-side trusted-publisher configuration, and live-enable controls before
declaring official live publication ready. It does not mean teaching the planner
to create, repair, or probe external account configuration.

Planner-time remote observation remains limited to the registry state reads in
Section 7 and the permission rules in Section 8. It must never use publish
credentials, request external OIDC tokens, enter the approval-gated `release`
environment, or run a synthetic trusted-publishing credential exchange merely to
test whether a registry account has been configured correctly. Trusted-publisher
misconfiguration is detected by readiness review before enablement or by the
real live publish job when that target is intentionally enabled.

For GitHub Packages, a package API 404 during remote observation is normalized
to `absent`. This keeps first-publication handling simple: the planner does not
try to distinguish a genuinely missing package from a package that is invisible
to the observation token. The publish executor remains the final authority for
permissions and conflicts; any non-404 GitHub Packages observation error still
fails hard before planning proceeds.

Before any official live external publication is declared ready, the implementer
must verify this checklist for every enabled target surface:

| Surface                         | Applies when                                                                                            | Live-ready configuration                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| ------------------------------- | ------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| GitHub/registry environments    | Any official job can create tags, mutate GitHub Release, mutate GitHub Packages, or publish externally. | Active environments are `github-release`, `pypi`, `npmjs-gate`, `npmjs`, and `rubygems` as applicable, with required reviewers configured where the active workflow expects human approval, prevent self-review enabled, deployment branch or tag restrictions limited to trusted release refs, and native admin bypass left to repository policy. No active generic `release` environment is assumed.                                                                                                                                     |
| PyPI trusted publishing         | Acceptance-blocking for each first-delivery project whose `pypi/pypi` official target is enabled.       | Project owner-side trusted publisher, or pending publisher before first project creation, for the exact PyPI project name. Configure owner `hcoona`, repository `three`, workflow filename `release-orchestrate.yml` with no `.github/workflows/` path, and environment `pypi`. Do not configure `official.yml` or deleted publish workflows as the PyPI publisher.                                                                                                                                                                        |
| npmjs trusted publishing        | Each enabled `npm/npmjs` official target.                                                               | Package owner-side trusted publisher for owner `hcoona`, repository `three`, workflow filename `official.yml` with no `.github/workflows/` path, and environment `npmjs`. The OIDC-token-requesting npm publish job is `publish-node-npmjs` in the reusable orchestrator, but npm validates the direct caller workflow name for `workflow_call`; do not configure deleted publish workflows as the npmjs trusted publisher.                                                                                                                |
| RubyGems.org trusted publishing | Each enabled `rubygems/rubygems-org` official target.                                                   | Gem owner-side trusted publisher, or pending trusted publisher before first gem creation, for repository `hcoona/three`, workflow filename `release-orchestrate.yml`, and environment `rubygems`. The workflow implementation must also satisfy the split-topology OIDC permission checks from Section 8.                                                                                                                                                                                                                                  |
| NuGet.org trusted publishing    | Deferred; no active `nuget/nuget-org` official target is modeled in the split topology.                 | Do not configure NuGet.org trusted publishing for this release path yet. Future NuGet support must first restore a dotnet/NuGet workflow path, reintroduce target instances and descriptor targets, and then document the exact trusted-publisher workflow filename and environment.                                                                                                                                                                                                                                                       |
| GitHub Release                  | Any enabled `github-release/public` target.                                                             | No external OIDC trusted-publisher policy. Use the active `github-release` environment, GitHub repository permissions, `contents: write` on `release-orchestrate.yml` `ensure-tag` for active tag creation or reviewed retargeting, `contents: write`, `actions: read`, and `attestations: read` on the exact `release-create-github-release.yml` mutation/proof-generation job that verifies the preexisting tag, keep `attestations: write` and `id-token: write` on the separate attestation jobs, and preserve active-only tag gating. |
| GitHub Packages                 | Any enabled GitHub Packages target.                                                                     | No external OIDC trusted-publisher policy and no owner-side trusted-publisher setup. Use `GITHUB_TOKEN`, the exact required `packages: write` permission, and any required package read permission.                                                                                                                                                                                                                                                                                                                                        |

This checklist is a readiness gate for live official publication, not an
acceptance evidence matrix. Section 10 remains the owner of detailed acceptance
evidence rows. Section 9 only defines what must be configured and verified before
an enabled target is treated as live-publication ready.

This checklist must not delay implementation of no-side-effect, GitHub Release,
or GitHub Packages paths. Official external-registry publication remains disabled
for any target whose owner-side trusted-publisher entry, registry-specific GitHub Actions
environment policy, workflow OIDC permission chain, or explicit live-enable token
has not been verified by the implementer.

First delivery uses an explicit non-secret repository variable,
`THREE_RELEASE_ENABLED_EXTERNAL_OIDC_TARGETS`, as the control-plane live-enable
allowlist for official live publication to external OIDC registries. The value is
a comma or newline separated list of package-scoped enablement tokens. Each token
has this exact case-sensitive shape:

```text
<target-instance-ref>#<project-id>#<planner-frozen-package-name>
```

Examples include
`npm/npmjs#hcoona-release-smoke-npm-dual#hcoona-release-smoke-npm-dual` and
`rubygems/rubygems-org#asciidoctor-latexmath#asciidoctor-latexmath`. A missing
or empty value enables none. First delivery does not define a wildcard token;
enabling a whole external registry target would be too coarse because
trusted-publisher readiness is package-owner-side. This allowlist applies only
to active `official` publish nodes whose target-instance snapshot has
`credential-posture: oidc`; GitHub Release and GitHub Packages nodes are not
gated by this variable. NuGet tokens are intentionally absent until NuGet target
instances are restored.

Allowlist normalization is closed:

1. split on comma and newline;
2. trim ASCII whitespace;
3. drop empty entries;
4. de-duplicate exact tokens and sort lexicographically for diagnostics;
5. reject any token whose target-instance ref is not a known catalog ref with
   `credential-posture: oidc`;
6. reject `*`, mixed wildcard forms, malformed tokens, and tokens with empty
   components.

After the plan and execution sets are computed in memory, but before either
artifact is published, before any protected environment is requested, and before
any live side-effect job is scheduled, the control plane must first reject any
active official external OIDC publish node whose frozen `publish-topology` cannot
be scheduled by the current workflow topology. `REQ_EXTERNAL_TOPOLOGY_BLOCKED` is
reserved for genuinely unsupported topology values or catalog combinations. It
must not be emitted for a normal active official `pypi/pypi` node, because PyPI is
supported through the `release-orchestrate.yml` hosted path in first delivery.

After that topology gate passes, PyPI live publication may still fail for real
readiness, configuration, credential, upload, or conformance reasons: for
example a missing live-enable token, a missing or mismatched PyPI publisher,
failure to mint a trusted-publishing credential, package metadata mismatch, a
target-side conflict, or an upload failure. Those failures must surface through
`REQ_EXTERNAL_TARGET_DISABLED`, the hosted publish job conclusion, missing
positive publish receipt, and executor or registry evidence as appropriate; they
must not be recast as `REQ_EXTERNAL_TOPOLOGY_BLOCKED`.

For the remaining external OIDC nodes, the control plane must compute the
required enablement token from the frozen `target-instance-snapshot-id`,
`project-id`, and `resolved-publish-identity.package-name`. If any selected node
is not enabled, the run fails before tag or publish side effects, writes
`planner-diagnostics.json` with `REQ_EXTERNAL_TARGET_DISABLED`, publishes no plan
or execution-set artifacts for that attempt, and emits no partial live-publish
receipts. The diagnostic `details` must include the required enablement token,
target-instance ref, project id, and resolved publish identity so the final
report is self-contained even though no plan artifact is published. These are
fail-closed readiness gates, not planner-owned remote-observation rules and not
skip conditions.

No-side-effect runs skip this environment gate entirely:

- dry-run or validation-only;
- zero-target;
- all selected publish nodes are `skip-satisfied`.

### Tag Orchestration

`ensure-tag` is a control-plane job, not an executor.

Implementation sequence:

1. Read the active GitHub Release publish nodes from the frozen plan using
   `active-github-release-publish-node-ids`.
2. Compute the distinct required `release-tag` set for those active nodes.
3. Query every existing tag in the active required set before creating any
   missing tag.
4. If any existing active required tag does not peel to the selected
   `commit-sha`, fail without creating tags.
5. After the active precheck passes, create every missing active required tag at
   the selected commit.

Newly created release tags are lightweight tags that point directly at the
selected commit. Existing annotated tags are accepted in the non-force path only
when peeling the tag object resolves to the selected commit. The non-force path
must never retarget an existing tag and must never treat a tag object that points
elsewhere as satisfying the selected commit requirement. When
`force_update_tag=true`, the active workflow may retarget the release tag through
the reviewed force path; buddy mutable-prerelease overwrite remains authorized
only by planner-owned `request-flags.force` policy and does not bypass this tag
identity check.

`ensure-tag` must not run when there are no active GitHub Release publish nodes.
That includes zero-target selections and selections where every GitHub Release
node is `skip-satisfied`. Skip-satisfied nodes are proven by planner observation
and synthetic skip-result receipts, not by protected write-capable tag
verification.

When the job succeeds, it must emit exactly one `tag-result.json` covering every
distinct active required release tag. If any existing tag fails the
peel-to-commit precheck, the job emits no positive tag result and creates no
missing tags.

### First-Delivery Author-Time Input Project Set

The first delivery must generate project descriptors for the full confirmed
requirements scope: every project rooted under `src/public/`, plus the private
apps `src/private/app/qidian-novel-downloader/` and
`src/private/app/vscode-copilot-telegram-hook/`. Each generated descriptor is
part of the implementation handoff, must declare both `buddy` and `official`,
and may use a zero-target profile where the requirements baseline allows it.
The target baseline below is frozen design input for first implementation, not
an implementer-owned selection list. A descriptor may refine internal artifact
handles and variant names, but it must preserve the profile target choice in this
table unless a successor design changes the baseline.

| Coverage category                         | Project                                             | Descriptor root                                                     | Primary manifest or build entry point                          | Frozen first-delivery target baseline                                                                                                                                                                                                                                                                    |
| ----------------------------------------- | --------------------------------------------------- | ------------------------------------------------------------------- | -------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| C# packable library                       | `CircularList`                                      | `src/public/lib/CircularList/`                                      | `CircularList.csproj`                                          | `buddy`: GitHub Release with `.nupkg` and `.snupkg`. `official`: GitHub Release with `.nupkg` and `.snupkg`. No package-registry target in first delivery.                                                                                                                                               |
| C# packable library                       | `hjg-pngcs`                                         | `src/public/lib/Hjg.Pngcs/`                                         | `Hjg.Pngcs.csproj`                                             | `buddy`: GitHub Release with `.nupkg` and `.snupkg`. `official`: GitHub Release with `.nupkg` and `.snupkg`. Live package-registry acceptance now uses dedicated smoke projects instead of this real package.                                                                                            |
| C# packable library                       | `Memoization`                                       | `src/public/lib/Memoization/`                                       | `Memoization.csproj`                                           | `buddy`: GitHub Release with `.nupkg` and `.snupkg`. `official`: GitHub Release with `.nupkg` and `.snupkg`. No package-registry target in first delivery.                                                                                                                                               |
| C# public helper or generator             | `Memoization.Generators`                            | `src/public/lib/Memoization.Generators/`                            | `Memoization.Generators.csproj`                                | `buddy`: zero-target. `official`: zero-target. The descriptor exists for confirmed-scope coverage only because the helper has no current publish target.                                                                                                                                                 |
| C# packable library                       | `MicrosoftExtensions.Logging.MSTest`                | `src/public/lib/MicrosoftExtensions.Logging.MSTest/`                | `MicrosoftExtensions.Logging.MSTest.csproj`                    | `buddy`: GitHub Release with `.nupkg` and `.snupkg`. `official`: GitHub Release with `.nupkg` and `.snupkg`. No package-registry target in first delivery.                                                                                                                                               |
| C# packable library                       | `MicrosoftExtensions.Logging.Xunit`                 | `src/public/lib/MicrosoftExtensions.Logging.Xunit/`                 | `MicrosoftExtensions.Logging.Xunit.csproj`                     | `buddy`: GitHub Release with `.nupkg` and `.snupkg`. `official`: GitHub Release with `.nupkg` and `.snupkg`. No package-registry target in first delivery.                                                                                                                                               |
| C# packable library                       | `MicrosoftExtensions.Options.DedupChangeExtensions` | `src/public/lib/MicrosoftExtensions.Options.DedupChangeExtensions/` | `MicrosoftExtensions.Options.DedupChangeExtensions.csproj`     | `buddy`: GitHub Release with `.nupkg` and `.snupkg`. `official`: GitHub Release with `.nupkg` and `.snupkg`. No package-registry target in first delivery.                                                                                                                                               |
| C# packable library                       | `PhiFailureDetector`                                | `src/public/lib/PhiFailureDetector/`                                | `PhiFailureDetector.csproj`                                    | `buddy`: GitHub Release with `.nupkg` and `.snupkg`. `official`: GitHub Release with `.nupkg` and `.snupkg`. No package-registry target in first delivery.                                                                                                                                               |
| C# packable library                       | `WebHdfs.Extensions.FileProviders`                  | `src/public/lib/WebHdfs.Extensions.FileProviders/`                  | `WebHdfs.Extensions.FileProviders.csproj`                      | `buddy`: GitHub Release with `.nupkg` and `.snupkg`. `official`: GitHub Release with `.nupkg` and `.snupkg`. No package-registry target in first delivery.                                                                                                                                               |
| C# public app installer                   | `ImageOcclusionEditor`                              | `src/public/app/ImageOcclusionEditor/`                              | `ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj` | `buddy`: GitHub Release with the Inno Setup installer. `official`: GitHub Release with the Inno Setup installer. The WinUI publish directory remains executor-internal input, not a descriptor artifact.                                                                                                 |
| C# public app binary                      | `PhiFailureDetector.Console`                        | `src/public/app/PhiFailureDetector.Console/`                        | `PhiFailureDetector.ConsoleApp.csproj`                         | `buddy`: zero-target. `official`: zero-target. First-delivery descriptor coverage is required, but live binary publication is deferred until the app leaves the old `Microsoft.Build.Artifacts` release shape.                                                                                           |
| C# private app binary                     | `qidian-novel-downloader`                           | `src/private/app/qidian-novel-downloader/`                          | `QidianNovelDownloader.csproj`                                 | `buddy`: GitHub Release with Windows, Linux, and macOS standalone executables. `official`: GitHub Release with the same executable set.                                                                                                                                                                  |
| C# private app binary                     | `vscode-copilot-telegram-hook`                      | `src/private/app/vscode-copilot-telegram-hook/`                     | `VSCodeCopilotTelegramHook.csproj`                             | `buddy`: GitHub Release with Windows and Linux standalone executables. `official`: GitHub Release with the same executable set.                                                                                                                                                                          |
| Python special version authority          | `nbgv-python`                                       | `src/public/lib/nbgv-python/`                                       | `pyproject.toml`                                               | `buddy`: GitHub Release with wheel and sdist. `official`: GitHub Release with wheel and sdist. Live PyPI acceptance now uses the dedicated `hcoona-release-smoke-pypi` project.                                                                                                                          |
| Legacy generic smoke package              | `hcoona-release-smoke`                              | `src/public/lib/hcoona-release-smoke/`                              | `pyproject.toml`                                               | Historical canary package only. It is not the future live-acceptance target and has no active release targets.                                                                                                                                                                                           |
| PyPI release smoke package                | `hcoona-release-smoke-pypi`                         | `src/public/lib/hcoona-release-smoke-pypi/`                         | `pyproject.toml`                                               | `buddy`: GitHub Release with wheel and sdist. `official`: GitHub Release plus PyPI with wheel and sdist, using normal build-system NBGV and the orchestrator-hosted PyPI topology.                                                                                                                       |
| Python public app                         | `markdown-hybrid-search-mcp`                        | `src/public/app/markdown-hybrid-search-mcp/`                        | `pyproject.toml`                                               | `buddy`: zero-target. `official`: zero-target. The project metadata is intentionally private/do-not-upload in current scope.                                                                                                                                                                             |
| Node npm package                          | `hexo-renderer-asciidoc`                            | `src/public/lib/hexo-renderer-asciidoc/`                            | `package.json`                                                 | `buddy`: GitHub Release plus GitHub Packages npm using `@hcoona/hexo-renderer-asciidoc`. `official`: GitHub Release plus npmjs using `hexo-renderer-asciidoc`. Live validation of this unscoped-official/scoped-buddy shape uses `hcoona-release-smoke-npm-dual`, not real Hexo.                         |
| Node public package or extension artifact | `steam-account-history-to-csv`                      | `src/public/lib/steam-account-history-to-csv/`                      | `package.json`                                                 | `buddy`: zero-target. `official`: zero-target. The package is `private: true`, and browser-extension archive publication remains outside first-delivery release contracts.                                                                                                                               |
| Ruby gem                                  | `asciidoctor-latexmath`                             | `src/public/lib/asciidoctor-latexmath/`                             | `asciidoctor-latexmath.gemspec`                                | `buddy`: GitHub Release with the `.gem`. `official`: GitHub Release with the `.gem`. Live RubyGems.org acceptance now uses `hcoona-release-smoke-rubygems`.                                                                                                                                              |
| GitHub Release smoke package              | `hcoona-release-smoke-github-release`               | `src/public/lib/hcoona-release-smoke-github-release/`               | `hcoona-release-smoke-github-release.csproj`                   | `buddy` and `official`: GitHub Release with `.nupkg` and `.snupkg`, using a dedicated release tag namespace.                                                                                                                                                                                             |
| NuGet.org release smoke package           | `hcoona-release-smoke-nuget`                        | `src/public/lib/hcoona-release-smoke-nuget/`                        | `hcoona-release-smoke-nuget.csproj`                            | `buddy` and `official`: GitHub Release-only evidence with `.nupkg` and `.snupkg`. NuGet registry publication is deferred because `families.nuget.instances: []`; no GitHub Packages NuGet or NuGet.org smoke target is active until the dotnet/NuGet workflow path and catalog instances are re-enabled. |
| npmjs release smoke package               | `hcoona-release-smoke-npm`                          | `src/public/lib/hcoona-release-smoke-npm/`                          | `package.json`                                                 | `buddy`: GitHub Release plus GitHub Packages npm using `@hcoona/hcoona-release-smoke-npm`. `official`: GitHub Release plus npmjs trusted publishing with the same scoped tarball identity.                                                                                                               |
| Dual npmjs/GitHub Packages smoke package  | `hcoona-release-smoke-npm-dual`                     | `src/public/lib/hcoona-release-smoke-npm-dual/`                     | `package.json`                                                 | `buddy`: GitHub Release plus GitHub Packages npm using `@hcoona/hcoona-release-smoke-npm-dual`. `official`: GitHub Release plus npmjs trusted publishing using unscoped `hcoona-release-smoke-npm-dual`, proving the Hexo-style split identity.                                                          |
| RubyGems.org release smoke package        | `hcoona-release-smoke-rubygems`                     | `src/public/lib/hcoona-release-smoke-rubygems/`                     | `hcoona-release-smoke-rubygems.gemspec`                        | `buddy`: GitHub Release plus GitHub Packages RubyGems using `hcoona-release-smoke-rubygems`. `official`: GitHub Release plus RubyGems.org trusted publishing with the same `.gem`.                                                                                                                       |
| GitHub Packages release smoke package     | `hcoona-release-smoke-github-packages`              | `src/public/lib/hcoona-release-smoke-github-packages/`              | `hcoona-release-smoke-github-packages.csproj`                  | Deferred/unavailable for live package-registry publication. It may retain GitHub Release-only `.nupkg` / `.snupkg` evidence, but GitHub Packages NuGet smoke publication is inactive until a reviewed dotnet/NuGet workflow path and NuGet catalog instance are re-enabled.                              |

This set intentionally excludes first-delivery descriptors only for projects
outside the confirmed requirements scope, such as private WXT or
browser-extension packages not named above, archive-only artifacts outside
`src/public/`, metadata-only artifacts outside `src/public/`,
tool/generator-specific release kinds outside `src/public/`, and multi-wheel or
platform-specific Python wheel layouts. Those cases may be added in later
descriptor migration work after the current release workflow contracts are
implemented and validated.

## 10. Acceptance Traceability

Acceptance evidence must be maintained as a readiness matrix in executable
fixtures or generated CI output. Each acceptance row must trace the scenario to
the descriptor or catalog input, emitted plan, execution-set selector, request or
receipt artifact, external or GitHub registry observation, and final workflow
conclusion where that column applies.

Dedicated public smoke descriptors cover the missing GitHub Release build
artifact shapes for .NET executable binaries, .NET Inno Setup installers, and
WXT browser-extension zips for Chrome, Firefox, and Edge. Python application
smoke coverage is intentionally skipped for now.

The first-delivery PyPI official publish row remains required final acceptance
evidence, but it is intentionally deferred until all other validation is green
and these workflow changes have merged to `main`. Before that point, continue
other validation; prior development-ref PyPI rejections are positive OIDC-path
evidence only, and buddy Python smoke success is separate evidence. The final
PyPI success run must be an official run from a proper NBGV public release ref.
Request/result evidence is limited to request/result identity, registry facts,
artifact references, and result references; permission scopes, OIDC token
minting, and proof that no secrets or tokens are serialized belong to
contract-validation, readiness, workflow, or job evidence instead.

| Scenario                                                                | Fixture anchor                                                                                                                                                                                                                                                                               | Descriptor or catalog evidence                                                                                                                                                                                                                                                                                                                     | Plan evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 | Execution-set selector evidence                                                                                                                                                                                                       | Request, result, or receipt evidence                                                                                                                                                                                                                                                                                                                                                                                                                                             | Registry or readiness evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | Workflow conclusion evidence                                                                                                                                                                                                                                                                 |
| ----------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Confirmed-scope descriptor coverage                                     | Author-time descriptor fixture batch covers every first-delivery project root listed in the low-level author-time input project set                                                                                                                                                          | Every confirmed-scope descriptor is discovered from its project root, declares both `buddy` and `official`, and either declares explicit targets or a valid zero-target profile where allowed                                                                                                                                                      | Single-project plans include only the requested active `project`; repository-wide descriptor coverage remains an authoring-validation concern and never infers projects from directory structure alone                                                                                                                                                                                                                                                                                                                                                                                                        | Execution selectors contain only graph members derived from the frozen plan and remain valid for zero-target and all-skip subsets; dry-run / validation-build subsets are future-only                                                 | Descriptor, plan, selector, request, receipt, skip, and report fixtures prove every confirmed-scope project can be handed off without using unstated descriptor defaults                                                                                                                                                                                                                                                                                                         | Registry evidence is required only for planned live targets; zero-target or skipped profiles still require descriptor, plan, selector, and conclusion evidence proving the explicit no-op path                                                                                                                                                                                                                                                                                                                                                                   | Acceptance fails if any confirmed-scope descriptor is missing, lacks either profile, or is excluded from an unfiltered first-delivery plan for reasons other than a blocking descriptor/catalog diagnostic                                                                                   |
| Descriptor discovery and invalid descriptor fail closed                 | One valid `src/**/three.release.yml` fixture plus one malformed descriptor fixture                                                                                                                                                                                                           | Valid descriptor is discovered only from the supported path; malformed descriptor records the closed validation error as registered `DESC_SCHEMA_INVALID` for descriptor schema failures and/or `DESC_STATIC_INVALID` for descriptor static validation failures, with no inferred release from directory shape                                     | Valid plan contains the descriptor-owned project; malformed case is a diagnostics-only failure with no frozen or partial plan artifact, and diagnostics map each malformed descriptor failure to `DESC_SCHEMA_INVALID` or `DESC_STATIC_INVALID` as applicable                                                                                                                                                                                                                                                                                                                                                 | Valid run has selectors only for planned graph members; malformed case publishes no `execution-sets.json` artifact and has no build, tag, publish, skip, or proof selectors                                                           | Valid run may produce normal receipts; malformed case has diagnostics only and produces no build receipt, `tag-result.json`, `publish-request.json`, `publish-result.json`, `skip-result.json`, `release-immutable-proof-v1-...` artifact containing `immutable-proof.json`, or other downstream build, tag, publish, skip, or proof artifact                                                                                                                                    | Readiness, workflow, or job evidence proves the malformed run stops before tag creation, registry mutation, or other live side effects                                                                                                                                                                                                                                                                                                                                                                                                                           | Valid run reaches normal conclusion; malformed run fails with diagnostics only before approval-gated or live side-effect jobs                                                                                                                                                                |
| Target catalog validation fails closed                                  | Invalid `eng/release/target-instances.yml` schema/static-validation fixtures plus a descriptor fixture whose target `uses` reference is absent from the catalog                                                                                                                              | Invalid catalog shape or static invariants are reported as registered `CATALOG_SCHEMA_INVALID` as applicable; a descriptor `uses` reference missing from the catalog is reported as registered `CATALOG_REF_NOT_FOUND`                                                                                                                             | Diagnostics-only failure after normalized `planner-request.json` materialization: no frozen plan artifact is emitted for invalid catalog input or unresolved catalog references                                                                                                                                                                                                                                                                                                                                                                                                                               | No `execution-sets.json` artifact is published; diagnostics or report evidence shows no selectors, publish jobs, topology routes, build variants, skip sets, or live routes were materialized                                         | No build receipt, `tag-result.json`, `publish-request.json`, `publish-result.json`, `skip-result.json`, `release-immutable-proof-v1-...` artifact containing `immutable-proof.json`, or other downstream build, tag, publish, or proof contract artifact is produced                                                                                                                                                                                                             | Readiness evidence proves rejection occurs before protected-environment approval, credentials, write-token use, OIDC token minting, tag creation, registry mutation, GitHub Release mutation, or any other side effect                                                                                                                                                                                                                                                                                                                                           | Workflow fails closed during target catalog validation or catalog reference resolution, with diagnostics only and no partial plan                                                                                                                                                            |
| Unknown requested project ID fails closed                               | Workflow fixture with active `project` input containing one unresolved project id                                                                                                                                                                                                            | Input normalization preserves the explicit unresolved `project` value and proves it is not an in-scope releasable project id                                                                                                                                                                                                                       | No plan artifact is emitted; `planner-diagnostics.json` records blocking `REQ_PROJECT_NOT_FOUND` for the unresolved requested project                                                                                                                                                                                                                                                                                                                                                                                                                                                                         | No `execution-sets.json` artifact is published; diagnostics or report evidence shows no selectors, publish jobs, build variants, or live routes were materialized                                                                     | Diagnostics-only failure: no build receipt, `tag-result.json`, publish request/result, skip receipt, immutable proof wrapper, or other downstream contract artifact is produced                                                                                                                                                                                                                                                                                                  | Readiness evidence proves rejection occurred before protected-environment approval, credentials, OIDC token minting, tag creation, registry mutation, GitHub Release mutation, or any other side effect                                                                                                                                                                                                                                                                                                                                                          | Workflow fails closed during request validation; valid explicit project-id requests proceed normally                                                                                                                                                                                         |
| Closed cross-job JSON contract validation                               | Executable contract-validation fixture suite with golden valid files and representative invalid rejection files                                                                                                                                                                              | Descriptor and catalog fixtures are used only to seed valid contract examples; invalid contract fixtures mutate shape/schema without changing descriptor semantics; .NET fixtures include both NuGet-shaped package cases where `requires-package-id` is true and app/installer/zero-target cases where it is false                                | Golden and rejection fixtures cover `release-plan.json`, including GitHub Release `attestation.signer-workflow`; invalid plan files fail closed before any planner, renderer, selector derivation, or downstream job consumes them                                                                                                                                                                                                                                                                                                                                                                            | Golden and rejection fixtures cover `execution-sets.json` selector files; invalid selector/execution-set files fail closed before build, tag, publish, skip, or report fan-out uses them                                              | Golden and rejection fixtures cover planner/build/publish requests, `dotnet-planner-metadata-input.json`, `dotnet-planner-metadata.json`, active `entry-publish-handoff.json`, build/publish/skip results, `tag-result.json` ensure-tag result evidence, immutable proof wrappers, GitHub Release result receipts, and GitHub Release asset proof wrappers, with extra root fields, missing required fields, wrong `api-version`/`kind`, and invalid conditional fields rejected | Contract-validation output also covers diagnostics and report-file contracts; treats successful-plan entry handoff artifact-name nullability as an active non-null report invariant, `requires-package-id` true/false derivation, required `package-id` presence when true, `package-id` omission when false, and `DOTNET_METADATA_FAILED` for missing or empty required `PackageId`; and proves closed shape/schema enforcement before credentials, OIDC tokens, protected environments, registry writes, GitHub Release writes, or tag mutation can be reached | CI fails before workflow data exchange is considered implementation-ready when any invalid fixture is accepted or any required valid fixture is rejected                                                                                                                                     |
| C# library package build and release                                    | `src/public/lib/Hjg.Pngcs/`                                                                                                                                                                                                                                                                  | Descriptor models `.nupkg`, `.snupkg`, and GitHub Release targets only for first delivery                                                                                                                                                                                                                                                          | Plan freezes Windows build unit, immutable members, release tag, and publish nodes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            | Build selector contains the Windows variant; publish selectors contain one logical node per planned GitHub Release target                                                                                                             | Windows build receipt covers `.nupkg` and `.snupkg`; GitHub Release request/result or skip receipt covers both modeled assets                                                                                                                                                                                                                                                                                                                                                    | GitHub Release observation matches final asset set; no package-registry evidence is expected for `hjg-pngcs` first delivery because live registry acceptance uses dedicated smoke rows                                                                                                                                                                                                                                                                                                                                                                           | Successful or skip-satisfied conclusion with missing-receipt failures surfaced when expected receipts are absent                                                                                                                                                                             |
| Selected registry publish flag false keeps GitHub Release-only delivery | Custom allowlisted channel fixture where the descriptor selects an external registry target but the matching registry publish flag is `false`                                                                                                                                                | The descriptor/catalog selection remains visible in the frozen plan; disabling the workflow registry flag does not rewrite descriptor intent                                                                                                                                                                                                       | Plan outputs still expose the selected registry target and active GitHub Release node so the build and GitHub Release path remain auditable                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | Registry publish selectors are treated as disabled for workflow completion when the matching publish flag is false; GitHub Release selectors remain active                                                                            | No package-registry publish receipt is required for the disabled registry target; the GitHub Release result and asset proof remain the terminal publish receipt set                                                                                                                                                                                                                                                                                                              | Readiness evidence proves disabled registry jobs and no-op gates cannot block or satisfy the release while the GitHub Release-only path completes                                                                                                                                                                                                                                                                                                                                                                                                                | Workflow succeeds through the matching `release-*-no-<registry>` split and release-completed sentinel when GitHub Release receipts are valid                                                                                                                                                 |
| NuGet registry targets deferred in active split topology                | `src/public/lib/hcoona-release-smoke-github-packages/` and `src/public/lib/hcoona-release-smoke-nuget/`                                                                                                                                                                                      | Dotnet smoke descriptors keep `.nupkg` and `.snupkg` artifacts for GitHub Release evidence only; active descriptors and `eng/release/target-instances.yml` do not declare `nuget/github-packages` or `nuget/nuget-org` targets                                                                                                                     | Plans for active dotnet smoke projects contain GitHub Release nodes only and no NuGet registry target snapshots                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               | Publish selectors contain no NuGet registry publish-node ids or NuGet topology routes                                                                                                                                                 | No NuGet registry publish request/result is expected while the active workflow has no dotnet/NuGet build and publish path                                                                                                                                                                                                                                                                                                                                                        | Workflow evidence proves no `NuGet/login`, `publish_nuget`, `packages: write` NuGet job, or NuGet OIDC credential path is present; NuGet registry live evidence is deferred until a future active workflow path is implemented                                                                                                                                                                                                                                                                                                                                   | Active runs cannot mutate NuGet.org or GitHub Packages NuGet package versions; adding such targets requires workflow, descriptor, catalog, and acceptance evidence updates                                                                                                                   |
| C# app binary release                                                   | `src/private/app/qidian-novel-downloader/`                                                                                                                                                                                                                                                   | Descriptor models app binary artifact and GitHub Release target                                                                                                                                                                                                                                                                                    | Plan freezes Windows publish variant, release asset identity, tag, and GitHub Release node                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    | Build selector contains the app publish variant; publish selector contains the GitHub Release node                                                                                                                                    | Build receipt names the binary artifact; GitHub Release request/result or skip receipt names the asset                                                                                                                                                                                                                                                                                                                                                                           | GitHub Release observation verifies the binary asset state                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | Successful or skip-satisfied conclusion                                                                                                                                                                                                                                                      |
| C# app Inno installer release                                           | `src/public/app/ImageOcclusionEditor/`                                                                                                                                                                                                                                                       | Descriptor models the installer artifact and GitHub Release target                                                                                                                                                                                                                                                                                 | Plan freezes the installer artifact identity and release asset binding                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        | Build selector contains the installer-producing Windows variant; publish selector contains the GitHub Release node                                                                                                                    | Build receipt plus packaging evidence proves Inno Setup consumed the executor-internal WinUI publish output; publish result names the installer asset                                                                                                                                                                                                                                                                                                                            | GitHub Release observation verifies installer asset state                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        | Successful or skip-satisfied conclusion                                                                                                                                                                                                                                                      |
| Python package including `nbgv-python`                                  | `src/public/lib/nbgv-python/`                                                                                                                                                                                                                                                                | Descriptor declares the special version authority and any `pypi/pypi` target                                                                                                                                                                                                                                                                       | Plan freezes selected commit version, artifacts, target snapshots, and publish nodes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          | Build selector contains the Python package variant; publish selectors route GitHub Release and PyPI nodes by topology                                                                                                                 | Build receipt records only artifact path, digest, and size; GitHub Release and PyPI publish request/result or skip receipts match the frozen request/result contracts and identity                                                                                                                                                                                                                                                                                               | Publish executor, job, or acceptance-harness evidence proves Python package metadata conformance before upload; GitHub Release evidence is required; PyPI registry evidence is required when the official `pypi/pypi` target is enabled                                                                                                                                                                                                                                                                                                                          | Successful, skip-satisfied, or fail-closed conclusion according to destination state                                                                                                                                                                                                         |
| Python normal NBGV/Hatch package                                        | `src/public/lib/hcoona-release-smoke-pypi/`                                                                                                                                                                                                                                                  | Descriptor declares build-system NBGV version authority and any `pypi/pypi` target                                                                                                                                                                                                                                                                 | Plan freezes selected commit version, artifacts, target snapshots, and publish nodes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          | Build selector contains the Python package variant; publish selectors route GitHub Release and PyPI nodes by topology                                                                                                                 | Build receipt records only artifact path, digest, and size; GitHub Release and PyPI publish request/result or skip receipts match the frozen request/result contracts and identity                                                                                                                                                                                                                                                                                               | Publish executor, job, or acceptance-harness evidence proves Python package metadata conformance before upload; GitHub Release evidence is required; PyPI registry evidence is required when the official `pypi/pypi` target is enabled                                                                                                                                                                                                                                                                                                                          | Successful, skip-satisfied, or fail-closed conclusion according to destination state                                                                                                                                                                                                         |
| First-delivery PyPI official publish                                    | At least one first-delivery Python fixture with an enabled `pypi/pypi` official target                                                                                                                                                                                                       | Descriptor enables official PyPI target; target-instance catalog requires the active split PyPI job in `release-orchestrate.yml`                                                                                                                                                                                                                   | `official` plan freezes PyPI project, version, release tag, target instance, and publish-node identity                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        | `execution-sets.json` selects the PyPI node for the orchestrator-hosted PyPI publish path and does not require deleted publish workflows                                                                                              | Materialized `publish-request.json` records the planned `publish-node-id`, PyPI identity, and artifact references; the publish request plus referenced build receipt and bundle evidence prove each planned `artifact-id`, path, digest, size, and bundle member; successful `publish-result.json` records only uploaded distribution filenames and the PyPI project/release URL as registry evidence                                                                            | Blocking workflow/readiness evidence: PyPI trusted publisher is configured for `hcoona/three`, workflow filename `release-orchestrate.yml`, and environment `pypi`; observed PyPI release-file evidence for the published version matches the planned/uploaded distribution filenames and SHA-256 digests proven by publish-request plus build receipt and bundle evidence                                                                                                                                                                                       | Deferred final acceptance: after merge to `main`, an official public-ref run succeeds only after protected-environment approval and live PyPI publication evidence are present                                                                                                               |
| Node package build and GitHub Release                                   | `src/public/lib/hcoona-release-smoke-npm/`                                                                                                                                                                                                                                                   | Descriptor models npm pack artifact and GitHub Release target                                                                                                                                                                                                                                                                                      | Plan freezes npm package identity, pack artifact, tag, and GitHub Release node                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                | Build selector contains npm pack variant; publish selector contains GitHub Release node                                                                                                                                               | npm pack receipt; GitHub Release request/result or skip receipt for the packed artifact                                                                                                                                                                                                                                                                                                                                                                                          | GitHub Release observation verifies packed asset state                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | Successful or skip-satisfied conclusion                                                                                                                                                                                                                                                      |
| Dual npmjs trusted publication                                          | `src/public/lib/hcoona-release-smoke-npm-dual/`                                                                                                                                                                                                                                              | Descriptor target `npm/npmjs` uses the unscoped official package while the buddy profile also targets scoped GitHub Packages npm; catalog requires the active split npmjs caller-workflow topology with the token-minting npmjs job in `release-orchestrate.yml`                                                                                   | Plan snapshots npmjs target instance, `publish-topology`, target capabilities, `resolved-publish-identity`, and resolved unscoped package identity without confusing the scoped GitHub Packages buddy identity                                                                                                                                                                                                                                                                                                                                                                                                | `execution-sets.json` selects the npmjs node for the orchestrator-hosted npmjs publish path and does not require deleted publish workflows                                                                                            | npm pack receipt; publish request/result records npm package/version identity, tarball artifact reference, target instance, and registry result reference                                                                                                                                                                                                                                                                                                                        | npmjs registry/readiness workflow evidence proves the trusted publisher is configured for workflow filename `official.yml`, environment `npmjs`, and package `hcoona-release-smoke-npm-dual`; the OIDC-token-requesting `npm publish` job is `publish-node-npmjs` in the reusable orchestrator, and both the official caller and reusable publish job grant `id-token: write`; deleted publish workflows are not trusted publishers                                                                                                                              | Official run succeeds or skip-satisfies with no unrelated OIDC-capable jobs; reruns validate the dual smoke project rather than publishing real Hexo                                                                                                                                         |
| Ruby gem build and GitHub Release                                       | `src/public/lib/hcoona-release-smoke-rubygems/`                                                                                                                                                                                                                                              | Descriptor models gem artifact and GitHub Release target                                                                                                                                                                                                                                                                                           | Plan freezes gem identity, `.gem` artifact, tag, and GitHub Release node                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      | Build selector contains gem build variant; publish selector contains GitHub Release node                                                                                                                                              | gem build receipt; GitHub Release request/result or skip receipt for the `.gem` artifact                                                                                                                                                                                                                                                                                                                                                                                         | GitHub Release observation verifies `.gem` asset state                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | Successful or skip-satisfied conclusion                                                                                                                                                                                                                                                      |
| Dedicated GitHub Packages NuGet smoke remains unavailable               | `src/public/lib/hcoona-release-smoke-github-packages/`                                                                                                                                                                                                                                       | Descriptor no longer declares `nuget/github-packages`; target-instance catalog keeps an empty NuGet family with no active instances                                                                                                                                                                                                                | Plan snapshots `.nupkg` artifact identity only for GitHub Release evidence and contains no GitHub Packages NuGet disposition                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | Publish selector has no GitHub Packages NuGet node                                                                                                                                                                                    | No NuGet registry publish request/result is materialized                                                                                                                                                                                                                                                                                                                                                                                                                         | Readiness evidence proves the active split workflow does not expose a GitHub Packages NuGet mutation job or token path                                                                                                                                                                                                                                                                                                                                                                                                                                           | Active runs cannot mutate GitHub Packages NuGet until a future dotnet/NuGet workflow path is implemented and tested                                                                                                                                                                          |
| RubyGems.org trusted publication                                        | `src/public/lib/hcoona-release-smoke-rubygems/`                                                                                                                                                                                                                                              | Descriptor target `rubygems/rubygems-org`; catalog requires the active split RubyGems job in `release-orchestrate.yml`                                                                                                                                                                                                                             | Plan snapshots RubyGems.org target instance, `publish-topology`, target capabilities, `resolved-publish-identity`, and resolved gem identity                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | Selector routes the logical RubyGems.org `publish-node-id` through the orchestrator-hosted RubyGems publish path without changing node identity                                                                                       | gem build receipt; publish request/result records gem/version identity, artifact reference, target instance, and registry result reference                                                                                                                                                                                                                                                                                                                                       | RubyGems.org registry/readiness workflow evidence proves the trusted publisher is configured for workflow filename `release-orchestrate.yml` and environment `rubygems`; the official reusable-workflow caller may carry `id-token: write` only as the upper-bound permission, while actual registry-token minting remains inside the RubyGems publish job                                                                                                                                                                                                       | Official run succeeds or skip-satisfies with no unrelated OIDC-token-minting jobs                                                                                                                                                                                                            |
| Mixed-topology graph                                                    | One run with GitHub Release, reusable-orchestrator PyPI target, reusable-orchestrator npmjs target, reusable-workflow RubyGems.org target, and supported GitHub-token package targets                                                                                                        | Descriptors and target catalog cover every active first-delivery topology in one normalized graph while leaving deferred NuGet registry targets unmodeled                                                                                                                                                                                          | Plan contains one logical `publish-node-id` per active target and keeps graph edges independent of concrete workflow route                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    | `execution-sets.json` places each active node in exactly the matching topology selector and proves selectors route correctly without forking logical publish-node or publish-result semantics                                         | Each selected job emits the same result contract keyed by its planned `publish-node-id`                                                                                                                                                                                                                                                                                                                                                                                          | Registry observations match each active target surface; readiness evidence is checked only for enabled external targets                                                                                                                                                                                                                                                                                                                                                                                                                                          | Workflow report correlates all logical nodes to one final conclusion with no duplicate or missing publish results                                                                                                                                                                            |
| Package-registry profile coexistence fail-closed rule                   | Synthetic package-registry fixtures where `buddy` and `official` select the same package name for the same registry, including a statically resolvable family and a NuGet case whose equality depends on evaluated `PackageId`                                                               | Static fixture records `DESC_STATIC_INVALID`; NuGet fixture proves author-time validation only emits `requires-package-id` metadata input and does not evaluate MSBuild `PackageId` before the trusted NBGV-based metadata phase                                                                                                                   | Diagnostics-only fail-closed result shows `DESC_STATIC_INVALID` for the static violation or `PUBLISH_IDENTITY_CONFLICT` after .NET metadata collection for the NuGet violation; no frozen or partial plan artifact is emitted                                                                                                                                                                                                                                                                                                                                                                                 | No `execution-sets.json` artifact is published; diagnostics or report evidence shows no selectors, publish jobs, topology routes, build variants, skip sets, or live routes were materialized                                         | No build receipt, `tag-result.json`, `publish-request.json`, `publish-result.json`, `skip-result.json`, `release-immutable-proof-v1-...` artifact containing `immutable-proof.json`, package artifact, or other downstream build, tag, publish, skip, or proof artifact is produced                                                                                                                                                                                              | Readiness evidence records that no protected environment, external registry readiness check, credential use, OIDC token request, or live registry mutation is reached; NuGet readiness evidence appears only after metadata handoff completes and before any live side-effect gate                                                                                                                                                                                                                                                                               | Workflow fails closed before package-registry publication and before any readiness or live side-effect gate                                                                                                                                                                                  |
| Package metadata mismatch fails closed                                  | Synthetic NuGet, PyPI, npm, or RubyGems package-registry fixture with concrete package metadata intentionally diverging from planner-frozen identity                                                                                                                                         | Descriptor and catalog select a package-registry target family where metadata conformance applies, with planner identity left valid                                                                                                                                                                                                                | Plan freezes `resolved-publish-identity` for the intended package name and version and does not authorize any alternate identity                                                                                                                                                                                                                                                                                                                                                                                                                                                                              | Active publish selector is materialized only up to the executor pre-upload gate for the planned logical `publish-node-id`                                                                                                             | Build receipt names the concrete package file; publish request references the frozen identity; no positive publish result or receipt is emitted for the mismatched package                                                                                                                                                                                                                                                                                                       | Executor or job evidence records the metadata mismatch before upload; registry evidence proves no upload, overwrite, package creation, release-file creation, or other registry mutation occurred for the mismatched concrete package                                                                                                                                                                                                                                                                                                                            | Workflow fails closed before registry mutation; rerun with matching package metadata can reach the normal publish path                                                                                                                                                                       |
| Superseded multi-project dispatch                                       | Historical/future fixture anchors only                                                                                                                                                                                                                                                       | Older selected descriptors were normalized from operator input and descriptor discovery; active dispatch uses one `project` input                                                                                                                                                                                                                  | Future multi-project plans would contain multiple project-scoped graphs and target snapshots pinned to the same commit SHA; active plans remain single-project                                                                                                                                                                                                                                                                                                                                                                                                                                                | Future selectors would contain project-scoped build variants and publish nodes for each selected project                                                                                                                              | Future receipts and publish results would remain project-scoped and keyed by planned ids                                                                                                                                                                                                                                                                                                                                                                                         | Registry observations would be separated by package identity and target instance                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 | Future run reports would show all selected projects and a single aggregate conclusion; active workflows do not expose this dispatch shape                                                                                                                                                    |
| Superseded dry-run with default no build                                | Historical/future fixture anchor only                                                                                                                                                                                                                                                        | Descriptor discovery and validation occur normally                                                                                                                                                                                                                                                                                                 | Plan or dry-run report records intended graph without live mutation intent                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    | Execution sets contain no live publish nodes, empty `active-github-release-publish-node-ids`, and no default build variants                                                                                                           | No build receipt is expected unless validation-build is explicitly requested; no `tag-result.json` or publish request/result exists                                                                                                                                                                                                                                                                                                                                              | Registry observations are read-only, if performed; no live registry or GitHub Release mutation occurs                                                                                                                                                                                                                                                                                                                                                                                                                                                            | Workflow concludes as dry-run validation without protected-environment approval                                                                                                                                                                                                              |
| Zero-target or all-skip no-side-effect live run                         | Valid selected project/profile fixture whose descriptors resolve to no publish targets, plus a separate fixture where all selected publish nodes classify as `skip-satisfied`                                                                                                                | Descriptor discovery, request normalization, and destination classification prove the selected graph either has no publish nodes or has only already-satisfied selected publish nodes                                                                                                                                                              | Plan records the selected graph and dispositions without dry-run suppression                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | Execution sets contain no active publish selectors; zero-target has no publish intent, and all-skip has only `skip-satisfied-publish-node-ids`                                                                                        | Zero-target emits no tag or publish request/result; all-skip emits only synthetic skip receipts and no tag or publish request/result                                                                                                                                                                                                                                                                                                                                             | Readiness evidence proves the protected environment gate is skipped because there are no live side effects, distinct from dry-run; no registry, GitHub Release, credential, OIDC, or tag-creation mutation occurs                                                                                                                                                                                                                                                                                                                                                | Workflow concludes successfully or skip-satisfied without protected-environment approval despite `dry-run: false`                                                                                                                                                                            |
| Superseded validation-build receipts are not immutable proof            | Historical/future fixture anchor only                                                                                                                                                                                                                                                        | Descriptor discovery and validation occur normally with `validation-build` enabled                                                                                                                                                                                                                                                                 | Plan evidence stays independent of `validation-build`; `validation-build` remains outside the planner request and plan envelope                                                                                                                                                                                                                                                                                                                                                                                                                                                                               | Build selectors may run; publish selectors remain dry-run-disabled                                                                                                                                                                    | Validation build receipts are present, are marked validation-only by control-plane/build receipt provenance, and are excluded from immutable proof wrappers and later proof lookup                                                                                                                                                                                                                                                                                               | No registry mutation occurs; proof lookup evidence shows no admissible proof from this run because validation-only provenance belongs to the receipt/proof layer, not the plan                                                                                                                                                                                                                                                                                                                                                                                   | Workflow concludes as validation-only dry-run and cannot satisfy later publish proof requirements                                                                                                                                                                                            |
| Build proof lookup and reuse limits                                     | Any immutable package fixture with prior admissible proof                                                                                                                                                                                                                                    | Descriptor identity and target instance match the prior proof binding                                                                                                                                                                                                                                                                              | Plan records `skip-satisfied` or proof reuse only when binding hash, commit, artifact identity, and retention window are valid; absent, expired, ambiguous, conflicting, mismatched, or validation-only proof records fail-closed `IMMUTABLE_PROOF_UNAVAILABLE`; proved partial immutable state records `IMMUTABLE_PARTIAL_UNSUPPORTED`                                                                                                                                                                                                                                                                       | Publish selector is skipped or materialized according to planner disposition; selector must not reuse expired or mismatched proof                                                                                                     | Synthetic skip receipt cites the admissible proof and proof artifact binding when reuse is valid; invalid proof states emit no skip/proof artifact that declares satisfied proof                                                                                                                                                                                                                                                                                                 | Registry evidence and proof artifact lookup both support the planner decision; validation-build receipts are inadmissible; invalid proof states have no live registry side effect                                                                                                                                                                                                                                                                                                                                                                                | Successful skip-satisfied conclusion for valid reuse; fail-closed conclusion for invalid reuse                                                                                                                                                                                               |
| Rerun after partial success                                             | Any multi-target package fixture above                                                                                                                                                                                                                                                       | Same descriptors and target catalog as the first attempt                                                                                                                                                                                                                                                                                           | First failed plan/report and rerun plan preserve completed target evidence while recomputing only planner-owned dispositions; partial immutable identity conflicts record `IMMUTABLE_PARTIAL_UNSUPPORTED`, while unavailable required proof records `IMMUTABLE_PROOF_UNAVAILABLE`                                                                                                                                                                                                                                                                                                                             | Rerun selectors skip completed satisfied nodes and select only eligible remaining nodes                                                                                                                                               | First attempt has at least one positive side-effect receipt before later failure; rerun emits skip or publish results per planned node                                                                                                                                                                                                                                                                                                                                           | Registry observations confirm completed side effects before reuse and prove fail-closed immutable conflicts or unavailable proof states cause no additional registry mutation                                                                                                                                                                                                                                                                                                                                                                                    | First non-cancelled run fails; rerun succeeds, skip-satisfies, or fails closed according to evidence                                                                                                                                                                                         |
| GitHub Release tag atomicity                                            | Multi-tag GitHub Release fixture with at least two distinct required release tags in one run, including one missing creation-eligible tag and one existing tag that peels to a different commit                                                                                              | Descriptor models multiple GitHub Release targets or projects whose frozen publish nodes require distinct release tag identities                                                                                                                                                                                                                   | Plan freezes the full required tag set, selected commit SHA, create-only or replace-authoritative dispositions, and asset sets before tag orchestration                                                                                                                                                                                                                                                                                                                                                                                                                                                       | `selected-github-release-publish-node-ids` and `active-github-release-publish-node-ids` identify every live GitHub Release node that requires tag orchestration before publish                                                        | The tag job verifies all existing tags before creating any missing tag; in the non-force path, the mismatched existing tag case emits no positive `tag-result.json`, creates no publish request, and proves the missing creation-eligible tag was not created. A separate `force_update_tag=true` fixture proves reviewed retargeting is explicit.                                                                                                                               | GitHub tag and Release observations prove no retargeted tag is accepted in the non-force path, no missing tag was created after the mismatch was discovered, and no GitHub Release asset mutation occurred; force-path evidence proves the reviewed retarget when explicitly requested                                                                                                                                                                                                                                                                           | Success only when the whole required tag set is atomic and consistent; any wrong existing tag fails before tag creation or release mutation unless `force_update_tag=true` explicitly authorizes retargeting                                                                                 |
| GitHub Release asset content equivalence                                | Same-tag GitHub Release fixture whose remote release has the expected state, asset names, and labels, but at least one asset has a missing, unverifiable, or mismatched GitHub Artifact Attestation or wrapper/sidecar content proof                                                         | Descriptor models GitHub Release assets with stable planned artifact identities                                                                                                                                                                                                                                                                    | Planner observation accepts `exact-satisfied` only when the release state, exact planned asset names, and planned labels all match and every required asset has both attestation-backed content-equivalence proof verified against the frozen signer workflow identity `attestation.signer-workflow` and selected commit plus admissible current wrapper/sidecar proof evidence; size and SHA evidence are corroborating inputs only, and missing, failed, or mismatched attestation or wrapper/sidecar proof enters the same-tag partial/conflict path or fails closed rather than becoming `skip-satisfied` | Selectors are emitted only when the replay matrix authorizes a publish mode; no selector is emitted for a false satisfied state                                                                                                       | Successful GitHub Release publish emits `github-release-asset-proof.json` and a GitHub Artifact Attestation for each uploaded asset; no skip receipt claims satisfaction for same-name assets whose bytes are unproved or mismatched                                                                                                                                                                                                                                             | GitHub Release observation downloads the remote asset, verifies its GitHub Artifact Attestation against the frozen full signer workflow identity `hcoona/three/.github/workflows/release-orchestrate.yml` and selected commit, requires admissible current wrapper/sidecar proof evidence, compares digest and size only as corroborating evidence, and proves mismatched or unverifiable attestation-backed or wrapper/sidecar content is not accepted as already satisfied even when names, labels, sizes, or SHA values match                                 | Rerun skips only when every planned GitHub Release asset is content-equivalent by exact names/labels plus attestation-backed and wrapper/sidecar proof; missing, failed, mismatched, or unverifiable proof fails closed or enters an authorized replacement path before any publish mutation |
| Buddy to official promotion unsupported fail-closed                     | Buddy GitHub Release proof mode disabled fixture, including mixed descriptors that also declare npm or RubyGems registry targets                                                                                                                                                             | Same descriptor identity would otherwise select `buddy` GitHub Release publication while buddy attestations remain disabled                                                                                                                                                                                                                        | Planner and plan-derived workflow gates treat a selected `buddy` descriptor containing `github-release/public` as an unsupported whole descriptor for a `buddy` run with `enable_attestation=false`; planning or the workflow backstop stops before publish handoff upload                                                                                                                                                                                                                                                                                                                                    | No publish handoff, tag orchestration, build selector, package-registry selector, or GitHub Release reusable-workflow selector is allowed to progress to mutation                                                                     | No build receipt, `tag-result.json`, `publish-request.json`, `publish-result.json`, `github-release-result.json`, `skip-result.json`, `immutable-proof.json`, or `github-release-asset-proof.json` is produced                                                                                                                                                                                                                                                                   | Readiness evidence proves the unsupported buddy GitHub Release path fails before protected-environment approval, tag creation, GitHub Release mutation, package-registry mutation, proof sidecar persistence, or completion receipt upload                                                                                                                                                                                                                                                                                                                       | Buddy GitHub Release remains unsupported and fails the whole selected descriptor while buddy GitHub Release attestations are disabled; registry targets in the same descriptor do not continue as a partial release, and official publication remains the supported live path                |
| Buddy force rejected after official freeze                              | Any GitHub Release fixture above with prior official final release evidence for the selected project/version                                                                                                                                                                                 | Same descriptor identity is selected by a `buddy` request with `force: true`; prior official release evidence marks the project/version as official-frozen                                                                                                                                                                                         | Planner diagnostics identify `OFFICIAL_FROZEN_VERSION`; no plan artifact is emitted for the rejected request                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | No `execution-sets.json`; diagnostics or report evidence shows no selectors, build variants, publish jobs, or live routes were materialized                                                                                           | No build receipt, `tag-result.json`, `publish-request.json`, `publish-result.json`, `skip-result.json`, or `release-immutable-proof-v1-...` artifact containing `immutable-proof.json` is produced                                                                                                                                                                                                                                                                               | Planner-owned GitHub Release observation proves the selected project/version is already official-frozen; readiness evidence proves rejection before protected-environment approval, tag creation, GitHub Release mutation, package-registry mutation, credentials, write-token use, or OIDC token minting                                                                                                                                                                                                                                                        | `buddy` with `force: true` fails closed with `OFFICIAL_FROZEN_VERSION`, no partial plan, no execution set, and no live side effects                                                                                                                                                          |
| Official GitHub Release publication                                     | Any GitHub Release fixture above                                                                                                                                                                                                                                                             | Descriptor has official GitHub Release target and no prior buddy evidence for same identity                                                                                                                                                                                                                                                        | `official` plan records `create-only`, selected commit SHA, release tag, and asset set                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        | `selected-github-release-publish-node-ids` includes the GitHub Release node; `active-github-release-publish-node-ids` remains populated only for live mutation                                                                        | `tag-result.json` and GitHub Release publish result prove creation                                                                                                                                                                                                                                                                                                                                                                                                               | GitHub Release observation verifies final release state                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          | `maintain+` authorization succeeds before planning; no generic protected `release` environment gate is assumed in the active topology                                                                                                                                                        |
| Immutable partial replay                                                | NuGet multi-member fixture, current-scope PyPI wheel-plus-sdist fixture, or future PyPI multi-wheel fixture mocked at adapter boundary                                                                                                                                                       | Descriptor models a multi-member immutable package identity                                                                                                                                                                                                                                                                                        | Plan diagnostics classify same-identity partial state as fail-closed with registered `IMMUTABLE_PARTIAL_UNSUPPORTED` and do not declare satisfied proof                                                                                                                                                                                                                                                                                                                                                                                                                                                       | No live publish selector is materialized for the conflicted immutable node                                                                                                                                                            | No publish request/result exists, and no skip or immutable proof artifact declares the conflicted node satisfied                                                                                                                                                                                                                                                                                                                                                                 | Registry observation at adapter boundary shows partial state; readiness evidence proves no protected-environment approval, OIDC token request, or live registry mutation is reached; broader PyPI multi-wheel support remains deferred                                                                                                                                                                                                                                                                                                                           | Workflow fails closed before live mutation                                                                                                                                                                                                                                                   |
| External OIDC topology blocked                                          | Selected live official external OIDC catalog fixture whose frozen `publish-topology` cannot be scheduled by the current workflow topology                                                                                                                                                    | Catalog target-instance snapshot identifies the blocked registry family and unsupported workflow topology                                                                                                                                                                                                                                          | No published plan artifact; diagnostics-only `planner-diagnostics.json` records registered code `REQ_EXTERNAL_TOPOLOGY_BLOCKED` for the unsupported external topology                                                                                                                                                                                                                                                                                                                                                                                                                                         | No `execution-sets.json` artifact is published; diagnostics or report evidence shows no execution-set selectors, publish jobs, topology routes, build variants, or live routes were materialized                                      | No `build-result.json`, `tag-result.json`, `publish-request.json`, `publish-result.json`, `skip-result.json`, or `release-immutable-proof-v1-...` artifact containing `immutable-proof.json` is produced                                                                                                                                                                                                                                                                         | Control-plane readiness/workflow evidence proves the topology gate fails before plan publication, protected-environment approval, OIDC token requests, registry mutation, or live side-effect jobs                                                                                                                                                                                                                                                                                                                                                               | Workflow fails before protected-environment approval and live side-effect jobs                                                                                                                                                                                                               |
| External target disabled                                                | PyPI, npmjs, or RubyGems.org fixture with live enablement off; NuGet registry smoke is future-only/topology-blocked until NuGet catalog instances and the dotnet/NuGet workflow path are restored                                                                                            | Descriptor has an active official external target but owner-side live enablement is disabled; NuGet registry descriptors are not activatable by a live-enable token while their catalog instances are absent                                                                                                                                       | No published plan artifact; diagnostics-only `planner-diagnostics.json` records registered code `REQ_EXTERNAL_TARGET_DISABLED` and the required enablement token for the disabled target                                                                                                                                                                                                                                                                                                                                                                                                                      | No `execution-sets.json` artifact is published; diagnostics or report evidence shows no execution-set selectors, publish jobs, topology routes, build variants, or live routes were materialized                                      | No `build-result.json`, `tag-result.json`, `publish-request.json`, `publish-result.json`, `skip-result.json`, or `release-immutable-proof-v1-...` artifact containing `immutable-proof.json` is produced                                                                                                                                                                                                                                                                         | Control-plane readiness/workflow evidence proves the live-enable gate fails before protected-environment approval, OIDC token requests, registry mutation, or live side-effect jobs; enabled-token rerun reaches normal readiness checks                                                                                                                                                                                                                                                                                                                         | Disabled active-target run fails before protected-environment approval; enabled active-target rerun reaches normal official approval and publish path. NuGet registry smoke remains topology-blocked/future-only until restored, not live-enable-token activatable today                     |
| External trusted-publisher misconfiguration fails closed                | PyPI, npmjs, or RubyGems.org fixture with the live-enable token present but the external registry trusted-publisher policy missing, mismatched, or bound to the wrong workflow/environment; NuGet registry smoke is future-only until restored                                               | Descriptor has an official external OIDC target and the catalog topology is supported; the failure is registry-owner-side trusted-publisher configuration, not descriptor shape, topology blocking, or live-enable allowlist absence                                                                                                               | Plan and `execution-sets.json` may be published because the target is intentionally enabled; the affected publish node remains a normal active publish selector for the frozen topology                                                                                                                                                                                                                                                                                                                                                                                                                       | Selector evidence routes the node to the correct topology path, including orchestrator-hosted PyPI/npmjs/RubyGems.org; NuGet.org remains deferred until re-enabled, and proves no fallback route is selected after credential failure | `publish-request.json` is materialized for the planned node, but no positive `publish-result.json` is emitted; build receipts remain ordinary positive build evidence and must not be recast as publish success                                                                                                                                                                                                                                                                  | Registry/job evidence proves the OIDC credential exchange or upload failed before any package file was created, replaced, yanked, unpublished, or otherwise mutated; evidence identifies the expected trusted workflow filename and registry-specific environment without serializing tokens or secrets                                                                                                                                                                                                                                                          | Workflow fails closed after official approval and before successful registry mutation; fixing the registry policy and rerunning may reach the normal publish or skip-satisfied path                                                                                                          |
| Invalid external OIDC live-enable allowlist fails closed                | Workflow fixture setting `THREE_RELEASE_ENABLED_EXTERNAL_OIDC_TARGETS` to malformed, wildcard, empty-component, unknown-target, or non-OIDC target tokens                                                                                                                                    | Target-instance catalog distinguishes known OIDC targets from unknown refs and known non-OIDC targets; descriptors are not used to forgive invalid allowlist syntax                                                                                                                                                                                | No published plan artifact; diagnostics-only `planner-diagnostics.json` records registered code `REQ_INVALID_INPUT` and the invalid live-enable token class                                                                                                                                                                                                                                                                                                                                                                                                                                                   | No `execution-sets.json` artifact is published; diagnostics or report evidence shows no execution-set selectors, publish jobs, topology routes, build variants, or live routes were materialized                                      | No `build-result.json`, `tag-result.json`, `publish-request.json`, `publish-result.json`, `skip-result.json`, or `release-immutable-proof-v1-...` artifact containing `immutable-proof.json` is produced                                                                                                                                                                                                                                                                         | Control-plane readiness/workflow evidence proves live-enable allowlist normalization fails closed before protected-environment approval, live side-effect jobs, credential use, OIDC token request, tag creation, external registry upload, GitHub Release mutation, or GitHub Packages mutation                                                                                                                                                                                                                                                                 | Workflow fails closed during live-enable allowlist normalization; valid normalized tokens proceed to normal external target gating                                                                                                                                                           |
| Entry actor authorization fails closed                                  | Workflow fixture dispatching `buddy`/`official` with unresolved actor permission and with permission below the profile threshold                                                                                                                                                             | Inputs identify selected profile and current-attempt actor; descriptors are irrelevant because rejection precedes discovery                                                                                                                                                                                                                        | No planner request or plan is emitted; diagnostics record `REQ_ACTOR_UNAUTHORIZED` for unresolved and insufficient permission                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 | No `execution-sets.json` artifact is published; report shows no execution-set selectors, publish jobs, topology routes, build variants, or live routes were materialized                                                              | No `build-result.json`, `tag-result.json`, `publish-request.json`, `publish-result.json`, `skip-result.json`, or `release-immutable-proof-v1-...` artifact containing `immutable-proof.json` is produced                                                                                                                                                                                                                                                                         | Readiness proves failure before protected-environment approval, environment-bound live jobs, credentials, write-token use, OIDC token minting, registry mutation, or other live side effect                                                                                                                                                                                                                                                                                                                                                                      | Unauthorized or unresolved actors fail before planning; authorized actors proceed without treating environment approval as authorization                                                                                                                                                     |
| Invalid entry input rejection                                           | Workflow fixtures dispatching both `buddy` and `official` with missing `project`, invalid `version`, invalid `target`, or unsupported legacy inputs such as `dry-run`, `validation-build`, `force`, or `canary-override-non-public-ref`                                                      | Shared entry input normalization records the rejected raw combination before planner execution for both entry workflows                                                                                                                                                                                                                            | No planner request or plan is emitted; diagnostics identify `REQ_INVALID_INPUT` for invalid active inputs or unsupported legacy input names                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | No `execution-sets.json` artifact is published; report shows no execution-set selectors, publish jobs, topology routes, build variants, or live routes were materialized                                                              | No `build-result.json`, `tag-result.json`, `publish-request.json`, `publish-result.json`, `skip-result.json`, or `release-immutable-proof-v1-...` artifact containing `immutable-proof.json` is produced                                                                                                                                                                                                                                                                         | Readiness proves rejection before protected-environment approval, environment-bound live jobs, credentials, write-token use, OIDC token minting, registry mutation, tag creation, or other side effect                                                                                                                                                                                                                                                                                                                                                           | Invalid inputs fail with diagnostics, no partial plan, and no side effects; valid active inputs `project`, `version`, optional `target`, and `force_update_tag` continue                                                                                                                     |
| Trusted workflow-ref and reusable caller identity gate                  | Workflow-level fixture dispatching from default branch, protected release ref, unprotected branch, indeterminate protection state, and reusable calls from `official.yml`, `buddy.yml`, or an unexpected workflow                                                                            | Entry inputs identify the selected workflow ref and trusted-ref policy source; reusable orchestrator OIDC claims identify the caller `workflow_ref`, reusable `job_workflow_ref`, repository, and selected ref; CODEOWNERS wildcard coverage governs every checked-in workflow file capable of becoming a caller                                   | Trusted refs and exact active callers may reach normal planning; untrusted or indeterminate refs emit diagnostics-only `planner-diagnostics.json` with registered code `REQ_UNTRUSTED_WORKFLOW_REF`, and mismatched reusable callers fail before policy/resolve with no planner request or plan artifact                                                                                                                                                                                                                                                                                                      | Rejected refs or callers publish no `execution-sets.json` artifact; diagnostics or report evidence shows no execution-set selectors, publish jobs, topology routes, build variants, or live routes were materialized                  | Rejected refs or callers produce no `build-result.json`, `tag-result.json`, `publish-request.json`, `publish-result.json`, `skip-result.json`, or `release-immutable-proof-v1-...` artifact containing `immutable-proof.json`                                                                                                                                                                                                                                                    | Readiness evidence proves the trusted-ref and reusable-caller gates fail closed before planning, credentials, protected environment use, registry OIDC token minting, or any live side effect; custom allowlisted callers are governed by workflow CODEOWNERS coverage and cannot impersonate reserved `official` or `buddy` channels                                                                                                                                                                                                                            | Trusted refs and exact active callers proceed to the applicable scenario; untrusted refs, indeterminate refs, and mismatched reusable callers fail closed before planning                                                                                                                    |
| Dispatch SHA pinning and buddy target authorization                     | Workflow fixture covering empty `target` from the GitHub UI dispatch ref/commit plus non-empty branch, lightweight tag, annotated tag, ref, and 40-hex SHA selectors while moving any original target ref after entry resolution; buddy target-policy fixture covers allowed and unsafe refs | Entry inputs preserve the raw selector when present and the once-resolved peeled release commit SHA; annotated-tag evidence proves the tag object is peeled to the target commit, while empty `target` evidence records the UI dispatch commit; buddy non-empty targets must be reachable from `eng/release/buddy-target-refs.yml` authorized refs | Planner request and plan use the pinned release `commit-sha` only; all project discovery, version authority, target snapshots, release tags, and publish identities are derived from that commit; raw SHA, tag, or unknown branch buddy targets cannot reach planning unless proven reachable from an authorized ref                                                                                                                                                                                                                                                                                          | Build, tag, publish, and report selectors all carry or reference the same pinned release commit SHA rather than the raw selector                                                                                                      | Build receipts, `tag-result.json`, publish requests/results, skip receipts, immutable proof wrappers, and diagnostics all record or validate the same pinned release commit SHA; no later job checks out or consumes a moved target ref                                                                                                                                                                                                                                          | Git, tag, GitHub Release, package registry, and proof evidence correspond to the pinned release commit; registry observations are not used to follow the moved target ref, workflow code remains from the trusted dispatch ref, and buddy registry publishes remain limited to commits reachable from checked-in authorized refs                                                                                                                                                                                                                                 | Workflow succeeds or fails solely against the pinned release commit; moving the target branch or tag after dispatch cannot change planned, built, tagged, or published content; unauthorized buddy targets fail closed before orchestration                                                  |
| Same-release cross-entry release-identity concurrency serialization     | Workflow fixture dispatching three same-release runs with different raw refs or equivalent selectors that resolve to the same project/version release tag on `buddy.yml` and `official.yml`                                                                                                  | `authorize-entry` resolves each raw selector before the lock and emits `needs.authorize-entry.outputs.release_group` for the `orchestrate` caller job                                                                                                                                                                                              | Runs stay pinned to their own resolved `commit-sha`; no supersession diagnostic or replacement plan is emitted; raw ref differences do not split the active dynamic job-level group after `authorize-entry`; entry authorization and the top-level pre-orchestration report bridge are not claimed as covered by the lock                                                                                                                                                                                                                                                                                     | Not applicable: concurrency is job-level workflow evidence, not execution-set selector evidence; selectors add no extra selector proof beyond the resolved target SHA carried into orchestration                                      | No additional request/result/receipt evidence is required for concurrency; receipts remain ordinary positive evidence when their jobs run and are not used to prove the concurrency key; the replaced pending orchestrate job has no positive release receipt                                                                                                                                                                                                                    | Workflow evidence shows the `orchestrate` job in both `buddy.yml` and `official.yml` declares `concurrency.group: ${{ needs.authorize-entry.outputs.release_group }}` with `cancel-in-progress: false`; proves child reusable workflows, matrix rows, publish jobs, and top-level pre-orchestration report jobs do not declare those groups; proves GitHub's one-running/one-pending job behavior may cancel the older pending same-group orchestrate job before live side effects                                                                               | The running same-group orchestrate job is not auto-cancelled; at most one pending same-group orchestrate job is retained by GitHub; older pending orchestrate jobs may be cancelled before side effects; matrix siblings are not canceled by same-group pending semantics                    |
| Cancellation and report best effort                                     | Workflow-level integration fixture                                                                                                                                                                                                                                                           | Descriptor evidence may be incomplete depending on cancellation timing                                                                                                                                                                                                                                                                             | Any already emitted plan is retained as evidence; missing later artifacts are not synthesized                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 | Any already emitted selector artifact is retained as evidence                                                                                                                                                                         | Already persisted positive receipts remain valid; missing expected receipts are reported from job conclusions when possible                                                                                                                                                                                                                                                                                                                                                      | Registry state is trusted only when matched by positive receipt or later planner observation                                                                                                                                                                                                                                                                                                                                                                                                                                                                     | GitHub cancelled conclusion is authoritative; in-run report artifact is best-effort because cancellation may prevent final reporting jobs                                                                                                                                                    |
| Official authorization and environment approval distinction             | Workflow-level integration fixture                                                                                                                                                                                                                                                           | Descriptor may be any live-capable fixture                                                                                                                                                                                                                                                                                                         | Plan is created only after entry authorization succeeds; environment approval is not treated as authorization                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 | Execution sets may contain live mutation publish nodes only after authorization; protected-environment approval is not represented by a selector                                                                                      | Emitted request/result/receipt records are limited to mutation target identity, registry facts, artifact references, and result references                                                                                                                                                                                                                                                                                                                                       | Active registry environments (`pypi`, `npmjs-gate`, `npmjs`, `rubygems`) required reviewers, self-review prevention, admin bypass behavior, environment-bound job conclusions, and absence of fabricated denied-approval receipts prove whether approval-gated mutation jobs ran or were blocked separately from actor authorization; no active `environment: release` gate is assumed                                                                                                                                                                           | `buddy` needs explicit authorization with no registry approval unless an active target environment requires it; `official` needs authorization before planning and active registry-environment approval before live registry side effects                                                    |

The matrix may live in test fixtures or generated CI output. It does not need to
become a new operator-facing release record. A missing row for any first-
delivery scenario is an acceptance-coverage gap before declaring the workflow
design ready for implementation. The implementation may choose whether the
matrix is represented as fixtures or generated CI output, but it must not defer
the required first-delivery scenario rows into implementation-owned scope.

## 11. Implementation-Owned Boundaries

The remaining implementation work is intentionally sized for one experienced
senior programmer. The implementer may choose the smallest maintainable internal
shape that satisfies this page, but must not use "implementation detail" as a
reason to rename workflow identities, reinterpret selectors, move frozen inputs
or outputs, weaken readiness gates, or change acceptance evidence.

The following details remain implementation-owned:

- internal module, class, function, and package decomposition for the planner,
  validators, executors, registry adapters, and report generation;
- helper script layout, internal command names, composite action structure, and
  shell or JavaScript/Python/.NET wrapper organization under repository
  conventions;
- exact language and runtime choices for internal helpers when the choice stays
  within the repository's established C#, Python, JavaScript/TypeScript, MISE,
  and HK conventions;
- local refactoring, shared utility extraction, and private adapter factoring
  inside a frozen workflow, planner, executor, or registry-adapter boundary;
- logging implementation details, including logger choice, message wording, log
  grouping, and debug verbosity, as long as required diagnostics, receipts,
  results, and run-summary evidence remain machine-readable where specified;
- temporary directories, cache directories, scratch bundle staging, cleanup
  behavior, and retry/backoff constants, as long as receipted paths and
  fail-closed semantics remain intact; and
- exact internal helper APIs, private function signatures, in-process data
  structures, JSON schema library selection, and test harness mechanics.

The following details are **not** implementation-owned:

- frozen workflow filenames, workflow identities, and registry-visible workflow
  filename roles from Section 3;
- descriptor file paths, shared target-instance catalog paths, discovery rules,
  and descriptor/catalog ownership;
- the `three.release.plan/v1alpha1` envelope, normalized graph shape, snapshot
  ownership, and other frozen plan-shape contracts;
- execution-set selector keys, selector object shapes, and selector semantics;
- planner request contracts, publish request contracts, publish result
  contracts, skip receipt contracts, build receipt contracts, tag result
  contracts, immutable-proof wrappers, diagnostics containers, and any
  acceptance-facing artifact identity;
- publish topology routing, including which topology path hosts each registry
  operation and which selector partition reaches each path;
- placement of `id-token`, `contents`, `packages`, and active registry
  environments on jobs that need them, and the absence of those permissions from
  unrelated jobs;
- external readiness gates, including repository environment configuration,
  owner-side trusted-publisher setup, and explicit live-enable controls;
- acceptance evidence requirements, including the traceability matrix rows and
  the concrete evidence each row requires; and
- package-registry identity conformance, including package-name equivalence
  rules and publish-time produced-file validation.

Shared code reuse is allowed and encouraged when it reduces duplication inside
the implementation-owned layer. It must not change a workflow identity or token
boundary, merge topology paths that are contractually distinct, alter any
request/result/receipt/plan contract, or make one registry adapter derive facts
that the frozen design assigns to another owner.

The implementation-owned details may change without reopening design only if all
frozen descriptor, catalog, plan, workflow identity, topology routing,
permission, request, result, receipt, proof, readiness, registry-observation,
registry-conformance, and acceptance-evidence contracts above remain intact.

## 12. Consistency Review

The final cross-section review after the topology, publish, registry,
permissions, readiness, and acceptance passes confirms that this page uses one
coherent vocabulary for topology partitions, workflow identity, permissions,
request and receipt files, external readiness, acceptance evidence, and
implementation-owned boundaries.

The consistency pass must also preserve or refresh the external documentation
grounding. This low-level design was checked against these official or primary
sources:

- GitHub Actions environments, required reviewers, prevent self-review, artifact
  APIs, OIDC claims, workflow syntax, workflow cancellation reference,
  `GITHUB_TOKEN`, GitHub Release REST API, and GitHub Packages registry guides.
- Microsoft Learn NuGet trusted publishing, service index, package base address,
  registration, package publish, and `.snupkg` symbol package pages.
- PyPI trusted publishing, JSON API, Python package name normalization, and
  version normalization specifications.
- npm trusted publishers, provenance, package-name guidelines, and npm CLI
  `view` and `pack` documentation.
- RubyGems trusted publishing, RubyGems.org API, publishing guide, and GitHub
  Packages RubyGems guide.

## Related Pages

- [Workflow Release Requirements Baseline](./workflow-release-requirements-baseline.md)
- [Workflow Release Design Direction](./workflow-release-design-direction.md)
- [Workflow Release Architecture Model](./workflow-release-architecture-model.md)
- [Workflow Release Descriptor Schema](./workflow-release-descriptor-schema.md)
- [Workflow Release Plan Shape](./workflow-release-plan-shape.md)
- [Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md)
- [Workflow Release Design Layering and Implementation Handoff Scope](./workflow-release-design-layering-and-handoff-scope.md)
- [Workflow Release OIDC Publish Topology Research](./workflow-release-oidc-publish-topology.md)
- [Workflow Release Deferred PyPI Multi-Wheel Support](./workflow-release-deferred-pypi-multi-wheel-support.md)
- [Workflow Release Low-Level Design Rebaseline Recommendation](./workflow-release-low-level-design-rebaseline-recommendation.md)
- [Workflow Release Operator Rollout Runbook](./workflow-release-operator-rollout.md)
