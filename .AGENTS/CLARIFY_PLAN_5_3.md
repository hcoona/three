<!-- markdownlint-disable MD013 MD024 MD029 -->

# CLARIFY_PLAN_5_3: Follow-up questions from independent review of PLAN_5

Date: 2026-01-06

This file lists **only** items that appear to require additional maintainer confirmation (policy-level), discovered during an independent strict review of `.AGENTS/PLAN_5.md`.

## 1) GitHub Action pinning policy for new RubyGems support

PLAN_5 frequently states actions should be “pinned”, but the repository’s current workflows are mixed (some SHA-pinned, some tag-pinned).

Please confirm the intended policy for this plan’s implementation:

- Option A (recommended): **Pin all third-party GitHub Actions by commit SHA**.
- Option B: Pin by **version tags** (e.g. `@v1`, `@v1.0.0`) and accept upstream tag movement risk.
- Option C: Keep the current mixed approach (document exceptions).

This decision affects at least:

- `rubygems/configure-rubygems-credentials`
- `ruby/setup-ruby`
- any future actions introduced for RubyGems support

Status: RESOLVED (2026-01-06)

Decision: Option A (Pin all third-party GitHub Actions by commit SHA.)

## 2) `environment: rubygems` protection settings (automation vs manual approval)

RubyGems Trusted Publishing configuration can constrain publishing by GitHub Actions environment.

PLAN_5 requires `environment: rubygems` in the RubyGems.org publish job.

Please confirm the intended operational posture for the GitHub Environment named `rubygems`:

- Option A (recommended): **No required reviewers / no manual approvals** (fully automated official releases).
- Option B: Require manual approval for the environment (official releases will pause awaiting approval).

If Option B is intended, please confirm whether this is acceptable for CI/release automation and whether the acceptance criteria should be updated accordingly.

Status: RESOLVED (2026-01-06)

Decision: Option A (No required reviewers / no manual approvals.)
