# PLAN_0: Add Ruby Gem project support to release workflows

## Goal

Extend the existing release system (modeled by `.github/workflows/official.yml` and `.github/workflows/buddy.yml`) to support Ruby Gem projects, in addition to the current Python and Node support.

The new support must:

- Resolve a Ruby gem project by `project` name (matching the release tag `release/<project>/v<version>`).
- Build the gem from a target commit and upload the built `.gem` file(s) as a workflow artifact.
- Publish the gem:
    - **Official**: publish to RubyGems.org using **Trusted Publishing (OIDC)**, and optionally publish to **GitHub Packages (RubyGems registry)**.
    - **Buddy**: publish **only** to GitHub Packages (RubyGems registry) and create a prerelease GitHub Release (same policy as buddy Node).
- Create a GitHub Release with the built assets attached (same as existing).
- Optionally generate build provenance attestations for the `.gem` files (same pattern as Python/Node).

Constraints and conventions from the current repo:

- English-only documentation and workflow comments.
- Keep the release/tag format unchanged: `release/<project>/v<version>`.
- Reuse the existing reusable workflow pattern where possible.

## References captured

### RubyGems.org Trusted Publishing (OIDC)

- RubyGems guides: Trusted Publishing overview and quickstarts.
- Recommended action: `rubygems/release-gem@v1`.
- Requires job-level `permissions: { id-token: write, contents: write }`.
- If a GitHub Actions environment is configured in RubyGems as a constraint, the job must declare `environment: <name>`.

### GitHub Packages RubyGems registry

- Host: `https://rubygems.pkg.github.com/<OWNER>`.
- Publish command: `gem push --key github --host https://rubygems.pkg.github.com/<OWNER> <gemfile>`.
- Authentication options:
    - Docs mention PAT classic is supported; docs also recommend `GITHUB_TOKEN` for registries with granular permissions.
    - For this repo’s existing pattern, attempt to publish with `github.token` (GITHUB_TOKEN) and require `permissions: packages: write`.

## Current architecture summary (baseline)

- Entry workflows:
    - `.github/workflows/official.yml`: builds + publishes (Python to PyPI, Node to npmjs+GPR, WXT special-case), creates GitHub Release, attests assets.
    - `.github/workflows/buddy.yml`: prerelease-oriented; adds guard against clobbering an existing official release; publishes Node only to GPR.
- Reusable workflows:
    - `release-resolve.yml`: resolves `project`, `version`, `target`, `project_kind`, `package_dir`, etc.
    - `release-build-python.yml`, `release-build-node-pack.yml`, `release-build-wxt.yml`.
    - `release-prepare-release-notes.yml`, `release-create-github-release.yml`.

## Proposed design

### 1) Add a third project kind: `ruby`

Enhance `release-resolve.yml` to detect Ruby projects and output:

- `project_kind = ruby`
- `package_dir` = directory containing the gem project

**Detection rule (recommended for robustness without executing Ruby):**

- A Ruby gem project is identified by a gemspec file named exactly `<project>.gemspec`.
- `package_dir` is the parent directory of that gemspec.

This aligns with the existing gem example in the repo:

- `src/public/lib/asciidoctor-latexmath/asciidoctor-latexmath.gemspec`

Implementation steps:

- Add a new helper script `eng/scripts/find_ruby_project_path.py` (mirrors the Python/Node scripts):
    - `rglob("*.gemspec")`, ignore common large/irrelevant directories (`.git`, `node_modules`, etc.).
    - Match on `path.name == f"{project}.gemspec"`.
    - Return the shortest matching parent directory.
    - Error if none or multiple ambiguous matches.
- Update `release-resolve.yml`:
    - Try Python detection, then Node detection, then Ruby detection.
    - Update the unknown-project error message accordingly.

### 2) Version validation policy for Ruby

Keep the current repo’s rule:

- Python: PEP 440 subset (allow leading `v`).
- Non-Python: SemVer 2.0.0 (no leading `v`).

Treat Ruby as **non-Python**:

- Validate Ruby gem release versions using the existing `eng/scripts/validate_semver2_version.py`.

Rationale:

- Keeps the tag format and validation uniform across “non-Python” projects.
- Avoids introducing a new version grammar unless needed.

If the repo later needs RubyGems-specific versions that are not SemVer2, add:

- `eng/scripts/validate_rubygems_version.py` and switch Ruby to that validator.

### 3) Add a reusable workflow: `release-build-ruby.yml`

Create `.github/workflows/release-build-ruby.yml` with inputs similar to the other build workflows:

- `target` (SHA)
- `package_dir`
- `project` (gem name)
- `version` (expected)
- `ruby_version`
- `artifact_name`

Job steps (Ubuntu):

1. Checkout `ref: target`.
2. Setup Ruby using `ruby/setup-ruby@v1`:
    - `ruby-version: <ruby_version>` (suggest `ruby` or a pinned major/minor).
    - `bundler-cache: true` (uses `Gemfile.lock` if present).
3. Build the gem:
    - `cd "$PACKAGE_DIR"`
    - `gem build "${PROJECT}.gemspec"`
    - copy resulting `*.gem` into `$GITHUB_WORKSPACE/out/`.
4. Verify built gem version matches the resolved release version:
    - Use `gem specification out/<file>.gem version` and compare with `inputs.version`.
    - Also verify gem name: `gem specification <file>.gem name` matches `inputs.project`.
5. Upload artifact `out/*`.

Optional best-effort quality checks (should not break repos that don’t define them):

- If `Rakefile` exists, attempt one of:
    - `bundle exec rake test`
    - `bundle exec rake spec`
