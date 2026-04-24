# Workflow Release Requirements-Phase Review

## Purpose

This page reviews the workflow-release initiative from the perspective of a
requirements analyst: what a normal project should settle during requirements
analysis, what this initiative has already settled, what should be deferred to
design, and what requirement items still need to be added.

## What Requirements Analysis Normally Needs to Determine

For a typical project, the requirements phase should freeze the following
business-level information:

1. **Business objective and success criteria**
    - Why the project exists.
    - What business problem it solves.
    - What counts as success for the first accepted milestone.
2. **Stakeholders, roles, and authority boundaries**
    - Who initiates, approves, owns, operates, and consumes the capability.
    - Which decisions belong to product, security, operations, or project owners.
3. **Scope and exclusions**
    - What is in scope, out of scope, and deferred.
    - Which domains, systems, projects, and user groups are covered.
4. **Business scenarios and trigger conditions**
    - The key use cases the system must support.
    - When the process starts, who starts it, and which paths are exceptional.
5. **Functional business rules**
    - The rules that govern participation, allowed actions, and decision logic.
    - The source of truth for those rules.
6. **External targets and interface obligations**
    - Which external systems or target platforms matter.
    - What contractual obligations apply to those integrations.
7. **Non-functional and compliance requirements**
    - Security, reliability, auditability, traceability, performance, and
      maintainability expectations.
8. **Failure-handling expectations**
    - What should happen on partial failure, rerun, rollback, or cancellation.
9. **Acceptance baseline**
    - The measurable conditions that mark the end of the requirements phase and
      the acceptance of the first delivery scope.

The requirements phase should answer **what and why**. It should avoid freezing
unnecessary details about **how** unless those details materially change the
business outcome.

## What Our Initiative Has Already Settled

The current workflow-release requirement baseline already covers several key
items well:

1. **Scope**
    - All public projects are included.
    - `qidian-novel-downloader` and `vscode-copilot-telegram-hook` are the
      explicitly included private projects.
2. **Participation rule**
    - A project participates only if it owns a release descriptor file.
    - No descriptor means the workflow must skip the project.
3. **Profile rule**
    - Every in-scope project supports both `buddy` and `official`.
    - Both profiles must be explicit.
    - A profile may legitimately have zero publish targets.
    - If a profile has any non-GitHub-Release target, it must also include
      GitHub Release in that same profile.
4. **Artifact rule**
    - Target-specific packaging may vary.
    - Binary production must remain canonical and unified to avoid inconsistent
      outputs.
    - A single canonical build for one binary variant may emit both the binary
      and its related package or installer outputs.
5. **Security rule**
    - OIDC or trusted publishing is mandatory where supported.
    - GitHub Packages is the known in-scope secretless exception path that uses
      `GITHUB_TOKEN` rather than OIDC.
6. **Approval rule**
    - `buddy` is `write+` without extra approval.
    - `official` is `maintain+` with a second approval step.
    - In current scope, that approval step uses GitHub protected-environment
      required reviewers with self-review prevention enabled.
    - If that protected environment keeps administrator bypass enabled, `admin`
      may still use GitHub's native bypass path.
7. **Initial lifecycle rule**
    - The first delivery scope prioritizes manual `workflow_dispatch`
      initiation.
    - One workflow-dispatch run may target one or more projects selected by
      input parameters.
    - `buddy` and `official` are separate workflow entry points.
    - The first delivery scope requires whole-release rerun.
    - The first delivery scope requires dry-run validation mode.
    - The first delivery scope does not require single-target retry.
    - The first delivery scope does not require a repo-defined supersession
      model across release requests.
    - If optional native duplicate-run cancellation is used, duplicate means the
      same workflow entry point and the same commit, regardless of project
      subset selection or other inputs.
8. **Initial failure rule**
    - The first delivery scope may preserve partial success.
    - The first delivery scope allows manual remediation and does not mandate
      automatic rollback.
