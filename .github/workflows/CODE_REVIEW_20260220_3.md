# Code Review Adjudication — 2026-02-20 (Pass 3)

## Scope

This document records an independent adjudicated outcome for workflow-refactor changes in:

- Added: `.github/workflows/release-orchestrate.yml`
- Modified: `.github/workflows/official.yml`
- Modified: `.github/workflows/buddy.yml`

Diff basis: `origin/main...HEAD`.

Adjudication method used in this pass:

1. Multiple independent strict code review passes.
2. Multiple independent adjudication passes (`true-positive` / `false-positive` / `partially-true`).
3. Majority-based consolidation.

Constraint followed during this pass:

- The following files were explicitly excluded from review input:
    - `.github/workflows/CODE_REVIEW_20260220.md`
    - `.github/workflows/CODE_REVIEW_20260220_REDO.md`

---

## Consolidated Findings (False Positives Excluded)

| ID  | Claim                                                                                                           | Adjudication (majority) | Notes                                                                                                                      |
| --- | --------------------------------------------------------------------------------------------------------------- | ----------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| P1  | Orchestrator policy globally enforces Node/Ruby publish targets before `resolve`, not scoped by `project_kind`. | **True Positive**       | Global checks reduce policy flexibility and can block future kind-specific release patterns. **Status: Resolved (2026-02-21)** — validation has been moved after `resolve` and scoped by `project_kind`. |
| P2  | `prepare-release-notes` now depends on guard jobs, reducing failure-path observability.                         | **True Positive**       | Guard failures can suppress notes artifacts that were previously available for diagnostics. **Status: Resolved (2026-02-21)** — release-notes generation now depends only on `resolve`, preserving notes artifacts for guard-failure diagnostics while keeping publish/release gating strict. |
| P3  | Channel prerelease invariant is not fully encoded in orchestrator policy (`buddy=true`, `official=false`).      | **Partially True**      | Callers currently pass expected values, but reusable policy does not fully hard-enforce channel-to-prerelease mapping. **Status: Resolved (2026-02-22)** — reusable orchestrator policy now hard-enforces `buddy => github_release_prerelease=true` and `official => github_release_prerelease=false`. |
| P4  | Non-trivial internal duplication remains in orchestrator branch jobs.                                           | **True Positive**       | Top-level duplication improved, but internal paired branches still carry drift risk over time. **Status: Resolved (2026-02-22)** — duplicated Node GPR branch pairs were consolidated into a single enabled publish job and a single gate job. |
| P5  | A Node GPR path is coupled to `environment: npmjs`, creating cross-target inconsistency.                        | **True Positive**       | One GPR branch uses npmjs environment gating while another does not, producing policy/behavior inconsistency. **Status: Resolved (2026-02-22)** — Node GPR publish jobs no longer bind to `environment: npmjs`; npmjs environment approval now applies only to npmjs publish target. |
| P6  | Permissions/toggle alignment has future mismatch risk (especially for buddy path evolution).                    | **Partially True**      | Current defaults are mostly aligned, but future toggle drift can create scope mismatch without stricter policy assertions. **Status: Resolved (2026-02-22)** — channel profile assertions now fail fast on official/buddy toggle drift (publish targets, attestation, pack mode, prerelease/non-clobber policy flags). |

---

## Merge Risk and Recommendation

### Current blockers

- None. (Previously blocked by **P5**, resolved on 2026-02-22.)

### High-priority follow-up

- None at this time for P3/P4/P5/P6; continue normal strict review for future workflow edits.

### Recommendation

**Current recommendation: `Proceed` (blockers cleared; major follow-ups addressed).**

Proceed and keep strict review/adjudication in CI for future workflow changes.

---

## Minimal Practical Fix Set

1. ✅ Completed (2026-02-21): Scope Node/Ruby target validation by resolved `project_kind` (or equivalent source-specific policy gates).
2. ✅ Completed (2026-02-21): Decouple release-notes artifact generation from guard-fail paths (publish/release gating remains strict).
3. ✅ Completed (2026-02-22): Remove Node GPR coupling to `environment: npmjs` (environment strategy is now explicit and consistent across Node publish branches).
4. ✅ Completed (2026-02-22): Enforce channel-prerelease invariants in orchestrator policy (`buddy => true`, `official => false`).
5. ✅ Completed (2026-02-22): Add policy-level assertions that prevent permission/toggle drift in caller workflows.
6. ✅ Completed (2026-02-22): Reduce internal Node orchestrator duplication by consolidating duplicated GPR branch jobs.

---

Reviewed on: 2026-02-20
Updated on: 2026-02-22
Repository: `hcoona/three`
Branch: `main`
