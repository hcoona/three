# Workflow Release Low-Level Design

## 1. Document Governance and Baseline Status

Status: this page is the post-middle-layer-topology-rebaseline low-level baseline
for workflow-release implementation handoff. It supersedes earlier low-level
workflow-release drafts for current-scope implementation guidance while keeping
the frozen requirements, high-level architecture, descriptor schema, plan shape,
and workflow/executor boundary contracts unchanged.

The target reader is one experienced senior implementer. This page freezes the
concrete realization seams that affect correctness, testability, external
registry configuration, and acceptance evidence, but it intentionally does not
freeze every helper, internal module, private API, script name, or command-line
wrapper. Those details remain implementation-owned unless this page names them as
cross-job, cross-workflow, registry-facing, or acceptance-facing contracts.

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
5. entry-hosted publish path;
6. publish executor design;
7. registry adapter partitioning;
8. permissions and environment;
9. external setup and readiness;
10. acceptance traceability;
11. implementation-owned boundaries;
12. consistency review.

Group 1 established that skeleton. Completed rebaseline passes replace their
owned sections as normative design content, while sections not yet reworked
continue to preserve starting material until their owning follow-up pass updates
them.

### Low-Level Design Summary

| Area                      | Low-level decision                                                                                                                                                                      |
| ------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Workflow files            | Use stable checked-in workflow filenames because OIDC trusted-publisher policies match workflow identity.                                                                               |
| Entry authorization       | Explicitly check `buddy` as `write+` and `official` as `maintain+` before planning; serialize same-entry, same-commit release work without auto-cancellation.                           |
| Planner host              | Expose the planner through a repo-owned CLI contract; the implementation language remains implementation-owned.                                                                         |
| Request and receipt files | Serialize all cross-job machine data as UTF-8 JSON with LF line endings and stable `api-version` plus `kind`.                                                                           |
| Dry-run builds            | Dry-run does not build by default. A separate `validation-build` input may run build units, but its receipts are validation-only and inadmissible as immutable proof.                   |
| Build proof lookup        | Publish one small attempt-scoped proof artifact per immutable-proof member binding under a binding-hash prefix so future planner runs can discover and validate admissible proofs.      |
| Dispatch SHA lock         | Resolve the UI-selected branch or tag before orchestration, pass the peeled commit SHA forward, and place same-entry same-commit concurrency where that SHA is available.               |
| Tag orchestration         | Create lightweight release tags and verify existing tags by peeling annotated tags to the selected commit.                                                                              |
| External setup            | Require the `release` environment, registry trusted-publisher policies, and explicit external-registry live enablement before official OIDC registry publication.                       |
| Diagnostics               | Use a small registered planner-code vocabulary plus a registration rule for new codes.                                                                                                  |
| Diagnostics artifact      | Serialize planner diagnostics through one closed container object rather than a raw array, NDJSON stream, or ad hoc log file.                                                           |
| Execution sets            | Materialize matrix selectors in one closed JSON object so empty dry-run, validation-build, zero-target, and all-skip runs have deterministic workflow behavior.                         |
| Failure reporting         | Treat success and skip receipts as positive evidence only; failed jobs are summarized from job conclusions plus missing expected receipts, while cancellation reporting is best-effort. |
| Registry adapters         | Keep remote observation in planner adapters, live mutation in publish executors, and package metadata conformance in publish executors before upload.                                   |
| Acceptance                | Maintain a trace table from each acceptance scenario to descriptors, plans, receipts, registry evidence, and workflow conclusions.                                                      |

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
- Every non-zero publish profile includes GitHub Release. The first delivery
  scope includes GitHub Release, NuGet, PyPI, npm, and RubyGems, including live
  official PyPI publication.
- `buddy` and `official` must not publish the same package name to the same
  registry.
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
- Manual dispatch resolves the selected branch or tag once to an exact
  `commit-sha`. Planning, build, tag, and publish work must stay pinned to that
  exact resolved commit SHA and must not follow a moving ref later in the run.
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
  even when topology routes the concrete publish job through an entry-hosted,
  caller-workflow-bound, reusable-workflow-bound, or GitHub-token path.
- Executors are thin consumers of materialized requests. They must not re-read
  release descriptors or `eng/release/target-instances.yml`, rediscover targets,
  query publish destinations for replay classification, or derive alternate
  publish identity, topology, overwrite policy, or same-tag GitHub Release
  replacement policy.
- Package-registry publish executors validate produced package metadata against
  the planner-frozen `resolved-publish-identity` before upload and fail closed on
  mismatch.
- `ensure-tag` verifies the full existing required tag set before creating any
  missing tags, creates none if any existing tag points elsewhere, and never
  retargets tags.
- Current-scope immutable proof reuse is limited to unexpired GitHub Actions
  artifacts under the platform retention window, subject to the frozen proof
  admissibility rules.

## 3. Workflow Identity and Filename Contract

Current-scope workflow files must use these stable checked-in paths:

| File                                          | Trigger or call shape | Stable responsibility                                                      | External registry filename role                                                               |
| --------------------------------------------- | --------------------- | -------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `.github/workflows/release-buddy.yml`         | `workflow_dispatch`   | `buddy` entry workflow.                                                    | Stable entry contract; not a first-delivery trusted-publisher filename.                       |
| `.github/workflows/release-official.yml`      | `workflow_dispatch`   | `official` entry workflow, including early actor-permission authorization. | Trusted-publisher filename for entry/caller-workflow-bound external registries.               |
| `.github/workflows/release-orchestrate.yml`   | `workflow_call`       | Shared orchestration workflow for one selected profile run.                | Internal orchestration contract; must not be configured as a current external publisher.      |
| `.github/workflows/release-build-variant.yml` | `workflow_call`       | One reusable build unit for one `variant-id`.                              | Internal build contract; must not be configured as an external publisher.                     |
| `.github/workflows/release-publish-node.yml`  | `workflow_call`       | One reusable publish unit for one `publish-node-id`.                       | Trusted-publisher filename only for registries that validate reusable workflow identity here. |

All five workflow filenames above are stable low-level workflow contracts, not
implementation details. The low-level contract is the checked-in workflow file
path; for registry-facing workflows, it also includes the registry-visible
filename derived from that path. Implementation-owned scripts, composite actions,
helper action versions, command wrappers, and executor internals are intentionally
outside this frozen filename contract. Renaming or replacing any file above is a
coordinated low-level/workflow contract change, not a harmless refactor. When the
changed file is currently configured in an external trusted-publisher policy, the
migration additionally requires coordinated registry-policy updates and evidence
refresh. In current scope, that additional registry-policy migration constraint
applies only to `release-official.yml` and `release-publish-node.yml`.

Only filenames that an external registry may store in a trusted-publisher policy
are registry-facing identity contracts. In first delivery, those filenames are
`release-official.yml` for entry/caller-workflow-bound publication and
`release-publish-node.yml` for the reusable-workflow-bound RubyGems.org
publication topology. `release-buddy.yml`, `release-orchestrate.yml`, and
`release-build-variant.yml` remain stable workflow contracts, but they must not
be entered into current external trusted-publisher registry policies.

PyPI trusted publishing must be configured to repository `hcoona/three`,
workflow filename `release-official.yml`, and GitHub Actions environment
`release` for first-delivery live PyPI publication. PyPI must not be configured
to trust `release-orchestrate.yml`, `release-publish-node.yml`, or any reusable
workflow identity in this design. A valid active official `pypi/pypi` publish
node therefore routes through the entry-workflow-bound path so the job requesting
the PyPI OIDC token runs under the configured `official` entry workflow identity.

npmjs trusted publishing stores the caller/top-level GitHub Actions workflow
filename. The reusable publish workflow may host the `npm publish` command and
mint the OIDC token, but npmjs validation is tied to the calling/top-level
workflow identity; in first delivery that registry-side filename is
`release-official.yml` with the `release` environment. For this topology, the
active caller chain is `release-official.yml` invoking `release-orchestrate.yml`,
then shared orchestration invoking `release-publish-node.yml` for the active
npmjs caller-workflow-bound selector. Every active caller job in that chain that
must pass the OIDC capability onward, plus the child reusable publish job that
requests the token, must have the required `id-token: write`; unrelated jobs
must not.

