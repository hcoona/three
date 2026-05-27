# Workflow Release CI Affected Validation Low-Level Design

## 1. Document Governance and Handoff Boundary

Status: this page is the low-level design handoff baseline for implementing CI
affected validation as a workflow-release entry point, rebaselined to a bounded
execution-batch model. It consumes the locked
[requirements](./workflow-release-ci-affected-validation-requirements.md),
approved [high-level design](./workflow-release-ci-affected-validation-high-level-design.md),
and approved
[middle-level design](./workflow-release-ci-affected-validation-middle-level-design.md)
as fixed input.

The target reader is one experienced senior engineer. This page freezes the
realization seams that affect correctness, testability, reviewability, workflow
behavior, plan/evidence contracts, and acceptance evidence. It intentionally does
not freeze every helper, internal module, private function, composite action,
shell wrapper, local scratch directory, or command-line implementation detail.
Those details remain implementation-owned only while they preserve the contracts
below.

Low-level design changes may still be incompatible before implementation starts,
but they must remain below requirements, HLD, and MLD decisions. If a later review
finds a contradiction that cannot be solved inside this low-level layer, the
upstream decision must be escalated rather than silently rewritten here.

## 2. Low-Level Design Summary

| Area               | Low-level decision                                                                                                                                                                                                                                                                                                            |
| ------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Workflow shape     | Add one top-level CI validation entry workflow with `pull_request`, `push`, and `schedule` triggers, plus reusable internal validation units only if implementation benefits from them.                                                                                                                                       |
| Plan format        | Emit one UTF-8 JSON validation plan with stable `api-version`, `kind`, `plan-id`, `mode`, provenance, classification, subject universe, planned obligations, logical work groups, stable selectors, evidence expectations, diagnostics, and verdict intent.                                                                   |
| Fail-closed        | Emit an inspectable fail-closed plan artifact and diagnostics, but the run conclusion must fail and no execution batches execute.                                                                                                                                                                                             |
| Subject universe   | Include discovered validation subjects with selected/excluded status, not only selected subjects.                                                                                                                                                                                                                             |
| Classification     | Use a conservative ordered rule table: unknown/unclassifiable always fail closed; broad expansion only applies to recognized global, ecosystem, or infrastructure categories.                                                                                                                                                 |
| Downstream closure | Use ecosystem-provided dependency facts when sufficient for downstream closure; otherwise fail closed.                                                                                                                                                                                                                        |
| Execution handoff  | Materialize bounded execution batches after planning from logical work groups and stable selectors; post-planning execution must preserve selector semantics and per-selector outcomes/evidence rows, and must not reclassify changes, rediscover subjects, silently drop obligations, downgrade obligations, or alter scope. |
| Evidence           | Emit one validation-only batch evidence bundle per execution batch plus two final aggregation artifacts: an aggregate evidence manifest and an aggregate summary; descriptor, subject, and artifact obligations are evidence rows, and all evidence is inadmissible as release immutable proof.                               |
| Credentials        | No publication credentials, release approvals, OIDC publish permissions, registry mutation, GitHub Release mutation, or release-tag mutation in CI validation.                                                                                                                                                                |
| Runners/tools      | Preserve .NET on Windows, Python and JavaScript/TypeScript on Ubuntu when applicable, and prefer `mise` for tool provisioning.                                                                                                                                                                                                |
| HK                 | Provide planner-aligned lightweight preflight only; HK output is local feedback, not CI evidence.                                                                                                                                                                                                                             |
| Acceptance         | Trace acceptance to plan artifacts, selected scopes, batch evidence bundles, failure verdicts, and no-publication boundaries.                                                                                                                                                                                                 |

## 3. Frozen Upstream Contracts and Non-Reopened Seams

This page does not reopen these upstream decisions:

- CI affected validation belongs to workflow-release and must not create a
  separate project list, artifact model, or build semantics.
- CI emits a sibling validation plan, not a release plan with publication
  disabled.
- The planner owns classification, subject selection, downstream expansion,
  descriptor-validation scope, validation obligations, and fail-closed outcomes.
- Execution consumes a fully materialized validation plan and must not recompute
  planning policy.
- Unknown or unclassifiable changes fail planning closed.
- Known global changes and scheduled full validation share the same full
  validation scope while preserving different provenance.
- Lightweight-only plans are allowed only when every changed path is known
  non-impacting.
- Descriptor-backed projects validate release-shaped artifacts and logical release-shaped receipt expectations for
  the union of artifacts required by all declared profiles, without publication
  side effects.
- Validation-only subjects participate in validation but never become publish
  subjects.
- CI evidence and release immutable proof are strictly separated.
- Policy-bearing CI planning changes may be planned by the validation-tree policy
  being reviewed, but the run still receives no release credentials or publication
  authority.

Inside that upstream envelope, this LLD freezes the implementation-level
performance and topology baseline without promoting it to a requirements, HLD, or
MLD contract:

- full, broad, and global validation target at most 12 minutes as an
  observable planning and CI-duration estimate, with historical duration telemetry
  used to revisit optimization if the target is missed;
- physical top-level GitHub Actions jobs are capped at 18 total, including at
  most 8 Windows jobs;
- validation artifacts target at most 20;
- final aggregation target 1 to 2 minutes;
- work groups and selectors are logical validation obligations, not concrete
  GitHub Actions jobs or matrix rows;
- the physical execution unit is the runner-family orchestrator job; each
  orchestrator slot runs at most one logical execution batch, and each logical
  execution batch produces one validation-only batch evidence bundle;
- execution-batch count is capped by the current-run artifact budget: maximum
  execution batches must be no more than 20 minus the
  expected non-bundle validation artifact count; with 7 expected non-bundle
  validation artifacts, the effective maximum is 13 execution batches;
- batch DAG and intra-batch ordering preserve dependencies;
- release-shaped validation may later coalesce compatible work by runner family,
  ecosystem, build recipe, setup, no-publish behavior, and artifact family, while
  descriptor, subject, and artifact obligations remain batch evidence rows rather
  than default job boundaries.

## 4. Workflow and Job Boundary

### 4.1 Workflow Identity

The CI validation entry point should be one checked-in workflow file:

| File                                | Trigger shape                      | Stable responsibility                                                                                                                                              |
| ----------------------------------- | ---------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `.github/workflows/ci-validate.yml` | `pull_request`, `push`, `schedule` | Normalize CI event input, run planning, materialize execution batches, run the batch DAG, aggregate validation-only evidence, and publish inspectable diagnostics. |

The workflow filename is a repository contract because branch protection and
operator documentation may refer to CI check names. Unlike release publication
workflows, it is not a trusted-publisher identity and must not be configured in
external registry policies.

The workflow `name` and the final required GitHub check context are also
repository contracts:

- workflow `name`: `CI Validation`;
- required final check context: `CI Validation / aggregate-evidence`.

Branch protection for this CI gate must bind to the final aggregate check
context, not to execution-batch job names or auxiliary checks. If the concrete
GitHub Actions job topology changes, the implementation must preserve that final
check context or update branch protection and this design together.

Implementation may introduce reusable internal workflow files, composite
actions, or bounded runner-family orchestrator jobs for execution batches. Those
internal files and orchestrator step boundaries are implementation-owned unless
later branch protection or external policy starts depending on them. The current
topology uses runner-family orchestrators rather than fixed dependency layers:
each orchestrator repeatedly runs the next batch whose declared dependencies are
ready and uploads each batch evidence bundle at its contract-owned artifact name
before that bundle can satisfy later dependencies. The bound is the existing
execution-batch/artifact budget, not an arbitrary dependency-depth limit.

### 4.2 Logical Job Sequence

The top-level CI validation workflow preserves this logical sequence:

1. **`normalize-input`**
    - resolves CI mode: `pull_request`, `push`, or `scheduled_full`;
    - derives the confirmed affected range for affected modes;
    - records event identity for diagnostics;
    - fails into a planner-facing fail-closed request when external event, API, or
      checkout inputs cannot provide a complete affected range.
2. **`plan`**
    - checks out the validation tree;
    - runs validation planning without publication credentials;
    - emits exactly one validation plan artifact at the contract-owned ref when
      the request boundary is replayable and a plan is expected;
    - emits plan diagnostics when planning fails closed.
3. **`materialize-execution-batches`**
    - reads the validation plan;
    - materializes bounded execution batches from planned validation obligations
      and evidence expectations;
    - emits the execution-batch manifest that assigns each executable obligation
      to exactly one batch without silently downgrading or dropping selected
      obligations;
    - coalesces obligations into fewer compatible batches as needed to satisfy the
      job and artifact budgets, and fails post-plan materialization if selected
      obligations cannot fit those budgets without dropping required evidence or
      downgrading planned obligations;
    - produces an empty executable batch set for fail-closed plans and
      lightweight-only
      plans with no executable lightweight obligations.
4. **Execution-batch DAG**
    - runs executable batches through independent Windows and Ubuntu
      runner-family orchestrator jobs;
    - fails closed if materialization or validation would require a
      batch-to-batch dependency that crosses runner families;
    - starts a same-family dependent batch when its declared dependency bundle
      artifact is available in the local orchestrator state, without waiting for
      unrelated peers from the same coarse layer;
    - preserves required intra-batch ordering when multiple obligations share a
      compatible runner family, ecosystem, build recipe, setup, no-publish
      behavior, and artifact family;
    - emits one validation-only batch evidence bundle per execution batch;
    - treats batch writer integrity as validation-grade batch integrity, not
      release-proof-grade per-artifact or per-selector writer observation;
    - never changes planned scope or obligations.
5. **`aggregate-evidence`**
    - runs after planning and execution-batch materialization are attempted, even
      when a prior logical job fails to produce a readable plan or batch set;
    - verifies execution-batch assignments before admitting any batch evidence
      bundle;
    - verifies expected batch evidence bundles and concentrates strict artifact
      namespace checks in final aggregation;
    - treats missing, unreadable, invalid, or unmaterializable plans as
      `invalid-plan` with no executable batches;
    - continues collecting independent evidence after validation failures while
      still blocking on control-plane and bundle write failures;
    - computes the CI validation verdict;
    - emits the aggregate evidence manifest and aggregate summary at their contract-owned refs;
    - fails the workflow when the aggregated validation outcome fails.

These job names are logical handoff names. The implementer may map them to
concrete job identifiers, reusable workflows, or grouped jobs, provided the
sequence, authority boundary, evidence semantics, invariant that each execution
batch maps to one budget-counted batch evidence bundle, and final required check
context remain intact.

Logical handoff names remain producer-authority boundaries for control-plane and
final artifacts. The workflow contract must define a boundary identity map before
execution that maps each non-bundle logical boundary (`normalize-input`, `plan`,
`materialize-execution-batches`, and `aggregate-evidence`) to the allowed GitHub
Actions job identifiers that may produce artifacts for that boundary. This map is
control-plane contract data, not a payload claim; artifact consumers verify
control-plane producer authority by comparing platform workflow/job metadata to
the mapped identity for the expected logical boundary.

G5 live CI does not use producer-side batch observation sidecars as trusted
producer identity for execution-batch bundle artifacts. Execution-batch bundle
admission is validation-grade and internal to the same workflow run attempt:
aggregation uses the execution-batch manifest's expected bundle refs and names,
the final aggregate job's current-run GitHub Actions artifact enumeration/API
singleton checks, and trusted downloader-observation metadata generated by
`download-ci-validation-observed-artifacts` while enumerating and downloading
GitHub Actions artifacts. That downloader-observed artifact ID/name/run/attempt
metadata is distinct from arbitrary producer sidecars, bundle payload fields, or
caller-provided observation manifests. Bundle payload validation,
plan/request/snapshot/run/run-attempt/manifest/dependency binding, and
fail-closed completeness then admit or reject the bundle. The `writer` payload
and any sidecar-like producer claim are insufficient by themselves and are not
release immutable proof. The control script does not expose a caller-writable
batch observation writer or consume caller-provided producer observation
manifests. `aggregate-ci-evidence` batch mode is not a standalone trust
boundary: the live workflow pairs it with a freshly
`download-ci-validation-observed-artifacts`-created observed-artifacts
directory, and both aggregate phases must consume that downloader-created
directory. Runner-family orchestrator dependency admission is same-family only:
same-run same-family dependencies use the artifact ID recorded immediately after
the upload step, then recheck artifact ID/API metadata before consumption.
Cross-family batch dependencies fail closed in the current validation topology;
there is no peer-family handoff or wait path. In-flight gating requires the
dependency bundle plus downloader-observed `artifact-metadata.json` binding the
artifact ID, physical name, run, attempt, and execution-batch boundary. The final
aggregate job performs the stronger live namespace and singleton checks.

The downloader also writes trusted namespace-enumeration observation metadata
and an internal batch admission manifest under the observed-artifacts directory.
Aggregate binding requires that downloader-produced admission data before any
candidate can be marked verified; this validation-grade control data is not part
of public release proof semantics, and aggregation rejects batch mode without an
explicit observed-artifacts directory. If live artifact enumeration is unavailable,
aggregate evidence records a fail-closed namespace diagnostic from that metadata
and does not treat the validation artifact namespace as fully observed. Bounded
live enumeration overflow remains separate: the downloader
still records only the configured cap plus one sentinel artifact, and aggregate
continues to report namespace overflow from the observed lower bound.
Live namespace closure only allows changed-files and fact snapshot artifact
names when the frozen plan proves those snapshot inputs are required. A future
G4 trusted-observation seam must come from a genuinely
trusted non-payload observer, not from producer-side sidecar artifacts.

### 4.3 Permissions

The CI validation workflow uses least privilege:

- `contents: read` for checkout and repository reads;
- pull request metadata reads only where event normalization needs them;
- read-only GitHub Actions control-plane access for the same workflow run attempt
  to enumerate contract artifacts, observe artifact instance metadata and stable
  instance IDs, enumerate the authoritative artifact namespace, and compare
  artifact-producing run/job/matrix identity with the boundary identity map;
- no `id-token: write`;
- no release environment;
- no package-registry secrets;
- no secrets needed only for side-effecting release.

If a future low-level implementation requires additional read-only permission for
event normalization or diagnostics, the reason must be documented at the workflow
boundary. Publication-capable permission is out of scope.

## 5. Control-Plane Request Files

Cross-job request data is serialized as UTF-8 JSON with LF line endings. Every
machine-readable file has:

- `api-version`;
- `kind`;
- `created-at`;
- `repository`;
- `run`;
- `schema-diagnostics` for producer-side warnings that are not validation
  failures.

The inherited common envelope fields have these minimum shapes:

```yaml
created-at: string
repository:
    owner: string
    name: string
run:
    workflow: string
    run-id: string
    run-attempt: string
schema-diagnostics: [diagnostic-record]
```

`created-at` is an RFC 3339 timestamp for producer inspection only and is not a
security or replay authority. `run.workflow`, `run.run-id`, and
`run.run-attempt` are copied from the GitHub Actions run context and are the only
common-envelope run identity fields used by cross-artifact binding.

Producer authority is not a payload self-claim. Any artifact that gates planning,
execution-batch materialization, batch evidence admission, or final acceptance
must be tied to the expected logical boundary by non-payload control-plane
evidence for the same `run-id` and `run-attempt`. The allowed
producer-authority signals are:

| Artifact class                                             | Required producer boundary        | Allowed non-payload authority signals                                                                                                                                                                                                                                                                     |
| ---------------------------------------------------------- | --------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Planner-facing CI request                                  | `normalize-input`                 | Contract-owned request ref and instance count plus immutable workflow/job context proving the artifact was uploaded by the input-normalization control-plane boundary                                                                                                                                     |
| Validation plan, changed-files snapshot, and fact snapshot | `plan`                            | Contract-owned artifact ref and instance count plus immutable workflow/job context proving the artifact was uploaded by the planning control-plane boundary for this run attempt                                                                                                                          |
| Execution-batch manifest                                   | `materialize-execution-batches`   | Contract-owned artifact ref and instance count plus immutable workflow/job context proving the artifact was uploaded by the execution-batch-materialization control-plane boundary                                                                                                                        |
| CI validation batch evidence bundle                        | Assigned execution-batch boundary | Validation-grade live admission through the execution-batch manifest's expected bundle ref/name, current-run artifact API singleton metadata, downloader-observed artifact ID/name/run/attempt metadata, payload validation, and run/run-attempt/batch binding; not immutable workflow/job producer proof |
| Aggregate evidence manifest and aggregate summary          | `aggregate-evidence`              | Contract-owned final refs and instance counts plus immutable workflow/job context proving final artifacts were uploaded by the aggregation control-plane boundary                                                                                                                                         |

For non-bundle control and final artifacts, the immutable workflow/job context
must match the boundary identity map from section 4.2 for the required producer
boundary. If the workflow platform or control-plane wrapper cannot expose those
signals for a non-bundle control/final artifact class, or the observed platform
identity is not allowed by the boundary identity map, that artifact is
producer-unverified. Producer-unverified planning artifacts make the plan
invalid, and producer-unverified final manifest or aggregate artifacts are not
authoritative acceptance evidence. Live batch evidence bundles instead fail
closed when their validation-grade manifest/name/current-run API singleton,
downloader-observation metadata, payload, or run/run-attempt/batch binding is
missing or mismatched. Payload fields, artifact names supplied by executable
validation commands, logs, and job conclusions never prove producer authority.
Validation-grade batch writer integrity is metadata embedded in the batch
evidence bundle and verified by final aggregation; it is not a separate artifact,
ref, or namespace entry.

For non-bundle control and final artifacts, the GitHub Actions implementation
enforces two independent control-plane checks before the artifact is consumed:
the run artifact namespace is enumerated and must contain exactly one live
instance at the contract-owned physical name, and the enumerated artifact
instance ID must match the upload output observed from the workflow job mapped to
that logical boundary. Consumers download producer-verified non-bundle inputs by
artifact ID where the workflow can fail immediately. The final aggregation job
performs the same namespace and producer-boundary verification for its
non-bundle inputs before accepting a plan, and verifies the aggregate evidence
manifest and aggregate summary uploads after publication. Batch evidence bundles
remain on the validation-grade admission path described above; they are admitted
through aggregate manifest/API singleton checks, downloader metadata, payload
validation, and run/run-attempt/batch binding, not immutable workflow/job
producer proof. Any missing, duplicated, stale, producer-mismatched non-bundle
artifact, or validation-grade bundle mismatch remains fail-closed.

`artifact-ref` values in this document are logical contract refs, not physical
GitHub artifact names. Every logical ref maps to one attempt-visible physical
artifact name with this digest mapping:

```text
physical-artifact-name = "three-ci-validation-" + run-id + "-" + run-attempt + "-" + lowercase_sha256(utf8(logical-artifact-ref))
```

The physical name length varies with the run identifiers, includes the producing
run attempt, and is stable for a given logical ref within that attempt. For
artifact classes that carry an `artifact-ref`, aggregation recomputes the
expected physical name from that payload field plus the authoritative run
identity and requires it to equal the observed physical name before trusting the
payload. Artifact instance counting, duplicate detection, namespace enumeration,
producer checks, and replay binding operate on physical artifact instances whose
names have the `three-ci-validation-` prefix and whose payload or contract ref
recomputes to the expected physical name. A prefixed physical artifact whose
payload is unreadable, has no expected `artifact-ref`, has a non-canonical
logical ref, or recomputes to a different physical name is non-authoritative for
that contract boundary even if its payload fields resemble valid evidence; in
closed namespaces, such an instance is an unexpected contract artifact.

Because the physical artifact namespace is flat, aggregation must classify all
prefixed physical artifacts against the complete set of pre-final input
non-bundle contract refs before closing the pre-final bundle namespace. That set
always includes the request, validation plan, changed-files snapshot, fact
snapshot, and execution-batch manifest refs, each with expected cardinality `0` or
`1` for the run attempt. The request ref is always expected with cardinality `1`;
missing, invalid, or unreplayable request input prevents an authoritative plan or
produces the existing request-invalid fail-closed path rather than becoming a
not-required artifact. Changed-files and fact snapshot refs use cardinality `1`
only when required by their plan fields and cardinality `0` otherwise.
Contract-owned aggregate evidence manifest and aggregate summary refs are always
classified as final-artifact refs whenever present, including during same-attempt
retry or partial-finalization reconciliation; they are excluded from pre-final
bundle classification and are handled only by post-publication final artifact
verification. Any prefixed physical artifact that matches a pre-final input
non-bundle physical name is handled only by that contract's cardinality and
validation rules, including the cardinality-`0` unexpected case; it is never
reclassified as bundle-like evidence. Any remaining prefixed physical artifact
that is not classifiable as a pre-final input non-bundle artifact or a final
aggregate artifact ref is batch-bundle-like for aggregate evidence namespace
enumeration, even when its payload is unreadable or does not reveal a logical
bundle ref. After final publication or on retry with occupied final refs,
aggregation performs a second producer-authority and count verification for the
aggregate evidence manifest and aggregate summary refs.

`schema-diagnostics` uses the same shape as `diagnostic-record`, sorted by
`diagnostic-id`. These diagnostics are producer-side schema or compatibility
warnings only: every entry must have `severity: warning` or `info` and
`verdict-effect: none`, and aggregation must not treat them as validation
failures. Schema diagnostics that indicate an artifact is unreadable, malformed,
schema-invalid, or structurally invalid must instead be represented by the
artifact-specific planner, batch-evidence, or aggregation diagnostic paths defined
below.

Schema blocks below use `common-envelope: inherited` to avoid repeating those
fields. The block then lists only fields specific to that artifact kind.

The planner-facing CI request common fields are:

```yaml
common-envelope: inherited
api-version: three.ci.validation.request/v1alpha1
kind: ci-validation-request
artifact-ref: string
request-digest: string
mode: pull_request | push | scheduled_full
validation-tree:
    commit-sha: string
    ref: string | null
event:
    name: string
    number: string | null
    actor: string
    run-id: string
    run-attempt: string
```

`event.run-id` and `event.run-attempt` duplicate the common-envelope run identity
for digest-bound request projection. They must exactly equal
`common-envelope.run.run-id` and `common-envelope.run.run-attempt`; a mismatch is
a schema-valid but invalid request that fails closed with
`diagnostic-detail: request-wrong-run-attempt` or emits no authoritative plan.

For `pull_request` and `push`, the request also has:

```yaml
affected-range:
    status: available | unavailable
    base-sha: string | null
    base-tip-sha: string | null
    head-sha: string | null
    changed-files: [string] | null
    source: pull_request | push
    diagnostic: range-unconfirmed | null
    diagnostic-detail: missing | incomplete | inconsistent | unconfirmed-provenance | null
```

For `scheduled_full`, the request instead has:

```yaml
scheduled-full:
    enabled: true
```

Rules:

- The planner-facing request artifact is a contract-owned control-plane handoff at
  `ci-validation/requests/<run-id>/<run-attempt>/ci-validation-request.json`.
  Exactly one artifact instance must exist at that ref before planning consumes
  the request. `request-digest` is the lowercase hexadecimal SHA-256 digest of
  the RFC 8785 canonical JSON bytes of the request projection containing
  `api-version`, `kind`, `mode`, `validation-tree`, `event`, and the applicable
  `affected-range` or `scheduled-full` block; common-envelope fields,
  `artifact-ref`, `request-digest`, and `schema-diagnostics` are excluded from
  the hash preimage. The `plan` boundary must verify request artifact ref,
  instance count, common-envelope `run-id` and `run-attempt`, schema,
  recomputed `request-digest`, and producer authority from the `normalize-input`
  boundary before trusting affected-range or scheduled-full fields. A missing,
  duplicate, unreadable, malformed, schema-invalid, digest-mismatched,
  wrong-run-attempt, or producer-unverified request makes planning fail closed or
  emit no authoritative plan rather than trusting a payload self-claim. Planning
  may emit an authoritative fail-closed plan for a `request-invalid` case only when
  the request artifact is replayable: the contract-owned request ref, physical
  artifact instance count, common-envelope run identity, `normalize-input`
  producer authority, schema-valid payload, matching payload `artifact-ref`, and
  recomputed canonical `request-digest` can all be established. Digest-mismatch and
  wrong-run-attempt requests are replayable only when those fields can still be
  parsed and verified enough to freeze the observed request ref and recomputed
  digest. Missing, duplicate, unreadable, malformed, schema-invalid,
  producer-unverified, or ref-unidentified requests are unreplayable; in those
  cases planning emits no authoritative validation plan. If planning emits a
  fail-closed plan for a replayable invalid request, it must include a
  `request-invalid` planner diagnostic with the applicable closed
  `diagnostic-detail`; otherwise no authoritative validation plan is emitted.
  A request whose payload `artifact-ref` does not equal the contract-owned request
  ref, or whose physical artifact name does not recompute from that logical ref,
  uses `diagnostic-detail: request-ref-mismatch` when represented by a
  fail-closed plan.
