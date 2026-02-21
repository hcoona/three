# Code Review Adjudication — 2026-02-20

## Scope

This document records the **current review comments** for the workflow refactor and the adjudicated result for each comment:

- Added: `.github/workflows/release-orchestrate.yml`
- Modified: `.github/workflows/official.yml`
- Modified: `.github/workflows/buddy.yml`

The adjudication is based on:

1. Two independent strict reviews.
2. A separate independent pass that classified each comment as:
    - `True Positive`
    - `False Positive`
    - `Partially True`

---

## Review Comments and Adjudication

| ID  | Review Comment                                                                                                                         | Adjudication       | Notes                                                                                                                                                                         |
| --- | -------------------------------------------------------------------------------------------------------------------------------------- | ------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| C1  | OIDC trusted publishing identity boundary changed after moving publish jobs into reusable workflow; this may break trusted publishing. | **Partially True** | The execution boundary changed (objective). Whether this breaks trusted publishing depends on external publisher configuration and claim matching details.                    |
| C2  | Official Node publish gating semantics changed: GPR may publish before npmjs environment approval.                                     | **True Positive**  | In the old flow, GPR and npmjs publish were in one `publish-node` job behind `environment: npmjs`. In the new flow, `publish-node-gpr` is separate from `publish-node-npmjs`. |
| C3  | Buddy permissions were broadened unnecessarily (`id-token`, `attestations`) while attestation is disabled.                             | **True Positive**  | `buddy.yml` grants broader permissions than required by its policy (`enable_attestation: false`). This is a least-privilege regression.                                       |
| C4  | Orchestrator policy is too global (requires at least one Node target and one Ruby target regardless of release kind/channel).          | **Partially True** | The check is globally enforced in `policy`. It is not currently breaking official/buddy, but reduces future extensibility.                                                    |
| C5  | Moving `prepare-release-notes` behind guards is a regression in observability/debuggability.                                           | **Partially True** | This can reduce diagnostics artifacts when guards fail. Impact is mostly operational rather than correctness.                                                                 |
| C6  | Internal duplication remains significant (with/without publish-target release jobs), so drift risk is still high.                      | **Partially True** | Duplication still exists, but this is primarily a maintainability concern rather than an immediate correctness bug.                                                           |
| C7  | Disabled/no-op jobs are harmful enough to be considered real issues.                                                                   | **False Positive** | Some no-op paths add noise, but they are not inherently merge blockers by themselves.                                                                                         |
| C8  | Hardcoded npm trusted publisher note (`Workflow filename: official.yml`) is incorrect/misleading after refactor.                       | **Partially True** | The hardcoded text can be misleading in a reusable workflow context.                                                                                                          |
| C9  | Source-specific input validation in orchestrator is insufficient (should fail earlier before `resolve`).                               | **Partially True** | Validation exists but is not as early/strict as possible at policy level. This is mainly robustness/usability.                                                                |
| C10 | Overall verdict should be FAIL for merge-readiness.                                                                                    | **True Positive**  | Blockers exist (notably C2 and C3), plus unresolved trusted publishing risk (C1/C8).                                                                                          |

---

## Blockers and Merge Recommendation

## Blockers

1. **C2** — Node publish gating semantics changed from previous behavior.
2. **C3** — Least-privilege regression in buddy permissions.

## Additional high-priority risk

- **C1/C8** — Trusted publishing identity and messaging consistency should be validated/fixed before merge.

## Recommendation

**Current recommendation: `Block` (do not merge yet).**

Proceed after addressing the blockers above and re-running strict review.

---

## Classification Summary

- True Positive: **3** (`C2`, `C3`, `C10`)
- Partially True: **6** (`C1`, `C4`, `C5`, `C6`, `C8`, `C9`)
- False Positive: **1** (`C7`)

---

Reviewed on: 2026-02-20
Repository: `hcoona/three`
Branch: `main`
