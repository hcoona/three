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
| Downstream closure | Use ecosystem-provided dependency facts when trustworthy; otherwise use approved ecosystem expansion or fail closed.                                                                                                      |
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

Implementation may introduce reusable internal workflow files or composite
actions for validation work groups. Those internal files are implementation-owned
unless later branch protection or external policy starts depending on them.

### 4.2 Logical Job Sequence

The top-level CI validation workflow preserves this logical sequence:

1. **`normalize-input`**
    - resolves CI mode: `pull_request`, `push`, or `scheduled_full`;
    - derives the trustworthy affected range for affected modes;
    - records event identity for diagnostics;
    - fails into a planner-facing fail-closed request when external event, API, or
      checkout inputs cannot provide a complete affected range.
2. **`plan`**
    - checks out the validation tree;
    - runs validation planning without publication credentials;
    - emits exactly one validation plan artifact;
    - emits plan diagnostics when planning fails closed.
3. **`materialize-work-groups`**
    - reads the validation plan;
    - materializes execution selectors from plan work groups;
    - produces an empty selector set for fail-closed plans and lightweight-only
      plans with no executable lightweight obligations.
4. **Validation work-group fan-out**
    - runs executable work groups by selector;
    - emits one validation-only receipt per work group;
    - never changes planned scope or obligations.
5. **`aggregate-evidence`**
    - verifies expected receipts;
    - computes the CI validation verdict;
    - emits one aggregation report;
    - fails the workflow when the aggregated validation outcome fails.

These job names are logical handoff names. The implementer may map them to
concrete job identifiers, reusable workflows, or grouped jobs, provided the
sequence, authority boundary, and evidence semantics remain intact.

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

Schema blocks below use `common-envelope: inherited` to avoid repeating those
fields. The block then lists only fields specific to that artifact kind.

The planner-facing CI request uses:

```yaml
common-envelope: inherited
api-version: three.ci.validation.request/v1alpha1
kind: ci-validation-request
mode: pull_request | push | scheduled_full
validation-tree:
    commit-sha: string
    ref: string | null
affected-range:
    status: available | unavailable
    base-sha: string | null
    head-sha: string | null
    changed-files: [string]
    source: pull_request | push | null
    diagnostic: range-unavailable | null
scheduled-full:
    enabled: boolean
event:
    name: string
    number: string | null
    actor: string
    run-id: string
    run-attempt: string
```

Rules:

- `affected-range` is required for `pull_request` and `push`.
- `affected-range.status: available` requires fixed endpoint SHAs and a complete
  changed-file list.
- `affected-range.status: unavailable` means the trusted control-plane logic could
  not form a complete affected input from GitHub event payloads, GitHub API data,
  or checkout/git data.
- `affected-range.status: unavailable` requires `diagnostic: range-unavailable`
  and forces fail-closed planning.
- `scheduled-full.enabled` is `true` only for `scheduled_full`.
- `changed-files` is absent or empty only for scheduled full or unavailable
  affected ranges.

The exact physical filename is implementation-owned, but the file is an internal
workflow artifact or workspace handoff, not a user-authored source file.

## 6. Validation Plan File

The validation plan is one JSON artifact emitted by planning.

### 6.1 Plan Envelope

```yaml
common-envelope: inherited
api-version: three.ci.validation.plan/v1alpha1
kind: ci-validation-plan
plan-id: string
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
    head-sha: string | null
    changed-files-hash: string | null
planner:
    policy-source: validation-tree
    version: string | null
subject-universe-id: string
fact-snapshot-id: string
```

`plan-id` is stable for the exact plan content and provenance. It is used to bind
work-group receipts and aggregation evidence to the plan.

### 6.2 Plan Sections

The plan contains these top-level sections:

```yaml
classification:
    impacts: [impact-record]
    broad-expansions: [broad-expansion-record]
    lightweight-only: boolean
subjects: [validation-subject-snapshot]
descriptor-obligations: [descriptor-obligation]
validation-obligations: [validation-obligation]
work-groups: [work-group]
evidence-expectations: [evidence-expectation]
diagnostics: [planner-diagnostic]
```

Fail-closed plans still contain envelope, classification, diagnostics, and enough
provenance to inspect why no executable validation plan was authorized. They have
no executable work groups.

The exact JSON Schema file and type generator strategy are implementation-owned,
but every section above is part of the low-level data contract.

