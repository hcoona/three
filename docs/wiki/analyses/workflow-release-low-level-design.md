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
`release-official.yml` with the `release` environment. For this topology,
`id-token: write` must be granted through both the caller/parent workflow path
and the called publish job that requests the token.

RubyGems.org trusted publishing can trust the reusable workflow identity where
configured. For the current same-repository reusable-publish topology, configure
RubyGems.org with workflow filename `release-publish-node.yml`, leave separate
workflow-repository owner/name fields blank, and use the `release` environment.

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

| Job               | Physical host                                                                                           | Required inputs                                                                                                                         | Required outputs                                                                         |
| ----------------- | ------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| `authorize-entry` | top-level entry workflow or the called orchestration workflow before planning                           | GitHub event context, selected profile                                                                                                  | Authorization conclusion and normalized run metadata.                                    |
| `plan`            | shared orchestration workflow                                                                           | Pinned checkout at `commit-sha`, normalized planner request, prior proof lookup service, raw dry-run controls, external live-enable map | Frozen plan, `execution-sets.json`, and synthetic skip-result artifacts, or diagnostics. |
| `build`           | shared orchestration workflow calling the reusable build unit                                           | Plan artifact, one `variant-id` per matrix row                                                                                          | Variant bundle, `build-result`, and optional immutable-proof artifacts.                  |
| `ensure-tag`      | control-plane job before publish fan-out                                                                | Frozen plan plus selected and active GitHub Release publish nodes                                                                       | `tag-result.json` tag verification or creation evidence.                                 |
| `publish`         | shared orchestration for reusable-hosted selectors; top-level entry workflow for entry-hosted selectors | Plan artifact, one `publish-node-id` per matrix row, referenced build receipts, and a materialized `publish-request.json`               | `publish-result` artifacts.                                                              |
| `report`          | top-level entry workflow after any entry-hosted publish jobs complete                                   | Plan, diagnostics, tag results, build results, skip results, publish results from all topology paths, job conclusions                   | Final operator summary.                                                                  |

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

The machine-readable selector file is `execution-sets.json` with this closed
top-level shape:

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
   by its frozen `target-instance-snapshot.capabilities.publish-topology` value.
   Empty topology arrays are serialized as `[]`. The shared orchestration
   workflow consumes reusable-hosted partitions, including caller-workflow-bound
   selectors whose registry validates the caller/top-level identity and
   reusable-workflow-bound selectors whose registry supports reusable identity.
   The top-level entry workflow consumes only entry-workflow-bound selectors
   after the orchestration call returns. A first-delivery `pypi/pypi` official
   publish node must therefore appear only in
   `active-publish-selectors.external-oidc-entry-workflow`; it must not be
   selected by target-family guessing, by a PyPI-specific side list, or by the
   reusable workflow partition.
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
cancelled before the platform can schedule that job.

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

The current PyPI entry-hosted publish path is captured across the workflow
identity, routing, publish executor, registry adapter, and permissions sections.
Group 5 owns any later dedicated rewrite of this path. Until that pass, the
binding rule is that entry-workflow-bound publish nodes are scheduled by the
top-level entry workflow and must not be routed through the reusable publish
workflow.

## 6. Publish Executor Design

Publish executors are selected from `target-instance-snapshot.family`.

Before any live upload starts, each package-registry publish executor must:

1. locate the receipted file for every `artifact-id` in the publish node;
2. for NuGet and PyPI, stage or rename each file so its basename equals the
   planner-frozen final distribution filename for that `artifact-id`;
3. read package metadata from the concrete file;
4. verify package name and version against
   `publish-node.resolved-publish-identity` under the family equivalence rules;
5. fail closed if metadata cannot be read or compared unambiguously.

Publish executors must not perform destination preflight queries to decide
whether to skip, overwrite, promote, or reconcile. Any destination call before
upload must be strictly necessary to carry out the already frozen publish action,
such as obtaining a short-lived trusted-publishing credential.

## 7. Registry Adapter Partitioning

### GitHub Release

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

The executor may delete and recreate assets only when the plan mode authorizes an
overwrite or authoritative replacement. It must not use release asset presence as
a fresh skip decision, and it must not derive alternate release asset names from
bundle-relative paths, produced filenames, or executor-local packaging output.

### NuGet

Planner adapter responsibilities:

- Resolve package identity from evaluated `PackageId`; do not fall back to
  `AssemblyName` or directory name.
- Use NuGet normalized version identity for remote version comparison.
- For `.nupkg`, use the NuGet V3 service index plus PackageBaseAddress and
  registration resources for public nuget.org observation where possible.
- For GitHub Packages NuGet, use GitHub-hosted read access with least-privilege
  `GITHUB_TOKEN` when public registry reads are insufficient.
- For NuGet.org `.snupkg`, use a documented symbol-package observation path and
  test its asynchronous validation and indexing behavior. The ordinary package
  content API documents `.nupkg` content, while symbol packages are published to
  NuGet's symbol-server path and can undergo asynchronous validation.

