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

Decision references consulted:

- `.AGENTS/CLARIFY_0.md` – `.AGENTS/CLARIFY_4.md`
- `.AGENTS/CLARIFY_CR_0.md` (confirmed)
- `.AGENTS/CLARIFY_CR_1.md` (confirmed)
- `.AGENTS/CLARIFY_CR_2_5.md` (confirmed)

Diff hygiene:

- `git diff --check origin/main...HEAD -- <files>`: **no whitespace errors** observed.

---

## Overall summary

This refactor is directionally correct and aligns with the confirmed constraints:

- Centralizing “resolve release inputs / detect project / normalize outputs” into the reusable workflow `release-resolve.yml` matches the explicit `workflow_call` input contract (see `CLARIFY_3.md`).
- Standardizing the artifact directory to `${GITHUB_WORKSPACE}/out` and treating `out/*` as the release asset contract matches `CLARIFY_1.md` / `CLARIFY_0.md`.
- The Node flow is now **pack first, publish from tarball** (and renames tarballs deterministically to `out/gpr.tgz` / `out/npmjs.tgz`), matching `CLARIFY_4.md`.
- `dist-tag` derivation is based on NBGV `PrereleaseVersionNoLeadingHyphen` with strict validation, matching `CLARIFY_1.md` and `CLARIFY_CR_0.md`.
- Buddy adds a non-clobber guard and gates all side-effect jobs behind it, matching `CLARIFY_CR_2_5.md`.
- Official adds dedicated attestation jobs and gates GitHub Release creation on both publishing and attestation, matching `CLARIFY_CR_0.md` #3 and `CLARIFY_CR_1.md` #4.