RubyGems.org trusted publishing can trust the reusable workflow identity where
configured. For the current same-repository reusable-publish topology, configure
RubyGems.org with workflow filename `release-publish-node.yml`, leave separate
workflow-repository owner/name fields blank, and use the `release` environment.
The active caller chain is the `release-official.yml` caller job invoking
`release-orchestrate.yml`, then the `release-orchestrate.yml` publish caller job
invoking `release-publish-node.yml`. Because reusable workflows cannot elevate
permissions above their caller jobs, every active caller job in that chain that
must pass OIDC capability onward, plus the child reusable publish job that mints
the RubyGems.org token, must declare `id-token: write`; unrelated jobs must not
receive that permission.

GitHub Release and GitHub Packages publication use `GITHUB_TOKEN` authority.
They do not have an external trusted-publisher policy and therefore do not add an
external registry workflow filename beyond the stable workflow contracts above.

## 4. Topology Routing Core

### Entry Workflow Inputs

Both entry workflows should expose the same operator-facing input shape except
for `force`, which is valid only on `buddy`.

| Input                   | Type    | Owner         | Meaning                                                                                        |
| ----------------------- | ------- | ------------- | ---------------------------------------------------------------------------------------------- |
| `requested-project-ids` | string  | control plane | Optional comma or newline separated project IDs. Empty means all in-scope releasable projects. |
| `dry-run`               | boolean | control plane | Run planning and non-publish validation without tag or live publish side effects.              |
| `validation-build`      | boolean | control plane | Valid only when `dry-run` is true. Runs build units for validation-only receipt output.        |
| `force`                 | boolean | `buddy` only  | Planner-facing `request-flags.force` for allowed `buddy` overwrite cases.                      |

The selected commit is not a text input. The operator selects the workflow ref in
the GitHub UI, and the control plane resolves that ref once to the exact
`commit-sha` at run start.

In current scope, the workflow source ref and the release source ref are the same
operator-selected GitHub ref. Because external trusted-publisher policies match
stable workflow identity and environment rather than reviewed workflow contents,
release workflows must be dispatched only from trusted refs: the default branch,
or a branch or tag protected by repository rules and allowed by the `release`
environment deployment policy. Arbitrary unprotected branch dispatch is out of
scope for live release workflows. Supporting a trusted default-branch dispatcher
that releases a separate arbitrary source ref would be a successor workflow-entry
design, not an implementation detail.

The entry workflow must resolve the selected ref before invoking shared
orchestration. That resolution step peels an annotated tag, if selected, to the
commit that all later planning, build, tag, and publish work will use, then
passes the resolved `commit-sha` as an explicit orchestration input. Later jobs
must not recompute the selected ref or follow a moving branch head.

Input normalization rules:

1. Split `requested-project-ids` on comma and newline, trim ASCII whitespace, drop
   empty entries, de-duplicate, and sort lexicographically before materializing
   the planner request.
2. Reject `force: true` for `official` before planner execution.
3. Reject `validation-build: true` unless `dry-run: true`.
4. Keep `dry-run` and `validation-build` outside the planner request and plan
   envelope so they do not enter whole-release rerun identity.

If a pre-planner input rule rejects the run after workflow input normalization
has begun, the control plane must write `planner-diagnostics.json` using the same
`planner-diagnostic` contract and registered `REQ_*` code vocabulary used by
planner-hosted request validation. It must not emit a partial plan.

### Entry Authorization and Duplicate-Run Concurrency

`authorize-entry` is a control-plane gate and runs before planner execution for
both profiles.

Current-scope authorization policy:

| Profile    | Required current-attempt actor repository permission | Approval behavior                                        |
| ---------- | ---------------------------------------------------- | -------------------------------------------------------- |
| `buddy`    | `write` or higher                                    | no extra approval                                        |
| `official` | `maintain` or higher                                 | protected `release` environment on live side-effect jobs |

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

Current scope does not adopt native duplicate-run auto-cancellation. Entry
workflows and the shared orchestration workflow must not configure
`cancel-in-progress: true` for release runs. They must still serialize
same-entry, same-commit release work with a GitHub Actions concurrency key
derived from the selected entry workflow plus resolved `commit-sha`, and must set
`cancel-in-progress: false`. Cancellation therefore remains manual operator
cancellation or ordinary platform cancellation, not a repo-defined supersession
protocol, while same-entry, same-commit live release side effects do not race
each other.

Because the resolved `commit-sha` is computed after the platform accepts the
entry workflow run, the same-entry same-commit concurrency key must be attached at
a workflow or job boundary where that value is already available, such as the
shared orchestration workflow call. A coarser entry-workflow concurrency key based
only on the raw ref may be added for operator convenience, but it is not a
substitute for the resolved-SHA key required above.

### Orchestration Job Realization

The selected entry workflow and the shared orchestration workflow together
implement the middle-layer job sequence with these concrete data handoffs:

| Job               | Physical host                                                                                                   | Required inputs                                                                                                                         | Required outputs                                                                           |
| ----------------- | --------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| `authorize-entry` | top-level entry workflow before invoking `release-orchestrate.yml`                                              | GitHub event context, selected profile, and resolved normalized dispatch context                                                        | Authorization conclusion and authorized normalized run metadata consumed by orchestration. |
| `plan`            | shared orchestration workflow                                                                                   | Pinned checkout at `commit-sha`, normalized planner request, prior proof lookup service, raw dry-run controls, external live-enable map | Frozen plan, `execution-sets.json`, and synthetic skip-result artifacts, or diagnostics.   |
| `build`           | shared orchestration workflow calling the reusable build unit                                                   | Plan artifact, one `variant-id` per matrix row                                                                                          | Variant bundle, `build-result`, and optional immutable-proof artifacts.                    |
| `ensure-tag`      | control-plane job before publish fan-out                                                                        | Frozen plan plus selected and active GitHub Release publish nodes                                                                       | `tag-result.json` tag verification or creation evidence.                                   |
| `publish`         | shared orchestration for reusable-hosted selectors; entry workflow resumes hosting for entry-workflow selectors | Plan artifact, one `publish-node-id` per matrix row, referenced build receipts, and a materialized `publish-request.json`               | `publish-result` artifacts.                                                                |
| `report`          | top-level entry workflow after any entry-hosted publish jobs complete                                           | Plan, diagnostics, tag results, build results, skip results, publish results from all topology paths, job conclusions                   | Final operator summary.                                                                    |

In current-scope first delivery, execution-set derivation is an implementation
detail of the `plan` job, not a separate reportable workflow job. This keeps the
closed `release-report.json.jobs` shape aligned with the workflow. The produced
selectors must still be serialized as machine-readable JSON rather than
reconstructed from ad hoc shell output in later jobs.

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
    "validation-build": false,
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
   `publish-disposition` is `publish`, before applying dry-run suppression.
2. `active-publish-node-ids` is empty when `dry-run` is true; otherwise it equals
   `publish-intent-node-ids`.
3. `active-variant-ids` contains the distinct variants reachable from
   `publish-intent-node-ids` only when either the run is live or
   `validation-build` is true. Ordinary dry-runs therefore serialize `[]`.
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
for ordinary dry-runs, validation-build dry-runs, zero-target selections,
all-`skip-satisfied` selections, and any run where a particular topology
partition has no active members.

Topology changes only the physical host that runs the live publish side effect.
It does not change the logical `publish-node-id`, the planner-frozen
`publish-disposition`, the synthetic skip-receipt semantics, the
`publish-request.json` materialization rules, or the standard `publish-result`
contract. Report aggregation therefore treats publish and skip receipts from all
topology paths as receipts for the same logical publish-node graph.

At fan-out time, the control plane routes from `execution-sets.json` at a high
level:

- `github-token`, `external-oidc-caller-workflow`, and
  `external-oidc-reusable-workflow` are reusable-hosted selectors and remain on
  the shared orchestration path that invokes the reusable publish unit.
- `external-oidc-entry-workflow` is entry-workflow-bound. The orchestration call
  returns this selector to the selected top-level entry workflow, and the entry
  workflow is responsible for physically hosting those publish jobs.

This section defines only the routing contract. Registry-specific entry-hosted
job details belong to the entry-hosted publish path design, and publish executor
internals remain executor-owned.

Current scope does not use a separate `approve` job. `official` live side effects
are gated by attaching the protected GitHub `release` environment directly to
the jobs that can perform those side effects: `ensure-tag` when it would create
tags for active GitHub Release publish nodes, and each live `publish` matrix job.
Read-only tag verification for all-`skip-satisfied` GitHub Release nodes does not
attach the protected environment because it cannot create tags or publish
externally. This keeps the environment claim on OIDC-backed external trusted
publishing jobs aligned with the registry-side trusted-publisher configuration.