9. **Target-scope rule**
    - GitHub Release is mandatory for any non-zero-target profile, while a
      zero-target profile may omit it.
    - Package targets remain explicitly project-declared; there is no repo-wide
      default registry mapping.
    - GitHub Release semantics are fixed as `buddy` = pre-release and
      `official` = release.
    - `buddy` and `official` must not share the same package registry under the
      same published package name.
10. **Acceptance rule**
    - The first delivery scope must be accepted against real projects.
    - Acceptance must include real publication, including at least one real
      `official` publication.
    - Acceptance must prove both `buddy` to `official` promotion and direct
      `official` publication.

These are all proper requirements-phase outcomes because they define business
constraints and decision rules rather than implementation mechanics.

## What Should Be Excluded From Requirements Analysis for This Initiative

Several topics have already appeared in discussion, but they belong to design or
implementation rather than to requirements analysis:

1. **Descriptor syntax details**
    - Exact filename such as `release.json`.
    - JSON versus YAML versus TOML.
    - Exact field names, nesting rules, and serialization details.
2. **Schema engineering choices**
    - Inheritance, reuse, anchors, includes, defaults, or normalization strategy.
    - Validation-library choice and error-reporting format.
3. **Workflow construction details**
    - Exact GitHub Actions YAML layout.
    - Job DAG shape, reusable-workflow boundaries, action selection, and matrix
      expansion algorithms.
4. **Execution plumbing**
    - Exact command-line wrappers, environment variable names, cache layout, and
      artifact directory conventions.
5. **Code organization**
    - Which scripts live where.
    - Whether to extend current scripts or replace them.

These choices matter, but they are solutions to the requirements rather than the
requirements themselves. Freezing them now would prematurely narrow the design
space.

## What Needs To Be Added to the Requirements Phase

Compared with a standard requirements checklist, the remaining unresolved scope
is now small and mostly design-facing.

### 1. Release trigger and lifecycle model

Replay handling has now been narrowed to automatic skip detection plus
idempotent retry, without extra operator-choice controls. The release-request
scope is also now frozen as multi-project workflow dispatch within one profile
entry point at a time.

This is no longer a major requirements gap unless new lifecycle scenarios appear.

### 2. Supported target taxonomy for the first delivery scope

This is no longer an empty gap. The business side has now frozen that:

- GitHub Release is mandatory for any non-zero-target profile, while a
  zero-target profile may omit it;
- the first delivery scope must cover the GitHub Release, NuGet, PyPI, npm, and
  RubyGems families;
- package targets remain project-declared rather than repo-defaulted;
- GitHub Packages support is only a capability boundary, not a default mapping;
- GitHub Packages publication is authenticated through `GITHUB_TOKEN` rather
  than through OIDC trusted publishing;
- Python is the known exception where GitHub Packages does not provide the
  package target, so Python `buddy` falls back to GitHub Release and Python
  `official` package publication uses PyPI when declared.
- immutable registries may not be shared across profiles under the same
  published package name.

This target-scope area is now effectively closed for requirements purposes.

### 3. Canonical binary-variant semantics

This is now much narrower than before. We have already frozen that:

- binaries for the same declared variant must stay canonical and unified;
- one canonical build may emit the binary and the related package or installer
  outputs for that same variant;
- the requirement is to forbid divergent recompilation per target, not to force
  a separate build stage and packaging stage.

Any remaining variant-shape questions now belong primarily to descriptor and
workflow design rather than to missing business intent.

### 4. Versioning and immutability rules

This area is also largely closed at the requirements level. The business side
has now frozen that:

- version identity is commit-centric rather than profile-centric;
- every in-scope project is expected to expose that commit-centric version
  identity through NBGV integrated into its ecosystem-native build system;
- `official` is the higher-status freezing state;
- `buddy FORCE` is an explicit but exceptional overwrite path before a version
  reaches `official`;
