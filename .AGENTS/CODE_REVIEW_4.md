# Code Review: `.github` changes (origin/main...HEAD)

<!-- markdownlint-disable MD013 -->
<!-- markdownlint-disable MD044 -->

Date: 2026-01-04
Branch: dev/shuaizhang/refactor-buddy-official
Scope: `/.github/workflows/*.yml` (root workflows only)

Changed files (from `git diff origin/main...HEAD --name-only -- .github`):

- `.github/workflows/buddy.yml`
- `.github/workflows/official.yml`
- `.github/workflows/release-build-node-pack.yml` (new)
- `.github/workflows/release-build-python.yml` (new)
- `.github/workflows/release-build-wxt.yml` (new)
- `.github/workflows/release-resolve.yml` (new)

Decision references consulted (per instruction):

- `.AGENTS/CLARIFY_0.md` – `.AGENTS/CLARIFY_4.md`
- `.AGENTS/CLARIFY_CR_0.md` (confirmed)
- `.AGENTS/CLARIFY_CR_1.md` (confirmed)
- `.AGENTS/CLARIFY_CR_2_5.md` (confirmed)

Diff hygiene:

- `git diff --check origin/main...HEAD -- .github`: no whitespace errors observed.

---

## Overall assessment

This change set is cohesive, policy-aligned, and improves release workflow correctness and maintainability:

- Release input resolution is centralized into `release-resolve.yml` using an explicit `workflow_call` contract (`source=tag|manual`, `run_url` pass-through), avoiding the reusable-workflow event context trap (matches `.AGENTS/CLARIFY_3.md`).
- The release asset contract is standardized to `${GITHUB_WORKSPACE}/out` with `out/*` as the complete set; workflows no longer depend on `dist_dir/dist_glob` outputs (matches `.AGENTS/CLARIFY_0.md` / `.AGENTS/CLARIFY_1.md`).
- Node releases are strictly **pack first, publish from tarball**, with deterministic tarball names `out/gpr.tgz` and `out/npmjs.tgz` (matches `.AGENTS/CLARIFY_4.md`).
- npm dist-tag derivation is single-sourced from NBGV prerelease metadata and validated using `^[a-z0-9][a-z0-9-]*$` (matches `.AGENTS/CLARIFY_1.md` and `.AGENTS/CLARIFY_CR_0.md`).
- Buddy implements the non-clobber guard and gates all build/pack/publish work behind it (matches `.AGENTS/CLARIFY_CR_2_5.md`).
- Official splits attestations into dedicated jobs and gates GitHub Release creation on both publishing and attestation (matches `.AGENTS/CLARIFY_CR_0.md` and `.AGENTS/CLARIFY_CR_1.md`).

Verdict: ✅ Approved.

---

## Blocking issues

None found in the current `.github` diff.

---

## High-impact non-blocking notes

### 1) `official.yml` / `buddy.yml`: minor consistency drift in “tool versions source of truth” usage

**Files:**

- `.github/workflows/official.yml`
- `.github/workflows/buddy.yml`

**What:**

Both workflows introduce a `versions` job exporting `python_version/node_version/pnpm_version` as outputs (as required by `.AGENTS/CLARIFY_0.md`). However, some later steps still read versions from `env.NODE_VERSION` instead of `needs.versions.outputs.node_version`.

**Impact:**

Not a functional bug today (same values), but it weakens the “single source of truth” discipline and makes future edits easier to get subtly wrong.

**Suggestion:**

Prefer `needs.versions.outputs.*` everywhere in jobs that already depend on `versions`.

**Triage:** True positive.

**Resolution:** Fixed in this branch by updating the Node publish jobs to read `node-version` from `needs.versions.outputs.node_version` (and explicitly adding `versions` to the `needs` list where required).

---

### 2) `buddy.yml`: non-clobber guard uses brittle 404 detection

**File:** `.github/workflows/buddy.yml`

**What:**

The guard treats “no release exists yet” as allow by grepping `"HTTP 404"` from `gh api` stderr.

**Impact:**

Likely stable in practice, but string-based and could break if `gh` output changes.

**Suggestion:**

If you want to harden it, prefer parsing status codes (e.g., `gh api --include` and parse the HTTP status line).

**Triage:** True positive.