### Dry-Run and Validation Build Policy

The concrete current-scope policy is:

1. `dry-run: true, validation-build: false` runs request normalization, descriptor
   and catalog validation, planner-time remote observation, plan serialization,
   execution-set derivation, and reporting. It does not run build units, create
   tags, request approval, or invoke publish units.
2. `dry-run: true, validation-build: true` additionally runs the active build
   units that would be needed by live publish nodes whose planner disposition is
   `publish`. It still does not create tags, request approval, or invoke publish
   units.
3. Validation-build receipts must be marked by control-plane provenance as
   `validation-only` and must never be returned by the prior immutable-proof
   lookup seam.
4. `validation-build: true` with `dry-run: false` is invalid input.

This policy keeps ordinary dry-run fast and side-effect-light while still giving
operators a deliberate path to test build and packaging realization.

### Planner CLI Boundary

The planner should be invoked through a repo-owned CLI with subcommands that
mirror stable workflow seams:

| Subcommand               | Required behavior                                                                                                                    |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| `validate-authoring`     | Load and validate all in-scope descriptors plus the shared catalog without emitting a plan.                                          |
| `plan`                   | Consume one normalized planner request and emit either one plan JSON file or planner diagnostics.                                    |
| `compute-pypi-filenames` | For the narrowed PyPI path, invoke the selected build backend tooling during planning and return exact final distribution filenames. |
| `render-summary`         | Convert plan, diagnostics, and receipts into compact Markdown for the workflow summary.                                              |

The implementation may merge these commands into one binary or script entry
point as long as the workflow still treats the file outputs as the stable
contract.

The planner job runs on Ubuntu and uses the repository's `mise` tool boundary to
install and invoke the ecosystem tools needed for planner-owned normalization.
Current scope allows the planner host to call `dotnet` or MSBuild evaluation for
NuGet `PackageId`, `uv`/Hatchling for PyPI filename computation, `pnpm`/`npm`
for npm package metadata, and Ruby/Bundler or RubyGems evaluation for gem
metadata. These calls are planner-owned observations, not release builds; actual
C# release builds remain in Windows build units, and publish credentials or
approval-gated secrets remain unavailable to the planner. If a required tool is
unavailable or its output cannot be normalized into the frozen contract, the
planner fails closed with diagnostics rather than falling back to static guesses.

The CLI must fail closed:

- invalid descriptors anywhere in current scope block all planning;
- remote observation errors after bounded retry block plan emission;
- no partial plan file is written on blocking planner failure;
- machine-readable diagnostics are written before returning a non-zero exit code
  whenever request normalization has begun.

### Planner Diagnostic Codes

The middle-layer contract freezes the diagnostic object shape but not the code
vocabulary. Current scope should start with this minimum code registry:

| Code                            | Phase            | Scope          | Meaning                                                                                                  |
| ------------------------------- | ---------------- | -------------- | -------------------------------------------------------------------------------------------------------- |
| `REQ_INVALID_INPUT`             | `validation`     | `request`      | Raw workflow input could not be normalized into the planner request contract.                            |
| `REQ_FORCE_FOR_OFFICIAL`        | `validation`     | `request`      | `request-flags.force` was true for `profile: official`.                                                  |
| `REQ_PROJECT_NOT_FOUND`         | `validation`     | `project`      | An explicitly requested project ID was not an in-scope releasable project.                               |
| `DESC_SCHEMA_INVALID`           | `validation`     | `project`      | A project descriptor failed file-schema validation.                                                      |
| `DESC_STATIC_INVALID`           | `validation`     | `project`      | Descriptor passed syntax but failed static repo validation.                                              |
| `CATALOG_SCHEMA_INVALID`        | `validation`     | `request`      | The shared target-instance catalog failed schema validation.                                             |
| `CATALOG_REF_NOT_FOUND`         | `validation`     | `project`      | A descriptor target reference did not resolve to exactly one catalog target instance.                    |
| `VERSION_AUTHORITY_FAILED`      | `normalization`  | `project`      | The planner could not resolve the project-scoped version identity.                                       |
| `PYPI_FILENAME_COMPUTE_FAILED`  | `normalization`  | `publish-node` | Planner-time PyPI filename computation failed or produced an unexpected member set.                      |
| `REMOTE_QUERY_FAILED`           | `query`          | `publish-node` | Destination query failed after bounded retry.                                                            |
| `REMOTE_NORMALIZATION_FAILED`   | `normalization`  | `publish-node` | Raw destination state could not be normalized for the target family.                                     |
| `REMOTE_CLASSIFICATION_FAILED`  | `classification` | `publish-node` | Normalized destination state could not be reduced to one remote-observation class.                       |
| `IMMUTABLE_PROOF_UNAVAILABLE`   | `classification` | `publish-node` | Required prior build digest proof was absent, expired, ambiguous, or conflicting.                        |
| `IMMUTABLE_PARTIAL_UNSUPPORTED` | `classification` | `publish-node` | Same-identity immutable remote state was a proved partial subset, which current scope fails closed.      |
| `REMOTE_CONFLICTING`            | `classification` | `publish-node` | Same-identity remote state conflicts with the frozen publish intent.                                     |
| `OFFICIAL_FROZEN_VERSION`       | `classification` | `project`      | A `buddy FORCE` request targeted a project/version already frozen by official GitHub Release.            |
| `REQ_ACTOR_UNAUTHORIZED`        | `validation`     | `request`      | The triggering actor did not have the required repository permission for the selected profile.           |
| `REQ_UNTRUSTED_WORKFLOW_REF`    | `validation`     | `request`      | The selected workflow ref was not a trusted protected release ref for current-scope release runs.        |
| `REQ_EXTERNAL_TARGET_DISABLED`  | `validation`     | `publish-node` | A selected live official external OIDC registry target was not present in the live-enable allowlist.     |
| `REQ_EXTERNAL_TOPOLOGY_BLOCKED` | `validation`     | `publish-node` | A selected live official external OIDC registry target cannot run through the current workflow topology. |
| `PLAN_INTERNAL_INVARIANT`       | `validation`     | `request`      | Planner detected an impossible internal state after validation should have prevented it.                 |

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
`publish-node-id` may be omitted if the implementation has not assigned the
opaque plan ID before the readiness gate, but `project-id`,
`target-instance-snapshot-id`, and `resolved-publish-identity` must be present.
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

| Logical object          | File name                  |
| ----------------------- | -------------------------- |
| Planner request         | `planner-request.json`     |
| Frozen plan             | `release-plan.json`        |
| Planner diagnostics     | `planner-diagnostics.json` |
| Build request           | `build-request.json`       |
| Build result            | `build-result.json`        |
| Tag result              | `tag-result.json`          |
| Publish request         | `publish-request.json`     |
| Publish result          | `publish-result.json`      |
| Skip result             | `skip-result.json`         |
| Execution sets          | `execution-sets.json`      |
| Immutable proof wrapper | `immutable-proof.json`     |
| Final run report data   | `release-report.json`      |

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

| Object family            | Extensibility field          | Rule                                                                                                     |
| ------------------------ | ---------------------------- | -------------------------------------------------------------------------------------------------------- |
| Planner diagnostics      | `details`                    | Adapter-specific machine context belongs under `details`.                                                |
| Publish and skip results | `evidence`                   | Small family-specific receipt evidence belongs under `evidence`.                                         |
| Immutable proof wrapper  | additional provenance fields | Extra control-plane provenance may be added only if proof lookup still applies the minimum checks below. |

The boundary documents define complete request and result object shapes for the
current `v1alpha1` handoff. In particular, `planner-request`, `build-request`,
`build-result`, `tag-result`, `publish-request`, `publish-result`, and
`skip-result` must not grow extra root-level fields during implementation unless
an extensibility field is named above or in the object's defining section. New
root-level machine fields require a successor contract update before tests or
workflows depend on them.