- same-registry same-name cross-profile package promotion is prohibited.

Any remaining work here is primarily design: how to encode these already-frozen
rules in the descriptor and workflow logic.

### 5. Failure, rollback, and partial-success expectations

This area has also been narrowed substantially:

- there are no exceptional cases in the first delivery scope that require
  automatic rollback;
- post-run manual remediation does not introduce an extra workflow-level closure
  or visibility mechanism.

This is no longer a major requirements gap unless compliance needs change later.

### 6. Auditability expectations

This has now been narrowed substantially:

- GitHub-native workflow history, approvals, and run records are considered
  sufficient for the current requirements baseline.
- The initiative does not currently require an extra repo-owned release-record
  artifact.

This is no longer a major requirement gap unless compliance needs change later.

### 7. Acceptance criteria for the first delivery scope

This is no longer a blank gap. The business side has now frozen that acceptance
must:

- use real projects instead of only synthetic workflow tests;
- cover a C# library, both C# app packaging paths, Python, Node, and Ruby;
- include real publication rather than only dry-run validation;
- include at least one real `official` publication;
- prove both same-commit `buddy` to `official` promotion and direct
  `official` publication;
- explicitly prove multi-project `workflow_dispatch` scope;
- explicitly prove dry-run or validation-only behavior;
- explicitly prove whole-release rerun behavior against the same input,
  including rerun after partial success on immutable targets;
- explicitly prove manual cancellation behavior;
- explicitly prove the approval boundary between `buddy` and `official`,
  including protected-environment required review for `official`, self-review
  prevention for the initiating actor, and administrator bypass behavior when
  that bypass path remains enabled;
- include at least one real GitHub Packages publication if that target is
  declared by a representative first-delivery project.

## Summary Judgment

### Already appropriate for requirements phase

- in-scope project set;
- descriptor-gated participation;
- explicit `buddy` and `official` profiles;
- target-specific packaging with unified binary production;
- project-kind-specific packaging variation even within one target family;
- canonical-build semantics that allow one build to emit both binary and
  packaging outputs for the same variant;
- secretless publication posture for currently known targets, including
  `GITHUB_TOKEN` for GitHub Packages;
- role-based approval and initiation rules;
- first-delivery-scope manual triggering priority;
- multi-project workflow-dispatch scope within one profile entry point;
- workflow-managed Git tag creation for both `buddy` and `official`;
- whole-release rerun plus dry run, without mandatory single-target retry;
- partial-success preservation with manual remediation instead of mandatory
  rollback;
- manual operator cancellation, without a mandatory repo-defined supersession
  model;
- shared visible handling rules across `buddy` and `official` for failure,
  cancellation, and partial success;
- commit-centric version identity and `official` freeze semantics;
- multi-target-class scope from the start;
- ecosystem-specific target families instead of a generic registry bucket;
- conditionally mandatory GitHub Release for every non-zero-target profile, with
  fixed `buddy` / `official` release semantics;
- project-declared registry targets with no repo-wide default mapping;
- immutable registries excluded from same-name cross-profile promotion;
- acceptance based on real projects and real publication rather than on dry-run
  evidence alone;
- explicit acceptance proof for multi-project dispatch, dry-run, rerun,
  cancellation, approval boundaries, and GitHub Packages when in scope;
- GitHub-native audit history as the current sufficient audit baseline.

### Should be deferred to design phase

- exact descriptor filename and syntax;
- exact schema shape;
- exact workflow YAML and job structure;
- exact script layout and command plumbing.

### Must be added before requirements sign-off

No major unresolved requirement gaps remain at the moment. The remaining work is
primarily design: descriptor syntax, schema shape, workflow structure, and the
mechanics that realize the already-frozen business rules.

## Related Pages

- [Workflow Release Requirements Baseline](./workflow-release-requirements-baseline.md)
- [Repository Release Landscape](./repository-release-landscape.md)
