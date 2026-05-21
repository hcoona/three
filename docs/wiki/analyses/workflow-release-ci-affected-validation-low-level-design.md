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
| Receipts/evidence  | Emit one validation-only batch evidence bundle per execution batch plus one aggregation report; descriptor, subject, and artifact obligations are evidence rows, and all evidence is inadmissible as release immutable proof.                                                                                                 |
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

- full, broad, and global validation target at most 12 minutes, with a hard
  ceiling of 15 minutes;
- total GitHub Actions jobs target 12 to 18, including 4 to 8 Windows jobs;
- validation artifacts target at most 20;
- final aggregation target 1 to 2 minutes;
- work groups and selectors are logical validation obligations, not concrete
  GitHub Actions jobs or matrix rows;
- the physical execution unit is the execution batch, and each execution batch
  produces one validation-only batch evidence bundle;
- execution-batch count is capped by both the job budget and the current-run
  artifact budget: maximum execution batches must be no more than 20 minus the
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

Implementation may introduce reusable internal workflow files or composite
actions for execution batches. Those internal files are implementation-owned
unless later branch protection or external policy starts depending on them. Each
execution batch still maps to exactly one budget-counted concrete GitHub Actions
job or matrix leg. A reusable-workflow call-site or matrix leg may be that one
budgeted execution-batch job, but it must not hide additional budget-relevant jobs
outside the execution-batch manifest counts.

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
    - emits exactly one validation plan artifact at the contract-owned ref;
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
    - runs executable batches in a DAG that preserves inter-batch dependencies;
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
    - emits one aggregation report at the contract-owned ref;
    - fails the workflow when the aggregated validation outcome fails.

These job names are logical handoff names. The implementer may map them to
concrete job identifiers, reusable workflows, or grouped jobs, provided the
sequence, authority boundary, evidence semantics, invariant that each execution
batch maps to one budgeted job, and final required check context remain intact.

Logical handoff names are also producer-authority boundaries. The workflow
contract must define a boundary identity map before execution that maps each
logical boundary (`normalize-input`, `plan`, `materialize-execution-batches`,
execution-batch boundaries, and `aggregate-evidence`) to the allowed GitHub
Actions job identifiers, reusable-workflow call-site job identifiers, and matrix
identity dimensions that may produce artifacts for that boundary. This map is
control-plane contract data, not a payload claim; artifact consumers verify
producer authority by comparing immutable workflow/job context to the mapped
identity for the expected logical boundary. If a concrete job topology change
cannot preserve that mapping, the workflow contract and acceptance evidence must
be updated before the changed topology is authoritative. Missing boundary-map
coverage or a platform identity outside the allowed set makes the artifact
producer-unverified for that boundary.

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

| Artifact class                                             | Required producer boundary        | Allowed non-payload authority signals                                                                                                                                              |
| ---------------------------------------------------------- | --------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Planner-facing CI request                                  | `normalize-input`                 | Contract-owned request ref and instance count plus immutable workflow/job context proving the artifact was uploaded by the input-normalization control-plane boundary              |
| Validation plan, changed-files snapshot, and fact snapshot | `plan`                            | Contract-owned artifact ref and instance count plus immutable workflow/job context proving the artifact was uploaded by the planning control-plane boundary for this run attempt   |
| Execution-batch manifest                                   | `materialize-execution-batches`   | Contract-owned artifact ref and instance count plus immutable workflow/job context proving the artifact was uploaded by the execution-batch-materialization control-plane boundary |
| CI validation batch evidence bundle                        | Assigned execution-batch boundary | Execution-batch assignment, contract-owned bundle ref, artifact instance ID, and immutable workflow/job context proving the bundle was uploaded by the assigned batch boundary     |
| Bundle manifest and aggregate                              | `aggregate-evidence`              | Contract-owned final refs and instance counts plus immutable workflow/job context proving final artifacts were uploaded by the aggregation control-plane boundary                  |

The immutable workflow/job context must match the boundary identity map from
section 4.2 for the required producer boundary. If the workflow platform or
control-plane wrapper cannot expose those signals for an artifact class, or the
observed platform identity is not allowed by the boundary identity map, that
artifact is producer-unverified. Producer-unverified planning artifacts make the
plan invalid, producer-unverified batch evidence bundles are inadmissible, and
producer-unverified final manifest or aggregate artifacts are not authoritative
acceptance evidence. Payload fields, artifact names supplied by executable
validation commands, logs, and job conclusions never prove producer authority.
Validation-grade batch writer integrity is metadata embedded in the batch
evidence bundle and verified by final aggregation; it is not a separate artifact,
ref, or namespace entry.

The GitHub Actions implementation enforces this with two independent
control-plane checks before a gating artifact is consumed: the run artifact
namespace is enumerated and must contain exactly one live instance at the
contract-owned physical name, and the enumerated artifact instance ID must match
the upload output observed from the workflow job mapped to that logical boundary.
Consumers download producer-verified inputs by artifact ID where the workflow can
fail immediately. The final aggregation job performs the same namespace and
producer-boundary verification for its inputs before accepting a plan, and verifies
the bundle manifest and aggregate uploads after publication; any missing,
duplicated, stale, or producer-mismatched artifact remains fail-closed.

`artifact-ref` values in this document are logical contract refs, not physical
GitHub artifact names. Every logical ref maps to one fixed-length physical
artifact name with this digest mapping:

```text
physical-artifact-name = "three-ci-validation-" + lowercase_sha256(utf8(logical-artifact-ref))
```

The physical name is 84 ASCII characters and is stable regardless of logical-ref
length. For artifact classes that carry an `artifact-ref`, aggregation recomputes
the expected physical name from that payload field and requires it to equal the
observed physical name before trusting the payload. Artifact instance counting,
duplicate detection, namespace enumeration, producer checks, and replay binding
operate on physical artifact instances whose names have the
`three-ci-validation-` prefix and whose payload or contract ref recomputes to the
expected physical name. A prefixed physical artifact whose payload is unreadable,
has no expected `artifact-ref`, has a non-canonical logical ref, or recomputes to
a different physical name is non-authoritative for that contract boundary even if
its payload fields resemble valid evidence; in closed namespaces, such an
instance is an unexpected contract artifact.

Because the physical artifact namespace is flat, aggregation must classify all
prefixed physical artifacts against the complete set of expected non-bundle
contract refs before closing the bundle namespace. The expected non-bundle set
contains the request, validation plan, changed-files snapshot, fact snapshot,
execution-batch manifest, bundle manifest, and aggregate refs for the current run
attempt. Any prefixed physical artifact that matches one of those expected
non-bundle physical names is handled only by that contract's validation rules. Any
remaining prefixed physical artifact that is not classifiable as an expected
non-bundle artifact is bundle-like for bundle manifest enumeration, even when its
payload is unreadable or does not reveal a logical bundle ref.

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
  final manifest/aggregate evidence; no batch evidence bundle is required or
  expected.
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
  mapping in this LLD. For the first implementation, `ruby` and `other` subjects
  may be discovered only as `inactive` or `not-selected`; if an affected,
  ecosystem, global, or scheduled-full scope would otherwise select them,
  planning fails closed with `fact-provider-insufficient`.
