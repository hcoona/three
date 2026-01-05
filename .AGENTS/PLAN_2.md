<!-- markdownlint-disable MD013 MD044 -->

# PLAN_2: Add RubyGems (Trusted Publishing only) + unify publish semantics across npm/PyPI/RubyGems

## Goal

Extend the existing release system (resolve → build artifact → publish → attest → GitHub Release) to support **Ruby gem** projects, while also **aligning and unifying** the publishing semantics across:

- **Python → PyPI** (Trusted Publishing / OIDC)
- **Node → npmjs + GitHub Packages** (npm Trusted Publishers / OIDC, plus GPR)
- **Ruby → RubyGems.org + GitHub Packages (RubyGems registry)** (RubyGems Trusted Publishing / OIDC, plus GPR)

Key theme: **artifact-first** (build once, publish from downloaded `out/*`), **trusted publishing only** (no long-lived tokens), and **consistent gating / safety** between buddy and official releases.

## Scope

### In scope

- Add Ruby project resolution and build/publish/attest jobs to the root workflows:
    - `.github/workflows/release-resolve.yml`
    - `.github/workflows/official.yml`
    - `.github/workflows/buddy.yml`
    - new reusable workflows for Ruby build/publish as needed
- Add/adjust helper scripts under `eng/scripts/` for:
    - Ruby project detection
    - Ruby version validation (Ruby SemVer2-variant format)
    - ambiguity handling contract across Python/Node/Ruby detection
- Standardize buddy safety rules across Python/Node/Ruby:
    - buddy must be **prerelease-only**
    - buddy must not clobber an existing **official** GitHub Release (`prerelease=false`)

### Non-goals

- Do not introduce RubyGems API tokens, PATs, or any non-OIDC authentication for RubyGems.org.
- Do not rebuild during publish.
- Do not redesign the overall release architecture.
- Do not migrate source layout (monorepo restructuring is out of scope).

## Current baseline (what we must preserve)

- Root workflows already implement:
    - reusable resolver (`release-resolve.yml`)
    - reusable build workflows (Python, Node pack, WXT)
    - artifact-first release assets under a **flat** `out/*` layout
    - official release gating on publish + attest (Python and Node)
    - buddy guard that prevents clobbering an existing official GitHub Release
- Maintainer decisions (source of truth):
    - `.AGENTS/CLARIFY_PLAN_0.md`
    - `.AGENTS/CLARIFY_PLAN_1.md`

## External references (authoritative)

- GitHub Packages RubyGems registry:
    - https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-rubygems-registry
    - Publishing requires: `gem push --key github --host https://rubygems.pkg.github.com/<NAMESPACE> GEM_FILE.gem`
    - Linking packages: set `github_repo` gem metadata
- RubyGems Trusted Publishing:
    - https://guides.rubygems.org/trusted-publishing/
    - Adding publisher / pending publisher:
        - https://guides.rubygems.org/trusted-publishing/adding-a-publisher
        - https://guides.rubygems.org/trusted-publishing/pushing-a-new-gem
    - Requires GitHub Actions `permissions: id-token: write` and (optionally) job `environment`
- RubyGems actions:
    - https://github.com/rubygems/configure-rubygems-credentials
        - Default behavior is Trusted Publishing when `api-token` and `role-to-assume` are not provided.
    - https://github.com/rubygems/release-gem
        - Useful reference, but not used directly due to artifact-first requirements.

## Policy decisions (confirmed)

### Publishing targets

- **Buddy** releases publish **only** to **GitHub Packages (RubyGems registry)**.
- **Official** releases publish to **both**:
    - **RubyGems.org** (Trusted Publishing only)
    - **GitHub Packages (RubyGems registry)**

### Identity matching (hard requirement)

For Ruby releases, the following must match:

- tag segment: `release/<project>/v<version>`
- gemspec filename: `<project>.gemspec`
- gem name inside built `.gem` (`gem specification <file> name`)

### Version policy (Ruby)

Status: CONFIRMED (2026-01-05)

- For Ruby releases, accept **only** the **SemVer2-variant Ruby-style** version format:
    - Allowed:
        - `MAJOR.MINOR.PATCH`
        - prerelease dot segments, e.g. `1.2.3.beta.1`, `1.2.3.rc.0`
    - Disallowed:
        - strict SemVer2 prerelease separator: `1.2.3-beta.1`
        - build metadata: `1.2.3+build.7`
        - RubyGems-valid but non-SemVer2-core forms: `1.0`, `2026.01.05`, `1.0.pre`
- Therefore for Ruby releases:
    - `version == rubygems_version`
    - no normalization step is needed

Notes:

- The resolver will still expose a `rubygems_version` output for Ruby (equal to `version`) to keep downstream steps explicit and future-proof.

### GitHub Packages access policy

Status: CONFIRMED (2026-01-05)

Because we intentionally rely on `${{ github.token }}` only (no fallback PAT/secret):

1. Require gemspec metadata `github_repo`:
    - `https://github.com/hcoona/three.git`
2. Maintainers must ensure Actions access is unblocked:
    - package is linked to `hcoona/three` with inheritance enabled, **or**
    - “Manage Actions access” explicitly allows workflows from `hcoona/three`