Because the repo-wide .NET pack configuration produces `.snupkg` for packable
library packages, current .NET package descriptors should model that symbol
package as a canonical artifact for GitHub Release evidence and for future
NuGet.org publication. First delivery does not require GitHub Packages NuGet to
publish `.snupkg`; GitHub Packages NuGet buddy publication uses only the modeled
`.nupkg` member until the implementation verifies that `.snupkg` members are
publishable and observable with the same fail-closed replay guarantees. This
follows NuGet's modern symbol-package model rather than the legacy
`.symbols.nupkg` format or embedding portable PDBs into the primary package.

GitHub Packages NuGet `.snupkg` support remains an implementation-time adapter
verification point. If the implementation cannot publish and observe `.snupkg`
members through GitHub Packages with the same fail-closed replay guarantees as
the primary `.nupkg`, current scope must not treat GitHub Packages `.snupkg` as
a required live package-registry member. In that case, keep `.snupkg` modeled for
GitHub Release evidence and continue publishing only the reliably observable
NuGet package-registry members until GitHub Packages symbol-package behavior is
documented and tested.

First delivery defers live NuGet.org official publication for `hjg-pngcs`
instead of accepting an unproven `.snupkg` observation path. Until that path is
documented and tested, the first-delivery `hjg-pngcs` acceptance scope covers the
modeled `.nupkg` and `.snupkg` artifacts through GitHub Release and GitHub
Packages NuGet `.nupkg` publication only, and the first-delivery `hjg-pngcs`
descriptor must not declare a `nuget/nuget-org` target or a GitHub Packages
NuGet `.snupkg` member. When NuGet.org publication or GitHub Packages `.snupkg`
publication is later enabled, the planner must keep the `.nupkg` and `.snupkg`
as separate planned artifacts and the publish executor must publish both when the
descriptor references both members; it must not silently publish an untracked
`.snupkg` side effect.

Publish executor responsibilities:

- For `nuget.org`, use NuGet trusted publishing through GitHub OIDC. Request the
  short-lived NuGet credential shortly before upload because NuGet documents the
  temporary API key as one-use and valid for one hour.
- For GitHub Packages NuGet, configure the package source with
  `GITHUB_TOKEN` and `packages: write`.
- Push the exact planned `.nupkg` and optional `.snupkg` members. If a target-side
  conflict occurs despite planner classification, fail and report the registry
  response instead of attempting reconciliation.

### PyPI

Planner adapter responsibilities:

- Resolve package identity from `[project].name` and serialize the PEP 503
  normalized name.
- Use Python packaging normalized version identity for remote comparison.
- Use the PyPI JSON API to observe existing release files and their SHA-256
  digests for the resolved project and version.
- Invoke the checked-in Hatchling build backend during planning to compute exact
  current-scope final distribution filenames, limited to one wheel plus optional
  sdist from one variant.

First delivery supports PyPI descriptor validation, planner-time filename
computation, build conformance, GitHub Release evidence for Python artifacts, and
live official PyPI publication through the entry-workflow-bound topology frozen
in the `pypi/pypi` target-instance snapshot. A valid active official `pypi/pypi`
node must not be routed through the reusable `release-publish-node.yml` publish
unit or rejected as a normal topology block.

PyPI live-publish responsibilities:

- Use PyPI Trusted Publishing with GitHub Actions OIDC.
- Run in the top-level `official` entry workflow identity configured on PyPI, not
  in `release-orchestrate.yml` or `release-publish-node.yml`.
- Upload only the frozen wheel and optional sdist members under the exact
  planner-frozen filenames.
- Fail before upload if wheel `METADATA` or sdist `PKG-INFO` does not match the
  frozen normalized package name and version.
- Emit the standard `publish-result.json` receipt with PyPI evidence, such as the
  project release URL and uploaded distribution filenames, after a successful
  live upload.

### npm

Planner adapter responsibilities:

- Resolve the final package name from descriptor projection or `package.json`
  `name`.
- Require lowercase current-scope names; scoped GitHub Packages names must match
  the catalog owner scope.
- Use npm package metadata, such as `npm view --json`, to observe the exact
  package version and `dist.integrity`.

Publish executor responsibilities:

- For npmjs, use npm trusted publishing with GitHub Actions OIDC when the package
  has a trusted publisher configured. npm trusted publishing also publishes
  provenance automatically with current npm CLI support.
- For GitHub Packages npm, use `GITHUB_TOKEN` with `packages: write`.
- Publish the receipted tarball under the frozen package name.
- Verify the packed package's `package/package.json` name and version before
  upload.

First delivery does not support live npm publication under a target-side package
name that differs from the receipted tarball's `package/package.json` `name`.
That excludes the projected `npm/github-packages` path for
`hexo-renderer-asciidoc`, whose npmjs package name is unscoped while GitHub
Packages npm would require an owner-scoped name. A future implementation may add
that path only by modeling a target-specific build artifact or a post-build
transform receipt that records the rewritten package contents, digest, and
metadata before upload.

### RubyGems

Planner adapter responsibilities:

- Resolve package identity from evaluated `Gem::Specification.name`.
- Compare versions with RubyGems `Gem::Version`.
- Resolve release versions through build-system-integrated NBGV for every Ruby
  project in current scope; the gemspec must fail closed when NBGV cannot provide
  `SemVer2` rather than falling back to a static source-tree version.
- For RubyGems.org, use the RubyGems.org API for version and digest observation.
- For GitHub Packages RubyGems, use GitHub-hosted read access or `gem fetch`
  with `GITHUB_TOKEN` where needed.

Publish executor responsibilities:

- For RubyGems.org, use RubyGems Trusted Publishing with GitHub Actions OIDC.
- For GitHub Packages RubyGems, configure RubyGems credentials with
  `GITHUB_TOKEN` and publish to the owner-scoped GitHub Packages host.
- Verify the built gem specification name and version before upload.

## 8. Permissions and Environment

Use job-level least privilege rather than a broad workflow-level write token.

| Job group                                   | Minimum permission intent                                                     |
| ------------------------------------------- | ----------------------------------------------------------------------------- |
| Planning without GitHub-hosted remote reads | `contents: read`, `actions: read` when proof lookup is needed.                |
| GitHub-hosted remote reads                  | Add only the required read scopes, such as `packages: read` where applicable. |
| Tag creation                                | `contents: write`, scoped to the `ensure-tag` job.                            |
| GitHub Release publication                  | `contents: write`, scoped to the GitHub Release publish job.                  |
| GitHub Packages publication                 | `packages: write`, scoped to the matching publish job.                        |
| Trusted publishing to external registries   | `id-token: write`, scoped to the matching publish job.                        |

For `external-oidc-caller-workflow` publish nodes, this table means both sides of
the reusable-workflow call boundary, not a workflow-wide grant. The caller or
parent job that invokes the reusable publish path must grant `id-token: write` so
GitHub allows OIDC capability to flow through `workflow_call`, and the child
`release-publish-node.yml` publish job that actually requests the OIDC token must
also grant `id-token: write`. In the current npmjs topology, apply that
permission only to the active npmjs caller-workflow-bound publish path; do not add
it to unrelated planner, build, tag-verification, report, GitHub Release, GitHub
Packages, or `skip-satisfied` jobs.

`official` live side-effect jobs must use the GitHub environment named
`release`, with required reviewers and prevent-self-review enabled. The
`release` environment is referenced only by jobs that can perform live external
side effects after planning and validation-build work have completed:

- `ensure-tag`, when it may create tags for active GitHub Release publish nodes;
- each live `publish` matrix job.

There is no separate approval-only job in current scope. External trusted
publisher policies must be configured for the topology-specific workflow
identity that each registry validates for the OIDC token and for the same
`release` environment, so the token-requesting job is also constrained by the
registry-side environment policy. PyPI uses the official entry workflow identity
in first delivery, NuGet.org remains conservative entry-workflow-bound until
registry verification proves otherwise, npmjs uses the caller/top-level workflow
identity required by npm trusted publishing while the publish job can remain
reusable-hosted, and RubyGems.org uses its reusable-workflow topology with
registry support for reusable identity. Planner-time remote observation remains
unable to access approval-gated secrets or OIDC publish jobs.

## 9. External Setup and Readiness

Before live official publication is enabled, release infrastructure setup must
include this checklist:

| Surface                         | Required configuration                                                                                                                                                                                                                                                                                                                                                                                                                      |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| GitHub environment              | Environment named `release`, required reviewers configured, prevent self-review enabled, deployment branch or tag restrictions limited to trusted release refs, and native admin bypass left to repository policy.                                                                                                                                                                                                                          |
| NuGet.org trusted publishing    | Package owner-side trusted publisher entry for repository `hcoona/three`, conservative entry workflow file name `release-official.yml` with no `.github/workflows/` path, and environment `release`; required only when the deferred NuGet.org target is enabled.                                                                                                                                                                           |
| PyPI trusted publishing         | Project owner-side trusted publisher entry, or pending publisher before first project creation, for each first-delivery PyPI project name. Configure repository owner `hcoona`, repository name `three`, workflow filename `release-official.yml` with no `.github/workflows/` path, and environment `release`. Do not configure `release-orchestrate.yml`, `release-publish-node.yml`, or any reusable workflow as the PyPI publisher.     |
| npmjs trusted publishing        | Package owner-side trusted publisher entry for repository `hcoona/three`, caller/top-level workflow file name `release-official.yml` with no `.github/workflows/` path per npm trusted-publishing identity rules, and environment `release` where the package supports trusted publishing. When npm publish runs through `workflow_call`, grant `id-token: write` on the caller/parent job path and on the child reusable publish job only. |
| RubyGems.org trusted publishing | Gem owner-side trusted publisher entry for repository `hcoona/three`, reusable publish workflow filename `release-publish-node.yml`, same-repository workflow owner fields left blank, and environment `release`.                                                                                                                                                                                                                           |
| GitHub Packages                 | No external OIDC trusted-publisher policy; publish jobs use `GITHUB_TOKEN` with the required package write permission.                                                                                                                                                                                                                                                                                                                      |

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
