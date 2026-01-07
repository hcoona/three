<!-- markdownlint-disable MD013 MD024 -->

# CODE_REVIEW_0: Staged changes review (PLAN_6)

Date: 2026-01-07

Scope: **Git staged** changes only.

Reviewed files:

- `.github/workflows/buddy.yml`
- `.github/workflows/official.yml`
- `.github/workflows/release-build-node-pack.yml`
- `.github/workflows/release-build-python.yml`
- `.github/workflows/release-build-ruby-gem.yml` (new)
- `.github/workflows/release-resolve.yml`
- `eng/scripts/find_project_path.py` (new)
- `eng/scripts/validate_rubygems_version.py` (new)

Reference plan: `.AGENTS/PLAN_6.md` and clarifications `.AGENTS/CLARIFY_PLAN_6_*.md`.

## Overall assessment

The staged changes substantially advance PLAN_6 goals:

- Ruby projects are now detectable/buildable/publishable in the root release workflows.
- RubyGems.org publishing is implemented via **Trusted Publishing (OIDC)** with **no API token inputs**, aligning with the “no fallback” requirement.
- Publishing steps across PyPI, npm (npmjs + GPR), and Ruby (RubyGems.org + GPR) implement **idempotent rerun safety** using remote-vs-local digest checks.
- Buddy prerelease-only gating is enforced via a dedicated guard using the resolver’s `is_prerelease` output.
- A baseline reproducibility envelope (`TZ`, `LC_ALL`, `SOURCE_DATE_EPOCH`) is applied to build-producing steps.

However, there are **significant correctness/maintainability risks** and at least one likely **CI blocker** related to the repository’s strict Ruff configuration for scripts.

## ✅ Requirements check (PLAN_6 hard requirements)

### RubyGems.org publishing uses Trusted Publishing only

- `official.yml` uses `rubygems/configure-rubygems-credentials` with `trusted-publisher: true`.
- No API key secret wiring, no `api-token`, no `role-to-assume`.

This satisfies the plan requirement _as specified_ (“mechanical enforcement” is not required; absence of fallback wiring is sufficient).

### GitHub Packages RubyGems publishes use `github.token` only

- Both `official.yml` and `buddy.yml` publish to GitHub Packages RubyGems using `${{ github.token }}`.
- No PAT fallback introduced.

### Buddy safety (all kinds)

- `buddy.yml` now includes `guard-prerelease-only` and wires it into Python/Node/Ruby job dependencies.
- Existing “buddy must not clobber official release” guard remains.

### Idempotent reruns for all publishes

- PyPI: file-level sha256 validation + `skip-existing: true`.
- npmjs/GPR: integrity (`dist.integrity`) validation against locally computed SRI.
- RubyGems.org: RubyGems API v2 version endpoint `sha` compared with local sha256.
- GitHub Packages RubyGems: `gem fetch` + sha256 digest compare, with bounded retry after “already exists” races.

### Third-party action pinning

New third-party actions introduced by the plan appear pinned to full SHAs (e.g. `rubygems/configure-rubygems-credentials`, `jdx/mise-action`, `ruby/setup-ruby`, `dcarbone/install-jq-action`).

## ❗ Blockers / high-severity issues

### 1) New Python scripts are very likely non-compliant with repo-wide Ruff rules

Status: RESOLVED (manual).

The repository’s `pyproject.toml` selects a wide ruleset (including `S`/bandit and `BLE`), with **no broad relaxations** for `**/scripts/**/*.py` besides allowing `print` (`T201`).

`eng/scripts/find_project_path.py` likely violates multiple rules:

- **Unused import**: `import os` (Pyflakes `F401`).
- **Broad exception catches**:
    - `except Exception:` is likely flagged by `BLE001` (“Do not catch blind exception: Exception”).
- **Subprocess usage**:
    - `subprocess.run(...)` is likely flagged by Bandit rules (commonly `S603`), even with `shell=False`.
- **Line length**: at least one line appears > 80 chars (E501).

`eng/scripts/validate_rubygems_version.py` also has likely **E501 line length** issues (e.g. the `has_letter = any(any(...` line).

If CI runs Ruff over these scripts (which is consistent with current configuration), this is a **hard blocker**.

**Recommendation**:

- Remove unused imports.
- Replace broad `except Exception` with specific exception types.
- For `subprocess.run`, add an explicit, justified suppression (e.g. `# noqa: S603`) and a short comment explaining why it is safe (no shell, args are controlled/escaped). Alternatively, consider a non-subprocess implementation _only if_ it still meets PLAN_6’s “must use fd” requirement.
- Run formatter/linter (`ruff format` + `ruff check`) and adjust to satisfy 80-char line length.

### 2) Potential credential leakage in logs (GitHub Packages RubyGems fetch)

Status: ACCEPTED (out of scope).

In both `official.yml` and `buddy.yml`, GitHub Packages RubyGems idempotency checks use an authenticated source URL of the form:

- `--source "https://${ACTOR}:${TOKEN}@rubygems.pkg.github.com/${OWNER}/"`

On certain failures, the workflow prints the captured stderr (`cat "${fetch_err}"`). If stderr includes the full URL, it may contain the token.

