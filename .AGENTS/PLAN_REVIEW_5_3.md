<!-- markdownlint-disable MD013 MD024 MD029 -->

# PLAN_REVIEW_5_3: Independent strict review of PLAN_5 (RubyGems Trusted Publishing only)

Date: 2026-01-06

This is an **independent** review of `.AGENTS/PLAN_5.md`.

Constraints honored:

- I used `.AGENTS/CLARIFY_PLAN_*.md` as the only “maintainer-confirmed policy” source.
- I did **not** consult any `.AGENTS/PLAN_REVIEW_*.md` documents.

## Executive verdict

`PLAN_5` is **conceptually sound** and (importantly) encodes the repo’s hard security requirement: **RubyGems.org publishing must use Trusted Publishing (OIDC) only, with no API key / no fallback**.

I would approve the plan **with a small set of required tightening edits**, mostly around:

1. **Action pinning strategy** (the plan says “pinned” but does not define a concrete policy nor provide actual pins).
2. **Operational guardrails** for the RubyGems `environment: rubygems` (to prevent accidental manual-approval deadlocks).
3. **Making “no fallback” mechanically testable** (ensure the workflow cannot silently succeed using an API token if one gets introduced later).

Update (maintainer decisions received):

- Action pinning policy is now **RESOLVED** as “pin third-party GitHub Actions by commit SHA”. See `.AGENTS/CLARIFY_PLAN_5_3.md`.
- The `rubygems` GitHub Environment posture is now **RESOLVED** as “no required reviewers / no manual approvals”. See `.AGENTS/CLARIFY_PLAN_5_3.md`.

Everything else reads implementable and aligns with the maintainer confirmations in `CLARIFY_PLAN_0..5`.

Update (plan text amended):

- PLAN_5 now explicitly documents the commit-SHA action pinning standard, the `rubygems` environment posture, and additional guardrails/compat notes identified below.

## What PLAN_5 gets right (high confidence)

### Security / auth model

- **RubyGems.org**: uses `rubygems/configure-rubygems-credentials` in “trusted publisher” mode (OIDC exchange) and explicitly forbids `api-token` / `role-to-assume` / any secret fallback.
- **GitHub Packages (RubyGems registry)**: uses `github.token` with `packages: write` and explicitly forbids PAT fallback.

This matches maintainer-confirmed policy:

- No long-lived RubyGems API keys. (`CLARIFY_PLAN_0`, `CLARIFY_PLAN_3`)
- No PAT fallback for GitHub Packages RubyGems. (`CLARIFY_PLAN_0`, `CLARIFY_PLAN_1`)

### Architecture / repeatability

- Preserves the repo’s **artifact-first** release model: build once, publish from `out/*` only.
- Makes idempotency a first-class contract across registries (and explicitly includes buddy publishes), aligned with `CLARIFY_PLAN_2` and `CLARIFY_PLAN_4`.
- Uses deterministic build inputs (`SOURCE_DATE_EPOCH`, `TZ`, `LC_ALL`), aligned with `CLARIFY_PLAN_2`.

### Ruby-specific correctness

- Ruby version policy is consistent with maintainer decisions:
    - Accept only `MAJOR.MINOR.PATCH` plus optional **dot** prerelease segments (e.g. `1.2.3.rc.0`).
    - Reject SemVer hyphen prereleases and build metadata.
    - Reject numeric-only extra dot segments.
    - `is_prerelease` logic aligns with `CLARIFY_PLAN_5_2`.
- Identity invariants (tag ↔ gemspec filename ↔ gem name in built `.gem`) match `CLARIFY_PLAN_0`.

### RubyGems.org idempotency approach is valid

- The plan relies on `GET /api/v1/versions/<gem>.json` and compares the version object’s `sha` to local SHA-256.
    - This is supported by RubyGems.org API documentation (`Gem Version Methods` shows a `sha` field on version objects).

## Required tightening / corrections (must address)

### 1) Define and apply a consistent GitHub Action pinning policy

PLAN_5 frequently says “pinned” (e.g. `ruby/setup-ruby@v1`, `rubygems/configure-rubygems-credentials`, `rubygems-await`), but it does not specify what “pinned” means in this repo:

- **Tag pin** (e.g. `@v1` / `@v1.0.0`) is convenient but still allows upstream retags in rare cases.
- **Commit SHA pin** is strongest, but more verbose and requires upkeep.

The current workflows in `.github/workflows/*` are mixed (some actions are SHA-pinned, some are major-version pinned), so PLAN_5 needs to explicitly state which standard to follow **for new RubyGems-related actions**.

Without that, the plan is underspecified and the “mechanically safer” claim is not fully justified.

Status: RESOLVED (2026-01-06)

Decision: Pin all third-party GitHub Actions by commit SHA. See `.AGENTS/CLARIFY_PLAN_5_3.md`.

### 2) Make the “Trusted Publishing only” requirement mechanically enforceable

PLAN_5 states “no fallback” (good), but the plan should also require a simple guard that prevents accidental future drift, e.g.:

- Ensure no RubyGems API token secret is referenced anywhere in the Ruby publish job.
- Ensure `rubygems/configure-rubygems-credentials` is invoked with `trusted-publisher: true` and with **neither** `api-token` nor `role-to-assume` set.