### Buddy safety

Status: CONFIRMED (2026-01-05)

- Buddy releases are **prerelease-only**.
- Buddy must never create/modify an existing GitHub Release where `prerelease=false`.
- This policy must apply consistently to Ruby, Node, and Python.

## Design overview

We keep the repo’s established pipeline:

1. **Resolve**: determine `{project, version, project_kind, target, package_dir, …}`
2. **Build**: build once from `target`, upload `out/*`
3. **Publish**: download artifacts, publish without rebuilding
4. **Attest**: generate provenance attestation for `out/*`
5. **GitHub Release**: attach the same `out/*` assets

Ruby is added as a third “kind” alongside Python and Node.

In addition, we add one shared output for consistency:

- `is_prerelease` (computed in resolver) used by buddy to enforce prerelease-only.

Ruby-specific:

- `rubygems_version` (string): for Ruby it equals `version` (no normalization); for non-Ruby it may be empty.

## Detailed changes

### 1) Resolver: add Ruby + remove ambiguity hazards

#### 1.1 Establish a detector contract (required)

Today, `release-resolve.yml` runs Python then Node detection and uses “first match wins”. This must be hardened before adding Ruby.

Define a **uniform detector contract** for `find_*_project_path.py` scripts:

- exit code `0`: unique match; print `package_dir` to stdout
- exit code `2`: ambiguous matches; print a machine-readable list (or clearly prefixed list) to stderr
- exit code `3`: not found
- exit code `1`: unexpected error (parse error, IO error, etc.)

Update existing detectors (`find_python_project_path.py`, `find_node_project_path.py`) to:

- treat multiple matches as ambiguous (exit `2`), and
- optionally ignore common heavy directories (`.git`, `node_modules`, `.output`, etc.) consistently.

#### 1.2 Add a Ruby detector

Add `eng/scripts/find_ruby_project_path.py`:

- Match exactly `<project>.gemspec` by filename
- `0 / 1 / many` results → follow the contract above

#### 1.3 Implement cross-kind selection in `release-resolve.yml`

In the “Determine project kind and package directory” step:

- Run **all** detectors (python/node/ruby)
- Collect successful candidates `(kind, package_dir)`
- Hard-fail immediately if any detector reports ambiguity (exit `2`)
- If candidates count is:
    - `0` → fail “Unknown project” with collected detector diagnostics
    - `>1` → fail “Ambiguous project kind” listing all candidates
    - `1` → proceed

This directly addresses the correctness gap highlighted in `PLAN_REVIEW_1.md`.

#### 1.4 Version validation per kind

Update resolver validation logic:

- Python: keep `validate_pep440_version.py` (manual accepts leading `v`)
- Node: keep `validate_semver2_version.py` (strict SemVer2; no leading `v`)
- Ruby: add `eng/scripts/validate_rubygems_version.py` (or similar):
    - reject `-` and `+`
    - require `MAJOR.MINOR.PATCH` and optional Ruby-style prerelease dot segments

Add resolver outputs:

- `is_prerelease` (boolean)
    - Python: computed via `packaging.version.Version(...).is_prerelease`
    - Node: prerelease if SemVer has `-...`
    - Ruby: prerelease if version contains additional dot segments beyond `MAJOR.MINOR.PATCH`

- `rubygems_version` (string)
    - Ruby: equals `version` (policy: only Ruby-style versions are accepted, so no transformation is required)
    - Python/Node: empty string

### 2) Entry workflows: align tool-version exports

Update `.github/workflows/official.yml` and `.github/workflows/buddy.yml`:

- Add `RUBY_VERSION` to `env:` (initial: `3.3`)
- Export `ruby_version` via the `versions` job outputs
- Pass Ruby version into reusable Ruby build workflows

Also update workflow input documentation (and the resolver `inputs.version` description) so manual runs are unambiguous:

- Python: PEP 440 subset (leading `v` allowed; stripped)
- Node: strict SemVer 2.0.0 (no leading `v`)
- Ruby: Ruby SemVer2-variant (no `-` prerelease separator, no `+` build metadata)

This matches the existing pattern used for Python/Node (`PYTHON_VERSION`, `NODE_VERSION`, `PNPM_VERSION`).

### 3) Ruby build: reusable workflow (artifact-first)

Add `.github/workflows/release-build-ruby-gem.yml` (reusable) with inputs:

- `target`, `project`, `package_dir`, `version`, `ruby_version`, `artifact_name`

Build job requirements:

1. checkout `ref: target`
2. setup Ruby via `ruby/setup-ruby@v1`
3. If `Gemfile` exists:
    - enable `bundler-cache: true`
    - run quality checks:
        - `bundle exec standardrb`
        - `bundle exec rspec`
4. Build gem from gemspec:
    - run `gem build <project>.gemspec`
    - stage result into `${GITHUB_WORKSPACE}/out/*` (flat layout)
    - avoid accidentally picking up stale `.gem` files (clean workspace, or build in a temp dir, or verify timestamps)
5. Verify artifacts:
    - `gem specification <file> name` == `project`
    - `gem specification <file> version` == `version`
