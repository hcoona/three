<!-- markdownlint-disable MD013 MD024 MD029 MD044 -->

# PLAN_REVIEW_6_2: Independent strict review of PLAN_6 (RubyGems support; Trusted Publishing only)

Date: 2026-01-06

## Scope and constraints

This review covers `.AGENTS/PLAN_6.md` only, validated against:

- The maintainer-confirmed clarifications: `.AGENTS/CLARIFY_PLAN_0.md` … `.AGENTS/CLARIFY_PLAN_6_0.md`.
- The current root release workflows and scripts in this repo (notably `.github/workflows/release-resolve.yml`, `official.yml`, `buddy.yml`, `release-build-python.yml`, `release-build-node-pack.yml`, and `eng/scripts/find_*`).
- Authoritative public documentation for:
    - RubyGems Trusted Publishing (OIDC)
    - GitHub Packages RubyGems registry
    - RubyGems.org API (for `sha` / version lookups)

I intentionally did **not** consult any existing plan-review files (`.AGENTS/PLAN_REVIEW_*.md`) to keep this review independent.

## High-level verdict

`PLAN_6` is coherent, implementable, and aligns with the stated hard requirements, especially:

- RubyGems.org publishing is **Trusted Publishing (OIDC) only**.
- There is **no fallback** to long-lived RubyGems API tokens, and no fallback auth inputs.
- GitHub Packages RubyGems publishing uses `${{ github.token }}` only.
- Buddy runs are intended to be **prerelease-only**, with a **non-clobber** guard for official GitHub Releases.
- Idempotent reruns apply to **all** publishes (official + buddy) via digest verification.

That said, `PLAN_6` is ambitious: it upgrades resolver semantics, adds a new Ruby build workflow, and also tightens npm + PyPI publishing idempotency. The plan is still acceptable, but several details are “failure-prone” unless specified precisely.

The remainder of this review lists:

- **Blocking / must-fix clarifications within the plan** (implementation precision gaps).
- **Non-blocking recommendations** (improve robustness and reduce CI surprises).

## ✅ Requirements compliance (Trusted Publishing only; no fallback)

### RubyGems.org Trusted Publishing (OIDC)

The plan’s approach is compatible with the upstream action API:

- `rubygems/configure-rubygems-credentials` supports an input named `trusted-publisher`.
- Its `action.yml` indicates `trusted-publisher` defaults to `true` when no other configuration is given.
- Therefore, explicitly setting `trusted-publisher: true` and _not_ passing `api-token` / `role-to-assume` is consistent with the “Trusted Publishing only / no fallback” requirement.

This is aligned with the maintainer-confirmed policy in `CLARIFY_PLAN_0` and `CLARIFY_PLAN_3`.

### GitHub Packages RubyGems registry: `${{ github.token }}` only

The plan correctly treats GitHub Packages access as configuration-dependent (package linkage + Actions access), and explicitly rejects adding a PAT fallback. This matches `CLARIFY_PLAN_0`, `CLARIFY_PLAN_1`, and `CLARIFY_PLAN_4`.

## 🔴 Blocking precision gaps / must-fix before implementation

These are not new policy questions, but they are places where the plan currently leaves enough ambiguity that an implementation could violate the _intent_ (idempotency / safety) even if it follows the text.

### 1) Project discovery contract: unify discovery and avoid any “shortest-path fallback”

Maintainer decision (accepted): unify project discovery behind a single entrypoint that uses `fd` to locate:

- `package.json` (Node)
- `pyproject.toml` (Python)
- `<project>.gemspec` (Ruby)

This reduces drift across languages and ensures consistent ambiguity handling.

**Non-negotiable safety constraints (must be explicit in the plan):**

- Multiple matches MUST be treated as ambiguity (no “pick shortest path” fallback).
- The discovery implementation MUST exclude large directories consistently.
- The resolver MUST ensure `fd` is available on the runner (install if needed).

### 2) `release-resolve.yml` must capture _detector exit codes_ without conflating them with “unexpected error”

The plan requires the resolver to:

- run each detector,
- interpret exit code `2` as ambiguity,
- interpret exit code `3` as not found,
- and only treat exit code `1` as unexpected.

**Risk:** naïve shell patterns like `if out=$(cmd); then ... elif out=$(cmd2); then ...` collapse all non-zero codes into a single “false” branch, losing the distinction between `2` and `3`.

**Plan should explicitly require:**

- capturing `stdout`, `stderr`, and `$?` for each detector in a way that preserves the exit code,
- then implementing the selection matrix (0/2/3/1) exactly.

This is essential once Ruby is introduced.

### 3) RubyGems.org idempotency: clarify that RubyGems API `sha` is SHA-256 and must be compared to local SHA-256 of the `.gem` bytes