- `scheduled_full` requests must not carry `affected-range` or `changed-files`;
  they are full-scope requests, not affected requests with an empty affected
  range.
- `affected-range.status: available` requires fixed endpoint SHAs, a complete
  changed-file list, and enough provenance consistency to confirm the range as
  the CI affected boundary.
- `affected-range.status: unavailable` means the control-plane logic could not
  establish a complete confirmed affected input from GitHub event payloads,
  GitHub API data, or checkout/git data.
- `affected-range.status: unavailable` requires `diagnostic: range-unconfirmed`
  and a `diagnostic-detail` that records whether range data is missing,
  incomplete, inconsistent, or complete-looking but not confirmable as the CI
  affected boundary.
- `affected-range.status: unavailable` forces fail-closed planning.
- `changed-files` is `null` for unavailable affected ranges. For
  `affected-range.status: available`, it is the confirmed repository-relative
  changed-file list. It may be empty only when the control plane positively
  confirms a zero-file affected range.
- A confirmed zero-file affected range is not scheduled-full and is not
  fail-closed. Planning emits `verdict-intent: executable`,
  `classification.impacts: []`, `classification.lightweight-only: true`, no
  selected subjects, no descriptor/validation/artifact obligations, no executable
  logical work groups or stable selectors requiring post-plan execution-batch
  materialization, no evidence expectations, and only the terminal
  `evidence-aggregation` obligation. Aggregation passes only after validating the
  plan, empty changed-files companion snapshot, non-null `changed-files-hash`, and
  aggregate evidence manifest and aggregate summary final artifacts; no batch
  evidence bundle is required or expected.
- Changed-file paths are canonical repository-relative Git paths. Each path uses
  `/` separators, is case-sensitive, and must not be empty, absolute, start with
  `./`, contain `\`, contain empty path segments, contain `.` or `..` path
  segments, or end with `/`.
- The control plane must not repair changed-file paths by normalizing,
  case-folding, or deduplicating them. Missing, incomplete, duplicate,
  non-canonical, or otherwise unconfirmed changed-file data makes the affected
  range unavailable with `diagnostic-detail: inconsistent` unless a more
  specific `range-unconfirmed` detail applies.
- After the control plane confirms the changed-file set and path shapes, it sorts
  the canonical changed-file sequence lexicographically by UTF-8 encoded bytes
  for deterministic plan and hash emission. Sorting a confirmed set is
  canonicalization, not repair.

Repository path representation:

- Unless a field explicitly says otherwise, any digest-bearing or
  evidence-bearing field that names repository content uses a canonical
  repository-relative Git path. This includes subject `root`, subject descriptor
  `path`, fact snapshot provider `roots`, artifact-obligation
  `descriptor-path`, descriptor coverage-target IDs, and release-shaped receipt
  descriptor `path`.
- Canonical repository-relative Git paths use the changed-file path invariants
  above: `/` separators, case-sensitive bytes, no empty value, no absolute path,
  no `./` prefix, no `\`, no empty segment, no `.` or `..` segment, and no
  trailing `/`.
- Directory root fields use the same representation without a trailing slash.
  The repository root, when it must be named as a directory root, is the only
  exception and is encoded as the exact string `.`.
- Producers must emit these paths only in canonical form. Consumers,
  aggregation, and acceptance must not repair, normalize, case-fold, or
  workspace-relativize path fields; non-canonical path values in the plan or its
  companion snapshots make the artifact schema-invalid or structurally invalid
  according to the containing artifact's validation boundary.

Mode-specific affected-range rules:

- For `pull_request`, `base-sha` is the explicit PR diff base used for changed
  file enumeration, normally the merge base between the event
  `pull_request.base.sha` and `pull_request.head.sha`; `base-tip-sha` records the
  event `pull_request.base.sha`; and `head-sha` is the event
  `pull_request.head.sha`. The changed-file set is the complete compare from the
  PR diff base to the head, whether validation executes on the head commit or a
  GitHub-created merge ref. GitHub API file-list data and checkout/git data used
  by the control plane must reconcile to the same diff-base/head endpoints and
  changed-file set. Base branch movement that changes `base-tip-sha` without
  changing the confirmed PR diff base is not by itself an inconsistency.
- For `push`, `base-sha` is the event `before` SHA and `head-sha` is the event
  `after` SHA; `base-tip-sha` is `null`. Deleted branches, all-zero endpoints,
  force-push ranges whose base cannot be fetched or compared, and otherwise
  unconfirmable push ranges make the affected range unavailable.
- The checked-out `validation-tree.commit-sha` must match the affected boundary.
  For `push`, it must equal `head-sha`. For `pull_request`, it must be either
  `head-sha` or a verified GitHub merge commit for the recorded
  `base-tip-sha`/`head-sha` pair. If the validation tree cannot be verified
  against the affected endpoints, the affected range is unavailable with
  `diagnostic-detail: unconfirmed-provenance`.
- File enumeration must consume every page or chunk from the chosen GitHub API or
  git source. Truncation, pagination failure, endpoint mismatch, or disagreement
  between sources used for reconciliation makes the range unavailable with
  `diagnostic-detail: incomplete` or `inconsistent`.
- Renames include both the old path and the new path because either side can
  affect ownership, descriptor mapping, or validation scope. Deletes include the
  deleted path. Paths that cannot be represented as canonical repository-relative
  Git paths make the range unavailable.

The exact physical filename is implementation-owned, but the file is an internal
workflow artifact or workspace handoff, not a user-authored source file.

## 6. Validation Plan File

The validation plan is one JSON artifact emitted by planning at the
contract-owned ref
`ci-validation/planning/<run-id>/<run-attempt>/validation-plan.json`. A missing,
unreadable, malformed, schema-invalid, digest-mismatched, or duplicate artifact at
that authoritative ref makes aggregation emit `invalid-plan`. Missing authoritative
plan artifacts use `diagnostic-detail: plan-missing`; duplicate authoritative plan
artifact instances use `diagnostic-detail: plan-duplicate`. Plan artifacts outside
that ref are non-authoritative auxiliary artifacts and must not be used for
execution-batch materialization or aggregation.
Execution-batch materialization and aggregation must verify the validation-plan
artifact's producer authority from platform/control-plane metadata before
trusting its payload. The authoritative plan must be produced by the logical
`plan` control-plane boundary for the same workflow run attempt; a plan artifact
authored by another job, an executable validation command, or an unverified
artifact instance is not authority even when its schema and self-digest match.
A producer-unverified plan produces `invalid-plan`.

### 6.1 Plan Envelope

```yaml
common-envelope: inherited
api-version: three.ci.validation.plan/v1alpha1
kind: ci-validation-plan
plan-id: string
plan-digest: string
mode: pull_request | push | scheduled_full
verdict-intent: executable | fail-closed
created-at: string
repository:
    owner: string
    name: string
run:
    workflow: string
    run-id: string
    run-attempt: string
validation-tree:
    commit-sha: string
    ref: string | null
affected-range:
    status: available | unavailable | not-applicable
    base-sha: string | null
    base-tip-sha: string | null
    head-sha: string | null
    changed-files-hash: string | null
request:
    artifact-ref: string
    request-digest: string
scheduled-full:
    enabled: boolean
planner:
    policy-source: validation-tree
    version: string | null
    execution-tree:
        observed-commit-sha: string | null
        source: plan-boundary
        verified: boolean
subject-universe:
    status: available | unavailable
    id: string | null
fact-snapshot:
    status: available | unavailable
    id: string | null
```

`request.artifact-ref` is the exact contract-owned planner-facing request artifact
ref from section 5. `request.request-digest` is the verified request digest the
planner recomputed before trusting the request or before emitting a replayable
`request-invalid` fail-closed plan. Both fields are required for every
authoritative plan. If the request artifact cannot be identified, parsed, verified
to the replayable request boundary, and canonically digested, the planner emits no
authoritative validation plan instead of inventing nullable request binding.
`plan-id` is an opaque run-scoped stable identifier assigned by the control plane.
It is not a content digest and must not be derived from a representation that
includes itself. `plan-digest` is the lowercase hexadecimal SHA-256 digest of the
RFC 8785 JSON Canonicalization Scheme canonical UTF-8 bytes for the frozen
validation plan after removing only the root-level `plan-digest` member. It must
match `^[0-9a-f]{64}$`. The plan payload must be I-JSON compatible for digesting;
duplicate object member names make it malformed. All remaining fields, including
nulls, false values, empty arrays or objects, request binding, diagnostics,
obligations, logical work groups, stable selectors, evidence expectations, and
detail profiles, participate in the digest. Execution batches are not planner
output and must not appear in the validation plan; they are derived only by the
post-plan `materialize-execution-batches` boundary.
Array order is preserved. Execution-batch manifests, batch evidence bundles, and
aggregation evidence bind to the frozen plan with both `plan-id` and
`plan-digest`, and post-run audit can replay-bind the plan to the normalized
request through the frozen request artifact ref and digest.

The `plan` boundary must observe its checkout tree before running planner logic
and bind that observation into `planner.execution-tree`. For authoritative plans,
`planner.policy-source` is `validation-tree`,
`planner.execution-tree.observed-commit-sha` equals the frozen
`validation-tree.commit-sha`, `planner.execution-tree.source` is `plan-boundary`,
and `planner.execution-tree.verified` is `true`. The planner implementation must
not self-attest this value from a command payload after planning; it is
control-plane provenance captured by the trusted planning boundary.
Execution-batch materialization, aggregation, and acceptance reject missing,
unverifiable, or mismatched planner execution-tree evidence as `invalid-plan`
because policy-bearing changes must be planned by the validation tree under
review.

Before computing `plan-digest`, the planner emits arrays in canonical order:

| Array family                                                                   | Canonical order                                                            |
| ------------------------------------------------------------------------------ | -------------------------------------------------------------------------- |
| Identifier-bearing records                                                     | Ascending by the record identifier field in UTF-8 byte lexicographic order |
| `source-impact-ids`, `source-expansion-ids`, references, paths, and string IDs | Ascending UTF-8 byte lexicographic order                                   |
| `planned-capabilities`                                                         | Declared capability order: build, test, lint, format, type-check           |
| Capability result arrays                                                       | Declared capability order                                                  |
| Subcheck requirement and result arrays                                         | Ascending by `subcheck-id` in UTF-8 byte lexicographic order               |
| `profile-coverage`                                                             | Ascending UTF-8 byte lexicographic order                                   |
| Diagnostics                                                                    | Ascending `diagnostic-id`                                                  |
| Tuple records without one identifier                                           | Ascending by the documented tuple fields; null sorts before strings        |

If two records compare equal under their canonical key, the plan is structurally
invalid unless the record kind explicitly permits duplicates. The planner must
not rely on source discovery order, API response order, filesystem order, or job
completion order for digest-affecting arrays.

Canonical array ordering is a structural validity rule for the frozen plan, not
only a planner emission convention. Aggregation must verify digest-affecting
arrays are in the canonical order defined above before accepting the plan; a
non-canonically ordered but digest-self-consistent plan is `invalid-plan` with
`diagnostic-detail: structurally-invalid`.

Scalar arrays in the plan are sets unless their schema explicitly says they are
ordered sequences or multiplicity-bearing lists. Duplicate values in set arrays,
including references, paths, string IDs, `planned-capabilities`, and
`profile-coverage`, make the plan structurally invalid.

Except for `plan-id`, which is explicitly run-scoped and opaque, every
identifier that participates in the validation plan digest must be derived
deterministically from a typed, versioned, RFC 8785 canonical JSON preimage
containing the record kind and the fields that define the record's semantic
identity. The exact identifier text may be a readable normalized prefix plus a
digest, but two equivalent planning inputs must produce the same plan-local
identifier values and references. Candidate-only audit IDs that exist only before
freezing, such as `subsumption-record.subsumed-candidate-ids`, are not
plan-local references but still use deterministic semantic-key IDs because they
remain inside the frozen plan digest.

For affected modes, `affected-range.status` is `available` or `unavailable` and
`scheduled-full.enabled` is `false`. For `scheduled_full`,
`affected-range.status` is `not-applicable`, affected-range hashes and SHAs are
`null`, and `scheduled-full.enabled` is `true`. The scheduled-full marker is the
full-scope selection source for scheduled plans; scheduled-full obligations and
work groups do not fabricate impact records.

For `affected-range.status: available`, `changed-files-hash` is the lowercase
hexadecimal SHA-256 digest of the RFC 8785 canonical JSON representation of this
versioned object:

```json
{
    "api-version": "three.ci.validation.changed-files/v1alpha1",
    "changed-files": []
}
```

`changed-files` is replaced by the canonical changed-file sequence from the
request contract. A confirmed zero-file affected range uses `changed-files: []`
and emits the digest of that object. For `affected-range.status: unavailable`
and for scheduled-full `not-applicable`, `changed-files-hash` is `null` and no
changed-files hash preimage is defined. Non-null `changed-files-hash` values
must match `^[0-9a-f]{64}$`. When `changed-files-hash` is `null`, zero artifact
instances must exist at the changed-files snapshot authoritative ref. An artifact
at that ref is non-authoritative and makes the plan `invalid-plan` because it
conflicts with the plan's no-snapshot contract.

When `changed-files-hash` is non-null, the planner must persist the exact
versioned changed-files object as a companion snapshot artifact:

```yaml
common-envelope: inherited
api-version: three.ci.validation.changed-files/v1alpha1
kind: ci-validation-changed-files-snapshot
artifact-ref: string
changed-files-hash: string
hash-payload:
    api-version: three.ci.validation.changed-files/v1alpha1
    changed-files: [string]
```

The changed-files snapshot artifact ref is contract-owned:
`ci-validation/planning/<run-id>/<run-attempt>/changed-files-snapshot.json`.
Exactly one artifact instance must exist at that authoritative ref when
`changed-files-hash` is non-null; zero or multiple instances make the plan
`invalid-plan`. `changed-files-hash` is computed only from the RFC 8785 canonical
JSON bytes of `hash-payload`; common-envelope fields, `kind`, and
`schema-diagnostics` are not part of that hash preimage. Aggregation and
acceptance must load the snapshot, verify its common-envelope `run-id` and
`run-attempt` match the plan, recompute `changed-files-hash`, and verify the
snapshot was produced by the logical `plan` control-plane boundary for the same
workflow run attempt. They reject the plan as `invalid-plan` if the artifact ref,
artifact instance count, snapshot producer, snapshot envelope, schema, or digest
is missing, unverified, malformed, or mismatched. The snapshot `changed-files`
array must be in the canonical UTF-8 byte lexicographic order defined by the
request contract and must exactly match the changed-file sequence frozen into the
plan; a self-consistent but noncanonical snapshot is still structurally invalid
with `diagnostic-detail: changed-files-snapshot-noncanonical`.

`subject-universe.id` is the lowercase hexadecimal SHA-256 digest of the RFC
8785 canonical JSON representation of this versioned object:

```json
{
    "api-version": "three.ci.validation.subject-universe/v1alpha1",
    "subjects": []
}
```

`subjects` is replaced by the frozen `subjects` section. When
`subject-universe.status` is `available`, `subject-universe.id` must match
`^[0-9a-f]{64}$`, `subjects` is authoritative for planning, and aggregation must
recompute the digest from that frozen section. There is no subject-universe
companion artifact; a missing artifact at a would-be subject-universe ref is not
evidence of failure because no such ref exists in this contract. When
`subject-universe.status` is `unavailable`, `subject-universe.id` is `null`,
`subjects` must be empty, and diagnostics must explain why the subject universe
could not be produced or confirmed; aggregation must not recompute a
subject-universe digest for that plan. `fact-snapshot.id` is the lowercase
hexadecimal SHA-256 digest of a
companion `three.ci.validation.fact-snapshot/v1alpha1` artifact containing the
deterministic provider facts used for planning. It must match `^[0-9a-f]{64}$`
when `fact-snapshot.status` is `available`; when `fact-snapshot.status` is
`unavailable`, its `id` is `null` and diagnostics must explain why the snapshot
could not be produced or confirmed. A fail-closed plan with
`fact-snapshot.status: unavailable` has no required fact snapshot artifact. A
fail-closed plan may instead use
`fact-snapshot.status: available` with a digest-bound artifact that records
provider entries with `status: unavailable`; in that case, diagnostics still
explain why the plan is fail-closed. The fact snapshot artifact is
plan-inspectable evidence for planning inputs, not executable validation evidence
and not release proof. When `fact-snapshot.status` is `unavailable`, zero
artifact instances must exist at the fact snapshot authoritative ref. An artifact
at that ref is non-authoritative and makes the plan `invalid-plan` because it
conflicts with the plan's no-snapshot contract.

The fact snapshot artifact uses this minimum shape:

```yaml
common-envelope: inherited
api-version: three.ci.validation.fact-snapshot/v1alpha1
kind: ci-validation-fact-snapshot
artifact-ref: string
fact-snapshot-id: string
plan-id: string
providers:
    - provider: dotnet | python | javascript-typescript | workflow-release
      provider-version: string | null
      status: available | unavailable
      roots: [string]
      subjects: [subject-id]
      dependency-edges:
          - from-subject-id: string
            to-subject-id: string
            relation: project-reference | package-reference | workspace | tooling
      tooling-surfaces: [string]
      descriptors:
          - descriptor-path: string
            descriptor-identity: string | null
            owner-subject-id: string | null
            source: ecosystem-provider | workflow-release-provider
      target-catalog:
          catalog-id: string | null
          descriptor-paths: [string]
          entries:
              - descriptor-path: string
                profile: string
                artifact:
                    kind-family: string
                    concrete-kind: string
                    logical-artifact-role: string
                    variant-dimensions: object
                    expected-artifact-refs: [string]
                release-receipt:
                    expected-family: string
                    logical-receipt-role: string
                    variant-dimensions: object
      diagnostics: [diagnostic-record]
```

Within one fact snapshot, `descriptor-path` is a global unique key across every
provider `descriptors` array. No two descriptor records may share the same
`descriptor-path`, even if `descriptor-identity`, `owner-subject-id`, or `source`
would differ. Target-catalog `descriptor-paths` and target-catalog entries must
resolve to that unique descriptor record. If providers cannot reconcile a single
descriptor record for a path required by planning, planning fails closed rather
than emitting duplicate descriptor facts; an emitted available fact snapshot with
duplicate descriptor paths is structurally invalid.
For release-shaped descriptors, `descriptor-identity` is fail-closed evidence:
the descriptor fact must exist and `descriptor-identity` must be a non-empty
string before no-publish release-shaped success can match the planned obligation.
Missing descriptor facts, null identities, and empty identities are blocked.
Outside that release-shaped/no-publish obligation-binding path, null descriptor
identity remains valid descriptor-fact modeling for unsupported, inactive, or
otherwise non-release-bound descriptor observations.

`from-subject-id` depends on `to-subject-id`. Downstream affected subjects are
the reverse transitive dependents of directly affected subjects, limited to
active subjects. Dependency cycles are traversed with a visited set and emitted
once in canonical subject order; if a cycle or partial graph prevents a complete
deterministic downstream closure, planning fails closed.

The provider ID set is closed. A fact snapshot may contain at most one entry for
each provider ID; duplicate provider IDs make the snapshot invalid. The
`javascript-typescript` provider is the single PNPM-backed fact provider for both
JavaScript and TypeScript subjects; subject records still use their normalized
`ecosystem` value to distinguish JavaScript and TypeScript validation scope.

The fact snapshot artifact ref is contract-owned:
`ci-validation/planning/<run-id>/<run-attempt>/fact-snapshot.json`.
Exactly one artifact instance must exist at that authoritative ref when
`fact-snapshot.status` is `available`; zero or multiple instances make the plan
`invalid-plan`. `fact-snapshot-id` equals the plan envelope `fact-snapshot.id` and
is computed as the RFC 8785 digest of the artifact projection containing only
`api-version`, `kind`, and `providers`; common-envelope fields, `artifact-ref`,
`plan-id`, `fact-snapshot-id`, and `schema-diagnostics` are not part of the hash
preimage. Provider entries are sorted by `provider`; `roots`, `subjects`, and
`tooling-surfaces` are sorted lexicographically by UTF-8 encoded bytes;
descriptor fact records are sorted by `(descriptor-path, descriptor-identity,
owner-subject-id, source)` with null before strings; `target-catalog` descriptor
paths are sorted lexicographically by UTF-8 encoded bytes; `target-catalog`
entries are sorted by `(descriptor-path, profile, artifact.kind-family,
artifact.concrete-kind, artifact.logical-artifact-role,
release-receipt.expected-family, release-receipt.logical-receipt-role)` with
`artifact.expected-artifact-refs` sorted lexicographically by UTF-8 encoded bytes
and variant-dimension objects compared by RFC 8785 canonical JSON bytes;
`dependency-edges` are sorted by `(from-subject-id, to-subject-id, relation)` with
each field compared as UTF-8 bytes; diagnostics are sorted by `diagnostic-id`.
Null sorts before strings for any future nullable tuple field.
Provider entries that do not own target-catalog facts use
`target-catalog.catalog-id: null`, `target-catalog.descriptor-paths: []`, and
`target-catalog.entries: []`.
Unavailable provider entries inside an emitted fact snapshot artifact must appear
with `status: unavailable`, empty fact arrays, and diagnostics explaining why the
planner failed closed. Planning, aggregation, and acceptance must verify the
artifact ref, artifact instance count, producer authority from the logical `plan`
control-plane boundary, common-envelope `run-id` and `run-attempt`, `plan-id`,
schema, the canonical ordering rules above, and recomputed `fact-snapshot-id`
before treating any plan whose `fact-snapshot.status` is `available` as
structurally valid, including fail-closed plans. A self-consistent fact snapshot
whose arrays or tuple records are not in canonical order is structurally invalid
with `diagnostic-detail: fact-snapshot-noncanonical`. The artifact `plan-id` must
equal the frozen validation plan's `plan-id`; a mismatch is an `invalid-plan`
failure even though `plan-id` is not part of the `fact-snapshot-id` digest
preimage.

### 6.2 Plan Sections

The plan contains these top-level sections:

```yaml
classification:
    impacts: [impact-record]
    broad-expansions: [broad-expansion-record]
    subject-selection-provenance: [subject-selection-provenance-record]
    subsumptions: [subsumption-record]
    lightweight-only: boolean
subjects: [validation-subject-snapshot]
descriptor-obligations: [descriptor-obligation]
validation-obligations: [validation-obligation]
artifact-obligations: [artifact-obligation]
work-groups: [work-group]
evidence-expectations: [evidence-expectation]
detail-profiles: [detail-profile-definition]
diagnostics: [planner-diagnostic]
```

These sections are planner-owned logical policy data. They authorize selected
obligations, logical work groups, stable selectors, and evidence expectations,
but they do not assign concrete jobs, batches, artifact bundle refs, or writer
identity. Post-plan execution-batch materialization consumes this policy data and
the companion planning snapshots to produce a separate execution-batch manifest.

Fail-closed plans still contain envelope, classification, diagnostics, and enough
provenance to inspect why no executable validation plan was authorized. They have
no executable validation work groups. Every emitted plan, including fail-closed
plans, must satisfy the schema and structural identity/reference rules in this
document. Fail-closed plans must leave descriptor, validation, artifact,
evidence-expectation, and detail-profile sections empty. Their `work-groups`
section is empty except for the single non-executable terminal
`evidence-aggregation` work group that emits the failed aggregate verdict.
Inspectability is carried by classification, snapshot status, provenance fields,
and diagnostics instead of non-executable obligation records.
`verdict-intent: fail-closed` structurally requires at least one planner
diagnostic with `verdict-effect: fail-closed`. A structurally valid fail-closed
plan always aggregates to `verdict: failed`, `reason.fail-closed: true`, and at
least one `failure` with `kind: fail-closed`; a fail-closed plan without a
fail-closed diagnostic is structurally invalid rather than an empty successful
plan.

Executable plans require `subject-universe.status: available` and
`fact-snapshot.status: available`. This includes zero-file no-scope executable
plans: they still contain the discovered subject universe with every discovered
subject marked `selection-status: not-selected`, and an available fact snapshot
with the provider discovery facts needed to justify that universe. A zero-file
plan has no selected subjects, no descriptor/validation/artifact obligations, and
no executable work groups, but it does not skip subject discovery. The subject
universe has no companion artifact in this design; its authority is the
validation plan digest plus aggregation recomputation from the frozen `subjects`
section. Fail-closed plans may use `unavailable` with `id: null`, but diagnostics
must identify which snapshot could not be produced or confirmed and why.
Aggregation must reject structurally invalid plans instead of converting them
into successful inspectable fail-closed evidence.

The exact JSON Schema file and type generator strategy are implementation-owned,
but every section above is part of the low-level data contract.

Each `broad-expansion-record` records the minimum audit trail for non-minimal
scope selection:

```yaml
expansion-id: string
source-impact-id: string
category: ecosystem | global | workflow-release-infrastructure
reason: string
resulting-scope:
    ecosystems: [string]
    subjects: [string]
    descriptors: all-discovered | selected | none
