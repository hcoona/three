# Code Review: .github changes (origin/main...HEAD)

<!-- markdownlint-disable MD013 -->

Date: 2026-01-03
Branch: dev/shuaizhang/refactor-buddy-official
Scope: `/.github/workflows/*` (root workflows only)

Changed files:

- `.github/workflows/buddy.yml`
- `.github/workflows/official.yml`
- `.github/workflows/release-build-node-pack.yml` (new)
- `.github/workflows/release-build-python.yml` (new)
- `.github/workflows/release-build-wxt.yml` (new)
- `.github/workflows/release-resolve.yml` (new)

Decision references (must match behavior):

- `.AGENTS/CLARIFY_0.md` – `.AGENTS/CLARIFY_4.md`
- `.AGENTS/CLARIFY_CR_0.md` (confirmed decisions found during CR)

---

## High-level assessment

This refactor is a meaningful improvement in maintainability and consistency:

- Centralizes input resolution (`release-resolve.yml`) and normalizes stringly-typed outputs (especially `force_update_tag`).
- Standardizes the artifact contract on `${GITHUB_WORKSPACE}/out` + `out/*`.
- Splits build/pack from publish, enabling safer, auditable releases.
- Aligns buddy Node flow with “pack first, publish from tarball” (good for provenance and auditability).
- Improves WXT artifact collection according to documented naming reality.

However, there are several correctness/behavioral issues that should be addressed before merging.

---

## Blocking issues (must fix)

### 1) Official releases are not gated on attestations (policy violation)

**Files:** `official.yml`

**What I see:**

- `attest-python`, `attest-node`, `attest-wxt` jobs exist and run, but the corresponding `release-*` jobs do **not** depend on them.
    - `release-python` needs: `resolve`, `prepare-release-notes`, `build-python`, `publish-python`
    - `release-node` needs: `resolve`, `prepare-release-notes`, `pack-node`, `publish-node`
    - `release-wxt` needs: `resolve`, `prepare-release-notes`, `build-wxt`

**Why this is a blocker:**

