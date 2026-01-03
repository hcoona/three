# Release workflow refactor plan v5 (root workflows only)

<!-- markdownlint-disable MD013 -->

This plan supersedes `PLAN_3.md` and incorporates the confirmed constraints in:

- `.AGENTS/CLARIFY_0.md`
- `.AGENTS/CLARIFY_1.md`
- `.AGENTS/CLARIFY_2.md`
- `.AGENTS/CLARIFY_3.md`
- `.AGENTS/CLARIFY_4.md`

It also addresses the gaps called out in `.AGENTS/PLAN_REVIEW_4.md`.

Scope reminder: **root workflows only** under `/.github/workflows/*.yml`.

## Scope

- Applies **only** to root workflows under `/.github/workflows/*.yml`.
- Do **not** change any nested `.github` directories under subprojects.
- This refactor may introduce new root reusable workflows and/or root composite actions.

## Goals

1. Reduce duplication between `official.yml` and `buddy.yml`.
2. Standardize a stable release artifact contract:
    - `${GITHUB_WORKSPACE}/out` is the only release asset directory.
    - `out/*` is the complete release asset set.
    - Flat layout only (no subdirectories).
3. Preserve “build at target commit” semantics.
4. Preserve publishing identity constraints for Trusted Publishing:
    - PyPI Trusted Publishing stays in `official.yml` under environment `pypi`.
    - npmjs Trusted Publishing stays in `official.yml` under environment `npmjs`.
5. Enforce buddy non-clobber: buddy must never modify an existing GitHub Release for the same tag if that release is `prerelease=false`.

## Non-goals

- Changing tag naming (`release/<project>/v<version>`), version validation rules, registries, or external publishing behavior.
- Introducing an orchestrator that calls `official.yml` via `workflow_call`.
- Rewriting `release-prepare-release-notes.yml` or `release-create-github-release.yml`.
- Adding a separate runbook.

## Hard contracts (must hold after refactor)

### A) Artifact contract

- The only release asset directory is `${GITHUB_WORKSPACE}/out`.
- The full upload payload is `out/*`.
- Flat layout is mandatory.
- Build/pack workflows must populate `out/` and upload artifacts from `out/*`.
- `release-create-github-release.yml` will fail if it sees directories under `out/`.

### B) Reusable workflow output stability

- Boolean-like outputs must be lowercase strings: `'true' | 'false'`.
- Enum-like outputs must keep a stable value set (e.g. `project_kind: 'python' | 'node'`).

### C) Tool versions source of truth

- `PYTHON_VERSION`, `NODE_VERSION`, `PNPM_VERSION` remain defined in the entry workflows (`official.yml` / `buddy.yml`).
- All reusable workflows must accept required `workflow_call` inputs for the versions they need (no defaults).

## Decisions (confirmed)

### 1) Buddy non-clobber policy (including drafts)

- If a GitHub Release exists for the tag and `prerelease=false`, buddy **fails fast**, regardless of `draft`.

### 2) Buddy non-clobber guard API contract

- Standardize on:
    - `gh api repos/${GITHUB_REPOSITORY}/releases/tags/${TAG_NAME}`

### 3) Dist-tag derivation (do not parse SemVer)

Dist-tag derivation must be identical in buddy and official, and based on NBGV metadata:

- `prerelease = dotnet tool run nbgv get-version -v PrereleaseVersionNoLeadingHyphen -p <PACKAGE_DIR>`
- If `prerelease` is empty:
    - If `version` is not a prerelease: dist-tag = `latest`
    - If `version` is a prerelease (contains `-`): **fail fast** (to avoid publishing prereleases under `latest`)
- Else:
    - `channel = prerelease.split('.', 1)[0].lower()`
    - Validate: `channel` must match `^[a-z0-9][a-z0-9-]*$`
    - If invalid/empty: **fail fast** with guidance to update version config (usually `version.json`) so prereleases start with a stable channel label (e.g. `-beta.{height}`, `-rc.{height}`)

### 4) WXT zip collection

- Only collect `.output/*.zip` (shallow; no recursion).
- Do **not** assume any filename pattern.
- Copy all zips into `${GITHUB_WORKSPACE}/out/`.
- Fail fast on basename collisions, and print actionable diagnostics:
    - conflicting basenames
    - each source path
    - reminder: only `.output/*.zip` is supported

### 5) Attestation placement

