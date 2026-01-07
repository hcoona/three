<!-- markdownlint-disable MD013 MD024 MD029 -->

# CODE_REVIEW_2: Strict review of `.github` changes (origin/main...HEAD)

Date: 2026-01-07

Scope: `git diff origin/main...HEAD -- .github`.

Constraints observed:

- Did **not** read any `.AGENTS/CODE_REVIEW_*.md` or `.AGENTS/PLAN_REVIEW_*.md`.
- Used `git --no-pager` to avoid pager issues.

## Executive summary

These workflow changes implement the core goals of `PLAN_6` inside `.github/`:

- Add Ruby (gem) build + publish support (GitHub Packages RubyGems + RubyGems.org).
- Enforce buddy prerelease-only via a single guard job driven by the resolver output.
- Make publishing rerun-safe via digest verification for:
    - Node (npmjs + GitHub Packages) using tarball SRI.
    - Python (PyPI) using per-file sha256.
    - Ruby (RubyGems.org + GitHub Packages) using sha256.
- Improve artifact determinism by setting `SOURCE_DATE_EPOCH`, `TZ`, `LC_ALL` in build workflows.
- Switch project discovery in `release-resolve.yml` to a unified detector requiring `fd`.

Net: the overall architecture is coherent and aligns with the repo’s “build once, publish from artifacts” approach.

## ✅ Resolved/clarified items

### 1) GitHub Packages RubyGems: `gem fetch` authentication

Files:

- `.github/workflows/official.yml` (`publish-ruby-gpr`)
- `.github/workflows/buddy.yml` (`publish-ruby-gpr`)

Clarification provided after this review (now persisted in `.AGENTS/CLARIFY_CR_2.md`):

- `gem fetch` can authenticate using the RubyGems credentials file (as resolved by `gem env credentials`).
- Therefore, the current approach of writing `:github: Bearer <token>` to the credentials file and using an unauthenticated `--source "https://rubygems.pkg.github.com/<OWNER>/"` is valid.

Action taken:

- No workflow change is required for this item.

## High-priority issues (accepted / addressed)

### 2) `fd` is installed as “latest” (tool drift risk)

File:

- `.github/workflows/release-resolve.yml`

Observation:

- The resolver now runs `mise install fd`.
- In `.mise.toml`, `fd = "latest"`, and `.mise.lock` does not appear to pin `fd` currently.

Risk:

- Workflows can change behavior over time because the discovered `fd` version can drift.
- This can impact project discovery reliability and diagnostics.

Decision:

- This risk is accepted for now (see `.AGENTS/CLARIFY_CR_2.md`).

### 3) Duplication of large publish scripts increases maintenance surface

Files:

- `.github/workflows/official.yml`
- `.github/workflows/buddy.yml`

Observation:

- Node (GPR) idempotent publish logic is duplicated between official and buddy.
- Ruby (GPR) idempotent publish logic is duplicated between official and buddy.

Risk:

- Fixes (e.g., the auth bug above) must be applied in two places.

Action taken:

- Implemented Scheme A: extracted duplicated publish logic into checked-in scripts under `eng/scripts/` and updated workflows to invoke them.

## Correctness and policy compliance checks

### RubyGems.org Trusted Publishing only (no fallback)

Files:

- `.github/workflows/official.yml` (`publish-ruby-rubygems`)

✅ Good:

- Uses `environment: rubygems`.
- Uses `permissions: id-token: write`.
- Uses `rubygems/configure-rubygems-credentials` with `trusted-publisher: true`.
- Does **not** set `api-token` and does **not** add any RubyGems API key secret.

### GitHub Packages RubyGems publish uses `github.token` only

Files:

- `.github/workflows/official.yml` (`publish-ruby-gpr`)
- `.github/workflows/buddy.yml` (`publish-ruby-gpr`)

✅ Good:

- Uses only `${{ github.token }}`.
- No PAT/secret fallback.
- Uses explicit `packages: write` permission.

