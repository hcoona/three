# Workflow Release Low-Level Design

## Purpose

This page is the lower-layer design handoff for implementing workflow-based
release after the requirements, architecture, descriptor-schema, plan-shape, and
workflow-boundary pages have been sealed for current scope.

The target reader is a senior implementer. This page therefore freezes concrete
realization seams that affect correctness, testability, and external registry
configuration, but it does not prescribe every internal class, function, or shell
line.

## Inputs Already Frozen

This page does not reopen the upper-layer or middle-layer design. It consumes
these existing contracts as authoritative:

- `src/**/three.release.yml` project descriptors and
  `eng/release/target-instances.yml` are the only author-time release files.
- The planner emits one `three.release.plan/v1alpha1` artifact with an envelope
  and normalized graph.
- The control plane fans out at exactly two execution granularities: one build
  unit per `variant-id` and one publish unit per `publish-node-id`.
- Executors consume materialized requests and must not rediscover descriptors,
  targets, publish identity, replay policy, or overwrite policy.
- Planner-time remote observation uses public reads where possible and otherwise
  only least-privilege `GITHUB_TOKEN` reads for GitHub-hosted surfaces. It never
  uses publish credentials or approval-gated environment secrets.
- Current-scope immutable proof reuse is limited to unexpired GitHub Actions
  artifacts under the platform retention window.

## Low-Level Design Summary

| Area                      | Low-level decision                                                                                                                                                    |
| ------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Workflow files            | Use stable checked-in workflow filenames because OIDC trusted-publisher policies match workflow identity.                                                             |
| Entry authorization       | Explicitly check `buddy` as `write+` and `official` as `maintain+` before planning; do not enable duplicate-run auto-cancellation.                                    |
| Planner host              | Expose the planner through a repo-owned CLI contract; the implementation language remains implementation-owned.                                                       |
| Request and receipt files | Serialize all cross-job machine data as UTF-8 JSON with LF line endings and stable `api-version` plus `kind`.                                                         |
| Dry-run builds            | Dry-run does not build by default. A separate `validation-build` input may run build units, but its receipts are validation-only and inadmissible as immutable proof. |
| Build proof lookup        | Publish one small proof artifact per immutable-proof member binding so future planner runs can query by exact artifact name.                                          |
| Tag orchestration         | Create lightweight release tags and verify existing tags by peeling annotated tags to the selected commit.                                                            |
| External setup            | Require the `release` environment and registry trusted-publisher policies to target the stable publish workflow and environment.                                      |
| Diagnostics               | Use a small registered planner-code vocabulary plus a registration rule for new codes.                                                                                |
| Diagnostics artifact      | Serialize planner diagnostics through one closed container object rather than a raw array, NDJSON stream, or ad hoc log file.                                         |
| Execution sets            | Materialize matrix selectors in one closed JSON object so empty dry-run, validation-build, zero-target, and all-skip runs have deterministic workflow behavior.       |
| Failure reporting         | Treat success and skip receipts as positive evidence only; failed or cancelled jobs are summarized from job conclusions plus missing expected receipts.               |
| Registry adapters         | Keep remote observation in planner adapters, live mutation in publish executors, and package metadata conformance in publish executors before upload.                 |
| Acceptance                | Maintain a trace table from each acceptance scenario to descriptors, plans, receipts, registry evidence, and workflow conclusions.                                    |

## Workflow File Layout

Current-scope workflow files should use these stable paths:

| File                                          | Trigger or call shape | Stable responsibility                                                      |
| --------------------------------------------- | --------------------- | -------------------------------------------------------------------------- |
| `.github/workflows/release-buddy.yml`         | `workflow_dispatch`   | `buddy` entry workflow.                                                    |
| `.github/workflows/release-official.yml`      | `workflow_dispatch`   | `official` entry workflow, including early actor-permission authorization. |
| `.github/workflows/release-orchestrate.yml`   | `workflow_call`       | Shared orchestration workflow for one selected profile run.                |
| `.github/workflows/release-build-variant.yml` | `workflow_call`       | One reusable build unit for one `variant-id`.                              |
| `.github/workflows/release-publish-node.yml`  | `workflow_call`       | One reusable publish unit for one `publish-node-id`.                       |