- Build-only reusable workflows must **not** run GitHub attestations.
- `official.yml` runs a dedicated job that downloads the dist artifact and attests `out/*`.
- `buddy.yml` does not attest.

### 6) Reusable workflow context differences (`workflow_call`)

- `release-resolve.yml` must not branch on `github.event_name` to detect tag vs manual.
- Entry workflows must pass an explicit `source` input (`tag | manual`) and the required mode-specific inputs.

### 7) `run_url` must point to entry workflow run

- Compute `run_url` in `official.yml` / `buddy.yml` as:
    - `https://github.com/${GITHUB_REPOSITORY}/actions/runs/${GITHUB_RUN_ID}`
- Pass it through to `release-prepare-release-notes.yml` (and to `release-resolve.yml` only as a pass-through if needed).

### 8) Node tarball identity after download

To avoid guessing which `.tgz` to publish, standardize deterministic tarball names produced by the pack workflow:

- `${GITHUB_WORKSPACE}/out/npmjs.tgz` (publish target: `registry.npmjs.org`)
- `${GITHUB_WORKSPACE}/out/gpr.tgz` (publish target: `npm.pkg.github.com`)

## Current issues to fix (carried forward)

1. Remove `dist_dir` / `dist_glob` outputs from the reusable resolve workflow and stop consuming them in root workflows.
2. Add buddy non-clobber guard in `buddy.yml` **before** any job that can modify an existing release.
3. Replace all npm dist-tag SemVer parsing with the NBGV metadata algorithm.
4. Buddy Node must pack first, publish from the packed `.tgz`, and upload the same `.tgz`.
5. WXT zip collection must be shallow (`.output/*.zip`), name-agnostic, and collision-safe.
6. Build reusable workflows must not generate attestations.
7. Official attests `out/*` in a dedicated job; buddy does not.
8. Node quality checks run by default in both flows.

## Target architecture

Entry workflows remain the policy layer:

- `/.github/workflows/official.yml`
- `/.github/workflows/buddy.yml`

Reusable workflows (building blocks):

- Existing:
    - `release-prepare-release-notes.yml`
    - `release-create-github-release.yml`
- New (introduced by this refactor):
    - `release-resolve.yml` (reusable)
    - `release-build-python.yml` (reusable; build only)
    - `release-build-wxt.yml` (reusable; build only)
    - `release-build-node-pack.yml` (reusable; pack only; runs Node quality checks)

Optional (only if it reduces duplication cleanly):

- Root composite actions under `/.github/actions/*` for shared toolchain setup and bash helpers.

## Reusable workflow contracts

### A) `release-resolve.yml` (new, reusable)

Purpose: single source of truth for resolving:

- `project`
- `version` (validated/normalized per kind)
- `tag_name`
- `target` (resolved commit SHA)
- `package_dir`
- `project_kind` (`python` | `node`)
- `is_wxt` (`'true' | 'false'`)
- `has_changelog` (`'true' | 'false'`)
- `changelog` (path)
- `release_title`
- `run_url` (pass-through)
- `force_update_tag` (`'true' | 'false'`)

Hard requirements:

- Must **not** emit `dist_dir` or `dist_glob`.
- Must `git checkout --detach <target>` before reading repository files.
- Must normalize `force_update_tag`:
    - When `source=tag`: output **always** `'false'`.
    - When `source=manual`: output `'true'` iff input `force_update_tag` is true.
- Must not infer mode from `github.event_name`.

Inputs (required unless stated otherwise):

- `source`: `tag | manual`
- `run_url`: string (computed in entry workflow; pass-through)
- `python_version`: string (for `uv` helper scripts)
- When `source=tag`:
    - `ref_name`: string (caller passes `${{ github.ref_name }}`)
    - `ref`: string (caller passes `${{ github.ref }}`)
- When `source=manual`:
    - `project`: string
    - `version`: string
    - `target`: optional string (defaults to HEAD)
    - `force_update_tag`: optional boolean

### B) `release-build-python.yml` (new, reusable; build only)

Inputs (required):

- `target`, `package_dir`, `version`
- `python_version`
- `artifact_name`

Behavior:

- Checkout at `target`.
- Build with `uv build --out-dir ${GITHUB_WORKSPACE}/out`.
- Verify built version via `eng/scripts/verify_python_artifact_version.py`.
- Upload artifact `${artifact_name}` with path `out/*`.

