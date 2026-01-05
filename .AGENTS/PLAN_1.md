# PLAN_1: Add RubyGems release support (artifact-first, Trusted Publishing only)

## Goal

Extend the existing release system (see `.github/workflows/official.yml`, `.github/workflows/buddy.yml`, and `.github/workflows/release-resolve.yml`) to support releasing Ruby gem projects in addition to the current Python and Node support.

This plan is a revision of `PLAN_0.md` to address the gaps called out in `PLAN_REVIEW_0.md` and to incorporate the maintainer decisions captured in `CLARIFY_PLAN_0.md`.

## Non-goals

- Do not introduce long-lived RubyGems API tokens or any non-Trusted-Publishing authentication method for RubyGems.org.
- Do not change the overall release architecture (resolve → build artifact → publish → attest → GitHub Release).
- Do not introduce a Bundler/Rake “release task” as the source of truth for the published gem.

## Key decisions (from CLARIFY_PLAN_0.md)

1. RubyGems.org publishing uses Trusted Publishing (OIDC) and publishes prebuilt `.gem` artifacts (no rebuild in publish jobs).
2. Official releases publish to both registries:
    - RubyGems.org
    - GitHub Packages (RubyGems registry)
      GitHub Release creation is gated on success of both.
3. Buddy releases publish only to GitHub Packages (RubyGems registry) and create a prerelease GitHub Release.
    - Buddy releases are **prerelease-only** and must never publish a stable/final version.
    - Buddy must not create or modify a GitHub Release whose `prerelease` flag is `false` (official).
4. GitHub Actions environment name for RubyGems Trusted Publishing: `rubygems`.
5. Ruby toolchain version policy: define `RUBY_VERSION` in entry workflows, export it via the `versions` job, and pass through to build/publish jobs. Initial version: `3.3`.
6. Project identity must match across:
    - tag segment `release/<project>/v<version>`
    - gemspec filename `<project>.gemspec`
    - gem name inside built `.gem` (via `gem specification`)
7. Ambiguity is a hard error:
    - if multiple `<project>.gemspec` matches exist
    - or if multiple project kinds match the same `project` name
8. GitHub Packages auth uses only `${{ github.token }}` with `permissions: packages: write`. No fallback secret.

## References (authoritative behavior)

- GitHub Packages RubyGems registry docs: `https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-rubygems-registry`
    - Publishing uses `gem push --host https://rubygems.pkg.github.com/<NAMESPACE>`.
    - Credentials commonly provided via `~/.gem/credentials` key `:github: Bearer <token>`.
- RubyGems Trusted Publishing docs: `https://guides.rubygems.org/trusted-publishing/`
    - Trusted publishers are configured per gem and can be constrained by GitHub Actions workflow filename and environment.
    - Requires `permissions: id-token: write`.
- `rubygems/release-gem@v1` runs `bundle exec rake release` and therefore may rebuild; that conflicts with this repository’s “build once, publish artifacts” model.
- `rubygems/configure-rubygems-credentials` supports Trusted Publishing by exchanging a GitHub OIDC token for a short-lived RubyGems token and configuring `RUBYGEMS_API_KEY`, `GEM_HOST_API_KEY`, and `~/.gem/credentials`.

## Design principles (align with existing repo patterns)

- Build once from the resolved `target` commit and upload artifacts under a flat `out/*` layout.
- Publish jobs must publish from downloaded artifacts (never rebuild).
- Use the same entry-workflow pattern as Python/Node:
    - `versions` job exports tool versions
    - `resolve` reusable workflow provides `{project, version, project_kind, target, package_dir, ...}`
    - build job produces `out/*` and uploads as an artifact
    - publish jobs download artifacts and publish
    - attestation job attests `out/*`
    - GitHub Release job attaches `out/*`
- Fail fast with clear errors for ambiguous project selection and identity mismatches.

Buddy/Official safety invariant (applies to all project kinds):