The plan proposes:

- `GET https://rubygems.org/api/v1/versions/<project>.json`
- compare the returned `sha` field against local digest

This is correct: RubyGems API docs show a `sha` field as a hex digest.

**Plan should explicitly say:**

- local digest must be `sha256(<gem file bytes>)`, in lowercase hex,
- and digest comparison is for the exact `.gem` file, not unpacked contents.

Without that explicitness, an implementation could accidentally compute an incompatible digest (e.g. SHA-512 or digest of extracted files).

### 4) GitHub Packages RubyGems idempotency: authenticated `gem fetch` is correct, but token leakage risk must be addressed explicitly

The plan’s chosen mechanism is:

- use `gem fetch` with an authenticated `--source` URL that embeds `${{ github.actor }}` and `${{ github.token }}`.

This matches the maintainer-confirmed decision (`CLARIFY_PLAN_4`) and matches GitHub Docs (USERNAME must be the GitHub username).

**However:** embedding tokens in command-line arguments is a real leakage hazard:

- some tools echo full URLs on error,
- process arguments can appear in debug logs,
- future maintainers might add `set -x` or echo variables.

The plan already says “do not echo the URL / do not enable tracing”. Good.

Maintainer decision (accepted): do not add additional mitigation here; accept the risk.

This means the implementation may embed `${{ github.token }}` in the `--source` URL, relying only on “do not echo URL” / “do not use shell tracing” discipline.

### 5) Ruby build workflow: step-level behavior is mostly solid, but bundler and test assumptions need to be explicit

The plan says:

- if `Gemfile` exists: run `bundle exec standardrb` and `bundle exec rspec`.

This is consistent with `CLARIFY_PLAN_0`.

Maintainer decision (accepted): do not add an explicit “Gemfile exists?” pre-check; just run the commands.

Implication to encode in the plan:

- Ruby release builds MUST attempt the Bundler-based checks.
- If Bundler context is missing or incomplete (no `Gemfile`, missing gems, incompatible Ruby, etc.), failing the build is expected and correct.

## ✅ Non-blocking recommendations (robustness / maintainability)

### A) Make the RubyGems Trusted Publishing job explicitly minimal

The plan already prefers checkout-free publish jobs. For RubyGems.org Trusted Publishing, keeping the job to:

- setup Ruby
- download artifact
- configure credentials (OIDC)
- idempotency check
- `gem push`

is ideal. Avoid any release tasks that touch git state.

Maintainer decision: accepted.

### B) Add explicit “platform gems not supported” message

Maintainers confirmed platform-specific gems are out of scope (`CLARIFY_PLAN_5`). The plan’s RubyGems.org idempotency check already selects `platform == "ruby"`.

Recommend: log a clear precondition (e.g. “Only platform=ruby gems are supported by this workflow”). This helps future maintainers understand why platform variants are ignored.

Maintainer decision: accepted.

### C) Reproducibility baseline: ensure the epoch is derived from the resolved target commit

The plan proposes `git show -s --format=%ct HEAD` after checking out `inputs.target`.

Recommend: explicitly state that the checkout must be `ref: target` (already true in current build workflows) and that the epoch must be computed _after_ the checkout, not before.

Maintainer decision: accepted.

### D) Keep “publish rerun logic” close to the publish job

The plan aims to add digest verification across npm/PyPI/Ruby registries. Recommend keeping this logic:

- in the publish job itself (not the build job),
- as a small, testable script step per ecosystem,

so that artifact format changes don’t require editing multiple workflows.

Maintainer decision: accepted.

## Consistency check against current repository workflows

`PLAN_6` correctly identifies current gaps:

- Resolver currently uses “Python else Node” logic and cannot safely support Ruby.
- Buddy currently has no prerelease-only guard.
- Builds do not set `SOURCE_DATE_EPOCH` / `TZ` / `LC_ALL`.
- Registry publishes are not rerun-safe.

The plan’s proposed modifications are consistent with the structure of:

- `.github/workflows/release-resolve.yml`
- `.github/workflows/official.yml`
- `.github/workflows/buddy.yml`
- `.github/workflows/release-build-python.yml`
- `.github/workflows/release-build-node-pack.yml`

but they must be implemented as a single coherent PR (resolver output contract changes will otherwise break callers).

## Final recommendation

Proceed with `PLAN_6`.

Before implementation starts, I recommend updating the plan text to explicitly lock down the five “blocking precision gaps” above (especially: detector ambiguity semantics, resolver exit-code handling, and secret-safe `gem fetch` idempotency).

No additional maintainer confirmations appear necessary beyond what is already recorded in `CLARIFY_PLAN_0` … `CLARIFY_PLAN_6_0`.