- `.AGENTS/CLARIFY_CR_0.md` (Decision #3) explicitly confirms: _“Attestation is mandatory for official releases and must block `release-_` when it fails.”\*
- With current wiring, an attestation failure still allows GitHub Release creation, violating the confirmed policy.

**Fix recommendation:**

- Add the corresponding `attest-*` job to each `release-*` job’s `needs` list:
    - `release-python` should need `attest-python`
    - `release-node` should need `attest-node`
    - `release-wxt` should need `attest-wxt`

Optionally also consider whether `publish-*` should depend on `attest-*` (policy did not require it, but be explicit if desired).

**Assessment:** True positive.

**Fix applied:** `official.yml` now gates `release-python`, `release-node`, and `release-wxt` on their corresponding `attest-*` jobs.

---

### 2) Buddy GPR publish auth is likely broken (NODE_AUTH_TOKEN scope)

**File:** `buddy.yml`

**What I see:**

- `publish-node-gpr` uses `actions/setup-node@v6` with `env: NODE_AUTH_TOKEN: ${{ github.token }}` on the **setup** step.
- The actual publish step runs `npm publish ...` without setting `NODE_AUTH_TOKEN`.

**Why this is a blocker:**

- The common/robust pattern is to set `NODE_AUTH_TOKEN` on the `npm publish` step itself. If `setup-node` writes a `.npmrc` that references `${NODE_AUTH_TOKEN}` (rather than embedding the value), the publish step will fail with auth errors.

**Fix recommendation:**

- Make auth unambiguous by either:
    1. Setting `NODE_AUTH_TOKEN: ${{ github.token }}` on the publish step, or
    2. Writing an explicit `.npmrc` file (similar to `official.yml`) and using `NPM_CONFIG_USERCONFIG` in the publish step.

**Assessment:** True positive.

**Fix applied:** `buddy.yml` now writes an explicit `.npmrc` for GitHub Packages and sets both `NODE_AUTH_TOKEN` and `NPM_CONFIG_USERCONFIG` on the `npm publish` step.

---

### 3) Node pack workflow still wires GitHub Packages registry for dependency install (contradicts confirmed CR decision)

**File:** `release-build-node-pack.yml`

**What I see:**

- It creates an `.npmrc` that routes the repository-owner scope to `npm.pkg.github.com` and uses it for `pnpm install`.

**Why this is a blocker (given confirmed decisions):**

- `.AGENTS/CLARIFY_CR_0.md` (Decision #2) confirms: _“We do not expect dependencies to be pulled from GitHub Packages during install. The GitHub Packages .npmrc wiring should be removed during install to avoid confusing failures.”_
- Keeping this wiring can introduce unexpected failures (e.g., if any dependency happens to match the owner scope, it will route to GPR and require `packages: read`, which the job does not have).

**Fix recommendation:**

- Remove the “Create .npmrc for GitHub Packages (dependency install)” step and do a normal `pnpm install --frozen-lockfile` against npmjs.
- Keep GitHub Packages auth only in publish jobs (where `packages: write` is granted).

**Assessment:** True positive.

**Fix applied:** `release-build-node-pack.yml` no longer creates a GitHub Packages `.npmrc` for dependency installation; it now runs a normal `pnpm install --frozen-lockfile`.

---

### 4) GitHub Packages scope casing mismatch (confirmed policy: lowercase)

**File:** `release-build-node-pack.yml` (and downstream publish)

**What I see:**

- The workflow derives the scope for `.npmrc` as lowercase `@${OWNER,,}`.
- But it passes `PACKAGE_SCOPE: ${{ github.repository_owner }}` (raw, possibly mixed-case) to `prepare_npm_publish.py`.

**Why this matters:**

- `.AGENTS/CLARIFY_CR_0.md` (Decision #7) confirms lowercase scope is desired.
- If the tarball is packed with an uppercase/mixed-case scope, you risk publishing under an unintended scope/name that doesn’t match registry routing expectations.

**Fix recommendation:**

- Ensure the scope passed to `prepare_npm_publish.py` is lowercased.
    - Either compute a lowercase value in-shell and pass that,
    - Or guarantee the script normalizes to lowercase and document that contract.

**Assessment:** False positive as a functional bug (the script normalizes scope to lowercase already), but true as a robustness/consistency nit.

**Hardening applied:** `release-build-node-pack.yml` now explicitly lowercases the owner before passing it to `prepare_npm_publish.py`.

---

## Important issues (should fix)

### 5) WXT build browser coverage is implicit

**File:** `release-build-wxt.yml`

**What I see:**

- Runs `wxt zip` (default) and then `wxt zip -b firefox` and `-b edge` (or repo scripts).

**Why to revisit:**

- Depending on WXT defaults, `wxt zip` may or may not generate the desired “default browser” artifacts.
- If Chrome is required explicitly in this repo, consider making it explicit (e.g., include `chrome` in the browser loop), or document why default is sufficient.

This is not necessarily a bug, but it is a potential footgun.

**Assessment:** False positive as a correctness issue (the workflow contract does not currently require explicitly enumerating Chrome). Keep as an optional improvement if requirements change.

---

### 6) `release-resolve.yml` fetch strategy may be heavier than needed

**File:** `release-resolve.yml`

**What I see:**

- Checkout with `fetch-depth: 0`, then `git fetch --force --tags` and `git fetch --force --prune --all`.

**Why to revisit:**

- This is correct but potentially slower than necessary.

**Suggestion:**

- Consider a narrower fetch if runtime becomes an issue, while preserving correctness for tag and manual targets.

**Assessment:** False positive as a correctness issue (this is an optimization suggestion, not a behavioral bug).

---

## Positive notes (keep)

- `release-resolve.yml`:
    - Explicit `source=tag|manual` input avoids `workflow_call` context traps (matches `.AGENTS/CLARIFY_3.md`).
    - Normalizes `force_update_tag` output to strict `'true'|'false'`.
    - Validates project/version and checks out the resolved target commit before running detection scripts.

- `release-build-node-pack.yml`:
    - Dist-tag derivation uses NBGV metadata (`PrereleaseVersionNoLeadingHyphen`) and fails fast on mismatch (matches `.AGENTS/CLARIFY_4.md`).
    - Produces deterministic tarball names (`out/gpr.tgz`, `out/npmjs.tgz`) (matches `.AGENTS/CLARIFY_4.md`).
    - Packs once and publishes from tarballs in caller workflows (good audit trail).

- `release-build-wxt.yml`:
    - Correctly treats WXT zip naming as flexible and only depends on `.output/*.zip` (shallow).
    - Collision detection is explicit and actionable.

- `buddy.yml`:
    - Adds the non-clobber guard before release creation (matches `.AGENTS/CLARIFY_1.md`).
    - Converts `force_update_tag` properly from string output.

---

## Suggested follow-up checks

- Run `actionlint` (or equivalent) to validate YAML and reusable workflow call wiring.
- Specifically validate:
    - `publish-node-gpr` auth works in practice (buddy flow).
    - Official `release-*` jobs are blocked when `attest-*` fails (after adding `needs`).
    - Scope normalization produces the intended package name in tarballs.

---

## Recommendation

**Do not merge as-is.** Fix the four blocking items above first (attestation gating, buddy publish auth, remove GPR install wiring in pack workflow, and enforce lowercase scope consistency). After those are addressed, the overall structure looks solid and aligns well with the clarified invariants.
