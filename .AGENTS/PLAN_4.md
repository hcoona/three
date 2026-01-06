<!-- markdownlint-disable MD013 MD024 MD029 MD044 -->

# PLAN_4: Add Ruby Gem release support (RubyGems Trusted Publishing only) and unify publish semantics across PyPI / npm / RubyGems

This plan supersedes `.AGENTS/PLAN_3.md`.

It is regenerated from:

- `.AGENTS/PLAN_REVIEW_3.md` (tightening requirements and closing review gaps)
- `.AGENTS/CLARIFY_PLAN_0.md` … `.AGENTS/CLARIFY_PLAN_3.md` (maintainer-confirmed policies)

The intent is to extend the existing release pipeline:

> resolve → build artifact(s) → publish → attest → GitHub Release

…to support **Ruby gems**, while also **aligning and unifying** npm/Python publishing behavior with the same safety, idempotency, and “artifact-first” principles.

## Goal

1. Add Ruby gem detection, build, and publishing support to the root GitHub workflows (`official.yml`, `buddy.yml`) in a way that matches the existing architecture.
2. Make multi-registry publishing **idempotent on reruns** (official: npm+GPR, RubyGems.org+GitHub Packages) and bring PyPI closer to the same contract.
3. Enforce **buddy prerelease-only** consistently across Python/Node/Ruby, and keep the existing “buddy must not clobber an official GitHub Release” guard.
4. Standardize a minimal reproducibility baseline across build workflows (`SOURCE_DATE_EPOCH`, `TZ`, `LC_ALL`) to improve provenance stability and reduce rerun surprises.

## Hard requirements (non-negotiable)

### RubyGems.org publishing

- **Trusted Publishing only**.
- No long-lived RubyGems API keys.
- No alternate authentication fallback. If OIDC trusted publishing cannot be established, the workflow must fail.

Trusted Publishing context:

- RubyGems.org trusted publisher is configured to trust **this** repository and **this** workflow.
- The publish job uses `permissions: id-token: write`.
- The publish job uses a GitHub Actions environment (maintainer decision: `rubygems`).

References:

- https://guides.rubygems.org/trusted-publishing/
- https://guides.rubygems.org/trusted-publishing/adding-a-publisher
- https://guides.rubygems.org/trusted-publishing/releasing-gems
- https://github.com/rubygems/configure-rubygems-credentials

### Artifact-first release model

- Build once from the resolved `target` commit.
- Publish only from downloaded build artifacts (`${GITHUB_WORKSPACE}/out/*`).
- Publish steps must not rebuild.

### Buddy safety (all project kinds)

- Buddy releases are **prerelease-only**.
- Buddy must not create/modify an existing GitHub Release where `prerelease=false`.

### Idempotent reruns

For any registry publish step:

- If the version already exists, treat it as success **only if** the remote artifact digest matches the local artifact digest.
- If the version exists but does not match the digest, fail (do not attempt to overwrite).

This must apply to:

- Node official: npmjs.org + GitHub Packages (npm)
- Ruby official: RubyGems.org + GitHub Packages (RubyGems)
- Python official: PyPI (single registry, but rerun-safety still required)

## Current repository baseline (what exists today)

Root workflows:

- `.github/workflows/release-resolve.yml` (reusable resolver)
- `.github/workflows/release-build-python.yml` (reusable build)
- `.github/workflows/release-build-node-pack.yml` (reusable pack)
- `.github/workflows/official.yml` and `.github/workflows/buddy.yml`
- Provenance attestations via `actions/attest-build-provenance@v3`

Known gaps in the current baseline (must be fixed as part of this plan):

1. `release-resolve.yml` uses a “first match wins” detector flow (Python → Node), which is unsafe once Ruby is added.
2. Buddy flow currently lacks a **prerelease-only** guard.
3. Publish steps are not idempotent on reruns (multi-registry Node, PyPI, future Ruby).
4. Build workflows do not standardize `SOURCE_DATE_EPOCH` / locale / timezone.

## Confirmed policy decisions (source of truth)

(From `.AGENTS/CLARIFY_PLAN_*.md`)

### Publishing targets

- Buddy:
    - Node: GitHub Packages (npm) only
    - Ruby: GitHub Packages (RubyGems) only
    - Python: no registry publish (GitHub prerelease only)