Each `broad-expansion-record` records the minimum audit trail for non-minimal
scope selection:

```yaml
expansion-id: string
source-impact-id: string
category: ecosystem | global | workflow-release-infrastructure | approved-fallback
reason: string
resulting-scope:
    ecosystems: [string]
    subjects: [string]
    descriptors: all-discovered | selected | none
```

The expansion record is inspectability data. Execution still consumes the final
selected subjects, obligations, and work groups rather than recomputing expansion.

### 6.3 Named Record Minimum Shapes

Each `impact-record` has:

```yaml
impact-id: string
category: project-scoped | ecosystem-scoped | workflow-release-infrastructure | global | known-non-impacting | unknown
matched-paths: [string]
source-rule: string
coverage-target:
    type: subject | ecosystem | tooling-surface | global | none
    id: string | null
requires:
    descriptor-validation: boolean
    downstream-expansion: boolean
    broad-expansion: boolean
diagnostic: diagnostic-code | null
```

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

Each `validation-obligation` has:

```yaml
validation-obligation-id: string
source-impact-ids: [string]
kind: lightweight-preflight | ecosystem-gate | release-shaped-artifact | workflow-release-tooling
coverage-target:
    type: subject | descriptor | tooling-surface | artifact-obligation
    id: string
required: boolean
blocking: boolean
work-group-id: string | null
expected-evidence-id: string | null
```

Each `evidence-expectation` has:

```yaml
evidence-expectation-id: string
work-group-id: string
coverage-target:
    type: subject | descriptor | tooling-surface | artifact-obligation
    id: string
category: lightweight | ecosystem-gate | descriptor | release-shaped-artifact | tooling | aggregation
required: boolean
blocking-if-missing: boolean
```

Each `planner-diagnostic` has:

```yaml
diagnostic-id: string
code: diagnostic-code
severity: info | warning | fail-closed | blocking-failure
source:
    type: request | impact | subject | descriptor | fact-provider | aggregation
    id: string | null
message: string
verdict-effect: none | fail-closed | failed
```

Binding rules:

- Obligations reference their source impacts for auditability.
- Required executable obligations must reference a work group and evidence
  expectation unless planning fails closed.
- Missing required evidence and blocking diagnostics fail aggregation.
- Informational diagnostics, including known non-impacting diagnostics, must not
  by themselves authorize or block execution.

## 7. Subject Universe Snapshot

Each discovered validation subject snapshot has:

```yaml
subject-id: string
ecosystem: dotnet | python | javascript | typescript | ruby | other
root: string
status: selected | excluded
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
    source: descriptor | workspace | solution | explicit
    reason: string
exclusion:
    reason: string | null
```

Rules:

- `status: excluded` subjects are included for auditability but cannot produce
  executable validation work.
- `capability-class: validation-only` subjects cannot have publish obligations.
- `capability-class: descriptor-backed` subjects may have release-shaped artifact
  obligations only when descriptor validation is trustworthy enough to derive
  them.
- Subject IDs are stable within a repository and should be path- and
  ecosystem-derived rather than display-name-derived.

## 8. Fact Provider Realization

The implementation uses one fact-provider seam per ecosystem family plus one
workflow-release tooling provider.

| Provider              | Discovery source                                                         | Required facts                                                                                                                                         |
| --------------------- | ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| .NET                  | solution/MSBuild project graph under active roots                        | project roots, project references when available, packable descriptor-backed projects, validation-only test/build projects, Windows runner expectation |
| Python                | `uv` workspace and project metadata under active roots                   | workspace members, package roots, validation-only projects, dependency facts when safely available, Ubuntu runner expectation                          |
| JavaScript/TypeScript | PNPM workspace metadata under active roots                               | workspace packages, package roots, validation-only packages, dependency facts when safely available, Ubuntu runner expectation                         |
| workflow-release      | release descriptors, target catalog, workflow-release docs/tooling paths | descriptor-backed subjects, tooling surfaces, descriptor schema documentation surfaces, smoke validation surfaces                                      |

Provider failure rules:

- If discovery fails for a selected ecosystem scope, planning fails closed unless a
  requirement-approved broader ecosystem expansion still has trustworthy facts.
- If dependency facts are unavailable for a project-scoped change, planning uses
  approved ecosystem expansion or fail-closed behavior.
- Providers report capabilities and facts; the planner assigns normalized subject
  capability class and final obligations.