```

The expansion record is inspectability data. Execution still consumes the final
selected subjects, obligations, and work groups rather than recomputing expansion.
It must not be used to imply obligation, work-group, or evidence subsumption.
Any duplicate removal caused by an expanded scope must have an explicit
`subsumption-record`.

Each `subject-selection-provenance-record` records why a selected subject is in
scope:

```yaml
provenance-id: string
subject-id: string
selection-kind: direct | downstream | broad-expansion | scheduled-full
source-impact-ids: [string]
direct-subject-id: string | null
dependency-edge-basis:
    - from-subject-id: string
      to-subject-id: string
      relation: project-reference | package-reference | workspace | tooling
broad-expansion-id: string | null
scheduled-full-source: boolean
```

Every active selected subject in an executable plan must have at least one
subject-selection provenance record, and mixed-impact unions may emit multiple
records for the same subject when independent direct, downstream, broad, or
scheduled causes select it. Direct project-scoped selections use `selection-kind:
direct`, the selected `subject-id`, the source project impact,
`direct-subject-id: null`, and an empty `dependency-edge-basis`. Downstream
selections use `selection-kind: downstream`, identify the originally affected
`direct-subject-id`, and list the digest-bound dependency edges that justify the
selected subject as a reverse transitive dependent. Broad selections use
`selection-kind: broad-expansion`, cite the corresponding expansion, and set
`scheduled-full-source: false`. Scheduled-full selections use `selection-kind:
scheduled-full`, `source-impact-ids: []`, `direct-subject-id: null`,
`dependency-edge-basis: []`, `broad-expansion-id: null`, and
`scheduled-full-source: true`; they are valid only when the plan-level
`scheduled-full.enabled` is `true` and must not fabricate impact records. The
planner must preserve all independent selection causes as provenance records or
as explicit `subsumption-record` entries that name the retained provenance.
Aggregation verifies that provenance subjects are selected subjects, source
impacts exist when listed, broad expansion IDs resolve when non-null,
`scheduled-full-source` agrees with `selection-kind` and the plan-level scheduled
marker, and every dependency edge basis is present in the fact snapshot; it does
not recompute downstream closure from repository state during acceptance.

### 6.3 Named Record Minimum Shapes

Each `impact-record` has:

```yaml
impact-id: string
category: project-scoped | ecosystem-scoped | workflow-release-infrastructure | global | known-non-impacting | unknown
matched-paths: [string]
source-rule: string
rationale: string
coverage-target:
    type: subject | ecosystem | tooling-surface | global | none
    id: string | null
requires:
    descriptor-validation: boolean
    downstream-expansion: boolean
    broad-expansion: boolean
diagnostic: diagnostic-code | null
```

`source-rule` is the stable classifier rule identifier. `rationale` is a
human-inspectable explanation of why the matched paths produced the category,
coverage target, required expansions, and diagnostic.

For affected plans with `affected-range.status: available`, the union of
`classification.impacts[].matched-paths` must equal the canonical changed-file
sequence in the changed-files companion snapshot as a set: no changed path may be
omitted, no unmatched path may appear, and no path may appear in more than one
impact record. A changed path that is unknown or unclassifiable is still
represented by an `impact-record` with `category: unknown` and the corresponding
fail-closed diagnostic rather than being omitted. Aggregation verifies this
coverage after loading the changed-files snapshot; a mismatch makes the plan
`invalid-plan`.

Each `descriptor-obligation` has:

```yaml
descriptor-obligation-id: string
source-impact-ids: [string]
descriptor-scope: selected | ecosystem | all-discovered
coverage-target:
    type: descriptor
    id: string
required: boolean
blocking: boolean
work-group-id: string | null
expected-evidence-id: string | null
```

Every descriptor obligation must resolve to a digest-bound fact snapshot
descriptor record by `coverage-target.id == descriptor-path`. For descriptor
obligations derived from ecosystem-owned subjects, `owner-subject-id` must equal
the selected subject; for workflow-release-only descriptor/tooling surfaces,
`owner-subject-id` is `null` and the obligation is reached through a
`tooling-surface` impact. Missing or mismatched descriptor fact backing makes the
plan structurally invalid.

Descriptor-validation obligations, work groups, and evidence expectations bind
one-to-one by `descriptor-obligation-id`. A required descriptor obligation with
non-null `work-group-id` and `expected-evidence-id` must reference a
`descriptor-validation` work group and evidence expectation whose
`coverage-target.type` is `descriptor` and whose `coverage-target.id` is exactly
the obligation descriptor path. No two descriptor obligations may share the same
descriptor-validation work group or evidence expectation. Descriptor-validation
execution must not rederive descriptor identity, owner, or scope from repository
files after planning; it consumes the frozen obligation and the digest-bound fact
snapshot descriptor record.

Each `validation-obligation` has:

```yaml
validation-obligation-id: string
source-impact-ids: [string]
kind: lightweight-preflight | ecosystem-gate | release-shaped-artifact | workflow-release-tooling
coverage-target:
    type: subject | ecosystem | descriptor | tooling-surface | artifact-obligation | lightweight-policy
    id: string
required: boolean
blocking: boolean
work-group-id: string | null
expected-evidence-id: string | null
```

Each `artifact-obligation` has:

```yaml
artifact-obligation-id: string
source-impact-ids: [string]
subject-id: string
descriptor-path: string
profile-coverage: [string]
artifact:
    kind-family: string
    concrete-kind: string
    logical-artifact-role: string
    variant-dimensions: object
    expected-artifact-refs: [string]
release-receipt:
    expected-family: string
    logical-receipt-role: string
    variant-dimensions: object
credential-posture: credential-free | unsigned-equivalent | unavailable
expected-evidence-category: release-shaped-artifact
required: boolean
blocking: boolean
validation-obligation-id: string
work-group-id: string | null
expected-evidence-id: string | null
```

Every artifact obligation must resolve to a digest-bound descriptor fact by
`descriptor-path` and to the relevant target-catalog fact when its artifact or
release-receipt dimensions are catalog-derived. The frozen
`artifact.kind-family`, `artifact.concrete-kind`,
`artifact.logical-artifact-role`, `artifact.variant-dimensions`,
`artifact.expected-artifact-refs`, `release-receipt.expected-family`,
`release-receipt.logical-receipt-role`, and `release-receipt.variant-dimensions`
must be derivable from those fact records. The artifact obligation `subject-id`
must resolve to an active selected descriptor-backed subject whose
`descriptor.path` equals `descriptor-path`; the digest-bound descriptor fact for
that path must have `owner-subject-id` equal to `subject-id`, and the
release-shaped work group's ecosystem and runner family must match the resolved
subject ecosystem mapping. For a required artifact obligation,
`expected-artifact-refs` is the complete validation-only artifact coverage the work
group must materialize or inspect for that obligation, must be non-empty, and must
not identify publication targets or remote release state. Missing, partial,
empty, or mismatched subject, descriptor, target-catalog, ecosystem, or runner
fact backing makes the plan structurally invalid.

Each `evidence-expectation` has:

```yaml
evidence-expectation-id: string
work-group-id: string
coverage-target:
    type: subject | ecosystem | descriptor | tooling-surface | artifact-obligation | lightweight-policy
    id: string
category: lightweight-preflight | ecosystem-gate | descriptor-validation | release-shaped-artifact | workflow-release-tooling
planned-capabilities: [build | test | lint | format | type-check] | null
detail-profile: string | null
required: boolean
blocking-if-missing: boolean
```

Every non-null `detail-profile` value in a work group or evidence expectation must
resolve to exactly one `detail-profile-definition` by `detail-profile-id`. Detail
profiles are plan-local, digest-bound contracts for validation categories whose
success depends on more than a single ecosystem capability. They are not free-form
labels and they cannot be supplied only by the receipt writer.

Each `detail-profile-definition` has:

```yaml
detail-profile-id: string
category: lightweight-preflight | workflow-release-tooling
coverage-target:
    type: subject | ecosystem | descriptor | tooling-surface | lightweight-policy
    id: string
required-subchecks:
    - subcheck-id: string
      check-kind: configuration | policy | contract | tool-discovery | documentation
      blocking: boolean
      description: string
```

The profile category and coverage target must match every work group and evidence
expectation that references the profile. `required-subchecks` is the complete
planner-authored subcheck set for that profile and must be non-empty. Required
subcheck identifiers are compared as opaque strings, but they must be stable within
the profile and unique after Unicode NFC normalization. A selected profile with an
empty, duplicate, or mismatched subcheck set makes the plan structurally invalid.

Each `subsumption-record` has:

```yaml
subsumption-id: string
source-impact-ids: [string]
source-expansion-ids: [string]
subsumed-kind: string
subsumed-candidate-ids: [string]
retained-id: string
reason: string
```

`subsumed-kind` is one of `descriptor-obligation`, `validation-obligation`,
`artifact-obligation`, `work-group`, `evidence-expectation`, `detail-profile`, or
`subject-selection-provenance`.

Each `planner-diagnostic` has:

```yaml
diagnostic-id: string
code: diagnostic-code
detail: diagnostic-detail | null
severity: info | warning | fail-closed | blocking-failure
source:
    type: request | impact | subject | descriptor | fact-provider | aggregation
    id: string | null
message: string
verdict-effect: none | fail-closed | failed
```

Each `diagnostic-record` has:

```yaml
diagnostic-id: string
code: diagnostic-code
detail: diagnostic-detail | null
severity: info | warning | fail-closed | blocking-failure
verdict-effect: none | fail-closed | failed
message: string | null
source:
    type: request | impact | subject | descriptor | fact-provider | work-group | aggregation
    id: string | null
```

`diagnostic-id` is deterministic within its artifact. It is derived from the
producer kind, stable source type and ID, diagnostic `code`, `detail`, and the
stable artifact/result location such as work-group ID, evidence expectation ID,
observed entry ID, or aggregate failure tuple. If multiple diagnostics would have
the same derived ID, the producer appends a deterministic ordinal based on the
same canonical ordering input; source discovery order, log order, and job
completion order must not affect diagnostic IDs.

Binding rules:

- All identifier-bearing records inside one frozen validation plan are resolved
  in typed plan-local namespaces. For each record kind, its identifier field must
  be unique within that record-kind namespace in the plan. This applies at least
  to `impact-id`, `expansion-id`, `subject-id`, `descriptor-obligation-id`,
  `validation-obligation-id`, `artifact-obligation-id`, `work-group-id`,
  `evidence-expectation-id`, `detail-profile-id`, `subsumption-id`, and
  `diagnostic-id`.
- References are not resolved by searching all string identifiers. Each non-null
  plan-local reference resolves only to its declared target namespace in the
  same frozen plan, either by the reference field name or by the accompanying
  kind/type discriminator such as `coverage-target.type`, `subsumed-kind`, or
  diagnostic `source.type`.
- Coverage target IDs use these namespaces:
    - `subject` resolves to `validation-subject-snapshot.subject-id`;
    - `ecosystem` is one normalized ecosystem identifier from the subject
      ecosystem enum;
    - `descriptor` is the canonical repository-relative descriptor path of a
      digest-bound fact-snapshot descriptor record, including descriptor-backed
      subject records and workflow-release-only descriptor/tooling records with
      `owner-subject-id: null`;
    - `tooling-surface` is a workflow-release provider surface ID from the
      closed set `planner`, `classifier`, `fact-provider`,
      `descriptor-contract`, `workflow-release-contract`,
      `authoring-validation`, `target-catalog`, `workflow-orchestration`,
      `build-execution`, `publish-execution`, `smoke-validation`, or
      `descriptor-schema-documentation`;
    - `artifact-obligation` resolves to
      `artifact-obligation.artifact-obligation-id`;
    - `lightweight-policy` uses the closed ID `known-non-impacting`;
    - `global` uses `id: null`;
    - `aggregation` uses `id: ci-validation-aggregate`;
    - `none` uses `id: null`.
- Duplicate identifiers within a namespace, unresolved references, or references
  that resolve only by guessing across namespaces make the plan structurally
  invalid. An executable plan must not be emitted with structural identity
  errors; consumers must reject structurally invalid executable plans rather than
  reinterpret, repair, or resolve references against receipts, prior plans, or
  repository state.
- `subsumption-record.subsumed-candidate-ids` are deterministic pre-freeze
  planner candidate audit identifiers, not plan-local references. Each candidate
  ID is derived from the record kind plus the deterministic semantic key of the
  candidate before subsumption. `retained-id` must resolve in the frozen plan
  within the namespace named by `subsumed-kind`.
- For `subsumed-kind: subject-selection-provenance`, `retained-id` resolves to a
  `classification.subject-selection-provenance[].provenance-id`. Its
  `subsumed-candidate-ids` name deterministic pre-freeze candidate provenance IDs
  for the same selected subject and selection cause family that were folded into
  the retained provenance record.
- Obligations reference their source impacts for auditability.
- Selected subjects record direct, downstream, broad-expansion, or scheduled-full
  provenance in `classification.subject-selection-provenance`; acceptance reads
  that frozen provenance rather than recomputing why a subject was selected.
- Scheduled-full obligations may have empty `source-impact-ids`; in that mode,
  the plan-level `scheduled-full` marker is their full-scope selection source.
- Required executable obligations must reference a work group and evidence
  expectation unless planning fails closed.
- Every emitted descriptor obligation, validation obligation, artifact
  obligation, executable work group, and evidence expectation in this design is
  verdict-relevant: obligation `required` and `blocking`, work-group
  `expected-evidence.required`, and evidence expectation `required` and
  `blocking-if-missing` must all be `true`. Descriptor obligations must resolve
  to gating work and evidence unless planning fails closed before derivation.
  Non-contractual auxiliary telemetry may be uploaded as logs or artifacts, but
  it must not emit
  `ci-validation-receipt`, appear in `evidence-expectations`, or affect
  aggregation.
- Each executable `work-group-id` in a plan must have exactly one
  `evidence-expectation`; the terminal `evidence-aggregation` work group has no
  executable evidence expectation. Batch result-to-expectation matching is
  therefore defined by `plan-id` and `work-group-id`, even when multiple logical
  work groups are coalesced into one execution batch.
- Every executable work group and evidence expectation pair must be sourced by
  exactly one required obligation chain. `lightweight-preflight`,
  `ecosystem-gate`, and `workflow-release-tooling` pairs must be referenced by
  exactly one matching `validation-obligation`. `descriptor-validation` pairs must
  be referenced by exactly one matching `descriptor-obligation`.
  `release-shaped-artifact` pairs must be referenced by exactly one matching
  `artifact-obligation` and by that artifact obligation's matching
  `release-shaped-artifact` validation obligation. Orphan executable work groups
  or evidence expectations, including tooling-surface work introduced by
  infrastructure expansion, make the plan structurally invalid instead of
  auxiliary validation work.
- For each executable work group and its evidence expectation, `coverage-target`,
  evidence `category`, `planned-capabilities`, `detail-profile`, and
  required/blocking semantics must match exactly. A mismatch between the
  duplicated work-group `expected-evidence` contract and the
  `evidence-expectation` record makes the plan structurally invalid.
- `detail-profile` is required and non-null for `lightweight-preflight` and
  `workflow-release-tooling` work groups whose `planned-capabilities` is `null`
  because those receipts use `category-result` evidence. It is a stable,
  plan-local profile identifier that names the intended lightweight or tooling
  validation profile for the frozen `coverage-target`, and it must match
  `^[a-z0-9][a-z0-9._-]{0,127}$`. It is `null` for capability-result branches and
  for categories whose required detail is fully defined by descriptor or artifact
  obligations.
- For each required validation obligation with non-null `work-group-id` and
  `expected-evidence-id`, the referenced executable work group and evidence
  expectation must bind back to that obligation's exact validation intent:
  `work-group.kind` must equal `validation-obligation.kind`,
  `evidence-expectation.category` must equal `validation-obligation.kind`, and
  both coverage targets must exactly equal the validation obligation
  `coverage-target`. No two frozen validation obligations may share the same
  work group or evidence expectation; any duplicate candidate work must be
  removed before freezing and recorded only through an explicit
  `subsumption-record`.
- Release-shaped validation obligations, work groups, and evidence expectations
  bind one-to-one to frozen artifact obligations by `artifact-obligation-id`.
  A required artifact obligation with non-null `work-group-id` and
  `expected-evidence-id` must reference a `release-shaped-artifact` work group
  and evidence expectation whose `coverage-target.type` is `artifact-obligation`
  and whose `coverage-target.id` is exactly that `artifact-obligation-id`. No two
  artifact obligations may share the same release-shaped work group or evidence
  expectation. The artifact obligation's `validation-obligation-id` must resolve
  to exactly one `release-shaped-artifact` validation obligation whose
  `coverage-target.type` is `artifact-obligation`, whose `coverage-target.id` is
  exactly the artifact obligation's `artifact-obligation-id`, and whose
  `work-group-id` and `expected-evidence-id` equal the artifact obligation fields.
  No two artifact obligations may share a validation obligation. Execution must
  not rederive artifact shape from descriptors.
- `descriptor-validation` work groups and evidence expectations bind one-to-one
  to frozen descriptor obligations by the obligation's `work-group-id` and
  `expected-evidence-id`; release-shaped artifact work groups are produced from
  artifact obligations.
- Missing required evidence and verdict-affecting blocking diagnostics fail
  aggregation.
- Informational diagnostics, including known non-impacting diagnostics, must not
  by themselves authorize or block execution.

## 7. Subject Universe Snapshot

Each discovered validation subject snapshot has:

```yaml
subject-id: string
ecosystem: dotnet | python | javascript | typescript | ruby | other
root: string
activity-status: active | explicitly-excluded | inactive
selection-status: selected | not-selected
capability-class: descriptor-backed | validation-only
descriptor:
    path: string | null
    identity: string | null
capabilities:
    build: boolean
    test: boolean
    lint: boolean
    format: boolean
    type-check: boolean
    release-shaped-artifacts: boolean
inclusion:
    source: descriptor | workspace | solution
    reason: string
exclusion:
    reason: string | null
```

Rules:

- `activity-status` records whether the discovered subject can participate in
  validation at the repository level.
- `selection-status` records whether the subject is selected by this specific
  validation plan.
- Only subjects with `activity-status: active` and `selection-status: selected`
  can produce executable validation work.
- A selected executable subject must have an explicit fact provider and execution
  mapping in this LLD. `ruby` is provider-bound and selectable; `other` subjects
  may be discovered only as `inactive` or `not-selected`; if an affected,
  ecosystem, global, or scheduled-full scope would otherwise select an `other`
  subject, planning fails closed with `fact-provider-insufficient`.
- An `other` subject-universe record is an unsupported audit entry, not
  a provider-bound validation subject. It must have `activity-status: inactive`,
  `selection-status: not-selected`, `descriptor.path: null`,
  `descriptor.identity: null`, all capability flags `false`, and
  `exclusion.reason: unsupported-ecosystem`. It cannot appear in provider
  `subjects`, dependency edges, subject-selection provenance, obligations, work
  groups, or evidence expectations.
- Explicit repository rules may adjust `activity-status` or `exclusion.reason`
  for discovered candidates, but they are not an inclusion authority for adding
  validation subjects.
- `capability-class: validation-only` subjects cannot have publish obligations.
- A selected active `capability-class: validation-only` subject must have at least
  one enabled validation capability among `build`, `test`, `lint`, `format`, or
  `type-check`. If a project, ecosystem, global, infrastructure, or scheduled-full
  scope selects a validation-only subject and no enabled validation capability can
  be derived from provider facts, planning fails closed with
  `no-validation-capability` rather than emitting a selected subject with no
  required evidence. This does not apply to zero-file or known-non-impacting
  lightweight-only plans, because those plans have no selected validation-only
  subjects.
- `capability-class: descriptor-backed` subjects may have release-shaped artifact
  obligations only when descriptor validation can derive them without
  confirmation gaps.
- Subject IDs are stable within a repository and should be path- and
  ecosystem-derived rather than display-name-derived.

## 8. Fact Provider Realization

The implementation uses one fact-provider seam per ecosystem family plus one
workflow-release tooling provider.

| Provider              | Discovery source                                                         | Required facts                                                                                                                                                                                                |
| --------------------- | ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| .NET                  | solution/MSBuild project graph under active roots                        | .NET subject ownership, project roots, project references when available, packable descriptor-backed projects, validation-only test/build projects, Windows runner expectation                                |
| Python                | `uv` workspace and project metadata under active roots                   | Python subject ownership, workspace members, package roots, validation-only projects, dependency facts when safely available, Ubuntu runner expectation                                                       |
| JavaScript/TypeScript | PNPM workspace metadata under active roots                               | JavaScript/TypeScript subject ownership, workspace packages, package roots, validation-only packages, dependency facts when safely available, Ubuntu runner expectation                                       |
| Ruby                  | RubyGems release descriptors and gemspec metadata under active roots     | Ruby subject ownership, gem roots, validation-only build capability from gemspecs, dependency facts when safely available, Ubuntu runner expectation                                                          |
| workflow-release      | release descriptors, target catalog, workflow-release docs/tooling paths | workflow-release tooling surfaces, descriptor metadata for descriptor-backed subjects owned by ecosystem providers, target-catalog facts, descriptor schema documentation surfaces, smoke validation surfaces |

The JavaScript/TypeScript row is one provider seam and emits the single fact
snapshot provider ID `javascript-typescript`. It may discover subjects whose
normalized subject `ecosystem` is `javascript` or `typescript`; ecosystem-scoped
selection, work-group IDs, evidence expectations, and runner mapping continue to
use the subject ecosystem rather than splitting the provider entry.

The workflow-release provider does not create `workflow-release` subjects in the
subject universe. Workflow-release-only validation is represented as
`workflow-release-tooling` work groups with `coverage-target.type:
tooling-surface`; descriptor/tooling-only workflow-release surfaces are tooling
surfaces, not `validation-subject-snapshot` records. For any plan with
`subject-universe.status: available` and `fact-snapshot.status: available`, every
provider-bound frozen subject-universe record, including inactive and not-selected
subjects, must bind to exactly one `status: available` provider entry that lists
the subject ID and supports that subject's ecosystem: `dotnet` for `.NET`,
`python` for Python, `javascript-typescript` for JavaScript or TypeScript, and
`ruby` for Ruby.
Conversely, every subject ID listed by those ecosystem provider entries must have
exactly one matching frozen subject-universe record. Unsupported `other` audit
entries are excluded from this provider-subject equality check only when they
satisfy the section 7 unsupported-subject constraints. Descriptor
metadata emitted by the `workflow-release` provider for ecosystem-owned
descriptor-backed subjects is digest-bound provider data in `descriptors` and
must not list those subjects in `provider.subjects`. Missing provider coverage,
duplicate provider coverage for the same provider-bound subject, unavailable
provider coverage in an executable plan, subject IDs present in provider facts but
absent from the subject universe, unsupported subject IDs present in provider
facts, or an ecosystem/provider mismatch makes planning fail closed with
`fact-provider-insufficient`.

Descriptor and target-catalog facts that affect descriptor obligations,
release-shaped artifact obligations, descriptor-validation scope, or smoke
validation scope must be represented in the digest-bound fact snapshot before the
plan can derive those obligations. The planner must not derive those obligations
from repository state, descriptor files, or target catalog data that is absent
from the frozen fact snapshot.

Provider failure rules:

- If discovery fails for a selected ecosystem scope, planning fails closed.
- If dependency facts are unavailable or insufficient for a project-scoped
  change, planning fails closed.
- Providers report capabilities and facts; the planner assigns normalized subject
  capability class and final obligations.
- Ecosystem-gate `planned-capabilities` are derived from the frozen selected
  subject capability booleans for the gate's scope. For a subject-scoped gate, the
  set is every enabled capability among `build`, `test`, `lint`, `format`, and
  `type-check` on that selected active subject. For an ecosystem-scoped gate, the
  set is the union of enabled capabilities across all selected active subjects in
  that ecosystem after downstream and broad-scope expansion. Provider-reported
  capability facts must be sufficient to justify the frozen subject capability
  booleans before planning can emit an executable ecosystem gate. If capability
  facts are missing, contradictory, or insufficient for a selected scope, planning
  fails closed with `fact-provider-insufficient`; the planner must not emit an
  under-specified non-null `planned-capabilities` set merely because receipts can
  later equality-check it.
- For every selected active validation-only subject in an executable non-lightweight
  plan, the derived subject-scoped `planned-capabilities` set must be non-empty and
  covered by a required ecosystem-gate validation obligation, executable work
  group, and evidence expectation. If provider facts are available but prove that
  the selected validation-only subject has no executable validation capability,
  planning fails closed with `no-validation-capability`. If provider facts cannot
  prove the capability set either way, planning fails closed with
  `fact-provider-insufficient`.

Fact collection must not perform build, test, packaging, release-shaped artifact
validation, publication, or remote publish-state observation. For the listed
release/build activities, build, test, packaging, and release-shaped artifact
validation may belong to execution-layer work groups authorized by the validation
plan; other plan-authorized validation-only checks, including lint, format,
type-check, descriptor validation, workflow-release-tooling validation, and
lightweight preflight, may also belong to execution-layer work groups.
Publication and remote publish-state observation are never planned, executed, or
accepted as CI validation evidence.

The exact commands used to query ecosystem tools are implementation-owned, but
the provider outputs must be deterministic and plan-inspectable.

## 9. Path Classification Table

The first implementation uses a conservative repository path classification
table. More-specific rules win before broader rules, except unknown always wins
when no rule matches.

| Path shape                                                                                                                                                                                                                                                                    | Category                        | Scope result                                                                                                                                                                                                                             |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/**`, `src/lab/**`, `tests/**` files owned by one discovered subject                                                                                                                                                                                                      | project-scoped                  | Direct subject plus safe downstream dependents, descriptor and release-shaped artifact/receipt obligations when descriptor-backed, applicable ecosystem gates                                                                            |
| Ecosystem workspace files such as root workspace metadata, lock files, package-manager configuration, or language tool configuration                                                                                                                                          | ecosystem-scoped                | All active subjects in the affected ecosystem, descriptor-backed descriptors in that ecosystem, and release-shaped artifact/receipt obligations when descriptor-backed                                                                   |
| Root monorepo tool configuration affecting multiple ecosystems, global repository build settings, or cross-ecosystem validation configuration                                                                                                                                 | global                          | Scheduled-full-equivalent validation scope with global provenance                                                                                                                                                                        |
| Workflow-release planner, classifier, fact-provider, descriptor contract, workflow-release contract, authoring validation, target catalog behavior, workflow orchestration, build execution, publish execution, smoke validation, or descriptor schema documentation surfaces | workflow-release infrastructure | Affected tooling surface, related ecosystems and subjects; all discovered descriptors only when descriptor semantics, authoring validation, planning, contracts, build execution, publish execution, or smoke validation can be affected |
| Documentation or files explicitly known not to affect build, test, descriptors, workflow-release tooling, or ecosystem behavior                                                                                                                                               | known non-impacting             | Lightweight-only plan with applicable lightweight work groups                                                                                                                                                                            |
| Anything else                                                                                                                                                                                                                                                                 | unknown                         | Fail-closed plan                                                                                                                                                                                                                         |