- A `ruby` or `other` subject-universe record is an unsupported audit entry, not
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
`python` for Python, and `javascript-typescript` for JavaScript or TypeScript.
Conversely, every subject ID listed by those ecosystem provider entries must have
exactly one matching frozen subject-universe record. Unsupported `ruby` and
`other` audit entries are excluded from this provider-subject equality check only
when they satisfy the section 7 unsupported-subject constraints. Descriptor
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
validation, publication, or remote publish-state observation. Those activities
belong to execution-layer work groups authorized by the validation plan.

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
ecosystem: dotnet | python | javascript | typescript | null
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
aggregate-output: ci-validation-aggregate
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
  dependency in an upstream batch has a successfully written batch evidence bundle
  that contains a selector result for the dependency. Final aggregation remains
  the only authority for final bundle and result admissibility.
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
        expected-non-bundle-validation-artifacts: integer
        max-execution-batches: integer
        actual-execution-batches: integer
        aggregate-target-duration-seconds: integer
        aggregate-max-duration-seconds: integer
    batches:
        - batch-id: string
          runner-family: windows | ubuntu
          compatibility-profile:
              ecosystem: dotnet | python | javascript | typescript | null
              setup-profile: string
              execution-profile: string
              release-shaped-profile: string | null
          depends-on-batches: [batch-id]
          ordered-selectors:
              - work-group-id: string
                selector-index: integer
                depends-on: [work-group-id]
                expected-evidence-id: string
                expected-evidence-slot: selector-evidence-slot
          expected-batch-evidence-bundle-ref: string
          batch-writer:
              identity-source: github-actions-job-context
              expected-boundary: execution-batch
              expected-job-identity: string
              provenance-fields: [workflow, job, matrix]
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
    execution compatibility. It records only the runner family, ecosystem, setup,
    execution, and release-shaped compatibility dimensions needed to prove that
    coalesced selectors can share one batch. `setup-profile` and
    `execution-profile` are stable path-safe profile identifiers whose digest
    preimages and equality checks include the frozen platform, setup, executor,
    and toolchain requirements not otherwise exposed as manifest enum fields. It
    does not define release-shaped build details, command lines, or evidence
    bundle schema. Those details remain owned by later release-shaped execution
    and evidence-bundle sections.
    Any batch containing a `release-shaped-artifact` selector must set a non-null,
    stable, path-safe `release-shaped-profile` derived from the frozen artifact
    and release-receipt obligations assigned to that batch. Its digest preimage
    and equality checks include the frozen release-shaped platform,
    workflow-release executor/toolchain, no-publish posture, and artifact-family
    requirements, in addition to the obligation identifiers and shape data. Every
    release-shaped selector in the batch must share that exact proven profile. If
    the materializer cannot prove shared release-shaped compatibility, it must
    split the selectors into safe batches or fail post-plan materialization
    without authorizing executable validation. Batches with no release-shaped
    selectors may keep `release-shaped-profile: null`.

    `expected-evidence-slot` is a pre-execution expectation slot, not an execution
    result. It may identify the logical work group, evidence expectation,
    category, planned capabilities, detail profile, coverage target, and the
    placeholder shape that the later batch bundle must fill. It must not contain
    outcome, success/failure state, diagnostics, observed artifact refs, observed
    digests, command output, or any other execution-produced data. Group 4 owns the
    detailed batch output result schema.

    `expected-batch-evidence-bundle-ref` is the single validation-only bundle ref
    expected from that execution batch:
    `ci-validation/bundles/<run-id>/<run-attempt>/<batch-id>/batch-evidence-bundle.json`.
    The bundle must contain separately addressable result rows for every ordered
    selector and evidence expectation assigned to the batch. A batch bundle may
    contain batch-level metadata and diagnostics, but it cannot collapse logical
    work-group outcomes into only a batch-level pass/fail result.

    `batch-writer` records validation-grade batch writer identity and provenance
    metadata for the expected bundle. The identity source is immutable GitHub
    Actions workflow/job/matrix context captured by the execution-batch boundary;
    payload fields, logs, command-authored JSON, and artifact path segments are not
    writer identity sources. This writer identity is embedded in the manifest or
    batch bundle metadata and verified by aggregation; there is no separate
    writer-integrity or writer-observation artifact in the current design.

    `budget.actual-execution-batches` must equal `batches.length`. The
    materializer must map each batch to exactly one budget-counted concrete GitHub
    Actions job or matrix leg, identified by the batch writer's expected job
    identity. Reusable workflows may implement that job, but they must not spawn or
    hide additional budget-relevant jobs outside the manifest's actual job counts.
    The materializer must compute `actual-total-jobs` from
    `non-batch-control-plane-job-count + actual-execution-batches`, compute
    `actual-windows-jobs` from Windows execution batches plus any Windows
    non-batch control-plane jobs, and compute `actual-validation-artifacts` from
    expected non-bundle validation artifacts plus expected batch evidence bundles.

    The artifact-derived execution-batch allowance is
    `20 - expected-non-bundle-validation-artifacts`. The job-derived
    execution-batch allowance is the smaller of
    `18 - non-batch-control-plane-job-count` and the remaining Windows allowance
    for Windows batches, while Ubuntu batches are still constrained by the total
    job allowance. `budget.max-execution-batches` must be no greater than the
    smaller of the artifact-derived allowance and the applicable job-derived
    allowance for the manifest's planned runner-family mix.

    Declared budget fields cannot relax the fixed LLD caps. Max caps apply to all
    manifests where applicable: `max-total-jobs` and `actual-total-jobs` must be at
    most 18, `max-windows-jobs` and `actual-windows-jobs` must be at most 8,
    `max-validation-artifacts` and `actual-validation-artifacts` must be at most
    20, and `aggregate-max-duration-seconds` must be at most 120. Lower-bound
    topology targets apply only to executable broad, full, or global
    materializations with non-empty batch sets: those manifests must keep
    `min-total-jobs` at least 12 and `min-windows-jobs` at least 4, and actual
    counts must satisfy those lower bounds. Fail-closed, no-executable,
    lightweight-only with no executable checks, and zero-file no-work
    materializations use a zero-execution budget profile with `batches: []`; they
    preserve fail-closed or no-work aggregation semantics and are not invalid
    merely because actual total or Windows jobs are below the broad/full/global
    lower-bound targets.

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
    unmaterializable-obligation manifest makes aggregation fail closed as
    `invalid-plan`; no batch evidence bundle is admissible under an invalid
    manifest.
    Aggregation must also recompute or verify the manifest's current-run budget
    totals before admitting any batch bundle: batch count equals `batches.length`,
    artifact count equals declared `actual-validation-artifacts`, total and
    Windows job counts equal their declared actual fields, each execution batch
    maps to one budget-counted concrete job or matrix leg, the aggregate duration
    budget fields use seconds with target less than or equal to max and max no
    greater than 120 seconds, max caps never exceed 18 total jobs, 8 Windows jobs,
    or 20 validation artifacts, and lower bounds are enforced only for executable
    broad/full/global manifests with non-empty batches. It must also verify
    `budget.max-execution-batches` against both the artifact-derived allowance and
    the job-derived allowance implied by total and Windows job caps. Any mismatch,
    hidden budget-relevant job, relaxed cap, invalid lower-bound use, or overflow
    is a post-plan control-plane/materialization failure reported through the
    invalid execution-batch manifest path.
    The `aggregate-evidence` boundary must measure its actual aggregate duration
    in seconds and fail the final required check if the actual duration exceeds
    `aggregate-max-duration-seconds`. That actual duration is execution-produced
    final evidence, not pre-execution manifest data; the later Group 4 aggregate
    report schema must record it with the final evidence.

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
  bundles, emits the aggregate verdict artifact, and does not produce executable
  validation evidence.
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
budget-counted concrete GitHub Actions job or matrix leg, and each executable
work-group selector appears in exactly one batch `ordered-selectors` list.

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
| `evidence-aggregation`                                        | Ubuntu                                                                                                  | Terminal control-plane aggregation; emits aggregate verdict artifact |

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

An execution-batch job is a validation control-plane boundary, not a raw command
line and not one job per work group. Before running category-specific validation,
the job must consume the frozen validation plan and its assigned
execution-batch manifest entry and verify at least:

- the manifest's `plan-id` and `plan-digest` match the frozen plan;
- the current GitHub Actions job or matrix identity equals the batch writer
  identity assigned to this `batch-id`;
- the batch runner family, platform, ecosystem, setup, execution profile,
  toolchain assumptions, and release-shaped profile are compatible with the
  current job context;
- every `ordered-selectors` entry resolves to the frozen work group and expected
  evidence slot by exact identifier and dependency list;
- every selector dependency is covered either by an earlier selector in this
  batch or by a declared upstream batch whose bundle/result is available for
  dependency gating;
- budget invariants that can be checked from the job context and manifest remain
  consistent, including the one-batch-to-one-budgeted-job mapping; and
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
the execution-batch manifest, and validation batch evidence bundles, emits
`ci-validation-aggregate`, and does not emit normal executable validation
evidence.

Aggregation uses always-run failure-reporting semantics after the planning and
execution-batch materialization attempts. If planning emits no readable plan,
emits an invalid plan, or execution-batch materialization fails before producing a
reliable executable batch set, aggregation emits an `invalid-plan` aggregate with
zero executable batches rather than allowing the workflow to end without an
aggregate artifact.

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
immutable proof. Pending Group 4 bundle schema finalization, the minimum result
shape below is authoritative only as the per-selector `category-result.detail`
content inside the manifest-assigned batch evidence bundle.

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
selector `evidence.artifact-refs` and `artifact.observed.refs` must both be
`[]`; `artifact.observed.digests` must be `[]`; `release-receipt.expected`
remains copied from the frozen obligation but
`release-receipt.schema-checked` must be `false`; and diagnostics must include
`validation-work-skipped` with `diagnostic-detail: dependency-blocked`. This batch
evidence row never satisfies the release-shaped artifact obligation as success: aggregation records
`required-evidence-skipped` and fails the final verdict.

## 14. Evidence and Receipt Files

Transition note: this section still contains legacy receipt terminology pending
the Group 4 evidence-bundle and aggregate rebaseline. For the current Group 3 handoff,
the authoritative execution unit is the execution batch, and each execution batch
emits one validation-only batch evidence bundle. A separate per-work-group receipt
artifact is not required by the current rebaseline. Until Group 4 replaces this
section, the receipt shape below is a compatibility placeholder for logical
per-selector result rows inside a batch evidence bundle, not an authoritative
requirement that every executable validation work group upload its own artifact.