Before workflow jobs exchange these files, implementation must add executable
contract coverage for the closed cross-job JSON shapes. That coverage may be JSON
Schema, typed fixture validation, or an equivalent repo-owned test harness, but
it must include golden valid fixtures and representative closed-shape rejection
cases for plan, request, result, selector, proof, diagnostics, and report files.
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
    "requested-project-ids": [],
    "request-flags": {
        "force": false
    }
}
```

`requested-project-ids` is the normalized unique lexicographic list produced from
the raw workflow input; `[]` means all in-scope releasable projects. In
`v1alpha1`, `request-flags` has the exact key set shown above.

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

`tags` must cover every distinct required GitHub Release tag for the run. Each
`outcome` is either `verified` for an existing tag that peeled to the selected
commit or `created` for a newly created lightweight tag. `expected-commit-sha`
is the selected commit, and `peeled-commit-sha` is the commit observed after
verification or creation. Current scope does not define failed tag-result files;
tag failures are reported from the `ensure-tag` job conclusion plus a missing
positive `tag-result`.

`release-report.json` is the control-plane-authored final report data consumed by
`render-summary`. Its closed current-scope shape is:

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
        "conclusion": "success"
    },
    "plan": {
        "plan-id": "...",
        "selected-project-ids": []
    },
    "artifacts": {
        "plan-artifact-name": "...",
        "planner-diagnostics-artifact-name": null,
        "execution-sets-artifact-name": "...",
        "tag-result-artifact-name": null,
        "build-result-artifact-names": [],
        "publish-result-artifact-names": [],
        "skip-result-artifact-names": []
    },
    "jobs": {
        "authorize-entry": { "conclusion": "success" },
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
        "selected-projects": 0,
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

| Field                                         | Nullability rule                                                                                     |
| --------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| `artifacts.plan-artifact-name`                | `null` whenever no plan artifact was published; otherwise the frozen plan artifact name.             |
| `artifacts.planner-diagnostics-artifact-name` | `null` only when no diagnostics artifact exists; otherwise the diagnostics artifact name.            |
| `artifacts.execution-sets-artifact-name`      | `null` whenever no execution-set artifact was published; otherwise the execution-set artifact name.  |
| `artifacts.tag-result-artifact-name`          | `null` when `ensure-tag` did not emit positive tag evidence; otherwise the tag-result artifact name. |
| `artifacts.build-result-artifact-names`       | Empty array when no positive build-result artifacts exist; otherwise sorted artifact names.          |
| `artifacts.publish-result-artifact-names`     | Empty array when no positive publish-result artifacts exist; otherwise sorted artifact names.        |
| `artifacts.skip-result-artifact-names`        | Empty array when no synthetic skip-result artifacts exist; otherwise sorted artifact names.          |

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

| Artifact            | Name pattern                                                                               |
| ------------------- | ------------------------------------------------------------------------------------------ |
| Frozen plan         | `release-plan-v1-<run-id>-<attempt>-<safe-id(plan-id)>`                                    |
| Planner diagnostics | `release-planner-diagnostics-v1-<run-id>-<attempt>`                                        |
| Execution sets      | `release-execution-sets-v1-<run-id>-<attempt>-<safe-id(plan-id)>`                          |
| Variant bundle      | `release-build-bundle-v1-<run-id>-<attempt>-<safe-id(plan-id + "\n" + variant-id)>`        |
| Build result        | `release-build-result-v1-<run-id>-<attempt>-<safe-id(plan-id + "\n" + variant-id)>`        |
| Tag result          | `release-tag-result-v1-<run-id>-<attempt>-<safe-id(plan-id)>`                              |
| Publish result      | `release-publish-result-v1-<run-id>-<attempt>-<safe-id(plan-id + "\n" + publish-node-id)>` |
| Skip result         | `release-skip-result-v1-<run-id>-<attempt>-<safe-id(plan-id + "\n" + publish-node-id)>`    |
| Immutable proof     | `release-immutable-proof-v1-<safe-id(binding-json)>-<run-id>-<attempt>`                    |
| Final report        | `release-report-v1-<run-id>-<attempt>`                                                     |

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
executor still receives only its `build-request` and does not receive publish-node
snapshots. For each live, non-dry-run, non-validation build result, the wrapper
reads the frozen plan and emits one immutable proof artifact for each immutable
package-registry publish-node/artifact binding that references an artifact
fulfilled by that build result. Dry-run and validation-build units must not emit
admissible immutable proof artifacts; if implementation persists any diagnostic
wrapper for such runs, proof lookup must ignore it through `run.dry-run` or
`run.validation-only`.

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

The control plane may add more provenance fields, but planner proof lookup must
ignore a proof unless all of these checks pass:

1. artifact exists and is not expired;
2. `run.live` is true;
3. `run.dry-run` and `run.validation-only` are false;
4. `run.head-sha` matches the selected `commit-sha`;
5. binding equals the current planner-frozen immutable-proof member binding;
6. referenced `build-result` exists and maps the same `artifact-id` to the same
   digest and byte size;
7. all admissible proof artifacts for the same binding collapse to one digest.

If multiple admissible proofs for one binding have different digests, proof is
unavailable. The planner must not pick the newest proof to break the tie.

### Build Executor Realization

Build executors are selected from `project.ecosystem`.

| Ecosystem | Runner requirement | Tool boundary                            | Notes                                                                       |
| --------- | ------------------ | ---------------------------------------- | --------------------------------------------------------------------------- |
| `dotnet`  | Windows            | `mise`, `dotnet`, PowerShell when needed | Required because repository guidance says C# release builds run on Windows. |
| `python`  | Ubuntu             | `mise`, `uv`, Hatch build backend        | `nbgv-python` must preserve the planner-frozen `pyproject.toml` version.    |
| `node`    | Ubuntu             | `mise`, `pnpm`, npm CLI                  | Pack exactly one npm tarball per planned npm artifact.                      |
| `ruby`    | Ubuntu             | `mise`, RubyGems, Bundler when needed    | Build exactly one `.gem` per planned RubyGems artifact.                     |

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

The entry-hosted publish path is the physical workflow realization for active
publish nodes partitioned under
`active-publish-selectors.external-oidc-entry-workflow` in
`execution-sets.json`. It exists for registries whose trusted-publisher policy
must validate the top-level entry workflow identity. In first delivery, live
official PyPI publication uses this path.

The shared orchestration workflow still owns planning, build fan-out,
tag-gating, reusable-hosted publish fan-out, and production of the authoritative
execution sets. For `external-oidc-entry-workflow` members, however,
orchestration must return the selected `publish-node-id` values and supporting
artifact references to the selected top-level entry workflow. It must not
physically host those publish jobs and must not hide them behind
`release-publish-node.yml`. The entry workflow resumes after the orchestration
call and schedules a live publish matrix over exactly the returned selector
members.

The high-level handoff is:

1. `release-official.yml` or `release-buddy.yml` resolves the trusted dispatch
   ref, performs entry authorization, and invokes `release-orchestrate.yml` with
   the resolved `commit-sha` and normalized controls.
2. `release-orchestrate.yml` emits the frozen plan, `execution-sets.json`,
   synthetic skip receipts, build receipts and bundles, tag evidence, and any
   reusable-hosted publish results. Its workflow-call outputs or documented
   artifacts include the
   `active-publish-selectors.external-oidc-entry-workflow` array and enough
   stable artifact names for the caller to materialize each entry-hosted publish
   request without recomputing topology.
3. The top-level entry workflow creates an entry-hosted publish job only for the
   returned entry selector members. An empty selector is serialized and treated
   as a normal skipped matrix, not as a missing output.
4. Each entry-hosted publish job consumes the same frozen plan, referenced build
   receipts and bundles, and materialized `publish-request.json` contract as a
   reusable-hosted publish job for the same `publish-node-id` would consume.
5. Each entry-hosted publish job emits the same standard `publish-result.json`
   receipt and uploads it using the standard publish-result artifact contract.
   The logical key remains the original `publish-node-id`; the physical host is
   not part of publish identity.

For first-delivery PyPI official publication, the job that requests the PyPI
trusted-publishing OIDC token and performs the live upload must be a job in
`.github/workflows/release-official.yml` with the GitHub Actions environment
`release`. That token-requesting job must not run in
`.github/workflows/release-orchestrate.yml`, must not run in
`.github/workflows/release-publish-node.yml`, and must not be implemented as a
reusable workflow call whose called workflow mints the PyPI OIDC token. Internal
code reuse is allowed through repository scripts, libraries, or composite
actions shared with other publish paths, provided the PyPI OIDC token is minted
only by the top-level official entry workflow job.

Entry-hosted publication does not create a separate request or receipt schema.
It uses the same `publish-request.json` and `publish-result.json` contracts,
artifact naming rules, plan identity, build-result references, and failure
semantics as reusable-hosted publish paths. Shared helpers may materialize or
validate those files, but they must not re-read descriptors, reclassify registry
state, recompute topology, or change the physical workflow identity that mints
an external OIDC token.

Report aggregation waits for both sides of the publish fan-out: reusable-hosted
results produced inside orchestration and entry-hosted results produced after
the orchestration call returns to the top-level workflow. The report consumes the
union of reusable-hosted publish results, entry-hosted publish results, synthetic
skip receipts, diagnostics, and job conclusions. A missing expected
entry-hosted `publish-result.json` is reported the same way as a missing expected
reusable-hosted publish result for the corresponding logical `publish-node-id`.

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
all rows below emit the same `publish-result.json` shape and use the same
`publish-node-id` semantics.

| Topology path                     | Physical publish host                                                                                               | Token or authority boundary                                                                    | Executor contract                                                                                         |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `github-token`                    | Reusable publish job on the shared orchestration path; currently reusable-hosted per `execution-sets.json` routing. | Uses GitHub-provided `GITHUB_TOKEN`; no external OIDC token is minted.                         | Execute the planned GitHub-hosted publication with the same request and result contracts as other paths.  |
| `external-oidc-entry-workflow`    | Top-level entry workflow job.                                                                                       | External registry token is requested and minted in the entry workflow identity.                | The command and token request must stay entry-hosted, even if scripts or helpers are shared.              |
| `external-oidc-caller-workflow`   | Command may run in the reusable publish workflow.                                                                   | Registry validates the caller or top-level workflow identity for the trusted-publisher policy. | Preserve the caller/top-level identity boundary while consuming the standard materialized request.        |
| `external-oidc-reusable-workflow` | Reusable publish workflow job.                                                                                      | Registry trusts the reusable workflow identity where the registry supports that policy shape.  | Mint and use the external token only inside the reusable workflow identity selected by the plan topology. |

Shared scripts, composite actions, or libraries may be used by both entry-hosted
and reusable-hosted publish paths. That code reuse must not move the live upload,
credential request, or OIDC token minting step across the workflow identity
boundary selected by topology.

### Request Consumption and Guardrails

For each active publish node, workflow routing materializes one
`publish-request.json` for one logical `publish-node-id`. The executor consumes
that request as an instruction, not as a discovery seed. It may validate that
referenced plan slices, build receipts, package files, and bundle digests are
internally consistent, but validation failures must stop the publish rather than
falling back to source-tree or registry discovery.

Before any live upload starts, each package-registry publish executor must:

1. locate the receipted file for every planned `artifact-id` in the publish node;
2. apply only planner-frozen filename materialization rules for registries whose
   final distribution filename is part of the plan;
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

Every topology path emits exactly one positive `publish-result.json` for a
successful live publish of a logical node. The receipt is keyed by the original
`publish-node-id`; physical host, called workflow filename, runner operating
system, helper implementation, or token-minting location do not create a new
logical publish identity. Failure reporting therefore treats a missing
entry-hosted result and a missing reusable-hosted result the same way for the
same planned `publish-node-id`.

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
  `release-official.yml`, and environment `release`.

Required publish topology:

- The `pypi/pypi` target requires `external-oidc-entry-workflow`.
- The OIDC token request and the upload command run in the `official` entry
  workflow identity, not in `release-orchestrate.yml` and not in
  `release-publish-node.yml`.
- If an active official PyPI node is routed through a reusable workflow identity,
  the control plane or executor must fail closed before upload. That is a
  topology/configuration error, not an alternate PyPI publish path.

Planner adapter responsibilities:

- Resolve package identity from `[project].name` and serialize the PEP 503
  normalized name.
- Use Python packaging normalized version identity for remote comparison.
- Use the PyPI JSON API to observe existing release files and their SHA-256
  digests for the resolved project and version.
- Invoke the checked-in Hatchling build backend during planning to compute exact
  current-scope final distribution filenames. Current scope is one wheel plus an
  optional sdist from one variant; extra distribution members are out of scope
  unless the descriptor and plan model them explicitly.

Publish executor responsibilities:

- Request the PyPI trusted-publishing credential only in the entry-hosted live
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

### npmjs

Credential posture:

- npmjs uses npm trusted publishing with GitHub Actions OIDC for packages whose
  npmjs trusted publisher is configured. No long-lived npm automation token is a
  current-scope credential for npmjs live official publication.
- npm trusted publishing is package-scoped on npmjs.com; package owner-side
  enablement is required before the live official target is allowed to run.

Required publish topology:

- The `npm/npmjs` target requires `external-oidc-caller-workflow`.
- npmjs validates the caller or top-level workflow filename configured on
  npmjs.com. In first delivery that identity is `release-official.yml` with the
  `release` environment.
- The concrete `npm publish` command may run inside
  `release-publish-node.yml`, and the reusable job may request the OIDC token, as
  long as the caller/top-level identity boundary required by npmjs trusted
  publishing is preserved. The active caller chain is the top-level
  `release-official.yml` job that calls shared orchestration, followed by the
  `release-orchestrate.yml` publish job that calls `release-publish-node.yml`;
  `release-official.yml` does not directly call `release-publish-node.yml`.

Planner adapter responsibilities:

- Resolve the package identity from an explicit descriptor target projection
  override when present; otherwise use `package.json` `name`.
- Require current-scope npmjs package names to match the produced package
  identity. Do not project an unscoped npmjs package into an owner-scoped GitHub
  Packages name unless a target-specific artifact or transform receipt explicitly
  models the rewritten package contents, digest, and metadata.
- Use npm package metadata, such as `npm view --json`, to observe the exact
  package version and `dist.integrity`.

Publish executor responsibilities:

- Publish the receipted tarball to npmjs using trusted publishing from the
  planned topology.
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

- NuGet.org uses NuGet trusted publishing with GitHub Actions OIDC when the
  deferred NuGet.org target is explicitly enabled. No long-lived NuGet API key is
  a current-scope credential.
- First delivery defers live NuGet.org official publication for `hjg-pngcs` until
  the trusted-publishing policy, live-enable token, and `.snupkg` observation
  behavior are documented and tested.

Required publish topology:

- The conservative NuGet.org topology is `external-oidc-entry-workflow` with
  `release-official.yml` and the `release` environment.
- Do not assume or model reusable-workflow trusted publishing for NuGet.org until
  official NuGet.org behavior is verified and this design is updated.

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

- When NuGet.org is enabled, request the short-lived NuGet trusted-publishing API
  key in the entry-hosted live publish job shortly before upload because NuGet
  documents the temporary key as one-use and valid for one hour.
- Push exactly the planned `.nupkg` and, only when modeled and enabled, the
  planned `.snupkg` member.
- Fail and report the NuGet response if a target-side conflict, validation
  delay, or symbol-package problem occurs; do not attempt reconciliation.

Identity conformance:

- Read the `.nupkg` package metadata and compare `PackageId` plus normalized
  version to `resolved-publish-identity`.
- If a `.snupkg` member is modeled, verify that it corresponds to the same
  planner-frozen package identity and version before upload.

Live mutation boundary:

- The executor may create only the planned NuGet.org package-version members. It
  must not push an unmodeled `.snupkg`, substitute a different package source,
  retry with a long-lived API key, delete package versions, or derive package
  identity from file names.
- Until NuGet.org is explicitly enabled, descriptors must not declare a
  `nuget/nuget-org` live target for first-delivery `hjg-pngcs`; keep `.nupkg` and
  `.snupkg` artifacts modeled for GitHub Release evidence and publish only the
  separately supported GitHub Packages `.nupkg` member.

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
  workflow filename `release-publish-node.yml` with repository `hcoona/three` and
  environment `release`; separate workflow-repository owner/name fields remain
  blank unless a future cross-repository reusable workflow is introduced.
- The active permission chain is the `release-official.yml` caller job, the
  `release-orchestrate.yml` publish caller job, and the
  `release-publish-node.yml` child publish job. Because reusable workflows cannot
  elevate OIDC permissions, every active caller job that passes OIDC onward plus
  the child job that requests the RubyGems.org token must declare
  `id-token: write`; unrelated jobs must not.

Planner adapter responsibilities:

- Resolve package identity from evaluated `Gem::Specification.name`.
- Compare versions with RubyGems `Gem::Version`.
- Resolve release versions through build-system-integrated NBGV for every Ruby
  project in current scope; the gemspec must fail closed when NBGV cannot provide
  `SemVer2` rather than falling back to a static source-tree version.
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
  in Section 3.

Required publish topology:

- GitHub Release uses the `github-token` topology. Current routing may host the
  command in the reusable publish job, but the authority remains GitHub's token
  for the repository.

Planner adapter responsibilities:

- Query releases by the frozen `release-tag`.
- Normalize release state as `prerelease` or `release`.
- Normalize the asset set by asset name and label, then compare it to the
  planner-frozen `projection.asset-names-by-artifact-id` and
  `projection.asset-labels-by-artifact-id` maps.
- Classify exact matches, same-tag prerelease partials, and same-tag conflicts
  according to the replay matrix.

Publish executor responsibilities:

- `create-only`: create the release for the already verified tag and upload the
  exact planned asset set under the planner-frozen asset names.
- `overwrite-mutable`: converge the mutable prerelease to the frozen `buddy`
  intent when the planner authorized `FORCE`.
- `replace-authoritative`: converge the same-tag prerelease to the frozen
  official intent, including final release state, asset names, and asset labels.

Identity conformance:

- The GitHub Release identity is the planner-frozen `release-tag`, release state,
  asset names, and asset labels. It is not a package metadata identity.
- The executor must not derive alternate release asset names from bundle-relative
  paths, produced filenames, or executor-local packaging output.

Live mutation boundary:

- The executor may create or converge only the planned release and planned assets
  for the already verified tag. It may delete and recreate assets only when the
  plan mode authorizes an overwrite or authoritative replacement.
- It must not use release asset presence as a fresh skip decision and must never
  retarget tags; tag creation and verification remain the `ensure-tag`
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
- For first-delivery `hjg-pngcs`, publish only the reliably modeled GitHub
  Packages NuGet `.nupkg` member; keep `.snupkg` as GitHub Release evidence until
  GitHub Packages symbol-package behavior is documented and tested.

Identity conformance:

- The executor must compare the concrete package metadata to the
  planner-frozen `resolved-publish-identity` and target projection. A GitHub
  Packages host requirement does not permit unmodeled package renaming.
- The projected `npm/github-packages` path for `hexo-renderer-asciidoc` remains
  out of first-delivery live scope because the npmjs tarball is unscoped while
  GitHub Packages npm would require an owner-scoped package identity. A future
  path must model a target-specific artifact or transform receipt that records
  rewritten contents, digest, and metadata before upload.

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

| Job group                                      | Minimum permission intent                                                                                       |
| ---------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| Authorization, report, skip, and pure planning | `contents: read` only, unless the job has a narrower documented read need.                                      |
| Immutable proof lookup                         | Add `actions: read` only to the job that downloads or lists proof artifacts.                                    |
| Planner GitHub-hosted remote observation       | Add only the required read scopes, such as `packages: read`, to the planning job that performs that read.       |
| Tag verification only                          | `contents: read`; do not grant tag write permission when all required tags are already expected to exist.       |
| Tag creation                                   | `contents: write`, scoped only to the `ensure-tag` job when it may create missing release tags.                 |
| GitHub Release publication                     | `contents: write`, scoped only to the GitHub Release publish job that creates or converges releases or assets.  |
| GitHub Packages publication                    | `packages: write`, scoped only to the matching GitHub Packages publish job; add `contents: read` if required.   |
| External trusted publishing with GitHub OIDC   | `id-token: write`, scoped only according to the topology rules below; do not combine with unrelated write jobs. |
| External OIDC registry publication artifacts   | Add only the read permissions needed to download the planned artifacts and receipts before minting credentials. |

`id-token: write` placement is topology-specific:

- `external-oidc-entry-workflow`: grant `id-token: write` only to the
  entry-hosted live publish job that requests the external registry credential
  and performs the upload. PyPI uses this path in first delivery; NuGet.org stays
  on this conservative path until registry behavior is verified and this design
  is updated. Reusable orchestration, planning, build, tag, report, skip, GitHub
  Release, and GitHub Packages jobs must not receive this OIDC grant for that
  publish node.
- `external-oidc-caller-workflow`: grant the OIDC capability along the active
  caller-workflow-bound path only. For the current npmjs path, that active chain
  is `release-official.yml` -> `release-orchestrate.yml` ->
  `release-publish-node.yml`: the top-level caller job that invokes shared
  orchestration and the shared orchestration publish job that invokes the
  reusable publish workflow must include `id-token: write` when they must pass
  the OIDC capability onward, and the child reusable publish job that requests
  the OIDC token must also include `id-token: write`. This is not a
  workflow-wide grant; unrelated matrix entries and unrelated jobs must not
  receive it.
- `external-oidc-reusable-workflow`: grant `id-token: write` only along the
  active reusable-workflow-bound publish path. Because reusable workflows cannot
  elevate permissions above their caller jobs, RubyGems.org uses the full nested
  caller chain in current scope: the `release-official.yml` caller job invokes
  `release-orchestrate.yml`, whose publish caller job invokes
  `release-publish-node.yml`. Every active caller job in that chain that passes
  OIDC capability onward must grant and pass `id-token: write`, and the child
  reusable publish job that mints the registry token must also declare
  `id-token: write`. Unrelated orchestration jobs, unrelated matrix entries,
  planning, build, tag, report, skip, GitHub Release, and GitHub Packages jobs
  must not receive this OIDC grant.
- `github-token`: do not grant `id-token: write`. GitHub Release and GitHub
  Packages paths use `GITHUB_TOKEN` only, with `contents: write` or
  `packages: write` scoped to the live mutation job that needs that authority.

Planner-time remote observation must never run in a publish-credential context.
Planner adapters may use public registry reads and the least-privilege
`GITHUB_TOKEN` read permissions described above, but they must not:

- request external OIDC tokens;
- run inside publish jobs solely to obtain registry trust;
- access approval-gated `release` environment secrets;
- use long-lived publish credentials; or
- turn trusted-publisher readiness probing into planner remote observation.

`official` repository authorization and protected-environment approval are
distinct gates. The entry workflow must first verify that the actor has
`maintain+` repository permission for `official`, while `buddy` continues to
require `write+`. Passing that authorization check does not approve deployment:
each live side-effect job still waits for the protected `release` environment
when the selected run can mutate tags, GitHub Release, GitHub Packages, or an
external registry.

The GitHub environment named `release` attaches directly to jobs that can perform
live side effects, not to a separate approval-only job. Current scope has no
standalone approval job. Attach `environment: release` to:

- `ensure-tag`, only when it may create release tags for active GitHub Release
  publish nodes; read-only tag verification does not need the environment; and
- each live publish job that can mutate GitHub Release, GitHub Packages, or an
  external registry.

External trusted-publisher policies must be configured for the
topology-specific workflow identity that each registry validates and for the
same `release` environment. The operational setup checklist in Section 9 remains
the owner-side source of truth for those policies, while Section 10 defines the
acceptance evidence that proves the resulting jobs, permissions, environments,
and receipts behaved as intended.

## 9. External Setup and Readiness

Before live official publication is enabled, release infrastructure setup must
include this checklist:

| Surface                         | Required configuration                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| GitHub environment              | Environment named `release`, required reviewers configured, prevent self-review enabled, deployment branch or tag restrictions limited to trusted release refs, and native admin bypass left to repository policy.                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| NuGet.org trusted publishing    | Package owner-side trusted publisher entry for repository `hcoona/three`, conservative entry workflow file name `release-official.yml` with no `.github/workflows/` path, and environment `release`; required only when the deferred NuGet.org target is enabled.                                                                                                                                                                                                                                                                                                                                                                                            |
| PyPI trusted publishing         | Project owner-side trusted publisher entry, or pending publisher before first project creation, for each first-delivery PyPI project name. Configure repository owner `hcoona`, repository name `three`, workflow filename `release-official.yml` with no `.github/workflows/` path, and environment `release`. Do not configure `release-orchestrate.yml`, `release-publish-node.yml`, or any reusable workflow as the PyPI publisher.                                                                                                                                                                                                                      |
| npmjs trusted publishing        | Package owner-side trusted publisher entry for repository `hcoona/three`, caller/top-level workflow file name `release-official.yml` with no `.github/workflows/` path per npm trusted-publishing identity rules, and environment `release` where the package supports trusted publishing. When npm publish runs through `workflow_call`, the active caller chain is `release-official.yml` -> `release-orchestrate.yml` -> `release-publish-node.yml`; grant `id-token: write` to every active caller job in that chain that must pass the OIDC capability onward and to the child reusable publish job that requests the token, but not to unrelated jobs. |
| RubyGems.org trusted publishing | Gem owner-side trusted publisher entry for repository `hcoona/three`, reusable publish workflow filename `release-publish-node.yml`, same-repository workflow owner fields left blank, and environment `release`. When RubyGems.org publish runs through `workflow_call`, the active caller chain is the `release-official.yml` caller job -> `release-orchestrate.yml` publish caller job -> `release-publish-node.yml` child publish job; grant `id-token: write` to every active caller job in that chain that must pass the OIDC capability onward and to the child reusable publish job that requests the token, but not to unrelated jobs.             |
| GitHub Packages                 | No external OIDC trusted-publisher policy; publish jobs use `GITHUB_TOKEN` with the required package write permission.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |

Missing trusted-publisher configuration is a live publish failure surfaced by the
matching publish executor or credential acquisition step. The planner must not
probe those approval-gated trusted-publishing credentials during remote
observation. This setup is not required to implement or validate dry-run,
validation-build, GitHub Release, or GitHub Packages paths, but it is a
live-enable prerequisite for official external-registry publication and
acceptance evidence.

This checklist is an external operations readiness gate, not a reason to delay
the implementation of no-side-effect, GitHub Release, or GitHub Packages paths.
Official external-registry publication remains disabled for any target whose
trusted-publisher entry or `release` environment policy has not been configured
by a repository or package owner.

First delivery uses an explicit non-secret repository variable,
`THREE_RELEASE_ENABLED_EXTERNAL_OIDC_TARGETS`, as the control-plane live-enable
allowlist for official live publication to external OIDC registries. The value is
a comma or newline separated list of package-scoped enablement tokens. Each token
has this exact case-sensitive shape:

```text
<target-instance-ref>#<project-id>#<planner-frozen-package-name>
```

Examples include `npm/npmjs#hexo-renderer-asciidoc#hexo-renderer-asciidoc`,
`rubygems/rubygems-org#asciidoctor-latexmath#asciidoctor-latexmath`, and
`nuget/nuget-org#hjg-pngcs#IO.Github.Hcoona.Pngcs`. A missing or empty value
enables none. First delivery does not define a wildcard token; enabling a whole
external registry target would be too coarse because trusted-publisher readiness
is package-owner-side. This allowlist applies only to active `official` publish
nodes whose target-instance snapshot has `credential-posture: oidc`; GitHub
Release and GitHub Packages nodes are not gated by this variable.

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
supported through the entry-workflow-bound path in first delivery.

