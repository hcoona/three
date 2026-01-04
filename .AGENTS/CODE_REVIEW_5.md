# Code Review: `.github` changes (origin/main...HEAD)

<!-- markdownlint-disable MD013 -->
<!-- markdownlint-disable MD044 -->

Date: 2026-01-04
Branch: dev/shuaizhang/refactor-buddy-official
Scope: `/.github/workflows/*.yml` (root workflows only)

Changed files (from `git diff origin/main...HEAD --name-only -- .github`):

- `.github/workflows/buddy.yml`
- `.github/workflows/official.yml`
- `.github/workflows/release-build-node-pack.yml`
- `.github/workflows/release-build-python.yml`
- `.github/workflows/release-build-wxt.yml`
- `.github/workflows/release-resolve.yml`

Decision references consulted (per instruction):

- `.AGENTS/CLARIFY_0.md` – `.AGENTS/CLARIFY_4.md`
- `.AGENTS/CLARIFY_CR_0.md` (confirmed)
- `.AGENTS/CLARIFY_CR_1.md` (confirmed)
- `.AGENTS/CLARIFY_CR_2_5.md` (confirmed)

Diff hygiene:

- `git diff --check origin/main...HEAD -- .github`: no whitespace errors observed.

---

## Overall assessment

This is a high-quality, policy-aligned refactor of the release pipelines.

Key improvements that match the confirmed decisions:

- **Single-sourced release input resolution** via `release-resolve.yml` with an explicit `workflow_call` contract (`source=tag|manual`, `run_url` pass-through), avoiding the reusable-workflow event-context trap (matches `.AGENTS/CLARIFY_3.md`).
- **Stable artifact contract**: `${GITHUB_WORKSPACE}/out` and `out/*` as the complete release asset set (matches `.AGENTS/CLARIFY_0.md` / `.AGENTS/CLARIFY_1.md`).
- **Node release correctness**: pack-first and publish-from-tarball, plus deterministic tarball names `out/gpr.tgz` and `out/npmjs.tgz` (matches `.AGENTS/CLARIFY_4.md`).
- **Npm dist-tag derivation** from NBGV `PrereleaseVersionNoLeadingHyphen` with strict validation `^[a-z0-9][a-z0-9-]*$` (matches `.AGENTS/CLARIFY_1.md` and `.AGENTS/CLARIFY_CR_0.md`).
- **Buddy non-clobber safety**: guard blocks touching existing `prerelease=false` releases and is an early gate for build/pack/publish work (matches `.AGENTS/CLARIFY_CR_2_5.md`).
- **Official attestation gating**: attestation jobs exist and GitHub Release creation depends on publish + attestation (matches `.AGENTS/CLARIFY_CR_0.md` and `.AGENTS/CLARIFY_CR_1.md`).
- **Trusted publishing identity stability**: PyPI/npmjs publishing stays in `official.yml` with `environment: pypi` / `environment: npmjs` (matches `.AGENTS/CLARIFY_0.md` #4/#5/#6).

Verdict: ✅ Approved.

---

## Blocking issues

None found.

---

## High-impact non-blocking notes

### 1) `buddy.yml`: `jq` dependency is implicit in the non-clobber guard

**File:** `.github/workflows/buddy.yml`

The guard uses `jq` to parse `.prerelease`, but does not install it. This is likely fine on `ubuntu-latest` (jq is typically present), but it is still an implicit runtime dependency.

Suggested hardening options (pick one, no policy change required):

- Use `gh api ... --jq '.prerelease'` and avoid `jq` entirely.
- Or add a tiny “Install jq” step (matching the pattern used in `release-resolve.yml` / `release-build-wxt.yml`).

### 2) `release-resolve.yml`: redundant fetching (correct but potentially heavier)

**File:** `.github/workflows/release-resolve.yml`

After `actions/checkout@v6` with `fetch-depth: 0`, the workflow does:

- `git fetch --force --tags`
- `git fetch --force --prune --all`

This is correct and deterministic, but may be more network IO than needed. If this workflow becomes a bottleneck, consider fetching only what you need (e.g., tags + the referenced commit/ref).

### 3) npm Trusted Publishing (OIDC): ensure runner/npm constraints are understood

**File:** `.github/workflows/official.yml`

The workflow correctly uses:

- `environment: npmjs`
- `permissions: id-token: write`
- `npm publish` without an npm token

This matches npm Trusted Publishing (OIDC). Per npm docs, Trusted Publishing:

- requires GitHub-hosted runners (not self-hosted), and
- requires npm CLI >= 11.5.1,
- automatically generates provenance when using OIDC (no need for `--provenance` in this mode).

Given `NODE_VERSION: '24'` and `runs-on: ubuntu-latest`, this should be satisfied.

---

## File-by-file highlights

### `.github/workflows/release-build-node-pack.yml`

- ✅ Runs quality checks by default with `--if-present`.
- ✅ Validates the computed NBGV version equals the resolved release version.
- ✅ Produces deterministic tarballs (`gpr.tgz` / `npmjs.tgz`) and publishes from the tarball in entry workflows.
- ✅ Avoids GitHub Packages auth for dependency installation (matches `.AGENTS/CLARIFY_CR_0.md` #2).

### `.github/workflows/release-build-wxt.yml`

- ✅ Explicit browser matrix `chrome firefox edge` (matches `.AGENTS/CLARIFY_CR_1.md` #1).
- ✅ Runs quality checks before producing zips (matches `.AGENTS/CLARIFY_CR_1.md` #2).
- ✅ Shallow `.output/*.zip` collection + collision-safe copy into flat `out/` (matches `.AGENTS/CLARIFY_1.md` #5).

### `.github/workflows/buddy.yml` / `.github/workflows/official.yml`

- ✅ Minimal top-level permissions (`contents: read`) and job-scoped elevation only when needed.
- ✅ `actions: read` is present for jobs downloading artifacts under restricted tokens.
- ✅ Environment gating is used where identity matters (`pypi`, `npmjs`).

---

## Suggested validation checklist

- Run a workflow linter (e.g., `actionlint`) to validate expressions.
- Validate key end-to-end paths:
    - Official tag push (Python): build → publish (PyPI OIDC) → attest → GitHub Release
    - Official tag push (Node): pack → publish (GPR + npmjs OIDC) → attest → GitHub Release
    - Buddy manual (Node): guard allow vs guard block; ensure blocked runs do not publish to GPR
    - WXT: verify chrome/firefox/edge zips exist and are collected into `out/`