Concrete glob spelling belongs to implementation, but every checked-in path must
fall into one of these semantic rule families. Any path not classified by the
table is unknown and fails closed.

Known non-impacting is a plan-level outcome, not a per-row shortcut. A run may
use the lightweight-only path only when every changed path resolves to known
non-impacting; if any changed path resolves to project, ecosystem, infrastructure,
global, or unknown, lightweight checks are only additive obligations for the
non-lightweight plan.

For mixed-impact changes, scope composition happens after all path rules have
produced impact records. If any impact is unknown or unclassifiable, the whole
plan fails closed. Otherwise, the planner unions selected subjects, descriptor
obligations, validation obligations, work groups, and evidence expectations from
all impacts. Broader scopes may subsume duplicate narrower obligations when the
plan records every retained and subsumed relationship in
`classification.subsumptions`. `broad-expansion-record` records why broader
scope was selected; it is not a subsumption ledger.

## 10. Scope Resolution Details

### 10.1 Project-Scoped

Project-scoped resolution:

1. map changed files to discovered validation subjects;
2. include directly mapped subjects;
3. compute downstream dependents from provider dependency facts;
4. when downstream facts are sufficient, include downstream subjects;
5. when downstream facts are unavailable or insufficient, fail closed;
6. add descriptor obligations for selected descriptor-backed subjects;
7. add release-shaped artifact and receipt obligations for selected
   descriptor-backed subjects;
8. add ecosystem gate obligations for selected subjects.

### 10.2 Ecosystem-Scoped

Ecosystem-scoped resolution selects:

- all active validation subjects in the ecosystem;
- descriptor validation for all descriptor-backed subjects in the ecosystem;
- release-shaped artifact and receipt validation for all descriptor-backed
  subjects in the ecosystem;
- all applicable ecosystem gates for that ecosystem.

### 10.3 Workflow-Release Infrastructure

Infrastructure resolution selects:

- workflow-release tooling-surface work groups for affected surfaces;
- related ecosystem and subject scopes when affected surfaces can influence them;
- all discovered release descriptors for descriptor semantics, authoring
  validation, planning, contracts, build execution, publish execution, or smoke
  validation impacts;
- no representative-smoke substitution for broader validation.

Tooling-surface expansion is deterministic:

| Tooling surface                   | Required validation scope                                                                                                                                                                                                 |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `planner`                         | Scheduled-full-equivalent scope plus workflow-release tooling validation                                                                                                                                                  |
| `classifier`                      | Scheduled-full-equivalent scope plus workflow-release tooling validation                                                                                                                                                  |
| `fact-provider`                   | All active subjects in ecosystems whose provider can be affected, all descriptors discovered through those subjects, all discovered release descriptors, all descriptor-validation work groups, and tooling validation    |
| `descriptor-contract`             | All discovered descriptors, descriptor-validation work groups, release-shaped artifact obligations derived from those descriptors, and tooling validation                                                                 |
| `workflow-release-contract`       | Scheduled-full-equivalent scope, all discovered release descriptors when validation plan/evidence/receipt/aggregate contracts can affect them, and workflow-release tooling validation                                    |
| `authoring-validation`            | All discovered release descriptors, all descriptor-validation work groups, descriptor authoring validation tooling, and workflow-release tooling validation                                                               |
| `target-catalog`                  | All descriptor-backed subjects with release-shaped artifact obligations and tooling validation                                                                                                                            |
| `workflow-orchestration`          | Scheduled-full-equivalent scope plus workflow-release tooling validation                                                                                                                                                  |
| `build-execution`                 | All active subjects with build or release-shaped artifact capabilities, their descriptor-backed artifact obligations, all discovered release descriptors, all descriptor-validation work groups, and tooling validation   |
| `publish-execution`               | All descriptor-backed subjects with release-shaped artifact obligations, all discovered release descriptors, all descriptor-validation work groups, and tooling validation; publication remains out of scope              |
| `smoke-validation`                | Smoke-validation tooling work groups, all discovered release descriptors, all descriptor-validation work groups, smoke descriptors/subjects, and descriptor-backed subjects whose smoke receipt contracts can be affected |
| `descriptor-schema-documentation` | Descriptor schema documentation tooling/docs-surface validation and workflow-release tooling validation                                                                                                                   |

If a changed workflow-release infrastructure path cannot be mapped to one of the
closed tooling surfaces, or if the mapped surface cannot determine its affected
ecosystems or subjects deterministically, planning fails closed with
`infrastructure-surface-unclassified`.

Descriptor schema documentation changes expand to all discovered descriptors only
when classification also shows they affect descriptor semantics, descriptor
contracts, authoring validation, planning, build execution, publish execution, or
smoke validation. Documentation-only changes do not by themselves require
all-discovered descriptor validation.

### 10.4 Global and Scheduled Full

Global and scheduled full select the same scope:

- all active validation subjects in all ecosystems;
- all discovered release descriptors;
- all applicable ecosystem gates;
- release-shaped artifact and receipt validation for descriptor-backed projects;
- `workflow-release-tooling` work groups and evidence expectations for every
  closed tooling surface: `planner`, `classifier`, `fact-provider`,
  `descriptor-contract`, `workflow-release-contract`, `authoring-validation`,
  `target-catalog`, `workflow-orchestration`, `build-execution`,
  `publish-execution`, `smoke-validation`, and
  `descriptor-schema-documentation`.

Global and scheduled-full workflow-release tooling work groups use
`coverage-target.type: tooling-surface` with one of the closed IDs above. They
must be required, blocking, and represented in evidence expectations just like
changed workflow-release infrastructure tooling work groups; the scope source is
the global or scheduled-full selection rather than a changed infrastructure path.

The only difference is provenance: global is affected validation caused by a
changed path, while scheduled full is time-based full-repository validation.

## 11. Work Group Selectors

Work groups use this closed kind set:

- `lightweight-preflight`;
- `ecosystem-gate`;
- `descriptor-validation`;
- `release-shaped-artifact`;
- `workflow-release-tooling`;
- `evidence-aggregation`.

Each executable validation work group has:

```yaml
work-group-id: string
kind: lightweight-preflight | ecosystem-gate | descriptor-validation | release-shaped-artifact | workflow-release-tooling
coverage-target:
    type: subject | ecosystem | descriptor | tooling-surface | artifact-obligation | lightweight-policy
    id: string
ecosystem: dotnet | python | javascript | typescript | ruby | null
runner-family: windows | ubuntu
selector-variant: string | null
depends-on: [work-group-id]
expected-evidence:
    category: lightweight-preflight | ecosystem-gate | descriptor-validation | release-shaped-artifact | workflow-release-tooling
    planned-capabilities: [build | test | lint | format | type-check] | null
    detail-profile: string | null
    required: boolean
```

Executable work groups must have a non-null `runner-family` in the frozen plan.
Runner assignment is plan authority: execution must not select a runner by
rediscovering subject ecosystem, descriptor contents, or tooling policy after
planning. If the planner cannot determine a required executable work group's
runner family from the closed mapping in section 12, planning fails closed or the
plan is structurally invalid rather than emitting `runner-family: null`.
`ecosystem` is plan authority for ecosystem-specific execution mapping. It must
be `dotnet`, `python`, `javascript`, or `typescript` for `ecosystem-gate` work
groups and for `release-shaped-artifact` work groups, derived from the referenced
subject or artifact obligation subject. It must be the owning subject ecosystem
for `descriptor-validation` only when descriptor validation requires
ecosystem-specific evidence; otherwise it is `null`. It is `null` for
`lightweight-preflight` and ordinary `workflow-release-tooling` work groups; a
workflow-release-tooling work group that fans out to ecosystem runners must use
separate work groups whose coverage and selector variant identify the relevant
ecosystem-owned scope. `ecosystem` must be consistent with `runner-family` under
section 12; illegal enum values, required-but-null ecosystems, or ecosystem/runner
mismatches make the plan structurally invalid.
`selector-variant` is `null` for the ordinary one-work-group-per-kind/target
case. It is required and path-safe when multiple executable work groups share the
same `kind` and `coverage-target`; examples include runner-family splits or
other validation variants that must remain distinct while covering the same
logical target.

Each terminal aggregation work group has:

```yaml
work-group-id: string
kind: evidence-aggregation
coverage-target:
    type: aggregation
    id: string
runner-family: ubuntu
depends-on: [work-group-id]
aggregate-output: ci-validation-aggregate-summary
```

Selector rules:

- `work-group-id` is stable within the plan and derived from kind, coverage
  target, and `selector-variant`. It is an artifact-ref-bearing identifier and
  therefore must match the path-safe grammar `^[a-z0-9][a-z0-9._-]{0,127}$`. The
  planner must not embed raw coverage target IDs or selector variants that can
  contain `/`, path separators, percent-escaped path syntax, control characters,
  or filesystem traversal tokens. When a coverage target or selector variant is
  not already path-safe, the planner derives `work-group-id` from the work-group
  kind, a normalized readable prefix, and a lowercase SHA-256 digest of the full
  typed coverage target and selector variant; the digest preimage, not the
  readable prefix, preserves uniqueness.
  The work-group ID digest preimage is the RFC 8785 canonical JSON bytes of:

    ```json
    {
        "api-version": "three.ci.validation.work-group-id/v1alpha1",
        "kind": "release-shaped-artifact",
        "selector-variant": null,
        "coverage-target": {
            "type": "artifact-obligation",
            "id": "artifact-obligation-id"
        }
    }
    ```

    `kind`, `selector-variant`, `coverage-target.type`, and `coverage-target.id`
    are replaced by the frozen work-group values before hashing.

- Work groups are selectors, not command lines.
- Every `depends-on` entry must resolve to a `work-group-id` in the same frozen
  plan. The work-group dependency graph must be acyclic. Executable work groups
  must not depend on the terminal `evidence-aggregation` work group.
- `depends-on` is a logical selector dependency gate. Inter-batch dependencies are
  represented by execution-batch DAG edges, and dependencies between selectors in
  the same batch are represented by the batch's ordered selector list. A dependent
  selector may run category-specific validation only after every dependency that
  is in the same batch has already produced a selector result, and after every
  dependency in an upstream batch has an admitted batch evidence bundle
  downloaded by the artifact-ID/API singleton path under the expected physical
  artifact name/directory. The bundle must include downloader-observed
  `artifact-metadata.json` and contain a selector result for the dependency. This
  same-workflow dependency scheduling admission is still narrower than final
  aggregation, which remains the stronger authority for final bundle and result
  admissibility.
- Validation failures do not block independent selectors. A selector result with
  `outcome: blocking-failure` is still a produced result for dependency-gating
  purposes when the batch can continue and write its bundle; the final failure is
  expressed by aggregation. A selector is dependency-blocked only by an upstream
  batch control-plane failure, cancellation, missing bundle, or a missing or
  skipped dependency result that prevents the dependent validation from being
  authorized. When a batch can still write evidence, each dependency-blocked
  selector assigned to that batch must produce a skipped selector result with
  empty artifact refs and a `validation-work-skipped` diagnostic with
  `diagnostic-detail: dependency-blocked`.
- `materialize-execution-batches` may coalesce multiple executable selectors into
  one execution batch only when per-selector outcomes and evidence expectations
  remain separately reportable and aggregate-checkable from the batch evidence
  bundle. It must not coalesce selectors connected by a `depends-on` edge unless
  the batch manifest orders those selectors and requires the same
  dependency-blocked result semantics before the dependent selector starts.
- `materialize-execution-batches` emits one execution-batch manifest at the
  contract-owned ref
  `ci-validation/execution-batches/<run-id>/<run-attempt>/execution-batch-manifest.json`.
  It has:

    ```yaml
    common-envelope: inherited
    api-version: three.ci.validation.execution-batch-manifest/v1alpha1
    kind: ci-validation-execution-batch-manifest
    execution-job: string  # legacy logical direct-batch job identity component
    plan-id: string
    plan-digest: string
    budget:
        min-total-jobs: integer
        max-total-jobs: integer
        min-windows-jobs: integer
        max-windows-jobs: integer
        non-batch-control-plane-job-count: integer
        actual-total-jobs: integer
        actual-windows-jobs: integer
        max-validation-artifacts: integer
        actual-validation-artifacts: integer
        expected-input-non-bundle-validation-artifacts: integer
        expected-final-validation-artifacts: integer
        expected-non-bundle-validation-artifacts: integer
        pre-final-validation-artifacts: integer
        max-execution-batches: integer
        actual-execution-batches: integer
        aggregate-target-duration-seconds: integer
        aggregate-max-duration-seconds: integer
    batches:
        - batch-id: string
          runner-family: windows | ubuntu
          compatibility-profile:
              ecosystem: dotnet | python | javascript | typescript | ruby | null
              setup-profile: string
              setup-profile-digest: string
              execution-profile: string
              execution-profile-digest: string
              release-shaped-profile: string | null
              release-shaped-profile-digest: string | null
          depends-on-batches: [batch-id]
          ordered-selectors:
              - work-group-id: string
                selector-index: integer
                depends-on: [work-group-id]
                expected-evidence-id: string
                expected-evidence-slot: selector-evidence-slot
          expected-batch-evidence-bundle-ref: string
          batch-writer:
              identity-source: github-actions-job-context  # logical direct-batch compatibility
              expected-boundary: execution-batch
              expected-job-identity: string
              provenance-fields: [workflow, job, matrix]  # legacy logical identity preimage
    ```

    `batch-id` is stable within the run attempt, path-safe, and derived from the
    ordered selector set plus the compatibility profile. It must match
    `^[a-z0-9][a-z0-9._-]{0,127}$`; if readable components are not path-safe, the
    materializer uses a normalized prefix plus a lowercase SHA-256 digest of the
    typed batch identity. `depends-on-batches` entries resolve within the manifest
    and form an acyclic DAG. `ordered-selectors` contains every executable
    work-group selector assigned to the batch exactly once, in the order the batch
    must evaluate them. The union of all `ordered-selectors.work-group-id` values
    across all batches must equal the executable work groups in the verified plan,
    excluding the terminal `evidence-aggregation` work group; no selected
    obligation may disappear, duplicate, or be downgraded by batching.
    Each ordered selector entry must equality-check against the frozen plan for
    `work-group-id`, the work group's exact `depends-on` list, and the matched
    evidence expectation identified by `expected-evidence-id`. The materializer
    must not drop, rewrite, or invent dependency or evidence bindings while
    preserving only the selector union. Every selector dependency edge from the
    frozen plan must be covered either by an earlier selector in the same batch's
    `ordered-selectors` order or by the producer batch listed in
    `depends-on-batches`; missing, extra, or stale batch DAG edges make the
    manifest structurally invalid.

    The `compatibility-profile` is the post-plan handoff placeholder for setup and
    execution compatibility. It records the runner family, ecosystem, setup,
    execution, and release-shaped compatibility dimensions needed to prove that
    coalesced selectors can share one batch. `setup-profile` and
    `execution-profile` are stable path-safe profile identifiers. Their matching
    `setup-profile-digest` and `execution-profile-digest` fields are lowercase
    SHA-256 digests of the RFC 8785 canonical preimages that include the frozen
    platform, setup, executor, and toolchain requirements not otherwise exposed as
    manifest enum fields. The batch bundle must copy these identifiers and
    digests exactly; aggregation does not admit a bundle whose compatibility
    profile differs from the manifest.
    Any batch containing a `release-shaped-artifact` selector must set a non-null,
    stable, path-safe `release-shaped-profile` and `release-shaped-profile-digest`
    derived from the frozen artifact and release-receipt obligations assigned to
    that batch. Its digest preimage and equality checks include the frozen
    release-shaped platform, workflow-release executor/toolchain, no-publish
    posture, and artifact-family requirements, in addition to the obligation
    identifiers and shape data. Every release-shaped selector in the batch must
    share that exact proven profile. If the materializer cannot prove shared
    release-shaped compatibility, it must split the selectors into safe batches or
    fail post-plan materialization without authorizing executable validation.
    Batches with no release-shaped selectors must set both release-shaped profile
    fields to `null`. The compatibility profile does not define release-shaped
    command lines; those details remain owned by release-shaped execution while
    preserving the manifest-bound digest equality checks.

    `expected-evidence-slot` is a pre-execution expectation slot, not an execution
    result. It may identify the logical work group, evidence expectation,
    category, planned capabilities, detail profile, coverage target, and the
    placeholder shape that the later batch bundle must fill. It must not contain
    outcome, success/failure state, diagnostics, observed artifact refs, observed
    digests, command output, or any other execution-produced data. Section 14 defines the
    detailed batch output result schema.

    `expected-batch-evidence-bundle-ref` is the single validation-only bundle ref
    expected from that execution batch:
    `ci-validation/bundles/<run-id>/<run-attempt>/<batch-id>/batch-evidence-bundle.json`.
    The bundle must contain separately addressable result rows for every ordered
    selector and evidence expectation assigned to the batch. A batch bundle may
    contain batch-level metadata and diagnostics, but it cannot collapse logical
    work-group outcomes into only a batch-level pass/fail result.

    `batch-writer` records logical validation-routing expectations for the
    expected bundle, but it is not immutable producer-identity proof for live G5
    bundle admission. `execution-job`, `identity-source:
    github-actions-job-context`, and `provenance-fields: [workflow, job,
    matrix]` are legacy/direct-batch compatibility fields that define the
    logical writer identity preimage for manifests and matrix rows; they do not
    assert that each batch has a separate physical GitHub Actions job. Payload
    fields, logs, command-authored JSON, artifact path segments, and
    caller-generated sidecars are not trusted writer identity sources. Live
    aggregation admits a bundle through the execution-batch manifest, current-run
    artifact API metadata, downloaded artifact metadata, payload validation, and
    run/run-attempt/batch binding. Immutable workflow/job producer proof is
    scoped to non-bundle control/final artifacts or to a future genuinely trusted
    observer seam. There is no separate writer-integrity or writer-observation
    artifact in the current design.

    When bounded runner-family orchestrators implement the batch DAG, bundle
    writer evidence distinguishes the logical batch identity (`batch-id`,
    `runner-family`, and `expected-batch-evidence-bundle-ref`) from the observed
    physical orchestrator job and slot identity. Physical orchestrator evidence
    appears only in the batch evidence bundle writer fields: `identity-source:
    github-actions-orchestrator-job-context`, `observed-job:
    execution-batch-<runner-family>-orchestrator`, `observed-matrix: {}`,
    `logical-batch-identity`, and `observed-orchestrator-slot-index`. It must
    not claim that the physical job is the retired per-batch `execution-job`
    abstraction. For `identity-source:
    github-actions-orchestrator-job-context`,
    `observed-orchestrator-slot-index` is required and must be a non-empty
    string. `null` is valid only for legacy/direct
    `github-actions-job-context` writers.

    `budget.actual-execution-batches` must equal `batches.length`. The
    materializer must map each batch to exactly one budget-counted batch evidence
    bundle, identified by the batch writer's expected logical job identity.
    Runner-family orchestrator jobs may implement multiple batches through
    bounded slots, but they must not hide additional budget-relevant public
    artifacts outside the manifest's artifact counts. The materializer must
    compute
    `pre-final-validation-artifacts` from expected input non-bundle validation
    artifacts plus expected batch evidence bundles, and compute
    `actual-validation-artifacts` from expected non-bundle validation artifacts
    plus expected batch evidence bundles. `expected-final-validation-artifacts` is
    currently `2` for the aggregate evidence manifest and aggregate summary.
    `expected-non-bundle-validation-artifacts` is the sum of expected input
    non-bundle artifacts and expected final validation artifacts.

    The artifact-derived execution-batch allowance is
    `20 - expected-non-bundle-validation-artifacts`. Logical batch count is
    tracked by `actual-execution-batches` and `max-execution-batches`, not by
    physical job totals. `budget.actual-total-jobs` counts non-batch
    control-plane jobs plus active runner-family orchestrator jobs, and
    `budget.actual-windows-jobs` counts Windows control-plane jobs plus the
    active Windows runner-family orchestrator job when Windows batches exist.
    `budget.max-execution-batches` must be no greater than the artifact-derived
    allowance for the manifest.

    Declared budget fields cannot relax the fixed LLD caps. Max caps apply to all
    manifests where applicable: `max-total-jobs` and `actual-total-jobs` must be at
    most 18, `max-windows-jobs` and `actual-windows-jobs` must be at most 8,
    `max-validation-artifacts` and `actual-validation-artifacts` must be at most
    20, `pre-final-validation-artifacts` must be at most
    `20 - expected-final-validation-artifacts`, and
    `aggregate-max-duration-seconds` must be at most 120. Lower-bound
    topology targets are informational under runner-family orchestrators:
    manifests must keep `min-total-jobs` and `min-windows-jobs` aligned with the
    physical orchestrator topology, and actual counts must satisfy those lower
    bounds. Lower bounds are waived for fail-closed, no-executable, all
    lightweight-only manifests including executable lightweight selectors/checks,
    and zero-work materializations. Fail-closed, no-executable, and zero-work
    materializations use a zero-execution budget profile with `batches: []`; they
    preserve fail-closed or no-work aggregation semantics and are not invalid
    merely because actual total or Windows jobs are below the broad/full/global
    lower-bound targets. Lightweight-only manifests with executable
    selectors/checks still declare and observe the corresponding counts, and
    those counts must satisfy the applicable maximum caps.

    Actual counts must match the declared manifest fields and stay within the
    applicable declared ranges and `max-validation-artifacts`.
    `aggregate-target-duration-seconds` and `aggregate-max-duration-seconds` use
    seconds; the target must be less than or equal to the max. A manifest whose
    declared caps, actual counts, or durations are inconsistent, exceed fixed LLD
    caps, overflow declared budgets, or cannot be validated by aggregation is a
    post-plan materialization/control-plane failure, not a planner policy
    fail-closed outcome. The zero-execution profile must not be used to downgrade
    or drop selected executable obligations. If selected executable obligations
    exist and cannot be coalesced into compatible batches within the applicable
    budgets without dropping required evidence, downgrading obligations, or
    violating dependencies, materialization fails post-plan and must not authorize
    executable validation.

    `materialize-execution-batches` must perform the same authoritative plan
    validation needed to safely fan out executable batches before emitting the
    manifest: plan ref and instance count, producer authority, envelope, schema,
    digest, structural reference rules, executable/fail-closed invariants, and
    required companion changed-files and fact snapshot artifacts. If plan identity
    or companion snapshot validation fails, it emits no executable batches and
    aggregation treats the run as `invalid-plan`. A structurally valid fail-closed
    plan, or a valid executable plan with no executable work groups, emits exactly
    one empty execution-batch manifest with the verified `plan-id`, `plan-digest`,
    budget fields, and `batches: []`; this preserves fail-closed or no-work
    semantics instead of authorizing validation from an invalid or absent
    manifest.

    Aggregation must verify the execution-batch manifest's producer authority from
    platform/control-plane metadata before trusting its payload because the
    manifest authorizes batch execution and bundle writers. A manifest authored
    outside `materialize-execution-batches`, by an executable validation command,
    or by an unverified artifact instance is not authority even if its payload
    matches. A missing, duplicate, unreadable, malformed, schema-invalid,
    plan-mismatched, dependency-mismatched, evidence-mismatched,
    producer-unverified, structurally invalid, budget-overflow, or
    unmaterializable-obligation manifest makes aggregation fail closed with
    `required-input-artifact-failure`; no batch evidence bundle is admissible
    under an invalid manifest.
    Aggregation must also recompute or verify the manifest's current-run budget
    totals before admitting any batch bundle: batch count equals `batches.length`,
    pre-final artifact count equals declared `pre-final-validation-artifacts`,
    enough final artifact slots remain for `expected-final-validation-artifacts`,
    total and Windows job counts equal their declared actual fields for the selected
    orchestrator topology, each execution batch maps to one budget-counted batch
    evidence bundle, the
    aggregate duration budget fields use seconds with target less than or equal to
    max and max no greater than 120 seconds, max caps never exceed 18 total jobs,
    8 Windows jobs, or 20 validation artifacts, and lower bounds match the
    selected physical runner-family orchestrator topology. Logical batch volume
    remains represented by `actual-execution-batches` and
    `max-execution-batches`; maximum caps still apply wherever the corresponding
    topology counts exist. It must also verify `budget.max-execution-batches`
    against the artifact-derived allowance. Any mismatch, hidden budget-relevant
    job, relaxed cap, invalid lower-bound use, or overflow is a post-plan
    control-plane/materialization failure reported through the invalid
    execution-batch manifest input path. Aggregation records the applicable
    `inadmissible-batch-evidence` diagnostic detail on the manifest input and
    sets the terminal `required-input-artifact-failure` reason/failure instead
    of registering execution-batch topology or budget details under
    `invalid-plan`. After publishing final aggregate artifacts, aggregation
    verifies that the observed final artifact count makes the complete
    `actual-validation-artifacts` equal the declared total and remain within the
    20-artifact cap.
    The `aggregate-evidence` boundary must measure its actual aggregate duration
    in seconds and record it as execution-produced final evidence, not
    pre-execution manifest data. Observed aggregate duration is telemetry for
    the performance target; exceeding `aggregate-max-duration-seconds` does not
    set a reason, emit diagnostics or failure kinds, or fail the final required
    check.

