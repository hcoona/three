<!-- markdownlint-disable MD013 MD024 MD029 MD044 -->

# PLAN_6: Ruby gem release support (RubyGems Trusted Publishing only) + align/idempotent publish semantics for npm + PyPI

This plan supersedes `.AGENTS/PLAN_5.md`.

It is regenerated from:

- `.AGENTS/PLAN_REVIEW_5*.md`
- `.AGENTS/CLARIFY_PLAN_*.md`

and reconciled with the repository’s current workflows:

- `.github/workflows/release-resolve.yml`
- `.github/workflows/official.yml`
- `.github/workflows/buddy.yml`
- `.github/workflows/release-build-python.yml`
- `.github/workflows/release-build-node-pack.yml`

## Goal

1. Add Ruby gem project support (detect/build/publish) to the **root** release workflows (`official.yml`, `buddy.yml`) following the existing “artifact-first” architecture.
2. Make registry publishing **rerun-safe** via deterministic digest verification:
    - Node: npmjs.org + GitHub Packages npm registry (official), GitHub Packages (buddy)
    - Python: PyPI (official)
    - Ruby: RubyGems.org (official) + GitHub Packages RubyGems registry (official + buddy)
3. Enforce **buddy prerelease-only** consistently (Python/Node/Ruby) and keep the existing “buddy must not clobber an official GitHub Release” guard.
4. Apply a minimal reproducibility baseline across build workflows: `SOURCE_DATE_EPOCH`, `TZ`, `LC_ALL`.

## Non-goals

- Migrating projects into new `src/<lang>/...` subdirectories.
- Changing the tag format (`release/<project>/v<version>`).
- Changing the existing GitHub Release asset strategy (the repo intentionally uses `--clobber` for GitHub Release assets).
- Introducing any fallback credentials for RubyGems.org publishing.

## Hard requirements (non-negotiable)

### RubyGems.org publishing: Trusted Publishing (OIDC) only

- RubyGems.org publishes must use **Trusted Publishing** (OIDC) only.
- No long-lived RubyGems API key secrets.
- No `api-token` or `role-to-assume` inputs.

Source of truth: `CLARIFY_PLAN_0`, `CLARIFY_PLAN_3`.

**Note on “mechanical enforcement”:**

- No runtime “self-detection” mechanism is required (do not add extra guards that attempt to detect future fallback references).
- The requirement is satisfied by not wiring any fallback secrets/inputs.

Source of truth: `CLARIFY_PLAN_5_4`.

### GitHub Packages RubyGems registry: `github.token` only

- Publish to GitHub Packages RubyGems registry must use `${{ github.token }}` only.
- No PAT fallback.
- Maintainers must ensure package linkage / “Actions access” is configured.

Source of truth: `CLARIFY_PLAN_0`, `CLARIFY_PLAN_1`, `CLARIFY_PLAN_4`.

### Buddy safety (all project kinds)

- Buddy runs are **prerelease-only**.
- Buddy must not create/modify an existing GitHub Release with `prerelease=false`.

Source of truth: `CLARIFY_PLAN_1`.

### Idempotent reruns apply to _all_ publishes

For any registry publish step (official or buddy):

- If the version already exists, treat the rerun as success **only if** the remote artifact digest matches the local artifact digest.
- If the version exists but the digest differs, fail (never overwrite registries).

Source of truth: `CLARIFY_PLAN_4`.

### Pinning policy for new third-party actions

- Pin all **third-party** GitHub Actions introduced by this plan to a **full commit SHA**.

Source of truth: `CLARIFY_PLAN_5_3`.

## External references (authoritative)

- GitHub Packages: Working with the RubyGems registry
    - https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-rubygems-registry
- RubyGems Trusted Publishing
    - https://guides.rubygems.org/trusted-publishing/
    - Adding a publisher: https://guides.rubygems.org/trusted-publishing/adding-a-publisher
    - Pushing a new gem: https://guides.rubygems.org/trusted-publishing/pushing-a-new-gem
