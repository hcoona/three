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
4. **Artifact rule**
    - Target-specific packaging may vary.
    - Binary production must remain canonical and unified to avoid inconsistent
      outputs.
5. **Security rule**
    - OIDC or trusted publishing is mandatory where supported.
    - There are currently no known in-scope targets that lack that support.
6. **Approval rule**
    - `buddy` is `write+` without extra approval.
    - `official` is `maintain+` with a second approval step.
    - `official` self-approval is allowed only for `admin`, not for plain
      `maintain`.
7. **Initial lifecycle rule**
    - Phase 1 prioritizes manual `workflow_dispatch` initiation.
    - Phase 1 requires whole-release rerun.
    - Phase 1 requires dry-run validation mode.
    - Phase 1 does not require single-target retry.
8. **Initial failure rule**
    - Phase 1 may preserve partial success.
    - Phase 1 allows manual remediation and does not mandate automatic rollback.

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

Compared with a standard requirements checklist, our current baseline is still
missing several business-level items.

### 1. Release trigger and lifecycle model

We still need to define the remaining business scenarios, for example:

- cancellation or supersession rules;
- tag-driven initiation in a later phase;
- whether replay detection is purely automatic or may need operator choices in
  some cases.

This is still requirements work because it defines expected user-visible
behavior.

### 2. Supported target taxonomy for milestone 1

We know targets are descriptor-driven, but we still need to define which target
classes the first milestone must support as business scope, for example:

- GitHub Release;
- GitHub Packages NuGet;
- NuGet.org;
- PyPI or TestPyPI;
- npm or other package registries.

Without that list, milestone scope and acceptance remain ambiguous.
What is already settled is that the first delivery scope must cover multiple
target classes rather than shipping as a single-target-only solution.
It is also settled that those target classes should be modeled by ecosystem
family, and that even the same target family may involve different packaging
paths or target-specific name transforms for different project kinds.

### 3. Canonical binary-variant semantics

We have already established that binaries must remain unified, but we still need
to define what counts as a legitimate variant:

- RID or host-target variants;
- debug versus release exclusion;
- installer derived from a binary versus a distinct shipped binary;
- whether one profile may intentionally publish multiple canonical binary
  variants.

This is requirement work because it constrains the allowed business meaning of a
release.

### 4. Versioning and immutability rules

The release process needs business rules for:

- whether both profiles publish the same version identity;
- whether buddy may publish preview or prerelease versions only;
- whether reruns must be idempotent;
- whether published artifacts are immutable once visible externally.

These are release-policy requirements, not mere implementation details.
Part of this is now settled: version identity is commit-centric, `official` is
the higher-status freezing state, and `buddy FORCE` is an explicit but
exceptional overwrite path before a version reaches `official`.

### 5. Failure, rollback, and partial-success expectations

We still need explicit business decisions for:

- what happens if GitHub Release succeeds but registry publication fails;
- whether `buddy` and `official` differ in their visible failure states;
- whether there are any cases where automatic rollback is still required;
- what operator obligations exist once a release is marked for manual
  remediation.

This is a requirement gap today.

### 6. Auditability and observability expectations

We have not yet frozen what must be observable or traceable, such as:

- which inputs produced a given release;
- which binary variant and packaging transforms were used;
- which identity published to each target;
- what audit trail must exist for approvals and final publication.

This belongs in requirements because it expresses compliance and operational
needs.

### 7. Acceptance criteria for the first milestone

This remains the clearest explicit gap. We still need measurable answers to
questions like:

- what subset of target types must work in phase 1;
- what subset of project kinds must work in phase 1;
- what evidence proves the descriptor-driven model is acceptable;
- what constitutes sign-off for the end of requirements and for the first
  implementation increment.

## Summary Judgment

### Already appropriate for requirements phase

- in-scope project set;
- descriptor-gated participation;
- explicit `buddy` and `official` profiles;
- target-specific packaging with unified binary production;
- project-kind-specific packaging variation even within one target family;
- OIDC-only publication posture for currently known targets;
- role-based approval and initiation rules;
- first-delivery-scope manual triggering priority;
- whole-release rerun plus dry run, without mandatory single-target retry;
- partial-success preservation with manual remediation instead of mandatory
  rollback;
- commit-centric version identity and `official` freeze semantics;
- multi-target-class scope from the start;
- ecosystem-specific target families instead of a generic registry bucket.

### Should be deferred to design phase

- exact descriptor filename and syntax;
- exact schema shape;
- exact workflow YAML and job structure;
- exact script layout and command plumbing.

### Must be added before requirements sign-off

- release trigger and lifecycle scenarios;
- first-delivery-scope supported target taxonomy;
- canonical binary-variant semantics;
- per-target transformation and packaging constraints that still need explicit
  cataloging;
- remaining versioning and immutability rules;
- remaining failure and rollback expectations;
- auditability expectations;
- acceptance criteria.

## Related Pages

- [Workflow Release Requirements Baseline](./workflow-release-requirements-baseline.md)
- [Repository Release Landscape](./repository-release-landscape.md)
