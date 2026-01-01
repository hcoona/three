# Clarifications needed for the release workflow refactor

<!-- markdownlint-disable MD029 -->

This file lists questions and decision points that should be clarified before implementing the refactor described in `PLAN.md`.

## Reusable workflow inputs and configuration

1. **Tool versions source of truth**
    - Decision: Keep `PYTHON_VERSION`, `NODE_VERSION`, and `PNPM_VERSION` defined in the entry workflows (`official.yml` / `buddy.yml`) as the single source of truth.
    - Decision: All reusable workflows must accept these versions as _required_ `workflow_call` inputs (no defaults).
    - Rationale: Avoid configuration drift and avoid relying on implicit `env` inheritance across `workflow_call` boundaries.

2. **Should resolve emit `dist_dir` / `dist_glob` for buddy too?**
    - Decision: No. Standardize on `${GITHUB_WORKSPACE}/out` as the only artifact directory.
    - Decision: Treat `out/*` as the complete release asset set and require a flat file layout (no subdirectories).
    - Rationale: With a fixed artifact contract, `dist_dir` and `dist_glob` would be constants and add no information.

3. **Force-update tag normalization**
    - Decision: `release-resolve.yml` must emit `force_update_tag` as a normalized `"true" | "false"` string output.
    - Decision: For tag-triggered runs, `force_update_tag` is always `"false"` (tags are immutable by policy).
    - Decision: All callers must pass a boolean to downstream reusable workflows via an explicit conversion, e.g. `fromJSON(needs.resolve.outputs.force_update_tag)` (or `needs.resolve.outputs.force_update_tag == 'true'`).
    - Rationale: GitHub Actions job/workflow outputs are stringly typed; explicit normalization + conversion avoids drift across call boundaries.

## GitHub Environments and OIDC / trusted publishers

4. **PyPI publishing identity constraints**
    - Decision: Official Python publishing must use PyPI Trusted Publishers (OIDC).
    - Decision: Keep the PyPI publish job/steps in `official.yml` (do not move them into a reusable workflow).
    - Decision: PyPI Trusted Publishing must run under the `pypi` GitHub Environment (i.e., `environment: pypi`).
    - Rationale: PyPI Trusted Publisher configuration depends on the environment, and we want the publishing identity to remain explicit and stable.

5. **npm Trusted Publishing is a special case**
    - Decision: Official publishing to `registry.npmjs.org` must use npm Trusted Publishers (OIDC).
    - Decision: Keep the npmjs publish job/steps in `official.yml` (do not move them into a reusable workflow).
    - Decision: npmjs Trusted Publishing must run under the `npmjs` GitHub Environment (i.e., `environment: npmjs`).
    - Rationale: npm Trusted Publisher configuration can be sensitive to workflow identity and calling context; keeping the publish identity explicit and stable avoids breaking OIDC-based publishing.

6. **Can we add an orchestrator workflow at all?**
    - Decision: Do not introduce an orchestrator that calls `official.yml` via `workflow_call`.
    - Decision: Do not move the `npm publish` (npmjs Trusted Publishing) step into any reusable workflow.
    - Decision: Additional orchestration layers are allowed for build-only workflows, as long as they do not affect the npmjs publish identity chain.
    - Rationale: npm Trusted Publishing (OIDC) can be sensitive to workflow identity and calling context; keeping the npm publish step directly in `official.yml` avoids breaking trusted publishing.

## Behavioral invariants (what must not change)

7. **Buddy tag creation policy**
    - Decision: Buddy releases may create/push the tag `release/<project>/v<version>` if it does not exist (keep current behavior).
    - Decision: Buddy releases must never modify an existing **non-prerelease** GitHub Release for the same tag. If a release already exists and `prerelease` is `false`, the buddy workflow must fail fast.
    - Rationale: Tag pushes performed via `GITHUB_TOKEN` do not trigger additional workflows by default; the primary risk to guard against is clobbering an existing official release (assets and prerelease state).

8. **Node buddy publish flow**
    - Decision: Align the buddy Node flow with `official.yml`: **pack first, then publish**, so the `.tgz` attached to the GitHub Release is byte-for-byte the same artifact that gets published.
    - Decision: For buddy, apply the GitHub Packages scope adjustment **before** the pack+publish sequence.
    - Decision: Buddy must publish the package **from the packed `.tgz` file** (not from the working tree) to guarantee the GitHub Release asset and the published package are consistent.
    - Decision: After publishing, restore any temporary scope/name changes.
    - Rationale: This removes a subtle inconsistency risk and makes the release asset an auditable representation of what was actually published.