- RubyGems actions (references)
    - `rubygems/configure-rubygems-credentials`
    - `rubygems/release-gem` (reference only; not used because it rebuilds)

## Current baseline (what exists today)

- Resolver (`release-resolve.yml`) resolves:
    - `project`, `version`, `target`, `tag_name`, `package_dir`, `project_kind` (currently: python or node), and `is_wxt`.
- Build is artifact-first:
    - Python: `release-build-python.yml` produces `out/*`.
    - Node: `release-build-node-pack.yml` produces `out/gpr.tgz` and optionally `out/npmjs.tgz`.
- Official publishes:
    - Python to PyPI using OIDC (`pypa/gh-action-pypi-publish`).
    - Node to GitHub Packages and npmjs.org.
- Buddy publishes:
    - Node to GitHub Packages only.
- GitHub Releases:
    - Created via reusable `release-create-github-release.yml` and upload uses `--clobber` by design.

Known gaps (must be addressed):

1. Resolver currently does “Python else Node” detection (unsafe once Ruby is added).
2. Buddy has no prerelease-only guard.
3. Registry publishing is not rerun-safe (Node multi-registry and buddy GPR; PyPI partial-publish safety).
4. Build workflows do not yet apply the reproducibility baseline.

## Design changes

### 1) Harden project resolution + add Ruby detection

#### 1.1 Unified project discovery (single entrypoint; fd-based)

Unify project discovery into a single entrypoint script so the resolver has one consistent implementation for all kinds.

Add:

- `eng/scripts/find_project_path.py` (new unified entrypoint)

Implementation requirements:

- The script MUST use `fd` to enumerate candidate files, instead of Python `rglob`, to keep discovery fast and consistent.
- Discovery MUST consider all supported kinds:
    - Node: locate `package.json` files and match on top-level `name == <project>`.
    - Python: locate `pyproject.toml` files and match on `[project].name == <project>`.
    - Ruby: locate a file named exactly `<project>.gemspec` and treat its parent directory as the package dir.

Exit-code contract (single script):

- exit `0`: unique match; print exactly one JSON object on stdout (single line) so the resolver can parse it deterministically, e.g.:
    - `{ "package_dir": "...", "project_kind": "ruby" }`
- exit `2`: ambiguous (either within-kind ambiguity or cross-kind ambiguity); print details to stderr
- exit `3`: not found
- exit `1`: unexpected error

Notes:

- The `fd` invocation MUST exclude common large directories to keep runtime bounded (at minimum: `.git`, `node_modules`, `obj`, `bin`, `.venv`, `.tox`).
- Do not “pick the shortest path” when multiple matches exist; that hides ambiguity and breaks safety.
- The resolver MUST ensure `fd` is available on the runner via the repo-standard toolchain (`mise`), not via ad-hoc runner package installs.
- There is no fallback binary: the discovery script must invoke `fd`. If `fd` is not available on `PATH`, fail fast with a clear error.

Toolchain note:

- `fd` is part of the repository `mise` toolchain (declared in `.mise.toml` and locked in `.mise.lock`).

#### 1.2 Project kind selection in `.github/workflows/release-resolve.yml`

Replace the current “Python → else Node → else fail” logic with:

0. Ensure runner prerequisites for discovery:
    - Install and activate `mise`.
    - Install `fd` via `mise` and ensure it is on `PATH` for the discovery step.
        - Install **only** `fd` (do not run a full `mise install`): `mise install fd`.
    - Verify `fd` is available (e.g. `command -v fd`) and print `fd --version` for diagnostics.

1. Run the unified project discovery script once.
2. Capture and interpret its exit code and output:
    - exit `0`: parse both `project_kind` and `package_dir` from the JSON object on stdout (via `jq -r`) and proceed
    - exit `2`: fail immediately and print the ambiguity diagnostics from stderr
    - exit `3`: fail “Unknown project” and print diagnostics
    - exit `1`: fail “Unexpected error” and print diagnostics

Diagnostics requirement:

- The resolver must preserve the discovery script’s exit-code contract end-to-end.
- Capture discovery stderr to a temp file and print it on exit `2` (ambiguous) and exit `3` (not found), so maintainers can fix ambiguity intentionally.