This can be done as a code review checklist item, but given the strictness requirement it’s better if PLAN_5 explicitly calls out that the workflow must **fail** if someone tries to wire in an API token.

(Reason: once the repository adds a secret, “helpful” contributors might be tempted to “fix CI” by adding a fallback. The plan is meant to forbid that.)

Status: ADDRESSED IN PLAN_5 (2026-01-06)

Notes:

- PLAN_5 now includes a dedicated hard-requirement subsection requiring a fail-fast guard that forbids RubyGems API token secret references and forbids `api-token` / `role-to-assume` inputs.

### 3) Clarify GitHub Environment protections for `environment: rubygems`

Trusted publisher configuration includes the GitHub Actions **environment** constraint. PLAN_5 correctly says the environment must be `rubygems`.

However, the plan should explicitly note an operational constraint:

- If the `rubygems` environment is configured with required reviewers or wait timers, **official releases will block**.

This is not a code concern but it _is_ a “release reliability” concern, and the plan’s acceptance criteria implicitly expect fully automated publishing.

Status: RESOLVED (2026-01-06)

Decision: The `rubygems` environment must have no required reviewers / no manual approvals. See `.AGENTS/CLARIFY_PLAN_5_3.md`.

## Strong recommendations (should address)

### A) `rubygems-await` pin + installation method should be made explicit in the plan

Maintainer policy already confirmed using `rubygems-await` to handle RubyGems eventual consistency (`CLARIFY_PLAN_3`). PLAN_5 mentions it but does not specify:

- Whether it is installed via Bundler (preferred when a Gemfile is present) vs `gem install` in publish jobs that do not use Bundler.
- How it is version-pinned.

Suggestion for PLAN_5: explicitly require one of:

- `bundle exec gem await ...` with `rubygems-await` pinned in the project’s Gemfile.lock, **or**
- `gem install rubygems-await -v <pinned>` in the publish job.

Decision: pin in publish job.

Status: ADDRESSED IN PLAN_5 (2026-01-06)

Notes:

- PLAN_5 now explicitly requires installing `rubygems-await` in the publish job via `gem install ... -v <PINNED_VERSION>` (no Bundler dependency in publish jobs).

### B) Build dependency list is heavy; keep it, but justify scope & failure mode

The apt dependency list in the Ruby build workflow is intentionally heavy (TeXLive, ImageMagick, Ghostscript, etc.), and that is maintainer-confirmed (`CLARIFY_PLAN_2`).

Still, the plan should record:

- This is a known risk for apt flakiness and job duration.
- When apt fails, the build should fail fast with actionable logs.

### C) Be explicit about error classification for “already exists” cases

For GitHub Packages RubyGems, the plan already says:

- Prefer fetch-then-compare.
- If push reports “already exists”, retry fetch with backoff.

Recommendation: specify how to robustly detect “not found” vs “unauthorized” vs “transient index” for `gem fetch`, and treat anything ambiguous as failure.

### D) Ensure the resolver output contract changes are called out as breaking

`release-resolve.yml` is a reusable workflow used by both `official.yml` and `buddy.yml`. PLAN_5 adds new outputs (`project_kind` expanded to include `ruby`, plus `is_prerelease`), changes detection semantics, and changes version validation.

Recommendation: explicitly state that:

- All callers must be updated in the same PR.
- Any job `if:` conditions based on `project_kind` must be audited.

Status: ADDRESSED IN PLAN_5 (2026-01-06)

Notes:

- PLAN_5 now includes an explicit compatibility note under the resolver output section calling this a breaking change and requiring caller updates in the same PR.

## Cross-check against confirmed policies (no conflicts found)

- Buddy publishing targets: Node GPR only, Ruby GPR only, Python none. (Matches `CLARIFY_PLAN_0`, PLAN_5 “Confirmed policies”.)
- Buddy must be prerelease-only and must not clobber an existing official GitHub Release. (Matches `CLARIFY_PLAN_1` and existing `buddy.yml` guard pattern.)
- RubyGems.org publishing is Trusted Publishing only. (Matches `CLARIFY_PLAN_0`, `CLARIFY_PLAN_3`.)
- Ruby toolchain and checks: `RUBY_VERSION=3.3`, run `standardrb` and `rspec` when `Gemfile` exists, install required apt deps. (Matches `CLARIFY_PLAN_0`, `CLARIFY_PLAN_2`.)

## Suggested edits to PLAN_5 (document-level)

These are changes to the _plan text_ (not implementation):

1. Add a short subsection: **“Action pinning standard for this plan”** and state whether to use commit SHA pins (preferred) or version tags, and apply consistently to:
    - `rubygems/configure-rubygems-credentials`
    - `ruby/setup-ruby`
    - any future `rubygems-await` usage if invoked via an action (currently it is a gem, not an action)
2. Add a note to the “Maintainer setup checklist”: ensure the `rubygems` GitHub Environment does not require manual approvals (unless that is intentionally desired).
3. Add an explicit guardrail statement under “Hard requirements”:
    - The workflow must not reference any RubyGems API token secret.
    - The workflow must fail if `api-token` or `role-to-assume` is configured.

## Bottom line

PLAN*5 is a solid, security-forward design that is consistent with all maintainer confirmations available in `.AGENTS/CLARIFY_PLAN*\*.md`.

The remaining issues are mostly **specification tightness** (pinning + guardrails), not architecture.
