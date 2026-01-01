# Release Workflows Refactor Plan (official.yml vs buddy.yml)

<!-- markdownlint-disable MD044 -->

## Executive summary

`/.github/workflows/official.yml` and `/.github/workflows/buddy.yml` are highly similar. The duplicated parts include:

- The entire `resolve` job structure (tool setup + project discovery + version validation + WXT detection + changelog discovery).
- The `prepare-release-notes` job (already reused via `release-prepare-release-notes.yml`).
- Python build steps (checkout, dotnet, python, uv, build, verify, upload artifact).
- WXT build steps (checkout, jq, dotnet, pnpm/node, build zips, verify, upload artifact).
- Release creation (already reused via `release-create-github-release.yml`).

The main differences are:

- Triggers: `official.yml` supports tag push (`release/<project>/v<version>`) + manual dispatch; `buddy.yml` is manual only.
- “Official vs Buddy” policies:
    - Python: official publishes to PyPI and attests provenance; buddy only builds and marks GitHub Release as prerelease.
    - Node (non-WXT): official publishes to GitHub Packages (GPR) and npmjs.org, includes provenance attestation and extra safety checks; buddy publishes to GPR only and uses a simpler packaging flow.
    - WXT: official attests provenance; buddy does not.

Given the duplication and the fact that this repo already uses reusable workflows (`release-prepare-release-notes.yml`, `release-create-github-release.yml`), it is worth extracting additional reusable workflows via `workflow_call`.

## Goals

1. Reduce YAML duplication between `official.yml` and `buddy.yml` without changing behavior.
2. Keep entrypoints (`official.yml`, `buddy.yml`) as thin “policy + trigger” wrappers.
3. Make it easier to add new release channels/policies (e.g., “rc”, “nightly”, “internal”).
4. Keep permissions, environments, and publication steps explicit and auditable.

## Non-goals

- Changing the release semantics (tag format, version rules, where artifacts are published).
- Rewriting the existing reusable workflows for release notes / GitHub Releases.

## Proposed architecture

### 1) Keep two thin entry workflows

- `official.yml`
    - Keeps `on.push.tags` and `workflow_dispatch`.
    - Calls a reusable workflow with explicit inputs for policy and the raw trigger context.

- `buddy.yml`
    - Keeps `workflow_dispatch`.
    - Calls the same reusable workflow but with different policy inputs.

This preserves the user experience (existing triggers remain), while moving most logic to reusable components.

### 2) Introduce a reusable “resolve” workflow

Add:

- `/.github/workflows/release-resolve.yml` (new, `on: workflow_call`)

Responsibilities:

- Compute `project`, `version`, `tag_name`, `target`.
- Validate `project` and `version` shell-safety.
- Detect project kind (`python` vs `node`) and `package_dir`.
- Detect WXT.
- Detect changelog and produce `has_changelog`/`changelog`.
- Output `dist_dir` and `dist_glob` (for provenance attestations).

Inputs (suggested):

- `mode`: `official` | `buddy` (string)
- `source`: `tag` | `manual` (string)
- `project` (string, optional depending on `source`)
- `version` (string, optional depending on `source`)
- `tag_name` (string, optional; for `source=tag`)
- `target` (string, optional)
- `force_update_tag` (boolean, optional)

Outputs (suggested superset to serve both callers):

- `project`, `version`, `project_kind`, `is_wxt`
- `tag_name`, `target`, `package_dir`
- `has_changelog`, `changelog`
- `dist_dir`, `dist_glob`
- `release_title`
- `run_url`
- `force_update_tag` (normalized `true|false`)

Notes:

- The existing logic in `official.yml`/`buddy.yml` can be moved almost verbatim.
- Prefer emitting a superset of outputs so downstream jobs don’t care whether they’re in buddy/official mode.

### 3) Add reusable build/publish workflows per “artifact type”

Create small reusable workflows that encapsulate one responsibility each and are invoked from the entry workflows (or from a higher-level orchestrator workflow).

Suggested set:

#### Python

