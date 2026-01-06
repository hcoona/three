<!-- markdownlint-disable MD013 MD024 MD029 MD044 -->

# PLAN_5: Ruby Gem release support (RubyGems Trusted Publishing only) + aligned, idempotent publish semantics for npm/PyPI/RubyGems

This plan **supersedes** `.AGENTS/PLAN_4.md`.

It is regenerated from:

- `.AGENTS/PLAN_REVIEW_4.md` (review findings and required corrections)
- `.AGENTS/CLARIFY_PLAN_0.md` … `.AGENTS/CLARIFY_PLAN_4.md` (maintainer-confirmed policies)

## Goal

1. Add **Ruby gem** detection/build/publish support to the root release workflows (`official.yml`, `buddy.yml`) with the same architecture used for Python and Node.
2. Align the three ecosystems (PyPI / npm / RubyGems) under one consistent contract:
    - **artifact-first** (build once; publish from `out/*` only)
    - **least-privilege** (minimal permissions per job)
    - **idempotent reruns** (safe to re-run after partial publishes)
3. Apply the repo-wide buddy release safety rules consistently across Python/Node/Ruby:
    - buddy is **prerelease-only**
    - buddy must **not clobber** an existing official GitHub Release (`prerelease=false`)

## Non-goals

- Reworking the entire monorepo layout (not part of this plan).
- Implementing a generic “release framework” beyond what is needed to support Ruby gems and align publish semantics.
- Supporting RubyGems.org publishing with long-lived API keys or any fallback mechanism.

## Hard requirements (non-negotiable)

### RubyGems.org publishing must be Trusted Publishing only (no fallback)

- RubyGems.org publishing must use **Trusted Publishing (OIDC)** only.
- No RubyGems API key secrets.
- No alternative authentication fallback.
- If OIDC trusted publishing cannot be established at runtime, the workflow must fail.

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

### Idempotent reruns apply to **all** publishes (official + buddy)

For any registry publish step:

- If the version already exists, treat it as success **only if** the remote artifact digest matches the local artifact digest.
- If the version exists but the digest does not match, fail (never overwrite).

This applies to:

- Node official: npmjs.org + GitHub Packages (npm)
- Node buddy: GitHub Packages (npm)
- Ruby official: RubyGems.org + GitHub Packages (RubyGems)
- Ruby buddy: GitHub Packages (RubyGems)
- Python official: PyPI (single registry; still must be rerun-safe)

## Confirmed policies (source of truth)

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

Ruby releases accept only a Ruby-style SemVer2-variant:

- Allowed: `MAJOR.MINOR.PATCH` and optional prerelease dot segments, e.g. `1.2.3`, `1.2.3.beta.1`, `1.2.3.rc.0`.
- Rejected: SemVer hyphen prerelease (`1.2.3-beta.1`), build metadata (`+...`), PEP 440 versions, and non-core RubyGems versions (`1.0`, dates, etc.).

### GitHub Packages RubyGems registry auth

- Use `${{ github.token }}` with `permissions: packages: write`.
- No PAT fallback secret.
- Maintain gemspec metadata `github_repo = https://github.com/hcoona/three.git` for auto-linking.
- Maintainers must ensure GitHub Packages “Manage Actions access” / permission inheritance allows publishing from this repository.

### Ruby toolchain

- Add `RUBY_VERSION` to entry workflows; initial value `3.3`.
- Build-time checks when a `Gemfile` exists:
    - `bundle exec standardrb`
    - `bundle exec rspec`
- Release build installs baseline OS dependencies (apt) for known Ruby packages.

## What changes vs PLAN_4

PLAN_5 incorporates the review and clarifications that PLAN_4 missed or underspecified:

1. **GitHub Packages RubyGems idempotency is now specified and confirmed**:
    - Download existing gems via `gem fetch` using an authenticated source URL (CLARIFY_PLAN_4).
2. **Idempotency applies to buddy publishes too** (Node buddy GPR and Ruby buddy GPR must be rerun-safe) (CLARIFY_PLAN_4).
3. **Ruby artifact filename is versioned**:
    - `out/<project>-<version>.gem` (CLARIFY_PLAN_4).