6. upload artifact `out/*`

### 4) Ruby publish: GitHub Packages (buddy + official)

Implement a publish job (inline or reusable) for GitHub Packages RubyGems:

- download `out/*` to `${GITHUB_WORKSPACE}/out`
- `permissions: packages: write` (and `contents: read`)
- create `~/.gem/credentials` containing:
    - `:github: Bearer ${{ github.token }}`

- ensure file permission safety:
    - `chmod 0600 ~/.gem/credentials`

- publish using:
    - `gem push --key github --host https://rubygems.pkg.github.com/${{ github.repository_owner }} ${GITHUB_WORKSPACE}/out/*.gem`

Rationale:

- GitHub documentation shows `--key github` and `:github:` credentials key.
- We must rely on `${{ github.token }}` only (no fallback secret).

### 5) Ruby publish: RubyGems.org (official only, Trusted Publishing only)

Add an official-only publish job to RubyGems.org:

- `environment: rubygems` (must match RubyGems trusted publisher config)
- `permissions: id-token: write` (and `contents: read`, `actions: read`)
- setup Ruby
- run `rubygems/configure-rubygems-credentials` with Trusted Publishing only:
    - do not provide `api-token`
    - do not provide `role-to-assume`
    - explicitly set `trusted-publisher: true` to future-proof defaults
    - pin action to a commit SHA (consistent with repo security posture)
- publish from artifacts only:
    - `gem push ${GITHUB_WORKSPACE}/out/*.gem`

Note:

- We do **not** use `rubygems/release-gem@v1` because it is primarily designed around a Bundler/Rake “release task” flow and can rebuild, violating artifact-first.

### 6) Attestations (Ruby)

Add an attestation job for Ruby, consistent with Python/Node:

- download `out/*`
- run `actions/attest-build-provenance@v3` with `subject-path: ${GITHUB_WORKSPACE}/out/*`
- permissions:
    - `id-token: write`
    - `attestations: write`
    - `actions: read`
    - `contents: read`

### 7) GitHub Release gating

#### Official gating

For Ruby official releases, GitHub Release creation must depend on:

- RubyGems.org publish job success
- GitHub Packages publish job success
- Ruby attestation job success (if enabled)

This mirrors Node official behavior (publish to both registries, then release).

#### Buddy flow

Buddy Ruby releases:

- must pass `guard-non-clobber`
- must enforce prerelease-only (`is_prerelease == true`)
- publish only to GitHub Packages
- create/update GitHub Release with `prerelease: true` and attach `out/*`

### 8) Cross-language unification: buddy prerelease-only

Implement prerelease-only enforcement in buddy for **all** kinds.

Recommended approach:

- Add `is_prerelease` output in `release-resolve.yml`
- In `buddy.yml`, add a guard step after `resolve`:
    - if `is_prerelease != true` then fail with a clear message

This aligns Ruby/Node/Python to the same invariant confirmed in `CLARIFY_PLAN_1.md`.

## Maintainer setup checklists

### RubyGems.org Trusted Publisher

For each gem that should publish to RubyGems.org:

- configure a Trusted Publisher (or pending Trusted Publisher for new gems)
- set values:
    - owner: `hcoona`
    - repository: `three`
    - workflow filename: `official.yml`
    - environment: `rubygems`

### GitHub Packages RubyGems registry

For each gem:

- ensure gemspec metadata includes:
    - `github_repo: https://github.com/hcoona/three.git`
- ensure package access is configured so `${{ github.token }}` can publish:
    - linked repository inheritance enabled, or Manage Actions access includes `hcoona/three`

## Acceptance criteria

### Ruby

- Resolver detects Ruby projects and fails on:
    - zero matches
    - within-kind ambiguity
    - cross-kind ambiguity
- Ruby version validation enforces the confirmed policy (Ruby SemVer2-variant only).
- Ruby build produces `out/*.gem` and verifies name/version match.
- Official Ruby release publishes the built artifacts to:
    - RubyGems.org using Trusted Publishing only
    - GitHub Packages RubyGems registry using `${{ github.token }}` only
- Buddy Ruby release:
    - fails if version is stable/final
    - publishes only to GitHub Packages
    - creates/updates a prerelease GitHub Release
    - never clobbers an existing official GitHub Release

### Unification

- Buddy prerelease-only rule is enforced consistently for Python, Node, and Ruby.
- Official releases remain gated on the required publish jobs (and attestations where applicable).
- All release assets remain artifact-first and attached via the existing `release-create-github-release.yml` (flat layout).

## Implementation sequence (incremental, testable)

1. Update detectors + introduce exit-code contract (Python + Node).
2. Add Ruby detector.
3. Update `release-resolve.yml` for:
    - cross-kind selection
    - Ruby version validation
    - `is_prerelease` output
4. Add buddy prerelease-only guard (all kinds).
5. Add Ruby build reusable workflow and wire into official/buddy.
6. Add Ruby publish to GitHub Packages (buddy first, validate end-to-end).
7. Add RubyGems.org Trusted Publishing publish job (official), validate with configured publisher.
8. Add Ruby attestation + official gating.
