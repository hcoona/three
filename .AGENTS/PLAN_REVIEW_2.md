<!-- markdownlint-disable MD013 MD024 MD029 -->

# PLAN_REVIEW_2: Strict review of PLAN_2 (RubyGems Trusted Publishing only)

This review evaluates `.AGENTS/PLAN_2.md` against:

- the repository’s current release architecture (resolve → build artifact → publish → attest → GitHub Release),
- the current workflows in `.github/workflows/*` (especially `release-resolve.yml`, `official.yml`, `buddy.yml`), and
- maintainer confirmations in `.AGENTS/CLARIFY_PLAN_0.md` + `.AGENTS/CLARIFY_PLAN_1.md`.

## Executive summary

`PLAN_2` is a strong, implementation-ready iteration that **directly fixes the key architectural gap in the current resolver** (sequential “first match wins”) and adds Ruby support in a way that matches the repo’s established patterns:

- artifact-first (`out/*`),
- trusted publishing only for RubyGems.org (OIDC; no long-lived tokens),
- consistent official gating on publish + attest,
- consistent buddy safety (non-clobber) and a new prerelease-only guard.

However, there are a few **high-risk operability gaps** that should be addressed _in the plan text_ before implementation:

1. **Ruby release build/test dependencies are underspecified** (some gems require OS packages; a generic build workflow can easily fail).
2. **Partial publish / rerun behavior is not specified** (publishing to two registries is not idempotent; failures can leave a half-published state).
3. **Ruby prerelease detection and validation must be tightly coupled** (to avoid “numeric dot suffix” edge-cases slipping through).
4. **Artifact selection should be deterministic** (avoid accidental multi-`.gem` pushes due to globs).

Items (1) and (2) likely require explicit maintainer decisions → see `.AGENTS/CLARIFY_PLAN_2.md`.

## What PLAN_2 gets right (and matches the repo)

### Resolver design: fixes a real bug in the current workflow

Current `release-resolve.yml`:

- runs Python detector, then Node detector;
- treats any non-zero exit as “no match”; and
- therefore cannot distinguish “not found” vs “ambiguous” vs “script error”.

`PLAN_2` correctly proposes a **detector contract** with distinct exit codes and makes ambiguity a hard error. This is necessary before adding a third kind (Ruby), and it is consistent with the correctness concerns in `PLAN_REVIEW_1.md`.

### Policy alignment: trusted publishing only + artifact-first

- RubyGems.org publishing is explicitly “Trusted Publishing only”.
- GitHub Packages publishing uses `${{ github.token }}` only (no fallback secret), and the plan includes the required package linkage/actions-access policy.
- The plan avoids `rubygems/release-gem@v1` because it tends to assume a Bundler/Rake release flow that can rebuild (which would violate artifact-first).

### Workflow integration: mirrors existing official/buddy shape

The repo’s current release flows already establish:

- `official.yml` gates GitHub Release on publish + attest.
- `buddy.yml` uses `guard-non-clobber` to avoid overwriting official releases.

`PLAN_2` extends this cleanly to Ruby:

- buddy: publish to GitHub Packages only + prerelease-only + non-clobber
- official: publish to both registries + attest + release

### Action usage: the proposed RubyGems credential action has the right inputs

`rubygems/configure-rubygems-credentials` includes an explicit `trusted-publisher` input (see its `action.yml`) and supports a mode where no `api-token` is provided.

`PLAN_2`’s recommendation to set `trusted-publisher: true` explicitly and pin to a commit SHA matches this repo’s current security posture (pin third-party actions; official `actions/*` are version-pinned).

## Critical issues / must tighten before implementation

### 1) Ruby build workflow must address OS-level test dependencies (NEW)

`PLAN_2` states:

- “If `Gemfile` exists: run `bundle exec standardrb` and `bundle exec rspec`.”

This is consistent with `CLARIFY_PLAN_0.md`, but it can fail in practice:

- At least one Ruby project in this workspace (`src/public/lib/asciidoctor-latexmath`) has CI that installs a non-trivial set of OS packages (TeX Live, converters, ImageMagick, Ghostscript, etc.) before running specs.
- A reusable `release-build-ruby-gem.yml` that runs `rspec` without those dependencies will fail, blocking release.

The plan needs an explicit mechanism for Ruby projects to declare release-time system dependencies or to opt into a lighter test suite.

**Action required:** add a section that defines one of these patterns:

- (A) “Release Ruby builds always install a standard baseline of system packages” (heavy; longer runtimes), OR
- (B) “Ruby projects may define a release hook script (e.g., `script/release-check`) and the workflow runs it if present”, OR
- (C) “Release builds run only lint + a minimal test subset; full integration tests are CI-only”, OR
- (D) “Expose a workflow input `apt_packages` / `system_deps_profile` (with a fixed allowlist).”

This requires maintainer confirmation → tracked in `.AGENTS/CLARIFY_PLAN_2.md`.

### 2) Partial publish / rerun semantics are unspecified (NEW)

Official Ruby releases publish to **two** registries:

- RubyGems.org (Trusted Publishing)
- GitHub Packages RubyGems registry

If publishing succeeds on one registry and fails on the other:

- GitHub Release creation is gated, so it will not run (good), **but**
- the successfully-published registry now contains that version, and rerunning will likely fail with “version already exists”.

This same failure mode exists today for Node official (npm + GPR), but `PLAN_2` claims to “unify publish semantics”, so it should explicitly state the policy:

- either “reruns are not supported after partial publish; bump version / yank manually”,
- or add a preflight check and fail early if either registry already has the version,
- or add registry-specific idempotency logic (more work).

This needs an explicit decision → tracked in `.AGENTS/CLARIFY_PLAN_2.md`.

### 3) Ruby prerelease detection must be coupled to validation

`PLAN_2` proposes:

- prerelease detection for Ruby: “version contains additional dot segments beyond `MAJOR.MINOR.PATCH`”.

This is only safe if the Ruby version validator enforces the confirmed format:

- allowed: `MAJOR.MINOR.PATCH` or `MAJOR.MINOR.PATCH.<alpha>.<n>...` style (e.g., `1.2.3.beta.1`)
- disallowed: numeric-only dot suffixes (e.g., `1.2.3.4`), `1.0`, dates, `-` prerelease separator, `+` build metadata

**Action required:** in the plan, specify the Ruby version regex/grammar (or a set of explicit rules) so that `is_prerelease` cannot be “tricked” by versions that should be rejected.

### 4) Artifact selection must be deterministic (avoid `out/*.gem` foot-guns)

Several steps use globs:

- build stages `out/*`
- publish stages push `out/*.gem`

Globs are convenient but risky:

- a directory could contain multiple `.gem` files (stale outputs, multiple gems, or accidental files),
- `gem push` will attempt to push each file, creating confusing partial success/failure.

**Action required:** update the plan to require:

- after build, determine the exact `.gem` path (single file) and record it (step output),
- publish jobs should use that single path, not a glob.

This also improves traceability in logs.

## Medium issues / improvements (should address, but not blockers)

### A) Add a short doc note reconciling GitHub Packages auth wording

GitHub’s RubyGems registry docs contain a prominent note that GitHub Packages “only supports authentication using a PAT (classic)”, but the same docs also describe using `GITHUB_TOKEN` in Actions workflows _when the package is associated with the workflow repository_.

`PLAN_2` already captures the practical requirements (repository linkage / Actions access). It would be helpful to quote or link the exact “GITHUB_TOKEN is supported for workflow repo-associated packages” guidance to avoid future confusion.

### B) Ruby build workflow should follow existing `out/` hygiene

Other build workflows (`release-build-python.yml`, `release-build-node-pack.yml`) do:

- `rm -rf $GITHUB_WORKSPACE/out`
- recreate `out/`
- then upload `out/*`

Ensure `release-build-ruby-gem.yml` does the same, and builds into a controlled directory to avoid stale artifacts.

### C) Consider making `rubygems_version` non-empty only for Ruby (OK as-is)

Keeping `rubygems_version` explicit is fine. If you want downstream steps to be simpler, you could also set it for non-Ruby (equal to `version`) and just document that only Ruby uses it today.

Not required; just a simplification option.

## Suggested concrete edits to PLAN_2 (summary)

1. Add a dedicated section “Ruby release checks: system dependencies strategy” and pick a mechanism (see Clarify #1).
2. Add a dedicated section “Rerun semantics / partial publish policy” (see Clarify #2).
3. Tighten the Ruby version validator description so it is unambiguous and matches the confirmed policy.
4. Require deterministic `.gem` selection (no publish globbing).

## Acceptance criteria traceability

`PLAN_2` acceptance criteria are broadly correct. With the above changes, they become much more testable:

- resolver ambiguity errors are deterministic (exit-code contract)
- buddy prerelease-only is enforced consistently (new `is_prerelease` output)
- Ruby build produces exactly one `.gem` and publish jobs use that exact file
- official gating ensures RubyGems + GPR publish + attest all succeeded before GitHub Release