4. RubyGems Trusted Publishing configuration is made mechanically safer:
    - explicitly set `trusted-publisher: true` (avoid implicit defaults drifting).

## Design changes

### 1) Harden project resolution and add Ruby detection

#### 1.1 Detector contract (all `find_*_project_path` scripts)

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

Replace the current “Python → else Node → else fail” logic with:

1. Run Python/Node/Ruby detectors, capturing exit code/stdout/stderr.
2. If any detector returns exit `2`, fail (within-kind ambiguity).
3. Collect kinds with exit `0`:
    - if 0 kinds match: fail “Unknown project” and print diagnostics
    - if >1 kinds match: fail “Ambiguous project kind” and list all matches
    - if exactly 1 kind matches: proceed

Update resolver outputs to include:

- `project_kind`: `python|node|ruby`
- `is_prerelease`: `true|false`

#### 1.3 Version validation per kind

Add:

- `eng/scripts/validate_rubygems_version.py`

Rules:

- Ruby version grammar: `MAJOR.MINOR.PATCH` plus optional prerelease dot segments (no `-`, no `+`).
- `is_prerelease=true` iff version has any segment beyond `MAJOR.MINOR.PATCH`.

For Python:

- Continue using the existing PEP 440 subset validator.
- `is_prerelease=true` for PEP 440 pre-releases and dev releases.

For Node:

- Continue using strict SemVer2 validator.
- `is_prerelease=true` iff version contains a SemVer prerelease part.

### 2) Enforce buddy prerelease-only early

Update `.github/workflows/buddy.yml`:

- After `resolve`, add a guard step/job that fails if `needs.resolve.outputs.is_prerelease != 'true'`.

This prevents buddy runs from publishing stable versions (which can block later official publishes).

### 3) Reproducibility baseline across build workflows

For all build jobs (Python build, Node pack, Ruby build):

- `TZ=UTC`
- `LC_ALL=C.UTF-8`
- `SOURCE_DATE_EPOCH` set to the `target` commit timestamp (`git show -s --format=%ct <target>`)

Apply to:

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
4. Run all Ruby commands in `working-directory: ${{ inputs.package_dir }}`.
5. If `Gemfile` exists:
    - enable bundler cache
    - run `bundle exec standardrb`
    - run `bundle exec rspec`
6. Build exactly one gem artifact:
    - create a clean `${GITHUB_WORKSPACE}/out`
    - `gem build <project>.gemspec --output ${GITHUB_WORKSPACE}/out/<project>-<version>.gem`
7. Verify identity:
    - `gem specification out/<project>-<version>.gem name == <project>`
    - `gem specification out/<project>-<version>.gem version == <version>`
8. Upload `out/*`.

Note: RubyGems’ official `rubygems/release-gem@v1` action is designed around `bundle exec rake release` and typically rebuilds. We will not use it for publishing because it conflicts with the repo’s artifact-first model, but it is a reference for the OIDC/permissions shape.

References:

- https://guides.rubygems.org/trusted-publishing/releasing-gems
- https://github.com/rubygems/release-gem

### 5) Ruby publish: GitHub Packages (buddy + official)

Implement Ruby publish to GitHub Packages RubyGems registry:

- Download `out/<project>-<version>.gem`.
- Publish to GitHub Packages RubyGems registry using an explicit host + key:
    - `gem push --key github --host https://rubygems.pkg.github.com/<owner> out/<project>-<version>.gem`

Rationale: without `--host`, `gem push` targets RubyGems.org by default; without `--key github`, the CLI may not select the `:github:` credentials entry.

Credentials:

- Publish auth (push): `~/.gem/credentials` with `:github: Bearer ${{ github.token }}` (chmod `0600`).
- Fetch auth (for idempotency verification): use `gem fetch` with an authenticated source URL (CLARIFY_PLAN_4).

Permissions:

- `packages: write`
- `contents: read`

Idempotent rerun behavior:

- Deterministic preflight (preferred):
    1. Attempt to fetch the remote gem first:
    - `gem fetch <project> -v <version> --source https://<user>:<token>@rubygems.pkg.github.com/<owner>/`
    2. If fetch succeeds:
    - Compare SHA-256 of the fetched `.gem` to local `out/<project>-<version>.gem`.
    - If equal: treat as success (already published).
    - If different: fail (never overwrite).
    3. If fetch indicates “not found”, then perform the publish:
    - `gem push --key github --host https://rubygems.pkg.github.com/<owner> out/<project>-<version>.gem`
    4. If the publish fails with a clear “already exists / repush not allowed” error (possible due to index propagation delay), then:
    - retry `gem fetch` with bounded backoff (e.g. a few attempts with short sleeps),
    - compare SHA-256 of the fetched `.gem` to local `out/<project>-<version>.gem`,
    - treat as success only if equal; otherwise fail.
    5. Any other fetch/push failures (401/403/network/etc.) must fail with clear diagnostics.

Security note:

- The workflow must avoid printing the token. Prefer passing the authenticated URL via environment variables and relying on GitHub masking.

### 6) Ruby publish: RubyGems.org (official only; Trusted Publishing only)

Implement RubyGems.org publish in official releases only:

- Use job `environment: rubygems`.
- Request OIDC: `permissions: id-token: write`.
- Configure credentials with `rubygems/configure-rubygems-credentials` (pinned) and **explicitly set** `trusted-publisher: true`.
    - Do not set `api-token`.
    - Do not set `role-to-assume`.
- Publish from the downloaded artifact only: `gem push out/<project>-<version>.gem`.

Permissions:

- `id-token: write`
- `contents: read`
- `actions: read`

Idempotent rerun behavior (RubyGems.org API):

- Query versions:
    - `GET https://rubygems.org/api/v1/versions/<project>.json`
- If `<version>` exists:
- If `<version>` exists:
    - select the matching version object (exact `number == <version>` and `platform == "ruby"` unless the package is explicitly known to publish platform gems)
    - compare its `sha` field to local SHA-256
    - if equal: skip publish
    - else: fail

Eventual consistency:

- Use `rubygems-await` (pinned) when post-publish verification requires waiting for propagation.

### 7) Align Node publishing semantics (npmjs + GitHub Packages)

Bring Node official and buddy GitHub Packages publishing in line with the idempotent rerun contract:

- Canonical remote digest: `dist.integrity` from `npm view`.
- Canonical local digest: compute SRI for the local tarball (sha512 SRI).

Rules per registry:

- If `<name>@<version>` exists:
    - if integrity matches: skip publish
    - else: fail

Tarball mapping (must be explicit):

- For GitHub Packages (GPR), the local artifact is `out/gpr.tgz`.
- For npmjs.org, the local artifact is `out/npmjs.tgz`.

Digest checks must compare each registry’s `dist.integrity` against the SRI computed from the corresponding local tarball (never cross-compare).

Operational tightening:

- Avoid repository checkout in publish jobs unless strictly required.
- Move “private package” refusal into the pack workflow (where the source is already checked out).

### 8) Align Python publishing semantics (PyPI)

Make Python official publish rerun-safe:

- Preflight query:
    - `GET https://pypi.org/pypi/<project>/json`
- Build a remote file map for the target version:
    - from the PyPI JSON payload, map `filename -> sha256` for all files where `release == <version>`.
- Enforce file-level idempotency (handles partial publishes safely):
    - For each local artifact file in `out/*`:
        - If the same `filename` already exists on PyPI:
            - If remote SHA-256 matches the local SHA-256: treat that file as already published.
            - If remote SHA-256 does not match: fail (never overwrite).
        - If the `filename` does not exist on PyPI yet: publish it.
- Publish strategy must not rebuild:
    - Either publish only the missing files (preferred), or use a “skip existing” upload mode only after digest-verifying any existing filenames.
- Postcondition:
    - After the publish step completes, verify that every local file in `out/*` exists on PyPI with a matching SHA-256.

