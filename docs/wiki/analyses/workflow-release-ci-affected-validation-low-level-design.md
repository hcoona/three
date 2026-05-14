# Workflow Release CI Affected Validation Low-Level Design

## 1. Document Governance and Handoff Boundary

Status: this page is the low-level design handoff baseline for implementing CI
affected validation as a workflow-release entry point. It consumes the locked
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

| Area               | Low-level decision                                                                                                                                                                                                        |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Workflow shape     | Add one top-level CI validation entry workflow with `pull_request`, `push`, and `schedule` triggers, plus reusable internal validation units only if implementation benefits from them.                                   |
| Plan format        | Emit one UTF-8 JSON validation plan with stable `api-version`, `kind`, `plan-id`, `mode`, provenance, classification, subject universe, obligations, work groups, evidence expectations, diagnostics, and verdict intent. |
| Fail-closed        | Emit an inspectable fail-closed plan artifact and diagnostics, but the run conclusion must fail and no validation work groups execute.                                                                                    |
| Subject universe   | Include discovered validation subjects with selected/excluded status, not only selected subjects.                                                                                                                         |
| Classification     | Use a conservative ordered rule table: unknown/unclassifiable always fail closed; broad expansion only applies to recognized global, ecosystem, or infrastructure categories.                                             |
| Downstream closure | Use ecosystem-provided dependency facts when sufficient for downstream closure; otherwise fail closed.                                                                                                                    |
| Execution handoff  | Fan out from plan work-group selectors; post-planning jobs must not reclassify changes, rediscover subjects, or alter obligations.                                                                                        |
| Receipts/evidence  | Emit validation-only JSON receipts per executable work group plus one aggregation report; all evidence is inadmissible as release immutable proof.                                                                        |
| Credentials        | No publication credentials, release approvals, OIDC publish permissions, registry mutation, GitHub Release mutation, or release-tag mutation in CI validation.                                                            |
| Runners/tools      | Preserve .NET on Windows, Python and JavaScript/TypeScript on Ubuntu when applicable, and prefer `mise` for tool provisioning.                                                                                            |
| HK                 | Provide planner-aligned lightweight preflight only; HK output is local feedback, not CI evidence.                                                                                                                         |
| Acceptance         | Trace acceptance to plan artifacts, selected scopes, work-group receipts, failure verdicts, and no-publication boundaries.                                                                                                |

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
- Descriptor-backed projects validate release-shaped artifacts and receipts for
  the union of artifacts required by all declared profiles, without publication
  side effects.
- Validation-only subjects participate in validation but never become publish
  subjects.
- CI evidence and release immutable proof are strictly separated.
- Policy-bearing CI planning changes may be planned by the validation-tree policy
  being reviewed, but the run still receives no release credentials or publication
  authority.

## 4. Workflow and Job Boundary

### 4.1 Workflow Identity

The CI validation entry point should be one checked-in workflow file:

| File                                | Trigger shape                      | Stable responsibility                                                                                                                            |
| ----------------------------------- | ---------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| `.github/workflows/ci-validate.yml` | `pull_request`, `push`, `schedule` | Normalize CI event input, run planning, fan out validation work groups, aggregate validation-only evidence, and publish inspectable diagnostics. |

The workflow filename is a repository contract because branch protection and
operator documentation may refer to CI check names. Unlike release publication
workflows, it is not a trusted-publisher identity and must not be configured in
external registry policies.

The workflow `name` and the final required GitHub check context are also
repository contracts:

- workflow `name`: `CI Validation`;
- required final check context: `CI Validation / aggregate-evidence`.

Branch protection for this CI gate must bind to the final aggregate check
context, not to fan-out validation job names or auxiliary checks. If the concrete
GitHub Actions job topology changes, the implementation must preserve that final
check context or update branch protection and this design together.

Implementation may introduce reusable internal workflow files or composite
actions for validation work groups. Those internal files are implementation-owned
unless later branch protection or external policy starts depending on them.

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
3. **`materialize-work-groups`**
    - reads the validation plan;
    - materializes execution selectors from plan work groups;
    - emits the selector-assignment manifest that binds each executable selector
      to its authorized receipt writer;
    - produces an empty selector set for fail-closed plans and lightweight-only
      plans with no executable lightweight obligations.
4. **Validation work-group fan-out**
    - runs executable work groups by selector;
    - emits one validation-only receipt per work group;
    - records writer observations for uploaded receipt artifact instances using
      control-plane job context, outside receipt payload authority;
    - never changes planned scope or obligations.
5. **`aggregate-evidence`**
    - runs after planning and selector materialization are attempted, even when a
      prior logical job fails to produce a readable plan or selector set;
    - verifies selector assignments before admitting any receipt;
    - verifies expected receipts;
    - treats missing, unreadable, invalid, or unmaterializable plans as
      `invalid-plan` with no executable selectors;
    - computes the CI validation verdict;
    - emits one aggregation report at the contract-owned ref;
    - fails the workflow when the aggregated validation outcome fails.

These job names are logical handoff names. The implementer may map them to
concrete job identifiers, reusable workflows, or grouped jobs, provided the
sequence, authority boundary, evidence semantics, and final required check
context remain intact.

Logical handoff names are also producer-authority boundaries. The workflow
contract must define a boundary identity map before execution that maps each
logical boundary (`normalize-input`, `plan`, `materialize-work-groups`, trusted
receipt/observation boundaries, and `aggregate-evidence`) to the allowed GitHub
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
selector materialization, receipt admission, or final acceptance must be tied to
the expected logical boundary by non-payload control-plane evidence for the same
`run-id` and `run-attempt`. The allowed producer-authority signals are:

| Artifact class                                             | Required producer boundary        | Allowed non-payload authority signals                                                                                                                                               |
| ---------------------------------------------------------- | --------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Planner-facing CI request                                  | `normalize-input`                 | Contract-owned request ref and instance count plus immutable workflow/job context proving the artifact was uploaded by the input-normalization control-plane boundary               |
| Validation plan, changed-files snapshot, and fact snapshot | `plan`                            | Contract-owned artifact ref and instance count plus immutable workflow/job context proving the artifact was uploaded by the planning control-plane boundary for this run attempt    |
| Selector-assignment manifest                               | `materialize-work-groups`         | Contract-owned artifact ref and instance count plus immutable workflow/job context proving the artifact was uploaded by the selector-materialization control-plane boundary         |
| CI validation receipt                                      | Assigned trusted receipt boundary | Selector assignment, contract-owned receipt ref, artifact instance ID, and matching writer-observation record derived from immutable job context rather than receipt payload fields |
| Writer-observation record                                  | Trusted observation boundary      | Contract-owned observation ref and instance count plus immutable workflow/job context proving the record was emitted by the observation boundary after receipt upload               |
| Receipt manifest and aggregate                             | `aggregate-evidence`              | Contract-owned final refs and instance counts plus immutable workflow/job context proving final artifacts were uploaded by the aggregation control-plane boundary                   |

