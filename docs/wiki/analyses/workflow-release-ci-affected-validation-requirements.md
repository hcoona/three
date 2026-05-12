# Workflow Release CI Affected Validation Requirements

## Purpose

This page captures the confirmed requirements for rebuilding repository CI as an
affected validation entry point that belongs to the workflow-release system. It
locks the requirements discussed before starting high-level or low-level design.

The goal is not to define the final workflow YAML, affected-plan schema, or
executor implementation. Those are design-phase outputs.

## Confirmed Direction

- CI affected validation is another entry point into the workflow-release
  system.
- It must not become a separate CI system with its own independent project list,
  artifact model, or build semantics.
- `buddy`, `official`, and CI validation should share the same underlying
  project discovery, descriptor interpretation, artifact/build contracts, and
  ecosystem adapter concepts wherever possible.
- CI differs from `buddy` and `official` primarily in its input and side-effect
  policy:
    - CI input is a changed-file set, normally derived from a base commit and a
      head commit.
    - Scheduled full CI input is the repository state at the scheduled commit,
      without affected narrowing.
    - `buddy` and `official` input is a release request.
    - CI performs validation only.
    - `buddy` and `official` may perform publication side effects according to
      their existing release rules.

## Confirmed Scope

- The new CI must support the repository's active monorepo layout under `src/`,
  `src/lab/`, and `tests/`.
- CI project discovery should be based on the same release-aware project catalog
  and descriptors used by workflow release, rather than a hand-maintained CI-only
  list.
- All active build/test projects must participate in CI affected validation,
  including projects that are not releasable and do not own release descriptors.
- Non-releasable active projects are validation subjects, not publish subjects.
  They must be represented without inventing a second release descriptor truth.
- For non-releasable validation subjects, the authoritative inclusion rule is
  ecosystem workspace or solution metadata under the active monorepo roots,
  combined with a repository-level explicit exclusion rule for projects that
  should not participate in CI validation.
- The first CI scope should cover project validation, ecosystem validation, and
  shared workflow-release tooling validation.
- The first CI scope must include a scheduled full validation entry point so
  repository drift can still be detected when no pull request or push activity
  occurs for a while.

## Confirmed Change Classification Model

Changed files must be classified into at least these impact categories:

1. **Project-scoped changes** affect one or more identifiable projects.
2. **Ecosystem-scoped changes** affect an entire language or build ecosystem.
3. **Workflow-release infrastructure changes** affect shared release planning,
   build, publish, contract, descriptor, or smoke validation behavior.
4. **Global changes** are known to affect more than one project or ecosystem.
5. **Known non-impacting changes** are known not to affect build, test, release
   descriptors, workflow-release tooling, or ecosystem behavior.
6. **Unknown changes** cannot be safely assigned to a project, ecosystem, or
   known global rule.

The classifier must produce a conservative result. Known broad-impact changes
must select the complete affected broad scope. Unknown or unclassifiable changes
must fail planning closed rather than running partial validation.

Known global changes are scheduled-full-equivalent for validation scope: they
must validate all active build/test projects in all ecosystems, all discovered
release descriptors, and relevant workflow-release tooling, without schedule-
specific metadata.

Known non-impacting changes may produce an inspectable lightweight validation
plan with no heavy validation scope. They may run lightweight policy or
formatting checks where applicable, but they must not be silently skipped.

## Confirmed Project-Scoped Behavior

- A project-scoped change should validate only the affected project set, not the
  entire ecosystem by default.
- The affected project set must include each directly changed project.
- The affected project set must also include downstream projects that depend on a
  changed project when the ecosystem dependency graph can identify such
  downstream impact.
- If downstream dependency impact cannot be computed safely for an ecosystem, the
  design must either use a requirement-approved ecosystem-level expansion for
  that ecosystem or fail planning closed. It must not silently validate only a
  partial downstream set.
- Project-scoped validation must use the same build shape that release would use
  for the affected project where a release descriptor exists.