- Category-specific validation commands may produce provisional result material
  inside an execution batch, but they must not have authority to write
  contract-owned execution-batch manifests, batch evidence bundles, final
  manifests, or aggregates. The execution-batch control-plane boundary evaluates
  command outcomes against the ordered selector contract and writes the single
  batch evidence bundle with per-selector results and validation-grade writer
  provenance.

- One `ecosystem-gate` selector covers the complete planned capability set for
  its coverage target. The work group, matching evidence expectation, and
  selector result in the batch evidence bundle record that set so build, test,
  lint, format, and type-check outcomes do not collapse into an opaque gate
  result.
  `expected-evidence.planned-capabilities` is derived from the frozen selected
  subject capabilities using the section 8 provider-fact rules. It must be
  non-empty for executable ecosystem gates, sorted in canonical capability order
  `build`, `test`, `lint`, `format`, `type-check`, and identical to the matching
  `evidence-expectation.planned-capabilities`. A subject-scoped gate uses the
  selected active subject's enabled capabilities. An ecosystem-scoped gate uses
  the union of enabled capabilities across all selected active subjects in that
  ecosystem. A selected descriptor-backed subject with no enabled validation
  capabilities does not create an executable ecosystem gate by itself because
  descriptor/release-shaped obligations carry their own evidence. A selected
  validation-only subject with no enabled validation capabilities is invalid under
  section 7 and must fail closed instead of contributing no evidence. Missing
  provider capability facts for any selected subject in the gate scope make the
  plan fail closed rather than silently omitting that capability from the gate.
- `coverage-target.type: ecosystem` is valid only for ecosystem-level
  `ecosystem-gate` selectors. If ecosystem gates are decomposed into subject
  selectors, the plan must preserve the ecosystem parent through source impacts
  or scheduled-full provenance.
- Fail-closed plans contain no executable validation work groups and exactly one
  terminal `evidence-aggregation` work group needed to emit the failed aggregate
  verdict. The fail-closed aggregation work group has no executable evidence
  expectation and an empty `depends-on` list.
- Lightweight-only plans may contain no executable work groups, or may contain
  lightweight-preflight work groups that must produce evidence before the run can
  pass.
- `lightweight-preflight` work groups for known-non-impacting changes use
  `coverage-target.type: lightweight-policy` and
  `coverage-target.id: known-non-impacting` by default. They may instead use
  `coverage-target.type: tooling-surface` when the changed known-non-impacting file
  naturally belongs to a workflow-release tooling surface whose lightweight policy
  is being checked. A lightweight-only plan must not use `subject`, `ecosystem`, or
  `descriptor` coverage targets because those would imply selected validation
  subjects without the normal subject-selection, capability, and obligation
  contracts.
- The `evidence-aggregation` work group is a non-executable terminal
  control-plane selector. Its completion boundary includes every planned
  execution batch that can emit into the closed batch evidence boundary. All such
  batches and their assigned logical work groups are verdict-relevant under this
  design. Aggregation reads the plan, execution-batch manifest, and batch evidence
  bundles, emits the aggregate evidence manifest and aggregate summary, and does
  not produce executable validation evidence.
- The terminal `evidence-aggregation` work group must be downstream of every
  executable work group, either by direct `depends-on` references or by the
  transitive dependency graph. A fail-closed plan has no executable work groups,
  so its terminal aggregation work group is terminal with empty `depends-on`. A
  plan whose dependencies do not make aggregation terminal is structurally
  invalid.

## 12. Execution Mapping

The planner maps logical work groups to runner families; it does not create
concrete GitHub Actions jobs, matrix rows, batch IDs, or bundle refs.
`materialize-execution-batches` is the first boundary that turns the frozen plan
into physical execution. Its manifest maps each `execution-batch` to exactly one
budget-counted batch evidence bundle, and each executable work-group selector
appears in exactly one batch `ordered-selectors` list. Concrete GitHub Actions
execution is grouped into bounded runner-family orchestrator jobs.

| Work group kind                                               | Default runner family                                                                                   | Notes                                                                |
| ------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| `lightweight-preflight`                                       | Ubuntu                                                                                                  | May run documentation, formatting, or policy checks when lightweight |
| `ecosystem-gate` for .NET                                     | Windows                                                                                                 | Preserves .NET runner expectation                                    |
| `ecosystem-gate` for Python                                   | Ubuntu                                                                                                  | Uses repository tool provisioning convention                         |
| `ecosystem-gate` for JavaScript/TypeScript                    | Ubuntu                                                                                                  | Uses repository tool provisioning convention                         |
| `descriptor-validation`                                       | Ubuntu, or the subject ecosystem runner when descriptor validation requires ecosystem-specific evidence | Must not publish or mutate release state                             |
| `release-shaped-artifact` for .NET                            | Windows                                                                                                 | Emits validation-only batch evidence rows                            |
| `release-shaped-artifact` for Python or JavaScript/TypeScript | Ubuntu                                                                                                  | Emits validation-only batch evidence rows                            |
| `workflow-release-tooling`                                    | Ubuntu, or Windows when the affected tooling surface requires Windows-only evidence                     | Uses separate selectors only when scope requires separate evidence   |
| `evidence-aggregation`                                        | Ubuntu                                                                                                  | Terminal control-plane aggregation; emits final aggregate artifacts  |

The planner applies this table before freezing work groups and records the
result in each executable selector. "Requires ecosystem runner" means the
descriptor validation must run with the same runner family as the selected
subject or artifact-producing ecosystem; mixed requirements are represented by
separate work groups with distinct non-null `selector-variant` values. "Requires
Windows-only evidence" means the affected tooling surface validates
Windows-specific .NET/build behavior. All other workflow-release-tooling work
groups use Ubuntu. All runners provision tools through `mise` where practical.
The concrete command lines and helper scripts are implementation-owned, but they
must run the repository's existing ecosystem gates for selected scopes.

The execution-batch orchestrator input row emitted for each manifest batch has this contract:

```yaml
batch-id: string
runner-family: windows | ubuntu
expected-batch-evidence-bundle-ref: string
identity-matrix:
    batch-id: string
    runner-family: windows | ubuntu
    expected-batch-evidence-bundle-ref: string
expected-job-identity: string
```

`identity-matrix` must exactly equal the three-field writer-ID hash payload. The
row may carry additional execution-only matrix dimensions in a future workflow,
but they are not part of the writer identity unless the contract explicitly adds
them to `identity-matrix`.

An execution-batch orchestrator slot is a validation control-plane boundary, not
a raw command line and not one job per work group. Before running
category-specific validation, the slot must consume the frozen validation plan
and its selected execution-batch manifest entry and verify at least:

- the manifest's `plan-id` and `plan-digest` match the frozen plan;
- the current GitHub Actions orchestrator job, slot index, and selected logical
  batch identity match the bundle writer evidence for this `batch-id`;
- the batch runner family, platform, ecosystem, setup, execution profile,
  toolchain assumptions, and release-shaped profile are compatible with the
  current job context;
- every `ordered-selectors` entry resolves to the frozen work group and expected
  evidence slot by exact identifier and dependency list;
- every selector dependency is covered either by an earlier selector in this
  batch or by a declared upstream batch whose bundle/result is available for
  dependency gating;
- budget invariants that can be checked from the orchestrator context and
  manifest remain consistent, including the one-batch-to-one-budget-counted
  evidence-bundle mapping; and
- no selected executable obligation assigned to the batch is missing, duplicated,
  rewritten, downgraded, or replaced by a selector discovered during execution.

The job executes `ordered-selectors` in manifest order. Category-specific
commands may run inside that sequence, but they cannot reclassify changes,
rediscover selected subjects, alter dependencies, or write contract-owned
execution-batch manifests, final manifests, or aggregate verdicts. They may
produce command-local material for the batch boundary to evaluate. The
execution-batch boundary writes exactly one validation-only batch evidence bundle
for each executable batch that reaches evidence writing. That bundle contains
separate per-selector evidence/result rows for every assigned selector and
expected evidence slot, preserving distinct outcomes even when selectors share a
runner, workspace, setup, or release-shaped build invocation.

Dependency gating is result-presence based, not success-only. A valid upstream
selector row with `outcome: blocking-failure` is a produced dependency result and
does not by itself dependency-block downstream selectors; aggregation later fails
the final verdict from that blocking validation result. A selector is
dependency-blocked only when a required dependency result is skipped, missing, or
unavailable, or when an upstream batch/control-plane/bundle failure prevents the
dependency result from being admitted for gating. When the batch can still write
evidence, the dependency-blocked selector emits a skipped per-selector row with
empty artifact refs and a `validation-work-skipped` diagnostic whose
`diagnostic-detail` is `dependency-blocked`.

Release-shaped selectors remain selected obligations. Execution must not drop,
downgrade, or replace them with ordinary ecosystem gates because they are
coalesced into a batch. Where applicable, release-shaped validation reuses the
workflow-release build executor/tooling path in validation-only/no-publish mode
for build and test behavior. CI evidence from that path is never release
immutable proof and must not be accepted as publication evidence. CI validation
must not configure or use publish credentials, release approvals, OIDC publish
permission, registry mutation, GitHub Release mutation, release tag mutation, or
release-environment side effects.

Every batch containing a release-shaped selector must carry a non-null, stable,
path-safe `compatibility-profile.release-shaped-profile` derived from the frozen
artifact and logical release-shaped receipt obligations assigned to the batch.
Release-shaped selectors may share one execution batch only when their frozen
profile, runner family, platform, ecosystem, setup, execution profile,
workflow-release executor/toolchain requirements, no-publish posture, and artifact
family are compatible. Those dimensions are not advisory: they are bound into the
manifest by the `setup-profile`, `execution-profile`, and
`release-shaped-profile` digest preimages and equality checks before selectors
may coalesce. If compatibility cannot be proven, `materialize-execution-batches`
must split the selectors into separate safe batches or fail post-plan
materialization within the fixed job/artifact budgets. Even when multiple
release-shaped selectors reuse one build executor invocation, the batch bundle
must retain one distinct selector result row per frozen artifact obligation and
evidence expectation.

Aggregation is mapped as the terminal control-plane job after all planned
execution batches are complete, skipped by workflow construction, or otherwise
known missing. It reads the frozen plan, required companion planning snapshots,
the execution-batch manifest, and validation batch evidence bundles, emits the
aggregate evidence manifest plus `ci-validation-aggregate-summary` aggregate
summary, and
does not emit normal executable validation evidence.

Aggregation uses always-run failure-reporting semantics after the planning and
execution-batch materialization attempts. If planning emits no readable plan,
emits an invalid plan, or execution-batch materialization fails before producing a
reliable executable batch set, aggregation emits an `invalid-plan` aggregate with
zero executable batches rather than allowing the workflow to end without an
aggregate evidence manifest and aggregate summary.

## 13. Release-Shaped Artifact Validation

For descriptor-backed subjects, release-shaped validation derives selected
artifact obligations from release descriptors and the existing workflow-release
artifact model. Those obligations remain required validation obligations in CI;
batching can change only their physical execution grouping, not their selected
scope, evidence expectations, or verdict relevance.

Artifact obligations are plan-level records in the top-level
`artifact-obligations` section. Release-shaped validation selectors consume those
frozen obligations by `artifact-obligation-id`, and execution-batch materialization
assigns each selector to exactly one compatible batch.

The `release-receipt` block describes the logical release-shaped receipt
expectation that is validated alongside the artifact shape. It is the receipt
shape being checked, not a standalone CI receipt artifact and not release
immutable proof. The minimum result shape below is authoritative as the per-selector
`category-result.detail` content inside the manifest-assigned batch evidence
bundle.

Rules:

- The obligation set is the union required by all declared profiles.
- `profile-coverage` values are descriptor-declared profile identifiers; current
  descriptors use `buddy` and `official`, but the field is not a closed enum.
- No publish nodes, target remote state, overwrite policy, release tags, GitHub
  Release operations, registry mutation, release approvals, OIDC publish
  permission, release environment side effects, or publish credentials are
  planned or available to CI validation.
- If a descriptor is invalid enough to prevent derivation, planning fails closed.
- If descriptor-validation work is executable and fails, the corresponding
  descriptor-validation selector records a blocking validation failure.
- If a shape cannot be confirmed without release-only credentials or side
  effects, the selector records a blocking validation failure.
- Release-shaped CI evidence is validation-only and inadmissible as immutable
  release proof.
- Where applicable, release-shaped execution uses the existing workflow-release
  build executor/tooling path in validation-only/no-publish mode for build and
  test behavior, instead of a simplified CI-only artifact path.
- Every release-shaped batch has a non-null, stable, path-safe
  `release-shaped-profile` derived from the frozen artifact and logical
  release-shaped receipt obligations in that batch. Its digest preimage and
  equality checks include the frozen platform, workflow-release executor/toolchain,
  no-publish posture, and artifact-family requirements. Selectors may share a
  batch only when that profile and their runner, platform, ecosystem, setup,
  execution, toolchain, no-publish, and artifact-family requirements are
  compatible.

A `release-shaped-artifact` per-selector batch evidence row uses
`category-result.detail` with this minimum shape:

```yaml
artifact-obligation-results:
    - artifact-obligation-id: string
      descriptor:
          path: string
          identity: string | null
      profile-coverage: [string]
      artifact:
          planned:
              kind-family: string
              concrete-kind: string
              logical-artifact-role: string
              variant-dimensions: object
              expected-artifact-refs: [string]
          observed:
              refs: [string]
              digests:
                  - artifact-ref: string
                    algorithm: sha256
                    digest: string
                    digest-available: boolean
                    diagnostics: [diagnostic-record]
          outcome: success | blocking-failure | skipped
          diagnostics: [diagnostic-record]
      release-receipt:
          planned:
              expected-family: string
              logical-receipt-role: string
              variant-dimensions: object
          expected: boolean
          schema-checked: boolean
          outcome: success | blocking-failure | skipped
          diagnostics: [diagnostic-record]
      outcome: success | blocking-failure | skipped
      diagnostics: [diagnostic-record]
```

The single planned `artifact-obligation-id` bound to the release-shaped
selector must appear exactly once in that selector row, and no other artifact
obligation may appear in that row. `profile-coverage` is copied from the
frozen obligation and artifact `planned` plus release-receipt `planned` fields
are copied from the frozen artifact obligation and equality-checked by
aggregation. The planned `expected-artifact-refs` set is the complete required
artifact coverage for the obligation and must be non-empty. The
`observed.refs` set must equal both the refs checked by the selector and the
frozen `expected-artifact-refs`; empty, partial, or extra refs are a blocking
release-shaped artifact validation failure. `observed.digests` must contain
exactly one entry per expected ref with `algorithm: sha256`, matching
`artifact-ref`, and a lowercase hexadecimal SHA-256 `digest` when
`digest-available: true`. A missing, duplicate, mismatched, non-SHA-256, or
unavailable digest is a blocking release-shaped artifact validation failure
with `artifact-shape-unconfirmed`; unavailable digest entries must set
`digest-available: false`, use an empty digest string, and carry a diagnostic
explaining why the artifact bytes could not be content-bound without
publication credentials or side effects. A successful release-shaped artifact
result also requires `release-receipt.expected` and
`release-receipt.schema-checked` to be `true`, and
`release-receipt.outcome` to be `success`; unchecked or unexpected logical
release-shaped receipt outputs are blocking failures, not successful
validation evidence.

CI validation must not synthesize successful release-shaped artifact evidence
from descriptor or obligation metadata alone. Public CI batch evidence has no
reused-receipt authority: a `reused-validation-receipt` value is never an
independent basis for successful release-shaped artifact evidence, even if the
receipt is schema-checked or byte-bound. Release-shaped public source proof is
admitted only from no-publish validation output, represented by
`evidence-source: no-publish-validation`, where the no-publish release build
output binds observed SHA-256 digests to produced artifact bytes. If no real
no-publish release build output can bind the observed SHA-256 digests to produced
artifact bytes, the selector emits a blocking `artifact-shape-unconfirmed` result
instead of a validation-only success.

When multiple compatible release-shaped selectors share one execution batch,
each selector still emits its own result row with its own
`artifact-obligation-id`, planned fields, observed refs/digests, logical
release-shaped receipt check, outcome, and diagnostics. A shared executor or
staged artifact set cannot collapse those rows into one batch-level pass/fail
result.

When the manifest-assigned release-shaped selector is skipped solely because
an upstream dependency result is skipped, missing, or unavailable, or because
an upstream batch/control-plane/bundle failure prevents dependency gating, the
batch evidence bundle records an admissible dependency-blocked per-selector
row rather than artifact-shape validation evidence. An upstream
`blocking-failure` result that was successfully written is still a produced
dependency result and does not by itself dependency-block this selector. In
the dependency-blocked row, the selector `outcome`,
`category-result.outcome`, the single obligation result `outcome`,
`artifact.outcome`, and `release-receipt.outcome` must all be `skipped`;
selector `evidence.artifact-refs` remains `[]`; the obligation detail still
copies the planned artifact and release receipt shape and may carry skipped or
unavailable observed artifact refs/digests when needed to explain the frozen
artifact obligation that could not run. `release-receipt.expected`
remains copied from the frozen obligation but
`release-receipt.schema-checked` must be `false`; and diagnostics must include
`validation-work-skipped` with `diagnostic-detail: dependency-blocked`. This batch
evidence row never satisfies the release-shaped artifact obligation as success: aggregation records
`required-evidence-skipped` and fails the final verdict.

## 14. Evidence, Aggregation, and Artifact Budget

This section is the current authoritative G5/current evidence contract. The
validation evidence unit is the manifest-assigned batch evidence bundle. The
current schema has no standalone per-work-group receipt authority, no selector
assignment artifact, no writer-observation artifact, and no unbounded receipt
intake namespace. Legacy receipt-like files, if any appear during migration, are
non-authoritative unexpected artifacts and cannot satisfy an evidence expectation,
prove writer identity, authorize selector assignment, or serve as release proof.
Clean G5 has no selector-assignment, standalone-receipt, receipt-manifest, or
writer-observation compatibility contract.

### 14.1 Current Contract Artifact Namespace

The validation artifact namespace is closed in two phases so aggregation does not
need a final artifact to describe its own uploaded instance before that upload
exists.

Current G5 physical artifact names are attempt-visible:
`three-ci-validation-<run-id>-<run-attempt>-<sha256(logical-ref)>`. Enumeration
and reconciliation count only names with the current attempt prefix. Artifacts
from earlier attempts of the same run are outside the current namespace and do
not poison closure, while any unknown artifact with the current attempt prefix
still fails closed.

**Pre-final input namespace closure** happens before bundle admission. It covers
all input non-bundle refs with explicit expected cardinality, plus the bundle refs
from the execution-batch manifest:

| Artifact class           | Logical ref                                                                            | Producer boundary                 | Count                                                                                                                                            |
| ------------------------ | -------------------------------------------------------------------------------------- | --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| Planner-facing request   | `ci-validation/requests/<run-id>/<run-attempt>/ci-validation-request.json`             | `normalize-input`                 | exactly one for every run attempt                                                                                                                |
| Frozen validation plan   | `ci-validation/planning/<run-id>/<run-attempt>/validation-plan.json`                   | `plan`                            | exactly one when the request boundary is replayable and a plan is expected; otherwise zero only for explicit no-authoritative-plan request cases |
| Changed-files snapshot   | `ci-validation/planning/<run-id>/<run-attempt>/changed-files-snapshot.json`            | `plan`                            | exactly one when `changed-files-hash` is non-null; otherwise zero                                                                                |
| Fact snapshot            | `ci-validation/planning/<run-id>/<run-attempt>/fact-snapshot.json`                     | `plan`                            | exactly one when `fact-snapshot.status: available`; otherwise zero                                                                               |
| Execution-batch manifest | `ci-validation/execution-batches/<run-id>/<run-attempt>/execution-batch-manifest.json` | `materialize-execution-batches`   | exactly one for every authoritative plan with reliable materialization; zero when no authoritative plan or reliable batch set exists             |
| Batch evidence bundle    | `ci-validation/bundles/<run-id>/<run-attempt>/<batch-id>/batch-evidence-bundle.json`   | assigned execution-batch boundary | exactly one per executable execution batch and zero for empty manifests                                                                          |

Planner diagnostics are non-authoritative diagnostic artifacts, not aggregate
input evidence. Even when a planner diagnostics upload uses the
`three-ci-validation-*` physical-name prefix, live validation-only namespace
closure must not whitelist it; if observed in the prefixed contract namespace,
aggregation surfaces it as an unexpected contract artifact instead of excluding
it from budget and input accounting.

**Post-publication final artifact verification** happens after aggregation writes
its final artifacts:

| Artifact class              | Logical ref                                                                       | Producer boundary    | Count                                                                  |
| --------------------------- | --------------------------------------------------------------------------------- | -------------------- | ---------------------------------------------------------------------- |
| Aggregate evidence manifest | `ci-validation/aggregate/<run-id>/<run-attempt>/aggregate-evidence-manifest.json` | `aggregate-evidence` | exactly one aggregation-authored pre-final namespace closure manifest  |
| Aggregate summary           | `ci-validation/aggregate/<run-id>/<run-attempt>/aggregate-summary.json`           | `aggregate-evidence` | exactly one aggregation-authored verdict summary bound to the manifest |