Keep the publish job minimal (download artifacts + publish + verify) and least-privilege, consistent with OIDC Trusted Publishing guidance.

### 9) Wire Ruby into entry workflows

Update `.github/workflows/official.yml` and `.github/workflows/buddy.yml`:

- Add `RUBY_VERSION: '3.3'` at workflow `env:`.
- Export `ruby_version` from the `versions` job.
- Add conditional Ruby jobs:
    - build: `release-build-ruby-gem.yml`
    - buddy publish: GitHub Packages RubyGems only
    - official publish: GitHub Packages RubyGems + RubyGems.org
    - attest (official)
    - GitHub release creation gated on publish success (or verified already-published)

## Maintainer setup checklist (required)

### RubyGems.org Trusted Publisher configuration

For each gem published to RubyGems.org, configure a trusted publisher:

- Owner: `hcoona`
- Repository: `three`
- Workflow filename: `official.yml`
- Environment: `rubygems`

Note: RubyGems documentation suggests an environment name like `release`, but this repository’s policy uses `rubygems`. The RubyGems trusted publisher configuration must match exactly.

### GitHub Packages RubyGems registry prerequisites

For each gem:

- Ensure gemspec metadata includes:
    - `github_repo = https://github.com/hcoona/three.git`
- Ensure GitHub Packages “Manage Actions access” (or inheritance) allows workflows from `hcoona/three` to upload.

## Acceptance criteria

### Resolver

- Detects Ruby gems via `<project>.gemspec`.
- Fails on:
    - within-kind ambiguity (detector exit 2)
    - cross-kind ambiguity (multiple kinds match)
    - unknown project
- Validates version per kind (Ruby grammar included).
- Emits `is_prerelease` for buddy enforcement.

### Ruby

- Build produces exactly one `.gem` artifact: `out/<project>-<version>.gem`.
- Buddy:
    - fails on stable versions
    - publishes only to GitHub Packages RubyGems
    - creates/updates a prerelease GitHub Release
    - never clobbers an official release
    - reruns are idempotent via digest verification
- Official:
    - publishes to GitHub Packages RubyGems using `${{ github.token }}` only
    - publishes to RubyGems.org using Trusted Publishing only
    - reruns are idempotent via digest verification on both registries
    - attestation + GitHub Release are gated on successful publish (or verified already-published)

### Alignment

- Python/Node/Ruby builds set `SOURCE_DATE_EPOCH`, `TZ`, `LC_ALL`.
- Node publishes (official + buddy) are idempotent via `dist.integrity` checks.
- Python official publish is rerun-safe via PyPI digest verification.

## Implementation sequence (incremental, testable)

1. Update Python/Node detectors to follow the exit-code contract (0/2/3/1).
2. Add Ruby detector + Ruby version validator.
3. Harden `.github/workflows/release-resolve.yml`:
    - run all detectors
    - fail on ambiguity
    - emit `is_prerelease`.
4. Add buddy prerelease-only guard in `.github/workflows/buddy.yml`.
5. Add reproducibility baseline to existing build workflows (Python + Node).
6. Add `.github/workflows/release-build-ruby-gem.yml`.
7. Add Ruby buddy publish to GitHub Packages (with idempotency).
8. Add Ruby official publish jobs:
    - RubyGems.org (Trusted Publishing only)
    - GitHub Packages RubyGems (idempotent)
    - attestation + release gating.
9. Add Node buddy and Node official idempotent publish logic.
10. Add Python official rerun-safe publish logic.

## Risks and mitigations

- **GitHub Packages RubyGems authentication differences vs docs** (docs often emphasize PATs):
    - Mitigation: enforce package linking (`github_repo`) and “Manage Actions access” as prerequisites; no fallback secret.

- **RubyGems.org eventual consistency**:
    - Mitigation: use `rubygems-await` for post-publish checks when needed.

- **Token/OIDC exposure in publish jobs**:
    - Mitigation: keep publish jobs minimal (download artifacts + publish + verify) and use per-job permissions.