- Project-scoped validation must also run the existing ecosystem gates that apply
  to the affected validation subjects, such as build, tests, lint, formatting
  checks, and type checks where those gates already exist for that ecosystem.

## Confirmed Ecosystem-Scoped Behavior

- An ecosystem-scoped change must validate all active projects in that ecosystem.
- Examples of ecosystem-scoped inputs include shared build configuration,
  workspace configuration, lock files, package management configuration, and
  ecosystem-level tool configuration.
- Ecosystem-scoped validation should not be reduced to only projects whose files
  changed, because shared configuration can affect projects that did not change.

## Confirmed Scheduled Full Validation Behavior

- CI must support a scheduled full validation entry point in addition to affected
  validation for pull requests and pushes.
- Scheduled full validation must validate all active build/test projects in all
  ecosystems.
- Scheduled full validation must validate all discovered release descriptors.
- Scheduled full validation must use the same validation obligations as the
  selected full repository CI scope, including existing ecosystem gates and
  release-shaped artifact and receipt validation for descriptor-backed projects.
- Scheduled full validation must remain side-effect free. It must not publish to
  registries, create GitHub Releases, or create or move release tags.
- The exact schedule cadence is a design/configuration decision, but the
  requirement is that repository validation must not depend exclusively on new
  pull request or push activity.

## Confirmed Workflow-Release Infrastructure Behavior

- Changes to workflow-release contracts, authoring validation, planning, build
  execution, publish execution, smoke projects, descriptor schema documentation,
  target catalog behavior, or workflow orchestration must validate the affected
  workflow-release tooling surface.
- If such a change can affect multiple ecosystems or artifact kinds, CI must
  expand to the related ecosystems or representative smoke coverage.
- If the affected workflow-release infrastructure surface cannot be classified
  safely, CI must fail planning closed rather than running partial validation.

## Confirmed Build and Validation Semantics

- CI must not run a separate simplified build path that can drift from release.
- CI validation should reuse the same build recipes, artifact contracts, and
  ecosystem adapters used by `buddy` and `official` as much as practical.
- CI must run the existing ecosystem validation gates that are already part of
  the repository's validation model for the selected scope, including build,
  tests, lint, formatting checks, and type checks where those gates exist.
- CI may run in a validation mode that produces or verifies build artifacts and
  receipts without publishing anything externally.
- CI must not push to package registries.
- CI must not create GitHub Releases.
- CI must not create or move release tags.
- CI must not require official release approval because it has no publication
  side effects.
- CI validation should be able to prove that the artifacts it validates have the
  same shape that `buddy` or `official` would later publish for the same project
  and descriptor.
- For projects with release descriptors, CI must validate the release-shaped
  build artifacts and receipts needed to prove descriptor-to-artifact shape
  consistency.
- CI must not validate publication execution or remote publish state.
- For projects with release descriptors, CI build and receipt validation must
  cover the union of artifacts required by all declared profiles. This validates
  that the `buddy` and `official` publication flows would have their required
  build outputs available without executing those publication flows.

## Confirmed CI Event, Schedule, and Trust Scope

- The first CI affected validation entry point must support both `pull_request`
  and `push` events.
- The first CI validation workflow must also support a scheduled full validation
  trigger.
- `pull_request` validation must derive the changed-file set from the pull
  request base and head commits.
- `push` validation must derive the changed-file set from the pushed range for
  the branch.
- Scheduled full validation must not depend on changed-file classification.
- CI validation must not rely on publication credentials or release approval.
- Untrusted pull request contexts must not receive publishing credentials or any
  other secret needed only for side-effecting release.
- Planning and fact collection must not require publication credentials, release
  privileges, or OIDC publish permissions.
- Planning and fact collection may use repository and ecosystem tooling to obtain
  facts, but planning remains validation-only and side-effect-free by contract.
- The accepted complexity direction is to avoid turning CI planning or fact
  collection into a heavyweight sandbox or security subsystem unless requirements
  are explicitly reopened. The intended controls are no publication credentials,
  no release privileges, validation-only planning, human review, and fail-closed
  scope handling.
