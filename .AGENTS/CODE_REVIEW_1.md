<!-- markdownlint-disable MD013 MD024 -->

# Code Review: `.github` changes vs `origin/main` (PLAN_6) — RubyGems support (Trusted Publishing only)

Date: 2026-01-06

Scope reviewed:

- `git diff origin/main...HEAD -- .github`
- Files changed:
    - `.github/workflows/release-build-ruby-gem.yml` (new)
    - `.github/workflows/release-resolve.yml`
    - `.github/workflows/official.yml`
    - `.github/workflows/buddy.yml`
    - `.github/workflows/release-build-node-pack.yml`
    - `.github/workflows/release-build-python.yml`

Constraints from `PLAN_6` considered:

- RubyGems.org publishes must be **Trusted Publishing (OIDC) only** (no API tokens, no fallback).
- GitHub Packages RubyGems publishes must use **`${{ github.token }}` only** (no PAT fallback).
- Buddy runs must be **prerelease-only** and must not clobber an official GitHub Release.
- Publishing must be **rerun-safe** via digest verification (never overwrite registries).
- Newly introduced **third-party** actions must be pinned to a **full commit SHA**.

## Executive summary

Overall, the workflow changes implement the PLAN_6 architecture correctly:

- Ruby project detection is integrated into the reusable resolver.
- A new reusable Ruby gem build workflow is introduced.
- Official workflow adds Ruby publishing to both GitHub Packages (RubyGems registry) and RubyGems.org.
- RubyGems.org publishing uses `rubygems/configure-rubygems-credentials` with `trusted-publisher: true` and OIDC permissions, with no token-based fallback.
- Publishing steps across PyPI, npm/GPR, and Ruby registries add digest-gated idempotency.

No evidence of prohibited fallback credentials for RubyGems.org was found in the reviewed `.github` changes.

## ✅ Hard requirements audit

### RubyGems.org publishing: Trusted Publishing (OIDC) only

- Implemented in `.github/workflows/official.yml` job `publish-ruby-rubygems`:
    - Uses `rubygems/configure-rubygems-credentials@bc6dd217f8a4f919d6835fcfefd470ef821f5c44` with `trusted-publisher: true`.
    - Job permissions include `id-token: write`.
    - Uses `environment: rubygems`.
    - Publishes from the built artifact `out/<project>-<version>.gem`.

✅ No `api-token`, no `role-to-assume`, and no RubyGems API key secrets were observed.

### GitHub Packages RubyGems registry: `${{ github.token }}` only

- Implemented in `.github/workflows/official.yml` job `publish-ruby-gpr` and `.github/workflows/buddy.yml` job `publish-ruby-gpr`:
    - Writes `:github: Bearer ${{ github.token }}` to the path returned by `gem env credentials`.
    - Publishes using `gem push --key github --host https://rubygems.pkg.github.com/<owner>`.

✅ No PAT or alternate token inputs were observed.

### Buddy safety

- Prerelease-only enforcement added in `.github/workflows/buddy.yml` via job `guard-prerelease-only` based on `needs.resolve.outputs.is_prerelease`.
- Official-release clobber guard remains and is used as an explicit prerequisite.

✅ Meets the “buddy prerelease-only + non-clobber” requirements.

### Third-party action pinning

Newly introduced third-party actions in these changes appear pinned to full commit SHAs, including:

- `ruby/setup-ruby@4c24fa5ec04b2e79eb40571b1cee2a0d2b705771`
- `rubygems/configure-rubygems-credentials@bc6dd217f8a4f919d6835fcfefd470ef821f5c44`

✅ Appears compliant with the stated pinning policy for third-party actions.

## Review notes by workflow

### 1) `.github/workflows/release-build-ruby-gem.yml` (new)

Strengths:

- Uses `gem build ... --output out/<project>-<version>.gem` and then enforces:
    - expected artifact exists
    - exactly one `*.gem` under `out/`
    - no extra `*.gem` created in the package directory
- Optional Bundler checks are enforced when a `Gemfile` exists (`standardrb`, `rspec`), and explicitly skipped otherwise.
- Reproducibility baseline is applied for the build-producing command (`TZ`, `LC_ALL`, `SOURCE_DATE_EPOCH`).
- Post-build verification checks gem `name` and `version` match the workflow inputs.

Concerns / suggestions:

- The dependency install step uses `apt-get install -y` without `--no-install-recommends`. Not incorrect, but it can increase variability and runtime.

Status: **Addressed**

