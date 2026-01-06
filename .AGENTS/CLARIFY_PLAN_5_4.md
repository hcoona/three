<!-- markdownlint-disable MD013 MD029 -->

# CLARIFY_PLAN_5_4: Follow-up maintainer confirmations for PLAN_5 (if any)

After an independent strict review of `.AGENTS/PLAN_5.md`, I did **not** identify any _new_ policy-level questions that require maintainer confirmation.

All policy-sensitive items appear already resolved by existing clarifications:

- Trusted Publishing (OIDC) only for RubyGems.org; no long-lived API tokens; no fallback. (`CLARIFY_PLAN_0`, `CLARIFY_PLAN_3`)
- `environment: rubygems` and it must be fully automated (no required reviewers). (`CLARIFY_PLAN_4`, `CLARIFY_PLAN_5_3`)
- Ruby version grammar and buddy prerelease-only gating semantics. (`CLARIFY_PLAN_1`, `CLARIFY_PLAN_5_2`)
- GitHub Packages RubyGems auth using `github.token` only, no PAT fallback, plus linkage/access prerequisites. (`CLARIFY_PLAN_0`, `CLARIFY_PLAN_1`)

## Implementation-only note (no maintainer decision needed)

The review previously suggested adding a separate “mechanical enforcement” guard to prevent future introduction of RubyGems API-token fallback (e.g., failing the job if `api-token` / `role-to-assume` / `secrets.*RUBYGEMS*` are referenced).

Status: RESOLVED (2026-01-06)

Decision: no additional enforcement mechanism is required.

- Implementation will satisfy the “no fallback” requirement by **not** wiring any RubyGems API token secrets and by **not** setting `api-token` or `role-to-assume`.
- If a future change introduces such a fallback, that is treated as a normal review/CI policy violation rather than something the workflow must self-detect at runtime.
