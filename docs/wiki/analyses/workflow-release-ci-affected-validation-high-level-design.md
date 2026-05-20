# Workflow Release CI Affected Validation High-Level Design

## Purpose

This page records the high-level design for CI affected validation as a new
workflow-release entry point. It follows the requirements locked in
[Workflow Release CI Affected Validation Requirements](./workflow-release-ci-affected-validation-requirements.md).

This page intentionally stays above middle-level design. It does not define the
validation-plan JSON schema, path classification tables, GitHub Actions YAML,
runner matrices, executor APIs, HK step lists, or dependency-graph algorithms.

## Design Inputs

The requirements baseline fixes these constraints:

- CI affected validation belongs to the workflow-release system rather than a
  separate CI system.
- CI supports `pull_request`, `push`, and scheduled full validation.
- Known global changes are scheduled-full-equivalent for validation scope.
- Known non-impacting changes may produce an inspectable lightweight validation
  plan, but must not be silently skipped.
- Unknown or unclassifiable changes fail planning closed rather than running
  partial validation.
- All active build/test projects participate in CI validation, including
  non-releasable validation-only subjects.
- Descriptor-backed projects validate release-shaped build artifacts and receipts
  for the union of artifacts required by all declared profiles.
- CI validation never validates publication execution or remote publish state.
- CI validation evidence and release immutable proof are strictly separated.
- HK should left-shift lightweight checks while avoiding heavyweight default
  local execution.

HK-specific source facts are captured in
[Workflow Release CI HK Source Page](../sources/2026-05-11-workflow-release-ci-hk-source-page.md).

## High-Level Architecture Decision

CI affected validation is a **planner-centric extension** of the existing
workflow-release architecture.

It adds a CI validation entry point and validation planning mode to the same
three-layer architecture already used by `buddy` and `official` release:

1. **Control plane** — accepts CI events, normalizes mode-specific inputs, and
   invokes planning.
2. **Planning layer** — interprets repository requirements and emits a
   validation plan.
3. **Execution layer** — consumes the validation plan and runs validation work
   without publication side effects.

CI does not create a parallel project list, artifact model, or build semantics.

## Plan Model

CI emits a **sibling validation plan** rather than reusing a release plan with
publication disabled.

The validation plan shares workflow-release concepts such as projects,
artifacts, descriptors, build obligations, validation evidence, and execution
handoff. It remains distinct from a release plan because CI is driven by changed
files or scheduled full validation rather than a `buddy` or `official` release
request.

At this layer, the important boundary is:

- release plans represent publication intent;
- validation plans represent validation scope and validation obligations;
- validation plans never encode publication side effects.

The validation plan is the fully materialized, execution-authoritative artifact
for the selected validation scope. Execution consumes the validation plan and
must not recompute changed-file classification, selected validation subjects,
downstream expansion, descriptor-validation scope, or validation obligations.

The validation plan freezes planning provenance at the architecture boundary.
For affected validation, that provenance includes both the validation-tree
snapshot used for fact collection and the confirmed change-detection range used
for affected planning. For scheduled full validation, it includes the scheduled
validation-tree snapshot. In all modes, it also includes the CI mode and the
planner-owned validation-subject universe and fact snapshot identity needed for
execution and evidence handoff.

The validation plan is an inspectable machine-readable or equivalent normalized
artifact. Its selected scope and planning decisions must be visible to later
stages and operators, including selected subjects, broad-scope expansions,
known-non-impacting lightweight selections, fail-closed reasons, and validation
work groups. The exact representation remains a middle-level design concern.

Fail-closed is a first-class planning outcome. A fail-closed outcome preserves
planning provenance and reasons for inspection, but it does not authorize
validation execution.

The exact validation-plan schema is deferred to middle-level design.

## Control Plane Model

CI uses a **single control-plane family with modes**.

The supported modes are:

- pull request affected validation;
- push affected validation;
- scheduled full validation.

Scheduled full validation selects the full repository validation scope: all
active build/test projects in all ecosystems, all discovered release descriptors,
and the full repository validation obligations.

These modes share one architectural flow:

1. normalize event input;
2. invoke CI validation planning;
3. pass the resulting validation plan to execution;
4. preserve validation evidence for inspection.

Mode-specific event details, workflow YAML, concurrency policy, and exact
base/head derivation are deferred to middle-level design.

At the high-level event-normalization boundary, affected validation requires a
confirmed input range. If the control plane cannot establish a confirmed
base/head or pushed range for affected planning, planning fails closed rather
than running a partial validation. CI validation does not require publication
credentials, and pull request contexts without release authority must not receive
credentials or secrets that are needed only for side-effecting release.