After that topology gate passes, PyPI live publication may still fail for real
readiness, configuration, credential, upload, or conformance reasons: for
example a missing live-enable token, a missing or mismatched PyPI publisher,
failure to mint a trusted-publishing credential, package metadata mismatch, a
target-side conflict, or an upload failure. Those failures must surface through
`REQ_EXTERNAL_TARGET_DISABLED`, the entry-hosted publish job conclusion, missing
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

1. Read all selected GitHub Release publish nodes from the frozen plan, including
   nodes whose frozen `publish-disposition` is `skip-satisfied`.
2. Compute the distinct required `release-tag` set and the subset of tags that
   are creation-eligible because at least one active GitHub Release publish node
   references that tag.
3. Query every existing tag in the full required set before creating any missing
   tag.
4. If any existing tag does not peel to the selected `commit-sha`, fail without
   creating tags.
5. If a required tag is missing but is not creation-eligible because it is needed
   only by `skip-satisfied` GitHub Release nodes, fail without creating tags.
6. After the full precheck passes, create every missing creation-eligible tag at
   the selected commit.

Newly created release tags are lightweight tags that point directly at the
selected commit. Existing annotated tags are accepted only when peeling the tag
object resolves to the selected commit. The job must never retarget an existing
tag and must never treat a tag object that points elsewhere as satisfying the
selected commit requirement.