- `/.github/workflows/release-python.yml` (new)
    - Inputs: `target`, `package_dir`, `version`, `artifact_name_prefix`, `publish_to_pypi` (bool), `attest` (bool)
    - Behavior:
        - Build with `uv build --out-dir out`
        - Verify with `verify_python_artifact_version.py`
        - If `attest=true`, run `actions/attest-build-provenance@v3`
        - If `publish_to_pypi=true`, run `pypa/gh-action-pypi-publish`
        - Upload dist artifact

Mapping:

- Official: `publish_to_pypi=true`, `attest=true`
- Buddy: `publish_to_pypi=false`, `attest=false`

#### Node (non-WXT)

Because the publish flows differ substantially, start with two reusable workflows (simpler, less parameter sprawl):

- `/.github/workflows/release-node-official.yml` (new)
    - Publishes to GPR and npmjs.org, includes OIDC/provenance.

- `/.github/workflows/release-node-buddy-gpr.yml` (new)
    - Publishes to GPR only.

Optional later improvement:

- Consolidate into `release-node.yml` with `publish_targets: [gpr, npmjs]` once both flows converge.

#### Node (WXT)

- `/.github/workflows/release-wxt.yml` (new)
    - Inputs: `target`, `project`, `package_dir`, `version`, `artifact_name_prefix`, `attest` (bool)
    - Behavior: current WXT build job (nearly identical between buddy/official).

Mapping:

- Official: `attest=true`
- Buddy: `attest=false`

### 4) Optional: an orchestrator reusable workflow

If desired, add:

- `/.github/workflows/release-orchestrator.yml` (new)

It would:

1. Call `release-resolve.yml`
2. Call `release-prepare-release-notes.yml`
3. Based on outputs (`project_kind`, `is_wxt`, and `mode`), call the appropriate build/publish workflow
4. Call `release-create-github-release.yml` with `prerelease` determined by `mode`

This makes `official.yml` and `buddy.yml` extremely small (pure trigger + input plumbing).

## Migration plan (phased)

### Phase 1 — Extract resolve

- Create `release-resolve.yml`.
- Update `official.yml` and `buddy.yml`:
    - Replace inlined `resolve` job with `uses: ./.github/workflows/release-resolve.yml`.
    - Keep the same outputs contract so other jobs remain unchanged.

Acceptance criteria:

- Both workflows still compute the same outputs for identical inputs.
- The downstream jobs run without changes.

### Phase 2 — Extract WXT build

- Create `release-wxt.yml`.
- Replace `build-wxt` jobs in both workflows with calls.

Acceptance criteria:

- Zip artifact collection rules and verification are unchanged.

### Phase 3 — Extract Python build/publish

- Create `release-python.yml`.
- Replace `publish-python` (official) and `build-python` (buddy) with calls.

Acceptance criteria:

- Official still publishes to PyPI and attests.
- Buddy still only builds.

### Phase 4 — Node non-WXT workflows

- Create `release-node-official.yml` and `release-node-buddy-gpr.yml`.
- Replace `publish-node` and `publish-node-gpr` jobs with calls.

Acceptance criteria:

- Version verification logic remains the same.
- Official still publishes to both registries and uploads dist.
- Buddy still publishes to GPR and uploads dist.

### Phase 5 — Optional orchestrator

- If Phase 1–4 are stable, optionally add `release-orchestrator.yml` and shrink `official.yml`/`buddy.yml` further.

## Risk management

- **Permissions & environments**: reusable workflows cannot exceed caller permissions; keep caller permissions explicit.
- **Context differences**: when moving logic into reusable workflows, prefer explicit inputs over relying on event context.
- **Behavior drift**: keep scripts/commands identical; only change YAML structure.

## Validation strategy

- Use `workflow_dispatch` on a test branch for both official and buddy with:
    - Python project
    - Node (non-WXT) project
    - Node (WXT) project
- Confirm:
    - Computed `tag_name`, `target`, `package_dir`, `project_kind`, `is_wxt`
    - Artifacts uploaded match expected names and contents
    - GitHub Release creation behavior matches prerelease policy

## Rollback plan

- Changes are isolated to workflow YAMLs.
- If issues occur, revert to the previous `official.yml` and `buddy.yml` versions while keeping new reusable workflows unused.
