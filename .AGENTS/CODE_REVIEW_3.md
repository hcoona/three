<!-- markdownlint-disable MD013 MD024 MD044 -->

# CODE_REVIEW_3: `.github/` diff (origin/main...HEAD)

Date: 2026-01-07

Scope reviewed:

- `git diff origin/main...HEAD -- .github`
- Files changed:
    - `.github/workflows/buddy.yml`
    - `.github/workflows/official.yml`
    - `.github/workflows/release-build-node-pack.yml`
    - `.github/workflows/release-build-python.yml`
    - `.github/workflows/release-build-ruby-gem.yml` (new)
    - `.github/workflows/release-resolve.yml`

Reviewer intent:

- Strictly verify alignment with `PLAN_6` hard requirements, especially:
    - RubyGems.org publishing must use Trusted Publishing (OIDC) only, with **no fallback secrets/inputs**.
    - All publishes must be rerun-safe via digest-based idempotency.
    - Buddy must be prerelease-only and must not clobber an official GitHub Release.
    - New third-party actions introduced by the plan must be pinned to full commit SHAs.

## Executive summary

Overall, the changes are strongly aligned with `PLAN_6`:

- Ruby is now a first-class project kind in the resolver (`release-resolve.yml`), and both entry workflows (`official.yml`, `buddy.yml`) wire Ruby build/publish/release jobs behind `project_kind == 'ruby'`.
- RubyGems.org publishing in `official.yml` uses `rubygems/configure-rubygems-credentials` with `trusted-publisher: true` and grants `id-token: write`, satisfying the “Trusted Publishing only” requirement (no API key wiring).
- Idempotency improvements landed across registries:
    - PyPI publish now validates remote digests and uses `skip-existing: true` only after digest checks.
    - npmjs publish now verifies `dist.integrity` vs local tarball SRI and handles “already exists” idempotently.
    - RubyGems.org publish checks the v2 API `sha` vs local SHA-256 and treats matching digests as success.
- Reproducibility baseline (`TZ`, `LC_ALL`, `SOURCE_DATE_EPOCH`) is applied in Python build, Node pack (both tarballs), and Ruby gem build.

The remaining concerns are mostly around robustness and maintainability, plus one potentially important workflow-consistency issue for manual runs.

## Blockers / high-risk issues

### 1) Manual releases may run publish scripts from a different revision than the release target

In `official.yml` (and similarly in `buddy.yml`), some publish jobs perform a lightweight checkout only to access checked-in scripts (e.g. `eng/scripts/publish_node_gpr_idempotent.sh`). In `official.yml`, the checkout step in `publish-node` is now:

- At the time of review, it used `actions/checkout@v6` without pinning `ref: ${{ needs.resolve.outputs.target }}`.
    - This has since been fixed (see “Follow-up changes applied after review”).

For `workflow_dispatch` runs, this means:

- build artifacts are created from `needs.resolve.outputs.target` (good and intended),
- but the publish scripts are checked out from the workflow run ref (often default branch HEAD), which might diverge from the target commit.

Risk:

- If publish scripts evolve (flags, assumptions, artifact names), manual releases that target older commits may become brittle or silently behavior-drift.

Recommendation:

- For jobs that checkout solely to obtain scripts, consider checking out `ref: ${{ needs.resolve.outputs.target }}` (or otherwise ensure scripts used are versioned with the artifact).

This is not explicitly forbidden by `PLAN_6`, but it is a correctness/operability risk that tends to surface during emergency reruns.

## Trusted Publishing (RubyGems.org) compliance

### What looks correct

- `official.yml` uses:
    - `permissions: id-token: write` in `publish-ruby-rubygems`.
    - `uses: rubygems/configure-rubygems-credentials@<sha>` with `trusted-publisher: true`.
    - No `api-token`, no long-lived key secret wiring, no role assumption.

This matches `PLAN_6` hard requirements and the explicit note that no runtime self-detection guard is required.

### What to keep an eye on (non-blocking)

- The RubyGems CLI can authenticate via `~/.gem/credentials` or the `GEM_HOST_API_KEY` environment variable.
    - This workflow does not set `GEM_HOST_API_KEY` (good).
    - Avoid adding future steps that export a long-lived `GEM_HOST_API_KEY` or similar (would violate the plan).

## Idempotency & digest checks

### PyPI: `official.yml` publish-python

Strengths:

- Explicitly validates per-file SHA-256 digests against PyPI JSON before publishing.
- Uses `skip-existing: true` only after digest verification.

Watch-outs:

- `curl` calls are single-shot. The behavior intentionally fails fast on unexpected HTTP statuses.
    - That is safe, but may increase rerun frequency during transient PyPI disruptions.

### npmjs: `official.yml` publish-node

Strengths:

- Uses canonical SRI (`sha512-<base64(sha512(tarball_bytes))>`) to compare against `npm view ... dist.integrity`.
- Treats “already exists” as success only when integrity matches.
- Separates registry-specific behavior:
    - GPR uses `out/gpr.tgz` via the shared script.
    - npmjs uses `out/npmjs.tgz` with explicit integrity checks.

Potential improvement:

- The “already exists” detection in the publish stderr grep is pragmatic; consider documenting the expected error strings (npm occasionally changes wording).

### RubyGems.org: `official.yml` publish-ruby-rubygems

Strengths:

- Checks `https://rubygems.org/api/v2/...` for version existence and compares remote `sha` against local `sha256sum`.
- Handles the race where `gem push` reports “already exists” by waiting (`rubygems-await`) and re-checking digest.
- Correctly fails on 429 / 5xx rather than misclassifying as “not found”.

Notes:

- The approach assumes the API v2 `sha` field corresponds to the SHA-256 of the `.gem` bytes (as intended by `PLAN_6`).

## Resolver changes (`release-resolve.yml`)

Strengths:

- Unified detector integration with explicit exit code contract is a major reliability upgrade.
- Adds `is_prerelease` output and centralizes prerelease detection logic in one place.
- Adds Ruby version validation and prerelease derivation consistent with `PLAN_6` rules.

Concerns:

- Installing `fd` via mise adds one more moving part (already accepted per `CLARIFY_CR_2`).
- The resolver now does network `git fetch --all --tags`; that is expected, but it increases runtime and failure surface.

## New Ruby build workflow (`release-build-ruby-gem.yml`)

Matches `PLAN_6` closely:

- Uses the reproducibility baseline.
- Builds directly to the expected output via `gem build ... --output ...`.
- Enforces “exactly one gem in out/” and verifies name/version via `gem specification`.
- Runs Bundler-based checks only when a `Gemfile` exists.

Operational considerations:

- The apt dependencies list is heavy; it is per-plan, but it will noticeably increase build time and can be a flake source if apt mirrors misbehave.

## Buddy workflow (`buddy.yml`) changes

Strengths:

- Adds an early prerelease-only guard using `needs.resolve.outputs.is_prerelease`, satisfying the buddy safety requirement without re-parsing versions.
- Adds Ruby build/publish-to-GPR/release path gated on `project_kind == 'ruby'`.
- Ensures the existing “non-clobber official release” guard remains in place.

## Third-party action pinning

Compliant with `PLAN_6`:

- Newly introduced third-party actions are pinned to full commit SHAs (e.g., `dcarbone/install-jq-action`, `jdx/mise-action`, `ruby/setup-ruby`, `rubygems/configure-rubygems-credentials`).

## Nits / polish

- Consider normalizing naming and step labels for consistency (e.g., “Checkout (workflow scripts)” is used in some publish jobs, but not all).
- Consider extracting the larger inline bash logic blocks (PyPI digest validation, npmjs integrity logic, RubyGems idempotency logic) into `eng/scripts/` for readability and to reduce YAML churn (consistent with the adopted “Scheme A”).

## Final verdict

- **Meets the PLAN_6 goals** for RubyGems Trusted Publishing only and digest-based idempotency.
- **Strongly recommend** addressing (or at least acknowledging) the “publish scripts revision skew” risk for manual releases.

## Follow-up changes applied after review

Based on maintainer feedback:

1. Agreed (implemented): pin the lightweight “Checkout (workflow scripts)” steps to the resolved target commit to avoid revision skew.
    - Updated in:
        - `.github/workflows/official.yml` (publish jobs that rely on checked-in scripts)
        - `.github/workflows/buddy.yml` (publish jobs that rely on checked-in scripts)

2. Out of scope: keep the current Ruby build apt dependency set as-is.

3. Skipped: no additional hardening of npm error-string matching beyond the existing logic.

4. Agreed (implemented): extract large inline idempotency blocks into `eng/scripts/` to reduce YAML churn and improve readability.
    - Added scripts:
        - `eng/scripts/validate_pypi_remote_digests.sh`
        - `eng/scripts/publish_node_npmjs_idempotent.sh`
        - `eng/scripts/publish_rubygems_org_idempotent.sh`
    - Updated `.github/workflows/official.yml` to call these scripts.
