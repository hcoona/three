# Additional clarifications for the release workflow refactor (follow-up)

This file captures **new** decision points found while reviewing `PLAN_1.md` against `CLARIFY.md` and the current workflows.

## Scope

Implementation updates described by this file apply **only** to the root workflows under `/.github/workflows/*.yml`.

- Do **not** change any nested `.github` directories under subprojects.

## 1) Resolve outputs: keep vs remove `dist_dir` / `dist_glob`

`CLARIFY.md` states we should standardize on `${GITHUB_WORKSPACE}/out` and treat `out/*` as the full release asset set.

Decision:

1. The new reusable `release-resolve.yml` must **not** emit `dist_dir` or `dist_glob`.
2. All root workflows under `/.github/workflows/*.yml` must be updated together to stop reading these outputs.

Replacement contract:

- `${GITHUB_WORKSPACE}/out` is the only artifact directory.
- `out/*` is the complete release asset set (flat layout; no subdirectories).
- Callers must treat `dist_dir` and `dist_glob` as constants and use `${{ github.workspace }}/out` and `${{ github.workspace }}/out/*` directly.

## 2) Enforcing the buddy non-clobber policy

`CLARIFY.md` requires: buddy must never modify an existing **non-prerelease** GitHub Release for the same tag.

Decision:

1. Guard placement: **Option B** — implement the guard in `buddy.yml` before calling `release-create-github-release.yml`.

2. Exact rule when the tag exists:
    - (a) The release does not exist yet: **allow**.
    - (b) The release exists and `prerelease=true`: **allow**.
    - (c) The release exists and `prerelease=false`: **fast fail**.
        - This must fail **before** performing any operation that could modify the existing Release, including (but not limited to) editing release notes/title/target, uploading or clobbering assets, or changing prerelease state.

3. Same-SHA does not grant an exception: buddy must still be **blocked** from touching an existing non-prerelease Release even if the tag already points to the same commit.

## 3) Node dist-tag derivation via NBGV metadata

`CLARIFY.md` decides that buddy and official must derive npm `dist-tag` via NBGV metadata (not SemVer string parsing), with identical rules:

- not prerelease → `latest`
- prerelease → derive channel from NBGV metadata; if unknown → fail

Decision:

1. Canonical source of prerelease/channel information: use the NBGV CLI variable `PrereleaseVersionNoLeadingHyphen`.

2. Mapping rules (buddy and official must be identical):
    - If `PrereleaseVersionNoLeadingHyphen` is empty: use `latest`.
    - Otherwise:
        - Derive the npm dist-tag as the first dot-separated segment of `PrereleaseVersionNoLeadingHyphen`.
            - Example: `alpha.3` → `alpha`
            - Example: `beta.121` → `beta`
            - Example: `rc.2` → `rc`

3. Failure policy:
    - If `PrereleaseVersionNoLeadingHyphen` is non-empty but the derived channel is empty or invalid, the workflow must **fast fail**.
    - Recommended error guidance: instruct maintainers to update the project versioning configuration (typically `version.json`) so that the prerelease format begins with a stable channel label (e.g., `-beta.{height}`, `-rc.{height}`), ensuring `PrereleaseVersionNoLeadingHyphen` starts with `beta`/`rc`/etc.

## 4) Buddy Node flow: which tarball goes to the GitHub Release?

Decision:

Buddy Node must align with the `official.yml` pattern to ensure the GitHub Release asset matches what gets published.

1. Buddy must apply the GitHub Packages scope/name adjustment **first**.
2. Buddy must then **pack first, then publish**.
    - Pack exactly once to produce a `.tgz` under `${GITHUB_WORKSPACE}/out/`.
    - Publish **from that `.tgz` file** (not from the working tree).
    - Upload the **same** `.tgz` file as the GitHub Release asset.
3. After publishing, buddy must restore any temporary scope/name changes.

## 5) WXT artifacts: output directory and name collisions

`CLARIFY.md` decides we must not assume any particular zip filename; we should collect all `*.zip` produced under WXT output and copy them into `${GITHUB_WORKSPACE}/out/`.

Decision:

1. We do **not** support custom WXT `outDir` discovery in workflows.
    - Workflows must assume the default WXT output directory is `.output`.
    - We will not parse `wxt.config.*`, introduce repo-wide conventions, or perform bounded-depth searches.
2. Artifact collection must be **shallow** and **predictable**:
    - Only copy `.output/*.zip` into `${GITHUB_WORKSPACE}/out/`.
    - Do not traverse `.output/**` subdirectories.
3. Because `${GITHUB_WORKSPACE}/out/` is a flat directory, basename collisions must be handled safely:
    - Fail fast if copying would overwrite an existing file in `out/`.
    - Do not auto-rename ZIPs.

## 6) Attestation placement vs environment gating

`PLAN_1.md` proposes build workflows that optionally attest `out/*`.
`CLARIFY.md` emphasizes environment-bound Trusted Publishing for PyPI (`pypi`) and npmjs (`npmjs`).

Decision:

1. Build workflows are responsible for producing release artifacts only.
    - They must populate `${GITHUB_WORKSPACE}/out` and publish the artifacts for downstream jobs.
    - They must not generate provenance/attestations.
2. PyPI / npmjs publishing provenance remains handled by the existing publishing actions.
    - Keep current behavior; do not add additional provenance generation steps beyond what the publish actions already do.
3. GitHub Release provenance/attestation policy differs by entry workflow:
    - `buddy.yml`: do **not** generate GitHub attestations for release assets.
    - `official.yml`: **do** generate GitHub attestations for the release assets (`out/*`).

## 7) Quality checks: official vs buddy default policy

`PLAN_1.md` suggests `run_quality_checks` for the Node pack build.

Decision:

1. Both `official.yml` and `buddy.yml` Node pack builds must run quality checks by default.
2. When a project does not define one or more quality scripts (`lint`, `typecheck`, `test`, `build`), skipping via `--if-present` is expected behavior.
    - Do not require at least one of these scripts to exist.