Planning and fact collection use the same minimal authority boundary: no publication
credentials, no release privileges, and no OIDC publish permissions. They may use
repository and ecosystem tooling to obtain facts, but planning remains
validation-only and side-effect-free by contract. If fact collection cannot
produce a confirmed validation scope, planning fails closed.

This is also a complexity boundary for later design phases. CI planning and fact
collection should not be expanded into a heavyweight sandbox or security
subsystem without explicitly reopening requirements. The accepted control model
is no publication authority, validation-only planning, human review, and
fail-closed scope handling.

Changes to planning or discovery inputs such as workspace metadata, descriptors,
or dependency facts are handled by the normal classification model. This
high-level design does not add a separate rule that automatically expands, fails,
or sandboxes validation solely because those inputs changed. Such changes remain
subject to ordinary human review plus the existing no-publication-authority,
validation-only, confirmed-scope, and fail-closed boundaries.

Changes to policy-bearing CI planning code, such as the planner, classifier, or
fact-provider implementation, are also validated through the normal CI planning
model. The authoritative validation plan for such changes may be produced by the
validation-tree policy code being reviewed. This is an intentional maintainability
tradeoff so CI policy changes are validated by their resulting planning behavior,
rather than by a separate baseline planner that cannot exercise the
changed policy. This does not grant publication authority or release credentials;
if the changed policy cannot produce a confirmed validation scope, planning
fails closed.

## Planning Responsibility Model

CI uses a **central validation planner with ecosystem fact providers**.

The central planner owns interpretation of high-level CI requirements, including:

- project-scoped, ecosystem-scoped, workflow-release infrastructure, global,
  known non-impacting, and unknown classifications;
- known global scheduled-full-equivalent expansion;
- known non-impacting lightweight planning;
- ecosystem-scoped expansion;
- fail-closed behavior for unknown or unclassifiable changes;
- descriptor validation scope;
- downstream expansion for project-scoped changes;
- workflow-release infrastructure descriptor-validation obligations;
- selected validation subjects and validation obligations.

Ecosystem adapters provide facts rather than owning policy. Examples of facts
include active project discovery, workspace membership, project roots, and
dependency relationships.

This keeps cross-ecosystem policy centralized while still allowing ecosystem
tools to provide authoritative ecosystem-specific data.

Fact providers contribute bounded discovery facts only. Build, test, packaging,
release-shaped artifact validation, and other validation commands remain
execution-layer responsibilities.

For project-scoped validation, the planner includes directly changed projects and
downstream dependent projects when downstream impact can be computed safely. If
an ecosystem lacks an approved dependency fact provider, planning may use a
requirement-approved ecosystem-level expansion for that ecosystem. If an expected
dependency fact provider fails to read or parse required metadata, planning fails
closed rather than expanding from incomplete facts.

For ecosystem-scoped validation, the planner selects all active validation
subjects in the affected ecosystem and validates descriptors for
descriptor-backed projects in that ecosystem.

Invalid descriptors fail validation rather than silently removing
descriptor-backed projects from CI scope. Descriptor discovery or validation
failures keep the affected descriptor-backed projects visible to the validation
plan and evidence flow.

For workflow-release infrastructure changes that can affect descriptor semantics,
descriptor schema documentation, authoring validation, planning, contracts,
target catalog behavior, workflow orchestration, build execution, publish
execution, smoke validation, or other validation behavior, the planner selects
affected subjects from the unified validation project universe. That selection
includes validation-only subjects when the infrastructure change can affect their
build, test, lint, type-check, or validation obligations.

Descriptor validation is an additional obligation for workflow-release
infrastructure changes that can affect descriptor semantics, authoring
validation, planning, contracts, build execution, publish execution, or smoke
validation. For those changes, the planner includes validation of all discovered
release descriptors, but descriptor-backed projects are not the only possible
affected subjects.

Workflow-release infrastructure changes also validate the affected
workflow-release tooling surface. When an infrastructure change can affect
multiple ecosystems or artifact kinds, planning expands to the related ecosystems
and affected validation subjects. Representative smoke coverage does not
substitute for that broader validation. If the affected tooling surface or
affected validation subjects cannot be classified safely, planning fails closed.

## Project Universe Model

CI uses a **unified validation project universe with capabilities**.

The universe includes:

- descriptor-backed release-capable projects;
- non-releasable validation-only subjects.

Release descriptors grant release capability. They do not define the entire CI
validation universe. Non-releasable validation subjects are included by ecosystem
workspace or solution metadata under active monorepo roots, subject to explicit
repository-level exclusions.

The planning layer owns the normalized validation-subject universe and capability
assignment. Ecosystem providers contribute discovery and dependency facts; they
do not own separate project universes.

Validation-only subjects remain validation subjects only. They must not become
publish subjects.

