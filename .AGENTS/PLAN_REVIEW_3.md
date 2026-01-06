<!-- markdownlint-disable MD013 MD024 MD029 -->

# PLAN_REVIEW_3: Strict review of PLAN_3 (RubyGems Trusted Publishing only + idempotent multi-registry publishes)

This review evaluates `.AGENTS/PLAN_3.md` against:

- the repository’s current release architecture (resolve → build artifact → publish → attest → GitHub Release),
- the current root workflows in `.github/workflows/*` (especially `release-resolve.yml`, `official.yml`, `buddy.yml`),
- maintainer confirmations in `.AGENTS/CLARIFY_PLAN_0.md`, `.AGENTS/CLARIFY_PLAN_1.md`, `.AGENTS/CLARIFY_PLAN_2.md`, and
- the _actual Ruby package currently present in this workspace_ (`src/public/lib/asciidoctor-latexmath`).

## Executive summary

`PLAN_3` is a meaningful upgrade over `PLAN_2` and is close to being implementation-ready:

- It fixes the **resolver ambiguity hazard** (today `release-resolve.yml` is “first match wins” for Python vs Node), which becomes non-negotiable once Ruby is added.
- It incorporates maintainer-confirmed requirements that were previously “operability gaps”: - release-time OS dependencies for Ruby tests (TeX/ImageMagick/Ghostscript stack), - **idempotent reruns** for multi-registry publishes (Ruby + Node), and now also for PyPI.
- It improves safety: deterministic single-artifact outputs, prerelease-only buddy enforcement, and a reproducibility baseline (`SOURCE_DATE_EPOCH`, `TZ`, `LC_ALL`).

However, there are **two hard blockers in the current repository state** (not just workflow design), plus several plan-text issues that should be tightened before implementation.

### Hard blockers (repo state)

1. **Ruby gem source/layout readiness (RESOLVED).**

    This was previously a hard blocker because the only gem in the repo (`src/public/lib/asciidoctor-latexmath`) was missing `lib/`, while the gemspec required `asciidoctor/latexmath/version`.

    Status: **RESOLVED (2026-01-05)** by commit `51559b8a3ac7ddf2083b90715327d72d6234de55` ("chore(asciidoctor-latexmath): Add Missing Files & Adapt Gem Spec for Migration").

2. **Gemspec metadata alignment with the monorepo policy (PARTIALLY RESOLVED).**

    This was previously a blocker because the gemspec metadata pointed to `hcoona/asciidoctor-latexmath` and omitted the maintainer-required `github_repo` metadata needed for GitHub Packages auto-linking.

    Status: **PARTIALLY RESOLVED (2026-01-05)** by the same commit above:
    - `source_code_uri` / `bug_tracker_uri` / `documentation_uri` have been adapted to point at `hcoona/three`.
    - `github_repo` is still not present in the gemspec metadata; if we continue to require `github_repo = https://github.com/hcoona/three.git`, this remains an actionable follow-up.

These must be addressed (or explicitly scoped out) before Ruby support is considered “ready”.

## What PLAN_3 gets right (and matches the repo)

### 1) Resolver hardening: required for correctness

Current `release-resolve.yml`:

- treats any detector failure the same,
- runs detectors sequentially (`if python … elif node … else fail`),
- cannot represent ambiguity, and
- will become actively unsafe once a third kind exists.

`PLAN_3` correctly specifies:

- a detector exit-code contract (0 unique / 2 ambiguous / 3 not found / 1 error), and
- cross-kind selection rules (fail fast on within-kind ambiguity; fail if >1 kind matches).

This is the most important correctness improvement.

### 2) Artifact-first + deterministic artifact paths

Requiring a single explicit output (`out/<project>.gem`) avoids the “glob publishes multiple files” foot-gun and aligns well with existing Node behavior (`out/npmjs.tgz`, `out/gpr.tgz`).

### 3) Buddy safety and prerelease-only guard

Buddy already has a non-clobber guard (`buddy.yml` checks existing GitHub Release `prerelease` flag). Adding `is_prerelease` from the resolver and failing buddy runs when it’s not `true` is the missing second half.

### 4) Idempotent reruns for multi-registry publishes

This is the right operational choice (CLARIFY_PLAN_2). Without it, official runs can become unrecoverable after a partial publish.

## Critical issues / must tighten in PLAN_3 before implementation

### 1) Ruby package readiness must be explicitly addressed

`PLAN_3` assumes that if a gemspec exists, then:

- `gem build <project>.gemspec` can run,
- tests can run, and
- the artifact is meaningful.