- Updated `.github/workflows/release-build-ruby-gem.yml` to use `apt-get install -y --no-install-recommends`.

### 2) `.github/workflows/release-resolve.yml`

Strengths:

- Moves to a unified project detector with an explicit exit-code contract and preserves diagnostics.
- Installs `fd` via mise (bounded install, not full toolchain), then uses the unified discovery script.
- Validates version formats per kind and exports `is_prerelease` for downstream buddy gating.

Potential edge case to consider:

- For Ruby prerelease detection, `is_prerelease` is inferred by a regex `^[0-9]+\.[0-9]+\.[0-9]+\..+$`. This matches the PLAN_6 rule (any segment beyond MAJOR.MINOR.PATCH), but correctness ultimately depends on `validate_rubygems_version.py` enforcing the “suffix must contain a letter” constraint.

### 3) Ruby publishing to GitHub Packages (Buddy + Official)

Good:

Issues (should fix):

1. **Potential ambiguous fall-through to push**
    - Current logic only treats `gem fetch` as “already exists” if `(fetch_rc == 0 && expected file exists)`.
    - If `gem fetch` exits `0` but does **not** produce the expected file (unexpected, but possible due to CLI quirks, platform naming, or path changes), the script will fall through and attempt `gem push`.

    This contradicts the PLAN_6 requirement to never fall through to push on ambiguous fetch outcomes.

    Recommendation:
    - Treat `(fetch_rc == 0 && expected file missing)` as a hard failure with diagnostics (e.g., `ls -la` in the temp dir), not as “not found”.

    Status: **Addressed**
    - Updated both `.github/workflows/buddy.yml` and `.github/workflows/official.yml` to fail fast when `gem fetch` exits 0 but the expected file is missing, instead of falling through to `gem push`.

2. **Token exposure risk via authenticated source URL**
    - The `gem fetch` source URL embeds `${ACTOR}:${TOKEN}@...`.
    - While `${{ github.token }}` is typically masked in logs, error output printed from `fetch_err` could still include the full URL.

    Recommendation: - Prefer a non-credentialed `--source https://rubygems.pkg.github.com/<owner>/` and rely on the already-written credentials file (`gem env credentials`)
    for authentication, avoiding embedding tokens in URLs entirely.

    Status: **Addressed (Scheme B)**
    - Updated both `.github/workflows/buddy.yml` and `.github/workflows/official.yml` to use:
        - `--source "https://rubygems.pkg.github.com/${OWNER}/"`
        - and rely on the credentials file written via `gem env credentials`.

### 4) Ruby publishing to RubyGems.org (Official)

Good:

- Uses Trusted Publishing credentials action with `trusted-publisher: true` and OIDC permissions.
- Idempotency uses RubyGems.org API v2 endpoint and compares remote `sha` to local SHA-256.
- Handles rate limit / 5xx as hard failures, not as “not found”.
- Uses `rubygems-await` with a pinned version to handle eventual consistency.

Minor suggestion:

- Consider adding `--no-document` to `gem install rubygems-await` to reduce install time/noise.

Status: **Addressed**

- Updated `.github/workflows/official.yml` to install `rubygems-await` with `--no-document`.

### 5) Node and Python publish idempotency

Python:

- `publish-python` pre-validates remote digests by filename and sha256, then publishes with `skip-existing: true`.
- This matches PLAN_6’s “partial publish safe” requirement.

Node:

- Both GPR and npmjs publishing compute local tarball SRI (sha512) and compare to `dist.integrity` before publishing.
- Failure classification distinguishes 404 vs 401/403 vs other errors.

One thing to double-check operationally (not strictly Ruby-related):

- The npmjs publish path intends to use npm Trusted Publishing (OIDC) and provides `id-token: write`, but it does not explicitly pass any npm auth token. Ensure the chosen mechanism is sufficient for npm’s Trusted Publishing flow as implemented by the npm CLI used on `ubuntu-latest`.

Status: **Out of scope** (per maintainer decision)

## Summary of requested follow-ups

### Should fix (recommended before merging)

- Prevent ambiguous `gem fetch` outcomes from falling through to `gem push` in the GitHub Packages RubyGems idempotency steps. (**Done**)
- Avoid embedding `${{ github.token }}` in the `--source` URL; rely on RubyGems credentials instead. (**Done; Scheme B**)

### Nice to have

- Consider `--no-install-recommends` for apt dependencies (build reproducibility and runtime). (**Done**)
- Add `--no-document` when installing helper gems (minor). (**Done**)