```yaml
common-envelope: inherited
api-version: three.ci.validation.receipt/v1alpha1
kind: ci-validation-receipt
artifact-ref: string
receipt-id: string
plan-id: string
plan-digest: string
work-group-id: string
assignment-id: string
mode: pull_request | push | scheduled_full
validation-tree:
    commit-sha: string
    ref: string | null
execution-tree:
    observed-commit-sha: string | null
    source: trusted-receipt-boundary
    verified: boolean
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
outcome: success | blocking-failure | skipped
evidence:
    category: string
    planned-capabilities: [build | test | lint | format | type-check] | null
    capability-results: [capability-result] | absent
    category-result: category-result | absent
    artifact-refs: [string]
diagnostics: [diagnostic-record]
proof-admissibility: validation-only
```

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

The `evidence` union determines which result branch appears:

```yaml
capability-result branch:
    planned-capabilities: non-empty [build | test | lint | format | type-check]
    capability-results:
        - capability: build | test | lint | format | type-check
          outcome: success | blocking-failure | skipped
          diagnostics: [diagnostic-record]
    category-result: absent
category-result branch:
    planned-capabilities: null
    capability-results: absent
    category-result:
        outcome: success | blocking-failure | skipped
        diagnostics: [diagnostic-record]
        detail: object | null
```

`lightweight-preflight` compatibility rows use `category-result.detail` with this
minimum shape pending Group 4:

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

`workflow-release-tooling` compatibility rows use `category-result.detail` with this
minimum shape pending Group 4:

```yaml
workflow-release-tooling:
    work-group-id: string
    detail-profile: string
    coverage-target:
        type: tooling-surface | subject | ecosystem | descriptor
        id: string
    ecosystem: dotnet | python | javascript | typescript | null
    selector-variant: string | null
    runner-family: windows | ubuntu
    outcome: success | blocking-failure | skipped
    subcheck-results:
        - subcheck-id: string
          outcome: success | blocking-failure | skipped
          diagnostics: [diagnostic-record]
    diagnostics: [diagnostic-record]
```

For required `lightweight-preflight` and `workflow-release-tooling` batch evidence rows,
`category-result.detail` must be non-null and must contain exactly the matching
category object above. Aggregation equality-checks `work-group-id`,
`detail-profile`, `coverage-target`, `ecosystem` when present,
`selector-variant`, `runner-family`, and `outcome` against the frozen work group,
evidence expectation, and selector outcome. Missing detail, the wrong detail
object, an unplanned `detail-profile`, or any mismatched frozen field makes the
batch evidence row inadmissible with `mismatched-evidence-payload`.
Aggregation also resolves the frozen `detail-profile-definition` and requires
`subcheck-results` to contain exactly one entry for every required subcheck and no
extra entries. A `success` category result is admissible only when every blocking
subcheck result is `success`, all skipped or failed blocking subchecks carry
diagnostics, and the category outcome equals the aggregate of its subcheck results.
A batch evidence row whose category-level outcome is successful while required subchecks are
missing, duplicated, extra, skipped, failed, or inconsistent is inadmissible with
`mismatched-evidence-payload`, not merely a successful self-attestation.

A `descriptor-validation` batch evidence row uses `category-result.detail` with this
minimum shape:

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

The single planned `descriptor-obligation-id` bound to the
`descriptor-validation` work group must appear exactly once, and no other
descriptor obligation may appear in that row. `descriptor.path`,
`descriptor.identity`, `descriptor.owner-subject-id`, `descriptor.source`, and
`descriptor-scope` are copied from the frozen descriptor obligation and its
digest-bound fact snapshot descriptor record, then equality-checked by
aggregation. A descriptor-validation batch evidence row that omits the bound obligation,
adds another obligation, or reports descriptor fields that do not match the
frozen plan and fact snapshot is inadmissible with `mismatched-evidence-payload`.

Legacy receipt compatibility rules, non-authoritative for Group 3 execution
mapping:

- The current authoritative evidence unit is the manifest-assigned batch evidence
  bundle. Execution batches are not required to emit separate per-work-group or
  per-selector receipt artifacts, and no standalone receipt artifact satisfies a
  required evidence expectation outside its containing batch bundle.
- The legacy receipt intake boundary below remains only a Group 4 compatibility
  placeholder for diagnostic mirroring of receipt-like artifacts that may appear
  during migration. It must not be used to infer selector assignment, writer
  authority, dependency gating, release proof, or batch success.
- If legacy receipt-like artifacts are present under
  `ci-validation/receipts/<run-id>/<run-attempt>/`, aggregation may enumerate them
  for diagnostics after first classifying expected non-bundle contract refs for
  the current run attempt. Unexpected prefixed artifacts fail closed only under
  the legacy compatibility rules; the current Group 3 admissibility path remains
  the execution-batch manifest plus expected batch evidence bundle refs.
- The nested receipt-manifest wording below is legacy/non-authoritative until
  Group 4 replaces it with final batch-bundle schema and namespace rules. If it
  is retained for diagnostics during migration, final receipt manifest and
  aggregate artifacts must be serialized as RFC 8785 canonical UTF-8 JSON bytes
  before upload. Any rule below that refers to raw
  manifest or aggregate artifact bytes, content digests, or replay recomputation
  uses those canonical bytes; semantically equivalent but non-canonical JSON is
  malformed final evidence.
  Aggregation owns manifest creation and finalization: at aggregation start it
  enumerates the closed namespace, computes observed digests from the artifact
  bytes it reads, and writes the control-plane manifest
  `ci-validation/manifests/<run-id>/<run-attempt>/receipt-manifest.json` as an observed
  enumeration artifact. Manifest entries have:

    ```yaml
    common-envelope: inherited
    api-version: three.ci.validation.receipt-manifest/v1alpha1
    kind: ci-validation-receipt-manifest
    plan-id: string | null
    plan-digest: string | null
    receipt-namespace-closure:
        source: aggregate-evidence
        closed-receipt-count: integer
        observed-entry-ids: [string]
    entries:
        - observed-entry-id: string
          artifact-ref: string | null
          physical-artifact-name: string
          artifact-instance-id: string
          assignment-id: string | null
          writer-work-group-id: string | null
          trusted-writer-id: string | null
          observed-writer-id: string | null
          writer-observation-ref: string | null
          receipt-id: string | null
          receipt-content-digest: string | null
    ```

    Receipt manifest entries always record the observed `physical-artifact-name`.
    `artifact-ref` is the logical receipt ref only after aggregation can establish
    it from a legacy expected receipt ref or from a readable receipt payload whose
    `artifact-ref` recomputes to the observed physical name;
    otherwise it is `null`. Unreadable, malformed, unparseable, or otherwise
    unclassified prefixed artifacts remain observed receipt-like entries with
    `artifact-ref: null`, `writer-work-group-id: null`, and
    `receipt-id: null`; they are inadmissible unexpected receipt artifacts rather
    than ignored artifacts.

    `receipt-namespace-closure` records the closed receipt set observed by the
    first authoritative aggregation pass for the run attempt. Aggregation may
    declare the namespace closed only after execution-batch materialization is
    complete
    and all executable execution-batch jobs that can write batch bundles have
    reached a terminal state. The closure's `closed-receipt-count` and
    `observed-entry-ids` must equal the manifest entries. Post-run acceptance and
    same-attempt aggregation retries must re-enumerate the receipt intake
    namespace and require exact equality with the manifest's closed physical
    artifact names, logical artifact refs when non-null, artifact instance IDs,
    and observed entry IDs. Any receipt-like artifact instance that appears after
    closure, or any missing closed instance, makes final evidence
    non-authoritative rather than extending the observed receipt set.

    `observed-entry-id` is a stable aggregation-assigned identifier for one
    observed receipt-like artifact instance. It is derived from `run-id`,
    `run-attempt`, `artifact-ref`, and the artifact service or control-plane
    enumeration `artifact-instance-id`. Aggregation validates the plan and
    required post-plan control artifacts before authoritative receipt
    admissibility classification. If the artifact store cannot provide a stable
    per-instance ID after the plan and current execution-batch manifest are
    authoritative, the receipt namespace is unenumerable and aggregation emits a
    failed aggregate with `reason.inadmissible-receipt: true`, a failure with
    `kind: inadmissible-receipt`, and diagnostic detail
    `unstable-artifact-instance-id` rather than collapsing duplicates. If the
    plan or post-plan control contract is invalid, unstable receipt instance IDs
    remain inspection-only observations and must not create
    `inadmissible-receipt` failures.
    `writer-work-group-id` is derived from the logical artifact-ref path segment
    after aggregation has established `artifact-ref`, not from the receipt payload
    alone or from the digest physical name; it is `null` when the logical ref is
    missing or malformed.
    `assignment-id`, `trusted-writer-id`, `observed-writer-id`, and
    `writer-observation-ref` are legacy receipt-manifest placeholder fields pending
    Group 4. They are not current Group 3 admissibility gates, and
    selector-assignment or writer-observation artifacts are not required
    contract-owned artifacts under the execution-batch handoff. Current batch
    bundle admissibility is gated by the verified execution-batch manifest,
    expected batch evidence bundle refs, batch writer provenance embedded in the
    manifest or bundle metadata, and platform/control-plane producer authority.
    `receipt-content-digest` in the manifest is the aggregator-observed SHA-256
    digest, not a writer claim. The receipt manifest artifact content digest is
    the lowercase hexadecimal SHA-256 digest of the raw manifest artifact bytes
    written at the contract-owned manifest ref. Aggregation is the only
    authorized writer for the manifest and the only reader that derives the
    CI-level verdict. Entries are sorted by `observed-entry-id`. Duplicate
    observed entries, artifact refs that do not match the derived pattern,
    writer/work-group mismatches between artifact ref and receipt payload,
    cross-attempt artifacts, and unreadable receipt artifacts are observed
    inadmissible entries and must appear in aggregate diagnostics/failures. Missing
    or mismatched legacy writer identity placeholders are diagnostic-only pending
    Group 4 and are not current Group 3 inadmissibility criteria. A pre-existing
    manifest
    uploaded by an executable work group in the receipt intake namespace is
    treated as an unexpected receipt-like artifact, not as aggregation authority.
    Re-running aggregation for the same `run-id` and `run-attempt` never adds to
    the observed receipt set once an authoritative manifest has closed the
    namespace. Same-attempt finalization is manifest-first. Aggregation first
    computes or verifies the final manifest from the closed receipt set, then
    emits or verifies the aggregate that binds that exact manifest digest. If
    neither final ref exists, aggregation uploads the manifest, verifies the
    uploaded manifest instance and digest, then uploads the aggregate that records
    that manifest digest. Their `created-at` values become the replay-stable final
    timestamps for those raw artifacts. If the manifest exists and the aggregate
    is missing, aggregation verifies the occupied manifest's raw content digest by
    recomputing the manifest with that artifact's existing `created-at`, then
    uploads the missing aggregate bound to the occupied manifest digest. If both
    final refs already have one artifact instance, aggregation verifies each raw
    digest by preserving that artifact's own `created-at` value and verifies the
    aggregate's recorded `receipt-manifest.content-digest` equals the occupied
    manifest digest. If the aggregate exists while the manifest is missing, the
    final state is non-recoverable and non-authoritative for that run attempt:
    aggregation must not recreate a manifest to satisfy an existing aggregate
    claim because the aggregate already binds exact manifest bytes. It must never
    upload a second artifact instance at an occupied final ref. A duplicate final
    instance, digest mismatch after preserving the occupied final `created-at`,
    unreadable occupied final artifact, namespace-closure mismatch,
    aggregate-without-manifest final state, or occupied ref whose producer
    authority cannot be verified makes finalization fail and leaves post-run
    acceptance without authoritative final evidence.

    Post-run acceptance evidence requires exactly one authoritative receipt
    manifest artifact instance at the contract-owned manifest ref. A missing,
    duplicate, unreadable, malformed, or wrong-run manifest is non-authoritative
    acceptance evidence even if the workflow produced other logs or auxiliary
    artifacts.

