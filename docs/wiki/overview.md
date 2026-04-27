# Wiki Overview

This page holds the current top-level synthesis of the wiki.

## Scope

The wiki compiles knowledge from curated source documents in `../sources/` and
supporting assets in `../raw/`.

## Current State

- The wiki scaffold is in place.
- The first release-focused source digests and analysis pages now exist.
- The current repository has release policy fragments and package metadata, but
  not yet a complete workflow layer.
- The requirements phase is signed off, and the wiki has now entered the design
  phase for workflow-based release.
- The requirements-phase baseline now records descriptor gating, unified binary
  expectations, and the current secretless publication posture.
- A dedicated review now distinguishes requirement-phase scope from design-phase
  concerns and confirms that the remaining work is primarily design-oriented.
- The baseline now also records role-based approval rules and first-delivery
  manual initiation.
- The lifecycle baseline now includes whole-release rerun and dry-run support,
  while leaving single-target retry out of the first delivery scope.
- The request-scope baseline now treats one `workflow_dispatch` run as targeting
  one or more selected projects within a single profile-specific workflow
  entry point.
- The failure baseline now allows partial success to remain visible and be
  repaired manually in the first delivery scope.
- The lifecycle baseline now treats duplicate-run cancellation as an optional
  native GitHub Actions concurrency behavior rather than as a repo-defined
  supersession rule.
- When that optional native cancellation is used, duplicate is defined by the
  same workflow entry point and the same commit, regardless of project subset
  or other inputs.
- The versioning baseline now treats project-scoped NBGV identity as the
  primary version source, treats successful official GitHub Release publication
  for that project-scoped tag as the freezing event, and requires multiple
  target classes in the first delivery scope.
- The target baseline now recognizes ecosystem-specific target families and
  project-kind-specific packaging differences even when the final target type is
  the same.
- The target baseline now also requires GitHub Release for any non-zero-target
  profile, keeps package targets project-declared, forbids same-name
  cross-profile publication to the same package registry, fixes `buddy` as
  pre-release plus `official` as release for GitHub Release, and limits tag
  creation or verification to runs that actually include a GitHub Release
  publish node.
- The artifact baseline now allows one canonical build to emit both the binary
  and related packages or installers for the same binary variant, as long as it
  does not recompile divergent binaries per target.
- The acceptance baseline now requires real-project, real-publication proof
  across the representative library, app, Python, Node, and Ruby scenarios.
- The acceptance baseline now also requires explicit proof for multi-project
  dispatch, dry-run, rerun including immutable-target partial-success replay,
  cancellation, approval boundaries, and GitHub Packages publication when that
  target is in scope.
- GitHub-native workflow and approval history is currently considered
  sufficient audit evidence; no extra repo-owned release-record artifact is
  required.
- A first design-direction page now frames the top-level architecture choice
  before descriptor syntax or workflow internals are designed.
- A dedicated architecture-model page now records the settled planner-centric
  boundaries, graph ownership rules, artifact identity rules, the split between
  control-plane run envelope and plan envelope, and the shared target-instance
  catalog model on the publish side, where projects still opt in explicitly,
  GitHub Packages is represented through host-specific target instances rather
  than as a target family, and execution consumes plan snapshots rather than
  re-reading the catalog.
- The descriptor-schema page now normatively defines the release authoring
  files: project-owned `src/**/three.release.yml` descriptors, the shared
  `eng/release/target-instances.yml` catalog, field-scoped relative-path bases,
  repo-wide rejection of checked-in descriptors outside `src/`, deterministic
  in-scope discovery, project-local `variants[].id` handle uniqueness plus
  descriptor-local rejection of duplicate semantic variant `dimensions` sets,
  variant-local rejection of duplicate semantic artifact identity tuples
  regardless of differing `artifact.id` handles, author-time resolution of
  `source` file paths to real checked-in files under each release root, a
  closed current-scope mapping from `project.ecosystem` to allowed
  `source.primary-manifest` types, catalog references, the current-scope
  catalog contract vocabulary, family-specific destination shapes including
  host-specific GitHub Packages instances inside the NuGet, npm, and RubyGems
  families while recognizing that GitHub Packages has no PyPI registry, closed
  current-scope
  capability assignments by family and host, closed current-scope projection
  shapes, contract-to-artifact compatibility rules, and the three-layer
  validation model: file-schema validation, author-time static repo validation,
  and planner-time validation.