The job must not run when dry-run is true or when the selected publish set
contains no GitHub Release publication. If selected GitHub Release nodes are all
`skip-satisfied`, the job is read-only tag verification and must not create
missing tags.

When the job succeeds, it must emit exactly one `tag-result.json` covering every
distinct required release tag. If any existing tag fails the peel-to-commit
precheck, the job emits no positive tag result and creates no missing tags.

### First-Delivery Author-Time Input Project Set

The first delivery should generate project descriptors for a deliberately small
project set that covers the release system's required ecosystem and artifact
shapes without turning first implementation into bulk descriptor migration.

| Coverage category                | Project id                | Descriptor root                            | Primary manifest or build entry point                          | Required coverage                                                                                                                                                                                                  |
| -------------------------------- | ------------------------- | ------------------------------------------ | -------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| C# packable library              | `hjg-pngcs`               | `src/public/lib/Hjg.Pngcs/`                | `Hjg.Pngcs.csproj`                                             | Windows `dotnet pack`, `.nupkg`, `.snupkg`, GitHub Release assets, and GitHub Packages NuGet for `.nupkg`; NuGet.org and GitHub Packages `.snupkg` publication are deferred until `.snupkg` observation is proven. |
| C# private app binary            | `qidian-novel-downloader` | `src/private/app/qidian-novel-downloader/` | `QidianNovelDownloader.csproj`                                 | Nonpackable app release, `dotnet publish` binary artifact, private-app first-delivery scope.                                                                                                                       |
| C# public app installer          | `image-occlusion-editor`  | `src/public/app/ImageOcclusionEditor/`     | `ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj` | Executor-internal WinUI publish output plus `installer/installer/inno-setup` produced from that output; first-delivery GitHub Release publication includes the installer artifact only.                            |
| Python special version authority | `nbgv-python`             | `src/public/lib/nbgv-python/`              | `pyproject.toml`                                               | `nbgv-python-pyproject-version` exception, Hatchling wheel plus optional sdist, GitHub Release publication, and live PyPI publication through the entry-workflow topology.                                         |
| Python normal NBGV/Hatch package | `hcoona-release-smoke`    | `src/public/lib/hcoona-release-smoke/`     | `pyproject.toml`                                               | Normal Python build-system NBGV integration through Hatchling, separate from the `nbgv-python` exception path; live PyPI publication uses the entry-workflow topology gate.                                        |
| Node npm package                 | `hexo-renderer-asciidoc`  | `src/public/lib/hexo-renderer-asciidoc/`   | `package.json`                                                 | pnpm/npm packaging, GitHub Release evidence, and npmjs trusted publishing; GitHub Packages npm projection is deferred until target-specific npm package artifacts or transform receipts are designed.              |
| Ruby gem                         | `asciidoctor-latexmath`   | `src/public/lib/asciidoctor-latexmath/`    | `asciidoctor-latexmath.gemspec`                                | RubyGems build and publication target shapes.                                                                                                                                                                      |