- Official:
    - Python: PyPI (Trusted Publishing / OIDC)
    - Node: npmjs.org (Trusted Publishing / OIDC) + GitHub Packages (npm)
    - Ruby: RubyGems.org (Trusted Publishing / OIDC) + GitHub Packages (RubyGems)

### Ruby identity matching

For Ruby releases, the following must match:

- tag: `release/<project>/v<version>`
- gemspec filename: `<project>.gemspec`
- gem name inside the built `.gem`

### Ruby version policy

- Tags remain `release/<project>/v<version>`.
- Ruby accepts only a Ruby-style SemVer2-variant:
    - allowed: `MAJOR.MINOR.PATCH` and prerelease dot segments such as `1.2.3.beta.1`, `1.2.3.rc.0`
    - rejected: SemVer hyphen prerelease (`1.2.3-beta.1`), build metadata (`+...`), and RubyGems-valid but non-SemVer-core forms (`1.0`, dates, etc.)
- Therefore `version == rubygems_version` for Ruby.

### GitHub Packages RubyGems registry auth

- Use `${{ github.token }}` only.
- No PAT fallback secret.
- Require gemspec metadata `github_repo` to enable auto-linking:
    - `https://github.com/hcoona/three.git`
- Maintainers must ensure GitHub Packages “Manage Actions access” or inheritance is correctly configured.

References:

- https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-rubygems-registry
- https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility#ensuring-workflow-access-to-your-package

### Ruby toolchain

- Add `RUBY_VERSION` to entry workflows, initial value `3.3`.
- Release build checks when a `Gemfile` exists:
    - `bundle exec standardrb`
    - `bundle exec rspec`
- Release build installs OS-level dependencies (baseline apt list) required by known Ruby packages.

## Design changes

### 1) Harden project resolution and add Ruby detection

#### 1.1 Detector contract (all find\_\*\_project_path scripts)

Establish a uniform detector exit-code contract:

- exit `0`: unique match; print `package_dir` to stdout
- exit `2`: ambiguous matches; print details to stderr
- exit `3`: not found
- exit `1`: unexpected error

Update existing detectors:

- `eng/scripts/find_python_project_path.py`
- `eng/scripts/find_node_project_path.py`

Add:

- `eng/scripts/find_ruby_project_path.py` (unique match for `<project>.gemspec`)

#### 1.2 Cross-kind selection in `.github/workflows/release-resolve.yml`

Replace the current `if python … elif node … else fail` flow with:

1. Run Python/Node/Ruby detectors, capturing:
    - exit code
    - stdout candidate path
    - stderr diagnostics
2. If any detector returns exit `2`, fail immediately (within-kind ambiguity).
3. Collect kinds with exit `0`:
    - if 0 kinds match: fail “Unknown project” and print diagnostics
    - if >1 kinds match: fail “Ambiguous project kind” and list all matches
    - if exactly 1 kind matches: proceed

Update resolver outputs to include:

- `project_kind`: `python|node|ruby`
- `is_prerelease`: `true|false` (string output)
- `rubygems_version`: Ruby only (equals `version`; empty for other kinds)

#### 1.3 Version validation per kind

Add:

- `eng/scripts/validate_rubygems_version.py`

Ruby version grammar (explicit):

- core: `<int>.<int>.<int>`
- optional prerelease: `.<ident>.<int>(.<ident>.<int>)*`
    - `<ident>` starts with a letter; only `[A-Za-z0-9]` thereafter
- reject `-` and `+` entirely

Prerelease detection rule:

- `is_prerelease=true` iff the version contains anything beyond the `MAJOR.MINOR.PATCH` core.

### 2) Add a Ruby “readiness checklist” (pre-flight)

To prevent “gemspec exists but cannot build” regressions, require a minimal readiness check in the Ruby build workflow:

- `<project>.gemspec` exists
- `lib/` exists
- `Gem::Specification.load(<gemspec>)` succeeds

(Repository note: `src/public/lib/asciidoctor-latexmath` has been fixed to be buildable; the checklist remains required for future gems.)

### 3) Reproducibility baseline across build workflows

For all build jobs (Python build, Node pack, Ruby build):

- set `TZ=UTC`
- set `LC_ALL=C.UTF-8`
- set `SOURCE_DATE_EPOCH` to the target commit timestamp
    - `git show -s --format=%ct <target>`

Apply this to:

- `.github/workflows/release-build-python.yml` before `uv build`
- `.github/workflows/release-build-node-pack.yml` before `npm pack`
- new Ruby build workflow before `gem build`

### 4) Ruby build: new reusable workflow

Add: `.github/workflows/release-build-ruby-gem.yml`.

Inputs:

- `target`, `project`, `package_dir`, `version`, `ruby_version`, `artifact_name`

Steps:

1. Checkout `ref: target`.
2. Install baseline apt dependencies:
    - `texlive-latex-base`
    - `texlive-latex-recommended`
    - `texlive-fonts-recommended`
    - `dvisvgm`
    - `pdf2svg`
    - `poppler-utils`
    - `imagemagick`
    - `ghostscript`
3. Setup Ruby (`ruby/setup-ruby@v1`, pinned).
4. All Ruby commands MUST run in `working-directory: ${{ inputs.package_dir }}`.
5. If `Gemfile` exists:
    - enable bundler cache
    - run `bundle exec standardrb`
    - run `bundle exec rspec`
6. Build exactly one gem artifact:
    - create a clean `${GITHUB_WORKSPACE}/out`
    - run: `gem build <project>.gemspec --output ${GITHUB_WORKSPACE}/out/<project>.gem`
    - refuse multiple outputs
7. Verify identity:
    - `gem specification out/<project>.gem name == <project>`
    - `gem specification out/<project>.gem version == <version>`
8. Upload `out/*`.

### 5) Ruby publish: GitHub Packages (buddy + official)

Implement a Ruby publish job that:

- downloads the Ruby artifact (`out/<project>.gem`)
- publishes to GitHub Packages RubyGems registry:
    - host: `https://rubygems.pkg.github.com/${{ github.repository_owner }}`
    - credentials: write `~/.gem/credentials` with `:github: Bearer ${{ github.token }}` and `chmod 0600`
    - publish command: `gem push --key github --host … out/<project>.gem`

Permissions:

- `packages: write`
- `contents: read`

Idempotent rerun behavior:

- if `gem push` fails due to version already existing:
    - authenticate for fetching as well
    - `gem fetch <project> -v <version> --source https://rubygems.pkg.github.com/${OWNER}`
    - compute SHA256 of fetched `.gem`
    - compare to SHA256 of local `out/<project>.gem`
    - success only if equal; otherwise fail

Note on access control:

- GitHub Packages may reject `${{ github.token }}` unless the package is linked to the repo and/or “Manage Actions access” is configured.
- This is expected and must be documented as a maintainer prerequisite (no fallback token).

### 6) Ruby publish: RubyGems.org (official only; Trusted Publishing only)

Implement a RubyGems.org publish job that:

- uses a GitHub Actions environment (maintainer decision: `rubygems`)
- requests OIDC via `permissions: id-token: write`
- uses `rubygems/configure-rubygems-credentials` (pinned) with Trusted Publishing defaults (no api-token, no role-to-assume)
- publishes the downloaded artifact only: `gem push out/<project>.gem`

Permissions:

- `id-token: write`
- `contents: read`
- `actions: read`

Idempotent rerun behavior (RubyGems.org API):

- Query version list:
    - `GET https://rubygems.org/api/v1/versions/<project>.json`
- If `<version>` exists, compare its `sha` field to local SHA256.
    - if equal: skip publish
    - else: fail

Eventual consistency:

- Use `rubygems-await` (pinned) to wait for propagation when we need to verify availability.

References:

- https://guides.rubygems.org/rubygems-org-api/ (version objects include `sha`)
- https://github.com/segiddins/rubygems-await

### 7) Unify Node official publish semantics (npmjs + GitHub Packages)

Update `.github/workflows/official.yml` Node publish job to be idempotent:

- Canonical remote digest: `dist.integrity`.
- Canonical local digest: compute SRI for the local tarball (`sha512-…`) and compare to `dist.integrity`.

For each registry:

- If `<name>@<version>` exists:
    - if integrity matches: skip publish to that registry
    - else: fail

Also reduce publish job token exposure:

- Move “Verify package is not private” into the pack workflow (source is already present there).
- Avoid checking out the repository in the publish job unless strictly necessary.

### 8) Unify Python official publish semantics (PyPI)

Update `.github/workflows/official.yml` Python publish job to be rerun-safe:

- Preflight query:
    - `GET https://pypi.org/pypi/<project>/json`