- Else if `spec/` exists, run `bundle exec rspec`.
- Else: skip tests with a clear log line.

### 4) Publishing workflows

#### 4.1) RubyGems.org (Trusted Publishing / OIDC)

Implement as an _inlined job_ in `official.yml` (and optionally `buddy.yml` if ever needed), or as a reusable workflow (recommended if we want symmetry with Python/Node).

Recommended approach:

- Add a job `publish-ruby-rubygems` to `official.yml`:
    - `environment: rubygems` (or `release`; must match RubyGems trusted publisher configuration)
    - `permissions: { contents: write, id-token: write }`
    - Checkout at `target` with `persist-credentials: false` (recommended by `rubygems/release-gem`).
    - Setup Ruby + bundler.
    - Run `rubygems/release-gem@v1` pinned to a commit SHA.

Notes:

- `rubygems/release-gem` assumes the project has Bundler release tasks configured (usually via `bundler gem` scaffolding + `rake release`).
- The RubyGems Trusted Publisher must be configured in RubyGems.org UI with:
    - owner, repo, workflow filename (`official.yml` or another Ruby-specific workflow), and (optionally) environment name.

#### 4.2) GitHub Packages (RubyGems registry)

Add publishing jobs similar to Node’s GPR publishing.

For **official**:

- Add job `publish-ruby-gpr` (runs on ubuntu-latest):
    - Needs: `resolve`, `build-ruby`.
    - `permissions: { contents: read, packages: write }`.
    - Download dist artifact to `$GITHUB_WORKSPACE/out`.
    - Write `~/.gem/credentials` with key `github`:
        - `:github: Bearer ${{ github.token }}`
        - file mode 0600.
    - Push each built gem:
        - `gem push --key github --host https://rubygems.pkg.github.com/${OWNER} out/*.gem`

For **buddy**:

- Add job `publish-ruby-gpr` analogous to buddy’s Node GPR publishing:
    - Needs: `resolve`, `build-ruby`, `guard-non-clobber`.
    - Same credential + `gem push` approach.

Caveat:

- GitHub Packages RubyGems docs historically emphasize PAT classic; however, GitHub Packages also supports `GITHUB_TOKEN` for many registries when the workflow has appropriate permissions. Plan to start with `github.token` and document a fallback:
    - If `GITHUB_TOKEN` is rejected, require an org/repo secret (classic PAT) dedicated to packages publishing.

### 5) Attestations and GitHub Release

- Add `attest-ruby` job (official and buddy) mirroring existing pattern:
    - Download dist artifact to `out/`.
    - `permissions: { actions: read, contents: read, id-token: write, attestations: write }`
    - `actions/attest-build-provenance@v3` with `subject-path: out/*`.

- Add `release-ruby` job that calls `release-create-github-release.yml`:
    - Needs: `resolve`, `prepare-release-notes`, `build-ruby`, and publishing jobs required by the policy.
    - Attach `.gem` artifacts.

## Entry workflow changes

### `.github/workflows/official.yml`

Add:

- `env: RUBY_VERSION` (suggest `ruby` or pinned like `3.3`).
- `build-ruby` job `if: project_kind == 'ruby'` -> uses `release-build-ruby.yml`.
- `publish-ruby-gpr` job (optional but recommended for parity with Node’s dual publish).
- `publish-ruby-rubygems` job (Trusted Publishing).
- `attest-ruby` job.
- `release-ruby` job.

Suggested dependency order:

- `build-ruby` -> (`publish-ruby-gpr` in parallel) -> `publish-ruby-rubygems` -> `attest-ruby` -> `release-ruby`.

### `.github/workflows/buddy.yml`

Add:

- `env: RUBY_VERSION`.
- `build-ruby` job (needs `guard-non-clobber`).
- `publish-ruby-gpr` job (needs `guard-non-clobber`).
- Optional `attest-ruby` job.
- `release-ruby` job (prerelease=true), similar to Node/GPR.

## Files to add / modify

### New files

- `eng/scripts/find_ruby_project_path.py` — locate `<project>.gemspec` directory.
- `.github/workflows/release-build-ruby.yml` — build and verify `.gem` artifacts.

Optional new reusable workflow (if we prefer symmetry):

- `.github/workflows/release-publish-ruby-gpr.yml`
- `.github/workflows/release-publish-ruby-rubygems.yml`

### Modified files

- `.github/workflows/release-resolve.yml` — add ruby detection and `project_kind=ruby`.
- `.github/workflows/official.yml` — wire Ruby jobs.
- `.github/workflows/buddy.yml` — wire Ruby jobs.

## Operational guidance (for maintainers)

### RubyGems Trusted Publisher configuration

For each gem:

- Add a trusted publisher in RubyGems.org UI:
    - Owner: `hcoona` (or org)
    - Repo: `three`
    - Workflow filename: `official.yml` (or a dedicated `publish_gem.yml` if you later split it)
    - Environment: must match the workflow job `environment:` if configured (recommend `rubygems` or `release`).

### GitHub Packages RubyGems registry

- First publish defaults to private visibility; adjust package visibility and Actions access as needed.

## Acceptance criteria

- `release-resolve.yml` can resolve a Ruby gem project in this repo (e.g. `asciidoctor-latexmath`).
- `official.yml` can build Ruby gem artifacts and publish to RubyGems.org via trusted publishing (no long-lived RubyGems API key in secrets).
- `buddy.yml` can build and publish the gem to GitHub Packages and create a prerelease GitHub Release, without clobbering any existing official release tag.
- Built gem version is verified against the `release/<project>/v<version>` tag-derived version.
- Existing Python/Node flows remain unchanged in behavior.