This set intentionally excludes first-delivery descriptors for private WXT or
browser-extension packages, archive-only artifacts, metadata-only artifacts,
tool/generator-specific release kinds, and multi-wheel or platform-specific
Python wheel layouts. Those cases may be added in later descriptor migration
work after the current release workflow contracts are implemented and validated.

Although the requirements baseline keeps both current private apps in confirmed
scope under the descriptor-gated participation rule, the first generated
author-time input batch intentionally includes only
`qidian-novel-downloader` for private-app coverage. A descriptor for
`src/private/app/vscode-copilot-telegram-hook/` is explicitly deferred to a later
descriptor-migration batch unless this low-level project set is updated.

## 10. Acceptance Traceability

Implementation should maintain a trace table in tests or CI reports with this
minimum shape:

| Scenario                               | Fixture anchor                                                                                                                                  | Required evidence                                                                                                                                                                                                                                                                                                                                                                                                        |
| -------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| C# library package build and release   | `src/public/lib/Hjg.Pngcs/`                                                                                                                     | Descriptor, plan snapshot, Windows build receipt for `.nupkg` and `.snupkg`, and GitHub Release publish or skip receipt for both modeled assets.                                                                                                                                                                                                                                                                         |
| GitHub Packages NuGet publication      | `src/public/lib/Hjg.Pngcs/`                                                                                                                     | Descriptor target `nuget/github-packages`, target-instance snapshot, Windows build receipt for the `.nupkg` member, `packages: write` GitHub Packages publish or skip receipt, and no live `.snupkg` package-registry side effect.                                                                                                                                                                                       |
| C# app `dotnet publish` binary         | `src/private/app/qidian-novel-downloader/`                                                                                                      | Descriptor, plan snapshot, Windows build receipt for binary artifact, GitHub Release evidence.                                                                                                                                                                                                                                                                                                                           |
| C# app Inno installer                  | `src/public/app/ImageOcclusionEditor/`                                                                                                          | Descriptor, plan snapshot, Windows build receipt for the `installer` artifact, acceptance packaging evidence artifact/log proving Inno Setup consumed the executor-internal WinUI publish output, and GitHub Release evidence for the installer.                                                                                                                                                                         |
| Python package including `nbgv-python` | `src/public/lib/nbgv-python/`                                                                                                                   | Descriptor with special version authority, plan snapshot with frozen version, build metadata conformance, GitHub Release evidence, and PyPI live evidence when the descriptor targets `pypi/pypi`.                                                                                                                                                                                                                       |
| Python normal NBGV/Hatch package       | `src/public/lib/hcoona-release-smoke/`                                                                                                          | Descriptor with build-system NBGV version authority, plan snapshot with frozen version, build metadata conformance, GitHub Release evidence, and PyPI live evidence when the descriptor targets `pypi/pypi`.                                                                                                                                                                                                             |
| First-delivery PyPI official publish   | At least one first-delivery Python fixture with an enabled `pypi/pypi` official target                                                          | Real `official` run evidence showing PyPI Trusted Publisher setup for workflow filename `release-official.yml` and environment `release`, `execution-sets.json` selecting the node under `external-oidc-entry-workflow`, an entry-hosted `publish-request.json`, a successful PyPI `publish-result.json` with uploaded filenames and project/version URL, and PyPI-side release-file evidence for the published version. |
| Node package build and release         | `src/public/lib/hexo-renderer-asciidoc/`                                                                                                        | Descriptor, plan snapshot, npm pack receipt, and GitHub Release publish or skip receipt for the packed npm artifact.                                                                                                                                                                                                                                                                                                     |
| npmjs trusted publication              | `src/public/lib/hexo-renderer-asciidoc/`                                                                                                        | Descriptor target `npm/npmjs`, target-instance snapshot, npm pack receipt, OIDC trusted-publisher setup for the caller/top-level workflow identity and environment `release`, plus npmjs publish or skip receipt.                                                                                                                                                                                                        |
| Ruby gem build and release             | `src/public/lib/asciidoctor-latexmath/`                                                                                                         | Descriptor, plan snapshot, gem build receipt, and GitHub Release publish or skip receipt for the `.gem` artifact.                                                                                                                                                                                                                                                                                                        |
| GitHub Packages RubyGems publication   | `src/public/lib/asciidoctor-latexmath/`                                                                                                         | Descriptor target `rubygems/github-packages`, target-instance snapshot, gem build receipt, `packages: write` GitHub Packages publish or skip receipt.                                                                                                                                                                                                                                                                    |
| RubyGems.org trusted publication       | `src/public/lib/asciidoctor-latexmath/`                                                                                                         | Descriptor target `rubygems/rubygems-org`, target-instance snapshot, gem build receipt, OIDC trusted-publisher setup for workflow filename `release-publish-node.yml`, same-repository workflow owner fields, environment `release`, and publish or skip receipt.                                                                                                                                                        |
| Multi-project dispatch                 | Any two fixture anchors above                                                                                                                   | One run report showing normalized selected projects and multiple project-scoped publish nodes.                                                                                                                                                                                                                                                                                                                           |
| Dry-run                                | Any fixture anchor above                                                                                                                        | Run report proving no tags, approval, or publish jobs ran.                                                                                                                                                                                                                                                                                                                                                               |
| Validation build                       | `src/public/lib/nbgv-python/` plus at least one package fixture                                                                                 | Dry-run report plus validation-only build receipts excluded from immutable proof lookup.                                                                                                                                                                                                                                                                                                                                 |
| Rerun skip                             | Any GitHub Release fixture or immutable package fixture with prior admissible proof                                                             | Planner diagnostics or plan snapshot proving `skip-satisfied` and synthetic skip receipt.                                                                                                                                                                                                                                                                                                                                |
| Rerun after partial success            | Any multi-target package fixture above                                                                                                          | First non-cancelled failed run report showing at least one completed positive side-effect receipt before later failure, followed by a rerun plan/report that preserves already completed evidence and applies planner-owned skip or fail-closed rules.                                                                                                                                                                   |
| Buddy to official promotion            | Any GitHub Release fixture above                                                                                                                | `buddy` publish evidence followed by same-commit `official` plan snapshot with `replace-authoritative`, tag-result verification for the same release tag, and publish evidence for the final release state, asset set, and labels.                                                                                                                                                                                       |
| Direct official publication            | Any GitHub Release fixture above                                                                                                                | `official` run with no prior `buddy` evidence for the same project-scoped version, protected-environment approval evidence, `create-only` plan snapshot, tag result, and publish result.                                                                                                                                                                                                                                 |
| Immutable partial replay               | NuGet multi-member fixture, current-scope PyPI wheel-plus-sdist fixture, or future PyPI multi-wheel fixture, real or mocked at adapter boundary | Planner diagnostic proving fail-closed behavior for a same-identity partial case while keeping broader PyPI multi-wheel support deferred.                                                                                                                                                                                                                                                                                |
| Cancellation                           | Workflow-level integration fixture                                                                                                              | GitHub cancelled conclusion plus any already persisted positive receipts; in-run report artifact is best-effort because platform cancellation may prevent the final report job from starting.                                                                                                                                                                                                                            |
| External OIDC live enablement          | PyPI, npmjs, RubyGems.org, or future NuGet.org official fixture                                                                                 | Disabled-token run fails before plan artifact publication, protected-environment approval, tags, or publish jobs with `REQ_EXTERNAL_TARGET_DISABLED`; enabled-token run reaches the normal official approval and publish path.                                                                                                                                                                                           |
| Unsupported OIDC topology gate         | Synthetic catalog fixture with an unsupported external OIDC topology                                                                            | Run fails before plan artifact publication, protected-environment approval, tags, OIDC token acquisition, or publish jobs with `REQ_EXTERNAL_TOPOLOGY_BLOCKED`.                                                                                                                                                                                                                                                          |
| Approval boundary                      | Workflow-level integration fixture                                                                                                              | `buddy` explicit `write+` authorization with no approval, `official` `maintain+` authorization, required-review run, self-review prevention, and admin bypass behavior when enabled.                                                                                                                                                                                                                                     |