This section was initially motivated by a real migration failure mode (a gemspec existing without a buildable `lib/` tree).

Status update: `asciidoctor-latexmath` is now buildable as of commit `51559b8a3ac7ddf2083b90715327d72d6234de55`, but the plan should still codify the readiness checklist as a general requirement (so future Ruby gems cannot regress into a non-buildable state).

**Plan text should add an explicit prerequisite / readiness checklist**, e.g.:

- package*dir contains `<project>.gemspec` \_and* `lib/` _and_ the required version file,
- `Gem::Specification.load(<gemspec>)` succeeds at the target commit,
- optionally: `bundle exec rspec` is runnable after installing baseline OS deps.

Otherwise the pipeline will ship “Ruby support” that cannot release the repo’s only Ruby project.

### 2) RubyGems Trusted Publishing: action behavior must be proven

`PLAN_3` states that `rubygems/configure-rubygems-credentials` can be used for Trusted Publishing without `api-token` and without `role-to-assume`.

Because this is the _single point of authentication_ for RubyGems.org (Trusted Publishing only, no long-lived secrets), the plan should:

- require a smoke test (one can be done on a throwaway gem/version), and
- define an OIDC-only fallback implementation if the action’s “no role-to-assume” mode does not work as assumed.

A safe fallback that still respects policy is implementing the documented RubyGems OIDC exchange API and writing the short-lived API key into credentials for the `gem push` step.

### 3) Working-directory handling must be specified for Ruby

The new reusable workflow will have to execute within `package_dir` for:

- Bundler (`Gemfile`, `Gemfile.lock`),
- `bundle exec standardrb`,
- `bundle exec rspec`, and
- building the gem from `<project>.gemspec`.

`PLAN_3` should explicitly state that Ruby commands run with `working-directory: ${{ inputs.package_dir }}`.

Without that, bundler-cache will target the wrong directory and `gem build` will either fail or build the wrong thing.

### 4) GitHub Packages RubyGems idempotency: the fetch mechanism is underspecified

The plan’s idempotency check for GitHub Packages uses `gem fetch ... --source ...`.

This can work, but the workflow must be explicit about:

- how authentication is provided for **fetch**, not just push,
- the expected credentials file format and permissions (`chmod 0600 ~/.gem/credentials`), and
- where the fetched file lands so its digest can be computed deterministically.

If this is not tightened, the idempotency logic may fail in exactly the rerun scenario it is meant to handle.

### 5) Node idempotent digest comparison: specify one canonical method

`PLAN_3` mentions `dist.integrity` _or_ `dist.shasum`.

To be testable and reliable, the plan should choose one canonical check:

- Prefer comparing to `dist.integrity` (SRI, typically SHA-512),
- and compute the matching SRI from the local tarball (or fetch the remote tarball and compare SHA-512 directly).

Mixing SHA-256/SHA-1/SHA-512 without a defined mapping will create false mismatches.

## Medium issues / improvements (recommended)

### A) Reproducibility baseline should be treated as “best effort” unless verified

Setting `SOURCE_DATE_EPOCH` is a good baseline. But `uv build` and `npm pack` reproducibility can still vary due to tooling behavior.

Consider adding a small, optional verification step (only in CI for release builds, or only when `REPRO_CHECK=true`): build/pack twice and compare digests, mirroring the existing Ruby `release:verify` concept.

### B) Enforce gemspec metadata policy in CI (optional but valuable)

Given the `github.token`-only policy for GitHub Packages, it may be worth failing the Ruby build job if:

- `gemspec.metadata["github_repo"]` is missing or not `https://github.com/hcoona/three.git`.

This converts “checklist drift” into an actionable error.

### C) Action pinning consistency

The repository already pins some third-party actions to SHAs (good). `PLAN_3` should explicitly state the expected pinning approach for any newly introduced actions (`ruby/setup-ruby`, `rubygems/configure-rubygems-credentials`).

## Suggested concrete edits to PLAN_3 (summary)

1. Add a section “Ruby package readiness prerequisites” and require that the gem can actually be built at `target`.
2. Add a section “Trusted Publishing implementation proof + fallback” for RubyGems.org.
3. Specify `working-directory` requirements for Ruby build/test/publish steps.
4. Specify the exact authentication + download path for the GitHub Packages RubyGems idempotency fetch.
5. Choose a single digest algorithm and comparison method for npmjs/GPR idempotent checks.

## Conclusion

Once the Ruby project source/layout and gemspec metadata issues are resolved, `PLAN_3` becomes a strong, implementation-ready plan that aligns publish semantics across Python/Node/Ruby and significantly improves release operability.
