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
- The requirements-phase baseline now records descriptor gating, unified binary
  expectations, and the current OIDC-only publication posture.
- A dedicated review now distinguishes requirement-phase scope from design-phase
  concerns and lists the remaining requirement gaps.
- The baseline now also records role-based approval rules and first-delivery
  manual initiation.
- The lifecycle baseline now includes whole-release rerun and dry-run support,
  while leaving single-target retry out of the first delivery scope.
- The failure baseline now allows partial success to remain visible and be
  repaired manually in the first delivery scope.
- The versioning baseline now treats commit identity as the primary version
  source, treats `official` as the freezing state, and requires multiple target
  classes in the first delivery scope.
- The target baseline now recognizes ecosystem-specific target families and
  project-kind-specific packaging differences even when the final target type is
  the same.
- The target baseline now also requires GitHub Release for every in-scope
  project, keeps package targets project-declared, and fixes `buddy` as
  pre-release plus `official` as release for GitHub Release.
- The artifact baseline now allows one canonical build to emit both the binary
  and related packages or installers for the same binary variant, as long as it
  does not recompile divergent binaries per target.
- The acceptance baseline now requires real-project, real-publication proof
  across the representative library, app, Python, Node, and Ruby scenarios.
- GitHub-native workflow and approval history is currently considered
  sufficient audit evidence; no extra repo-owned release-record artifact is
  required.
- The structure is ready for follow-up work on the remaining lifecycle rules and
  then workflow design.

## Open Questions

- Which file should become the source of truth for per-project release targets?
- Which private projects deserve recurring buddy or official releases?
- Which metadata-private projects under `src/public/` should stay private?
- Which design should express the already-frozen descriptor business fields,
  target declarations, and canonical-build rules?

## Related Pages

- [Wiki Index](./index.md)
- [Wiki Log](./log.md)
- [Repository Release Landscape](./analyses/repository-release-landscape.md)
- [Workflow Release Requirements Baseline](./analyses/workflow-release-requirements-baseline.md)
- [Workflow Release Requirements-Phase Review](./analyses/workflow-release-requirements-phase-review.md)
