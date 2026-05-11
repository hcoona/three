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
- Projects that are not releasable may still need validation coverage when they
  are part of the repository's active build/test surface. Design must explicitly
  decide how those projects are represented without duplicating release
  descriptor truth.
- The first CI scope should cover project validation, ecosystem validation, and
  shared workflow-release tooling validation.

## Confirmed Change Classification Model

Changed files must be classified into at least these impact categories:

1. **Project-scoped changes** affect one or more identifiable projects.
2. **Ecosystem-scoped changes** affect an entire language or build ecosystem.
3. **Workflow-release infrastructure changes** affect shared release planning,
   build, publish, contract, descriptor, or smoke validation behavior.
4. **Global or unknown changes** cannot be safely assigned to a narrower scope.

The classifier must produce a conservative result. When a file cannot be safely
classified, CI must expand validation rather than skip work.

## Confirmed Project-Scoped Behavior

- A project-scoped change should validate only the affected project set, not the
  entire ecosystem by default.
- The affected project set must include each directly changed project.
- The affected project set must also include downstream projects that depend on a
  changed project when the ecosystem dependency graph can identify such
  downstream impact.
- If downstream dependency impact cannot be computed safely for an ecosystem, the
  design must choose a conservative fallback for that ecosystem.
- Project-scoped validation must use the same build shape that release would use
  for the affected project where a release descriptor exists.

## Confirmed Ecosystem-Scoped Behavior

- An ecosystem-scoped change must validate all active projects in that ecosystem.
- Examples of ecosystem-scoped inputs include shared build configuration,
  workspace configuration, lock files, package management configuration, and
  ecosystem-level tool configuration.
- Ecosystem-scoped validation should not be reduced to only projects whose files
  changed, because shared configuration can affect projects that did not change.

## Confirmed Workflow-Release Infrastructure Behavior

- Changes to workflow-release contracts, authoring validation, planning, build
  execution, publish execution, smoke projects, descriptor schema documentation,
  target catalog behavior, or workflow orchestration must validate the affected
  workflow-release tooling surface.
- If such a change can affect multiple ecosystems or artifact kinds, CI must
  expand to the related ecosystems or representative smoke coverage.
- If the affected surface is unclear, CI must fail closed by expanding to a
  broader validation scope.

## Confirmed Build and Validation Semantics

- CI must not run a separate simplified build path that can drift from release.
- CI validation should reuse the same build recipes, artifact contracts, and
  ecosystem adapters used by `buddy` and `official` as much as practical.
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

## Confirmed Runner and Ecosystem Expectations

- .NET validation in GitHub Actions should run on Windows runners.
- Python and JavaScript/TypeScript validation may run on Ubuntu runners.
- The design may introduce multiple jobs or matrices, but the runner split must
  preserve the repository's ecosystem expectations.
- Ecosystem tools managed by `mise` should remain the preferred way to provision
  toolchains.

## Confirmed Outputs

- CI affected validation should emit a machine-readable affected validation plan
  or equivalent normalized artifact.
- The plan should make the selected validation scope inspectable, including:
    - directly changed projects;
    - ecosystem-wide selections;
    - expanded downstream project selections;
    - fallback expansions;
    - validation jobs or build groups to run.
- CI should emit validation evidence or receipts that can be checked by later
  jobs and inspected after failures.
- These CI outputs are validation artifacts only. They must not be treated as
  immutable publish proofs unless a later design explicitly defines that
  relationship.

## Confirmed Safety and Fallback Rules

- Detection failure must not lead to skipped validation.
- Unknown path classification must expand scope.
- Invalid project descriptors must fail validation rather than silently removing
  those projects from the CI scope.
- If a project-level change cannot be mapped to a known project safely, CI must
  expand to the relevant ecosystem or the whole repository.
- If an ecosystem-level rule is missing for a changed shared file, CI must expand
  to a broader scope until the rule is added.

## Non-Goals for This Requirements Baseline

- This page does not define the final affected-plan schema.
- This page does not define exact path-matching rules.
- This page does not define exact GitHub Actions workflow files.
- This page does not decide whether validation receipts can later be reused as
  release immutable proofs.
- This page does not require project-graph affected builds to be perfectly
  minimal in the first implementation.
- This page does not require replacing existing `hk` local validation behavior.

## Design Questions Deferred

- How should non-releasable but active validation projects be represented in the
  shared catalog?
- What is the exact affected-plan JSON shape?
- Which paths are project-scoped, ecosystem-scoped, release-infrastructure
  scoped, or global?
- How should downstream dependency closure be computed for .NET, Python, and
  JavaScript/TypeScript?
- Which workflow-release tooling changes require smoke-only validation, and which
  require full ecosystem validation?
- Which CI validation artifacts can safely become release proof inputs in a later
  design, if any?