These filenames are intentionally part of the low-level design because NuGet.org,
npm, and RubyGems.org trusted-publisher policies are configured against GitHub
Actions workflow identity. The job that requests an OIDC token must stay in this
repository. When a publish job runs through the reusable publish workflow, the
trusted-publisher policy must be configured for the workflow file that mints the
OIDC token, currently `release-publish-node.yml`, not merely the top-level entry
workflow. GitHub's OIDC token includes reusable-workflow identity in
`job_workflow_ref`, and RubyGems explicitly documents reusable-workflow
configuration.

The implementer may refactor internal scripts and helper actions, but changing
these workflow file names after registry policies exist is a release-infra
migration, not a harmless rename.

## Entry Workflow Inputs

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

## Entry Authorization and Duplicate-Run Concurrency

`authorize-entry` is a control-plane gate and runs before planner execution for
both profiles.

Current-scope authorization policy:

| Profile    | Required triggering-actor repository permission | Approval behavior                                        |
| ---------- | ----------------------------------------------- | -------------------------------------------------------- |
| `buddy`    | `write` or higher                               | no extra approval                                        |
| `official` | `maintain` or higher                            | protected `release` environment on live side-effect jobs |

The implementation must perform the permission check explicitly through the
GitHub API rather than relying only on the workflow dispatch UI. If the
permission check cannot resolve the actor's effective repository permission, or
if the resolved permission is below the selected profile's threshold, the run
fails closed before planning. When a machine-readable diagnostic is emitted for
that failure, it uses the planner-diagnostic file contract with
`REQ_ACTOR_UNAUTHORIZED`.

Current scope does not adopt native duplicate-run auto-cancellation. Entry
workflows and the shared orchestration workflow must not configure
`cancel-in-progress: true` for release runs. If the implementation needs a
GitHub Actions concurrency key to serialize same-commit release work, it must use
a key derived from the selected entry workflow plus resolved `commit-sha` and
must set `cancel-in-progress: false`. Cancellation therefore remains manual
operator cancellation or ordinary platform cancellation, not a repo-defined
supersession protocol.

## Orchestration Job Realization

The shared orchestration workflow should implement the middle-layer job sequence
with these concrete data handoffs:

| Job                     | Required inputs                                                                         | Required outputs                                                        |
| ----------------------- | --------------------------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| `authorize-entry`       | GitHub event context, selected profile                                                  | Authorization conclusion and normalized run metadata.                   |
| `plan`                  | Pinned checkout at `commit-sha`, normalized planner request, prior proof lookup service | Frozen plan artifact or planner diagnostics.                            |
| `derive-execution-sets` | Frozen plan, raw dry-run controls                                                       | `execution-sets.json` selector object.                                  |
| `build`                 | Plan artifact, one `variant-id` per matrix row                                          | Variant bundle, `build-result`, and optional immutable-proof artifacts. |
| `ensure-tag`            | Frozen plan and active GitHub Release publish nodes                                     | Tag verification or creation evidence.                                  |
| `publish`               | Plan artifact, one `publish-node-id` per matrix row, referenced build receipts          | `publish-result` artifacts.                                             |
| `report`                | Plan, diagnostics, build results, skip results, publish results, job conclusions        | Final operator summary.                                                 |

`derive-execution-sets` may be a separate job or an implementation detail of
`plan`, but the produced selectors must be serialized as machine-readable JSON
rather than reconstructed from ad hoc shell output in later jobs.

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
    "skip-satisfied-publish-node-ids": [],
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
4. `skip-satisfied-publish-node-ids` contains every selected publish node whose
   frozen `publish-disposition` is `skip-satisfied`; the control plane uses this
   set to emit synthetic skip receipts.
5. `active-github-release-publish-node-ids` is the subset of
   `active-publish-node-ids` whose target family is `github-release`; `ensure-tag`
   must not run when this array is empty.

Empty arrays are first-class workflow outcomes, not missing outputs. A build or
publish matrix with an empty corresponding selector is skipped by the control
plane, and the `report` job still runs from the serialized selectors, available
receipts, diagnostics, and job conclusions.

Current scope does not use a separate `approve` job. `official` live side effects
are gated by attaching the protected GitHub `release` environment directly to
the jobs that can perform those side effects: `ensure-tag` when it would create
or verify release tags for active GitHub Release publish nodes, and each live
`publish` matrix job. This keeps the environment claim on OIDC-backed external
trusted publishing jobs aligned with the registry-side trusted-publisher
configuration.

## Dry-Run and Validation Build Policy

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

## Planner CLI Boundary

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

The CLI must fail closed:

- invalid descriptors anywhere in current scope block all planning;
- remote observation errors after bounded retry block plan emission;
- no partial plan file is written on blocking planner failure;
- machine-readable diagnostics are written before returning a non-zero exit code
  whenever request normalization has begun.

## Planner Diagnostic Codes

The middle-layer contract freezes the diagnostic object shape but not the code
vocabulary. Current scope should start with this minimum code registry:

| Code                            | Phase            | Scope          | Meaning                                                                                             |
| ------------------------------- | ---------------- | -------------- | --------------------------------------------------------------------------------------------------- |
| `REQ_INVALID_INPUT`             | `validation`     | `request`      | Raw workflow input could not be normalized into the planner request contract.                       |
| `REQ_FORCE_FOR_OFFICIAL`        | `validation`     | `request`      | `request-flags.force` was true for `profile: official`.                                             |
| `REQ_PROJECT_NOT_FOUND`         | `validation`     | `project`      | An explicitly requested project ID was not an in-scope releasable project.                          |
| `DESC_SCHEMA_INVALID`           | `validation`     | `project`      | A project descriptor failed file-schema validation.                                                 |
| `DESC_STATIC_INVALID`           | `validation`     | `project`      | Descriptor passed syntax but failed static repo validation.                                         |
| `CATALOG_SCHEMA_INVALID`        | `validation`     | `request`      | The shared target-instance catalog failed schema validation.                                        |
| `CATALOG_REF_NOT_FOUND`         | `validation`     | `project`      | A descriptor target reference did not resolve to exactly one catalog target instance.               |
| `VERSION_AUTHORITY_FAILED`      | `normalization`  | `project`      | The planner could not resolve the project-scoped version identity.                                  |
| `PYPI_FILENAME_COMPUTE_FAILED`  | `normalization`  | `publish-node` | Planner-time PyPI filename computation failed or produced an unexpected member set.                 |
| `REMOTE_QUERY_FAILED`           | `query`          | `publish-node` | Destination query failed after bounded retry.                                                       |
| `REMOTE_NORMALIZATION_FAILED`   | `normalization`  | `publish-node` | Raw destination state could not be normalized for the target family.                                |
| `REMOTE_CLASSIFICATION_FAILED`  | `classification` | `publish-node` | Normalized destination state could not be reduced to one remote-observation class.                  |
| `IMMUTABLE_PROOF_UNAVAILABLE`   | `classification` | `publish-node` | Required prior build digest proof was absent, expired, ambiguous, or conflicting.                   |
| `IMMUTABLE_PARTIAL_UNSUPPORTED` | `classification` | `publish-node` | Same-identity immutable remote state was a proved partial subset, which current scope fails closed. |
| `REMOTE_CONFLICTING`            | `classification` | `publish-node` | Same-identity remote state conflicts with the frozen publish intent.                                |
| `OFFICIAL_FROZEN_VERSION`       | `classification` | `project`      | A `buddy FORCE` request targeted a project/version already frozen by official GitHub Release.       |
| `REQ_ACTOR_UNAUTHORIZED`        | `validation`     | `request`      | The triggering actor did not have the required repository permission for the selected profile.      |
| `PLAN_INTERNAL_INVARIANT`       | `validation`     | `request`      | Planner detected an impossible internal state after validation should have prevented it.            |

New planner diagnostic codes may be added by implementation, but every new code
must be registered in this page or in a successor registry before tests depend
on it. Free-form adapter messages belong in `details`, not in the `code` field.

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

## File Formats

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

Where the boundary documents say a request or result object contains "at least"
some fields, this low-level handoff freezes those listed top-level fields as the
complete `v1alpha1` contract unless an extensibility field is named above or in
the object's defining section. In particular, `build-request`, `build-result`,
`publish-request`, `publish-result`, and `skip-result` must not grow extra
root-level fields during implementation. New root-level machine fields require a
successor contract update before tests or workflows depend on them.