Must not:

- Publish to PyPI.
- Run GitHub attestations.

### C) `release-build-wxt.yml` (new, reusable; build only)

Inputs (required):

- `target`, `project`, `package_dir`, `version`
- `node_version`, `pnpm_version`
- `artifact_name`

Behavior:

- Checkout at `target`.
- Install deps.
- Build WXT zips using the package’s scripts or direct WXT invocation (matching today’s behavior).
- Collect artifacts:
    - Only `.output/*.zip` (shallow)
    - Copy all zips into `${GITHUB_WORKSPACE}/out/`
    - Fail on basename collisions with actionable diagnostics
- Verify `NpmPackageVersion` matches `version`.
- Upload artifact `${artifact_name}` with path `out/*`.

Must not:

- Run GitHub attestations.

### D) `release-build-node-pack.yml` (new, reusable; pack only)

Inputs (required):

- `target`, `project`, `package_dir`, `version`
- `python_version`, `node_version`, `pnpm_version`
- `artifact_name`
- `pack_mode`: enum `gpr-only | both`

Behavior:

- Checkout at `target`.
- Install deps.
- Verify `NpmPackageVersion` matches `version`.
- Run quality checks by default (skipping via `--if-present` is expected):
    - `pnpm --filter <project> --if-present lint`
    - `pnpm --filter <project> --if-present typecheck`
    - `pnpm --filter <project> --if-present test`
    - `pnpm --filter <project> --if-present build`
- Produce tarballs into `${GITHUB_WORKSPACE}/out`:
    - Always produce `gpr.tgz` (scoped name) by:
        - applying scope/name adjustment via `eng/scripts/prepare_npm_publish.py`
        - packing
        - renaming the produced tarball to `${GITHUB_WORKSPACE}/out/gpr.tgz`
        - restoring scope/name changes
    - If `pack_mode=both`, also produce `npmjs.tgz` (public name) by packing the restored (unscoped) package and renaming to `${GITHUB_WORKSPACE}/out/npmjs.tgz`.

Must not:

- Publish to GPR or npmjs.
- Run GitHub attestations.

## Entry workflow behavior

### `official.yml`

- Calls `release-resolve.yml`.
    - For tag runs: `source=tag`, pass `ref_name`, `ref`, `run_url`.
    - For manual runs: `source=manual`, pass `project`, `version`, optional `target`, optional `force_update_tag`, plus `run_url`.
- Calls `release-prepare-release-notes.yml` (with the **entry** `run_url`).

Then, based on `project_kind` and `is_wxt`:

1. Python:
    - Build: call `release-build-python.yml` → uploads `official-<project>-dist`.
    - Publish: job in `official.yml` under environment `pypi`:
        - download `official-<project>-dist`
        - publish with `pypa/gh-action-pypi-publish`
    - Attest: dedicated job downloads `official-<project>-dist` and runs `actions/attest-build-provenance@v3` with `subject-path: ${GITHUB_WORKSPACE}/out/*`.
    - Release: call `release-create-github-release.yml`.

2. Node (non-WXT):
    - Pack: call `release-build-node-pack.yml` with `pack_mode=both` → uploads `official-<project>-dist` containing `gpr.tgz` and `npmjs.tgz`.
    - Publish: job in `official.yml` under environment `npmjs`:
        - download `official-<project>-dist`
        - compute dist-tag via NBGV metadata algorithm
        - publish to GPR **from** `${GITHUB_WORKSPACE}/out/gpr.tgz`
        - publish to npmjs **from** `${GITHUB_WORKSPACE}/out/npmjs.tgz` (Trusted Publishing)
    - Attest: dedicated job downloads `official-<project>-dist` and attests `out/*`.
    - Release: call `release-create-github-release.yml`.

3. Node (WXT):
    - Build: call `release-build-wxt.yml` → uploads `official-<project>-dist`.
    - Attest: dedicated job downloads and attests `out/*`.
    - Release: call `release-create-github-release.yml`.

### `buddy.yml`

- Calls `release-resolve.yml` with `source=manual` and the entry `run_url`.
- Calls `release-prepare-release-notes.yml`.

Add a required preflight guard job before any job that could modify the GitHub Release:

- Query by tag using `gh api repos/${GITHUB_REPOSITORY}/releases/tags/${TAG_NAME}`.
    - If 404: allow.
    - Else if `prerelease == false`: **fail fast** (even if `draft == true`).
    - Else (`prerelease == true`): allow.

