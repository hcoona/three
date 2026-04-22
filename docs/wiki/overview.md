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
- The baseline now also records role-based approval rules and phase-1 manual
  initiation.
- The lifecycle baseline now includes whole-release rerun and dry-run support,
  while leaving single-target retry out of phase 1.
- The failure baseline now allows partial success to remain visible and be
  repaired manually in phase 1.
- The structure is ready for follow-up work on release metadata and workflows.

## Open Questions

- Which file should become the source of truth for per-project release targets?
- Which private projects deserve recurring buddy or official releases?
- Which metadata-private projects under `src/public/` should stay private?
- What approval and acceptance rules should gate the first workflow-release
  milestone?
- Which trigger, rollback, and audit requirements should be frozen before design
  starts?
- Which partial-failure, cancellation, and supersession lifecycle rules belong
  in phase 1?
- Do `buddy` and `official` need different visible failure-state rules?

## Related Pages

- [Wiki Index](./index.md)
- [Wiki Log](./log.md)
- [Repository Release Landscape](./analyses/repository-release-landscape.md)
- [Workflow Release Requirements Baseline](./analyses/workflow-release-requirements-baseline.md)
- [Workflow Release Requirements-Phase Review](./analyses/workflow-release-requirements-phase-review.md)