GitHub often masks `${{ github.token }}`, but relying on masking is brittle and still not ideal.

**Recommendation**:

- Avoid emitting raw stderr when it may contain credentials.
- If diagnostics must be printed, scrub secrets first (e.g. replace the token string with `***`) or only print curated, non-sensitive lines.
- Consider using a credential mechanism that does not embed the token in the URL (if feasible for `gem fetch`).

### 3) Ruby idempotency logic assumes pure-Ruby platform and canonical `.gem` filenames

Status: ACCEPTED (out of scope; current limitation acknowledged).

Both GitHub Packages RubyGems and RubyGems.org idempotency checks assume:

- The published artifact is platform `ruby` (RubyGems.org query uses `platform=ruby`).
- The downloaded filename is exactly `${PROJECT}-${VERSION}.gem`.

This breaks for platform-specific gems (e.g. `name-version-x86_64-linux.gem`), and may cause:

- False “not found” during `gem fetch`.
- Repeated push attempts and eventual failure of digest verification.

**Recommendation**:

- Either document the restriction explicitly (only pure-Ruby gems supported), or update logic to handle platform suffixes:
    - Determine the platform from the local gemspec (e.g. `gem specification`),
    - Query RubyGems.org without forcing `platform=ruby`, or match the actual platform,
    - Accept/locate the fetched filename dynamically rather than hardcoding `${PROJECT}-${VERSION}.gem`.

## Major issues / risks

### `release-resolve.yml`: dependency on `mise` activation semantics

Status: ACCEPTED (out of scope).

The resolver installs `fd` via `mise install fd` and then immediately expects `fd` to be on `PATH`.

This is probably correct with `jdx/mise-action`, but it is a fragile integration point.

**Recommendation**:

- Keep the current `command -v fd` check (good), and consider printing `mise --version` and `mise which fd` for clearer diagnostics when it fails.

### Workflow maintainability: duplicated idempotent publish logic

Status: ACCEPTED (out of scope).

The idempotency logic for:

- npm GPR publish (buddy and official)
- GitHub Packages RubyGems publish (buddy and official)

is largely duplicated.

**Recommendation**:

- Consider extracting common logic to `eng/scripts/...` helper scripts (shell or Python) so behavior stays in sync.

### Ruby build workflow installs large system dependencies for all Ruby gems

Status: ACCEPTED (out of scope).

`release-build-ruby-gem.yml` installs a fairly heavy TeX/ImageMagick toolchain.

This is acceptable if the repo’s Ruby gems require it (and PLAN_6 states it is confirmed), but it has performance implications.

**Recommendation**:

- If future Ruby gems don’t need these dependencies, consider making this conditional (e.g. optional input, or install only when a marker file is present).

## Minor notes / polish

- `gem install rubygems-await -v 0.5.4` should likely include `--no-document` to reduce noise and speed up installs.
- The RubyGems.org API validation step handles 429/5xx well; consider adding a short retry with jitter for transient 5xx (optional).
- `validate_rubygems_version.py` could be simplified by using a regex search for `[A-Za-z]` across the suffix, improving readability and line length.

## File-by-file quick notes

### `.github/workflows/official.yml`

- ✅ Correctly adds Ruby build + publish (GPR + RubyGems.org) + attestation + release wiring.
- ✅ RubyGems.org publishing uses Trusted Publishing action, pinned to SHA, with `id-token: write`.
- ✅ Idempotent checks for PyPI and npm are aligned with PLAN_6.
- ⚠️ Ruby publish to GitHub Packages prints stderr on auth failures; see leakage note above.

### `.github/workflows/buddy.yml`

- ✅ Adds prerelease-only guard.
- ✅ Adds Ruby build + GPR publish and creates prerelease GitHub Releases.
- ✅ Node GPR publish now uses digest gating.

### `.github/workflows/release-resolve.yml`

- ✅ Unifies project resolution and adds `is_prerelease` output.
- ✅ Uses `mise` to provide `fd` rather than ad-hoc installs.
- ⚠️ Consider validating parsed JSON fields are non-empty (`jq -e`) for defense-in-depth.

### `.github/workflows/release-build-ruby-gem.yml` (new)

- ✅ Enforces single `.gem` output and validates name/version.
- ✅ Applies reproducibility env vars.
- ⚠️ Bundler checks assume `standardrb` and `rspec` exist when a Gemfile exists; that is likely intended but can be surprising.

### `eng/scripts/find_project_path.py` (new)

- ✅ Implements the exit-code contract and cross-kind ambiguity.
- ❗ Likely fails Ruff checks (see blockers).

### `eng/scripts/validate_rubygems_version.py` (new)

- ✅ Encodes the repository’s Ruby version policy.
- ❗ Likely fails Ruff E501 due to long lines.

## Suggested next steps

1. Make the two new Python scripts pass `ruff check` and `ruff format` under the repository’s current strict configuration.
2. Out of scope (accepted): Reduce risk of logging secrets in the RubyGems GPR idempotency checks.
3. Out of scope (accepted): Support platform-specific gems / adjust filename-platform handling.