The validation artifact cap is still at most 20 contract artifacts for the run
attempt. Before bundle admission, aggregation verifies the pre-final budget
without pretending final artifacts already exist:

```text
pre-final-validation-artifacts = expected-input-non-bundle-validation-artifacts + actual-execution-batches
pre-final-validation-artifacts <= 20 - expected-final-validation-artifacts
```

`expected-final-validation-artifacts` is currently `2`: the aggregate evidence
manifest and aggregate summary. The aggregate summary payload records expected and
reserved final counts only; authoritative observed final counts are external to
the payload and are verified after publication by artifact enumeration:

```text
expected-actual-validation-artifacts = pre-final-validation-artifacts + expected-final-validation-artifacts
expected-actual-validation-artifacts <= 20
post-publication observed final count must equal expected-final-validation-artifacts
post-publication observed total validation artifacts must equal expected-actual-validation-artifacts
```

One executable execution batch maps to one budget-counted batch evidence bundle.
The materializer must fail post-plan rather than emit a manifest whose executable
batch set cannot fit the artifact caps, including the two reserved final artifact
slots, without dropping selected obligations, downgrading required evidence, or
violating dependencies. A structurally valid fail-closed plan, a no-work plan,
or a plan with no executable validation work uses the zero-execution profile:
the manifest has `batches: []`, aggregation expects no batch evidence bundles,
and the terminal aggregate evidence records the failing or passing no-work
verdict.

Live namespace closure and final reconciliation use bounded prefixed-artifact
enumeration. The downloader/aggregator collects at most the relevant
`three-ci-validation-` prefixed cap plus one sentinel; once the sentinel is
observed, it records namespace overflow and fails closed instead of collecting or
materializing an unbounded unexpected-artifact list. Implementations must not use
an artifact-listing mode that forces full pagination before the overflow sentinel
can stop enumeration.

The aggregate evidence manifest is an aggregation-authored pre-final namespace
closure, not a receipt manifest and not a self-referential final-artifact record.
It records every input non-bundle ref with expected cardinality `0` or `1`, plus
every expected bundle slot from the execution-batch manifest and the observed
candidates at that expected bundle ref before writing final artifacts. A bundle
candidate is recorded in its expected slot even when it is missing, duplicate,
API/metadata-unverified, malformed, or otherwise inadmissible; `valid` admitted
bundle candidates require validation-grade artifact API metadata and manifest
binding, not immutable workflow/job producer proof.

```yaml
common-envelope: inherited
api-version: three.ci.validation.aggregate-evidence-manifest/v1alpha1
kind: ci-validation-aggregate-evidence-manifest
artifact-ref: string
plan-id: string | null
plan-digest: string | null
input-artifacts:
    request:
        artifact-ref: string | null
        artifact-instance-id: string | null
        content-digest: string | null
        required: true
        expected-cardinality: 1
        admissibility: valid | inadmissible | missing
        diagnostics: [diagnostic-record]
    validation-plan:
        artifact-ref: string | null
        artifact-instance-id: string | null
        content-digest: string | null
        required: boolean
        expected-cardinality: 0 | 1
        admissibility: valid | inadmissible | missing | not-required
        diagnostics: [diagnostic-record]
    changed-files-snapshot:
        artifact-ref: string | null
        artifact-instance-id: string | null
        content-digest: string | null
        required: boolean
        expected-cardinality: 0 | 1
        admissibility: valid | inadmissible | missing | not-required
        diagnostics: [diagnostic-record]
    fact-snapshot:
        artifact-ref: string | null
        artifact-instance-id: string | null
        content-digest: string | null
        required: boolean
        expected-cardinality: 0 | 1
        admissibility: valid | inadmissible | missing | not-required
        diagnostics: [diagnostic-record]
    execution-batch-manifest:
        artifact-ref: string | null
        artifact-instance-id: string | null
        content-digest: string | null
        required: boolean
        expected-cardinality: 0 | 1
        admissibility: valid | inadmissible | missing | not-required
        diagnostics: [diagnostic-record]
batch-bundles:
    - batch-id: string
      artifact-ref: string
      expected-cardinality: 1
      slot-admissibility: valid | inadmissible | missing | duplicate
      admitted-candidate-id: string | null
      observed-candidates:
          - candidate-id: string
            artifact-instance-id: string | null
            content-digest: string | null
            producer-verification: verified | producer-unverified | wrong-producer | not-checked
            payload-readable: boolean
            admissibility: valid | inadmissible
            diagnostics: [diagnostic-record]
      diagnostics: [diagnostic-record]
unexpected-contract-artifacts:
    - physical-artifact-name: string
      observed-physical-artifact-name: string | null # optional
      artifact-instance-id: string | null
      classification: unexpected | unreadable | wrong-ref | wrong-producer
      diagnostics: [diagnostic-record]
namespace-overflow:
    detected: boolean
    observed-prefixed-artifact-count-lower-bound: integer
    max-prefixed-validation-artifacts: integer
    diagnostics: [diagnostic-record]
projection-authority:
    mode: pull_request | push | scheduled_full
    validation-tree:
        commit-sha: string
        ref: string | null
    affected-range:
        status: available | unavailable | not-applicable
        base-sha: string | null
        base-tip-sha: string | null
        head-sha: string | null
        changed-files-hash: string | null
    request:
        artifact-ref: string | null
        request-digest: string | null
    scheduled-full:
        enabled: boolean
    projection-digest: string
pre-final-validation-artifacts: integer
namespace-closed-at: string
proof-admissibility: validation-only
```

Each `batch-bundles` entry represents an expected bundle slot, not only admitted
bundle evidence. `observed-candidates` records all observed artifact instances at
that expected bundle ref, including duplicate instances and candidates whose
current-run artifact metadata or payload binding cannot satisfy validation-grade
admission checks. An internally unverified, wrong-run, present
wrong-run-attempt, wrong-ref, duplicate, unreadable, malformed, or otherwise
inadmissible candidate remains associated with the expected bundle slot for
replay diagnostics; it must not be promoted to valid evidence or moved only to
`unexpected-contract-artifacts`. Missing GitHub per-artifact attempt metadata is
not by itself inadmissible when artifact ID/name/run, attempt-scoped namespace,
downloaded metadata, and payload validation all bind to trusted orchestrator or
aggregate state. In the G5 live topology, candidate
`producer-verification: verified` means the internal admission checks above
passed. It is not a trusted producer-identity claim for release proof.
`projection-authority` is the aggregate manifest's
canonical authorization preimage for the plan projection being admitted. Its
`projection-digest` is computed over the listed mode, validation tree, request,
affected-range, and scheduled-full bindings; validation rejects a supplied plan
projection when this authority is absent, contains snapshot binding keys, is
malformed, or contradicts the authoritative input rows.
The projection authority is not release proof and does not make CI evidence
release immutable proof. Future topologies may feed genuinely trusted non-payload
observations through the G4 seam and can then interpret producer verification
more strongly.
`unexpected-contract-artifacts` is reserved for prefixed artifacts that do not map
to an input non-bundle ref, final aggregate ref, or expected bundle ref.
`observed-physical-artifact-name` is optional downloader-observation metadata for
the raw GitHub Actions artifact name seen during enumeration/download when that
name is relevant to diagnostics or sorting; it is not a producer-attested sidecar
claim.
`classification` intentionally remains the coarse aggregate classification
(`unexpected`, `unreadable`, `wrong-ref`, or `wrong-producer`). Specific namespace
conditions such as duplicate, malformed, schema-invalid, wrong-run,
digest-mismatched, late, or overflow are represented in the closest
`diagnostics[].diagnostic-detail` on the unexpected artifact entry, expected slot,
namespace-overflow record, or final-artifact diagnostic as applicable.
`candidate-id` is derived as
`"candidate-" + lowercase_sha256(RFC8785({"run-id", "run-attempt", "batch-id",
"artifact-ref", "artifact-instance-id", "physical-artifact-name"}))`, using the
empty string for unavailable nullable fields. `observed-candidates` are sorted by
`candidate-id`. Unexpected artifact entries have an implicit deterministic ID
`"unexpected-" + lowercase_sha256(RFC8785({"run-id", "run-attempt",
"physical-artifact-name", "artifact-instance-id", "classification",
"observed-physical-artifact-name"}))` when `observed-physical-artifact-name` is
present and non-null, otherwise the observed-name component is omitted. Other
unavailable nullable fields use the empty string; the
`unexpected-contract-artifacts` array is sorted by that implicit ID before RFC
8785 serialization.

For the request entry, `required` is always `true`, `expected-cardinality` is
always `1`, and `admissibility: not-required` is not allowed. Missing, duplicate,
unreadable, malformed, wrong-run, digest-mismatched, or producer-unverified
request artifacts drive the existing request-invalid fail-closed or
no-authoritative-plan behavior. For the validation-plan entry,
`expected-cardinality` is `1` whenever the request boundary is replayable and a
plan is expected; a missing expected plan is `invalid-plan`, not a zero-cardinality
observation. The validation-plan cardinality is `0` only for explicit
no-authoritative-plan request cases where planning was not authorized to emit a
plan. For changed-files and fact snapshots, `expected-cardinality` is `1` only
when the frozen plan fields require the snapshot and `0` otherwise; an artifact
instance at a cardinality-`0` input ref is unexpected input evidence and cannot be
reclassified as a batch bundle. For the execution-batch-manifest entry,
`expected-cardinality` is `1` for every authoritative plan whose materialization
produces a reliable batch set, including valid fail-closed, no-work, and
zero-execution plans with `batches: []`. It is `0` only when no authoritative plan
exists or materialization fails before a reliable batch set exists; in that path no
bundle is admissible and aggregation follows the invalid-plan/no-bundle path.

Contract-owned aggregate evidence manifest and aggregate summary refs are final
artifact refs even when they are observed before a retry starts; aggregation must
not classify them as batch bundles or unexpected pre-final bundle-like evidence.
The aggregate evidence manifest must not contain its own artifact instance ID or
content digest and must not contain the aggregate summary artifact instance ID or
content digest. Aggregation verifies those two final artifacts only after upload:
it uploads the aggregate evidence manifest, verifies exactly one producer-verified
instance at that ref and computes its digest, then uploads the aggregate summary
that records `aggregate-evidence-manifest.content-digest`. Finally it verifies
exactly one producer-verified aggregate summary instance at its ref and recomputes
that summary digest for post-run acceptance. The summary cannot contain its own
content digest; consumers verify it from the artifact bytes and producer metadata.
If summary generation must preserve an existing aggregate evidence manifest whose
bytes differ from the recomputed validation view, the summary keeps that preserved
digest as non-authoritative, records `producer-verified: false`, and carries the
manifest authority diagnostics into both final evidence failures and
`final-artifacts.aggregate-evidence-manifest.authority-diagnostics`.

Aggregation may declare the pre-final namespace closed only after planning,
execution-batch materialization, and every executable execution-batch job that can
write a bundle have reached a terminal state. Same-attempt retries and post-run
acceptance re-enumerate the prefixed physical artifact namespace and require exact
equality with the manifest's closed pre-final artifact instance IDs, logical refs
where known, and content digests, plus the separately verified final aggregate
artifact instances. A late, missing, duplicate, wrong-producer, or mismatched
contract artifact makes final evidence non-authoritative and fails the final
required check.

All prefixed validation artifacts considered by aggregation are bounded by the
same 20-artifact validation cap. During pre-final namespace closure, aggregation
must enumerate at most `20 - expected-final-validation-artifacts` pre-final
artifacts plus one sentinel prefixed artifact before declaring overflow. If
pre-final enumeration exceeds that reserved-slot limit, or the artifact service
cannot prove that the final aggregate slots remain reserved, aggregation records a
bounded `namespace-overflow` diagnostic, fails namespace closure, and must not
keep collecting an unbounded artifact list. During complete post-publication
final reconciliation, the post-upload workflow gate enumerates at most the full
20-artifact cap plus one sentinel prefixed artifact before declaring overflow. If
final reconciliation observes more than 20 prefixed validation artifacts, or the
artifact service cannot prove that at most 20 prefixed artifacts exist for the run
attempt, the gate fails with a log-only diagnostic and must not create
aggregate-summary `namespace-overflow`, `reason`, `failures`, or public
diagnostic details. The
`unexpected-contract-artifacts` array is therefore bounded; overflow is
represented by `namespace-overflow` rather than by appending unbounded entries.

Aggregation must never upload a second artifact instance at an occupied final ref.
If a final aggregate summary exists without its aggregate evidence manifest, the
final state is non-authoritative and must not be repaired by recreating a manifest
for the existing summary.

### 14.2 Batch Evidence Bundle Schema

Each executable execution batch writes exactly one bundle with this minimum
shape:

```yaml
common-envelope: inherited
api-version: three.ci.validation.batch-evidence-bundle/v1alpha1
kind: ci-validation-batch-evidence-bundle
artifact-ref: string
bundle-id: string
plan-id: string
plan-digest: string
mode: pull_request | push | scheduled_full
validation-tree:
    commit-sha: string
    ref: string | null
affected-range:
    status: available | unavailable | not-applicable
    base-sha: string | null
    base-tip-sha: string | null
    head-sha: string | null
    changed-files-hash: string | null
scheduled-full:
    enabled: boolean
execution-batch-manifest:
    artifact-ref: string
    content-digest: string
batch:
    batch-id: string
    runner-family: windows | ubuntu
    compatibility-profile:
        ecosystem: dotnet | python | javascript | typescript | ruby | null
        setup-profile: string
        setup-profile-digest: string
        execution-profile: string
        execution-profile-digest: string
        release-shaped-profile: string | null
        release-shaped-profile-digest: string | null
    depends-on-batches: [batch-id]
writer:
    identity-source: github-actions-job-context | github-actions-orchestrator-job-context
    expected-boundary: execution-batch
    expected-job-identity: string
    observed-writer-identity: string
    observed-workflow: string
    observed-job: string  # direct logical job or physical orchestrator job
    observed-matrix: object | null  # logical batch matrix, or {} for orchestrator
    logical-batch-identity: object | null  # required for orchestrator writers
    observed-orchestrator-slot-index: string | null  # required non-empty string for orchestrator writers; null only for legacy/direct writers
execution-tree:
    observed-commit-sha: string | null
    source: execution-batch-boundary
    verified: boolean
started-at: string
completed-at: string
selector-results:
    - selector-result
batch-diagnostics: [diagnostic-record]
proof-admissibility: validation-only
```

`artifact-ref` must equal the manifest's `expected-batch-evidence-bundle-ref` for
that `batch-id`. `bundle-id` is stable within the run attempt and derived from the
run identity, `batch-id`, manifest digest, and bundle artifact ref; it is not a
writer-selected authority. `plan-id`, `plan-digest`, the manifest ref, the manifest
digest, `batch.batch-id`, `batch.runner-family`, `batch.depends-on-batches`, and
the complete compatibility profile, `mode`, `validation-tree`, `affected-range`,
and `scheduled-full` are equality-checked against the frozen plan and
execution-batch manifest before any selector row is admitted.

The compatibility profile fields prove that coalesced selectors actually ran
under the materialized platform, setup, executor/toolchain, no-publish posture,
and release-shaped artifact-family assumptions. `setup-profile-digest`,
`execution-profile-digest`, and `release-shaped-profile-digest` are lowercase
SHA-256 digests of the same canonical preimages used by the materializer. A
release-shaped selector requires non-null `release-shaped-profile` and
`release-shaped-profile-digest`; a non-release-shaped batch uses `null` for both.

`writer` records validation-grade writer provenance inside the bundle, but it does
not claim trusted producer identity. In G5 live CI, aggregation admits a batch
bundle only when final aggregate same-run artifact enumeration, API singleton
metadata, trusted downloader-observation metadata generated by
`download-ci-validation-observed-artifacts`, and bundle payload validation all
bind to the execution-batch manifest, current run, current run attempt, expected
batch ref, expected batch ID, dependencies, and plan/request/snapshot context.
Payload writer fields are insufficient by themselves, and producer-side writer or
batch observation sidecars are not required or consumed by the live topology.
When `observed-writer-identity` is present, validation recomputes it from
`observed-workflow`, `observed-job`, and `observed-matrix`; for orchestrator
writers, `observed-matrix` is `{}` and the logical batch matrix is carried
separately in `logical-batch-identity`.
Runner-family orchestrator slot dependency admission is intentionally
same-family only. The materializer and manifest validator fail closed with the
diagnostic that the current runner-family validation topology does not support
cross-family batch dependencies; affected work must be coalesced into one family
or await a future explicit cross-family mode. Within one family, the
orchestrator records uploaded batch artifact IDs in local job state, rechecks the
GitHub Actions artifact ID/API metadata before a dependent slot consumes the
bundle, and passes the resulting admission record directly to bundle validation.
There is no peer-family manifest wait, lookup, publication, payload-digest field,
or cross-family terminal manifest dependency path. Aggregate collection remains a
separate fan-in after both family jobs complete through workflow `needs`; it
admits the expected batch bundle artifacts from the execution-batch manifest and
fails closed on missing, duplicate, malformed, wrong-run, or wrong-attempt
artifacts rather than falling back to broad public namespace discovery. The
design does not use broad namespace enumeration or separate writer-integrity,
writer-observation, selector-assignment, batch observation, or peer-family state
manifest artifacts for dependency admission. Artifact admission assumes the
checked-in CI workflow and control-plane scripts are trusted reviewable code for
the validation tree. A malicious workflow or control-plane change is therefore a
reviewed code-change risk outside artifact admission's threat model, not
something payload admission can independently defeat.
Live namespace closure treats optional/not-required snapshot artifact names as
unexpected unless the plan proves those snapshots are required. Any future
trusted-observation input must come from a genuinely trusted non-payload
observer rather than a caller-written producer-side sidecar.

Each `selector-result` has:

```yaml
work-group-id: string
selector-index: integer
expected-evidence-id: string
expected-evidence-slot-digest: string
mode: pull_request | push | scheduled_full
validation-tree:
    commit-sha: string
    ref: string | null
affected-range:
    status: available | unavailable | not-applicable
    base-sha: string | null
    base-tip-sha: string | null
    head-sha: string | null
    changed-files-hash: string | null
scheduled-full:
    enabled: boolean
coverage-target:
    type: subject | ecosystem | descriptor | tooling-surface | artifact-obligation | lightweight-policy
    id: string
ecosystem: dotnet | python | javascript | typescript | ruby | null
runner-family: windows | ubuntu
selector-variant: string | null
depends-on: [work-group-id]
dependency-results:
    - work-group-id: string
      source-batch-id: string
      upstream-artifact-ref: string | null
      upstream-bundle-id: string | null
      upstream-artifact-instance-id: string | null
      upstream-admitted-candidate-id: string | null
      outcome: satisfied | missing | skipped | failed
      admitted-for-gating: boolean
outcome: success | blocking-failure | skipped
skip-reason: dependency-blocked | not-applicable | null
evidence:
    category: lightweight-preflight | ecosystem-gate | descriptor-validation | release-shaped-artifact | workflow-release-tooling
    planned-capabilities: [build | test | lint | format | type-check] | null
    capability-results: [capability-result] | absent
    category-result: category-result | absent
    artifact-refs: [string]
diagnostics: [diagnostic-record]
proof-admissibility: validation-only
```

`selector-index` and `expected-evidence-id` must match the manifest's
`ordered-selectors` entry. `expected-evidence-slot-digest` is the lowercase
SHA-256 digest of the RFC 8785 canonical JSON bytes of that manifest entry's
`expected-evidence-slot`; aggregation uses it to prove the result filled the
pre-execution slot rather than a rewritten execution-local expectation. Before
admitting a selector row, aggregation equality-checks all frozen selector identity
and evidence-shape fields against the manifest `ordered-selectors` entry, its
`expected-evidence-slot`, and the frozen plan work group/evidence expectation.
This includes `work-group-id`, `selector-index`, `expected-evidence-id`,
`coverage-target`, `ecosystem`, `runner-family`, `selector-variant`, `depends-on`,
`evidence.category`, `evidence.planned-capabilities`, the selected evidence union
branch, and any other frozen selector identity or evidence-shape field carried in
the expected slot. Each selector result's `mode`, `validation-tree`,
`affected-range`, and `scheduled-full` fields must exactly equal the batch-level
values and the frozen plan envelope. Aggregation rejects any mismatch as
`malformed-bundle`. Every ordered selector assigned to the batch must
have exactly one selector result row, rows must appear in manifest order, and no
extra selector row may appear.

Selector-level `outcome` remains the execution result enum `success |
blocking-failure | skipped`. Each `dependency-results[].outcome` instead uses
the dependency evidence enum `satisfied | missing | skipped | failed`.
`admitted-for-gating` records whether upstream dependency evidence was admitted
for gating, not whether that upstream selector succeeded: `satisfied` and
`failed` are admitted evidence, while `missing` and `skipped` are not. Trusted
upstream identity fields bind an admitted dependency result to the authoritative
upstream bundle evidence consumed through the execution-batch manifest.
`upstream-artifact-ref`, `upstream-bundle-id`,
`upstream-artifact-instance-id`, and `upstream-admitted-candidate-id` are
nullable or omittable only when no upstream artifact evidence applies to the
dependency result. When `upstream-artifact-ref` or `upstream-bundle-id` is
present, `upstream-artifact-instance-id` and
`upstream-admitted-candidate-id` are required non-empty strings, and all supplied
upstream identity values must match the authoritative upstream evidence admitted
for that dependency.

`capability-result` has:

```yaml
capability: build | test | lint | format | type-check
outcome: success | blocking-failure | skipped
diagnostics: [diagnostic-record]
```

`category-result` has:

```yaml
outcome: success | blocking-failure | skipped
diagnostics: [diagnostic-record]
detail: object | null
```

The `evidence` union is selected by `planned-capabilities`. Ecosystem gates with
non-null `planned-capabilities` include exactly one `capability-results` entry for
each planned capability and omit `category-result`. Descriptor, release-shaped,
lightweight, and workflow-release-tooling selectors use `planned-capabilities:
null`, omit `capability-results`, and include exactly one `category-result`.
`null`, empty-array, or empty-object placeholders for the omitted branch are
inadmissible.

`evidence.artifact-refs` is constrained by evidence category. It must be `[]` for
`lightweight-preflight`, `ecosystem-gate`, `descriptor-validation`, and
`workflow-release-tooling` rows; any non-empty value for those categories is
inadmissible with `malformed-bundle`. For `release-shaped-artifact`
rows, it must exactly equal the sole nested
`category-result.detail.artifact-obligation-results[0].artifact.observed.refs`
set for the selector row, including the explicit dependency-blocked skipped form
where both sets are `[]`. Missing, extra, reordered, or mismatched artifact refs
are inadmissible with `malformed-bundle`.

For capability evidence, the selector `outcome` is derived from the capability
rows: any `blocking-failure` makes the selector `blocking-failure`; otherwise any
`skipped` makes it `skipped`; all planned capabilities succeeding makes it
`success`. For category evidence, the selector `outcome` must match
`category-result.outcome` and the category-specific detail rules below. A mismatch
between row outcome, nested outcome, diagnostics, or skipped semantics makes the
row inadmissible with `malformed-bundle`.

### 14.3 Category Detail Rows

`lightweight-preflight` rows use `category-result.detail` with this minimum
shape:

```yaml
lightweight-preflight:
    work-group-id: string
    detail-profile: string
    coverage-target:
        type: lightweight-policy | subject | ecosystem | descriptor | tooling-surface
        id: string
    selector-variant: string | null
    runner-family: windows | ubuntu
    outcome: success | blocking-failure | skipped
    subcheck-results:
        - subcheck-id: string
          outcome: success | blocking-failure | skipped
          diagnostics: [diagnostic-record]
    diagnostics: [diagnostic-record]
```

`workflow-release-tooling` rows use `category-result.detail` with this minimum
shape:

```yaml
workflow-release-tooling:
    work-group-id: string
    detail-profile: string
    coverage-target:
        type: tooling-surface | subject | ecosystem | descriptor
        id: string
    ecosystem: dotnet | python | javascript | typescript | ruby | null
    selector-variant: string | null
    runner-family: windows | ubuntu
    outcome: success | blocking-failure | skipped
    subcheck-results:
        - subcheck-id: string
          outcome: success | blocking-failure | skipped
          diagnostics: [diagnostic-record]
    diagnostics: [diagnostic-record]
```