- When aggregation cannot verify a readable plan identity, the manifest
  `plan-id` and `plan-digest` are `null`. Manifest entries still record observed
  receipt-like artifacts in the closed intake namespace, but no entry can be
  admissible until a structurally valid plan, current post-plan execution-batch
  manifest, and matching plan identity are verified. The older
  selector-assignment manifest wording in this section is legacy pending Group 4
  and is not a current Group 3 admissibility requirement.
- `receipt-id` is an opaque stable identifier for the receipt emission within the
  run attempt or equivalent execution provenance. It must not be derived from a
  representation that includes itself.
- `plan-id`, `plan-digest`, and `work-group-id` must match the validation plan.
  `plan-digest` matching means equality to the recomputed digest defined in
  section 6.1, not merely equality to an unverified string copied from the plan.
  `assignment-id` is a legacy placeholder pending Group 4; receipt payloads cannot
  create, change, or authorize current execution-batch assignment.
- Because each executable `work-group-id` has exactly one evidence expectation in
  the plan, aggregation matches receipts to evidence expectations by `plan-id`
  and `work-group-id`.
- Receipts must mirror the plan provenance: affected-mode receipts carry
  `validation-tree`, `affected-range`, and `scheduled-full` fields matching the
  plan envelope; scheduled-full receipts carry `affected-range.status:
not-applicable`, null affected-range SHAs and hash, and `scheduled-full.enabled:
true`.
- The execution-batch boundary must observe the checkout or execution tree before
  running validation and bind it to the planned tree in the batch evidence row.
  For executable selectors, `execution-tree.observed-commit-sha` must equal the
  frozen `validation-tree.commit-sha`, `execution-tree.source` is the
  execution-batch boundary, and `execution-tree.verified` must be `true` for the
  row to be admissible. The category-specific validation command may not
  self-attest the execution tree. A missing, unverifiable, or mismatched execution
  tree makes the row inadmissible with `mismatched-evidence-payload`; if the
  execution-batch boundary cannot verify the tree before validation, it must emit
  a blocking row or no row rather than a successful one.
- Receipts and aggregates copy `changed-files-hash` from the frozen plan. They
  must not rediscover or reorder changed files; aggregation only recomputes the
  hash from the companion changed-files snapshot artifact. A receipt mismatch is
  inadmissible as `wrong-plan` or `mismatched-evidence-payload`; a missing or
  mismatched companion snapshot makes the plan invalid.
- `receipt-content-digest` is the lowercase hexadecimal SHA-256 digest of the raw
  receipt artifact bytes as observed by aggregation. It is recorded for every
  observed receipt artifact, including malformed or inadmissible artifacts, and
  must match `^[0-9a-f]{64}$` when the artifact bytes are readable.
- Receipt payload fields must match the frozen plan's matched work group and
  evidence expectation: `coverage-target`, evidence `category`, and
  `planned-capabilities` are equality-checked against the plan. Mismatches are
  inadmissible.
- Receipt top-level `evidence.artifact-refs` is not independent verdict evidence.
  It must be `[]` for every receipt category except `release-shaped-artifact`. For
  `release-shaped-artifact` receipts, it must equal the canonical
  `category-result.detail.artifact-obligation-results[0].artifact.observed.refs`
  set, which is already equality-checked against the frozen artifact obligation's
  `expected-artifact-refs`, except for the explicit dependency-blocked skipped
  release-shaped form where both values are `[]` and the receipt outcome is
  `skipped`. A missing, extra, or mismatched top-level `artifact-refs` value makes
  the receipt inadmissible with `mismatched-evidence-payload`.
- `proof-admissibility` is always `validation-only`.
- Receipt `evidence` is a discriminated union on `planned-capabilities`. Receipts
  with non-null `planned-capabilities` must include exactly one
  `capability-results` entry for each planned capability in the corresponding
  work group, must have at least one planned capability, and must omit
  `category-result`. Receipts with null
  `planned-capabilities` must include exactly one `category-result` and must omit
  `capability-results`; they must not invent capability-level coverage. `null`,
  empty-array, or empty-object substitutes for the omitted branch are
  inadmissible.
- For receipts with capability results, top-level `outcome` is derived from those
  results: any `blocking-failure` capability makes the receipt
  `blocking-failure`; otherwise any `skipped` capability makes the receipt
  `skipped`; all planned capabilities succeeding makes it `success`. A top-level
  outcome that disagrees with capability results is inadmissible.
- For null-capability receipts, top-level `outcome` is derived from
  `category-result` and, for detail-profile categories, from the profile subcheck
  results: `success` is admissible only when every blocking subcheck and the
  category-specific validation completed successfully and no blocking diagnostic is
  present; `blocking-failure` is admissible only when category-specific validation
  failed, a blocking subcheck failed, or a blocking diagnostic is present; `skipped`
  is admissible only when category-specific validation was intentionally not
  executed or a blocking subcheck was skipped and the receipt carries a diagnostic
  explaining the skip. A mismatch between top-level `outcome`, `category-result`,
  profile subcheck results, diagnostics, or skipped rules is inadmissible.
- Receipt diagnostics are verdict-affecting according to their own
  `verdict-effect` and location. Capability diagnostics affect their containing
  capability result; `category-result.diagnostics` affect the category result;
  top-level receipt diagnostics affect the whole receipt. A diagnostic with
  `verdict-effect: failed` makes the containing result `blocking-failure`;
  `verdict-effect: fail-closed` is not valid inside an executable work-group
  receipt and is inadmissible unless the low-level design registers a specific
  receipt diagnostic family for it.
