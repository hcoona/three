<!-- markdownlint-disable MD013 MD024 MD029 MD044 -->

# PLAN_3: Add RubyGems (Trusted Publishing only) + align publish semantics across PyPI / npm / RubyGems

This plan supersedes `.AGENTS/PLAN_2.md` by addressing the high-risk gaps identified in `.AGENTS/PLAN_REVIEW_2.md` and incorporating maintainer confirmations from:

- `.AGENTS/CLARIFY_PLAN_0.md`
- `.AGENTS/CLARIFY_PLAN_1.md`
- `.AGENTS/CLARIFY_PLAN_2.md`

## Goal

Extend the repo’s existing release pipeline:

> resolve → build artifact(s) → publish → attest → GitHub Release

…to support **Ruby gems** and to **align/standardize** publishing behavior and safety rules across:

- Python → PyPI (Trusted Publishing / OIDC)
- Node → npmjs.org (Trusted Publishing / OIDC) + GitHub Packages (npm)
- Ruby → RubyGems.org (Trusted Publishing / OIDC) + GitHub Packages (RubyGems)

Key principles:

1. **Trusted Publishing only** (no long-lived secrets) where applicable.
2. **Artifact-first**: build once from `target`, publish from downloaded `out/*`.
3. **Deterministic artifacts**: avoid globs that can accidentally publish multiple files.
4. **Buddy safety**: buddy must be prerelease-only and must not clobber an official release.
5. **Idempotent reruns for multi-registry publishing**: if a version already exists, treat as success only when the remote artifact matches the local artifact digest.

## Non-goals

- Do not introduce RubyGems API keys, PATs, or any non-OIDC auth for RubyGems.org.
- Do not restructure the monorepo layout.
- Do not change the tag format policy: tags remain `release/<project>/v<version>`.
- Do not adopt `rubygems/release-gem@v1` for publishing, because it is optimized for Bundler/Rake release workflows and may rebuild.

## Current baseline (what must be preserved)

The root workflows already implement:

- `.github/workflows/release-resolve.yml` (reusable resolver)
- `.github/workflows/release-build-python.yml` (reusable build)
- `.github/workflows/release-build-node-pack.yml` (reusable pack)
- `.github/workflows/official.yml` and `.github/workflows/buddy.yml`
- `.github/workflows/release-create-github-release.yml` and attestations via `actions/attest-build-provenance@v3`

The expected artifact layout is a **flat** `out/*` uploaded as a workflow artifact and later attached to the GitHub Release.

## External references (authoritative)

- GitHub Packages RubyGems registry
    - https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-rubygems-registry
- RubyGems Trusted Publishing
    - https://guides.rubygems.org/trusted-publishing/
    - https://guides.rubygems.org/trusted-publishing/adding-a-publisher
    - https://guides.rubygems.org/trusted-publishing/releasing-gems
- RubyGems actions
    - https://github.com/rubygems/configure-rubygems-credentials
        - `audience` default: `rubygems.org`
        - defaults to Trusted Publishing when no `api-token` or `role-to-assume` is provided
    - https://github.com/rubygems/release-gem/blob/v1/action.yml (reference only)
- RubyGems.org API (used for idempotency verification)
    - https://guides.rubygems.org/rubygems-org-api/

## Confirmed policy decisions (source of truth)

### Publishing targets

- Buddy:
    - Node: publish to GitHub Packages (npm) only
    - Ruby: publish to GitHub Packages (RubyGems) only
    - Python: no registry publish today (only GitHub prerelease)

- Official:
    - Python: publish to PyPI (Trusted Publishing / OIDC)
    - Node: publish to npmjs.org (Trusted Publishing / OIDC) and GitHub Packages (npm)
    - Ruby: publish to RubyGems.org (Trusted Publishing / OIDC) and GitHub Packages (RubyGems)

### Identity matching for Ruby (hard requirement)

For Ruby releases, the following must match:

- tag segment: `release/<project>/v<version>`
- gemspec filename: `<project>.gemspec`
- gem name inside built `.gem`

### Version policy