The immutable workflow/job context must match the boundary identity map from
section 4.2 for the required producer boundary. If the workflow platform or
control-plane wrapper cannot expose those signals for an artifact class, or the
observed platform identity is not allowed by the boundary identity map, that
artifact is producer-unverified. Producer-unverified planning artifacts make the
plan invalid, producer-unverified receipts or writer-observations are
inadmissible, and producer-unverified final manifest or aggregate artifacts are
not authoritative acceptance evidence. Payload fields, artifact names supplied by
executable validation commands, logs, and job conclusions never prove producer
authority.

`artifact-ref` values in this document are logical contract refs, not physical
GitHub artifact names. Every logical ref is encoded to one physical artifact name
with this reversible mapping:

```text
physical-artifact-name = "three-ci-validation-" + base64url_no_padding(utf8(logical-artifact-ref))
```

The decoded logical ref must exactly equal the payload `artifact-ref` field for
artifact classes that carry one. Artifact instance counting, duplicate detection,
namespace enumeration, producer checks, and replay binding operate on physical
artifact instances whose names decode to the expected logical contract ref or
logical ref prefix. A physical artifact name that does not decode, decodes to a
non-canonical logical ref, or decodes to a ref outside the expected namespace is
non-authoritative for that contract boundary even if its payload fields resemble
valid evidence.

`schema-diagnostics` uses the same shape as `diagnostic-record`, sorted by
`diagnostic-id`. These diagnostics are producer-side schema or compatibility
warnings only: every entry must have `severity: warning` or `info` and
`verdict-effect: none`, and aggregation must not treat them as validation
failures. Schema diagnostics that indicate an artifact is unreadable, malformed,
schema-invalid, or structurally invalid must instead be represented by the
artifact-specific planner, receipt, or aggregation diagnostic paths defined
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
  emit no authoritative plan rather than trusting a payload self-claim.
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
  validation work groups, no evidence expectations, and only the terminal
  `evidence-aggregation` work group. Aggregation passes only after validating the
  plan, empty changed-files companion snapshot, non-null `changed-files-hash`, and
  final manifest/aggregate evidence; no validation receipt is required or
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
that authoritative ref makes aggregation emit `invalid-plan`. Plan artifacts
outside that ref are non-authoritative auxiliary artifacts and must not be used
for selector materialization or aggregation.
Selector materialization and aggregation must verify the validation-plan
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

`plan-id` is an opaque run-scoped stable identifier assigned by the control
plane. It is not a content digest and must not be derived from a representation
that includes itself. `plan-digest` is the lowercase hexadecimal SHA-256 digest
of the RFC 8785 JSON Canonicalization Scheme canonical UTF-8 bytes for the
frozen validation plan after removing only the root-level `plan-digest` member.
It must match `^[0-9a-f]{64}$`. The plan payload must be I-JSON compatible for
digesting; duplicate object member names make it malformed. All remaining
fields, including nulls, false values, empty arrays or objects, diagnostics,
obligations, work groups, and evidence expectations, participate in the digest.
Array order is preserved. Receipts and aggregation evidence bind to the frozen
plan with both `plan-id` and `plan-digest`.

The `plan` boundary must observe its checkout tree before running planner logic
and bind that observation into `planner.execution-tree`. For authoritative plans,
`planner.policy-source` is `validation-tree`,
`planner.execution-tree.observed-commit-sha` equals the frozen
`validation-tree.commit-sha`, `planner.execution-tree.source` is `plan-boundary`,
and `planner.execution-tree.verified` is `true`. The planner implementation must
not self-attest this value from a command payload after planning; it is
control-plane provenance captured by the trusted planning boundary. Selector
materialization, aggregation, and acceptance reject missing, unverifiable, or
mismatched planner execution-tree evidence as `invalid-plan` because
policy-bearing changes must be planned by the validation tree under review.

Before computing `plan-digest`, the planner emits arrays in canonical order:

| Array family                                                                   | Canonical order                                                            |
| ------------------------------------------------------------------------------ | -------------------------------------------------------------------------- |
| Identifier-bearing records                                                     | Ascending by the record identifier field in UTF-8 byte lexicographic order |
| `source-impact-ids`, `source-expansion-ids`, references, paths, and string IDs | Ascending UTF-8 byte lexicographic order                                   |
| `planned-capabilities`                                                         | Declared capability order: build, test, lint, format, type-check           |
| Capability result arrays                                                       | Declared capability order                                                  |
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
diagnostics: [planner-diagnostic]
```

Fail-closed plans still contain envelope, classification, diagnostics, and enough
provenance to inspect why no executable validation plan was authorized. They have
no executable validation work groups. Every emitted plan, including fail-closed
plans, must satisfy the schema and structural identity/reference rules in this
document. Fail-closed plans must leave descriptor, validation, artifact, and
evidence-expectation sections empty. Their `work-groups` section is empty except
for the single non-executable terminal `evidence-aggregation` work group that
emits the failed aggregate verdict. Inspectability is carried by classification,
snapshot status, provenance fields, and diagnostics instead of non-executable
obligation records.
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
must be derivable from those fact records. For a required artifact obligation,
`expected-artifact-refs` is the complete validation-only artifact coverage the
work group must materialize or inspect for that obligation, must be non-empty,
and must not identify publication targets or remote release state. Missing,
partial, empty, or mismatched descriptor or target-catalog fact backing makes the
plan structurally invalid.

Each `evidence-expectation` has:

```yaml
evidence-expectation-id: string
work-group-id: string
coverage-target:
    type: subject | ecosystem | descriptor | tooling-surface | artifact-obligation | lightweight-policy
    id: string
category: lightweight-preflight | ecosystem-gate | descriptor-validation | release-shaped-artifact | workflow-release-tooling
planned-capabilities: [build | test | lint | format | type-check] | null
required: boolean
blocking-if-missing: boolean
```

Each `subsumption-record` has:

```yaml
subsumption-id: string
source-impact-ids: [string]
source-expansion-ids: [string]
subsumed-kind: descriptor-obligation | validation-obligation | artifact-obligation | work-group | evidence-expectation
subsumed-candidate-ids: [string]
retained-id: string
reason: string
```

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
  `evidence-expectation-id`, `subsumption-id`, and `diagnostic-id`.
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
  receipt expectation. Receipt-to-expectation matching is therefore defined by
  `plan-id` and `work-group-id`.
- For each executable work group and its evidence expectation, `coverage-target`,
  evidence `category`, `planned-capabilities`, and required/blocking semantics
  must match exactly. A mismatch between the duplicated work-group
  `expected-evidence` contract and the `evidence-expectation` record makes the
  plan structurally invalid.
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
  expectation. Execution must not rederive artifact shape from descriptors.
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
- Explicit repository rules may adjust `activity-status` or `exclusion.reason`
  for discovered candidates, but they are not an inclusion authority for adding
  validation subjects.
- `capability-class: validation-only` subjects cannot have publish obligations.
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
surfaces, not `validation-subject-snapshot` records. For every subject with
`activity-status: active` and `selection-status: selected` in an executable plan,
the fact snapshot must contain exactly one `status: available` provider entry
that lists the subject ID and supports that subject's ecosystem: `dotnet` for
`.NET`, `python` for Python, and `javascript-typescript` for JavaScript or
TypeScript. For structurally valid fail-closed plans, selected subjects may be
listed by `status: unavailable` provider entries only when fail-closed
diagnostics explain why provider facts could not be produced or confirmed.
Descriptor metadata emitted by the `workflow-release` provider for
ecosystem-owned descriptor-backed subjects is digest-bound provider data in
`descriptors` and must not list those subjects in `provider.subjects`. Missing
provider coverage, duplicate provider coverage for the same selected subject,
unavailable provider coverage in an executable plan, or an ecosystem/provider
mismatch makes planning fail closed with `fact-provider-insufficient`.

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
- relevant workflow-release tooling validation.

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
ecosystem: string | null
runner-family: windows | ubuntu
selector-variant: string | null
depends-on: [work-group-id]
expected-evidence:
    category: lightweight-preflight | ecosystem-gate | descriptor-validation | release-shaped-artifact | workflow-release-tooling
    planned-capabilities: [build | test | lint | format | type-check] | null
    required: boolean
```

