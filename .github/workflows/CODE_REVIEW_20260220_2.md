# Code Review Adjudication (Archived) — 2026-02-20

## Scope

This archive records adjudicated conclusions for the workflow refactor involving:

- Added: `.github/workflows/release-orchestrate.yml`
- Modified: `.github/workflows/official.yml`
- Modified: `.github/workflows/buddy.yml`

Adjudication basis:

1. Multiple independent strict code review passes.
2. Multiple independent adjudication passes (`true-positive` / `false-positive` / `partially-true`).
3. Cross-pass consolidation by majority outcome.

> Note: Per archival policy for this record, **false positives are intentionally omitted**.

---

## Adjudicated Findings (Non-FP Only)

| ID  | Review Claim                                                                                                                 | Adjudication       | Notes                                                                                                                                                     |
| --- | ---------------------------------------------------------------------------------------------------------------------------- | ------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| J1  | Official Node publish ordering semantics changed after split jobs; partial-publish risk increased.                           | **True Positive**  | Node publish path moved from a single combined job to split jobs, introducing a higher chance of one-side success and one-side failure across registries. |
| J2  | OIDC trusted-publishing identity boundary changed under reusable workflow; hardcoded workflow-filename guidance may mislead. | **Partially True** | The trusted-publishing execution boundary changed with `workflow_call`; hardcoded guidance can be misleading in this model.                               |
| J3  | Buddy permissions are broader than strictly necessary for some paths.                                                        | **Partially True** | Current permissions are generally functional, but least-privilege can be tightened further by path-sensitive reduction.                                   |
| J4  | Orchestrator policy is too global (Node/Ruby target requirements not sufficiently scoped by release kind/channel).           | **True Positive**  | Policy constraints are globally enforced and can reduce future extensibility for narrower channel/release kinds.                                          |
| J5  | Moving release-notes generation behind guard dependencies reduced failure-path observability.                                | **True Positive**  | Guard failures can prevent notes artifact generation, reducing diagnostics visibility in some failure cases.                                              |
| J6  | Internal duplication remains non-trivial in orchestrator branches.                                                           | **True Positive**  | Duplication was reduced at top-level, but substantial branch duplication still exists inside orchestrator jobs.                                           |
| J8  | Source-specific input validation should fail earlier at policy stage.                                                        | **Partially True** | Validation exists, but fail-fast strictness/specificity can be improved before resolve-stage execution.                                                   |
| J9  | Step-level disable flags still allow job-level write permissions in some publish jobs.                                       | **True Positive**  | Some jobs keep write scopes even when publish flags disable actual publish steps, violating strict least-privilege intent.                                |
| J10 | Attestation-disabled paths still request attestation/id-token permissions.                                                   | **True Positive**  | Attestation jobs can still request high scopes while doing disabled/no-op execution paths.                                                                |

---

## Blockers and Merge Recommendation

### Blockers

1. **J1** — Node publish sequencing regression with elevated partial-publish risk.
2. **J9** — Publish-disabled paths still grant write permissions at job scope.
3. **J10** — Attestation-disabled paths still request elevated attestation/OIDC permissions.

### Additional high-priority risk

- **J2** — Trusted-publishing identity boundary and guidance consistency should be corrected before merge.

### Recommendation

**Current recommendation: `Block` (do not merge yet).**

Proceed after addressing blockers and re-running strict review/adjudication.

---

## Minimal Fix Set (Practical)

1. Restore deterministic Node publish sequencing (or equivalent atomic gating) to avoid split-registry partial-publish outcomes.
2. Move publish-enable checks to job-level gating so disabled publish paths do not receive write permissions.
3. Gate attestation jobs at job-level on attestation enablement; avoid requesting `id-token`/`attestations` when disabled.
4. Update trusted-publishing guidance to match reusable-workflow execution identity (remove hardcoded `official.yml` assumption).

---

Reviewed on: 2026-02-20
Repository: `hcoona/three`
Branch: `main`