- Buddy publishes prerelease versions only.
- Buddy may only create/update GitHub Releases where `prerelease: true`.
- Official may publish stable or prerelease versions.

## Required workflow changes

### 1) Extend resolver to support Ruby projects and ambiguity detection

#### 1.1 Add a Ruby detector script

Add `eng/scripts/find_ruby_project_path.py` with behavior:

- Inputs: `project_name`, optional `--root`.
- Search for gemspec files named exactly `<project_name>.gemspec`.
- Ignore irrelevant/huge directories (at least: `.git`, `node_modules`, `.output`).
- If no matches: exit non-zero with a clear “not found” message.
- If multiple matches: exit non-zero with an “ambiguous project” message and list all matches.
- If exactly one match: print the parent directory.

This mirrors the existing `find_node_project_path.py` / `find_python_project_path.py` scripts, but with explicit ambiguity handling.

#### 1.2 Tighten existing Python/Node detectors (recommended)

Update `eng/scripts/find_python_project_path.py` and `eng/scripts/find_node_project_path.py` to match the same ambiguity rule:

- If multiple matches exist, exit non-zero and list the matches.

Notes:

- Today the repository uses two separate detector scripts:
    - `eng/scripts/find_python_project_path.py`
    - `eng/scripts/find_node_project_path.py`
      This plan adds a third: `eng/scripts/find_ruby_project_path.py`.
- After this change, all three detectors follow the same contract: `0 matches => fail`, `>1 matches => fail`, `1 match => print package_dir`.
- A single unified detector script (e.g. `find_project_path.py --kind python|node|ruby`) is possible, but not required for this change.
  Keeping separate scripts is intentional because each kind has different parsing rules and file formats (TOML vs JSON vs gemspec filename), and the resolver must still handle cross-kind ambiguity (e.g. both a Python and a Ruby project named the same).

Rationale: ambiguity must be handled consistently for all project kinds; otherwise a monorepo can publish the wrong project silently.

#### 1.3 Update `release-resolve.yml`

Revise the “Determine project kind and package directory” logic so it does not use “first match wins” ordering.

Target behavior:

- Run Python detection, Node detection, and Ruby detection.
- Collect successes as `(project_kind, package_dir)` candidates.
- If 0 candidates → fail “Unknown project”.
- If >1 candidates → fail “Ambiguous project kind” and list all detected candidates.
- If exactly 1 → proceed.

This resolves the cross-kind ambiguity risk called out in `PLAN_REVIEW_0.md`.

### 2) Ruby version policy (normalization + single canonical RubyGems version)

RubyGems uses `Gem::Version` semantics, which are not identical to strict SemVer 2.0.0. The release system must define one canonical RubyGems version used for:

- verifying the built `.gem` metadata
- publishing to RubyGems.org and GitHub Packages

#### 2.1 Resolver outputs

Extend `release-resolve.yml` outputs for Ruby projects:

- `version`: the “release version” (used in UI and release title)
- `rubygems_version`: the canonical RubyGems version derived from the tag/manual input

For non-Ruby projects, `rubygems_version` can be empty.

#### 2.2 Normalization rules

Normalization from release version to `rubygems_version` (minimum):

- Drop SemVer build metadata suffix `+...`.
- Convert SemVer prerelease separator `-` to RubyGems-style `.`.

Examples:

- `1.2.3` → `1.2.3`
- `1.2.3-beta.1` → `1.2.3.beta.1`
- `1.2.3-beta.1+githash` → `1.2.3.beta.1`

Implementation note:

- Keep the existing shell-safety checks.
- Add a Ruby-specific validator/normalizer script (recommended: `eng/scripts/normalize_rubygems_version.py`) that applies the rules and rejects results that contain characters unsafe or unsupported by RubyGems publishing.

### 3) Add Ruby build workflow: build + verify + upload `.gem` artifact(s)