The trace table may live in test fixtures or generated CI output. It does not
need to become a new operator-facing release record.

The default implementation choice is to keep the trace table with the executable
acceptance fixtures or generated CI evidence. A missing row for any first-
delivery scenario is an acceptance-coverage gap before declaring the workflow
implementation complete, but it does not reopen design or block initial coding.

## 11. Implementation-Owned Boundaries

These details remain owned by implementation:

- exact planner implementation language and internal module boundaries;
- exact JSON schema library and test harness;
- exact wrapper scripts and helper names under `eng/`;
- exact retry counts and backoff timings, as long as retry is bounded and failure
  remains fail-closed;
- internal bundle directory layout beyond receipted bundle-relative paths;
- exact Markdown wording in the final run summary.

These details may change without reopening design if the frozen descriptor, plan,
workflow, executor, receipt, proof, and registry-observation contracts above
remain intact.

## 12. Consistency Review

Group 12 owns the final cross-section review after the topology, publish,
registry, permissions, readiness, and acceptance groups complete. That pass must
check that this page uses one coherent vocabulary for topology partitions,
workflow identity, permissions, request and receipt files, external readiness,
acceptance evidence, and implementation-owned boundaries.

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
- [Workflow Release Architecture Model](./workflow-release-architecture-model.md)
- [Workflow Release Descriptor Schema](./workflow-release-descriptor-schema.md)
- [Workflow Release Plan Shape](./workflow-release-plan-shape.md)
- [Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md)
- [Workflow Release Design Layering and Implementation Handoff Scope](./workflow-release-design-layering-and-handoff-scope.md)
- [Workflow Release OIDC Publish Topology Research](./workflow-release-oidc-publish-topology.md)
- [Workflow Release Deferred PyPI Multi-Wheel Support](./workflow-release-deferred-pypi-multi-wheel-support.md)