- Tags remain `release/<project>/v<version>`.
- Python:
    - accepts a PEP 440 subset; leading `v` is allowed and stripped
- Node:
    - strict SemVer 2.0.0
- Ruby:
    - accept only a Ruby-style SemVer2-variant:
        - allowed: `MAJOR.MINOR.PATCH` and prerelease dot segments such as `1.2.3.beta.1`, `1.2.3.rc.0`
        - rejected: SemVer hyphen prerelease (`1.2.3-beta.1`), build metadata (`+...`), and RubyGems-valid but non-SemVer-core forms (`1.0`, dates, etc.)
    - therefore `version == rubygems_version` for Ruby (no normalization step)

### GitHub Packages RubyGems auth

- Use `${{ github.token }}` only (no fallback secrets).
- Maintainers must ensure GitHub Packages “Actions access” is correctly configured.
- Require gemspec metadata `github_repo` to enable auto-linking:
    - `https://github.com/hcoona/three.git`

### Buddy safety (all kinds)

- Buddy releases are **prerelease-only**.
- Buddy must not create or modify an existing GitHub Release where `prerelease=false`.

### Ruby toolchain and release build checks

- Ruby version input is pinned from the entry workflows (similar to Node/Python tool versions): initial `3.3`.
- Release build runs:
    - `bundle exec standardrb`
    - `bundle exec rspec`
    - when `Gemfile` exists

### Ruby release build system dependencies (release job)

Adopt the “install system deps in release build job” strategy.

Baseline apt packages:

- `texlive-latex-base`
- `texlive-latex-recommended`
- `texlive-fonts-recommended`
- `dvisvgm`
- `pdf2svg`
- `poppler-utils`
- `imagemagick`
- `ghostscript`

## Design changes

### 1) Resolver: add Ruby + remove ambiguity hazards

The current resolver (`release-resolve.yml`) is effectively “first match wins” across Python/Node. This must be hardened before adding Ruby.

#### 1.1 Detector contract (all find\_\*\_project_path scripts)

Establish a uniform detector contract:

- exit `0`: unique match; print `package_dir` to stdout
- exit `2`: ambiguous matches; print details to stderr
- exit `3`: not found
- exit `1`: unexpected error

Update existing detectors:

- `eng/scripts/find_python_project_path.py`
- `eng/scripts/find_node_project_path.py`

…to implement ambiguity detection and the contract above.

Add:

- `eng/scripts/find_ruby_project_path.py` (match `<project>.gemspec`)

#### 1.2 Cross-kind selection in `release-resolve.yml`

In the “Determine project kind and package directory” step:

1. Run Python/Node/Ruby detectors and capture:
    - exit code
    - stdout candidate path
    - stderr diagnostics
2. Fail immediately if any detector returns exit `2` (within-kind ambiguity).
3. Collect candidates with exit `0`.
4. If candidates are:
    - `0`: fail “Unknown project” and print detector diagnostics
    - `>1`: fail “Ambiguous project kind” listing all candidates
    - `1`: proceed

Add resolver outputs:

- `is_prerelease` (string `true|false`)
- `rubygems_version` (string; Ruby only, equals `version`)

#### 1.3 Version validation per kind

Add `eng/scripts/validate_rubygems_version.py`.

Define the accepted Ruby grammar explicitly to support safe prerelease detection:

- core: `<int>.<int>.<int>`
- optional prerelease: `.<ident>.<int>(.<ident>.<int>)*`
    - `<ident>` must start with a letter; numeric-only suffixes are rejected
- reject `-` and `+` entirely

Then:

- Ruby prerelease detection is: “version has any prerelease segments beyond MAJOR.MINOR.PATCH”.

### 2) Reproducible builds baseline (Python / Node / Ruby)

To support provenance and reduce “works on rerun” surprises, standardize a minimal reproducibility baseline across build workflows.

For all build jobs (Python build, Node pack, Ruby build):

- set `TZ=UTC`
- set `LC_ALL=C.UTF-8`
- set `SOURCE_DATE_EPOCH` to the target commit timestamp
    - recommended: `git show -s --format=%ct <target>`

