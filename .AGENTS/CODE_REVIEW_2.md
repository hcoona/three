# Code Review: .github workflow changes (origin/main...HEAD)

<!-- markdownlint-disable MD013 -->

Date: 2026-01-04
Branch: dev/shuaizhang/refactor-buddy-official
Scope: `/.github/workflows/*` (root workflows only)

Changed files:

- `.github/workflows/buddy.yml`
- `.github/workflows/official.yml`
- `.github/workflows/release-build-node-pack.yml` (new)
- `.github/workflows/release-build-python.yml` (new)
- `.github/workflows/release-build-wxt.yml` (new)
- `.github/workflows/release-resolve.yml` (new)

Decision references (expected to match behavior):

- `.AGENTS/CLARIFY_0.md` – `.AGENTS/CLARIFY_4.md`
- `.AGENTS/CLARIFY_CR_0.md` (confirmed)
- `.AGENTS/CLARIFY_CR_1.md` (confirmed decisions listed inline)

---

## Overall assessment

This refactor is directionally strong and substantially improves maintainability:

- Standardizes the artifact contract to `${GITHUB_WORKSPACE}/out` and `out/*` across build/release flows.
- Centralizes resolution logic into a reusable workflow with explicit inputs (avoids `workflow_call` context traps).
- Aligns Node releases with “pack first, publish from tarball”, improving auditability.
- Enforces buddy “non-clobber” protection for existing official GitHub Releases.
- Implements deterministic tarball naming (`out/gpr.tgz`, `out/npmjs.tgz`) and NBGV-based dist-tag derivation.

There are, however, a couple of policy mismatches that should be treated as **blocking**, plus several smaller correctness/robustness issues.

---

## Blocking issues (must fix before merge)

### 1) WXT browser set must be explicit (Chrome required)

**Files:** `.github/workflows/release-build-wxt.yml`

**Decision:** `.AGENTS/CLARIFY_CR_1.md` Decision #1 = **B** (be explicit; run `wxt zip -b chrome` and define the complete browser list explicitly).

**What I see now:**

- The workflow runs `wxt zip` (implicit default) and then `wxt zip -b firefox` and `wxt zip -b edge`.

**Why this is blocking:**

- This violates the explicit-browser decision. If WXT defaults change (or default is not Chrome), we may silently ship the wrong artifacts.

**Fix recommendation:**

- Replace the implicit run with an explicit browser list, e.g. iterate `chrome firefox edge` and run `wxt zip -b <browser>` (and/or call `zip:<browser>` scripts if present).

**Status:** ✅ True positive — **fixed**

**Applied fix:**

- Updated `.github/workflows/release-build-wxt.yml` to use an explicit browser list `chrome firefox edge`.
- Removed reliance on implicit `wxt zip` defaults by invoking `wxt zip -b <browser>` when a per-browser script is not present.

---

### 2) WXT builds must run standard Node quality checks by default

**Files:** `.github/workflows/release-build-wxt.yml`

**Decision:** `.AGENTS/CLARIFY_CR_1.md` Decision #2 = **A** (run `pnpm --filter <project> --if-present lint|typecheck|test|build`).

**What I see now:**

- `release-build-wxt.yml` installs dependencies and produces zip artifacts but does **not** run the standard quality checks.

**Why this is blocking:**

- This is a confirmed policy requirement and is important for “official artifacts are built from checked sources” discipline.

**Fix recommendation:**

- Add the same quality check step used in `release-build-node-pack.yml`, ideally before producing zips:
    - `pnpm --filter "${PROJECT}" --if-present lint`
    - `pnpm --filter "${PROJECT}" --if-present typecheck`
    - `pnpm --filter "${PROJECT}" --if-present test`
    - `pnpm --filter "${PROJECT}" --if-present build`

**Status:** ✅ True positive — **fixed**

**Applied fix:**

- Added a dedicated "Run quality checks" step to `.github/workflows/release-build-wxt.yml` using the standard `pnpm --filter ... --if-present` pattern.

---

## Important issues (should fix)

### 3) Buddy non-clobber guard does not gate build/publish steps (wasted work and possible side-effects)

**Files:** `.github/workflows/buddy.yml`

**Decision context:** `.AGENTS/CLARIFY_1.md` requires buddy must fail fast when an existing **non-prerelease** GitHub Release exists for the tag.

**What I see now:**

- `guard-non-clobber` runs, but:
    - `build-python`, `pack-node`, and `build-wxt` do not depend on it.
    - `publish-node-gpr` also does not depend on it.

**Risk:**

- If a run is doomed (because an official release already exists), we still spend CI time building/packing, and may publish to GPR before the later release job fails.

**Recommendation:**

- Consider adding `guard-non-clobber` to the `needs` of build/pack/publish jobs so the workflow fails early and avoids unnecessary side effects.

**Status:** ❌ False positive — already fixed in the current branch