**Resolution:** Fixed in this branch by replacing string-grep based `"HTTP 404"` detection with an HTTP status line parse via `gh api --include`, and extracting `.prerelease` from the JSON body in the success case.

---

### 3) `release-build-node-pack.yml`: implicit npm feature assumption (`--pack-destination`)

**File:** `.github/workflows/release-build-node-pack.yml`

**What:**

The pack steps rely on `npm pack --pack-destination`.

**Impact:**

This is fine with the repo’s current Node/npm versions, but it can fail on older npm (e.g., if a future change pins Node down or a self-hosted runner differs).

**Suggestion:**

Optional: include a compatibility fallback (as older code did) or document the minimum Node/npm version expectation.

**Triage:** False positive (risk accepted).

**Rationale:** The root workflows pin Node to `24` via the entry workflow tool-version contract, and `actions/setup-node@v6` provides an npm version that supports `npm pack --pack-destination` on `ubuntu-latest`.

**Resolution:** No change.

---

## File-by-file review notes

### `.github/workflows/release-resolve.yml` (new)

Strengths:

- Explicit `source=tag|manual` avoids relying on `github.event_name` inside reusable workflows.
- `run_url` pass-through preserves the release notes link to the entry workflow run.
- Normalizes boolean-like outputs to `'true'|'false'`.
- Resolves the target SHA deterministically and checks out the target before running helper scripts.

Minor notes:

- After `actions/checkout(fetch-depth: 0)`, it also does `git fetch --tags` and `git fetch --all`; correct but potentially redundant/heavier.

### `.github/workflows/release-build-node-pack.yml` (new)

Strengths:

- NBGV-based dist-tag derivation + strict validation with a clear failure mode.
- Runs default quality checks via `pnpm --if-present`.
- Deterministic output tarball naming (`out/gpr.tgz`, `out/npmjs.tgz`).
- GPR scope rewrite uses lowercase owner (matches `.AGENTS/CLARIFY_CR_0.md` #7).

Notes:

- `npm pack --ignore-scripts` is security-forward; ensure required build artifacts are produced by explicit steps (quality checks + `prepack`) and not by npm lifecycle scripts.

### `.github/workflows/release-build-python.yml` (new)

Strengths:

- Clean build isolation (`rm -rf out`), and version verification is built in.

### `.github/workflows/release-build-wxt.yml` (new)

Strengths:

- Explicit browser list `chrome firefox edge` (matches `.AGENTS/CLARIFY_CR_1.md` #1).
- Runs standard quality checks before producing ZIPs (matches `.AGENTS/CLARIFY_CR_1.md` #2).
- Enforces shallow `.output/*.zip` collection and collision safety (matches `.AGENTS/CLARIFY_1.md` #5).

Potential rough edge:

- Projects must provide either `zip:<browser>` scripts or `scripts/nbgv-version.mjs`. This is acceptable (fail-fast + actionable error), but worth documenting as the minimal WXT release contract.

### `.github/workflows/official.yml`

Strengths:

- PyPI/npmjs Trusted Publishing stays in `official.yml` with environment gating (matches `.AGENTS/CLARIFY_0.md` #4/#5/#6).
- GitHub Release creation is gated on publish + attestation for Python/Node/WXT.
- Publishing from tarballs ensures the GitHub Release artifacts match what was published.

### `.github/workflows/buddy.yml`

Strengths:

- Non-clobber guard blocks buddy from touching `prerelease=false` releases.
- Guard is an early gate for build/pack/publish (matches `.AGENTS/CLARIFY_CR_2_5.md`).
- GPR publishing uses a step-scoped `.npmrc` + `NPM_CONFIG_USERCONFIG` (matches `.AGENTS/CLARIFY_CR_1.md` #3).

---

## Suggested validation checklist

- Run a workflow linter (e.g., `actionlint`) to validate YAML and expressions.
- Validate these scenarios (ideally in a fork/test repo):
    - Official tag push (Python): build → publish (PyPI OIDC) → attest → create release
    - Official tag push (Node): pack → publish (GPR + npmjs OIDC) → attest → create release
    - Buddy manual (Node): guard allow vs guard block; verify guard blocks build/pack/publish and release jobs
    - WXT: verify chrome/firefox/edge zips are produced and `.output/*.zip` is collected into `out/`