Executable work groups must have a non-null `runner-family` in the frozen plan.
Runner assignment is plan authority: execution must not select a runner by
rediscovering subject ecosystem, descriptor contents, or tooling policy after
planning. If the planner cannot determine a required executable work group's
runner family from the closed mapping in section 12, planning fails closed or the
plan is structurally invalid rather than emitting `runner-family: null`.
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
- `depends-on` is an execution gate based on observable upstream job outcome and
  receipt presence, not final receipt admissibility. A dependent work group may
  execute validation only after every dependency's concrete job completed
  successfully and uploaded a receipt artifact at its expected ref. If a
  dependency job failed, was cancelled, was skipped, or did not upload the
  expected receipt, the dependent work group is dependency-blocked: it must not
  run its category-specific validation and must still emit its own receipt with
  `outcome: skipped`, empty artifact refs, and a diagnostic explaining the
  blocking dependency when workflow construction can still run that dependent
  selector. Terminal aggregation remains the only authority for final receipt
  admissibility and may later mark either the dependency or dependent receipt
  inadmissible. Aggregation treats an admissible dependency-blocked receipt as
  `required-evidence-skipped`; if the dependency-blocked receipt is also missing,
  aggregation reports `required-evidence-missing`.
- Receipt-emitting executable work-group jobs separate validation outcome from
  control-plane/job infrastructure outcome. A category-specific validation
  failure that is successfully captured in a contract-owned receipt with
  `outcome: blocking-failure` still lets the concrete receipt-emitting job
  complete successfully for dependency gating; final failure is expressed by
  aggregation, not by the selector job conclusion. The concrete job fails only
  when the trusted receipt boundary cannot evaluate the planned contract, cannot
  verify required control-plane inputs such as the execution tree, cannot upload
  the expected receipt and writer observation, or otherwise cannot produce
  contract evidence for that selector. Downstream selectors are
  dependency-blocked only by those control-plane/job failures, cancellation,
  skipping, or missing expected receipt artifacts, not by an upstream
  validation-failure receipt that was emitted successfully.
- The control plane may batch multiple executable selectors into one concrete job
  only when the resulting receipts still report each required selector
  separately. It must not batch selectors connected by a `depends-on` edge in the
  same concrete job unless the concrete job enforces the same intra-job ordering,
  receipt upload, and dependency-blocked receipt behavior before starting the
  dependent selector's validation.
- `materialize-work-groups` emits a selector-assignment manifest at the
  contract-owned ref
  `ci-validation/assignments/<run-id>/<run-attempt>/selector-assignments.json`.
  It has:

    ```yaml
    common-envelope: inherited
    api-version: three.ci.validation.selector-assignments/v1alpha1
    kind: ci-validation-selector-assignments
    plan-id: string
    plan-digest: string
    assignments:
        - assignment-id: string
          work-group-id: string
          trusted-writer-id: string
          writer-identity-source: github-actions-job-context
          receipt-artifact-ref: string
          writer-observation-ref: string
    ```

    `assignment-id` is stable within the run attempt, derived from
    `work-group-id`, and artifact-ref-bearing because it appears in
    `writer-observation-ref`. It must match the same path-safe grammar as
    `work-group-id`, `^[a-z0-9][a-z0-9._-]{0,127}$`. The default assignment ID is
    `work-group-id`; if a future assignment strategy needs a different value, it
    must use a normalized readable prefix plus a lowercase SHA-256 digest of the
    typed assignment identity rather than embedding raw coverage targets or path
    separators. `trusted-writer-id` is the normalized control-plane job or
    matrix leg identity authorized to upload the receipt for that selector.
    `writer-identity-source` declares the non-payload source aggregation must use
    to observe that identity: immutable GitHub Actions job context captured by the
    control plane before receipt upload. The writer identity preimage is the RFC
    8785 canonical JSON bytes of:

    ```json
    {
        "api-version": "three.ci.validation.writer-id/v1alpha1",
        "workflow": "workflow-name",
        "job": "job-id",
        "matrix": {}
    }
    ```

    `workflow` is the common-envelope `run.workflow`; `job` is the stable
    concrete GitHub Actions job identifier or reusable-workflow call-site job
    identifier that owns the upload; and `matrix` is the canonical matrix object
    for the leg, or `{}` when absent. `trusted-writer-id` and
    `observed-writer-id` are both `github-actions-job:` followed by the lowercase
    SHA-256 digest of that preimage, and must match
    `^github-actions-job:[0-9a-f]{64}$`. Receipt payload fields, artifact path
    segments, wrapper records, and log text are never identity sources.
    `receipt-artifact-ref` must equal the contract-owned receipt ref derived from
    the work group. `writer-observation-ref` must equal
    `ci-validation/writer-observations/<run-id>/<run-attempt>/<assignment-id>.json`.
    Assignment entries are sorted by `work-group-id`; duplicate work groups,
    duplicate receipt refs, duplicate writer-observation refs, or mismatches with
    the frozen plan make selector materialization invalid. Exactly one selector
    assignment manifest artifact instance must exist at the authoritative ref;
    zero or multiple instances make the plan invalid for aggregation.
    `materialize-work-groups` must perform the same authoritative plan validation
    needed to safely fan out executable selectors before emitting any assignment
    manifest: plan ref and instance count, producer authority, envelope, schema,
    digest, structural reference rules, executable/fail-closed invariants, and
    required companion changed-files and fact snapshot artifacts. If the plan
    identity cannot be verified, or any of those checks fails, it emits no
    selector-assignment manifest and no executable selectors. A structurally valid
    fail-closed plan, or a valid lightweight-only plan with no executable work
    groups, must emit exactly one empty selector-assignment manifest with the
    verified `plan-id` and `plan-digest`; this keeps later aggregation in the
    intended fail-closed or no-work path rather than treating the missing manifest
    as `invalid-plan`. Validation work-group fan-out must be contingent on a
    non-empty selector-assignment manifest produced after those checks; it must
    not run executable selectors from a plan that only aggregation later discovers
    is invalid.
    Aggregation must verify the selector-assignment artifact's producer authority
    from platform/control-plane metadata before trusting its payload because the
    manifest authorizes receipt writers. A selector-assignment manifest authored
    outside `materialize-work-groups`, by an executable validation command, or by
    an unverified artifact instance is not authority even if its payload matches.
    A missing, duplicate, unreadable, malformed, schema-invalid, plan-mismatched,
    producer-unverified, or structurally invalid selector-assignment manifest
    makes the plan invalid for aggregation and produces an `invalid-plan`
    aggregate with a selector-assignment diagnostic detail.

