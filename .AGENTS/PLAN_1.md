# Release Workflows Refactor Plan v1 (Build/Release Split + Artifacts)

<!-- markdownlint-disable MD013 -->
<!-- markdownlint-disable MD044 -->

This document proposes a revised refactor design for reducing duplication between:

- `.github/workflows/official.yml`
- `.github/workflows/buddy.yml`

The key change vs the original plan is an explicit **build vs publish/release split**:

- **Build jobs** produce reproducible artifacts under `out/` and upload them as workflow artifacts.
- **Publish / Release jobs** consume those artifacts and perform external publication (PyPI/npm/GPR) and GitHub Release creation.

This split improves reuse and permission isolation while preserving behavior.

## Goals

1. Reduce YAML duplication between `official.yml` and `buddy.yml` without changing behavior.
2. Keep the entry workflows as thin “policy + trigger” wrappers.
3. Standardize an artifact contract (what is produced in `out/`) so downstream jobs do not depend on implementation details.
4. Improve least-privilege by separating build permissions from publish permissions.
5. Avoid breaking npm Trusted Publishing (OIDC) by keeping workflow identity constraints in mind.

## Non-goals

- Changing release semantics (tag format, version rules, registries, or artifact naming).
- Converging official and buddy Node publish flows (they may remain intentionally different).
- Rewriting existing reusable workflows:
    - `release-prepare-release-notes.yml`
    - `release-create-github-release.yml`

## Key constraints and design rules

### A) Keep npm Trusted Publishing stable

The official Node flow publishes to `registry.npmjs.org` using npm Trusted Publishing (OIDC). npm’s Trusted Publisher configuration is tied to a workflow filename and may validate the caller identity in ways that can be sensitive to `workflow_call` chains.

**Rule:** keep the step that performs `npm publish` to npmjs.org in `official.yml` (or validate very carefully before moving it).

This plan therefore treats **official npmjs publish** as a special case:

- We still extract shared build/pack logic.
- We keep the actual `npm publish` invocation in the entry workflow to preserve workflow identity.

### B) Preserve “build at target commit” semantics

The current pipelines resolve a `target` SHA and run detection/build against that SHA.

**Rule:** every build job must checkout `ref: ${{ needs.resolve.outputs.target }}` and must not accidentally build `HEAD`.

### C) Buddy must not clobber official releases

Buddy releases must never modify an existing **non-prerelease** GitHub Release for the same tag.

**Rule:** if a GitHub Release already exists for `release/<project>/v<version>` and it is **not** a prerelease, the buddy workflow must fail fast (before uploading assets or changing prerelease state).

### D) Tool versions must be explicit

After extraction, tool versions (`PYTHON_VERSION`, `NODE_VERSION`, `PNPM_VERSION`) must not silently drift.

**Rule:** keep tool versions defined in the entry workflows (`official.yml` / `buddy.yml`) as the single source of truth, and pass them as **required** `workflow_call` inputs to reusable workflows (no defaults).

## Proposed workflow architecture

### Entry workflows (thin wrappers)

#### `.github/workflows/official.yml`

Responsibilities:

- Triggers: `push` tags `release/*/v*` + `workflow_dispatch`.
- Define concurrency and top-level permissions.
- Call shared resolve and build components.
- Perform publication steps that are **policy-specific**:
    - Python: publish to PyPI (OIDC)
    - Node: publish to GitHub Packages and npmjs.org (OIDC)
    - WXT: (optional) provenance attestation
- Call reusable release-notes and GitHub Release creation.

#### `.github/workflows/buddy.yml`

Responsibilities:

- Trigger: `workflow_dispatch`.
- Define concurrency and top-level permissions.
- Call shared resolve and build components.
- Perform buddy-specific publication steps:
    - Node: publish to GitHub Packages only (existing buddy flow)
    - Python: no PyPI publish
    - WXT: no attestation
- Call reusable release-notes and GitHub Release creation (marked prerelease).

### Shared reusable workflows

#### 1) `.github/workflows/release-resolve.yml` (new)

Purpose: single source of truth for resolving and validating release inputs.

Inputs (suggested):

- `source`: `tag | manual`
- `project`, `version`, `target`, `tag_name`, `force_update_tag`

Outputs (superset):

- `project`, `version`
- `project_kind`: `python | node`
- `is_wxt`: `true | false` (string)
- `tag_name`, `target`, `force_update_tag` (normalized)
- `package_dir`
- `has_changelog`, `changelog`
- `release_title`, `run_url`

Notes:

- Must preserve current version normalization rules (Python may strip leading v/V).
- Must detach to `target` before running helper scripts.

#### 2) `.github/workflows/release-build-python.yml` (new)

Purpose: build Python distributions and upload as artifacts.

Inputs:

- `target` (sha), `package_dir`, `version`
- `python_version`
- `artifact_name` (e.g., `official-<project>-dist` / `buddy-<project>-dist`)
- `attest` (boolean)

Behavior:

- checkout `ref: target`
- setup dotnet + python + uv
- `uv build --out-dir $GITHUB_WORKSPACE/out`
- verify version (`verify_python_artifact_version.py`)
- if `attest=true`: `actions/attest-build-provenance@v3` with `subject-path: out/*`
- upload artifact: `out/*`

Permissions:

- base: `contents: read`
- if `attest=true`: requires caller to grant `id-token: write` and `attestations: write`

Publish to PyPI is intentionally **not** part of this workflow.

#### 3) `.github/workflows/release-build-wxt.yml` (new)