Update resolver outputs to include:

- `project_kind`: `python|node|ruby`
- `is_prerelease`: `true|false`

Compatibility note:

- This is a breaking change to the resolver output contract.
- Callers (`official.yml`, `buddy.yml`) must be updated in the same PR.
- Any other entry workflow calling `release-resolve.yml` (if added in the future) must be updated together to avoid contract skew.

#### 1.3 Version validation per kind

Add:

- `eng/scripts/validate_rubygems_version.py`

Ruby rules (RubyGems-style versions):

- Grammar: `MAJOR.MINOR.PATCH` plus optional suffix dot segments.
- Must not contain `-` or `+`.
- If suffix segments exist:
    - dot-separated ASCII alphanumerics only (`[0-9A-Za-z]+`)
    - reject versions where the suffix is numeric-only (all suffix segments are numeric), e.g. `1.2.3.1`, `1.2.3.0.1`
    - require at least one letter in the suffix (e.g. `1.2.3.rc.0` is valid)
- `is_prerelease=true` iff the version has any segment beyond `MAJOR.MINOR.PATCH`.

Note:

- This prerelease definition intentionally matches RubyGems' prerelease notion (letters in the suffix). Keeping the validator requirement “suffix must include at least one letter” prevents accidental drift (e.g., allowing numeric-only suffixes) that would silently change buddy prerelease gating semantics.

Python rules:

- Continue using the existing PEP 440 subset validator.
- `is_prerelease=true` for PEP 440 pre-releases and dev releases.

Node rules:

- Continue using strict SemVer2 validator.
- `is_prerelease=true` iff the version contains a SemVer prerelease part.

### 2) Enforce buddy prerelease-only early

Update `.github/workflows/buddy.yml`:

- After `resolve`, add a guard step/job that fails if `needs.resolve.outputs.is_prerelease != 'true'`.

This must apply to Python, Node, and Ruby.

### 3) Reproducibility baseline across build workflows

For all build jobs (Python build, Node pack, Ruby build):

- `TZ=UTC`
- `LC_ALL=C.UTF-8`
- `SOURCE_DATE_EPOCH` set to the target commit timestamp

Digest-gated idempotency note:

- Artifact determinism for the supported build workflows has been manually confirmed.
- Digest-gated idempotency relies on byte-for-byte deterministic artifacts for a given commit.

Compute `SOURCE_DATE_EPOCH` from the checked-out target:

- `git show -s --format=%ct HEAD`

Apply _only_ to the build-producing command step (not the entire job) to minimize side effects.

Update:

- `.github/workflows/release-build-python.yml` before `uv build`
- `.github/workflows/release-build-node-pack.yml` before both `npm pack` invocations
- new Ruby build workflow before `gem build`

### 4) Ruby build: new reusable workflow

Add: `.github/workflows/release-build-ruby-gem.yml`.

Inputs:

- `target`, `project`, `package_dir`, `version`, `ruby_version`, `artifact_name`

Runner: `ubuntu-latest`.

Steps:

1. Checkout `ref: target`.
2. Install baseline apt dependencies (confirmed policy):
    - `texlive-latex-base`
    - `texlive-latex-recommended`
    - `texlive-fonts-recommended`
    - `dvisvgm`
    - `pdf2svg`
    - `poppler-utils`
    - `imagemagick`
    - `ghostscript`
3. Setup Ruby using `ruby/setup-ruby@v1` (pinned to commit SHA).
4. Run all Ruby commands in `working-directory: ${{ inputs.package_dir }}`.
5. Run Ruby quality checks when a Bundler context exists:
    - If a `Gemfile` exists in `package_dir`:
        - enable bundler cache
        - run `bundle exec standardrb`
        - run `bundle exec rspec`
    - If no `Gemfile` exists:
        - skip Bundler-based checks
        - emit a clear log message that checks were skipped due to missing Bundler context

Rationale:

- If a Bundler context is present, the release build MUST enforce these checks.
- If the package does not provide a Bundler context (e.g. missing `Gemfile`), these checks are skipped (with a clear log message) and the build proceeds to `gem build`.