- Receipt and writer-observation emission require a trusted receipt boundary
  inside each executable selector. Category-specific validation commands may
  produce provisional result material for the selector, but they must not have
  authority to write contract-owned receipt artifacts, selector assignments,
  writer observations, manifests, or aggregates. A control-plane wrapper for the
  selector evaluates the command outcome against the planned evidence contract
  and is the only authority allowed to emit the selector's
  `ci-validation-receipt`. Contract-owned receipt payloads must be derived from
  that wrapper-controlled boundary, not from untrusted command-authored JSON.
  Auxiliary logs or command outputs are never receipt authority.

- The control plane emits a contract-owned writer-observation record from a
  trusted observation boundary after the receipt artifact has a stable
  `artifact-instance-id` and before aggregation admits that receipt:

    ```yaml
    common-envelope: inherited
    api-version: three.ci.validation.writer-observation/v1alpha1
    kind: ci-validation-writer-observation
    plan-id: string
    plan-digest: string
    assignment-id: string
    work-group-id: string
    receipt-artifact-ref: string
    artifact-instance-id: string
    writer-identity-source: github-actions-job-context
    observed-writer-id: string
    ```

    The writer-observation artifact ref is exactly the assignment's
    `writer-observation-ref`. Exactly one writer-observation artifact instance
    must exist at that ref for the observed receipt; zero or multiple instances
    make the corresponding receipt inadmissible with
    `mismatched-writer-identity`. `observed-writer-id` is captured from immutable
    GitHub Actions job context outside the receipt payload and computed from the
    same writer identity preimage as `trusted-writer-id`. The executable writer
    does not supply `artifact-instance-id` or self-attest the observed writer
    identity; the trusted observation boundary binds the already-uploaded receipt
    artifact instance to the previously captured job context.

    Aggregation must verify the observation artifact's producer authority from
    platform/control-plane metadata before trusting its payload. A
    writer-observation record authored by the executable validation command, by a
    receipt writer outside the trusted observation boundary, or by an unverified
    artifact instance is not authority even if its payload fields match.
    Aggregation verifies the observation record's artifact instance count,
    producer authority, envelope, schema, plan identity, assignment, receipt ref,
    `artifact-instance-id`, writer source, and observed writer ID before admitting
    the receipt. Missing, malformed, mismatched, duplicate, wrong-plan, or
    producer-unverified writer-observation records make the corresponding receipt
    inadmissible with `mismatched-writer-identity`.

- One `ecosystem-gate` selector covers the complete planned capability set for
  its coverage target. The work group, matching evidence expectation, and receipt
  record that set so build, test, lint, format, and type-check outcomes do not
  collapse into an opaque gate result.
  `expected-evidence.planned-capabilities` is derived from the frozen selected
  subject capabilities using the section 8 provider-fact rules. It must be
  non-empty for executable ecosystem gates, sorted in canonical capability order
  `build`, `test`, `lint`, `format`, `type-check`, and identical to the matching
  `evidence-expectation.planned-capabilities`. A subject-scoped gate uses the
  selected active subject's enabled capabilities. An ecosystem-scoped gate uses
  the union of enabled capabilities across all selected active subjects in that
  ecosystem. A selected subject with no enabled validation capabilities does not
  create an executable ecosystem gate by itself; descriptor/release-shaped
  obligations carry their own evidence. Missing provider capability facts for any
  selected subject in the gate scope make the plan fail closed rather than
  silently omitting that capability from the gate.
- `coverage-target.type: ecosystem` is valid only for ecosystem-level
  `ecosystem-gate` selectors. If ecosystem gates are decomposed into subject
  selectors, the plan must preserve the ecosystem parent through source impacts
  or scheduled-full provenance.
- Fail-closed plans contain no executable validation work groups and exactly one
  terminal `evidence-aggregation` work group needed to emit the failed aggregate
  verdict. The fail-closed aggregation work group has no receipt expectation and
  an empty `depends-on` list.
- Lightweight-only plans may contain no executable work groups, or may contain
  lightweight-preflight work groups that must produce evidence before the run can
  pass.
- `lightweight-preflight` work groups for known-non-impacting changes use
  `coverage-target.type: lightweight-policy` and
  `coverage-target.id: known-non-impacting` unless they naturally bind to a more
  specific subject, ecosystem, or tooling surface.
- The `evidence-aggregation` work group is a non-executable terminal
  control-plane selector. Its completion boundary includes every planned
  executable work group that can emit into the closed receipt boundary. All such
  work groups are verdict-relevant under this design. Aggregation reads the plan
  and receipts, emits the aggregate verdict artifact, and does not produce a
  work-group receipt.
- The terminal `evidence-aggregation` work group must be downstream of every
  executable work group, either by direct `depends-on` references or by the
  transitive dependency graph. A fail-closed plan has no executable work groups,
  so its terminal aggregation work group is terminal with empty `depends-on`. A
  plan whose dependencies do not make aggregation terminal is structurally
  invalid.

## 12. Execution Mapping

The planner maps work groups to runner families:

| Work group kind                                               | Default runner family                                                                                   | Notes                                                                       |
| ------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| `lightweight-preflight`                                       | Ubuntu                                                                                                  | May run documentation, formatting, or policy checks when lightweight enough |
| `ecosystem-gate` for .NET                                     | Windows                                                                                                 | Preserves .NET runner expectation                                           |
| `ecosystem-gate` for Python                                   | Ubuntu                                                                                                  | Uses repository tool provisioning convention                                |
| `ecosystem-gate` for JavaScript/TypeScript                    | Ubuntu                                                                                                  | Uses repository tool provisioning convention                                |
| `descriptor-validation`                                       | Ubuntu, or the subject ecosystem runner when descriptor validation requires ecosystem-specific evidence | Must not publish or mutate release state                                    |
| `release-shaped-artifact` for .NET                            | Windows                                                                                                 | Produces validation-only artifact receipts                                  |
| `release-shaped-artifact` for Python or JavaScript/TypeScript | Ubuntu                                                                                                  | Produces validation-only artifact receipts                                  |
| `workflow-release-tooling`                                    | Ubuntu, or Windows when the affected tooling surface requires Windows-only evidence                     | May fan out to related ecosystem runners when scope requires                |
| `evidence-aggregation`                                        | Ubuntu                                                                                                  | Terminal control-plane aggregation; emits aggregate verdict artifact        |

