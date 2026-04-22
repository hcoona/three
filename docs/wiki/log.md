# Wiki Log

This file is the append-only chronological record of wiki activity.

## [2026-04-21] bootstrap | Initialize docs wiki scaffold

- Created the initial `docs/` LLM Wiki structure.
- Added the agent contract, wiki index, overview page, and category guides.
- Reserved `docs/sources/` for immutable sources and `docs/raw/` for immutable
  supporting assets.

## [2026-04-21] query | Audit repository release landscape

- Surveyed the monorepo's C#, Python, and JS/TS projects for release intent and
  publishability signals.
- Added source digests for the root workspace manifests, release-policy
  scripts, representative package manifests, and current C# app publish flows.
- Wrote a repository-wide release analysis covering buddy and official profile
  gaps, OIDC direction, and C# app migration targets.

## [2026-04-21] query | Capture workflow release requirements baseline

- Recorded requirement-phase clarifications from the workflow release
  discussion.
- Tightened the release analysis to require OIDC for all currently known
  supported targets.
- Added a dedicated requirements baseline analysis documenting descriptor
  gating, explicit profiles, unified binaries, and target-specific packaging.

## [2026-04-21] query | Review requirements-phase boundary

- Compared the workflow-release initiative against a standard
  requirements-analysis checklist.
- Identified which current discussion items belong to design rather than
  requirements.
- Added a gap review listing the missing requirement items that should be
  settled before requirements sign-off.

## [2026-04-21] query | Freeze approval and initiation rules

- Recorded the role-based approval model for `buddy` and `official`.
- Captured the self-approval exception for `admin` on `official`.
- Updated the requirements baseline to prioritize manual `workflow_dispatch`
  initiation for the first delivery scope.

## [2026-04-21] query | Freeze rerun and dry-run rules

- Recorded whole-release rerun as a first-delivery-scope business requirement.
- Explicitly left single-target retry out of the first delivery scope and tied
  replay handling to skip detection plus idempotent behavior.
- Added dry-run or validation-only execution as a first-delivery-scope
  requirement.

## [2026-04-21] query | Freeze partial-success handling

- Allowed first-delivery-scope releases to preserve partial success instead of
  forcing transactional rollback.
- Recorded manual remediation as acceptable for the first delivery scope.
- Narrowed the remaining failure-policy gaps to cancellation, supersession, and
  any exceptional rollback cases.

## [2026-04-21] query | Freeze version identity and target-scope principles

- Recorded commit-centric version identity and the rule that `official` is the
  freezing state for a version.
- Allowed explicit `buddy FORCE` before `official`, while keeping target-platform
  constraints authoritative.
- Recorded that the first delivery scope must cover multiple target classes from
  the start.

## [2026-04-21] query | Freeze target-family and transform rules

- Distinguished ecosystem-specific target families instead of using the generic
  term package registry.
- Recorded that the same final target family may still require different
  packaging paths for different project kinds.
- Added target-specific transformation examples such as GitHub Packages scope
  changes versus npmjs canonical package names.

## [2026-04-22] query | Freeze target semantics and acceptance baseline

- Recorded GitHub Release as mandatory for any non-zero-target profile, while
  leaving zero-target profiles legal, with fixed `buddy` = pre-release and
  `official` = release semantics when GitHub Release is declared.
- Clarified that package targets remain project-declared even when GitHub
  Packages supports the ecosystem, and captured the Python exception because
  GitHub Packages does not expose a PyPI registry.
- Clarified that `buddy` and `official` may not share the same package registry
  under the same published package name.
- Tightened canonical-build semantics so one build may emit binary and
  package/installer outputs for the same variant without target-by-target
  recompilation.
- Added real-project acceptance requirements, including real `official`,
  promotion, and direct-`official` proof.
- Added lifecycle rules for manual cancellation, same-project same-profile
  supersession, and shared visible handling across `buddy` and `official`.
- Excluded extra manual-remediation closure mechanics from workflow scope and
  kept release initiation manual while assigning Git tag creation to the
  workflow itself.
- Froze replay handling as automatic-only and confirmed there are no exceptional
  first-delivery cases that require automatic rollback.
- Froze the descriptor business-field set and confirmed that the first delivery
  scope will not add target families beyond GitHub Release, NuGet, PyPI, npm,
  and RubyGems.