- If an event payload cannot provide a trustworthy base/head range for affected
  planning, CI must fail planning closed rather than running a partial affected
  validation.

## Confirmed Runner and Ecosystem Expectations

- .NET validation in GitHub Actions should run on Windows runners.
- Python and JavaScript/TypeScript validation may run on Ubuntu runners.
- The design may introduce multiple jobs or matrices, but the runner split must
  preserve the repository's ecosystem expectations.
- Ecosystem tools managed by `mise` should remain the preferred way to provision
  toolchains.
- `hk` should be used to left-shift lightweight checks where practical so
  developers can catch common validation failures before pushing.
- `hk` checks should not become so heavyweight that ordinary local development
  feedback is significantly degraded. Heavy validation remains a CI
  responsibility.

## Confirmed Outputs

- CI affected validation should emit a machine-readable affected validation plan
  or equivalent normalized artifact.
- Scheduled full validation should emit a machine-readable full validation plan
  or equivalent normalized artifact.
- The plan should make the selected validation scope inspectable, including:
    - directly changed projects;
    - ecosystem-wide selections;
    - expanded downstream project selections;
    - known broad-scope expansions;
    - known non-impacting lightweight selections;
    - fail-closed classification reasons;
    - scheduled full-run selection;
    - validation jobs or build groups to run.
- CI should emit validation evidence or receipts that can be checked by later
  jobs and inspected after failures.
- These CI outputs are validation artifacts only. They must not be treated as
  immutable publish proofs.
- CI validation evidence and release immutable proof must remain strictly
  separate. CI validation artifacts must not become `buddy` or `official`
  publication proof.
- CI-produced receipts and evidence must carry validation-only provenance and
  must be excluded from release immutable-proof lookup and publication
  admissibility paths.

## Confirmed Safety and Fallback Rules

- Detection failure must not lead to skipped validation.
- Unknown path classification must fail planning closed rather than running
  partial validation.
- Invalid project descriptors must fail validation rather than silently removing
  those projects from the CI scope.
- If a project-level change cannot be mapped to a known project safely, CI must
  fail planning closed rather than running a partial affected validation.
- If an ecosystem-level rule is missing for a changed shared file, CI must fail
  planning closed until the rule is added.
- Project-scoped runs must validate release descriptors for the affected
  descriptor-backed projects.
- Ecosystem-scoped runs must validate descriptors for descriptor-backed projects
  in the selected ecosystem.
- Global runs must validate all discovered release descriptors.
- Global runs must validate all active build/test projects in all ecosystems.
- Scheduled full validation runs must validate all discovered release
  descriptors.
- Workflow-release infrastructure changes that can affect descriptor semantics,
  authoring validation, planning, contracts, build execution, publish execution,
  or smoke validation must validate all discovered release descriptors.

## Non-Goals for This Requirements Baseline

- This page does not define the final affected-plan schema.
- This page does not define exact path-matching rules.
- This page does not define exact GitHub Actions workflow files.
- This page does not define the exact scheduled full validation cadence.
- This page does not define release proof reuse because CI validation evidence is
  not eligible to become release immutable proof.
- This page does not require project-graph affected builds to be perfectly
  minimal in the first implementation.
- This page does not require designing a heavyweight sandbox for CI planning or
  fact collection.
- This page does not require replacing existing `hk` local validation behavior or
  moving heavyweight CI validation into local hooks.

## Design Questions Deferred

- What is the exact affected-plan JSON shape?
- Which paths are project-scoped, ecosystem-scoped, release-infrastructure
  scoped, or global?
- How should downstream dependency closure be computed for .NET, Python, and
  JavaScript/TypeScript?
- Which workflow-release tooling changes require smoke-only validation, and which
  require full ecosystem validation?
- How should ecosystem-discovered non-releasable validation subjects and explicit
  exclusions be represented without making those subjects publish subjects?
- Which lightweight checks should be left-shifted into `hk`, and which checks are
  too heavyweight for local hook execution?
- What cadence should scheduled full validation use?
