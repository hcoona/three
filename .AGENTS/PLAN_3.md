# Release workflow refactor plan v3 (root workflows only)

This plan replaces `PLAN_1.md` and resolves the gaps identified in `PLAN_REVIEW_2.md`, while conforming to the decisions in `CLARIFY_0.md` and `CLARIFY_1.md` and explicitly deciding the remaining open questions in `CLARIFY_2.md`.

## Scope

- Applies **only** to root workflows under `/.github/workflows/*.yml`.
- Do **not** change any nested `.github` directories under subprojects.
- This refactor is allowed to introduce new root reusable workflows and/or root composite actions.

## Goals

1. Reduce duplication between `official.yml` and `buddy.yml`.
2. Standardize a stable artifact contract: `${GITHUB_WORKSPACE}/out` is the only release asset directory, and `out/*` is the full asset set (flat layout).
3. Preserve “build at target commit” semantics.
4. Preserve publishing identity constraints for Trusted Publishing:
    - PyPI Trusted Publishing stays in `official.yml` under environment `pypi`.
    - npm Trusted Publishing stays in `official.yml` under environment `npmjs`.
5. Enforce buddy non-clobber: buddy must never modify an existing GitHub Release for the same tag if that release is `prerelease=false`.

## Non-goals

- Changing tag naming (`release/<project>/v<version>`), version validation rules, registries, or external publishing behavior.
- Introducing an orchestrator that calls `official.yml` via `workflow_call`.
- Rewriting `release-prepare-release-notes.yml` or `release-create-github-release.yml`.

## Decisions (including CLARIFY_2 open questions)

### 1) Buddy non-clobber policy: draft releases

If a release exists for the tag and `prerelease=false`, buddy **fails fast**, regardless of `draft`.

Rationale: simplest and safest rule.

### 2) Buddy non-clobber guard: API contract

Standardize on:

- `gh api repos/${GITHUB_REPOSITORY}/releases/tags/${TAG_NAME}`

Rationale: explicit, can query `prerelease`, `draft`, `id`.

### 3) npm dist-tag derivation and validation

Dist-tag is derived from NBGV metadata (not SemVer parsing), with identical rules in buddy and official.

- Source: `dotnet tool run nbgv get-version -v PrereleaseVersionNoLeadingHyphen -p <PACKAGE_DIR>`
- If empty: `latest`
- Else: `channel = first segment before '.'` (if there is no '.', the full value is the channel)
- Normalize: `channel = channel.lower()`
- Validation: `channel` must match:
    - `^[a-z0-9][a-z0-9-]*$`
- If invalid/empty: fail fast with guidance to update version configuration (typically `version.json`) so the prerelease begins with a stable channel label (e.g., `-beta.{height}`, `-rc.{height}`).

Notes:

- This deliberately rejects underscores and uppercase. If the repository later decides to broaden allowed tags, the regex can be expanded in one place.

### 4) NBGV prerelease formats: dot required?

No. Accept both:

- `beta.123` → `beta`
- `beta1` → `beta1`

As long as the derived channel is valid per the dist-tag validation rule.

### 5) Official GitHub asset attestation placement

Use a dedicated job (Option C):

- Download the `official-<project>-dist` artifact
- Attest `out/*`

Policy:

- `official.yml`: **does** attest GitHub release assets (`out/*`)
- `buddy.yml`: **does not** attest GitHub release assets

### 6) WXT zip collisions diagnostics

Fail fast on basename collisions when copying into a flat `out/`, and print:

- Each conflicting basename
- Its source path(s)
- A reminder that only `.output/*.zip` is supported (no recursion)

## Current issues to fix (must be addressed by this plan)

From `PLAN_REVIEW_2.md`:

1. Remove `dist_dir` / `dist_glob` outputs from the new `release-resolve.yml`, and update all callers to stop consuming them.
2. Implement buddy non-clobber guard in `buddy.yml` **before** calling `release-create-github-release.yml`.
3. Replace all npm dist-tag SemVer parsing with the NBGV metadata algorithm above.
4. Buddy Node must `pack` first, then publish **from that `.tgz`**, and upload the same `.tgz` as the GitHub Release asset.
5. WXT zip collection must be shallow (`.output/*.zip`), copy all zips, and fail on basename collisions.
6. Reusable build workflows must not generate attestations.
7. Node quality checks must run by default in both official and buddy flows (using `--if-present`).

## Target architecture

Entry workflows remain the policy layer:

- `/.github/workflows/official.yml`
- `/.github/workflows/buddy.yml`

Reusable building blocks:

- Existing:
    - `release-prepare-release-notes.yml`
    - `release-create-github-release.yml`
- New (introduced by this refactor):
    - `release-resolve.yml` (reusable)
    - `release-build-python.yml` (reusable; build only)
    - `release-build-wxt.yml` (reusable; build only)
    - `release-build-node-pack.yml` (reusable; pack only, runs quality checks)

Optional (only if it reduces duplication cleanly):

- Root composite actions under `/.github/actions/*` for toolchain setup (dotnet + uv + pnpm + node) and for shared bash helpers.

### Artifact contract (hard contract)

