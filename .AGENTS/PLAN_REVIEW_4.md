<!-- markdownlint-disable MD013 MD024 MD029 -->

# PLAN_REVIEW_4: Strict review of PLAN_4 (RubyGems Trusted Publishing only; no fallback) + idempotent multi-registry publishing

This review evaluates `.AGENTS/PLAN_4.md` against:

- maintainer-confirmed policies in `.AGENTS/CLARIFY_PLAN_0.md` … `.AGENTS/CLARIFY_PLAN_3.md`,
- the current root release workflows in `.github/workflows/*` (especially `release-resolve.yml`, `official.yml`, `buddy.yml`), and
- the stated hard requirement: **RubyGems.org publishing must be Trusted Publishing (OIDC) only, with no fallback to long-lived API keys**.

## Executive summary

`PLAN_4` is the most implementation-ready plan so far. It:

- clearly codifies the **non-negotiable RubyGems.org policy** (Trusted Publishing only; no fallback),
- fixes the resolver architecture so adding a third project kind (Ruby) does not introduce “first match wins” ambiguity,
- extends the repo’s “artifact-first” model to Ruby (build once → publish from `out/*`),
- and adds an operationally necessary contract: **idempotent reruns** for multi-registry publishes.

However, there are **two correctness-level issues** and several “tighten before implementing” items.

### Correctness issues (must address before implementation)

1. **GitHub Packages RubyGems idempotency check is underspecified and likely incorrect as written.**

    `PLAN_4` proposes using `gem fetch ... --source https://rubygems.pkg.github.com/<OWNER>` to retrieve the already-published `.gem` for SHA comparison.

    GitHub’s docs distinguish:
    - publishing auth via `~/.gem/credentials` with `:github: Bearer TOKEN`, vs
    - install/fetch auth via a source URL containing `USERNAME:TOKEN@...`.

    That implies `gem fetch` may _not_ automatically use the `:github` bearer credential for downloads.

    The plan must specify a proven, deterministic fetch method (and how it authenticates) for the digest comparison.

2. **“Idempotent reruns for any registry publish step” is stated as a universal requirement, but the plan does not explicitly cover buddy publishes for Node (and future Ruby).**

    Today, `buddy.yml` publishes Node to GitHub Packages (`publish-node-gpr`) without any rerun-safe handling.

    If the requirement truly applies to “any publish step”, then both:
    - buddy Node GitHub Packages publishes, and
    - buddy Ruby GitHub Packages publishes

    must implement the same “already exists → verify digest → success/fail” behavior.

    If the intent is “official only”, then the hard requirement wording in `PLAN_4` should be narrowed to avoid future drift.

## What PLAN_4 gets right (and matches confirmed policy)

### 1) RubyGems.org Trusted Publishing only (no fallback)

- `PLAN_4` explicitly prohibits RubyGems API keys and any fallback auth.
- It correctly requires `permissions: id-token: write` and uses a GitHub Actions environment (`rubygems`).
- It aligns with maintainer confirmations in `CLARIFY_PLAN_3` that if OIDC cannot be established, the workflow must fail.

Note: The action `rubygems/configure-rubygems-credentials` supports a `trusted-publisher` mode (see its `action.yml`). A “no api-token, no role-to-assume” configuration is consistent with “trusted publisher” usage, but the plan should still _explicitly_ set `trusted-publisher: true` to prevent accidental changes from introducing an implicit fallback.

### 2) Resolver redesign: required before adding Ruby

Current `release-resolve.yml` is sequential (Python → Node) and cannot represent ambiguity.

`PLAN_4` fixes this by:

- defining a detector exit-code contract (0 unique / 2 ambiguous / 3 not found / 1 error),
- running all detectors and failing on within-kind or cross-kind ambiguity,
- emitting `project_kind` and `is_prerelease` outputs.

This is the primary structural prerequisite for Ruby support.

### 3) Ruby artifact-first build workflow design

The new reusable workflow `release-build-ruby-gem.yml` is well aligned with the repo’s existing patterns:

- builds from `target` commit,
- runs checks only when a `Gemfile` is present,
- produces exactly one artifact under a deterministic path (`out/<project>.gem`),
- verifies identity (name + version) from the built `.gem`.

### 4) RubyGems.org idempotency design is sound

Using RubyGems.org’s versions endpoint (`GET /api/v1/versions/<gem>.json`) and comparing the returned `sha` field to the local SHA-256 is correct in principle; the API explicitly exposes `sha` in version objects.

This is exactly the right approach for safe reruns after partial publish.

## Tighten before implementing (recommended but important)

### A) Make Trusted Publishing “no fallback” mechanically enforceable

`PLAN_4` states “no fallback”, but the plan should require implementation details that prevent accidental reintroduction:

- **Do not define** any RubyGems API token secret in the `rubygems` environment.
- Set `trusted-publisher: true` explicitly in the `rubygems/configure-rubygems-credentials` step.
- Consider failing fast if any unexpected `RUBYGEMS_*TOKEN*` env/secret is present (log-only redaction, no secret echo).

### B) RubyGems.org digest comparison: define selection rules

`GET /api/v1/versions/<gem>.json` returns an array of version objects.

The plan should specify:

- select the entry where `number == <version>` AND `platform == "ruby"` (or whatever platform is expected),
- compare `sha` (hex) to local `sha256sum` output (hex),
- if multiple entries match (unlikely but possible across platforms), fail with diagnostics.

### C) Action pinning requirements

The repository already pins many actions to SHAs.

`PLAN_4` should explicitly state that new actions must be pinned (not just “@v1”):

- `ruby/setup-ruby@v1` (pin SHA)
- `rubygems/configure-rubygems-credentials@...` (pin SHA)
- any tool install steps for `rubygems-await` (pin version)

### D) Publish jobs should avoid checkout unless strictly required

`PLAN_4` calls this out for Node. Apply the same to Ruby publish jobs.

Publish jobs should ideally be:

- setup runtime,
- download `out/*`,
- authenticate,
- publish/verify.

No repository checkout.

### E) Reproducibility baseline: note current gaps

The plan’s `SOURCE_DATE_EPOCH`, `TZ`, `LC_ALL` baseline is directionally correct and matches maintainer intent.

But the plan should acknowledge:

- `npm pack` and Python sdist/wheel reproducibility can still vary by tool behavior;
- the baseline is “best effort” unless a double-build comparison is added.

This isn’t a blocker for RubyGems support, but it avoids overpromising determinism.

## Items requiring additional maintainer confirmation

See `.AGENTS/CLARIFY_PLAN_4.md`.

## Conclusion

With two adjustments—(1) specifying a correct and authenticated way to fetch an existing `.gem` from GitHub Packages for digest comparison, and (2) clarifying/enforcing whether idempotent reruns apply to buddy publishes—`PLAN_4` becomes fully consistent with the confirmed policy set and should be safe to implement.