Add a new reusable workflow `.github/workflows/release-build-ruby-gem.yml` (naming can be adjusted to match existing conventions) with inputs similar to `release-build-python.yml`:

- `target` (SHA)
- `package_dir`
- `project` (gem name)
- `rubygems_version` (expected gem version)
- `ruby_version`
- `artifact_name`

Steps (Ubuntu runner):

1. Checkout at `ref: target`.
2. Setup Ruby with `ruby/setup-ruby@v1` using `ruby-version: <ruby_version>`.
3. Bundler context detection:
    - If `Gemfile` exists in `package_dir`, enable bundler cache (`bundler-cache: true`).
    - If not, skip Bundler steps and log “no Gemfile; skipping bundle/lint/test”.
4. Quality checks (only when `Gemfile` exists):
    - Lint: `bundle exec standardrb`.
    - Tests: `bundle exec rspec`.
    - If either tool is missing from the bundle, fail with a clear message (this is an explicit policy choice for Ruby projects that include a Gemfile).
5. Build the gem:
    - Run `gem build <project>.gemspec` in `package_dir`.
    - Copy resulting `*.gem` files into `${GITHUB_WORKSPACE}/out/` (flat layout).
6. Verify built artifacts:
    - For each `.gem` in `out/`, verify:
        - `gem specification <file> name` equals `project`
        - `gem specification <file> version` equals `rubygems_version`
    - Fail if any mismatch.
7. Upload artifact: `out/*`.

This mirrors the Python “build then verify” approach (`verify_python_artifact_version.py`) and ensures the artifact is authoritative.

### 4) Publishing (artifact-first)

#### 4.1 Publish to RubyGems.org (Official only, Trusted Publishing only)

Add an official-only job (either inline in `.github/workflows/official.yml` or as a reusable workflow) that:

- Downloads the build artifact into `${GITHUB_WORKSPACE}/out`.
- Sets job permissions: `id-token: write` (required) and `contents: read`.
- Sets `environment: rubygems` (must match the RubyGems trusted publisher configuration).
- Sets up Ruby (`ruby/setup-ruby@v1`) so `gem` is available.
- Runs `rubygems/configure-rubygems-credentials@v1` (pin to a commit SHA).
    - Do not provide `api-token`.
    - Do not provide `role-to-assume`.
    - Rely on Trusted Publishing default.
- Publishes the downloaded artifacts with `gem push ${GITHUB_WORKSPACE}/out/*.gem`.

Important: do not use `rubygems/release-gem@v1` because it runs `bundle exec rake release` and can rebuild, violating the artifact-first model.

#### 4.2 Publish to GitHub Packages (RubyGems registry)

Add jobs for both official and buddy that:

- Download the build artifact into `${GITHUB_WORKSPACE}/out`.
- Use `${{ github.token }}` only.
- Set job permissions: `packages: write` (and `contents: read`).
- Write `~/.gem/credentials` with a `github` key containing a Bearer token.
- Publish each `.gem` with host `https://rubygems.pkg.github.com/<OWNER>`.

Policy: if `GITHUB_TOKEN` is rejected by GitHub Packages in this repository/org configuration, the job must fail with a clear error. No fallback secret is added.

### 5) Attestations (optional but consistent with existing flows)

Add an attestation job for Ruby artifacts similar to `attest-python` and `attest-node`:

- Download dist artifact to `${GITHUB_WORKSPACE}/out`.
- Use `actions/attest-build-provenance@v3` with `subject-path: ${GITHUB_WORKSPACE}/out/*`.
- Required permissions: `id-token: write`, `attestations: write`, `actions: read`, `contents: read`.

### 6) GitHub Release creation and gating

#### Official

- GitHub Release creation for Ruby is gated on:
    - `publish-ruby-gpr` success
    - `publish-ruby-rubygems` success
    - `attest-ruby` success (if enabled)

This aligns with the “official release is gated on successful publishing to all required registries” policy used for Node.

#### Buddy