The exact catalog representation for capabilities, validation-only subjects, and
exclusions is deferred to middle-level design.

## Execution Relationship

CI execution reuses release build execution for release-shaped outputs and uses
validation execution for existing ecosystem gates.

For descriptor-backed projects:

- CI validates build artifacts and receipts for the union of artifacts required
  by all declared profiles;
- CI reuses release-shaped build semantics so artifact shape does not drift from
  `buddy` or `official`;
- CI does not execute publish nodes, remote registry operations, GitHub Release
  operations, or release tag operations.

CI release-shaped validation also excludes release-only credentials and release-
only side effects, including signing, notarization, or privileged artifact
production capabilities that are not required for ordinary validation. CI should
use credential-free equivalents or unsigned validation artifacts to validate
artifact shape. If an artifact shape cannot be confirmed without release-only
credentials or side effects, execution records a blocking validation failure
rather than silently claiming equivalence.

For all selected validation subjects:

- CI runs existing ecosystem gates that apply to the selected scope, such as
  build, tests, lint, formatting checks, and type checks where those gates exist.
- Affected modes narrow the selected scope before execution. Once a subject or
  obligation is selected, the high-level validation semantics remain the same as
  scheduled full validation unless a lighter validation class is explicitly
  defined by requirements.

Runner selection and tool provisioning remain execution concerns, but the
execution design must preserve the high-level ecosystem expectations: .NET
validation in GitHub Actions runs on Windows runners, Python and
JavaScript/TypeScript validation may run on Ubuntu runners, and toolchains should
be provisioned through `mise` where practical.

The exact mapping from validation obligations to jobs, commands, receipts, and
executor calls is deferred to middle-level design.
This includes the concrete GitHub Actions job topology. A validation plan may
describe logical work groups or execution selectors without requiring one
workflow job, matrix row, or runner allocation per logical work group.

## Validation Evidence Boundary

CI validation evidence is **strictly separate** from release immutable proof.

CI evidence may be used to understand validation results and to connect CI
execution within a CI run. It must not be reused as `buddy` or `official`
publish proof. CI evidence references the validation plan identity and resolved
planning provenance so operators can identify the exact planned scope or
fail-closed outcome it belongs to.
CI-produced receipts and evidence must carry validation-only provenance and must
be excluded from release immutable-proof lookup and publication admissibility
paths.

This separation avoids coupling CI authority boundaries, scheduled runs, pull request
events, and local validation evidence to release publication authorization.
It also means high-level design requires checkable validation evidence, not a
proof-grade release artifact boundary or a dedicated concrete job for every
logical validation selector.

## HK Left-Shift Boundary

HK is a **planner-aligned lightweight preflight**.

The high-level intent is:

- HK should align with CI lightweight planning and classification where practical
  so local feedback matches CI scope interpretation.
- HK should default to lightweight gates.
- Heavy validation, including full test suites, full builds, release-shaped
  artifact production, packaging, and scheduled-full-equivalent validation,
  should remain explicit or CI-only.
- HK success must not be treated as CI success.

HK source facts supporting lightweight and heavyweight selection mechanisms are
captured in
[Workflow Release CI HK Source Page](../sources/2026-05-11-workflow-release-ci-hk-source-page.md).

Exact HK profiles, steps, local commands, and integration points are deferred to
middle-level design.

## High-Level Flow

At the architecture layer, CI validation follows this flow:

1. The control plane receives a CI event and determines the CI mode.
2. The control plane normalizes event input and invokes the validation planner.
3. The validation planner combines changed-file or scheduled input with project
   universe facts and ecosystem facts.
4. The validation planner emits a validation plan.
5. The execution layer consumes the validation plan and runs the selected
   validation obligations.
6. The run preserves validation evidence for inspection.

This flow is intentionally expressed without defining plan schema, job topology,
or command-level execution details.

## Explicit Deferrals to Middle-Level Design

The following are not high-level design decisions and remain deferred:

- validation-plan JSON shape;
- exact path classification rules;
- exact representation of project capabilities, validation-only subjects, and
  explicit exclusions;
- exact dependency-closure algorithms for .NET, Python, JavaScript/TypeScript,
  Ruby, and other ecosystems;
- exact GitHub Actions workflow files and job matrices;
- exact scheduled full validation cadence;
- exact validation evidence and receipt formats;
- exact executor interfaces;
- exact HK profiles, steps, and lightweight/heavyweight command mapping.

## Outcome

This high-level design extends the existing planner-centric workflow-release
architecture without introducing a separate CI truth. It defines CI as a
side-effect-free validation entry point with a sibling validation plan, centralized
planning policy, ecosystem fact providers, a unified validation project universe,
release-shaped build reuse, strict separation from release proof, and
planner-aligned lightweight HK preflight.