- If version exists, compare PyPI-provided file digests against the local artifacts (`out/*`).
    - if all match: skip publish
    - else: fail

Keep the publish job minimal (download artifacts + publish) to reduce OIDC token exposure, consistent with Trusted Publishing security guidance.

### 9) Buddy prerelease-only guard (all kinds)

Add `is_prerelease` output to the resolver and enforce it in `.github/workflows/buddy.yml`:

- Immediately after `resolve`, fail if `is_prerelease != true`.

This should apply consistently to Python/Node/Ruby.

### 10) Wire Ruby into entry workflows

Update `.github/workflows/official.yml` and `.github/workflows/buddy.yml`:

- Add `RUBY_VERSION: '3.3'` at workflow `env:`
- Export `ruby_version` in the `versions` job
- Add conditional Ruby jobs:
    - `build-ruby` (reusable build)
    - `publish-ruby-gpr` (GitHub Packages RubyGems)
    - `publish-ruby-rubygems` (RubyGems.org; official only)
    - `attest-ruby` (official)
    - `release-ruby` gating consistent with Python/Node patterns

## Maintainer setup checklist (required)

### RubyGems.org Trusted Publisher

For each gem published to RubyGems.org:

- Configure a trusted publisher:
    - Owner: `hcoona`
    - Repository: `three`
    - Workflow filename: `official.yml`
    - Environment: `rubygems`

Note: RubyGems docs often recommend naming the environment `release`, but this repository’s policy uses `rubygems`. The RubyGems trusted publisher configuration must match the workflow exactly.

### GitHub Packages RubyGems registry

For each gem:

- Ensure gemspec metadata includes:
    - `github_repo = https://github.com/hcoona/three.git`
- Ensure “Manage Actions access” / inheritance allows workflows from `hcoona/three` to upload new versions.

## Acceptance criteria

### Resolver

- Detects Ruby gems via `<project>.gemspec`.
- Fails on:
    - within-kind ambiguity (exit 2)
    - cross-kind ambiguity (multiple kinds match)
    - unknown project
- Validates versions per kind, including Ruby grammar.
- Emits `is_prerelease` and `rubygems_version` outputs.

### Ruby

- Build produces exactly one deterministic artifact: `out/<project>.gem`.
- Buddy:
    - fails on stable versions
    - publishes only to GitHub Packages RubyGems
    - creates/updates a prerelease GitHub Release
    - never clobbers an official release
- Official:
    - publishes to GitHub Packages RubyGems using `${{ github.token }}` only
    - publishes to RubyGems.org using Trusted Publishing only
    - multi-registry publishing is idempotent on reruns via digest verification
    - attest + GitHub Release are gated on successful publish (or verified already-published)

### Alignment

- Python/Node/Ruby builds set `SOURCE_DATE_EPOCH`, `TZ`, `LC_ALL`.
- Node official publish uses `dist.integrity` as the canonical idempotency check.
- Python official publish is rerun-safe via PyPI JSON API digest verification.

## Implementation sequence (incremental, testable)

1. Update Python/Node detectors to follow the exit-code contract (0/2/3/1).
2. Add Ruby detector + Ruby version validator.
3. Harden `.github/workflows/release-resolve.yml`:
    - run all detectors
    - fail on ambiguity
    - emit `is_prerelease` and `rubygems_version`.
4. Add buddy prerelease-only guard in `.github/workflows/buddy.yml`.
5. Add reproducibility baseline to existing build workflows (Python + Node).
6. Add `.github/workflows/release-build-ruby-gem.yml` and wire Ruby build into buddy first (publish to GitHub Packages only).
7. Add Ruby official publish jobs:
    - RubyGems.org (Trusted Publishing)
    - GitHub Packages RubyGems
    - attestation + release gating.
8. Add Node official idempotent publishing (npmjs + GPR) using `dist.integrity`.
9. Add Python official rerun-safe publishing (PyPI) using digest verification.

## Risks and mitigations

- GitHub Packages RubyGems auth inconsistencies (docs sometimes emphasize PATs):
    - Mitigation: enforce package linking (`github_repo`) and “Manage Actions access” as maintainer prerequisites; no fallback secret.

- RubyGems.org eventual consistency:
    - Mitigation: use `rubygems-await` for post-publish availability checks.

- Token/OIDC exposure in publish jobs:
    - Mitigation: keep publish jobs minimal (download artifacts + publish + verify) and use per-job permissions.