For required lightweight and workflow-release-tooling rows, aggregation
first equality-checks frozen identity fields in the detail payload against the
frozen work group, evidence expectation, and detail profile. `work-group-id`,
`detail-profile`, `coverage-target`, `runner-family`, `ecosystem` when present,
and `selector-variant` must match the frozen selector row and expected evidence
slot exactly. Detail `outcome` is execution-produced result data and is not
compared to the frozen expected evidence slot. A frozen identity mismatch is
inadmissible with `malformed-bundle`. Aggregation then resolves the
frozen `detail-profile-definition` and requires exactly one `subcheck-results`
entry for every required subcheck and no extra entries. Detail `outcome` must
match the enclosing selector/category outcome and the outcome derived from the
subcheck aggregate. A successful detail result is admissible only when every
blocking subcheck succeeds, skipped or failed blocking subchecks carry diagnostics,
and the category outcome equals the aggregate of its subcheck results.

A `descriptor-validation` row uses `category-result.detail` with this minimum
shape:

```yaml
descriptor-obligation-results:
    - descriptor-obligation-id: string
      descriptor:
          path: string
          identity: string | null
          owner-subject-id: string | null
          source: ecosystem-provider | workflow-release-provider
      descriptor-scope: selected | ecosystem | all-discovered
      outcome: success | blocking-failure | skipped
      diagnostics: [diagnostic-record]
```

The single planned `descriptor-obligation-id` bound to the selector must appear
exactly once, and no other descriptor obligation may appear in that row.
Descriptor path, identity, owner, source, and scope are copied from the frozen
plan and digest-bound fact snapshot, then equality-checked by aggregation.

A `release-shaped-artifact` row uses the section 13 `category-result.detail`
shape. Those rows may contain logical release-shaped receipt checks because CI is
validating the release-shaped output contract, but the row, bundle, aggregate
manifest, and aggregate summary are validation-only evidence. They are not release
immutable proof and cannot be reused to satisfy release publication evidence.
Release-shaped rows must preserve one distinct selector result per frozen artifact
obligation even when a shared validation-only/no-publish executor invocation
produces multiple staged artifacts. The selector-level `evidence.artifact-refs`
set must exactly equal that row's single nested `artifact.observed.refs` set; the
nested release-shaped detail remains the authority for planned-vs-observed artifact
shape checks.

### 14.4 Skipped, Failed, and Dependency-Blocked Semantics

Validation failures do not block independent selectors. A valid upstream selector
row with `outcome: blocking-failure` is a produced dependency result and may be
used for dependency gating; aggregation later fails the final verdict from that
blocking validation result. A selector is dependency-blocked only when a required
dependency result is skipped, missing, unavailable, or not admitted for gating, or
when an upstream batch/control-plane/bundle failure prevents the dependency result
from being admitted.

When a dependency-blocked selector is assigned to a batch that can still write its
bundle, the bundle must contain one skipped selector row for that selector:

- `outcome: skipped`;
- `skip-reason: dependency-blocked`;
- `evidence.artifact-refs: []`;
- nested capability or category outcomes set to `skipped`;
- release-shaped category detail still carries one result for each frozen
  artifact obligation assigned to the selector, including planned artifact and
  receipt shape plus skipped/unavailable observed shape fields; and
- a `validation-work-skipped` diagnostic with `diagnostic-detail:
dependency-blocked`.

Dependency-blocked evidence is admissible as a produced skipped row, but it never
satisfies a required evidence expectation as successful validation. Aggregation
records `required-evidence-skipped` and fails the final verdict. Missing bundles, malformed skipped rows, and skipped rows without the required
diagnostic are inadmissible and also produce `required-evidence-missing` with
`missing-bundle` when no valid bundle satisfies the expected slot.

### 14.5 Aggregate-Evidence Behavior and Summary Schema

`aggregate-evidence` attempts to consume the frozen validation plan, required
companion planning snapshots, the execution-batch manifest when one is expected,
and the expected batch evidence bundles. Before admitting bundle evidence, it
verifies the pre-final input namespace:

- exactly one authoritative request artifact for every run attempt; a missing,
  invalid, or unreplayable request follows the existing request-invalid
  fail-closed/no-authoritative-plan path and is never treated as not-required;
- every pre-final input non-bundle ref with expected cardinality `0` or `1`:
  changed-files and fact snapshots require exactly one authoritative artifact when
  their frozen plan fields require them, and require zero instances otherwise;
- exactly one authoritative plan artifact when the request boundary is replayable
  and a plan is expected, including schema, digest, producer authority, and
  companion snapshot bindings; a missing expected plan is `invalid-plan`, not a
  zero-cardinality observation;
- zero validation-plan cardinality only for explicit no-authoritative-plan request
  cases where planning was not authorized to emit a plan;
- exactly one authoritative execution-batch manifest when an authoritative plan
  exists and materialization produces a reliable batch set, with matching
  `plan-id`, `plan-digest`, dependency DAG, selector assignment coverage,
  expected evidence slots, writer identities, compatibility-profile digests, and
  fixed budget caps;
- empty-manifest semantics for authoritative fail-closed, no-work, and
  zero-execution plans, which still require exactly one manifest with `batches:
[]`;
- zero execution-batch-manifest cardinality only when no authoritative plan exists
  or materialization fails before a reliable batch set exists; in that path no
  bundle is admissible, no frozen executable evidence expectations are emitted
  for no-authoritative-plan terminal handling, and aggregation emits the
  invalid-plan/no-bundle result;
- one expected bundle slot for each executable batch and no bundle slot for an
  empty manifest; each executable slot must have exactly one internally admitted
  candidate to be valid, while missing, duplicate, stale retry, wrong-run,
  wrong-attempt, wrong-batch-ref, wrong-batch-id, off-batch validation result,
  dependency-incomplete, unreadable, malformed, or otherwise inadmissible
  candidates remain recorded under that slot for replay diagnostics;
- bundle identity, manifest binding, validation-grade writer provenance,
  runner/platform/profile compatibility proofs and digests, execution-tree
  binding, dependency binding, and per-selector row completeness;
- dependency coverage across admitted upstream bundle rows and earlier in-batch
  selector rows;
- pre-final artifact count, job count, Windows job count, aggregate target
  duration, and aggregate maximum duration caps from the manifest and observed
  workflow state, with final artifact slots reserved but not yet producer-verified;
- no unexpected, duplicate, wrong-producer, wrong-run, unreadable, malformed,
  wrong-ref, or overflowed prefixed contract artifact in the bounded pre-final
  namespace; and
- no publication authority or release side effects in any evidence path.

After bundle admission and verdict computation, aggregation writes and verifies the
aggregate evidence manifest and then writes and verifies the aggregate summary. The
post-publication verification recomputes final artifact digests from uploaded
bytes, verifies producer authority and instance counts for both final refs, and
checks that adding those final artifacts keeps the complete validation artifact
count at or below 20.

Aggregation measures its own duration in seconds. The target remains 1 to 2
minutes, and `aggregate-max-duration-seconds` must be no greater than 120
seconds. Observed aggregate duration is recorded telemetry for the performance
target only. It is not a correctness contract: exceeding the manifest maximum
does not set a reason, emit diagnostics or failure kinds, influence verdict
derivation, or fail the final required check. `final-evidence-failure` remains
reserved for aggregate evidence manifest authority diagnostics.

The aggregate summary uses:

```yaml
common-envelope: inherited
api-version: three.ci.validation.aggregate-summary/v1alpha1
kind: ci-validation-aggregate-summary
artifact-ref: ci-validation/aggregate/<run-id>/<run-attempt>/aggregate-summary.json
plan-id: string | null
plan-digest: string | null
mode: pull_request | push | scheduled_full | unknown
aggregate-evidence-manifest:
    artifact-ref: string | null
    artifact-instance-id: string | null
    content-digest: string | null
final-artifacts:
    aggregate-evidence-manifest:
        artifact-ref: string
        artifact-instance-id: string | null
        content-digest: string | null
        producer-verified: boolean
        authority-diagnostics: diagnostic[] | absent
    aggregate-summary:
        artifact-ref: string
        artifact-instance-id: absent
        content-digest: absent
        producer-verified-after-upload: external-to-payload
validation-tree:
    commit-sha: string | null
    ref: string | null
affected-range:
    status: available | unavailable | not-applicable | unknown
    base-sha: string | null
    base-tip-sha: string | null
    head-sha: string | null
    changed-files-hash: string | null
request:
    artifact-ref: string | null
    request-digest: string | null
scheduled-full:
    enabled: boolean | null
verdict: passed | failed
reason:
    invalid-plan: boolean
    fail-closed: boolean
    required-evidence-missing: boolean
    required-evidence-skipped: boolean
    blocking-validation-failure: boolean
    inadmissible-batch-evidence: boolean
    namespace-closure-failure: boolean
    required-input-artifact-failure: boolean
    aggregate-summary-without-manifest: boolean
    final-producer-unverified: boolean
    final-evidence-failure: boolean
budgets:
    pre-final-validation-artifacts: integer
    expected-final-validation-artifacts: integer
    expected-actual-validation-artifacts: integer
    max-validation-artifacts: integer
    actual-execution-batches: integer
    actual-total-jobs: integer
    actual-windows-jobs: integer
    aggregate-duration-seconds: integer
    aggregate-target-duration-seconds: integer
    aggregate-max-duration-seconds: integer
diagnostics:
    - diagnostic-record
batch-bundles:
    - batch-id: string
      artifact-ref: string
      bundle-id: string | null
      admitted-candidate-id: string | null
      candidate-count: integer
      admissibility: valid | inadmissible | missing | duplicate
      diagnostics: [diagnostic-record]
evidence-results:
    - evidence-expectation-id: string
      work-group-id: string
      batch-id: string | null
      bundle-id: string | null
      selector-index: integer | null
      outcome: satisfied | missing | skipped | failed
      diagnostics: [diagnostic-record]
failures:
    - kind: failure-kind
      batch-id: string | null
      work-group-id: string | null
      evidence-expectation-id: string | null
      bundle-id: string | null
      diagnostic: diagnostic-record
      message: string
work-groups:
    executable-required: integer
    required-succeeded: integer
    required-failed: integer
    required-skipped: integer
    required-missing: integer
    terminal-aggregation: present
proof-admissibility: validation-only
```

`budgets.expected-final-validation-artifacts` and
`budgets.expected-actual-validation-artifacts` are pre-upload expectations, not
observed final artifact counts. The authoritative `observed-final-validation-artifacts`
and final `actual-validation-artifacts` values are external-to-payload results of
post-publication artifact enumeration. `final-artifacts.aggregate-summary.artifact-instance-id`
and `final-artifacts.aggregate-summary.content-digest` are explicitly absent
because the summary cannot contain the instance ID or digest of its own uploaded
artifact. Post-run acceptance and same-attempt retry verification compute the
aggregate summary digest from the artifact bytes and compare producer authority,
instance identity, and final observed artifact counts outside the summary payload.

`evidence-results` is the normalized machine-readable result for every evidence
expectation. Every evidence expectation is verdict-relevant: `missing`, `skipped`,
and `failed` results must have corresponding `failures` entries. `failure-kind` is
one of `invalid-plan`, `required-evidence-missing`, `required-evidence-skipped`,
`blocking-validation-failure`, `inadmissible-batch-evidence`,
`namespace-closure-failure`, `required-input-artifact-failure`,
`aggregate-summary-without-manifest`, `final-producer-unverified`,
`final-evidence-failure`, or `fail-closed`.

For structurally valid plans, `plan-digest`, `mode`, `validation-tree`,
`affected-range`, `request`, and `scheduled-full` are copied from the frozen plan
after aggregation verifies the plan and request replay boundary. If aggregation
cannot parse, schema-validate, digest-verify, producer-verify, or structurally
validate the plan, it emits a failed aggregate with `reason.invalid-plan: true`,
`reason.fail-closed: false`, unverified plan-derived fields set to `null` or
`unknown`, empty `evidence-results`, zero executable counts, and exactly the
applicable `invalid-plan` failure. If no authoritative plan exists because the
request boundary is missing or unreplayable, aggregation must emit the applicable
aggregation-produced `request-invalid` detail for every missing or unreplayable
request-boundary path, sets `reason.required-evidence-missing: false`, admits no
bundle, and emits the no-bundle aggregate. If the plan is valid but a required post-plan
control artifact or companion planning snapshot is invalid, no bundle is
admissible and the aggregate follows the same `invalid-plan` path while retaining
verified plan fields for inspection.

For a structurally valid fail-closed plan, aggregation verifies the empty manifest,
admits no batch bundle, copies planner fail-closed diagnostics into the aggregate,
sets `reason.fail-closed: true`, records fail-closed failures, and fails the
workflow conclusion. For no-work and zero-execution success cases, aggregation
verifies the empty manifest, admits no batch bundle, records zero executable
required work groups, and may pass only after final aggregate evidence validation
succeeds.

Aggregate arrays use canonical ordering to keep reruns and retries stable:
`diagnostics` by `diagnostic-id`; `batch-bundles` by `batch-id`;
`batch-bundles[].observed-candidates` by `candidate-id`;
`unexpected-contract-artifacts` by the implicit deterministic unexpected-artifact
ID defined in section 14.1; `evidence-results` by `evidence-expectation-id`; and
`failures` by `(kind, batch-id, work-group-id, evidence-expectation-id,
diagnostic.diagnostic-id)` with `null` before strings. The aggregate evidence manifest and aggregate summary are
serialized as RFC 8785 canonical UTF-8 JSON bytes before upload. Semantic JSON
equivalence is insufficient for final digest replay.

## 15. Diagnostics

Planner, execution-batch, and aggregation diagnostics use a closed registered
vocabulary. Contract diagnostics are carried inside the validation plan, batch
evidence bundles, aggregate evidence manifest, and aggregate summary. They are not supplied by standalone per-work-group receipts,
selector-assignment artifacts, writer-observation artifacts, or unbounded
receipt uploads. Legacy receipt-like files are non-authoritative unexpected
artifacts only; they are never current authority for selector assignment, writer
identity, or validation success.

| Diagnostic family                     | Producer                                    | CI verdict effect                                                                             |
| ------------------------------------- | ------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `request-invalid`                     | planner or aggregation                      | fail-closed when planner-produced; invalid-plan/no-bundle aggregate when aggregation-produced |
| `range-unconfirmed`                   | planner                                     | fail-closed                                                                                   |
| `unknown-change`                      | planner                                     | fail-closed                                                                                   |
| `subject-unresolved`                  | planner                                     | fail-closed                                                                                   |
| `dependency-impact-insufficient`      | planner                                     | fail-closed                                                                                   |
| `fact-provider-insufficient`          | planner                                     | fail-closed                                                                                   |
| `no-validation-capability`            | planner                                     | fail-closed                                                                                   |
| `infrastructure-surface-unclassified` | planner                                     | fail-closed                                                                                   |
| `descriptor-invalid`                  | planner or descriptor-validation work group | fail-closed when obligations cannot be derived; otherwise blocking validation failure         |
| `artifact-shape-unconfirmed`          | release-shaped validation work group        | blocking validation failure                                                                   |
| `validation-work-failed`              | executable validation work group            | blocking validation failure                                                                   |
| `validation-work-skipped`             | executable validation work group            | required evidence skipped                                                                     |
| `known-non-impacting`                 | planner                                     | inspectable non-failure                                                                       |
| `required-evidence-missing`           | aggregation                                 | failed verdict                                                                                |
| `required-evidence-skipped`           | aggregation                                 | failed verdict                                                                                |
| `inadmissible-batch-evidence`         | aggregation                                 | failed verdict                                                                                |
| `namespace-closure-failure`           | aggregation                                 | failed verdict or no authoritative final aggregate                                            |
| `required-input-artifact-failure`     | aggregation                                 | invalid-plan/no-bundle aggregate or failed verdict                                            |
| `aggregate-summary-without-manifest`  | aggregation                                 | failed verdict when summary cannot use manifest authority                                     |
| `final-producer-unverified`           | aggregation                                 | failed verdict when final aggregate evidence manifest producer cannot be verified             |
| `final-evidence-failure`              | aggregation                                 | failed verdict from aggregate evidence manifest authority diagnostics only                    |
| `invalid-plan`                        | aggregation                                 | failed verdict with `reason.invalid-plan: true` and `reason.fail-closed: false`               |

`diagnostic-detail` is a stable subcode for diagnostic families that need
machine-readable reasons. `request-invalid` details are:

- `request-missing`;
- `request-duplicate`;
- `request-unreadable`;
- `request-malformed`;
- `request-schema-invalid`;
- `request-ref-mismatch`;
- `request-digest-mismatch`;
- `request-wrong-run-attempt`;
- `request-producer-unverified`.

Planner-produced `request-invalid` diagnostics require an authoritative
fail-closed plan. When request input is missing or unreplayable and no
authoritative plan exists, aggregation must emit the applicable
aggregation-produced `request-invalid` diagnostic detail with the same closed
detail taxonomy for every no-authoritative-plan request-boundary path. That
aggregate path sets `reason.invalid-plan: true`, `reason.fail-closed: false`,
and `reason.required-evidence-missing: false`; no execution-batch manifest or
bundle is admissible, and the aggregate records the invalid-plan/no-bundle
result.

`range-unconfirmed` details are:

- `missing`;
- `incomplete`;
- `inconsistent`;
- `unconfirmed-provenance`.

Current G5 `inadmissible-batch-evidence` details include:

- `malformed-bundle`;
- `missing-bundle`;
- `duplicate-bundle-candidates`;
- `bundle-producer-unverified`;
- `bundle-metadata-authority-invalid`;
- `execution-batch-manifest-missing`;
- `execution-batch-manifest-duplicate`;
- `execution-batch-manifest-unreadable`;
- `execution-batch-manifest-malformed`;
- `execution-batch-manifest-non-canonical`;
- `execution-batch-manifest-digest-mismatch`;
- `execution-batch-manifest-plan-mismatch`;
- `execution-batch-manifest-bundle-ref-mismatch`.

These details are limited to expected bundle slots and their observed
candidates. Prefixed contract artifacts outside admitted refs are namespace
closure failures, not inadmissible batch evidence.

`invalid-plan` details include:

- `plan-unreadable`;
- `plan-missing`;
- `plan-duplicate`;
- `malformed-plan`;
- `schema-invalid`;
- `plan-producer-unverified`;
- `plan-digest-mismatch`;
- `subject-universe-digest-mismatch`;
- `changed-files-impact-coverage-mismatch`;
- `changed-files-snapshot-missing`;
- `changed-files-snapshot-unexpected`;
- `changed-files-snapshot-duplicate`;
- `changed-files-snapshot-producer-unverified`;
- `changed-files-snapshot-unreadable`;
- `changed-files-snapshot-malformed`;
- `changed-files-snapshot-schema-invalid`;
- `changed-files-snapshot-ref-mismatch`;
- `changed-files-snapshot-envelope-mismatch`;
- `changed-files-snapshot-noncanonical`;
- `changed-files-snapshot-digest-mismatch`;
- `fact-snapshot-missing`;
- `fact-snapshot-unexpected`;
- `fact-snapshot-duplicate`;
- `fact-snapshot-producer-unverified`;
- `fact-snapshot-unreadable`;
- `fact-snapshot-malformed`;
- `fact-snapshot-schema-invalid`;
- `fact-snapshot-ref-mismatch`;
- `fact-snapshot-envelope-mismatch`;
- `fact-snapshot-plan-mismatch`;
- `fact-snapshot-cross-reference-invalid`;
- `fact-snapshot-noncanonical`;
- `fact-snapshot-digest-mismatch`;
- `structurally-invalid`.

`validation-work-failed` details are:

- `build`;
- `test`;
- `lint`;
- `format`;
- `type-check`;
- `tooling`.

`validation-work-skipped` details are:

- `dependency-blocked`.

`required-evidence-missing` details are:

- `missing-bundle`.

`required-evidence-skipped` details are:

- `dependency-blocked`.

`namespace-closure-failure` details are:

- `unexpected-contract-artifact`;
- `namespace-enumeration-unavailable`;
- `namespace-overflow`.

These details apply to pre-final bounded namespace closure before final
aggregate publication. Post-publication reconciliation mismatches, including
final namespace closure mismatches and namespace overflow, are workflow-gate
failures only. They must not be emitted as aggregate-summary JSON `reason`
keys, failure kinds, or public diagnostic details, and must not create duplicate
generic fail-closed rows.

`required-input-artifact-failure`, `aggregate-summary-without-manifest`, and
`final-producer-unverified` each use their matching diagnostic detail. They are
first-class aggregate summary failure kinds/reason keys and must not be
collapsed into `final-evidence-failure`. Aggregate duration observations are
telemetry only and must not be emitted as aggregate summary reason keys, failure
kinds, or public diagnostic details.

`final-evidence-failure` details are limited to aggregate evidence manifest
authority diagnostics:

- `aggregate-evidence-manifest-missing`;
- `aggregate-evidence-manifest-duplicate`;
- `aggregate-evidence-manifest-unreadable`;
- `aggregate-evidence-manifest-malformed`;
- `aggregate-evidence-manifest-non-canonical`;
- `aggregate-evidence-manifest-digest-mismatch`.

Aggregate-summary artifact missing, duplicate, malformed, non-canonical,
digest-mismatch, or producer-boundary problems discovered after
`aggregate-summary.json` is uploaded are post-upload workflow gate diagnostics.
They are not public aggregate summary JSON `reason` keys, failure kinds, or
`final-evidence-failure`/`final-producer-unverified` details.

Executable validation work groups use `validation-work-failed` for
`blocking-failure` selector rows unless a more specific registered diagnostic
family applies.

Every planner diagnostic with `verdict-effect: fail-closed` must be copied into
the aggregate `diagnostics` and represented in `failures` with `kind:
fail-closed`. When an affected request fails closed with `range-unconfirmed`,
its `diagnostic-detail` must be propagated to the planner diagnostic and the
aggregate failure under this general rule.

When aggregation sees inadmissible batch evidence for an expected bundle slot or
an invalid execution-batch manifest input, it must record
`inadmissible-batch-evidence` with the applicable registered diagnostic detail
and fail the aggregate verdict. Execution-batch manifest plan-id and plan-digest
mismatches use `execution-batch-manifest-plan-mismatch` and
`execution-batch-manifest-digest-mismatch`; `execution-batch-manifest-malformed`
is reserved for structural invalidity. When it sees a prefixed contract artifact outside
the admitted request, plan, companion snapshot, execution-batch manifest, expected
batch bundle, aggregate evidence manifest, or aggregate summary refs, it must
record `namespace-closure-failure` with the applicable namespace detail. If the
pre-final bounded namespace count exceeds
`20 - expected-final-validation-artifacts`, or the platform cannot prove enough
final aggregate slots remain, aggregation records `namespace-closure-failure`
with `diagnostic-detail: namespace-overflow` instead of appending an unbounded
artifact list. If the platform cannot enumerate the namespace, aggregation
records `namespace-closure-failure` with `diagnostic-detail:
namespace-enumeration-unavailable`. If post-publication final reconciliation
overflows the 20-artifact validation cap, the workflow gate fails under
`namespace-closure-failure` handling; it does not produce a public
`final-evidence-failure` row.

Observed candidates may be preserved in aggregate diagnostics for replay, but
only producer-verified admitted batch bundle candidates can satisfy evidence
expectations. When a required expectation has no valid matching bundle because all observed
candidates were missing, duplicate, producer-unverified,
bundle-metadata-authority-invalid, malformed, or otherwise inadmissible,
aggregation represents the inadmissible candidates with specific
`inadmissible-batch-evidence` details and also records
`required-evidence-missing` with `missing-bundle` when no valid bundle satisfies
the expectation. Payload writer fields, command logs, job conclusions,
selector-assignment files, and receipt-like files do not satisfy evidence by
themselves.

Diagnostic families and `diagnostic-detail` values are closed for this
`v1alpha1` low-level contract. A verdict-relevant diagnostic family or detail not
listed here is not valid evidence and must be introduced only by updating this
LLD, the schema, and the relevant `api-version` or compatibility contract.
Implementation-owned informational messages may appear only in non-contract logs
or as registered diagnostics with `verdict-effect: none`; they must not create
new machine-readable verdict-affecting codes by convention.

## 16. HK Left-Shift Mapping

HK uses planner-aligned lightweight checks only.

| HK surface                                      | CI relationship                                                                | Constraint                                                           |
| ----------------------------------------------- | ------------------------------------------------------------------------------ | -------------------------------------------------------------------- |
| default hooks                                   | Early feedback for formatting, linting, and policy checks that are lightweight | Must not require release-shaped artifacts or full dependency closure |
| explicit slow/profile checks                    | Optional local approximation of heavier CI gates                               | Must not be treated as CI evidence                                   |
| `hk run --all` or equivalent manual invocation  | Developer-selected broader local confidence                                    | Must not be required for ordinary hook success                       |
| `hk --plan` / JSON plan-style output where used | Local inspectability aid                                                       | Must not be used as workflow-release validation plan proof           |