Fact collection must not perform build, test, packaging, release-shaped artifact
validation, publication, or remote publish-state observation. Those activities
belong to execution-layer work groups authorized by the validation plan.

The exact commands used to query ecosystem tools are implementation-owned, but
the provider outputs must be deterministic and plan-inspectable.

## 9. Path Classification Table

The first implementation uses a conservative repository path classification
table. More-specific rules win before broader rules, except unknown always wins
when no rule matches.

| Path shape                                                                                                                                                                                                                   | Category                        | Scope result                                                                                                                                                        |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/**`, `src/lab/**`, `tests/**` files owned by one discovered subject                                                                                                                                                     | project-scoped                  | Direct subject plus safe downstream dependents, descriptor obligations when descriptor-backed, applicable ecosystem gates                                           |
| Ecosystem workspace files such as root workspace metadata, lock files, package-manager configuration, or language tool configuration                                                                                         | ecosystem-scoped                | All active subjects in the affected ecosystem and descriptor-backed descriptors in that ecosystem                                                                   |
| Root monorepo tool configuration affecting multiple ecosystems, global repository build settings, or cross-ecosystem validation configuration                                                                                | global                          | Scheduled-full-equivalent validation scope with global provenance                                                                                                   |
| Workflow-release planner, classifier, fact-provider, descriptor contract, target catalog behavior, workflow orchestration, build execution, publish execution, smoke validation, or descriptor schema documentation surfaces | workflow-release infrastructure | Affected tooling surface, related ecosystems and subjects, all discovered descriptors when descriptor semantics or listed infrastructure categories can be affected |
| Documentation or files explicitly known not to affect build, test, descriptors, workflow-release tooling, or ecosystem behavior                                                                                              | known non-impacting             | Lightweight-only plan with applicable lightweight work groups                                                                                                       |
| Anything else                                                                                                                                                                                                                | unknown                         | Fail-closed plan                                                                                                                                                    |

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
plan records the subsumption in classification rationale or broad-expansion
records.

## 10. Scope Resolution Details

### 10.1 Project-Scoped

Project-scoped resolution:

1. map changed files to discovered validation subjects;
2. include directly mapped subjects;
3. compute downstream dependents from provider dependency facts;
4. when downstream facts are trustworthy, include downstream subjects;
5. when downstream facts are not trustworthy, select approved ecosystem expansion
   or fail closed;
6. add descriptor obligations for selected descriptor-backed subjects;
7. add ecosystem gate obligations for selected subjects.

### 10.2 Ecosystem-Scoped

Ecosystem-scoped resolution selects:

- all selected-status validation subjects in the ecosystem;
- descriptor validation for all descriptor-backed subjects in the ecosystem;
- all applicable ecosystem gates for that ecosystem.

### 10.3 Workflow-Release Infrastructure

Infrastructure resolution selects:

- workflow-release tooling-surface work groups for affected surfaces;
- related ecosystem and subject scopes when affected surfaces can influence them;
- all discovered release descriptors for descriptor semantics, authoring
  validation, planning, contracts, build execution, publish execution, or smoke
  validation impacts;
- no representative-smoke substitution for broader validation.

### 10.4 Global and Scheduled Full

Global and scheduled full select the same scope:

- all selected-status validation subjects in all ecosystems;
- all discovered release descriptors;
- all applicable ecosystem gates;
- release-shaped artifact and receipt validation for descriptor-backed projects;
- relevant workflow-release tooling validation.

The only difference is provenance: global is affected validation caused by a
changed path, while scheduled full is time-based full-repository validation.

## 11. Work Group Selectors

Each executable work group has:

```yaml
work-group-id: string
kind: enum
coverage-target:
    type: subject | descriptor | tooling-surface | artifact-obligation
    id: string
ecosystem: string | null
runner-family: windows | ubuntu | null
depends-on: [work-group-id]
expected-evidence:
    category: lightweight | ecosystem-gate | descriptor | release-shaped-artifact | tooling | aggregation
    required: boolean
```

Selector rules:

- `work-group-id` is stable within the plan and derived from kind plus coverage
  target.
- Work groups are selectors, not command lines.
- The control plane may batch multiple selectors into one concrete job only when
  the resulting receipts still report each required selector separately.
- Fail-closed plans contain no executable work groups.
- Lightweight-only plans may contain no executable work groups, or may contain
  lightweight-preflight work groups that must produce evidence before the run can
  pass.

## 12. Execution Mapping

The implementation maps work groups to runner families:

| Work group kind                                                       | Default runner family                                         | Notes                                                                       |
| --------------------------------------------------------------------- | ------------------------------------------------------------- | --------------------------------------------------------------------------- |
| `lightweight-preflight`                                               | Ubuntu                                                        | May run documentation, formatting, or policy checks when lightweight enough |
| `ecosystem-gate` for .NET                                             | Windows                                                       | Preserves .NET runner expectation                                           |
| `ecosystem-gate` for Python                                           | Ubuntu                                                        | Uses repository tool provisioning convention                                |
| `ecosystem-gate` for JavaScript/TypeScript                            | Ubuntu                                                        | Uses repository tool provisioning convention                                |
| `descriptor-validation`                                               | Ubuntu unless descriptor validation requires ecosystem runner | Must not publish or mutate release state                                    |
| `release-shaped-build-validation` for .NET                            | Windows                                                       | Produces validation-only artifact receipts                                  |
| `release-shaped-build-validation` for Python or JavaScript/TypeScript | Ubuntu                                                        | Produces validation-only artifact receipts                                  |
| `workflow-release-tooling-validation`                                 | Ubuntu unless affected tooling requires Windows evidence      | May fan out to related ecosystem runners when scope requires                |
| `evidence-aggregation`                                                | Ubuntu                                                        | Reads plan and receipts only                                                |

All runners provision tools through `mise` where practical. The concrete command
lines and helper scripts are implementation-owned, but they must run the
repository's existing ecosystem gates for selected scopes.

## 13. Release-Shaped Artifact Validation

For descriptor-backed subjects, release-shaped validation derives artifact
obligations from release descriptors and the existing workflow-release artifact
model.

Each artifact obligation records:

```yaml
artifact-obligation-id: string
subject-id: string
descriptor-path: string
profile-coverage: [string]
artifact:
    kind-family: string
    concrete-kind: string
    logical-artifact-role: string
    variant-dimensions: object
credential-posture: credential-free | unsigned-equivalent | unavailable
expected-evidence-category: release-shaped-artifact
```

Rules:

- The obligation set is the union required by all declared profiles.
- `profile-coverage` values are descriptor-declared profile identifiers; current
  descriptors use `buddy` and `official`, but the field is not a closed enum.
- No publish nodes, target remote state, overwrite policy, release tags, or GitHub
  Release operations are planned.
- If a descriptor is invalid enough to prevent derivation, planning fails closed.
- If descriptor-validation work is executable and fails, the corresponding
  descriptor-validation work group records a blocking validation failure.
- If a shape cannot be trusted without release-only credentials or side effects,
  the work group records a blocking validation failure.
- Artifact validation receipts are validation-only and inadmissible as immutable
  release proof.

## 14. Evidence and Receipt Files

Every executable work group emits one validation receipt:

```yaml
common-envelope: inherited
api-version: three.ci.validation.receipt/v1alpha1
kind: ci-validation-receipt
plan-id: string
work-group-id: string
mode: pull_request | push | scheduled_full
validation-tree:
    commit-sha: string
affected-range:
    base-sha: string | null
    head-sha: string | null
coverage-target:
    type: subject | descriptor | tooling-surface | artifact-obligation
    id: string
outcome: success | blocking-failure | skipped
evidence:
    category: string
    artifact-refs: [string]
diagnostics: [diagnostic-code]
proof-admissibility: validation-only
```

Receipt rules:

- `plan-id` and `work-group-id` must match the validation plan.
- `proof-admissibility` is always `validation-only`.
- A receipt with `blocking-failure` contributes to a failing aggregated outcome.
- Missing required receipts contribute to a failing aggregated outcome.
- A receipt with `skipped` is valid only for non-required work groups.
- A required work group that produces `skipped` contributes to a failing
  aggregated outcome with `required-evidence-skipped`.
- A concrete job may upload additional logs, but logs are not a substitute for the
  machine-readable receipt.

The aggregation report uses:

```yaml
common-envelope: inherited
api-version: three.ci.validation.aggregate/v1alpha1
kind: ci-validation-aggregate
plan-id: string
verdict: passed | failed
reason:
    fail-closed: boolean
    missing-required-evidence: boolean
    skipped-required-evidence: boolean
    blocking-validation-failure: boolean
work-groups:
    required: integer
    succeeded: integer
    failed: integer
    skipped: integer
    missing: integer
proof-admissibility: validation-only
```

The aggregation report is the only CI-level verdict artifact. Workflow conclusion
must fail when `verdict` is `failed`.

## 15. Diagnostics

Planner and aggregation diagnostics use a small registered vocabulary:

| Diagnostic family                     | Producer                                    | CI verdict effect                                                                     |
| ------------------------------------- | ------------------------------------------- | ------------------------------------------------------------------------------------- |
| `range-unavailable`                   | planner                                     | fail-closed                                                                           |
| `unknown-change`                      | planner                                     | fail-closed                                                                           |
| `subject-unresolved`                  | planner                                     | fail-closed                                                                           |
| `dependency-impact-untrusted`         | planner                                     | fail-closed or ecosystem expansion if approved                                        |
| `fact-provider-untrusted`             | planner                                     | fail-closed                                                                           |
| `infrastructure-surface-unclassified` | planner                                     | fail-closed                                                                           |
| `descriptor-invalid`                  | planner or descriptor-validation work group | fail-closed when obligations cannot be derived; otherwise blocking validation failure |
| `artifact-shape-untrusted`            | release-shaped validation work group        | blocking validation failure                                                           |
| `known-non-impacting`                 | planner                                     | inspectable non-failure                                                               |
| `required-evidence-missing`           | aggregation                                 | failed verdict                                                                        |
| `required-evidence-skipped`           | aggregation                                 | failed verdict                                                                        |

New diagnostic families may be added during implementation only when they map to
one of the verdict effects above or the low-level design is updated.

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

| Scenario                                                      | Expected evidence                                                                                                                                                     |
| ------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Project-scoped descriptor-backed change                       | Plan selects direct subject, safe downstream subjects, descriptor obligation, ecosystem gates, release-shaped artifact obligations, receipts, and passing aggregation |
| Project-scoped validation-only change                         | Plan selects validation-only subject and ecosystem gates, no publish or release-shaped artifact obligation unless descriptor-backed                                   |
| Ecosystem-scoped change                                       | Plan selects all active subjects in ecosystem and descriptors for descriptor-backed subjects                                                                          |
| Workflow-release infrastructure change                        | Plan selects affected tooling surface, related subjects/ecosystems, and all required discovered descriptors                                                           |
| Known global change                                           | Plan selects scheduled-full-equivalent scope with global provenance                                                                                                   |
| Scheduled full run                                            | Plan selects full repository scope with scheduled provenance                                                                                                          |
| Known non-impacting change with no executable checks          | Lightweight-only plan passes without heavy work and remains inspectable                                                                                               |
| Known non-impacting change with executable lightweight checks | Lightweight work receipts are required for pass                                                                                                                       |
| Unknown path                                                  | Fail-closed plan, failing aggregation/workflow conclusion, no validation work groups                                                                                  |
| Invalid descriptor blocking derivation                        | Fail-closed planning or blocking descriptor-validation failure according to derivability                                                                              |
| Missing receipt                                               | Aggregation fails with `required-evidence-missing`                                                                                                                    |
| Untrusted artifact shape                                      | Blocking validation failure, no release-proof admissibility                                                                                                           |
| Untrusted PR context                                          | No publication credentials, release environment, or OIDC publish permission exposed                                                                                   |

These scenarios are acceptance contracts, not prescribed test framework or file
layout. The implementer may choose the concrete test harness.

## 18. Implementation-Owned Boundaries

The following remain implementation-owned for the single senior engineer:

- internal planner module boundaries and private data structures;
- concrete command lines for ecosystem gates and descriptor validation;
- exact workflow job identifiers when they preserve the logical sequence;
- reusable workflow, composite action, or helper script decomposition;
- exact JSON Schema file locations and type-generation approach;
- temporary directories, artifact upload names, and log formatting;
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

## 19. Outcome

This low-level design gives the implementer a concrete handoff baseline for CI
affected validation without prescribing code internals. It freezes the workflow
entry boundary, logical job sequence, request and plan files, subject snapshot
shape, fact-provider realization, semantic path classification families, scope
resolution rules, work-group selectors, execution mapping, artifact-validation
obligations, validation-only receipts, diagnostics, HK relationship, and
acceptance evidence. Concrete code structure and command implementation remain
owned by the senior engineer as long as these contracts are preserved.