One item was a real compatibility risk for the **official tag-push path** and has been fixed (see Blocking #1 below).

---

## Blocking

### 1) `official.yml`: referencing `inputs.*` in the `push` path can break expression evaluation

**Verdict:** ✅ True positive

**Fix status:** ✅ Fixed in `.github/workflows/official.yml`

**File:** `.github/workflows/official.yml`

**Observed (before fix):**

- The workflow supports both `push` (tag) and `workflow_dispatch`.
- Multiple expressions referenced `inputs.project` / `inputs.version` / `inputs.target` / `inputs.force_update_tag`, for example:
    - `concurrency.group`: `...-${{ inputs.project || 'push' }}`
    - `resolve` job `with:`: `project: ${{ inputs.project }}`

**Risk:**

- In GitHub Actions, the `inputs` context is primarily for `workflow_dispatch` / `workflow_call`.
- On `push` runs, `inputs` can be unavailable and may cause expression evaluation failures (instead of yielding empty values).
- This would break the critical official tag-push release path.

**What was changed (implementation):**

- Replaced all direct `inputs.*` references in `official.yml` with `github.event.inputs.*`.
- Normalized `force_update_tag` to a boolean expression (`== 'true' || == true`) before passing it to the reusable workflow.

This avoids relying on the `inputs` context in event types where it may be unavailable.

---

## Important (suggested / confirm intent)

### 2) `release-build-wxt.yml`: fallback requires `scripts/nbgv-version.mjs` (clarify minimum WXT release requirements)

**Verdict:** ⚠️ False positive (accepted behavior / not a defect)

**File:** `.github/workflows/release-build-wxt.yml`

**Current behavior:**

- Prefer package scripts `zip:<browser>`.
- For Chrome, a plain `zip` script is accepted only if it clearly runs `wxt zip -b chrome`.
- If scripts are missing, the workflow requires `./scripts/nbgv-version.mjs` and uses it to run `wxt zip -b <browser>` explicitly.

**Pros:**

- Matches `CLARIFY_CR_1.md` #1 (explicit chrome/firefox/edge) and #2 (quality checks).
- Supports “version stamping at pack time” via the helper script, which is useful when packages have placeholder versions.

**Potential issue:**

- New or not-yet-migrated WXT projects will hard fail if they provide neither `zip:<browser>` scripts nor `scripts/nbgv-version.mjs`.

**Assessment rationale:**

- The workflow already fails fast with actionable diagnostics when neither `zip:<browser>` scripts nor `scripts/nbgv-version.mjs` exist.
- That failure mode is consistent with the policy of being explicit about browser targets (see `CLARIFY_CR_1.md` #1) and is preferable to silently producing incomplete artifacts.

**Optional follow-up (documentation-only):** add a short “WXT release requirements” note somewhere user-facing (e.g. `README.md` or a dedicated release doc). This is useful, but not required to fix correctness.

---

## File-by-file notes

### `.github/workflows/release-resolve.yml` (new)

Strengths:

- Clear `source=tag|manual` input contract, does not infer mode from `github.event_name` (matches `CLARIFY_3.md`).
- `run_url` is passed through so release notes can link back to the entry workflow run (matches `CLARIFY_3.md` #2).
- Checks out the resolved target commit via `git checkout --detach "${target}"` before detection/validation.
- Normalizes boolean-like outputs (e.g. `force_update_tag`) to `'true'|'false'`, making downstream comparisons deterministic.

Potential improvement (non-blocking):

- After `checkout(fetch-depth: 0)`, the workflow does `git fetch --tags` and `git fetch --all`. This is correct but may be heavier than necessary if performance becomes a concern.

### `.github/workflows/release-build-node-pack.yml` (new)

Strengths:

- Dist-tag derives from NBGV prerelease metadata, and fails fast when `version` looks like a prerelease but metadata is empty (matches `CLARIFY_4.md` #2).
- Runs `lint/typecheck/test/build` by default with `--if-present` (matches `CLARIFY_1.md`).
- Produces deterministic tarball names (`out/gpr.tgz`, `out/npmjs.tgz`) (matches `CLARIFY_4.md`).
- Separation of pack vs publish enforces a consistent “publish from tarball” discipline.

Note:

- Using `npm pack --ignore-scripts` is a security-forward choice. Ensure packages that require build output either run build steps explicitly before packing or use a `prepack`-equivalent flow that does not rely on lifecycle scripts.

### `.github/workflows/release-build-python.yml` (new)

Strengths:

- `uv build --out-dir out` plus `verify_python_artifact_version.py` enforces version correctness.
- Minimal permissions.

### `.github/workflows/release-build-wxt.yml` (new)

Strengths:

- Explicit browser matrix `chrome firefox edge` (matches `CLARIFY_CR_1.md` #1).
- Runs default quality checks (matches `CLARIFY_CR_1.md` #2).
- Only collects `.output/*.zip` (shallow) and checks for basename collisions (matches `CLARIFY_1.md` #5).

### `.github/workflows/buddy.yml`

Strengths:

- `guard-non-clobber` uses `gh api .../releases/tags/<tag>` and blocks when `prerelease=false` (matches `CLARIFY_CR_0.md` #5).
- Guard is placed as an early gate via `needs` for build/pack/publish.
- GPR publish uses a step-scoped `.npmrc` + `NPM_CONFIG_USERCONFIG` (matches `CLARIFY_CR_1.md` #3).
- Buddy Node publishes from a tarball (`out/gpr.tgz`), matching the contract.

Potential improvement (non-blocking):

- The `HTTP 404` detection relies on `gh` error text. If this becomes flaky, consider parsing status codes.

### `.github/workflows/official.yml`

Strengths:

- PyPI/npmjs publishing remains in `official.yml` with environment gating (matches `CLARIFY_0.md` #4/#5/#6).
- Attestation is done in dedicated jobs and gates release creation.
- Node publishing uses tarballs produced by the pack workflow.

Resolved risk:

- The `inputs.*` context risk in the `push` path is fixed (see Blocking #1).

---

## Suggested validation checklist

- Run `actionlint` (or equivalent) for workflow syntax and expression validation (focus on `official.yml`).
- Validate these scenarios (ideally in a fork/test repo):
    - Official tag push (Python): build → publish → attest → create release
    - Official tag push (Node): pack → publish (GPR + npmjs OIDC) → attest → create release
    - Buddy manual (Node): both guard-allow and guard-block paths; ensure guard failure prevents publishing
    - WXT: confirm chrome/firefox/edge zips are produced and `.output/*.zip` is collected into `out/`