- The only release asset directory is: `${GITHUB_WORKSPACE}/out`.
- The full asset set is: `out/*`.
- Flat layout is mandatory (no subdirectories).
- Callers must treat `out/*` as the complete release upload payload.

## Reusable workflow contracts

### A) `release-resolve.yml` (new, reusable)

Purpose: single source of truth for resolving:

- `project`
- `version` (normalized per kind)
- `tag_name`
- `target` (resolved SHA)
- `package_dir`
- `project_kind` (`python` | `node`)
- `is_wxt` (`true` | `false`, string)
- `has_changelog` (`true` | `false`, string)
- `changelog` (path)
- `release_title`
- `run_url`
- `force_update_tag` (`true` | `false`, string)

Hard requirements:

- Must not emit `dist_dir` or `dist_glob`.
- Boolean-like outputs must be lowercase strings: `"true" | "false"`.
- Must `git checkout --detach <target>` before reading repository files.
- Must normalize `force_update_tag` as a string output:
    - Tag-triggered runs (`push` on `release/*/v*`): always `"false"` (tags are immutable by policy).
    - Manually triggered runs (`workflow_dispatch`): `"true"` iff the caller input is true.

Tool version inputs:

- Required inputs: `python_version` (and any other versions it actually needs).

### B) `release-build-python.yml` (new, reusable; build only)

Inputs (required):

- `target`, `package_dir`, `version`, `python_version`, `artifact_name`

Behavior:

- Checkout at `target`
- Build with `uv build --out-dir ${GITHUB_WORKSPACE}/out`
- Verify version via `verify_python_artifact_version.py`
- Upload artifact `${artifact_name}` with path `out/*`

Must not:

- Publish to PyPI
- Run GitHub attestations

### C) `release-build-wxt.yml` (new, reusable; build only)

Inputs (required):

- `target`, `project`, `package_dir`, `version`, `node_version`, `pnpm_version`, `artifact_name`

Behavior:

- Checkout at `target`
- Build WXT zips via the package scripts or direct WXT invocation (same logic as today)
- Collect artifacts:
    - Only `.output/*.zip` (shallow)
    - Copy all zips
    - Fail on basename collisions when copying into `${GITHUB_WORKSPACE}/out`
- Verify `NpmPackageVersion` matches `version`
- Upload artifact `${artifact_name}` with path `out/*`

Must not:

- Run GitHub attestations

### D) `release-build-node-pack.yml` (new, reusable; pack only)

Inputs (required):

- `target`, `project`, `package_dir`, `version`, `python_version`, `node_version`, `pnpm_version`, `artifact_name`

Behavior:

- Checkout at `target`
- Install deps
- Verify `NpmPackageVersion` matches `version`
- Run quality checks by default (always; skipping via `--if-present` is expected):
    - `pnpm --filter <project> --if-present lint`
    - `pnpm --filter <project> --if-present typecheck`
    - `pnpm --filter <project> --if-present test`
    - `pnpm --filter <project> --if-present build`
- Produce tarballs into `${GITHUB_WORKSPACE}/out`:
    - One tarball for npmjs publish (public name)
    - One tarball for GPR publish (scoped name), if required by current official flow

Implementation note:

- Continue using `eng/scripts/prepare_npm_publish.py` to adjust the scoped name for the GPR tarball, and restore after packing.

Must not:

- Publish to GPR or npmjs
- Run GitHub attestations

## Entry workflow behavior

### `official.yml`

- Calls `release-resolve.yml`.
- Calls `release-prepare-release-notes.yml`.

Then, based on `project_kind` and `is_wxt`:

1. Python:
    - Build: call `release-build-python.yml` to upload `official-<project>-dist`.
    - Publish: a job in `official.yml` under environment `pypi` downloads that artifact and runs `pypa/gh-action-pypi-publish`.
    - Attest: dedicated job downloads `official-<project>-dist` and runs `actions/attest-build-provenance@v3` with `subject-path: ${GITHUB_WORKSPACE}/out/*`.
    - Release: call `release-create-github-release.yml`.

2. Node (non-WXT):
    - Pack: call `release-build-node-pack.yml` to upload `official-<project>-dist`.
    - Publish: a job in `official.yml` under environment `npmjs` downloads that artifact and publishes:
        - GPR publish **from the packed `.tgz`**
        - npmjs publish **from the packed `.tgz`** (Trusted Publishing)
        - Dist-tag derived via NBGV metadata (shared algorithm)
    - Attest: dedicated job downloads `official-<project>-dist` and attests `out/*`.
    - Release: call `release-create-github-release.yml`.

3. Node (WXT):
    - Build: call `release-build-wxt.yml` to upload `official-<project>-dist`.
    - Attest: dedicated job downloads and attests `out/*`.
    - Release: call `release-create-github-release.yml`.

### `buddy.yml`

- Calls `release-resolve.yml`.
- Calls `release-prepare-release-notes.yml`.

Add a required preflight guard job before any job that could modify the GitHub Release:

- If a release exists for `tag_name` and `prerelease=false`, fail fast.
- Otherwise allow.

Then, based on `project_kind` and `is_wxt`:

1. Python:
    - Build: call `release-build-python.yml` to upload `buddy-<project>-dist`.
    - No PyPI publishing.
    - No GitHub attestation.
    - Release: call `release-create-github-release.yml` with `prerelease: true`.

2. Node (non-WXT):
    - Publish job remains in `buddy.yml` (GPR publish), but is refactored to:
        - Compute dist-tag via NBGV metadata (shared algorithm)
        - Apply scope/name adjustment
        - `npm pack` exactly once into `${GITHUB_WORKSPACE}/out`
        - Publish **from that `.tgz`**
        - Upload `buddy-<project>-dist` containing the same `.tgz`
    - No GitHub attestation.
    - Release: call `release-create-github-release.yml` with `prerelease: true`.

3. Node (WXT):
    - Build: call `release-build-wxt.yml` to upload `buddy-<project>-dist`.
    - No GitHub attestation.
    - Release: call `release-create-github-release.yml` with `prerelease: true`.

## Buddy non-clobber guard: exact algorithm

In `buddy.yml`, before calling `release-create-github-release.yml`:

- Query release by tag:
    - If 404: allow
    - Else read `prerelease` (and `draft` for diagnostics)
- If `prerelease == false`: fail with an error explaining:
    - An official (non-prerelease) release already exists for this tag
    - Buddy is blocked to prevent clobbering assets or modifying release metadata

This guard must run before any step that could:

- edit release notes/title/target
- upload assets (including clobber)
- change prerelease state

## Dist-tag derivation helper (shared)

Both `official.yml` and `buddy.yml` must use the same helper logic:

- `prerelease = nbgv PrereleaseVersionNoLeadingHyphen`
- If empty → `latest`
- Else → `channel = prerelease.split('.', 1)[0].lower()`
- Validate `channel` with `^[a-z0-9][a-z0-9-]*$`
- Fail if invalid, with guidance to adjust version config.

## Permissions model (summary)

- Build reusable workflows: `contents: read` only.
- Download artifacts within release/publish jobs: requires `actions: read` when token permissions are restricted.
- `release-create-github-release.yml`: `contents: write`, `actions: read`.
- Official publishing jobs:
    - PyPI: `environment: pypi`, `id-token: write`
    - npmjs: `environment: npmjs`, `id-token: write`
    - GPR publish: `packages: write`
- Official attestation job: `id-token: write`, `attestations: write`.

## Migration plan (phased)

### Phase 1 — Resolve extraction + output contract cleanup

- Add `release-resolve.yml`.
- Update `official.yml` and `buddy.yml` to use it.
- Remove all usage of `dist_dir` / `dist_glob`.

Acceptance:

- All root workflows pass validation.
- No workflow references `dist_dir` or `dist_glob`.

### Phase 2 — Buddy non-clobber guard

- Add preflight guard job in `buddy.yml` using `gh api .../releases/tags/...`.

Acceptance:

- If a non-prerelease release exists for the tag, buddy fails before any release modification.

### Phase 3 — Python build reuse

- Add `release-build-python.yml` and wire both entry workflows.
- Keep PyPI publish in `official.yml` (download artifact → publish).

Acceptance:

- Built artifacts match expected version and are uploaded as `*-dist`.

### Phase 4 — WXT build reuse + artifact collection fix

- Add `release-build-wxt.yml`.
- Switch WXT zip collection to `.output/*.zip` only, copy all zips, fail on collisions.

Acceptance:

- No recursion under `.output/**`.
- Collision failure prints full diagnostics.

### Phase 5 — Node packaging alignment + dist-tag via NBGV

- Add `release-build-node-pack.yml` for official pack.
- Update official publish to publish from packed `.tgz` files and compute dist-tag via NBGV.
- Update buddy Node publish to pack first and publish from that `.tgz`.

Acceptance:

- No SemVer parsing for dist-tag remains.
- Buddy uploads the same `.tgz` that it published.

### Phase 6 — Official GitHub attestation job

- Add a dedicated attestation job in `official.yml` that downloads `official-<project>-dist` and attests `out/*`.
- Remove attestation steps from all build workflows.

Acceptance:

- Official produces GitHub attestations for release assets.
- Buddy produces none.

## Acceptance criteria (final)

1. Root workflows under `/.github/workflows/*.yml` no longer consume `dist_dir` / `dist_glob`.
2. Buddy non-clobber enforced exactly:
    - release exists and `prerelease=false` → buddy fails fast
    - release missing → allow
    - release exists and `prerelease=true` → allow
3. Dist-tag derived via NBGV metadata (same algorithm in buddy and official).
4. Buddy Node flow: pack first, publish from packed `.tgz`, upload the same `.tgz`.
5. WXT zip collection:
    - only `.output/*.zip`
    - copy all zips
    - fail on basename collisions with actionable diagnostics
6. Build reusable workflows do not run GitHub attestations.
7. Official runs GitHub attestation for `out/*`; buddy does not.
8. Node quality checks run by default in both flows (via `--if-present`).

## Rollback plan

All changes are isolated to root workflow YAML (and optional root composite actions). Rollback is a straightforward revert of the workflow changes.