Purpose: build WXT zip artifacts and upload as artifacts.

Inputs:

- `target`, `project`, `package_dir`, `version`
- `node_version`, `pnpm_version`
- `artifact_name`
- `attest` (boolean)

Behavior:

- checkout `ref: target`
- setup dotnet + jq + node + pnpm
- build zip artifacts (existing logic)
- verify node version (`nbgv get-version`)
- if `attest=true`: attest `out/*`
- upload artifact `out/*`

#### 4) `.github/workflows/release-build-node-pack.yml` (new)

Purpose: build and pack Node package artifacts for GitHub Release assets.

Important: this workflow is **about building/packing**, not publishing.

Inputs:

- `target`, `project`, `package_dir`, `version`
- `node_version`, `pnpm_version`, `python_version` (if uv scripts are used)
- `artifact_name`
- `run_quality_checks` (boolean)
- `attest` (boolean)

Behavior:

- checkout `ref: target`
- setup dotnet + python/uv (for helper scripts) + node + pnpm
- install dependencies
- verify version (`nbgv get-version`)
- optionally run quality checks (lint/typecheck/test/build)
- produce `out/*` assets for GitHub Releases.

Packing contract:

- For official flow, two tarballs may be needed (current behavior):
    - a GitHub Packages (GPR) tarball with scoped name
    - an npmjs tarball with public name
- For buddy flow, the existing behavior produces one tarball that is used as the GitHub Release asset.

To preserve behavior, this workflow should support two modes:

- `pack_mode=official` → produce both `out/*` tarballs as done today.
- `pack_mode=buddy` → produce the single tarball as done today.

This keeps "what goes into the GitHub Release" in the build step, while publication remains separate.

## Publication and release creation flow

### A) Python

- Both official and buddy call `release-build-python.yml`.
- Only official then runs a **publish step** (in `official.yml`) that:
    - downloads the `official-<project>-dist` artifact
    - publishes to PyPI (OIDC)

This avoids duplicating build logic, and keeps environment/policy explicit.

### B) Node (non-WXT)

- Both official and buddy call `release-build-node-pack.yml` to produce `out/*` release assets.

Publication remains policy-specific:

- **Buddy** continues to publish to GitHub Packages using its current method.
- **Official** continues to publish to:
    - GitHub Packages
    - npmjs.org using Trusted Publishing (OIDC)

**Important:** keep the `npm publish` step for npmjs.org in `official.yml`.

The publish jobs may download the packed tarballs from artifacts to avoid re-running pack steps.

### C) WXT

- Both channels call `release-build-wxt.yml`.
- Official may enable attestation (`attest=true`), buddy uses `attest=false`.

### D) GitHub Release

No change:

- `release-prepare-release-notes.yml` is called by both.
- `release-create-github-release.yml` is called by both.

## Artifact contract

All build workflows must upload release assets under a flat `out/` directory.

- Python: wheels and sdists in `out/*`
- Node: `.tgz` artifacts in `out/*`
- WXT: `.zip` artifacts in `out/*`

This matches current expectations of `release-create-github-release.yml`.

Artifact naming conventions remain:

- Official:
    - `official-<project>-dist`
    - `official-<project>-release-notes`
- Buddy:
    - `buddy-<project>-dist`
    - `buddy-<project>-release-notes`

## Permissions model

- Build workflows should default to `contents: read`.
- Only jobs that need OIDC/publishing should request:
    - `id-token: write`
    - `attestations: write`
    - `packages: write`

Caller workflows remain the source of truth for permissions (reusable workflows cannot exceed caller permissions).

## Migration plan (phased)

### Phase 1 — Extract resolve (low risk)

- Add `release-resolve.yml`.
- Update `official.yml` and `buddy.yml` to use it.
- Preserve the output contract.

Acceptance:

- Outputs match current behavior for representative inputs.

### Phase 2 — Extract build workflows (Python + WXT)

- Add `release-build-python.yml` and `release-build-wxt.yml`.
- Wire both entry workflows to call them.

Acceptance:

- Produced artifacts match current files and naming.
- Official attestation still works for Python/WXT.

### Phase 3 — Extract Node packing as a build step

- Add `release-build-node-pack.yml` with `pack_mode`.
- Wire both entry workflows.

Acceptance:

- Official still produces the same tarballs and publishes as before.
- Buddy still publishes to GPR and produces the same GitHub Release asset.

### Phase 4 — Reduce remaining duplication via composite actions (optional)

If additional duplication remains (toolchain setup or repeated scripts), add local composite actions under `.github/actions/*` for:

- dotnet setup + `dotnet tool restore`
- python + uv setup
- pnpm + node setup

This avoids expanding reusable workflow surface area where external identity constraints matter.

### Phase 5 — Orchestrator workflow (not recommended while npm Trusted Publishing is in use)

Avoid introducing a multi-level `workflow_call` chain for the job that performs `npm publish` to npmjs.org unless you have validated trusted publisher behavior end-to-end.

## Validation strategy

Use `workflow_dispatch` on a test branch for both channels with:

- a Python project
- a Node project (non-WXT)
- a Node/WXT project

Validate:

- `resolve` outputs (`project`, `version`, `tag_name`, `target`, `package_dir`, `project_kind`, `is_wxt`)
- artifact contents under `out/`
- publish behavior:
    - official: PyPI + npmjs + GPR
    - buddy: GPR only
- GitHub Release creation and prerelease policy.

## Rollback plan

All changes are isolated to workflow YAML and (optionally) composite actions.

Rollback is a simple revert of workflow changes.