6. Apply reproducibility baseline and build exactly one gem:
    - Build **directly** to the expected output path (do not rely on implicit naming + `mv`):
        - `gem build "${PROJECT}.gemspec" --output "${GITHUB_WORKSPACE}/out/<project>-<version>.gem"`
    - Strict requirement:
        - The workflow MUST rely on the `--output` flag.
        - Do not add any fallback path.
    - Fail if any additional `*.gem` file is produced during the build (defensive check).
7. Verify the built gem matches `project` and `version`:
    - `gem specification out/<project>-<version>.gem name version` (or equivalent) and compare.
8. Upload the flat `out/*` artifact.

### 5) Ruby publish: GitHub Packages RubyGems registry (buddy + official)

Add publish jobs that:

- Setup Ruby (publish jobs may avoid checkout, but still need `gem` CLI).
- Download `out/<project>-<version>.gem` artifact.
- Write credentials at the path reported by `gem env credentials`:
    - entry: `:github: Bearer ${{ github.token }}`
    - file mode `0600`
- Publish using explicit host + key:
    - `gem push --key github --host https://rubygems.pkg.github.com/<owner> out/<project>-<version>.gem`

Permissions:

- `packages: write`
- `contents: read`

Idempotent rerun behavior:

- Deterministic preflight (preferred):
    1. Attempt to fetch the remote gem first:
        - Create a temp directory and run all fetch operations inside it.
        - Define auth convention (must be consistent across jobs):
            - `<user>`: `${{ github.actor }}`
            - `<token>`: `${{ github.token }}`
        - Fetch using RubyGems CLI with an authenticated source URL:
            - `gem fetch <project> -v <version> --norc --silent --clear-sources --source "https://<user>:<token>@rubygems.pkg.github.com/<owner>/"`
        - Expected downloaded filename:
            - `<project>-<version>.gem`
        - Error classification requirement:
            - The implementation MUST capture `gem fetch` exit status and stderr.
            - Classify “not found” only when stderr clearly indicates an actual not-found condition (e.g., a RubyGems “could not find” / HTTP 404 style message).
            - Any auth/permission-related error must fail fast with an actionable message (most commonly: package linkage / Actions access is misconfigured).
            - Any transient network/server error must fail fast (rerun can retry).
            - Never “fall through to push” on an ambiguous `gem fetch` failure.
    2. If fetch succeeds (expected file exists):
        - compute SHA-256 of the fetched file and local `out/<project>-<version>.gem`
        - if equal: treat as already published (skip push)
        - else: fail
    3. If fetch did not produce the expected file, attempt push.

- Eventual-consistency discriminator:
    - If `gem push` fails with an “already exists / repush not allowed” error, retry `gem fetch` with bounded backoff, then compare digests.

### 6) Ruby publish: RubyGems.org (official only; Trusted Publishing only)

Add an official-only publish job that:

- `environment: rubygems`
- `permissions: id-token: write, contents: read, actions: read`
- Setup Ruby (`ruby/setup-ruby`, pinned)
- Configure credentials via `rubygems/configure-rubygems-credentials` (pinned) using Trusted Publishing only:
    - do not pass `api-token`
    - do not pass `role-to-assume`
    - set `trusted-publisher: true` explicitly to avoid default drift
- Publish from the artifact only:
    - `gem push out/<project>-<version>.gem`

Idempotent rerun behavior (RubyGems.org API):

- Query:
    - `GET https://rubygems.org/api/v2/rubygems/<project>/versions/<version>.json?platform=ruby`
- If the response indicates the version exists (HTTP 200):
    - compare its `sha` to the local SHA-256 (sha256 of the `.gem` file bytes, hex)
    - if equal: skip publish
    - else: fail
- If the response indicates the version does not exist (HTTP 404): attempt publish.
- For any other response status:
    - HTTP 429 and any 5xx: fail fast (do not treat as “not found”).
    - Any other non-200/non-404: fail fast.

Eventual consistency:

- Use `rubygems-await` to handle RubyGems.org eventual consistency (required for reliable idempotency and post-publish verification).
    - Install with an explicitly pinned version.
    - Run: `gem await <project>:<version>:ruby` (or equivalent) before attempting to fetch/verify availability after a publish or an “already exists” race.

### 7) Align Node publishing semantics (npmjs + GitHub Packages)

Make Node publishing rerun-safe for:

- official: npmjs.org + GitHub Packages
- buddy: GitHub Packages

Canonical digests:

- Remote digest: `dist.integrity` from `npm view`.
- Local digest: compute SRI (sha512) from the exact tarball file.

Local SRI computation rule:

- Compute SRI as `sha512-<base64(sha512(tarball_bytes))>` over the **tarball bytes** (do not hash extracted content).

Tarball mapping:

- GitHub Packages (GPR): compare against SRI computed from `out/gpr.tgz`.
- npmjs.org: compare against SRI computed from `out/npmjs.tgz`.

Package identity mapping (must be explicit and consistent):

- npmjs.org package name: `<project>` (unscoped).
- GitHub Packages (GPR) package name: `@<owner>/<project>` where `<owner>` is `${{ github.repository_owner }}` lowercased.

The idempotency check must query the correct registry using the corresponding name and version, e.g. `npm view <name>@<version> --registry <registry>`.

Failure-mode classification requirement for `npm view`:

- If `npm view` reports 404 / `E404`: treat as “not found”.
- If it reports 401/403 (or `E401`/`E403`): fail fast (auth/permission issue).
- Any other non-zero exit: fail fast (transient network/server issue).

Rules per registry:

- If `<name>@<version>` exists:
    - if integrity matches: treat as already published (skip publish)
    - else: fail
- If it does not exist: publish.

Operational tightening:

- Keep publish jobs checkout-free (download artifacts only).
- Move “Verify package is not private” into `release-build-node-pack.yml`.

### 8) Align Python publishing semantics (PyPI)

Make Python official publish rerun-safe and safe under partial publishes.

Canonical digests:

- Remote digest source: `GET https://pypi.org/pypi/<project>/json` (map `filename -> sha256` for `releases[<version>]`).
- Local digest: SHA-256 of each file under `out/*`.

Rules (file-level idempotency):

- For each local artifact file:
    - if `filename` exists remotely:
        - if sha256 matches: treat that file as already published
        - if sha256 differs: fail
    - if `filename` does not exist remotely: publish it

Error classification requirement for the PyPI project JSON query:

- If the project JSON endpoint returns HTTP 404, treat it as “project not found” (i.e., no remote files exist) and proceed to publish.
- Any other non-200 response must fail fast.

Publish strategy:

- Continue using `pypa/gh-action-pypi-publish` with OIDC.
- Run digest validation **before** invoking the publish action.
- Use `skip-existing: true` only after digest validation has proven that any existing files match.

### 9) Wire Ruby into entry workflows

Update `.github/workflows/official.yml`:

- Add `RUBY_VERSION` to `env:` (initial: `3.3`).
- Export `ruby_version` from the `versions` job.
- Add Ruby jobs gated by `project_kind == 'ruby'`:
    - `build-ruby` (calls `release-build-ruby-gem.yml`)
    - `publish-ruby-gpr` (GitHub Packages; idempotent)
    - `publish-ruby-rubygems` (RubyGems.org; Trusted Publishing only; idempotent)
    - `attest-ruby` (attest the built artifacts)
    - `release-ruby` (gated on both publish jobs + attestation + release notes)

Ruby attestation requirements:

- Use `actions/attest-build-provenance@v3` (first-party; no SHA pinning required).
- Attestation subject should be the build output artifact files (e.g. `out/*`).
- Required permissions should include (at minimum):
    - `attestations: write`
    - `id-token: write`
    - `contents: read`

Update `.github/workflows/buddy.yml`:

- Add `RUBY_VERSION` to `env:` and export it.
- Add the prerelease-only guard.
- Add Ruby jobs gated by `project_kind == 'ruby'`:
    - `build-ruby` (calls `release-build-ruby-gem.yml`)
    - `publish-ruby-gpr` (GitHub Packages; idempotent)
    - `release-ruby` (prerelease=true; depends on guard-non-clobber + publish)

Update `.github/workflows/release-resolve.yml`:

- Add Ruby detector execution and Ruby version validation.
- Add `is_prerelease` output.
- Update caller input descriptions to mention Ruby version rules.

## Maintainer setup checklist (required)

### RubyGems.org Trusted Publisher configuration

For each gem published to RubyGems.org:

- Configure a trusted publisher:
    - Owner: `hcoona`
    - Repository: `three`
    - Workflow filename: `official.yml`
    - Environment: `rubygems`

The GitHub Actions environment named `rubygems` must allow unattended automation:

- No required reviewers / no manual approvals.
- Avoid wait timers that would block official releases.

### GitHub Packages RubyGems registry prerequisites

For each gem:

- Ensure gemspec metadata includes:
    - `github_repo = https://github.com/hcoona/three.git`
- Ensure GitHub Packages “Manage Actions access” (or inheritance) allows workflows from `hcoona/three` to publish.

## Acceptance criteria

### Resolver / safety

- Resolver can uniquely resolve a Python/Node/Ruby project.
- Resolver fails clearly on:
    - within-kind ambiguity
    - cross-kind ambiguity
    - unknown project
- Resolver outputs `is_prerelease` and buddy enforces it.

### Ruby

- Build produces exactly one `.gem` artifact: `out/<project>-<version>.gem`.
- If no `Gemfile` exists, Bundler-based quality checks are skipped and the build logs that fact explicitly.
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

### Node

- Official publishes to both registries rerun-safely.
- Buddy publishes to GitHub Packages rerun-safely.
- Digest comparisons use the correct tarball per registry (`gpr.tgz` vs `npmjs.tgz`).

### Python

- Official PyPI publish is rerun-safe, including partial publish scenarios.

### Alignment

- Python/Node/Ruby builds set `SOURCE_DATE_EPOCH`, `TZ`, `LC_ALL` (scoped to build-producing steps).

## Risks and mitigations

- GitHub Packages RubyGems auth inconsistencies (docs sometimes emphasize PATs):
    - Mitigation: require `github_repo` metadata and “Manage Actions access” configuration; do not add fallback secrets.

- Registry eventual consistency:
    - Mitigation: bounded retries for “push says exists but fetch doesn’t” (GPR Ruby), plus `rubygems-await` (pinned) for RubyGems.org post-publish checks.

## Implementation sequence (incremental, testable)

Approach A (adopted): introduce the unified detector first and switch the resolver to it (do not retrofit/extend the legacy per-kind detectors).

1. Add `eng/scripts/find_project_path.py` (fd-based unified detector) and wire it into `release-resolve.yml`.
    - Ensure `fd` is installed via `mise install fd` in the resolver job.
    - Emit `project_kind` and `package_dir` from the unified script.
2. Add kind-specific version validation (Python / Node / Ruby) and emit `is_prerelease` from `release-resolve.yml`.
3. Update callers (`official.yml`, `buddy.yml`) in the same PR to consume the new outputs.
4. Add buddy prerelease-only guard in `buddy.yml`.
    - Guard must use `needs.resolve.outputs.is_prerelease` (do not re-parse version strings in `buddy.yml`).
5. Add reproducibility baseline to existing build workflows (Python + Node), and apply it to the new Ruby build workflow.
6. Add `.github/workflows/release-build-ruby-gem.yml` and wire Ruby build into buddy first.
7. Add Ruby buddy publish to GitHub Packages RubyGems (with idempotency).
8. Add Ruby official publish jobs:
    - RubyGems.org (Trusted Publishing only)
    - GitHub Packages RubyGems (idempotent)
    - attestation + release gating
9. Add Node buddy and Node official idempotent publish logic (registry-specific tarball mapping).
10. Add Python official rerun-safe publish logic (file-level idempotency).
11. Update workflow input descriptions and maintainer-facing docs if needed.