### Buddy prerelease-only is enforced centrally

Files:

- `.github/workflows/buddy.yml`
- `.github/workflows/release-resolve.yml`

✅ Good:

- `release-resolve.yml` emits `is_prerelease`.
- Buddy adds a single guard job (`guard-prerelease-only`) and wires it into all publish/release jobs, including WXT.

### Idempotent rerun semantics

#### PyPI (official)

File:

- `.github/workflows/official.yml` (`publish-python`)

✅ Good:

- Fetches `https://pypi.org/pypi/<project>/json` and compares per-file sha256 for `releases[version]`.
- Uses `skip-existing: true` only after verifying matching hashes.

Minor note:

- For PyPI partial publish scenarios, this approach is correct (file-level idempotency).

#### npm / GitHub Packages (official + buddy)

Files:

- `.github/workflows/official.yml` (`publish-node`)
- `.github/workflows/buddy.yml` (`publish-node-gpr`)

✅ Good:

- Computes SRI from the tarball bytes and compares with `npm view ... dist.integrity`.
- Treats “exists but differs” as failure.

Caveat:

- Error classification relies on grepping stderr for `E404`, `E401`, etc. This is pragmatic but somewhat brittle across npm CLI versions/locales.

#### RubyGems.org (official)

File:

- `.github/workflows/official.yml` (`publish-ruby-rubygems`)

✅ Good:

- Uses RubyGems v2 API `.../versions/<version>.json?platform=ruby` and compares `.sha` vs local sha256.
- Handles eventual consistency using pinned `rubygems-await`.

### Reproducibility baseline in build workflows

Files:

- `.github/workflows/release-build-python.yml`
- `.github/workflows/release-build-node-pack.yml`
- `.github/workflows/release-build-ruby-gem.yml`

✅ Good:

- Sets `TZ=UTC`, `LC_ALL=C.UTF-8`, and `SOURCE_DATE_EPOCH` scoped to the build-producing commands.

## Security review

### Action pinning

✅ Good:

- New third-party actions are SHA-pinned:
    - `jdx/mise-action`
    - `dcarbone/install-jq-action`
    - `ruby/setup-ruby`
    - `rubygems/configure-rubygems-credentials`

Note:

- First-party actions remain tag-pinned (e.g., `actions/checkout@v6`). That matches the stated policy, but if you want maximum supply-chain hardening, consider SHA-pinning first-party actions as well.

### Permissions

✅ Generally good:

- Publish jobs request `packages: write` only where needed.
- RubyGems.org publish job requests `id-token: write` (required).

Potential tightening:

- Some jobs include `actions: read` even when they don’t obviously need it (likely harmless, but could be minimized).

## File-by-file notes

### `.github/workflows/release-resolve.yml`

- Good transition to a unified detector with explicit exit codes.
- Good addition of `is_prerelease` output and centralized prerelease derivation.
- Consider ensuring `fd` version is deterministic (see issue #2).

### `.github/workflows/release-build-ruby-gem.yml`

- Matches the “artifact-first” architecture.
- Correctly uses `gem build ... --output` and defensively verifies that no extra `.gem` files are produced.
- Checks gem name/version via `gem specification`.

### `.github/workflows/official.yml`

- Ruby pipeline wiring is clean: build → publish (GPR + RubyGems) → attest → GitHub Release.
- PyPI idempotency logic is a strong improvement.
- Node publish idempotency is implemented for both registries.

### `.github/workflows/buddy.yml`

- Prerelease-only guard is correctly centralized and wired into all relevant jobs.
- Ruby buddy flow publishes only to GitHub Packages (as required).

## Suggested follow-ups (non-blocking)

- Add a small test matrix / smoke workflow that runs `actionlint` on PRs touching workflows (the repo already lists actionlint in mise tools).
- Consider consolidating the duplicated publish logic into reusable scripts to reduce future drift.