The planner applies this table before freezing work groups and records the
result in each executable selector. "Requires ecosystem runner" means the
descriptor validation must run with the same runner family as the selected
subject or artifact-producing ecosystem; mixed requirements are represented by
separate work groups with distinct non-null `selector-variant` values. "Requires
Windows-only evidence" means the affected tooling surface validates
Windows-specific .NET/build behavior. All other
workflow-release-tooling work groups use Ubuntu. All runners provision tools
through `mise` where practical. The concrete command lines and helper scripts are
implementation-owned, but they must run the repository's existing ecosystem gates
for selected scopes. Release-shaped artifact work groups must invoke the
existing workflow-release build recipes/adapters in validation/no-publish mode
where practical. Wrappers, artifact staging locations, and receipt emission may
differ from release runs, but build semantics, descriptor interpretation, and
artifact-contract checks must not use a separate simplified CI-only path.

Aggregation is mapped as the terminal control-plane job after all planned
executable work groups are complete, skipped by workflow construction, or
otherwise known missing. It reads the frozen plan, required companion planning
snapshots, and validation receipts, emits `ci-validation-aggregate`, and does not
emit a normal work-group receipt.

Aggregation uses always-run failure-reporting semantics after the planning and
selector materialization attempts. If planning emits no readable plan, emits an
invalid plan, or selector materialization fails before producing a reliable
executable selector set, aggregation emits an `invalid-plan` aggregate with zero
executable selectors rather than allowing the workflow to end without an
aggregate artifact.

## 13. Release-Shaped Artifact Validation

For descriptor-backed subjects, release-shaped validation derives artifact
obligations from release descriptors and the existing workflow-release artifact
model.

Artifact obligations are plan-level records in the top-level
`artifact-obligations` section. Release-shaped validation work groups consume
those frozen obligations by `artifact-obligation-id`.

The `release-receipt` block describes the release-shaped receipt expectation
that is validated alongside the artifact shape. It is the receipt shape being
checked, not the CI validation receipt emitted by the work group.

Rules:

- The obligation set is the union required by all declared profiles.
- `profile-coverage` values are descriptor-declared profile identifiers; current
  descriptors use `buddy` and `official`, but the field is not a closed enum.
- No publish nodes, target remote state, overwrite policy, release tags, or GitHub
  Release operations are planned.
- If a descriptor is invalid enough to prevent derivation, planning fails closed.
- If descriptor-validation work is executable and fails, the corresponding
  descriptor-validation work group records a blocking validation failure.
- If a shape cannot be confirmed without release-only credentials or side
  effects, the work group records a blocking validation failure.
- Artifact validation receipts are validation-only and inadmissible as immutable
  release proof.
- A `release-shaped-artifact` receipt must use `category-result.detail` with this
  minimum shape:

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

    The single planned `artifact-obligation-id` bound to the release-shaped work
    group must appear exactly once, and no other artifact obligation may appear in
    that receipt. `profile-coverage` is copied from the frozen obligation and
    artifact `planned` plus release-receipt `planned` fields are copied from the
    frozen artifact obligation and equality-checked by aggregation. The planned
    `expected-artifact-refs` set is the complete required artifact coverage for
    the obligation and must be non-empty. The `observed.refs` set must equal both
    the refs checked by the work group and the frozen `expected-artifact-refs`;
    empty, partial, or extra refs are a blocking release-shaped artifact
    validation failure. `observed.digests` must contain exactly one entry per
    expected ref with `algorithm: sha256`, matching `artifact-ref`, and a
    lowercase hexadecimal SHA-256 `digest` when `digest-available: true`. A
    missing, duplicate, mismatched, non-SHA-256, or unavailable digest is a
    blocking release-shaped artifact validation failure with
    `artifact-shape-unconfirmed`; unavailable digest entries must set
    `digest-available: false`, use an empty digest string, and carry a diagnostic
    explaining why the artifact bytes could not be content-bound without
    publication credentials or side effects. A successful release-shaped artifact
    result also requires `release-receipt.expected` and
    `release-receipt.schema-checked` to be `true`, and
    `release-receipt.outcome` to be `success`; unchecked or unexpected
    release-shaped receipt outputs are blocking failures, not successful
    validation evidence.

## 14. Evidence and Receipt Files

Every executable validation work group emits one CI validation receipt:

```yaml
common-envelope: inherited
api-version: three.ci.validation.receipt/v1alpha1
kind: ci-validation-receipt
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

A `descriptor-validation` receipt must use `category-result.detail` with this
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
descriptor obligation may appear in that receipt. `descriptor.path`,
`descriptor.identity`, `descriptor.owner-subject-id`, `descriptor.source`, and
`descriptor-scope` are copied from the frozen descriptor obligation and its
digest-bound fact snapshot descriptor record, then equality-checked by
aggregation. A descriptor-validation receipt that omits the bound obligation,
adds another obligation, or reports descriptor fields that do not match the
frozen plan and fact snapshot is inadmissible with `mismatched-evidence-payload`.

Receipt rules:

- The receipt intake boundary is a closed control-plane-owned manifest-indexed
  namespace for validation receipts. Aggregation enumerates every receipt-like
  entry in that boundary and does not treat ordinary logs or auxiliary artifacts
  outside that boundary as observed receipts.
- The closed receipt intake boundary is the run-attempt-scoped artifact namespace
  `ci-validation/receipts/<run-id>/<run-attempt>/`. The trusted receipt boundary
  for an executable work-group job is authorized to write only that selector's
  own receipt artifacts in that namespace; category-specific validation commands
  are not authorized writers for contract-owned receipt artifacts. Each expected
  receipt artifact ref is derived from the frozen selector, not from receipt
  payload claims:
  `ci-validation/receipts/<run-id>/<run-attempt>/<work-group-id>/receipt.json`.
  If one concrete job batches multiple selectors, it must still write one receipt
  artifact at the derived ref for each covered `work-group-id`.
  Final receipt manifest and aggregate artifacts must be serialized as RFC 8785
  canonical UTF-8 JSON bytes before upload. Any rule below that refers to raw
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
          artifact-ref: string
          artifact-instance-id: string
          assignment-id: string | null
          writer-work-group-id: string | null
          trusted-writer-id: string | null
          observed-writer-id: string | null
          writer-observation-ref: string | null
          receipt-id: string | null
          receipt-content-digest: string | null
    ```

    `receipt-namespace-closure` records the closed receipt set observed by the
    first authoritative aggregation pass for the run attempt. Aggregation may
    declare the namespace closed only after selector materialization is complete
    and all executable work-group jobs that can write trusted receipts have
    reached a terminal state. The closure's `closed-receipt-count` and
    `observed-entry-ids` must equal the manifest entries. Post-run acceptance and
    same-attempt aggregation retries must re-enumerate the receipt intake
    namespace and require exact equality with the manifest's closed artifact
    refs, artifact instance IDs, and observed entry IDs. Any receipt-like artifact
    instance that appears after closure, or any missing closed instance, makes
    final evidence non-authoritative rather than extending the observed receipt
    set.

    `observed-entry-id` is a stable aggregation-assigned identifier for one
    observed receipt-like artifact instance. It is derived from `run-id`,
    `run-attempt`, `artifact-ref`, and the artifact service or control-plane
    enumeration `artifact-instance-id`. Aggregation validates the plan and
    required post-plan control artifacts before authoritative receipt
    admissibility classification. If the artifact store cannot provide a stable
    per-instance ID after the plan and selector-assignment contracts are
    authoritative, the receipt namespace is unenumerable and aggregation emits a
    failed aggregate with `reason.inadmissible-receipt: true`, a failure with
    `kind: inadmissible-receipt`, and diagnostic detail
    `unstable-artifact-instance-id` rather than collapsing duplicates. If the
    plan or post-plan control contract is invalid, unstable receipt instance IDs
    remain inspection-only observations and must not create
    `inadmissible-receipt` failures.
    `writer-work-group-id` is derived from the artifact ref path segment, not
    from the receipt payload, and is `null` when the ref is malformed.
    `assignment-id`, `trusted-writer-id`, `observed-writer-id`, and
    `writer-observation-ref` are copied only after aggregation verifies the
    selector assignment and writer-observation record for the artifact instance.
    `receipt-content-digest` in the manifest is the aggregator-observed SHA-256
    digest, not a writer claim. The receipt manifest artifact content digest is
    the lowercase hexadecimal SHA-256 digest of the raw manifest artifact bytes
    written at the contract-owned manifest ref. Aggregation is the only
    authorized writer for the manifest and the only reader that derives the
    CI-level verdict. Entries are sorted by `observed-entry-id`. Duplicate
    observed entries, artifact refs that
    do not match the derived pattern, writer/work-group mismatches between
    artifact ref, assignment manifest, writer-observation record, and receipt
    payload, missing or mismatched trusted writer identity, cross-attempt
    artifacts, and unreadable receipt artifacts are observed inadmissible entries
    and must appear in aggregate diagnostics/failures. A pre-existing manifest
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
  admissible until a structurally valid plan, selector-assignment manifest, and
  matching plan identity are verified.
- `receipt-id` is an opaque stable identifier for the receipt emission within the
  run attempt or equivalent execution provenance. It must not be derived from a
  representation that includes itself.
- `plan-id`, `plan-digest`, and `work-group-id` must match the validation plan.
  `plan-digest` matching means equality to the recomputed digest defined in
  section 6.1, not merely equality to an unverified string copied from the plan.
  `assignment-id` must match the selector-assignment manifest entry for the work
  group; the receipt payload cannot create, change, or authorize that assignment.
- Because each executable `work-group-id` has exactly one evidence expectation in
  the plan, aggregation matches receipts to evidence expectations by `plan-id`
  and `work-group-id`.
- Receipts must mirror the plan provenance: affected-mode receipts carry
  `validation-tree`, `affected-range`, and `scheduled-full` fields matching the
  plan envelope; scheduled-full receipts carry `affected-range.status:
not-applicable`, null affected-range SHAs and hash, and `scheduled-full.enabled:
true`.
- The trusted receipt boundary must observe the checkout or execution tree before
  running validation and bind it to the planned tree in the receipt. For
  executable work groups, `execution-tree.observed-commit-sha` must equal the
  frozen `validation-tree.commit-sha`, `execution-tree.source` is
  `trusted-receipt-boundary`, and `execution-tree.verified` must be `true` for a
  receipt to be admissible. The category-specific validation command may not
  self-attest the execution tree. A missing, unverifiable, or mismatched execution
  tree makes the receipt inadmissible with `mismatched-evidence-payload`; if the
  trusted receipt boundary cannot verify the tree before validation, it must emit
  a blocking receipt or no receipt rather than a successful one.
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
  `category-result`: `success` is admissible only when the category-specific
  validation completed successfully and no blocking diagnostic is present;
  `blocking-failure` is admissible only when the category-specific validation
  failed or a blocking diagnostic is present; `skipped` is admissible only when
  category-specific validation was intentionally not executed and the receipt
  carries a diagnostic explaining the skip. A mismatch between top-level
  `outcome`, `category-result`, diagnostics, or skipped rules is inadmissible.
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
  expectation, the receipt with the lowest `observed-entry-id` is the only
  candidate that may satisfy the expectation; every other matching receipt is
  inadmissible with `duplicate-receipt`. Receipts that are inadmissible for other
  reasons do not participate in choosing the satisfying receipt.
- Any observed inadmissible receipt contributes to a failing aggregated outcome
  with `inadmissible-receipt`; a valid receipt does not offset an extra
  inadmissible receipt.
- A required evidence expectation passes aggregation only when exactly one valid
  matching receipt satisfies it; zero valid receipts or only inadmissible receipts
  aggregate as `required-evidence-missing`.
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
  descriptor facts, target-catalog facts, exact selected-subject provider
  coverage, subject-selection provenance dependency-edge basis, and
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
      artifact-ref: string
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
For structurally valid plans,
`plan-digest`, `mode`, `validation-tree`, `affected-range`, and `scheduled-full`
are copied from the frozen plan; the aggregator must verify they match the plan
before emitting the report. Summary booleans and counts are for quick
inspection. `evidence-results` is the normalized machine-readable result for
every evidence expectation. Every evidence expectation is verdict-relevant, so
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
exists.

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
or mismatched companion snapshot or selector-assignment manifest, it emits a
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
| `range-unconfirmed`                   | planner                                     | fail-closed                                                                           |
| `unknown-change`                      | planner                                     | fail-closed                                                                           |
| `subject-unresolved`                  | planner                                     | fail-closed                                                                           |
| `dependency-impact-insufficient`      | planner                                     | fail-closed                                                                           |
| `fact-provider-insufficient`          | planner                                     | fail-closed                                                                           |
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
machine-readable reasons. `range-unconfirmed` details are:

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
- `selector-assignment-missing`;
- `selector-assignment-duplicate`;
- `selector-assignment-unreadable`;
- `selector-assignment-malformed`;
- `selector-assignment-schema-invalid`;
- `selector-assignment-plan-mismatch`;
- `selector-assignment-producer-unverified`;
- `selector-assignment-structurally-invalid`;
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

