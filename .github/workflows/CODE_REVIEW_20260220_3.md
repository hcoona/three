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
| P1  | Orchestrator policy globally enforces Node/Ruby publish targets before `resolve`, not scoped by `project_kind`. | **True Positive**       | Global checks reduce policy flexibility and can block future kind-specific release patterns.                               |
| P2  | `prepare-release-notes` now depends on guard jobs, reducing failure-path observability.                         | **True Positive**       | Guard failures can suppress notes artifacts that were previously available for diagnostics.                                |
| P3  | Channel prerelease invariant is not fully encoded in orchestrator policy (`buddy=true`, `official=false`).      | **Partially True**      | Callers currently pass expected values, but reusable policy does not fully hard-enforce channel-to-prerelease mapping.     |
| P4  | Non-trivial internal duplication remains in orchestrator branch jobs.                                           | **True Positive**       | Top-level duplication improved, but internal paired branches still carry drift risk over time.                             |
| P5  | A Node GPR path is coupled to `environment: npmjs`, creating cross-target inconsistency.                        | **True Positive**       | One GPR branch uses npmjs environment gating while another does not, producing policy/behavior inconsistency.              |
| P6  | Permissions/toggle alignment has future mismatch risk (especially for buddy path evolution).                    | **Partially True**      | Current defaults are mostly aligned, but future toggle drift can create scope mismatch without stricter policy assertions. |

---

## Merge Risk and Recommendation

### Current blockers

1. **P1** — Over-global target policy constraints in orchestrator.
2. **P2** — Reduced diagnostics visibility on guard-failure paths.
3. **P5** — Cross-target environment coupling inconsistency for Node publish paths.

### High-priority follow-up

- **P3** — Encode channel prerelease invariant as hard policy in reusable workflow.
- **P6** — Strengthen permission/toggle consistency guarantees to preserve least privilege during future changes.

### Recommendation

**Current recommendation: `Block` (do not merge yet).**

Proceed after addressing blockers and re-running strict review/adjudication.

---

## Minimal Practical Fix Set

1. Scope Node/Ruby target validation by resolved `project_kind` (or equivalent source-specific policy gates).
2. Decouple release-notes artifact generation from guard-fail paths (keep publish/release gating strict).
3. Remove Node GPR coupling to `environment: npmjs` (or make environment strategy explicit and consistent across branches).
4. Enforce channel-prerelease invariants in orchestrator policy (`buddy => true`, `official => false`).
5. Add policy-level assertions that prevent permission/toggle drift in caller workflows.

---

Reviewed on: 2026-02-20
Repository: `hcoona/three`
Branch: `main`