- The plan-shape page now normatively defines `three.release.plan/v1alpha1`: an
  envelope keyed by the resolved request and selected project snapshots,
  including frozen project-selection normalization and error semantics plus
  normalized request flags inside the authoritative `plan-id` identity, a
  normalized graph keyed by stable deterministic planner ids plus shared
  target-instance-snapshot ids, deterministic mapping of every Group 1 construct
  into that plan, planner-authored per-publish-node resolved artifact set and
  publish identity including project-scoped NBGV-derived release tags and GitHub
  Release desired release state, planner-owned replay-satisfaction disposition
  including same-tag GitHub Release satisfied skips and same-tag prerelease-to-
  release authoritative replacement semantics, the official-frozen predicate for
  buddy `FORCE`, live publish mode, a closed current-scope replay and `buddy
FORCE` outcome matrix, normalized projection references onto plan artifact
  ids, frozen catalog data inside target-instance snapshots, and an explicit
  boundary for what remains outside the plan.

- The workflow-and-executor-boundaries page now fixes the control-plane shape on
  top of that plan: `buddy` and `official` entry workflows over one shared
  orchestration contract, a normalized planner-facing request contract for
  current scope including frozen project-selection semantics, per-variant build
  fan-out, topology-partitioned per-publish-node publish fan-out that returns
  entry/caller-bound OIDC selectors to the top-level entry workflow identity,
  control-plane-owned approvals, concurrency, distinct project-scoped tag
  orchestration only when a GitHub Release publish node is present, runtime
  wiring, and reporting, plus plan-to-job handoff contracts, a distinct control-
  plane-authored synthetic skip receipt contract, a minimal structured planner-
  diagnostic contract, and thin executor boundaries that keep replay decisions,
  overwrite or same-tag replacement mode, publication identity, and GitHub
  Release desired state planner-owned.
- A dedicated design-layering and handoff-scope page now records the current
  three-layer reading of the design corpus and now records that upper-layer and
  middle-layer design are both closed in current scope, while lower-layer
  realization remains intentionally implementation-owned.
- The current middle-layer design now also freezes current-scope handoff seams:
  manual dispatch selects a branch/tag ref and then pins all later stages to the
  resolved SHA, planner-time remote observation uses public reads plus
  least-privilege `GITHUB_TOKEN` reads for GitHub-hosted surfaces, `official`
  runs require an early control-plane `maintain+` authorization check distinct
  from later approval, and immutable proof reuse is guaranteed only within the
  default GitHub Actions artifact retention window.
- The lower-layer design handoff now freezes the implementation seams that affect
  registry configuration and replay safety: stable workflow filenames for trusted
  publishing, entry inputs including dry-run plus explicit validation-build,
  registered planner diagnostic codes, JSON handoff files, artifact and
  immutable-proof naming, registry-adapter obligations, GitHub permission
  boundaries, tag orchestration, and acceptance traceability.

## Open Questions

- No major upper-layer or middle-layer design gap remains in the current scope.
- No known lower-layer handoff guardrail remains unresolved for current scope.
- Internal planner modules, helper script decomposition, exact retry timings, and
  command wrappers remain intentionally implementation-owned within the frozen
  lower-layer contracts.

## Related Pages

- [Wiki Index](./index.md)
- [Wiki Log](./log.md)
- [Repository Release Landscape](./analyses/repository-release-landscape.md)
- [Workflow Release Requirements Baseline](./analyses/workflow-release-requirements-baseline.md)
- [Workflow Release Requirements-Phase Review](./analyses/workflow-release-requirements-phase-review.md)
- [Workflow Release Design Direction](./analyses/workflow-release-design-direction.md)
- [Workflow Release Architecture Model](./analyses/workflow-release-architecture-model.md)
- [Workflow Release Design Layering and Implementation Handoff Scope](./analyses/workflow-release-design-layering-and-handoff-scope.md)
- [Workflow Release Descriptor Schema](./analyses/workflow-release-descriptor-schema.md)
- [Workflow Release Plan Shape](./analyses/workflow-release-plan-shape.md)
- [Workflow Release Workflow and Executor Boundaries](./analyses/workflow-release-workflow-executor-boundaries.md)
- [Workflow Release Low-Level Design](./analyses/workflow-release-low-level-design.md)