- Must depend on the existing `guard-non-clobber` job.
- Publishes only to GitHub Packages.
- Creates/updates a prerelease GitHub Release with attached `.gem` assets.

Additional buddy safety requirements (align with Node and Python):

- Buddy must fail fast if the resolved version is stable/final (buddy is prerelease-only).
- Buddy must fail fast if a GitHub Release for the target tag already exists and `prerelease` is `false` (never overwrite official).

## Entry workflow integration

### `.github/workflows/official.yml`

- Add `RUBY_VERSION` to `env:`.
- Extend `versions` job outputs to include `ruby_version`.
- Add jobs conditional on `needs.resolve.outputs.project_kind == 'ruby'`:
    - `build-ruby` → calls `release-build-ruby-gem.yml`
    - `publish-ruby-gpr` → publishes to GitHub Packages
    - `publish-ruby-rubygems` → publishes to RubyGems.org via Trusted Publishing
    - `attest-ruby`
    - `release-ruby` → calls `release-create-github-release.yml`

### `.github/workflows/buddy.yml`

- Add `RUBY_VERSION` to `env:`.
- Extend `versions` job outputs to include `ruby_version`.
- Add Ruby jobs conditional on `project_kind == 'ruby'` and gated by `guard-non-clobber`:
    - `build-ruby`
    - `publish-ruby-gpr`
    - optional `attest-ruby`
    - `release-ruby` (prerelease=true)

## Maintainer setup checklist (RubyGems.org)

For each gem to be published to RubyGems.org:

- Configure a Trusted Publisher on RubyGems.org for that gem.
- Provide:
    - Owner: `hcoona`
    - Repository: `three`
    - Workflow filename: `official.yml`
    - Environment: `rubygems`

If the gem is new and does not yet exist on RubyGems.org, configure a pending trusted publisher (RubyGems supports this flow).

## Maintainer setup checklist (GitHub Packages)

Because we intentionally have **no fallback secret** and rely only on `github.token`, maintainers must ensure the GitHub Packages RubyGems publish path is unblocked.

For each gem to be published to GitHub Packages:

1. Require gemspec metadata `github_repo`

- The gemspec must set the metadata key `github_repo` to:
    - `https://github.com/hcoona/three.git`

1. Require correct repository linkage / Actions access

- The package must be either:
    - linked to `hcoona/three` (with inheritance enabled), or
    - explicitly configured under “Manage Actions access” to allow workflows from `hcoona/three`.

## Acceptance criteria

- Resolver can uniquely resolve a Ruby project by `project` name using `<project>.gemspec`, and fails on ambiguity.
- Ruby build workflow produces `.gem` artifacts under `out/*` and verifies gem name/version match the resolved `project` and `rubygems_version`.
- Official release publishes the built `.gem` artifacts to:
    - RubyGems.org via Trusted Publishing (OIDC), without rebuilding
    - GitHub Packages RubyGems registry using `${{ github.token }}`
- Buddy release publishes only to GitHub Packages and creates/updates a prerelease GitHub Release, and must never clobber an existing official release (enforced via GitHub Release `prerelease` flag).
- Buddy publishing is prerelease-only (no stable/final versions) and this policy is aligned across Ruby, Node (npm), and Python.
- GitHub Release contains the same `.gem` assets produced by the build job.
- Attestations (if enabled) are generated for the `.gem` artifacts.

## Implementation sequence (recommended)

1. Add Ruby detector + ambiguity handling in Python/Node detectors.
2. Update `release-resolve.yml` to support Ruby and cross-kind ambiguity detection.
3. Add Ruby build reusable workflow and wire into `official.yml`/`buddy.yml`.
4. Add GitHub Packages publish job for Ruby (buddy first), validate end-to-end.
5. Add RubyGems.org Trusted Publishing job (official), validate against a configured trusted publisher.
6. Add attestation + gating, then validate GitHub Release attachment behavior.
