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
- The versioning baseline now treats commit identity as the primary version
  source, treats `official` as the freezing state, and requires multiple target
  classes in the first delivery scope.
- The target baseline now recognizes ecosystem-specific target families and
  project-kind-specific packaging differences even when the final target type is
  the same.
- The target baseline now also requires GitHub Release for any non-zero-target
  profile, keeps package targets project-declared, forbids same-name
  cross-profile publication to the same package registry, and fixes `buddy` as
  pre-release plus `official` as release for GitHub Release.
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
  boundaries, graph ownership rules, artifact identity rules, and publish-side
  structure.

## Open Questions

- Which descriptor format and schema should carry the already-frozen business
  fields and instantiate the settled architecture model?
- What exact plan object shape should realize the agreed envelope/graph model
  without leaking schema details back into the architecture layer?

## Related Pages

- [Wiki Index](./index.md)
- [Wiki Log](./log.md)
- [Repository Release Landscape](./analyses/repository-release-landscape.md)
- [Workflow Release Requirements Baseline](./analyses/workflow-release-requirements-baseline.md)
- [Workflow Release Requirements-Phase Review](./analyses/workflow-release-requirements-phase-review.md)
- [Workflow Release Design Direction](./analyses/workflow-release-design-direction.md)
- [Workflow Release Architecture Model](./analyses/workflow-release-architecture-model.md)