The concrete HK profile names and command lists are implementation-owned. The
implementation must avoid turning ordinary local hooks into full CI.

## 17. Acceptance Traceability

Implementation acceptance must include at least these evidence scenarios:

| Scenario                                                                                                                                                                                                                           | Expected evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Project-scoped descriptor-backed change                                                                                                                                                                                            | Plan selects direct subject, safe downstream subjects, direct-vs-downstream selection provenance, descriptor obligation, ecosystem gates, release-shaped artifact obligations, execution-batch manifest, batch evidence bundles with per-selector evidence/result rows, logical release-shaped receipt checks, and passing aggregation                                                                                                                                                                                                                                                                                                                                                                  |
| Project-scoped validation-only change                                                                                                                                                                                              | Plan selects validation-only subject and ecosystem gates, execution-batch manifest assigns the executable gates, batch evidence bundles contain per-selector evidence/result rows, and no publish or release-shaped artifact obligation appears unless descriptor-backed; selected active validation-only subjects with no enabled validation capability fail closed with `no-validation-capability` instead of producing no required evidence                                                                                                                                                                                                                                                          |
| Ecosystem-scoped change                                                                                                                                                                                                            | Plan selects all active subjects in ecosystem, descriptors for descriptor-backed subjects, release-shaped artifact and logical release-shaped receipt-check obligations, and applicable ecosystem gates; execution-batch manifest, batch evidence bundles, and per-selector evidence/result rows cover the selected executable obligations                                                                                                                                                                                                                                                                                                                                                              |
| Workflow-release infrastructure change                                                                                                                                                                                             | Plan selects affected tooling surface, related subjects/ecosystems, and all discovered descriptors only for descriptor semantics, authoring validation, planning, contracts, build execution, publish execution, or smoke validation impacts; execution-batch manifest, batch evidence bundles, and per-selector evidence/result rows cover the selected executable obligations                                                                                                                                                                                                                                                                                                                         |
| Known global change                                                                                                                                                                                                                | Plan selects scheduled-full-equivalent scope with global provenance and required workflow-release-tooling work groups for every closed tooling surface; execution-batch manifest, batch evidence bundles, and per-selector evidence/result rows cover the selected executable obligations                                                                                                                                                                                                                                                                                                                                                                                                               |
| Scheduled full run                                                                                                                                                                                                                 | Plan selects full repository scope with required workflow-release-tooling work groups for every closed tooling surface and scheduled provenance records using `selection-kind: scheduled-full`, empty impact/expansion refs, `scheduled-full-source: true`, and `scheduled-full.enabled: true`; execution-batch manifest, batch evidence bundles, and per-selector evidence/result rows cover the selected executable obligations                                                                                                                                                                                                                                                                       |
| Executable plan with multiple logical selectors coalesced into fewer concrete execution batches                                                                                                                                    | The frozen plan remains authoritative for logical work groups, selectors, dependencies, and evidence expectations; the execution-batch manifest assigns each executable selector exactly once to one execution batch, each execution batch maps to one budget-counted batch evidence bundle, may place multiple compatible selectors in one batch, and batch bundles preserve one result row per assigned selector without requiring one job or artifact per logical selector/work group                                                                                                                                                                                                           |
| Broad, global, or scheduled-full executable materialization with non-empty batches                                                                                                                                                 | Execution-batch manifest and aggregate evidence show bounded topology: runner-family orchestrator jobs remain bounded, each execution batch maps to exactly one budget-counted batch evidence bundle, lower topology-count bounds are waived for fail-closed, no-executable, all lightweight-only manifests including executable lightweight selectors/checks, and zero-work manifests, and maximum caps still apply wherever the corresponding topology count is present                                                                                                                                                       |
| Validation artifact budget at normal finalization                                                                                                                                                                                  | The run has at most 20 prefixed validation artifacts total, including input non-bundle artifacts, one batch evidence bundle per executable batch, the aggregate evidence manifest, and the aggregate summary; acceptance does not require or allow one artifact per selector/work group and treats overflow as bounded namespace failure                                                                                                                                                                                                                                                                                                                                                                |
| Full, broad, or global validation performance evidence                                                                                                                                                                             | Aggregate summary and workflow evidence expose observed CI duration and historical estimates for the full/broad/global 12-minute target; the target is an optimization and observability expectation, not a hard correctness ceiling, and no per-batch duration cap is inferred from this acceptance goal                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| Aggregate duration budget evidence                                                                                                                                                                                                 | Execution-batch manifest declares aggregate target/max duration in seconds with max no greater than 120, and aggregate summary records observed aggregate duration as telemetry only; aggregate duration is observable performance evidence, not a correctness contract, and observed overruns do not set reasons, emit diagnostics or failure kinds, or fail the final required check                                                                                                                                                                                                                                                                                                                       |
| Known non-impacting change with no executable checks                                                                                                                                                                               | Lightweight-only plan passes without heavy work, remains inspectable, has no executable validation work groups, uses a verified empty execution-batch manifest, has no batch evidence bundles, and has terminal aggregate evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| Known non-impacting change with executable lightweight checks                                                                                                                                                                      | Verified execution-batch manifest assigns the lightweight selectors, and lightweight work appears as per-selector success evidence/result rows in the assigned batch evidence bundle for pass                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| Known non-impacting lightweight-only plan attempts subject, ecosystem, or descriptor-scoped lightweight work                                                                                                                       | The plan is structurally invalid; lightweight-only executable checks must use `lightweight-policy` or workflow-release `tooling-surface` coverage targets rather than implying selected validation subjects                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Confirmed zero-file affected range                                                                                                                                                                                                 | Affected request has `affected-range.status: available`, empty `changed-files`, non-null canonical `changed-files-hash`, executable lightweight-only plan with available discovered subjects all marked `not-selected`, an available provider fact snapshot whose provider subject IDs exactly match the provider-bound frozen subject universe, unsupported audit subjects only when they satisfy the unsupported-subject constraints, no selected subjects, no executable validation work groups, no evidence expectations, verified empty execution-batch manifest, no batch evidence bundles or per-selector evidence rows, and passing terminal aggregate evidence after final evidence validation |
| Wrong-run or producer-unverified planner-facing request                                                                                                                                                                            | Planning does not trust affected-range or scheduled-full payload claims; it either fails closed with `request-invalid` or emits no authoritative plan unless the request ref, digest, instance count, envelope, and `normalize-input` producer authority verify; authoritative plans and aggregates freeze the verified request ref and digest for replay                                                                                                                                                                                                                                                                                                                                               |
| Missing, duplicate, unreadable, malformed, schema-invalid, producer-unverified, or ref-unidentified planner-facing request                                                                                                         | No authoritative validation plan is emitted because the request cannot satisfy the replayable request boundary needed for plan request binding                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| No authoritative plan path reaches aggregation                                                                                                                                                                                     | Aggregation emits a failed terminal aggregate with `invalid-plan` or the request-invalid/no-authoritative-plan diagnostic path as applicable, admits no execution-batch manifest or batch evidence bundles, preserves bounded diagnostics, emits no missing evidence expectations for the no-authoritative-plan terminal path, and does not treat missing executable evidence as a passing zero-work result                                                                                                                                                                                                                                                                                             |
| Digest-mismatched or wrong-run request that is still replayable enough to freeze request ref and recomputed digest                                                                                                                 | Planning may emit a fail-closed plan with `request-invalid`; aggregation replay-verifies the request artifact boundary and preserves the fail-closed diagnostic rather than converting it to `invalid-plan`; the fail-closed handoff has no executable validation work groups, a verified empty execution-batch manifest, no batch evidence bundles, and terminal aggregate evidence                                                                                                                                                                                                                                                                                                                    |
| Execution-batch materialization receives invalid, producer-unverified, identity-unverified, or companion-mismatched plan                                                                                                           | `materialize-execution-batches` emits no executable batch set unless it can verify plan identity and all authoritative plan plus companion snapshot checks; fan-out never runs from a plan that has not passed that validation                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Structurally valid fail-closed or no-executable plan                                                                                                                                                                               | No executable validation work groups, exactly one verified empty execution-batch manifest, no batch evidence bundles, and terminal aggregate evidence preserve fail-closed or no-work semantics instead of reporting `invalid-plan`                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| Mixed project/ecosystem/infrastructure/non-impacting change                                                                                                                                                                        | Plan unions all selected scopes, descriptor/release-shaped obligations, ecosystem gates, and additive lightweight obligations; execution-batch manifest, batch evidence bundles, and per-selector evidence/result rows cover the selected executable obligations; broader scopes may subsume duplicates only with explicit `classification.subsumptions` records, and non-impacting paths do not replace required heavyweight validation                                                                                                                                                                                                                                                                |
| Multiple independent selection causes for the same subject are subsumed before freezing                                                                                                                                            | The retained subject-selection provenance record remains in `classification.subject-selection-provenance`, and `classification.subsumptions` uses `subsumed-kind: subject-selection-provenance` with deterministic candidate provenance IDs                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Policy-bearing planner/classifier/fact-provider change                                                                                                                                                                             | Plan is produced using the validation tree under review, exposes `planner.policy-source: validation-tree` plus verified `planner.execution-tree` provenance, and acceptance rejects evidence planned by a baseline or wrong-tree policy                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| Affected plan omits or invents changed-path impact coverage                                                                                                                                                                        | Aggregation emits `invalid-plan` with `diagnostic-detail: changed-files-impact-coverage-mismatch` rather than allowing omitted paths to bypass fail-closed classification                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| PR/push affected range unconfirmed                                                                                                                                                                                                 | Request diagnostic `range-unconfirmed`, fail-closed plan, failing aggregation/workflow conclusion, no executable validation work groups, verified empty execution-batch manifest, no batch evidence bundles, and terminal aggregate evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| Project-scoped change with insufficient downstream facts                                                                                                                                                                           | Planner diagnostic `dependency-impact-insufficient` or `fact-provider-insufficient`, fail-closed plan, failing aggregation/workflow conclusion, no executable validation work groups, verified empty execution-batch manifest, no batch evidence bundles, and terminal aggregate evidence                                                                                                                                                                                                                                                                                                                                                                                                               |
| Unclassifiable workflow-release infrastructure impact                                                                                                                                                                              | Planner diagnostic `infrastructure-surface-unclassified`, fail-closed plan, failing aggregation/workflow conclusion, no executable validation work groups, verified empty execution-batch manifest, no batch evidence bundles, and terminal aggregate evidence                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Unknown path                                                                                                                                                                                                                       | Fail-closed plan, failing aggregation/workflow conclusion, no executable validation work groups, verified empty execution-batch manifest, no batch evidence bundles, and terminal aggregate evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Invalid descriptor blocking derivation                                                                                                                                                                                             | Fail-closed derivation has no executable validation work groups, verified empty execution-batch manifest, no batch evidence bundles, failing terminal aggregate evidence, and failing workflow conclusion; executable descriptor-validation failures are captured in manifest-assigned batch evidence bundles with per-selector failure rows                                                                                                                                                                                                                                                                                                                                                            |
| Duplicate descriptor paths appear in the fact snapshot                                                                                                                                                                             | The fact snapshot is structurally invalid because descriptor obligations, target-catalog entries, and descriptor evidence rows resolve descriptor facts by the globally unique `descriptor-path` key                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Descriptor-validation batch evidence row omits or mismatches its bound descriptor obligation                                                                                                                                       | Aggregation treats the batch evidence row as inadmissible with `malformed-bundle`; descriptor obligation ID, descriptor path/identity/owner/source, and descriptor scope must match the frozen plan and fact snapshot exactly                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| Missing, unreadable, malformed, schema-invalid, duplicate, producer-unverified, or digest-mismatched validation plan                                                                                                               | Aggregation emits a failed `invalid-plan` aggregate with the applicable plan diagnostic detail, unverified plan-derived fields set to `null` or `unknown`, empty evidence results, zero executable counts, and no batch-bundle admissibility authority                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Structurally invalid but schema/digest-valid validation plan                                                                                                                                                                       | Aggregation emits `invalid-plan` with `diagnostic-detail: structurally-invalid`, empty evidence results, zero executable counts, and no batch-bundle admissibility authority                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| Release-shaped artifact obligation references a mismatched subject, descriptor owner, validation obligation, work group, ecosystem, or runner                                                                                      | The plan is structurally invalid because artifact validation must bind to the selected descriptor-backed subject and its digest-bound descriptor and target-catalog facts                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| Work group has a missing or mismatched ecosystem for ecosystem-specific execution                                                                                                                                                  | The plan is structurally invalid because runner and command selection consume the frozen work-group ecosystem rather than rediscovering it during execution                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Missing, unexpected, duplicate, producer-unverified, malformed, ref-mismatched, noncanonical, or digest-mismatched companion planning snapshot                                                                                     | Aggregation rejects the otherwise readable plan as `invalid-plan` with the applicable changed-files or fact-snapshot diagnostic detail                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Validation obligation references a mismatched or shared work group/evidence expectation                                                                                                                                            | The plan is structurally invalid unless duplicate candidates were removed before freezing and represented only by explicit subsumption records                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Executable work group or evidence expectation is not referenced by its required source obligation chain                                                                                                                            | The plan is structurally invalid; every executable validation selector is verdict-relevant and must be bound to the matching validation, descriptor, or artifact obligation contract                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Missing, duplicate, producer-unverified, plan-mismatched, budget-overflowing, unmaterializable, or structurally invalid execution-batch manifest produces required-input-artifact-failure with inadmissible-batch-evidence details | Aggregation emits `required-input-artifact-failure`, records the manifest diagnostic under `inadmissible-batch-evidence`, and does not admit batch evidence bundles                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| Non-bundle control or final artifact mapped to missing or wrong platform job identity                                                                                                                                              | Producer authority verification rejects the artifact as producer-unverified using the boundary identity map; payload producer claims, logs, and job conclusions do not substitute. This immutable workflow/job proof is not required for live batch bundle admission.                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Logical contract artifact ref maps ambiguously or incorrectly to a physical artifact name                                                                                                                                          | Artifact instance counting, duplicate detection, and namespace enumeration use the canonical attempt-visible physical-name mapping `three-ci-validation-<run-id>-<run-attempt>-<sha256(logical-ref)>`; prefixed artifacts whose payload refs do not recompute to the observed physical name for the current run attempt are non-authoritative or unexpected in closed namespaces                                                                                                                                                                                                                                                                                                                        |
| Prefixed physical artifact does not match any expected non-bundle contract ref during evidence namespace closure                                                                                                                   | Aggregation classifies expected non-bundle artifacts first, classifies aggregate evidence manifest and aggregate summary refs as final-artifact refs whenever present, then classifies remaining prefixed artifacts against expected batch bundle refs from the execution-batch manifest; only artifacts matching none of those sets are unexpected evidence / unexpected contract artifacts                                                                                                                                                                                                                                                                                                            |
| Batch evidence bundle has missing or mismatched validation-grade manifest/API binding                                                                                                                                              | Aggregation treats the bundle as inadmissible; live bundle admission uses manifest expectations, current-run artifact/API metadata, downloaded artifact metadata, payload validation, and run/run-attempt/batch binding, without a separate writer-observation artifact or immutable workflow/job producer proof.                                                                                                                                                                                                                                                                                                                                                                                       |
| Request payload artifact ref or physical artifact name mismatches the contract-owned request ref                                                                                                                                   | Planning fails closed with `request-invalid` and `diagnostic-detail: request-ref-mismatch`, or emits no authoritative plan                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| Aggregate cannot replay-verify the request artifact ref or digest frozen into an otherwise structurally valid plan                                                                                                                 | Aggregation emits `invalid-plan`; copied plan semantics are insufficient without the authoritative normalized request artifact identity and digest                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| Missing batch evidence bundle or per-selector evidence row                                                                                                                                                                         | Aggregation fails with `required-evidence-missing` and identifies the missing work group or evidence expectation                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| Planned validation work skipped or failed                                                                                                                                                                                          | Verified execution-batch manifest assigns the selectors, batch evidence bundles contain per-selector skipped or failed evidence/result rows, and aggregation records `required-evidence-skipped` or `blocking-validation-failure` and fails the final verdict; planned executable validation work is not optional or non-gating                                                                                                                                                                                                                                                                                                                                                                         |
| Ecosystem gate omits a capability enabled by selected subject/provider facts                                                                                                                                                       | Plan is fail-closed with `fact-provider-insufficient` or structurally invalid; batch evidence cannot pass by matching an under-planned capability set                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Upstream selector emits a valid `blocking-failure` batch evidence row                                                                                                                                                              | The batch can still write evidence for dependency gating, downstream selectors are not dependency-blocked solely by that validation outcome, and aggregation fails the final verdict from the batch evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| Batch evidence row emitted after validation on the wrong or unverifiable execution tree                                                                                                                                            | Aggregation treats the batch evidence row as inadmissible with `malformed-bundle`; copied plan provenance is insufficient without execution-tree evidence from the execution-batch boundary                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Non-current contract artifact appears in the contract artifact namespace                                                                                                                                                        | Aggregation treats it as a non-authoritative unexpected contract artifact; it cannot replace the current execution-batch manifest, batch evidence bundle, aggregate evidence manifest, or aggregate summary requirements                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| Obsolete control artifact appears in the contract artifact namespace                                                                                                                                                        | Aggregation treats it as non-authoritative and, when it uses the prefixed contract namespace, as an unexpected contract artifact; selector assignment comes only from the execution-batch manifest, and live bundle admission comes only from manifest expectations, artifact/API metadata, downloaded metadata, and payload validation                                                                                                                                                                                                                                                                                                                                                                 |
| Release-shaped artifact batch evidence row with empty, partial, extra, or unavailable expected artifact coverage, missing artifact digest, unchecked logical release-shaped receipt check, or mismatched planned shape             | Aggregation records `artifact-shape-unconfirmed` or `malformed-bundle` and fails the final verdict                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| Dependency-blocked release-shaped artifact batch evidence row                                                                                                                                                                      | The batch evidence row may use the explicit skipped form with empty observed artifact refs and digests plus `validation-work-skipped: dependency-blocked`; aggregation treats it as required evidence skipped and fails the final verdict, not as successful artifact-shape validation                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Lightweight-preflight or workflow-release-tooling batch evidence row omits or mismatches its required detail profile or subcheck results                                                                                           | Aggregation treats the batch evidence row as inadmissible with `malformed-bundle`; category-result detail must match the frozen work group, evidence expectation, and profile subcheck contract                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| Invalid or mismatched batch evidence row or bundle                                                                                                                                                                                 | Inadmissible batch evidence does not satisfy required evidence; aggregation fails with the applicable inadmissibility reason, and also `required-evidence-missing` when no valid matching batch evidence exists                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| Forged or self-attested batch writer metadata                                                                                                                                                                                      | Matching payload fields or caller-generated sidecars are insufficient; aggregation treats the batch evidence bundle as inadmissible unless current-run artifact/API metadata, downloaded metadata, payload validation, and manifest binding all validate                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| Valid required batch evidence plus extra inadmissible batch evidence                                                                                                                                                               | Required evidence is satisfied by the valid batch evidence, but aggregation still fails for the extra malformed, duplicate, producer-unverified, or metadata-invalid batch evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| Batch evidence bundle appears after evidence namespace closure                                                                                                                                                                     | Post-run acceptance or same-attempt retry treats final evidence as non-authoritative rather than extending the closed evidence set                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| Same-attempt finalization retry with occupied aggregate evidence manifest or aggregate summary                                                                                                                                     | Aggregation preserves the occupied artifact's `created-at` while recomputing raw digest equality; aggregate evidence manifest authority mismatches produce `final-evidence-failure`, while aggregate summary self-artifact mismatches fail the post-upload workflow gate without public summary diagnostics                                                                                                                                                                                                                                                                                                                                                                                             |
| Aggregate summary exists at the final ref but the aggregate evidence manifest is missing                                                                                                                                           | Same-attempt retry treats the final state as non-recoverable and non-authoritative; it does not recreate a manifest to satisfy the aggregate's existing aggregate-evidence-manifest content-digest claim                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| Aggregate evidence manifest finalization/reconciliation fails despite a passing computed validation verdict                                                                                                                        | The aggregate records `reason.final-evidence-failure` and `failure-kind: final-evidence-failure` only for aggregate evidence manifest authority diagnostics; otherwise the `aggregate-evidence` job and final required check fail under the no-authoritative-final-evidence contract                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Missing, duplicate, malformed, wrong-run, wrong-producer, or mutually mismatched aggregate evidence manifest or aggregate summary                                                                                                  | Aggregate evidence manifest authority failures are public `final-evidence-failure` rows; aggregate summary self-artifact failures are post-upload workflow gate failures only. Logs, job conclusions, or auxiliary artifacts cannot replace the exact contract-owned manifest and aggregate summary artifacts                                                                                                                                                                                                                                                                                                                                                                                           |
| Aggregate evidence manifest or aggregate summary JSON is not RFC 8785 canonical UTF-8 JSON                                                                                                                                         | Aggregate evidence manifest JSON authority failures record `final-evidence-failure`; aggregate summary JSON byte/canonical/digest failures fail the post-upload workflow gate without a public summary reason/kind/detail. Semantic JSON equivalence is insufficient for final digest replay                                                                                                                                                                                                                                                                                                                                                                                                            |
| Unknown verdict-relevant diagnostic family or detail appears in contract evidence                                                                                                                                                  | Schema or aggregation rejects it under the closed `v1alpha1` diagnostic vocabulary unless the LLD/schema/api-version contract has been updated                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Unconfirmed artifact shape                                                                                                                                                                                                         | Blocking validation failure, no release-proof admissibility                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Unconfirmed PR context                                                                                                                                                                                                             | No publication credentials, release environment, or OIDC publish permission exposed                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| Accidental publication or remote publish-state validation                                                                                                                                                                          | Static workflow/config/code review and batch-evidence/aggregate inspection show no work group, command output, batch evidence field, aggregate field, registry query, GitHub Release lookup, tag lookup, or remote publish-state observation is used as validation evidence                                                                                                                                                                                                                                                                                                                                                                                                                             |
| All CI validation modes have no configured publication authority                                                                                                                                                                   | Static workflow/config/code review covers `pull_request`, `push`, and `scheduled_full`; no publication credentials, OIDC publish permission, release environment, registry mutation, GitHub Release mutation, or release tag mutation is configured                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| CI validation evidence is presented to release proof lookup or publication admissibility                                                                                                                                           | Release proof consumers reject batch bundles, aggregate evidence manifests, aggregate summaries, and logical release-shaped receipt checks because every current CI evidence path is `proof-admissibility: validation-only` and cannot satisfy release immutable proof                                                                                                                                                                                                                                                                                                                                                                                                                                  |

These scenarios are acceptance contracts, not prescribed test framework or file
layout. The implementer may choose the concrete test harness.

## 18. Implementation-Owned Boundaries

The following remain implementation-owned for the single senior engineer:

- internal planner module boundaries and private data structures;
- concrete command lines for ecosystem gates and descriptor validation;
- exact workflow job identifiers when they preserve the boundary identity map and
  logical sequence;
- reusable workflow, composite action, or helper script decomposition;
- exact JSON Schema file locations and type-generation approach;
- temporary directories and log formatting;
- upload names for logs and auxiliary artifacts other than contract-owned
  execution-batch manifests, batch evidence bundles, aggregate evidence manifests, and aggregate summaries;
- batching strategy for work-group selectors;
- exact HK profile names and step ordering;
- internal test organization.

The following are not implementation-owned:

- CI belonging to workflow-release rather than a separate CI truth;
- validation plan, execution-batch manifest, batch evidence bundle, aggregate
  evidence manifest, and aggregate summary `api-version`/`kind` families;
- plan authority over classification, subjects, obligations, work groups, and
  diagnostics;
- unknown/unclassifiable fail-closed behavior;
- scheduled-full-equivalent global scope;
- no publication credentials or release side effects;
- validation-only proof inadmissibility;
- final verdict semantics for fail-closed, missing evidence, blocking failures,
  and lightweight-only plans.
- contract-owned execution-batch manifest refs/names, including
  `execution-batch-manifest.json`;
- contract-owned batch evidence bundle refs/names, including
  `batch-evidence-bundle.json`;
- contract-owned aggregate evidence manifest and aggregate summary refs/names.

## 19. Outcome

This low-level design gives the implementer a concrete handoff baseline for CI
affected validation without prescribing code internals. It freezes the workflow
entry boundary, logical job sequence, request and plan files, subject snapshot
shape, fact-provider realization, semantic path classification families, scope
resolution rules, work-group selectors, execution mapping, artifact-validation
obligations, validation-only batch evidence, diagnostics, HK relationship, and
acceptance evidence. Concrete code structure and command implementation remain
owned by the senior engineer as long as these contracts are preserved.