`release-report.json` is the control-plane-authored final report data consumed by
`render-summary`. Its minimum shape is:

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
        "build-result-artifact-names": [],
        "publish-result-artifact-names": [],
        "skip-result-artifact-names": []
    },
    "jobs": {
        "authorize-entry": { "conclusion": "success" },
        "plan": { "conclusion": "success" },
        "build": {
            "conclusion": "success",
            "failed-variant-ids": []
        },
        "ensure-tag": { "conclusion": "skipped" },
        "publish": {
            "conclusion": "success",
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

For planner failure before plan emission, `plan.plan-id`,
`plan.selected-project-ids`, and `artifacts.plan-artifact-name` are `null`, while
`artifacts.planner-diagnostics-artifact-name` identifies the diagnostics
artifact when one was produced. `run.conclusion` uses GitHub job conclusion
spelling such as `success`, `failure`, or `cancelled`. Job-level conclusions
under `jobs` use the same spelling and may also use `skipped` for jobs that did
not run because their serialized selector set was empty or their prerequisite
path was suppressed.

Successful `build-result`, `publish-result`, and `skip-result` files are
positive evidence only. Current scope does not define failed build, failed
publish, or failed skip receipt files. The `report` job must run after success,
failure, cancellation, and skipped matrix paths, then summarize failure from the
serialized execution sets, job conclusions, and any missing expected positive
receipts. A completed positive receipt remains valid evidence of a side effect
that happened before a later job failed or the workflow was cancelled.

## Artifact Naming and Retention

GitHub Actions artifact names are the lookup key available to later runs through
the Actions artifact API, including exact `name` filtering. The control plane
should therefore use deterministic artifact names with a short hash suffix
instead of embedding raw plan IDs that may contain slashes or long strings.

Define:

```text
safe-id(input) = first 24 lowercase hex chars of SHA-256 over the UTF-8 input
```

Current-scope artifact names:

| Artifact            | Name pattern                                                            |
| ------------------- | ----------------------------------------------------------------------- |
| Frozen plan         | `release-plan-v1-<safe-id(plan-id)>`                                    |
| Planner diagnostics | `release-planner-diagnostics-v1-<run-id>-<attempt>`                     |
| Execution sets      | `release-execution-sets-v1-<safe-id(plan-id)>`                          |
| Variant bundle      | `release-build-bundle-v1-<safe-id(plan-id + "\n" + variant-id)>`        |
| Build result        | `release-build-result-v1-<safe-id(plan-id + "\n" + variant-id)>`        |
| Publish result      | `release-publish-result-v1-<safe-id(plan-id + "\n" + publish-node-id)>` |
| Skip result         | `release-skip-result-v1-<safe-id(plan-id + "\n" + publish-node-id)>`    |
| Immutable proof     | `release-immutable-proof-v1-<safe-id(binding-json)>`                    |
| Final report        | `release-report-v1-<run-id>-<attempt>`                                  |

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

The workflow should not extend artifact retention just to satisfy immutable proof
reuse. If an artifact is expired or missing, the proof is unavailable and the
planner fails closed when proof is required.

## Immutable Proof Wrapper

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

## Build Executor Realization

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

## Publish Executor Realization

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

## Registry Adapter Details

### GitHub Release

Planner adapter responsibilities:

- Query releases by the frozen `release-tag`.
- Normalize release state as `prerelease` or `release`.
- Normalize the asset set by asset name and label.
- Classify exact matches, same-tag prerelease partials, and same-tag conflicts
  according to the replay matrix.

Publish executor responsibilities:

- `create-only`: create the release for the already verified tag and upload the
  exact planned asset set.
- `overwrite-mutable`: converge the mutable prerelease to the frozen `buddy`
  intent when the planner authorized `FORCE`.
- `replace-authoritative`: converge the same-tag prerelease to the frozen
  official intent, including final release state, asset set, and asset labels.

The executor may delete and recreate assets only when the plan mode authorizes an
overwrite or authoritative replacement. It must not use release asset presence as
a fresh skip decision.

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
package as a canonical artifact. For NuGet.org publication, first delivery
should keep the `.nupkg` and `.snupkg` as separate planned artifacts and publish
both when the descriptor references both members. This follows NuGet's modern
symbol-package model rather than the legacy `.symbols.nupkg` format or embedding
portable PDBs into the primary package. If symbol-package observation cannot be
implemented and tested in first delivery, then first delivery must defer
NuGet.org publication for affected .NET package descriptors rather than silently
publishing untracked `.snupkg` side effects.

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

Publish executor responsibilities:

- Use PyPI Trusted Publishing with GitHub Actions OIDC.
- Upload only the frozen wheel and optional sdist members under the exact
  planner-frozen filenames.
- Fail before upload if wheel `METADATA` or sdist `PKG-INFO` does not match the
  frozen normalized package name and version.

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
- Publish the receipted tarball under the frozen package name. If the package
  name must differ from manifest metadata, stage a temporary package directory or
  manifest rewrite that affects only the packed artifact for that publish node.
- Verify the packed package's `package/package.json` name and version before
  upload.

### RubyGems

Planner adapter responsibilities:

- Resolve package identity from evaluated `Gem::Specification.name`.
- Compare versions with RubyGems `Gem::Version`.
- For RubyGems.org, use the RubyGems.org API for version and digest observation.
- For GitHub Packages RubyGems, use GitHub-hosted read access or `gem fetch`
  with `GITHUB_TOKEN` where needed.

Publish executor responsibilities:

- For RubyGems.org, use RubyGems Trusted Publishing with GitHub Actions OIDC.
- For GitHub Packages RubyGems, configure RubyGems credentials with
  `GITHUB_TOKEN` and publish to the owner-scoped GitHub Packages host.
- Verify the built gem specification name and version before upload.

## GitHub Permissions and Environments

Use job-level least privilege rather than a broad workflow-level write token.

| Job group                                   | Minimum permission intent                                                     |
| ------------------------------------------- | ----------------------------------------------------------------------------- |
| Planning without GitHub-hosted remote reads | `contents: read`, `actions: read` when proof lookup is needed.                |
| GitHub-hosted remote reads                  | Add only the required read scopes, such as `packages: read` where applicable. |
| Tag creation                                | `contents: write`, scoped to the `ensure-tag` job.                            |
| GitHub Release publication                  | `contents: write`, scoped to the GitHub Release publish job.                  |
| GitHub Packages publication                 | `packages: write`, scoped to the matching publish job.                        |
| Trusted publishing to external registries   | `id-token: write`, scoped to the matching publish job.                        |

`official` live side-effect jobs must use the GitHub environment named
`release`, with required reviewers and prevent-self-review enabled. The
`release` environment is referenced only by jobs that can perform live external
side effects after planning and validation-build work have completed:

- `ensure-tag`, when it would create or verify tags for active GitHub Release
  publish nodes;
- each live `publish` matrix job.

There is no separate approval-only job in current scope. External trusted
publisher policies for NuGet.org, PyPI, npmjs, and RubyGems.org should be
configured for the stable publish workflow file and the same `release`
environment, so the job that obtains the OIDC token is also the job constrained
by the registry-side environment policy. Planner-time remote observation remains
unable to access approval-gated secrets or OIDC publish jobs.

Before live official publication is enabled, release infrastructure setup must
include this checklist:

| Surface                         | Required configuration                                                                                                                                                                                    |
| ------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| GitHub environment              | Environment named `release`, required reviewers configured, prevent self-review enabled, and native admin bypass left to repository policy.                                                               |
| NuGet.org trusted publishing    | Package owner-side trusted publisher entry for repository `hcoona/three`, workflow `.github/workflows/release-publish-node.yml`, and environment `release`.                                               |
| PyPI trusted publishing         | Project owner-side trusted publisher entry for repository `hcoona/three`, workflow `.github/workflows/release-publish-node.yml`, and environment `release`.                                               |
| npmjs trusted publishing        | Package owner-side trusted publisher entry for repository `hcoona/three`, workflow `.github/workflows/release-publish-node.yml`, and environment `release` where the package supports trusted publishing. |
| RubyGems.org trusted publishing | Gem owner-side trusted publisher entry for repository `hcoona/three`, workflow `.github/workflows/release-publish-node.yml`, and environment `release`.                                                   |
| GitHub Packages                 | No external OIDC trusted-publisher policy; publish jobs use `GITHUB_TOKEN` with the required package write permission.                                                                                    |

Missing trusted-publisher configuration is a live publish failure surfaced by the
matching publish executor or credential acquisition step. The planner must not
probe those approval-gated trusted-publishing credentials during remote
observation.

No-side-effect runs skip this environment gate entirely:

- dry-run or validation-only;
- zero-target;
- all selected publish nodes are `skip-satisfied`.

## Tag Orchestration

`ensure-tag` is a control-plane job, not an executor.

Implementation sequence:

1. Read all active GitHub Release publish nodes from the frozen plan.
2. Compute the distinct required `release-tag` set.
3. Query every existing tag in the set before creating any missing tag.
4. If any existing tag does not peel to the selected `commit-sha`, fail without
   creating tags.
5. After the full precheck passes, create every missing tag at the selected
   commit.

Newly created release tags are lightweight tags that point directly at the
selected commit. Existing annotated tags are accepted only when peeling the tag
object resolves to the selected commit. The job must never retarget an existing
tag and must never treat a tag object that points elsewhere as satisfying the
selected commit requirement.

The job must not run when dry-run is true or when the active publish set contains
no GitHub Release publication.

## Acceptance Traceability

Implementation should maintain a trace table in tests or CI reports with this
minimum shape:

| Scenario                               | Fixture anchor                                                                                | Required evidence                                                                                                                                                                    |
| -------------------------------------- | --------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| C# library package release             | `src/public/lib/Hjg.Pngcs/`                                                                   | Descriptor, plan snapshot, Windows build receipt, NuGet or GitHub Release publish or skip receipt.                                                                                   |
| C# app `dotnet publish` binary         | `src/private/app/qidian-novel-downloader/` or `src/private/app/vscode-copilot-telegram-hook/` | Descriptor, plan snapshot, Windows build receipt for binary artifact, GitHub Release evidence.                                                                                       |
| C# app Inno installer                  | `src/public/app/ImageOcclusionEditor/`                                                        | Descriptor, plan snapshot, Windows build receipt proving installer produced from binary artifact, GitHub Release evidence.                                                           |
| Python package including `nbgv-python` | `src/public/lib/nbgv-python/`                                                                 | Descriptor with special version authority, plan snapshot with frozen version, build metadata conformance, PyPI or GitHub Release evidence.                                           |
| Node package                           | `src/public/lib/hexo-renderer-asciidoc/`                                                      | Descriptor, plan snapshot, npm pack receipt, npmjs or GitHub Packages evidence.                                                                                                      |
| Ruby gem                               | `src/public/lib/asciidoctor-latexmath/`                                                       | Descriptor, plan snapshot, gem build receipt, RubyGems.org or GitHub Packages evidence.                                                                                              |
| Multi-project dispatch                 | Any two fixture anchors above                                                                 | One run report showing normalized selected projects and multiple project-scoped publish nodes.                                                                                       |
| Dry-run                                | Any fixture anchor above                                                                      | Run report proving no tags, approval, or publish jobs ran.                                                                                                                           |
| Validation build                       | `src/public/lib/nbgv-python/` plus at least one package fixture                               | Dry-run report plus validation-only build receipts excluded from immutable proof lookup.                                                                                             |
| Rerun skip                             | Any GitHub Release fixture or immutable package fixture with prior admissible proof           | Planner diagnostics or plan snapshot proving `skip-satisfied` and synthetic skip receipt.                                                                                            |
| Immutable partial replay               | NuGet or PyPI multi-member fixture, real or mocked at adapter boundary                        | Planner diagnostic proving fail-closed behavior for a same-identity partial case.                                                                                                    |
| Cancellation                           | Workflow-level integration fixture                                                            | GitHub cancelled conclusion plus report showing already completed external side effects only.                                                                                        |
| Approval boundary                      | Workflow-level integration fixture                                                            | `buddy` explicit `write+` authorization with no approval, `official` `maintain+` authorization, required-review run, self-review prevention, and admin bypass behavior when enabled. |

The trace table may live in test fixtures or generated CI output. It does not
need to become a new operator-facing release record.

## External Documentation Grounding

This low-level design was checked against these official or primary sources:

- GitHub Actions environments, required reviewers, prevent self-review, artifact
  APIs, OIDC claims, workflow syntax, `GITHUB_TOKEN`, GitHub Release REST API, and
  GitHub Packages registry guides.
- Microsoft Learn NuGet trusted publishing, service index, package base address,
  registration, package publish, and `.snupkg` symbol package pages.
- PyPI trusted publishing, JSON API, Python package name normalization, and
  version normalization specifications.
- npm trusted publishers, provenance, package-name guidelines, and npm CLI
  `view` and `pack` documentation.
- RubyGems trusted publishing, RubyGems.org API, publishing guide, and GitHub
  Packages RubyGems guide.

## What Remains Implementation-Owned

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

## Related Pages

- [Workflow Release Requirements Baseline](./workflow-release-requirements-baseline.md)
- [Workflow Release Architecture Model](./workflow-release-architecture-model.md)
- [Workflow Release Descriptor Schema](./workflow-release-descriptor-schema.md)
- [Workflow Release Plan Shape](./workflow-release-plan-shape.md)
- [Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md)
- [Workflow Release Design Layering and Implementation Handoff Scope](./workflow-release-design-layering-and-handoff-scope.md)
- [Workflow Release Deferred PyPI Multi-Wheel Support](./workflow-release-deferred-pypi-multi-wheel-support.md)