- Malformed receipts, duplicate receipts for the same expectation, unexpected
  receipts, wrong-plan receipts, and receipts with an unknown or mismatched
  `work-group-id` are inadmissible for satisfying evidence expectations.
- When multiple otherwise admissible receipts match the same evidence
  expectation, aggregation fails closed to duplicate handling by default: the
  receipt with the lowest `observed-entry-id` is the only candidate that may
  satisfy the expectation, and every other matching receipt is inadmissible with
  `duplicate-receipt`. Receipts that are inadmissible for other reasons do not
  participate in choosing the satisfying receipt.
- The previous release-shaped reused-receipt-chain exception is legacy and
  non-authoritative pending Group 4 evidence-bundle finalization. Group 3 now
  allows sharing only through one compatible manifest-assigned execution batch
  with distinct per-selector rows; duplicate release-shaped receipt-like or
  bundle evidence is not accepted by this current Group 3 handoff and remains
  fail-closed duplicate evidence.
- Ambiguous duplicates, malformed chains, self-asserted or cyclic chains,
  multiple maximal chains, mismatched work-group or target/scope identities,
  missing writer authority, missing observed source proof or equivalent admissible
  source binding, unsupported evidence sources, or otherwise unsupported duplicate
  patterns remain fail-closed `duplicate-receipt` cases.
- Any observed inadmissible receipt contributes to a failing aggregated outcome
  with `inadmissible-receipt`; a valid receipt does not offset an extra
  inadmissible receipt.
- A required evidence expectation passes aggregation only when exactly one valid
  matching receipt satisfies it; zero valid receipts, duplicate evidence, or only
  inadmissible receipts aggregate as `required-evidence-missing`.
- A valid required receipt with `blocking-failure` contributes to a failing
  aggregated outcome.
- Missing required receipts contribute to a failing aggregated outcome.
- A valid required receipt with `skipped` contributes to a failing aggregated
  outcome with `required-evidence-skipped`.
- A concrete job may upload additional logs, but logs are not a substitute for the
  machine-readable receipt.
- Release-shaped validation receipts may carry evidence that both the planned
  artifact shape and the planned release-shaped receipt expectation were checked.
  The CI validation receipt is not itself the release-shaped receipt being
  validated.
- The terminal `evidence-aggregation` work group does not emit
  `ci-validation-receipt`; it emits `ci-validation-aggregate`.
- Before emitting the aggregate for any structurally valid plan, aggregation must
  recompute `subject-universe.id` from the canonical frozen `subjects` section
  when `subject-universe.status` is `available`; when it is `unavailable`,
  aggregation verifies `subject-universe.id: null`, empty `subjects`, and
  explanatory diagnostics instead. Aggregation must also verify the companion
  fact snapshot artifact when `fact-snapshot.status` is `available`, including
  provider subject IDs, dependency-edge subject IDs, roots, tooling-surface IDs,
  descriptor facts, target-catalog facts, exact subject-universe-to-provider
  coverage for every provider-bound frozen subject, unsupported-subject audit
  constraints, subject-selection provenance dependency-edge basis, and
  descriptor/artifact-obligation fact backing against the frozen plan namespaces.
  Aggregation also verifies the companion changed-files snapshot artifact when
  `changed-files-hash` is non-null. Missing, malformed,
  schema-invalid, cross-reference-invalid, or digest-mismatched companion
  artifacts, snapshot IDs, or fact-to-obligation bindings make the plan invalid
  and produce an `invalid-plan` aggregate.

The aggregation report uses:

```yaml
common-envelope: inherited
api-version: three.ci.validation.aggregate/v1alpha1
kind: ci-validation-aggregate
plan-id: string | null
plan-digest: string | null
mode: pull_request | push | scheduled_full | unknown
receipt-manifest:
    artifact-ref: string | null
    content-digest: string | null
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
    inadmissible-receipt: boolean
    final-evidence-failure: boolean
diagnostics:
    - diagnostic-record
observed-receipts:
    - observed-entry-id: string
      artifact-ref: string | null
      physical-artifact-name: string
      artifact-instance-id: string
      receipt-id: string | null
      work-group-id: string | null
      receipt-content-digest: string | null
      admissibility: valid | inadmissible
      diagnostics: [diagnostic-record]
evidence-results:
    - evidence-expectation-id: string
      work-group-id: string
      receipt-id: string | null
      observed-entry-id: string | null
      receipt-artifact-ref: string | null
      receipt-content-digest: string | null
      outcome: satisfied | missing | skipped | failed
      diagnostics: [diagnostic-record]
failures:
    - kind: failure-kind
      work-group-id: string | null
      evidence-expectation-id: string | null
      receipt-id: string | null
      observed-entry-id: string | null
      receipt-artifact-ref: string | null
      receipt-content-digest: string | null
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

The `receipt-manifest`, `observed-receipts`, `receipt-id`, and
`receipt-artifact-ref` fields in this aggregate shape are compatibility
placeholders until Group 4 replaces receipt-manifest finalization with batch
bundle finalization. They must not be read as requiring separate per-work-group
receipt artifacts under the current Group 3 execution-batch handoff; batch evidence
bundles remain the authoritative evidence unit for this rebaseline.

The aggregation report is the only CI-level verdict artifact and is emitted at
the contract-owned ref
`ci-validation/aggregate/<run-id>/<run-attempt>/ci-validation-aggregate.json`.
`receipt-manifest.artifact-ref` is the contract-owned manifest ref,
`ci-validation/manifests/<run-id>/<run-attempt>/receipt-manifest.json`, and
`receipt-manifest.content-digest` is the lowercase hexadecimal SHA-256 digest of
the raw manifest artifact bytes. Both are `null` only if aggregation cannot write
or verify the manifest artifact; in that case the aggregate must be failed and is
not authoritative acceptance evidence. Post-run acceptance must load the single
authoritative manifest artifact, recompute its digest, and reject any
manifest/aggregate mismatch as non-authoritative final evidence.
Post-run acceptance evidence requires exactly one authoritative aggregate
artifact instance at that ref. A missing, duplicate, unreadable, malformed, or
wrong-run aggregate is non-authoritative CI verdict evidence; logs, job
conclusions, or auxiliary artifacts cannot substitute for it. Post-run acceptance
must also verify that the final manifest and aggregate artifacts were produced by
the `aggregate-evidence` control-plane boundary for the same run attempt. A
manifest or aggregate artifact authored by an executable validation command, a
non-aggregation job, or an unverified artifact instance is non-authoritative even
if its payload, schema, and digest bindings match. The same-attempt finalization
reconciliation above is the only replay/idempotency contract for both final
artifacts; consumers must not choose among multiple final instances by timestamp,
job conclusion, or payload self-claims. Any final manifest or aggregate
finalization/reconciliation failure must fail the `aggregate-evidence` job and
the final required check even when the computed validation verdict would
otherwise be `passed`. Workflow conclusion must fail when `verdict` is `failed`.
For structurally valid plans, `plan-digest`, `mode`, `validation-tree`,
`affected-range`, `request`, and `scheduled-full` are copied from the frozen plan;
the aggregator must verify they match the plan before emitting the report.
Aggregation and post-run acceptance must use `request.artifact-ref` and
`request.request-digest` to verify the authoritative normalized request artifact
for executable plans, fail-closed plans, and replayable `request-invalid`
fail-closed plans: exactly one artifact instance at the frozen request ref,
matching common-envelope run identity, `normalize-input` producer authority,
schema validity, matching payload `artifact-ref`, and recomputed request digest.
For replayable `request-invalid` plans, aggregation verifies the request replay
boundary and then preserves the planner's `request-invalid` fail-closed diagnostic;
it does not require the request to be semantically valid. If request replay
verification itself fails for an otherwise structurally valid plan, aggregation
emits `invalid-plan` rather than treating the plan as detached from its original
request.
Summary booleans and counts are for quick inspection. `evidence-results` is the
normalized machine-readable result for every evidence expectation. Every evidence
expectation is verdict-relevant, so
`missing`, `skipped`, and `failed` results must have corresponding `failures`
entries. Finalization/reconciliation failures that leave no authoritative final
evidence must produce `reason.final-evidence-failure: true` and a corresponding
`failure` when an aggregate can be written; if no authoritative aggregate can be
written, the final required check still fails under the no-authoritative-final-
evidence contract. `failure-kind` is one of `invalid-plan`,
`required-evidence-missing`, `required-evidence-skipped`,
`blocking-validation-failure`, `inadmissible-receipt`,
`final-evidence-failure`, or `fail-closed`.
`observed-receipts` records every artifact instance in the closed intake
boundary, including valid, malformed, unexpected, wrong-plan, duplicate, and
otherwise inadmissible receipt artifacts. Evidence results and failures reference
the observed entry, receipt artifact, and digest that caused the result when one
exists. For unreadable or unclassified prefixed receipt artifacts,
`artifact-ref` is `null` and `physical-artifact-name` is still recorded so the
aggregate mirrors the receipt manifest without inventing a logical ref.

Aggregate arrays use canonical ordering to keep reruns and retries stable:
`diagnostics` by `diagnostic-id`; `observed-receipts` by `observed-entry-id`;
`evidence-results` by `evidence-expectation-id`; and `failures` by `(kind,
work-group-id, evidence-expectation-id, observed-entry-id,
diagnostic.diagnostic-id)` with `null` before strings. Nested diagnostics use the
same `diagnostic-id` ordering.

If aggregation cannot parse, schema-validate, digest-verify, or structurally
validate the plan, it emits a failed aggregate with `reason.invalid-plan: true`
and `reason.fail-closed: false`. In that mode, unverified plan-derived fields are
`null` or `unknown`, `evidence-results` is empty, counts are zero except
`terminal-aggregation: present`, `reason.inadmissible-receipt` is `false`, and
`failures` contains only an `invalid-plan` failure with an `invalid-plan`
diagnostic. Observed receipt-like artifacts may still be listed in
`observed-receipts` for inspection with `admissibility: inadmissible`, but they
must not create `inadmissible-receipt` failures because no verified plan exists
against which receipt admissibility can be authoritative. The aggregate must not
copy unverified plan fields merely because they were present in the unreadable or
invalid input.

If aggregation verifies the plan identity, schema, digest, and structural
validity but a required post-plan control artifact is invalid, such as a missing
or mismatched companion snapshot or execution-batch manifest, it emits a
failed aggregate with `reason.invalid-plan: true` and `reason.fail-closed: false`.
In that post-plan-contract-invalid mode, verified plan-derived fields may be
copied from the frozen plan for inspection, but no receipt can be admissible,
`evidence-results` is empty, counts are zero except `terminal-aggregation:
present`, `reason.inadmissible-receipt` is `false`, and `failures` contains only
the applicable `invalid-plan` failure. Observed receipt-like artifacts may still
be listed for inspection with `admissibility: inadmissible`, but they must not
create `inadmissible-receipt` failures because the selector and receipt
admission contracts are not authoritative under an invalid plan.
`reason.fail-closed` is reserved for structurally valid planner fail-closed
plans and must be `true` for those plans.

## 15. Diagnostics

Planner and aggregation diagnostics use a small registered vocabulary:

| Diagnostic family                     | Producer                                    | CI verdict effect                                                                     |
| ------------------------------------- | ------------------------------------------- | ------------------------------------------------------------------------------------- |
| `request-invalid`                     | planner                                     | fail-closed                                                                           |
| `range-unconfirmed`                   | planner                                     | fail-closed                                                                           |
| `unknown-change`                      | planner                                     | fail-closed                                                                           |
| `subject-unresolved`                  | planner                                     | fail-closed                                                                           |
| `dependency-impact-insufficient`      | planner                                     | fail-closed                                                                           |
| `fact-provider-insufficient`          | planner                                     | fail-closed                                                                           |
| `no-validation-capability`            | planner                                     | fail-closed                                                                           |
| `infrastructure-surface-unclassified` | planner                                     | fail-closed                                                                           |
| `descriptor-invalid`                  | planner or descriptor-validation work group | fail-closed when obligations cannot be derived; otherwise blocking validation failure |
| `artifact-shape-unconfirmed`          | release-shaped validation work group        | blocking validation failure                                                           |
| `validation-work-failed`              | executable validation work group            | blocking validation failure                                                           |
| `validation-work-skipped`             | executable validation work group            | required evidence skipped                                                             |
| `known-non-impacting`                 | planner                                     | inspectable non-failure                                                               |
| `required-evidence-missing`           | aggregation                                 | failed verdict                                                                        |
| `required-evidence-skipped`           | aggregation                                 | failed verdict                                                                        |
| `inadmissible-receipt`                | aggregation                                 | failed verdict                                                                        |
| `final-evidence-failure`              | aggregation                                 | failed verdict or no authoritative final aggregate                                    |
| `invalid-plan`                        | aggregation                                 | failed verdict with `reason.invalid-plan: true` and `reason.fail-closed: false`       |

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

`range-unconfirmed` details are:

- `missing`;
- `incomplete`;
- `inconsistent`;
- `unconfirmed-provenance`.

`inadmissible-receipt` details include:

- `malformed-artifact-ref`;
- `malformed-receipt`;
- `wrong-plan`;
- `unknown-work-group`;
- `mismatched-work-group`;
- `mismatched-writer-identity`;
- `mismatched-evidence-payload`;
- `mismatched-outcome`;
- `duplicate-receipt`;
- `unstable-artifact-instance-id`;
- `unexpected-receipt`.

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
- `execution-batch-manifest-missing`;
- `execution-batch-manifest-duplicate`;
- `execution-batch-manifest-unreadable`;
- `execution-batch-manifest-malformed`;
- `execution-batch-manifest-schema-invalid`;
- `execution-batch-manifest-plan-mismatch`;
- `execution-batch-manifest-producer-unverified`;
- `execution-batch-manifest-structurally-invalid`;
- `execution-batch-manifest-budget-overflow`;
- `execution-batch-manifest-unmaterializable-obligation`;
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

`final-evidence-failure` details include:

- `final-manifest-missing`;
- `final-manifest-duplicate`;
- `final-manifest-unreadable`;
- `final-manifest-malformed`;
- `final-manifest-non-canonical`;
- `final-manifest-digest-mismatch`;
- `final-aggregate-missing`;
- `final-aggregate-duplicate`;
- `final-aggregate-unreadable`;
- `final-aggregate-malformed`;
- `final-aggregate-non-canonical`;
- `final-aggregate-digest-mismatch`;
- `final-producer-unverified`;
- `final-namespace-closure-mismatch`;
- `aggregate-without-manifest`.

Executable validation work groups use `validation-work-failed` for
`blocking-failure` receipts unless a more specific registered diagnostic family
applies.

Every planner diagnostic with `verdict-effect: fail-closed` must be copied into
the aggregate `diagnostics` and represented in `failures` with `kind:
fail-closed`. When an affected request fails closed with `range-unconfirmed`,
its `diagnostic-detail` must be propagated to the planner diagnostic and the
aggregate failure under this general rule.

When aggregation sees an inadmissible receipt, it must record
`inadmissible-receipt` with the applicable diagnostic detail and fail the
aggregate verdict. When a required expectation has no valid matching receipt
because all observed candidates were inadmissible, aggregation must also record
`required-evidence-missing` for that expectation rather than allowing an
inadmissible receipt to satisfy it.

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

| Scenario                                                                                                                                                                                                               | Expected evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Project-scoped descriptor-backed change                                                                                                                                                                                | Plan selects direct subject, safe downstream subjects, direct-vs-downstream selection provenance, descriptor obligation, ecosystem gates, release-shaped artifact obligations, execution-batch manifest, batch evidence bundles with per-selector evidence/result rows, logical release-shaped receipt checks, and passing aggregation                                                                                                                                                                                                                                                                                                                                                                  |
| Project-scoped validation-only change                                                                                                                                                                                  | Plan selects validation-only subject and ecosystem gates, execution-batch manifest assigns the executable gates, batch evidence bundles contain per-selector evidence/result rows, and no publish or release-shaped artifact obligation appears unless descriptor-backed; selected active validation-only subjects with no enabled validation capability fail closed with `no-validation-capability` instead of producing no required evidence                                                                                                                                                                                                                                                          |
| Ecosystem-scoped change                                                                                                                                                                                                | Plan selects all active subjects in ecosystem, descriptors for descriptor-backed subjects, release-shaped artifact and logical release-shaped receipt-check obligations, and applicable ecosystem gates; execution-batch manifest, batch evidence bundles, and per-selector evidence/result rows cover the selected executable obligations                                                                                                                                                                                                                                                                                                                                                              |
| Workflow-release infrastructure change                                                                                                                                                                                 | Plan selects affected tooling surface, related subjects/ecosystems, and all discovered descriptors only for descriptor semantics, authoring validation, planning, contracts, build execution, publish execution, or smoke validation impacts; execution-batch manifest, batch evidence bundles, and per-selector evidence/result rows cover the selected executable obligations                                                                                                                                                                                                                                                                                                                         |
| Known global change                                                                                                                                                                                                    | Plan selects scheduled-full-equivalent scope with global provenance and required workflow-release-tooling work groups for every closed tooling surface; execution-batch manifest, batch evidence bundles, and per-selector evidence/result rows cover the selected executable obligations                                                                                                                                                                                                                                                                                                                                                                                                               |
| Scheduled full run                                                                                                                                                                                                     | Plan selects full repository scope with required workflow-release-tooling work groups for every closed tooling surface and scheduled provenance records using `selection-kind: scheduled-full`, empty impact/expansion refs, `scheduled-full-source: true`, and `scheduled-full.enabled: true`; execution-batch manifest, batch evidence bundles, and per-selector evidence/result rows cover the selected executable obligations                                                                                                                                                                                                                                                                       |
| Known non-impacting change with no executable checks                                                                                                                                                                   | Lightweight-only plan passes without heavy work, remains inspectable, has no executable validation work groups, uses a verified empty execution-batch manifest, has no batch evidence bundles, and has terminal aggregate evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| Known non-impacting change with executable lightweight checks                                                                                                                                                          | Verified execution-batch manifest assigns the lightweight selectors, and lightweight work appears as per-selector success evidence/result rows in the assigned batch evidence bundle for pass                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| Known non-impacting lightweight-only plan attempts subject, ecosystem, or descriptor-scoped lightweight work                                                                                                           | The plan is structurally invalid; lightweight-only executable checks must use `lightweight-policy` or workflow-release `tooling-surface` coverage targets rather than implying selected validation subjects                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Confirmed zero-file affected range                                                                                                                                                                                     | Affected request has `affected-range.status: available`, empty `changed-files`, non-null canonical `changed-files-hash`, executable lightweight-only plan with available discovered subjects all marked `not-selected`, an available provider fact snapshot whose provider subject IDs exactly match the provider-bound frozen subject universe, unsupported audit subjects only when they satisfy the unsupported-subject constraints, no selected subjects, no executable validation work groups, no evidence expectations, verified empty execution-batch manifest, no batch evidence bundles or per-selector evidence rows, and passing terminal aggregate evidence after final evidence validation |
| Wrong-run or producer-unverified planner-facing request                                                                                                                                                                | Planning does not trust affected-range or scheduled-full payload claims; it either fails closed with `request-invalid` or emits no authoritative plan unless the request ref, digest, instance count, envelope, and `normalize-input` producer authority verify; authoritative plans and aggregates freeze the verified request ref and digest for replay                                                                                                                                                                                                                                                                                                                                               |
| Missing, duplicate, unreadable, malformed, schema-invalid, producer-unverified, or ref-unidentified planner-facing request                                                                                             | No authoritative validation plan is emitted because the request cannot satisfy the replayable request boundary needed for plan request binding                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Digest-mismatched or wrong-run request that is still replayable enough to freeze request ref and recomputed digest                                                                                                     | Planning may emit a fail-closed plan with `request-invalid`; aggregation replay-verifies the request artifact boundary and preserves the fail-closed diagnostic rather than converting it to `invalid-plan`; the fail-closed handoff has no executable validation work groups, a verified empty execution-batch manifest, no batch evidence bundles, and terminal aggregate evidence                                                                                                                                                                                                                                                                                                                    |
| Execution-batch materialization receives invalid, producer-unverified, identity-unverified, or companion-mismatched plan                                                                                               | `materialize-execution-batches` emits no executable batch set unless it can verify plan identity and all authoritative plan plus companion snapshot checks; fan-out never runs from a plan that has not passed that validation                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Structurally valid fail-closed or no-executable plan                                                                                                                                                                   | No executable validation work groups, exactly one verified empty execution-batch manifest, no batch evidence bundles, and terminal aggregate evidence preserve fail-closed or no-work semantics instead of reporting `invalid-plan`                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| Mixed project/ecosystem/infrastructure/non-impacting change                                                                                                                                                            | Plan unions all selected scopes, descriptor/release-shaped obligations, ecosystem gates, and additive lightweight obligations; execution-batch manifest, batch evidence bundles, and per-selector evidence/result rows cover the selected executable obligations; broader scopes may subsume duplicates only with explicit `classification.subsumptions` records, and non-impacting paths do not replace required heavyweight validation                                                                                                                                                                                                                                                                |
| Multiple independent selection causes for the same subject are subsumed before freezing                                                                                                                                | The retained subject-selection provenance record remains in `classification.subject-selection-provenance`, and `classification.subsumptions` uses `subsumed-kind: subject-selection-provenance` with deterministic candidate provenance IDs                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Policy-bearing planner/classifier/fact-provider change                                                                                                                                                                 | Plan is produced using the validation tree under review, exposes `planner.policy-source: validation-tree` plus verified `planner.execution-tree` provenance, and acceptance rejects evidence planned by a baseline or wrong-tree policy                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| Affected plan omits or invents changed-path impact coverage                                                                                                                                                            | Aggregation emits `invalid-plan` with `diagnostic-detail: changed-files-impact-coverage-mismatch` rather than allowing omitted paths to bypass fail-closed classification                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| PR/push affected range unconfirmed                                                                                                                                                                                     | Request diagnostic `range-unconfirmed`, fail-closed plan, failing aggregation/workflow conclusion, no executable validation work groups, verified empty execution-batch manifest, no batch evidence bundles, and terminal aggregate evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| Project-scoped change with insufficient downstream facts                                                                                                                                                               | Planner diagnostic `dependency-impact-insufficient` or `fact-provider-insufficient`, fail-closed plan, failing aggregation/workflow conclusion, no executable validation work groups, verified empty execution-batch manifest, no batch evidence bundles, and terminal aggregate evidence                                                                                                                                                                                                                                                                                                                                                                                                               |
| Unclassifiable workflow-release infrastructure impact                                                                                                                                                                  | Planner diagnostic `infrastructure-surface-unclassified`, fail-closed plan, failing aggregation/workflow conclusion, no executable validation work groups, verified empty execution-batch manifest, no batch evidence bundles, and terminal aggregate evidence                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Unknown path                                                                                                                                                                                                           | Fail-closed plan, failing aggregation/workflow conclusion, no executable validation work groups, verified empty execution-batch manifest, no batch evidence bundles, and terminal aggregate evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Invalid descriptor blocking derivation                                                                                                                                                                                 | Fail-closed derivation has no executable validation work groups, verified empty execution-batch manifest, no batch evidence bundles, failing terminal aggregate evidence, and failing workflow conclusion; executable descriptor-validation failures are captured in manifest-assigned batch evidence bundles with per-selector failure rows                                                                                                                                                                                                                                                                                                                                                            |
| Duplicate descriptor paths appear in the fact snapshot                                                                                                                                                                 | The fact snapshot is structurally invalid because descriptor obligations, target-catalog entries, and descriptor evidence rows resolve descriptor facts by the globally unique `descriptor-path` key                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Descriptor-validation batch evidence row omits or mismatches its bound descriptor obligation                                                                                                                           | Aggregation treats the batch evidence row as inadmissible with `mismatched-evidence-payload`; descriptor obligation ID, descriptor path/identity/owner/source, and descriptor scope must match the frozen plan and fact snapshot exactly                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| Missing, unreadable, malformed, schema-invalid, duplicate, producer-unverified, or digest-mismatched validation plan                                                                                                   | Aggregation emits a failed `invalid-plan` aggregate with the applicable plan diagnostic detail, unverified plan-derived fields set to `null` or `unknown`, empty evidence results, zero executable counts, and no batch-bundle admissibility authority                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Structurally invalid but schema/digest-valid validation plan                                                                                                                                                           | Aggregation emits `invalid-plan` with `diagnostic-detail: structurally-invalid`, empty evidence results, zero executable counts, and no batch-bundle admissibility authority                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| Release-shaped artifact obligation references a mismatched subject, descriptor owner, validation obligation, work group, ecosystem, or runner                                                                          | The plan is structurally invalid because artifact validation must bind to the selected descriptor-backed subject and its digest-bound descriptor and target-catalog facts                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| Work group has a missing or mismatched ecosystem for ecosystem-specific execution                                                                                                                                      | The plan is structurally invalid because runner and command selection consume the frozen work-group ecosystem rather than rediscovering it during execution                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Missing, unexpected, duplicate, producer-unverified, malformed, ref-mismatched, noncanonical, or digest-mismatched companion planning snapshot                                                                         | Aggregation rejects the otherwise readable plan as `invalid-plan` with the applicable changed-files or fact-snapshot diagnostic detail                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Validation obligation references a mismatched or shared work group/evidence expectation                                                                                                                                | The plan is structurally invalid unless duplicate candidates were removed before freezing and represented only by explicit subsumption records                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Executable work group or evidence expectation is not referenced by its required source obligation chain                                                                                                                | The plan is structurally invalid; every executable validation selector is verdict-relevant and must be bound to the matching validation, descriptor, or artifact obligation contract                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Missing, duplicate, producer-unverified, plan-mismatched, budget-overflowing, unmaterializable, or structurally invalid execution-batch manifest                                                                       | Aggregation emits `invalid-plan` rather than admitting batch evidence bundles                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| Logical boundary mapped to missing or wrong platform job identity                                                                                                                                                      | Producer authority verification rejects the artifact as producer-unverified using the boundary identity map; payload producer claims, logs, and job conclusions do not substitute                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| Logical contract artifact ref maps ambiguously or incorrectly to a physical artifact name                                                                                                                              | Artifact instance counting, duplicate detection, and namespace enumeration use the canonical fixed-length SHA-256 physical-name mapping; prefixed artifacts whose payload refs do not recompute to the observed physical name are non-authoritative or unexpected in closed namespaces                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Prefixed physical artifact does not match any expected non-bundle contract ref during evidence namespace closure                                                                                                       | Aggregation treats it as unexpected evidence with `artifact-ref: null`; expected non-bundle artifacts are classified first and handled only by their own contract rules                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| Batch evidence bundle has missing or mismatched validation-grade writer provenance                                                                                                                                     | Aggregation treats the bundle as inadmissible; batch writer identity is verified from manifest or bundle metadata and platform provenance, without a separate writer-observation artifact                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| Request payload artifact ref or physical artifact name mismatches the contract-owned request ref                                                                                                                       | Planning fails closed with `request-invalid` and `diagnostic-detail: request-ref-mismatch`, or emits no authoritative plan                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| Aggregate cannot replay-verify the request artifact ref or digest frozen into an otherwise structurally valid plan                                                                                                     | Aggregation emits `invalid-plan`; copied plan semantics are insufficient without the authoritative normalized request artifact identity and digest                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| Missing batch evidence bundle or per-selector evidence row                                                                                                                                                             | Aggregation fails with `required-evidence-missing` and identifies the missing work group or evidence expectation                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| Planned validation work skipped or failed                                                                                                                                                                              | Verified execution-batch manifest assigns the selectors, batch evidence bundles contain per-selector skipped or failed evidence/result rows, and aggregation records `required-evidence-skipped` or `blocking-validation-failure` and fails the final verdict; planned executable validation work is not optional or non-gating                                                                                                                                                                                                                                                                                                                                                                         |
| Ecosystem gate omits a capability enabled by selected subject/provider facts                                                                                                                                           | Plan is fail-closed with `fact-provider-insufficient` or structurally invalid; batch evidence cannot pass by matching an under-planned capability set                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Upstream selector emits a valid `blocking-failure` batch evidence row                                                                                                                                                  | The batch can still write evidence for dependency gating, downstream selectors are not dependency-blocked solely by that validation outcome, and aggregation fails the final verdict from the batch evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| Batch evidence row emitted after validation on the wrong or unverifiable execution tree                                                                                                                                | Aggregation treats the batch evidence row as inadmissible with `mismatched-evidence-payload`; copied plan provenance is insufficient without execution-tree evidence from the execution-batch boundary                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Legacy receipt-like artifact appears in the compatibility intake namespace                                                                                                                                             | Pending Group 4, receipt-like artifact handling is compatibility/diagnostic placeholder behavior and does not replace the current execution-batch manifest and batch evidence bundle requirements                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| Aggregate mirrors an unreadable or unclassified legacy receipt-like artifact                                                                                                                                           | Pending Group 4, `observed-receipts` compatibility fields may record the manifest entry for diagnostics, but current Group 3 acceptance relies on batch evidence bundles                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| Release-shaped artifact batch evidence row with empty, partial, extra, or unavailable expected artifact coverage, missing artifact digest, unchecked logical release-shaped receipt check, or mismatched planned shape | Aggregation records `artifact-shape-unconfirmed` or `mismatched-evidence-payload` and fails the final verdict                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| Dependency-blocked release-shaped artifact batch evidence row                                                                                                                                                          | The batch evidence row may use the explicit skipped form with empty observed artifact refs and digests plus `validation-work-skipped: dependency-blocked`; aggregation treats it as required evidence skipped and fails the final verdict, not as successful artifact-shape validation                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Legacy receipt compatibility row has top-level artifact refs populated for non-artifact evidence or differing from release-shaped observed refs                                                                        | Pending Group 4, this remains a compatibility placeholder; current batch evidence rows must keep artifact refs aligned with the category-specific release-shaped observed refs                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Lightweight-preflight or workflow-release-tooling batch evidence row omits or mismatches its required detail profile or subcheck results                                                                               | Aggregation treats the batch evidence row as inadmissible with `mismatched-evidence-payload`; category-result detail must match the frozen work group, evidence expectation, and profile subcheck contract                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| Invalid or mismatched batch evidence row or bundle                                                                                                                                                                     | Inadmissible batch evidence does not satisfy required evidence; aggregation fails with the applicable inadmissibility reason, and also `required-evidence-missing` when no valid matching batch evidence exists                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| Forged or producer-unverified batch writer metadata                                                                                                                                                                    | Matching payload fields are insufficient; aggregation treats the batch evidence bundle as inadmissible and fails the final verdict                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| Valid required batch evidence plus extra inadmissible batch evidence                                                                                                                                                   | Required evidence is satisfied by the valid batch evidence, but aggregation still fails for the extra malformed, duplicate, unexpected, wrong-plan, unknown-work-group, or mismatched-work-group evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| Batch evidence bundle appears after evidence namespace closure                                                                                                                                                         | Post-run acceptance or same-attempt retry treats final evidence as non-authoritative rather than extending the closed evidence set                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| Same-attempt finalization retry with occupied final manifest or aggregate                                                                                                                                              | Aggregation preserves the occupied artifact's `created-at` while recomputing raw digest equality; digest mismatch or duplicate final artifact leaves no authoritative final evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Aggregate exists at the final ref but the final manifest is missing                                                                                                                                                    | Same-attempt retry treats the final state as non-recoverable and non-authoritative; it does not recreate a manifest to satisfy the aggregate's existing final-manifest content-digest claim                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Final manifest or aggregate finalization/reconciliation fails despite a passing computed validation verdict                                                                                                            | The aggregate records `reason.final-evidence-failure` and `failure-kind: final-evidence-failure` when it can be written; otherwise the `aggregate-evidence` job and final required check fail under the no-authoritative-final-evidence contract                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| Missing, duplicate, malformed, wrong-run, wrong-producer, or mutually mismatched final manifest or aggregate                                                                                                           | Post-run acceptance treats the final evidence as non-authoritative; logs, job conclusions, or auxiliary artifacts cannot replace the exact contract-owned manifest and aggregate artifacts                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| Final manifest or aggregate JSON is not RFC 8785 canonical UTF-8 JSON                                                                                                                                                  | Final evidence is malformed/non-authoritative or records `final-evidence-failure`; semantic JSON equivalence is insufficient for final digest replay                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Unknown verdict-relevant diagnostic family or detail appears in contract evidence                                                                                                                                      | Schema or aggregation rejects it under the closed `v1alpha1` diagnostic vocabulary unless the LLD/schema/api-version contract has been updated                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Unconfirmed artifact shape                                                                                                                                                                                             | Blocking validation failure, no release-proof admissibility                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Unconfirmed PR context                                                                                                                                                                                                 | No publication credentials, release environment, or OIDC publish permission exposed                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| Accidental publication or remote publish-state validation                                                                                                                                                              | Static workflow/config/code review and batch-evidence/aggregate inspection show no work group, command output, batch evidence field, aggregate field, registry query, GitHub Release lookup, tag lookup, or remote publish-state observation is used as validation evidence                                                                                                                                                                                                                                                                                                                                                                                                                             |
| All CI validation modes have no configured publication authority                                                                                                                                                       | Static workflow/config/code review covers `pull_request`, `push`, and `scheduled_full`; no publication credentials, OIDC publish permission, release environment, registry mutation, GitHub Release mutation, or release tag mutation is configured                                                                                                                                                                                                                                                                                                                                                                                                                                                     |

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
  execution-batch manifests, batch evidence bundles, receipt compatibility
  artifacts, and final manifests;
- batching strategy for work-group selectors;
- exact HK profile names and step ordering;
- internal test organization.

The following are not implementation-owned:

- CI belonging to workflow-release rather than a separate CI truth;
- validation plan, execution-batch manifest, batch evidence bundle, and receipt
  compatibility `api-version`/`kind` families;
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
- contract-owned receipt compatibility artifact refs, intake namespace, and
  aggregate manifest location pending Group 4 replacement.

## 19. Outcome

This low-level design gives the implementer a concrete handoff baseline for CI
affected validation without prescribing code internals. It freezes the workflow
entry boundary, logical job sequence, request and plan files, subject snapshot
shape, fact-provider realization, semantic path classification families, scope
resolution rules, work-group selectors, execution mapping, artifact-validation
obligations, validation-only receipts, diagnostics, HK relationship, and
acceptance evidence. Concrete code structure and command implementation remain
owned by the senior engineer as long as these contracts are preserved.
