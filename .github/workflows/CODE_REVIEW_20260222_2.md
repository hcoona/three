# Code Review Report (2026-02-22)

## Context

- Repository: `hcoona/three`
- Branch under review: `dev/shuaizhang/refactor-buddy-official-workflows`
- Diff range: `origin/main...HEAD`
- Review scope (YAML only):
  - `.github/workflows/buddy.yml`
  - `.github/workflows/official.yml`
  - `.github/workflows/release-orchestrate.yml`
  - `.review/buddy_before.yml`
  - `.review/official_before.yml`

## Review Method

Multiple independent strict reviews were run, then each finding was re-validated by separate independent passes to classify:

- **True Positive (TP)**
- **False Positive (FP)**
- **Unclear**

Only findings that remained after FP filtering are included below.

## Final Findings (False Positives Removed)

### 1) High — `publish-node-npmjs` can proceed even if GPR publish fails

- **Status:** True Positive (high confidence)
- **File:** `.github/workflows/release-orchestrate.yml`
- **Key area:** `jobs.publish-node-npmjs.if`
- **Why it matters:** The npmjs publish job uses `always()` and does not require `needs.publish-node-gpr.result == 'success'`. This can allow npm publish to continue after GPR failure, creating partial/inconsistent release outcomes.
- **Suggested fix (minimal):** Add an explicit success dependency guard for `publish-node-gpr` when GPR publish is enabled.

### 2) High — npmjs environment binding moved away from the actual npm publish job

- **Status:** True Positive (high confidence)
- **File:** `.github/workflows/release-orchestrate.yml`
- **Key area:** `jobs.gate-node-publish-npmjs.environment` vs `jobs.publish-node-npmjs`
- **Why it matters:** `environment: npmjs` is set on the gate job, while the real npm publish job has `id-token: write` but no matching environment. This can break or weaken expected OIDC/Trusted Publisher behavior depending on environment-bound policy.
- **Suggested fix (minimal):** Set `environment: npmjs` on the actual `publish-node-npmjs` job (the job that requests OIDC and performs npm publish).

### 3) Medium — Channel policy is maintained in two places (caller + orchestrator assertions)

- **Status:** True Positive (majority agreement, medium confidence)
- **Files:**
  - `.github/workflows/buddy.yml`
  - `.github/workflows/official.yml`
  - `.github/workflows/release-orchestrate.yml`
- **Why it matters:** Channel policy booleans are both passed by callers and asserted again in the orchestrator. This improves safety but still creates dual maintenance points and potential drift overhead.
- **Suggested fix (optional):** Move to a single source of truth derived from `channel` within orchestrator, and reduce repeated policy inputs in callers.

### 4) Low — Large `before` snapshots in `.review/` add maintenance noise

- **Status:** True Positive (majority agreement, low confidence)
- **Files:**
  - `.review/buddy_before.yml`
  - `.review/official_before.yml`
- **Why it matters:** These snapshots are not runtime workflows, but they can become stale and add search/review noise over time.
- **Suggested fix (optional):** Keep snapshots outside the main workflow path or replace with concise diff-oriented artifacts.

## Excluded / Not Kept as Actionable

### A) `channel_allowlist` bypass concern

- **Final classification:** Not kept as actionable (FP/Unclear consensus)
- **Reason:** Current `buddy`/`official` callers do not use the allowlist path, and default behavior remains fail-closed for known channels.

## Recommended Priority

1. **Must fix before merge:**
   - GPR-failure-to-npm continuation risk
   - npmjs environment binding on actual publish job
2. **Should improve (maintainability):**
   - Reduce channel policy dual maintenance
3. **Nice to have:**
   - Clean up or relocate `.review/*_before.yml` snapshots

## Summary

The orchestration refactor successfully reduces top-level duplication overall. However, two high-risk behavioral regressions in Node publish flow should be addressed before merge to avoid inconsistent release states and potential OIDC/publishing failures.