Per language:

- Python (`uv build`): set `SOURCE_DATE_EPOCH` before `uv build`.
- Node (`npm pack`): set `SOURCE_DATE_EPOCH` before `npm pack`.
- Ruby (`gem build`): set `SOURCE_DATE_EPOCH` before `gem build`.

### 3) Ruby build: new reusable workflow (artifact-first)

Add `.github/workflows/release-build-ruby-gem.yml`.

Inputs:

- `target`, `project`, `package_dir`, `version`, `ruby_version`, `artifact_name`

Steps:

1. Checkout `ref: target`.
2. Install baseline system deps (apt) required by known Ruby projects.
3. Setup Ruby via `ruby/setup-ruby@v1`.
4. If `Gemfile` exists:
    - `bundler-cache: true`
    - run `bundle exec standardrb`
    - run `bundle exec rspec`
5. Build exactly one gem artifact:
    - clean `${GITHUB_WORKSPACE}/out`
    - run `gem build <project>.gemspec --output ${GITHUB_WORKSPACE}/out/<project>.gem`
    - refuse multiple gem outputs
6. Verify artifact identity:
    - `gem specification out/<project>.gem name == <project>`
    - `gem specification out/<project>.gem version == <version>`
7. Upload artifact `out/*`.

### 4) Ruby publish: GitHub Packages (buddy + official)

Publish to GitHub Packages RubyGems registry using `${{ github.token }}`.

Job permissions:

- `packages: write`
- `contents: read`

Auth:

- write `~/.gem/credentials` with:
    - `:github: Bearer ${{ github.token }}`

Publish command:

- `gem push --key github --host https://rubygems.pkg.github.com/${OWNER} out/<project>.gem`

Idempotent rerun behavior:

- If push fails with “version already exists”, fetch the remote `.gem` and compare digests:
    - `gem fetch <project> -v <version> --source https://rubygems.pkg.github.com/${OWNER}`
    - compute SHA256 of downloaded `.gem`
    - compare to SHA256 of `out/<project>.gem`
    - if equal: treat as success and continue
    - else: fail (registry already has different content for same version)

### 5) Ruby publish: RubyGems.org (official only, Trusted Publishing only)

Use OIDC Trusted Publishing only.

Job:

- `environment: rubygems` (must match RubyGems trusted publisher configuration)
- permissions:
    - `id-token: write`
    - `contents: read`
    - `actions: read`

Steps:

1. Setup Ruby.
2. Run `rubygems/configure-rubygems-credentials` (pinned to a commit SHA):
    - do not pass `api-token`
    - do not pass `role-to-assume`
    - optionally set `trusted-publisher: true` explicitly
3. Publish from artifact only:
    - `gem push out/<project>.gem`

Idempotent rerun behavior:

- Preflight check via RubyGems.org API:
    - `GET https://rubygems.org/api/v1/versions/<project>.json`
    - if `<version>` exists, compare its `sha` field to local SHA256.
    - if equal: skip publish and continue
    - else: fail

### 6) Node publish alignment (official: npmjs + GPR)

Node already publishes to two registries. Align it with the same idempotent rerun policy.

For each registry:

- derive the expected tarball digest from the local artifact (`out/npmjs.tgz` for npmjs, `out/gpr.tgz` for GPR)
- query registry metadata:
    - npmjs: `npm view <name>@<version> dist.integrity` (or `dist.shasum`)
    - GPR: same query with `--registry https://npm.pkg.github.com` and authenticated config
- if version exists:
    - if digest matches: skip publishing to that registry
    - else: fail

This reduces the “half-published, cannot rerun” trap.

### 7) Python publish alignment (official: PyPI)

PyPI is single-registry, but we still want safe reruns.

Add a preflight step in the publish job:

- query `https://pypi.org/pypi/<project>/json`
- if `<version>` exists:
    - compare all artifact file digests (sdist + wheels) against local `out/*`
    - if all match: skip publish and continue
    - else: fail

Also set `SOURCE_DATE_EPOCH` in the build workflow before `uv build`.