**Notes:**

- In `.github/workflows/buddy.yml`, the relevant jobs already include `guard-non-clobber` in `needs` (e.g. `build-python`, `pack-node`, `publish-node-gpr`, `build-wxt`).
- `prepare-release-notes` does not depend on the guard, but it is read-only and does not publish or mutate releases.

---

### 4) Official workflow uses `github.event.inputs.*` instead of `inputs.*` (non-blocking but cleaner)

**Files:** `.github/workflows/official.yml`

**What I see now:**

- For `workflow_dispatch`, the workflow reads values via `github.event.inputs.project/version/target/force_update_tag`.

**Recommendation:**

- Prefer `inputs.project`, `inputs.version`, etc. This is more idiomatic and avoids surprising type/string comparisons.

(If the current style is intentional, it is still functional; treat as cleanup.)

**Status:** ✅ True positive (cleanup) — **fixed**

**Applied fix:**

- Updated `.github/workflows/official.yml` to use `inputs.project|version|target|force_update_tag` for `workflow_dispatch` input access.

---

## Notes by file

### `.github/workflows/release-resolve.yml`

Good:

- Uses explicit `source=tag|manual` inputs and requires `ref_name` and `ref` for tag mode (matches `.AGENTS/CLARIFY_3.md` / `.AGENTS/CLARIFY_4.md`).
- Normalizes stringly-typed outputs (notably `force_update_tag` as strict `'true'|'false'`).
- Checks out the resolved `target` commit before detection/validation, avoiding “validate HEAD but release another SHA” mistakes.

Minor suggestion:

- The combination of `checkout(fetch-depth: 0)` plus `git fetch --tags` and `git fetch --all` is correct but heavy; consider narrowing later if runtime is a concern.

### `.github/workflows/release-build-node-pack.yml`

Good:

- Dist-tag derivation is sourced from NBGV metadata (`PrereleaseVersionNoLeadingHyphen`) and fails fast on mismatches (matches `.AGENTS/CLARIFY_4.md` + `.AGENTS/CLARIFY_CR_0.md`).
- Standard quality checks run by default with `--if-present` (matches `.AGENTS/CLARIFY_1.md`).
- Produces deterministic tarball names `out/gpr.tgz` and `out/npmjs.tgz` (matches `.AGENTS/CLARIFY_4.md`).
- Avoids wiring GitHub Packages during dependency install (matches `.AGENTS/CLARIFY_CR_0.md` Decision #2).

### `.github/workflows/release-build-python.yml`

Good:

- Builds into `${GITHUB_WORKSPACE}/out` and verifies built artifacts match the expected PEP440 version.
- Minimal permissions.

### `.github/workflows/buddy.yml`

Good:

- Implements buddy non-clobber guard via `gh api .../releases/tags/<tag>` and blocks on `prerelease=false` (matches `.AGENTS/CLARIFY_1.md` + `.AGENTS/CLARIFY_CR_0.md` Decision #5).
- Publishes to GPR from the packed tarball (`out/gpr.tgz`) using an explicit step-scoped `.npmrc` (matches `.AGENTS/CLARIFY_CR_1.md` Decision #3).

### `.github/workflows/official.yml`

Good:

- Keeps PyPI and npmjs publish steps in `official.yml` and uses GitHub Environments (`pypi`, `npmjs`) per `.AGENTS/CLARIFY_0.md`.
- Creates GitHub attestations in `official.yml` and gates GitHub Release creation on attestation jobs (matches `.AGENTS/CLARIFY_CR_0.md` Decision #3 and `.AGENTS/CLARIFY_CR_1.md` Decision #4).
- Node publish uses tarballs produced by pack workflow and publishes to GPR and npmjs.

Caution:

- Tokenless npm publishing relies on correct npm Trusted Publisher (OIDC) configuration; this is expected, but please verify the exact failure mode and ensure maintainers have clear setup guidance (the current note helps).

---

## Suggested follow-up validation

- Run an Actions workflow lint (e.g., `actionlint`) locally or in CI.
- Dry-run the following scenarios in a test repo/fork:
    - Official tag push for Python: attest + publish + create GitHub Release.
    - Official tag push for Node: pack (both tarballs) + publish (GPR + npmjs OIDC) + attest + create GitHub Release.
    - Buddy manual run for Node: guard blocks when an official release exists; otherwise pack + publish to GPR + create prerelease.
    - WXT build: ensure explicit Chrome build is produced after fixing browsers, and quality checks run.

---

## Recommendation

**Previously blocking items are now addressed.**

- WXT now uses an explicit browser list including Chrome.
- WXT now runs the standard Node quality checks by default.
- Buddy jobs are already gated on the non-clobber guard.

Remaining recommendation: optionally keep the `inputs.*` cleanup (now applied) and run a workflow lint / dry-run validation for confidence.