| Scenario                                                                                                                                                                                        | Expected evidence                                                                                                                                                                                                                                                                                                                                                                                                     |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Project-scoped descriptor-backed change                                                                                                                                                         | Plan selects direct subject, safe downstream subjects, direct-vs-downstream selection provenance, descriptor obligation, ecosystem gates, release-shaped artifact obligations, receipts, and passing aggregation                                                                                                                                                                                                      |
| Project-scoped validation-only change                                                                                                                                                           | Plan selects validation-only subject and ecosystem gates, no publish or release-shaped artifact obligation unless descriptor-backed                                                                                                                                                                                                                                                                                   |
| Ecosystem-scoped change                                                                                                                                                                         | Plan selects all active subjects in ecosystem, descriptors for descriptor-backed subjects, release-shaped artifact/receipt obligations, and applicable ecosystem gates                                                                                                                                                                                                                                                |
| Workflow-release infrastructure change                                                                                                                                                          | Plan selects affected tooling surface, related subjects/ecosystems, and all discovered descriptors only for descriptor semantics, authoring validation, planning, contracts, build execution, publish execution, or smoke validation impacts                                                                                                                                                                          |
| Known global change                                                                                                                                                                             | Plan selects scheduled-full-equivalent scope with global provenance                                                                                                                                                                                                                                                                                                                                                   |
| Scheduled full run                                                                                                                                                                              | Plan selects full repository scope with scheduled provenance records using `selection-kind: scheduled-full`, empty impact/expansion refs, `scheduled-full-source: true`, and `scheduled-full.enabled: true`                                                                                                                                                                                                           |
| Known non-impacting change with no executable checks                                                                                                                                            | Lightweight-only plan passes without heavy work and remains inspectable                                                                                                                                                                                                                                                                                                                                               |
| Known non-impacting change with executable lightweight checks                                                                                                                                   | Lightweight work receipts are required for pass                                                                                                                                                                                                                                                                                                                                                                       |
| Confirmed zero-file affected range                                                                                                                                                              | Affected request has `affected-range.status: available`, empty `changed-files`, non-null canonical `changed-files-hash`, executable lightweight-only plan with available discovered subjects all marked `not-selected`, an available provider fact snapshot, no selected subjects, no executable validation work groups, no evidence expectations, no receipts, and passing aggregate after final evidence validation |
| Wrong-run or producer-unverified planner-facing request                                                                                                                                         | Planning does not trust affected-range or scheduled-full payload claims; it fails closed or emits no authoritative plan unless the request ref, digest, instance count, envelope, and `normalize-input` producer authority verify                                                                                                                                                                                     |
| Selector materialization receives invalid, producer-unverified, identity-unverified, or companion-mismatched plan                                                                               | `materialize-work-groups` emits no selector-assignment manifest and no executable selectors unless it can verify plan identity and all authoritative plan plus companion snapshot checks; fan-out never runs from a plan that has not passed that validation                                                                                                                                                          |
| Structurally valid fail-closed or no-executable plan                                                                                                                                            | `materialize-work-groups` emits exactly one empty selector-assignment manifest with verified plan identity so aggregation preserves fail-closed or no-work semantics instead of reporting `invalid-plan`                                                                                                                                                                                                              |
| Mixed project/ecosystem/infrastructure/non-impacting change                                                                                                                                     | Plan unions all selected scopes, descriptor/release-shaped obligations, ecosystem gates, and additive lightweight obligations; broader scopes may subsume duplicates only with explicit `classification.subsumptions` records, and non-impacting paths do not replace required heavyweight validation                                                                                                                 |
| Policy-bearing planner/classifier/fact-provider change                                                                                                                                          | Plan is produced using the validation tree under review, exposes `planner.policy-source: validation-tree` plus verified `planner.execution-tree` provenance, and acceptance rejects evidence planned by a baseline or wrong-tree policy                                                                                                                                                                               |
| Affected plan omits or invents changed-path impact coverage                                                                                                                                     | Aggregation emits `invalid-plan` with `diagnostic-detail: changed-files-impact-coverage-mismatch` rather than allowing omitted paths to bypass fail-closed classification                                                                                                                                                                                                                                             |
| PR/push affected range unconfirmed                                                                                                                                                              | Request diagnostic `range-unconfirmed`, fail-closed plan, failing aggregation/workflow conclusion, no executable validation work groups, and no validation receipts                                                                                                                                                                                                                                                   |
| Project-scoped change with insufficient downstream facts                                                                                                                                        | Planner diagnostic `dependency-impact-insufficient` or `fact-provider-insufficient`, fail-closed plan, failing aggregation/workflow conclusion, no executable validation work groups, and no validation receipts                                                                                                                                                                                                      |
| Unclassifiable workflow-release infrastructure impact                                                                                                                                           | Planner diagnostic `infrastructure-surface-unclassified`, fail-closed plan, failing aggregation/workflow conclusion, no executable validation work groups, and no validation receipts                                                                                                                                                                                                                                 |
| Unknown path                                                                                                                                                                                    | Fail-closed plan, failing aggregation/workflow conclusion, no validation work groups                                                                                                                                                                                                                                                                                                                                  |
| Invalid descriptor blocking derivation                                                                                                                                                          | Fail-closed planning or blocking descriptor-validation failure according to derivability                                                                                                                                                                                                                                                                                                                              |
| Descriptor-validation receipt omits or mismatches its bound descriptor obligation                                                                                                               | Aggregation treats the receipt as inadmissible with `mismatched-evidence-payload`; descriptor obligation ID, descriptor path/identity/owner/source, and descriptor scope must match the frozen plan and fact snapshot exactly                                                                                                                                                                                         |
| Unreadable, malformed, schema-invalid, duplicate, producer-unverified, or digest-mismatched validation plan                                                                                     | Aggregation emits a failed `invalid-plan` aggregate with unverified plan-derived fields set to `null` or `unknown`, empty evidence results, zero executable counts, and no receipt admissibility authority                                                                                                                                                                                                            |
| Structurally invalid but schema/digest-valid validation plan                                                                                                                                    | Aggregation emits `invalid-plan` with `diagnostic-detail: structurally-invalid`, empty evidence results, zero executable counts, and no receipt admissibility authority                                                                                                                                                                                                                                               |
| Missing, unexpected, duplicate, producer-unverified, malformed, ref-mismatched, noncanonical, or digest-mismatched companion planning snapshot                                                  | Aggregation rejects the otherwise readable plan as `invalid-plan` with the applicable changed-files or fact-snapshot diagnostic detail                                                                                                                                                                                                                                                                                |
| Validation obligation references a mismatched or shared work group/evidence expectation                                                                                                         | The plan is structurally invalid unless duplicate candidates were removed before freezing and represented only by explicit subsumption records                                                                                                                                                                                                                                                                        |
| Missing, duplicate, producer-unverified, plan-mismatched, or structurally invalid selector-assignment manifest                                                                                  | Aggregation emits `invalid-plan` rather than materializing selectors or admitting receipts                                                                                                                                                                                                                                                                                                                            |
| Logical boundary mapped to missing or wrong platform job identity                                                                                                                               | Producer authority verification rejects the artifact as producer-unverified using the boundary identity map; payload producer claims, logs, and job conclusions do not substitute                                                                                                                                                                                                                                     |
| Logical contract artifact ref maps ambiguously or incorrectly to a physical artifact name                                                                                                       | Artifact instance counting, duplicate detection, and namespace enumeration use the canonical `base64url_no_padding(utf8(logical-artifact-ref))` physical-name mapping; non-decodable or mismatched physical names are non-authoritative                                                                                                                                                                               |
| Missing receipt                                                                                                                                                                                 | Aggregation fails with `required-evidence-missing` and identifies the missing work group or evidence expectation                                                                                                                                                                                                                                                                                                      |
| Planned validation work skipped or failed                                                                                                                                                       | Aggregation records `required-evidence-skipped` or `blocking-validation-failure` and fails the final verdict; planned executable validation work is not optional or non-gating                                                                                                                                                                                                                                        |
| Ecosystem gate omits a capability enabled by selected subject/provider facts                                                                                                                    | Plan is fail-closed with `fact-provider-insufficient` or structurally invalid; receipts cannot pass by matching an under-planned capability set                                                                                                                                                                                                                                                                       |
| Upstream selector emits a valid `blocking-failure` receipt                                                                                                                                      | The selector job completes successfully for dependency gating, downstream selectors are not dependency-blocked solely by that receipt outcome, and aggregation fails the final verdict from the receipt evidence                                                                                                                                                                                                      |
| Receipt emitted after validation on the wrong or unverifiable execution tree                                                                                                                    | Aggregation treats the receipt as inadmissible with `mismatched-evidence-payload`; copied plan provenance is insufficient without `execution-tree` evidence from the trusted receipt boundary                                                                                                                                                                                                                         |
| Release-shaped artifact receipt with empty, partial, extra, or unavailable expected artifact coverage, missing artifact digest, unchecked expected release receipt, or mismatched planned shape | Aggregation records `artifact-shape-unconfirmed` or `mismatched-evidence-payload` and fails the final verdict                                                                                                                                                                                                                                                                                                         |
| Invalid or mismatched receipt                                                                                                                                                                   | Inadmissible receipt does not satisfy required evidence; aggregation fails with `inadmissible-receipt`, and also `required-evidence-missing` when no valid matching receipt exists                                                                                                                                                                                                                                    |
| Forged or producer-unverified writer observation                                                                                                                                                | Matching payload fields are insufficient; aggregation treats the receipt as inadmissible with `mismatched-writer-identity` and fails as `inadmissible-receipt`                                                                                                                                                                                                                                                        |
| Valid required receipt plus extra inadmissible receipt                                                                                                                                          | Required evidence is satisfied by the valid receipt, but aggregation still fails with `inadmissible-receipt` for the extra malformed, duplicate, unexpected, wrong-plan, unknown-work-group, or mismatched-work-group receipt                                                                                                                                                                                         |
| Receipt-like artifact appears after receipt namespace closure                                                                                                                                   | Post-run acceptance or same-attempt retry treats final evidence as non-authoritative rather than extending the closed receipt set                                                                                                                                                                                                                                                                                     |
| Same-attempt finalization retry with occupied final manifest or aggregate                                                                                                                       | Aggregation preserves the occupied artifact's `created-at` while recomputing raw digest equality; digest mismatch or duplicate final artifact leaves no authoritative final evidence                                                                                                                                                                                                                                  |
| Aggregate exists at the final ref but the final manifest is missing                                                                                                                             | Same-attempt retry treats the final state as non-recoverable and non-authoritative; it does not recreate a manifest to satisfy the aggregate's existing `receipt-manifest.content-digest` claim                                                                                                                                                                                                                       |
| Final manifest or aggregate finalization/reconciliation fails despite a passing computed validation verdict                                                                                     | The aggregate records `reason.final-evidence-failure` and `failure-kind: final-evidence-failure` when it can be written; otherwise the `aggregate-evidence` job and final required check fail under the no-authoritative-final-evidence contract                                                                                                                                                                      |
| Missing, duplicate, malformed, wrong-run, wrong-producer, or mutually mismatched final manifest or aggregate                                                                                    | Post-run acceptance treats the final evidence as non-authoritative; logs, job conclusions, or auxiliary artifacts cannot replace the exact contract-owned manifest and aggregate artifacts                                                                                                                                                                                                                            |
| Final manifest or aggregate JSON is not RFC 8785 canonical UTF-8 JSON                                                                                                                           | Final evidence is malformed/non-authoritative or records `final-evidence-failure`; semantic JSON equivalence is insufficient for final digest replay                                                                                                                                                                                                                                                                  |
| Unknown verdict-relevant diagnostic family or detail appears in contract evidence                                                                                                               | Schema or aggregation rejects it under the closed `v1alpha1` diagnostic vocabulary unless the LLD/schema/api-version contract has been updated                                                                                                                                                                                                                                                                        |
| Unconfirmed artifact shape                                                                                                                                                                      | Blocking validation failure, no release-proof admissibility                                                                                                                                                                                                                                                                                                                                                           |
| Unconfirmed PR context                                                                                                                                                                          | No publication credentials, release environment, or OIDC publish permission exposed                                                                                                                                                                                                                                                                                                                                   |
| Accidental publication or remote publish-state validation                                                                                                                                       | Static workflow/config/code review and receipt/aggregate inspection show no work group, command output, receipt field, aggregate field, registry query, GitHub Release lookup, tag lookup, or remote publish-state observation is used as validation evidence                                                                                                                                                         |
| All CI validation modes have no configured publication authority                                                                                                                                | Static workflow/config/code review covers `pull_request`, `push`, and `scheduled_full`; no publication credentials, OIDC publish permission, release environment, registry mutation, GitHub Release mutation, or release tag mutation is configured                                                                                                                                                                   |

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
- upload names for logs and auxiliary artifacts other than contract-owned receipt
  artifacts and manifests;
- batching strategy for work-group selectors;
- exact HK profile names and step ordering;
- internal test organization.

The following are not implementation-owned:

- CI belonging to workflow-release rather than a separate CI truth;
- validation plan and receipt `api-version`/`kind` families;
- plan authority over classification, subjects, obligations, work groups, and
  diagnostics;
- unknown/unclassifiable fail-closed behavior;
- scheduled-full-equivalent global scope;
- no publication credentials or release side effects;
- validation-only proof inadmissibility;
- final verdict semantics for fail-closed, missing evidence, blocking failures,
  and lightweight-only plans.
- contract-owned receipt artifact refs, intake namespace, and aggregate manifest
  location.

## 19. Outcome

This low-level design gives the implementer a concrete handoff baseline for CI
affected validation without prescribing code internals. It freezes the workflow
entry boundary, logical job sequence, request and plan files, subject snapshot
shape, fact-provider realization, semantic path classification families, scope
resolution rules, work-group selectors, execution mapping, artifact-validation
obligations, validation-only receipts, diagnostics, HK relationship, and
acceptance evidence. Concrete code structure and command implementation remain
owned by the senior engineer as long as these contracts are preserved.