9. **Node dist-tag derivation differences**
    - Decision: Buddy and official must use the NBGV CLI as the single source of truth for prerelease/channel information (do not parse the SemVer `version` string).
    - Decision: Dist-tag derivation rules must be identical in buddy and official:
        - If the resolved version is not a prerelease: use `latest`.
        - If the resolved version is a prerelease: derive the channel from NBGV metadata (not from string parsing); if the channel cannot be determined, fast fail.
    - Rationale: Parsing SemVer strings is easy to get wrong (e.g., `+build` metadata) and can drift across workflows. Using NBGV metadata keeps behavior consistent and aligned with the repository’s versioning source of truth.

10. **WXT artifact naming conventions**
    - Decision: Do **not** assume `${PROJECT}.zip` (or `${PROJECT}-*.zip`) naming.
    - Decision: Standardize release assets under `${GITHUB_WORKSPACE}/out/`. When building WXT, collect all `*.zip` files emitted under WXT's output directory (default: `.output`) and copy them into `${GITHUB_WORKSPACE}/out/`.
    - Evidence:
        - WXT's `outDir` default is `.output` and it stores build folders and ZIPs: https://wxt.dev/api/reference/wxt/interfaces/inlineconfig#outdir
        - WXT's default zip output filename template is `{{name}}-{{version}}-{{browser}}.zip`, and the sources zip default is `{{name}}-{{version}}-sources.zip`: https://wxt.dev/api/reference/wxt/interfaces/inlineconfig#zip
        - The WXT publishing docs show store submission using patterns like `.output/*-chrome.zip`, `.output/*-firefox.zip`, and `.output/*-sources.zip` (not `${PROJECT}.zip`): https://wxt.dev/guide/essentials/publishing#automation
    - Rationale: Our pipeline should only depend on the stable contract "WXT produces ZIPs under its output directory" + our repo contract "release assets live in `${GITHUB_WORKSPACE}/out/`", not on any specific ZIP filename.

## Implementation details and safety

11. **Required permissions by mode**
    - Please confirm the intended policy matrix using **features**, plus the corresponding **permission requirements**.

    - Feature → permission mapping:
        - Read repository content (checkout, read files): `contents: read`
        - Upload workflow artifacts (`actions/upload-artifact@v4`): no additional permissions required in this repo (works with `contents: read`)
        - Download workflow artifacts (`actions/download-artifact@v4`): `actions: read` (still required even within the same workflow run when `GITHUB_TOKEN` permissions are restricted)
        - Create/move tags, create/edit GitHub Releases, upload release assets: `contents: write`
        - Publish to GitHub Packages (GPR): `packages: write`
        - OIDC token (Trusted Publishing / Trusted Publishers): `id-token: write`
        - Provenance attestation (`actions/attest-build-provenance@v3`): `attestations: write` **and** `id-token: write`
        - GitHub Environment gating (not a permission): `environment: <name>`

    - Mode → features (→ implied permissions):
        - All modes that call `release-create-github-release.yml` require: download artifacts + create/edit release/tag
            - Permissions: `actions: read`, `contents: write`
        - Official Python: build + attest + publish to PyPI (Trusted Publishers) under `environment: pypi`
            - Permissions: `contents: read`, `id-token: write`, `attestations: write`
        - Buddy Python: build only
            - Permissions: `contents: read`
        - Official Node (non-WXT): build + publish to GPR + publish to npmjs (Trusted Publishers) + attest, under `environment: npmjs`
            - Permissions: `contents: read`, `packages: write`, `id-token: write`, `attestations: write`
        - Buddy Node (non-WXT): build + publish to GPR
            - Permissions: `contents: read`, `packages: write`
        - Official WXT: build + attest
            - Permissions: `contents: read`, `id-token: write`, `attestations: write`
        - Buddy WXT: build only
            - Permissions: `contents: read`

12. **Output contract stability**
    - Decision: Yes. Downstream jobs rely on exact output strings today (string comparisons in `if:` and in `with:` inputs).
    - Decision: The reusable resolve workflow must preserve both output **names** and output **value formats**.
    - Required stability rules:
        - Boolean-like outputs must be emitted as lowercase strings: `'true' | 'false'` (not booleans, not `True`/`False`).
        - Enum-like outputs must keep the same exact value set (e.g., `project_kind: 'python' | 'node'`).
    - Examples of existing exact-string dependencies:
        - `needs.resolve.outputs.project_kind == 'python' | 'node'`
        - `needs.resolve.outputs.is_wxt == 'true' | 'false'`
        - `needs.resolve.outputs.has_changelog == 'true' | 'false'`
        - `needs.resolve.outputs.force_update_tag == 'true' | 'false'` (and callers convert via `== 'true'` or `fromJSON(...)`).

13. **Documentation / runbook**
    - Decision: No. Do not add a separate internal runbook as part of this refactor.
