# Code Review: staged workflow refactor (buddy/official)

<!-- markdownlint-disable MD013 -->

Date: 2026-01-02

Scope (git staged):

- `.github/workflows/buddy.yml` (modified)
- `.github/workflows/official.yml` (modified)
- `.github/workflows/release-resolve.yml` (added)
- `.github/workflows/release-build-python.yml` (added)
- `.github/workflows/release-build-node-pack.yml` (added)
- `.github/workflows/release-build-wxt.yml` (added)

This review is guided by the repository decisions captured in `.AGENTS/CLARIFY_0.md`–`.AGENTS/CLARIFY_4.md`.

## High-level summary

This refactor is directionally solid:

- It centralizes “resolve” logic into a reusable workflow with an explicit `source` input, avoiding `github.event_name` pitfalls under `workflow_call`.
- It standardizes the artifact contract to `${GITHUB_WORKSPACE}/out` and removes `dist_dir/dist_glob` drift.
- It implements the buddy “non-clobber” guard to prevent accidentally modifying an existing official release.
- It aligns Node publishing with “pack first, then publish” and publishes from the `.tgz` tarball.

That said, there are a few issues that are either outright blocking or high-risk if not addressed.

## Blocking / must-fix issues

### 1) Official release is not gated on attestation jobs

`official.yml` introduces dedicated `attest-*` jobs (Python/Node/WXT), but the corresponding `release-*` jobs do **not** depend on them.

Impact:

- The workflow can successfully publish and create an official GitHub Release even if attestation fails or is skipped.
- This undermines the policy in `CLARIFY_1.md`/`CLARIFY_0.md` that official releases should generate GitHub attestations for `out/*`.

Recommendation:

- Add `attest-python` to `release-python.needs`.
- Add `attest-node` to `release-node.needs`.
- Add `attest-wxt` to `release-wxt.needs`.

(Alternatively, merge attestation into a single job that runs after artifacts are downloaded, but the key is: release creation must not bypass it.)

### 2) Potential missing `packages: read` in the Node pack workflow (dependency install)

`release-build-node-pack.yml` creates an `.npmrc` wired to GitHub Packages and runs `pnpm install`.

The job permissions are currently:

- `contents: read`

If any dependency is fetched from `npm.pkg.github.com` using `GITHUB_TOKEN`, GitHub often requires `packages: read`.

Recommendation:

- Consider adding `packages: read` to `jobs.pack.permissions` (least privilege remains acceptable).
- If the intent is “no GH Packages dependencies during build”, remove the GPR `.npmrc` install configuration to avoid confusing failures.

## High-risk / correctness concerns

### 3) Verify action versions actually exist (`@v6`, `@v5`)

Multiple workflows use:

- `actions/checkout@v6`
- `actions/setup-node@v6`
- `actions/setup-python@v6`
- `actions/setup-dotnet@v5`

If any of these major versions do not exist (or are not yet available on GitHub-hosted runners), the workflows will fail immediately.

Recommendation:

- Confirm these versions are valid.
- If not, downgrade to currently-available majors or pin to SHAs.

### 4) `official.yml` uses `github.event.inputs.*` in several places

It works in many cases, but the more idiomatic and type-safe approach for `workflow_dispatch` is to use the `inputs.*` context.

Recommendation (non-blocking):

- Prefer `inputs.project`, `inputs.version`, etc. for dispatch paths.

## File-by-file notes

### `.github/workflows/release-resolve.yml` (added)

Strengths:

- Correctly follows `CLARIFY_3.md`: mode is driven by explicit `source` input, and `run_url` is passed in from the entry workflow.
- Outputs are string-normalized for boolean-like values (`"true" | "false"`).
- Uses NBGV + repo scripts as the single source of truth for version validation.

Concerns / suggestions:

- Project name validation rejects scoped names (e.g., `@scope/pkg`). This is fine if it’s an intentional invariant, but it should be explicitly documented as part of the release contract.

### `.github/workflows/release-build-node-pack.yml` (added)

Strengths:

- Implements dist-tag derivation via NBGV metadata (`PrereleaseVersionNoLeadingHyphen`) and fails fast on mismatches per `CLARIFY_4.md`.
- Runs quality checks by default with `--if-present` per `CLARIFY_1.md`.
- Packs once (or twice) and publishes from deterministic filenames (`out/gpr.tgz`, `out/npmjs.tgz`) per `CLARIFY_4.md`.

Concerns:

- See must-fix #2 on `packages: read`.
- Consider lowercasing the `--scope` passed into `prepare_npm_publish.py` to match the lowercased scope used in `.npmrc`.

### `.github/workflows/release-build-python.yml` (added)

Looks good:

- Uses `${GITHUB_WORKSPACE}/out` and verifies produced artifacts match the expected version.
- Minimal permissions (`contents: read`).

### `.github/workflows/release-build-wxt.yml` (added)

Strengths:

- Matches the artifact contract and collects only `.output/*.zip` (shallow) and fails if none found, aligning with `CLARIFY_1.md`/`CLARIFY_0.md`.
- Avoids assuming any WXT zip filename convention.

Minor notes:

- Basename collision detection is effectively redundant when globbing only one directory (`.output/*.zip` cannot contain duplicate basenames), but it’s harmless.

### `.github/workflows/buddy.yml` (modified)

Strengths:

- Implements the buddy non-clobber guard (before calling `release-create-github-release.yml`) as decided in `CLARIFY_1.md`.
- Uses the reusable pack workflow and publishes from `out/gpr.tgz`, aligning buddy Node behavior with official.

Notes:

- Guard checks only `.prerelease`. This also blocks draft-but-non-prerelease releases (since `prerelease=false`), which matches the conservative recommendation in `CLARIFY_2.md`.

### `.github/workflows/official.yml` (modified)

Strengths:

- Keeps PyPI and npmjs publishing steps in `official.yml` and uses GitHub Environments (`pypi`, `npmjs`) per `CLARIFY_0.md`.
- Publishes Node packages from tarballs produced by the pack workflow.

Must-fix:

- See #1: attestation jobs exist but do not gate release creation.

## Overall verdict

**Conditional approval**: The structure and direction are good and align with the clarifications, but please address the blocking issues (especially attestation gating, and the likely permission gap for package installs) before merging.