Then, based on `project_kind` and `is_wxt`:

1. Python:
    - Build: call `release-build-python.yml` to upload `buddy-<project>-dist`.
    - No PyPI publishing.
    - No GitHub attestation.
    - Release: call `release-create-github-release.yml` with `prerelease: true`.

2. Node (non-WXT):
    - Pack: call `release-build-node-pack.yml` with `pack_mode=gpr-only` to upload `buddy-<project>-dist` containing `gpr.tgz`.
    - Publish (GPR): a job in `buddy.yml`:
        - download `buddy-<project>-dist`
        - compute dist-tag via NBGV metadata algorithm
        - publish to GPR **from** `${GITHUB_WORKSPACE}/out/gpr.tgz`
    - No GitHub attestation.
    - Release: call `release-create-github-release.yml` with `prerelease: true`.

3. Node (WXT):
    - Build: call `release-build-wxt.yml` to upload `buddy-<project>-dist`.
    - No GitHub attestation.
    - Release: call `release-create-github-release.yml` with `prerelease: true`.

## Permissions model (summary)

- Build reusable workflows: `contents: read`.
- Jobs that download artifacts when token permissions are restricted: `actions: read`.
- `release-create-github-release.yml`: `contents: write`, `actions: read`.
- Official publishing jobs:
    - PyPI: `environment: pypi`, `id-token: write`.
    - npmjs: `environment: npmjs`, `id-token: write`.
    - GPR publish: `packages: write`.
- Official attestation job: `id-token: write`, `attestations: write`.

## Migration plan (phased)

### Phase 1 — Introduce `release-resolve.yml` and remove `dist_dir/dist_glob` consumption

- Add `release-resolve.yml` with explicit `source` input contract.
- Update `official.yml` / `buddy.yml` to use it.
- Remove all references to `dist_dir` / `dist_glob` outputs.

Acceptance:

- Root workflows validate.
- No root workflow reads `dist_dir` or `dist_glob` outputs.

### Phase 2 — Buddy non-clobber guard

- Add preflight guard job in `buddy.yml` using `gh api .../releases/tags/...`.

Acceptance:

- If an existing non-prerelease release exists for the tag, buddy fails before any release modification.

### Phase 3 — Python build reuse + official publish split

- Add `release-build-python.yml`.
- Official: build via reusable workflow, publish in `official.yml` after downloading artifact.

Acceptance:

- Built artifacts match expected version.

### Phase 4 — WXT build reuse + artifact collection fix

- Add `release-build-wxt.yml`.
- Enforce `.output/*.zip` only and collision-safe copy.

Acceptance:

- No recursion under `.output/**`.
- Collision failure prints full diagnostics.

### Phase 5 — Node pack reuse + dist-tag via NBGV

- Add `release-build-node-pack.yml`.
- Official: publish from `out/gpr.tgz` and `out/npmjs.tgz`, dist-tag from NBGV.
- Buddy: call pack workflow too (enforces quality checks), publish to GPR from `out/gpr.tgz`.

Acceptance:

- No SemVer parsing remains for dist-tag.
- Buddy publishes from packed tarball(s) and uploads the same tarball.

### Phase 6 — Official GitHub attestation job

- Add a dedicated job in `official.yml` that downloads `official-<project>-dist` and attests `out/*`.
- Remove attestation steps from all build workflows.

Acceptance:

- Official produces GitHub attestations for release assets.
- Buddy produces none.

## Acceptance criteria (final)

1. Root workflows no longer consume `dist_dir` / `dist_glob` outputs.
2. Buddy non-clobber enforced exactly:
    - release exists and `prerelease=false` → buddy fails fast
    - release missing → allow
    - release exists and `prerelease=true` → allow
3. Dist-tag derived via NBGV metadata (same algorithm in buddy and official).
4. Buddy Node flow publishes from packed tarball and uploads the same tarball.
5. WXT zip collection:
    - only `.output/*.zip`
    - copy all zips
    - fail on basename collisions with actionable diagnostics
6. Build reusable workflows do not run GitHub attestations.
7. Official runs GitHub attestation for `out/*`; buddy does not.
8. Node quality checks run by default in both flows.

## Rollback plan

All changes are isolated to root workflow YAML (and optional root composite actions). Rollback is a straightforward revert of those workflow changes.