### 8) Buddy prerelease-only enforcement (all kinds)

Add `is_prerelease` output to the resolver.

In `.github/workflows/buddy.yml`, add a guard step immediately after `resolve`:

- if `is_prerelease != true`: fail with a clear message

This applies to Python, Node, and Ruby consistently.

### 9) Entry workflows: add Ruby version export + wire Ruby jobs

Update `.github/workflows/official.yml` and `.github/workflows/buddy.yml`:

- add `RUBY_VERSION: '3.3'` in `env:`
- include `ruby_version` in the `versions` job outputs

Wire new Ruby jobs:

- `build-ruby` (uses `release-build-ruby-gem.yml`)
- `publish-ruby-gpr` (buddy + official)
- `publish-ruby-rubygems` (official only)
- `attest-ruby` (official)
- `release-ruby` / `release-ruby-gpr` gating consistent with existing patterns

### 10) Documentation and maintainer checklists

#### RubyGems.org Trusted Publisher

For each gem to be published to RubyGems.org:

- configure Trusted Publisher:
    - owner: `hcoona`
    - repository: `three`
    - workflow filename: `official.yml`
    - environment: `rubygems`

#### GitHub Packages RubyGems registry

For each gem:

- set gemspec metadata `github_repo`:
    - `https://github.com/hcoona/three.git`
- ensure “Manage Actions access” permits workflows from `hcoona/three` (or inheritance is enabled)

Note: GitHub Packages docs include wording about PAT usage; however, GitHub also recommends `GITHUB_TOKEN` for registries with granular permissions when packages are associated with the workflow repository. This repo’s policy is to rely on `${{ github.token }}` only.

## Acceptance criteria

### Resolver

- Detects Ruby gems by `<project>.gemspec`.
- Fails on:
    - within-kind ambiguity (multiple matches)
    - cross-kind ambiguity (multiple kinds match)
    - unknown project
- Validates versions per kind, including Ruby grammar.
- Exposes `is_prerelease` and (Ruby) `rubygems_version` outputs.

### Ruby

- Build produces exactly one deterministic gem artifact: `out/<project>.gem`.
- Official:
    - publishes to RubyGems.org via Trusted Publishing only
    - publishes to GitHub Packages RubyGems via `${{ github.token }}` only
    - attests and creates GitHub Release only after both publishes succeed (or are confirmed already-published with matching digests)
- Buddy:
    - fails on stable versions
    - publishes only to GitHub Packages RubyGems
    - creates/updates a prerelease GitHub Release
    - never clobbers an official release

### Alignment

- Python/Node/Ruby build steps set `SOURCE_DATE_EPOCH`.
- Official Node and Ruby multi-registry publishes support idempotent reruns via digest verification.
- Official Python publish supports safe reruns via digest verification.

## Implementation sequence (incremental, testable)

1. Implement detector exit-code contract for Python and Node detectors.
2. Add Ruby detector + Ruby version validator.
3. Update `release-resolve.yml` to:
    - run all detectors
    - fail on ambiguity
    - emit `is_prerelease` and `rubygems_version`.
4. Add buddy prerelease-only guard in `buddy.yml`.
5. Add reproducibility baseline (`SOURCE_DATE_EPOCH`, `TZ`, `LC_ALL`) to existing build workflows.
6. Add `release-build-ruby-gem.yml` and wire Ruby build into buddy (GPR publish only) first.
7. Add Ruby official publishing:
    - RubyGems.org (Trusted Publishing)
    - GitHub Packages RubyGems
    - attestation + release gating.
8. Add idempotent rerun logic to Node official publish (npmjs + GPR).
9. Add idempotent rerun logic to Python official publish (PyPI).

## Risks and mitigations

- Ruby build system dependency drift (apt):
    - mitigate by keeping the baseline list minimal and updating as projects require.
- Non-idempotent registries:
    - mitigate by digest-based “already published” verification and hard-fail on mismatches.
- Publishing multiple artifacts accidentally:
    - mitigate by naming single artifacts deterministically (`out/<project>.gem`, `out/npmjs.tgz`, `out/gpr.tgz`).
