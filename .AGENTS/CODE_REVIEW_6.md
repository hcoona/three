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

This change set is well-structured and closely follows the confirmed policy decisions. In particular:

- ✅ Centralizes resolution logic in `release-resolve.yml` with an explicit `workflow_call` contract (`source=tag|manual`, `run_url` pass-through), avoiding the reusable-workflow event-context pitfall.
- ✅ Standardizes the artifact contract on `${GITHUB_WORKSPACE}/out` and `out/*`.
- ✅ Implements deterministic tarball naming for Node (`out/gpr.tgz`, `out/npmjs.tgz`) and publishes from tarballs.
- ✅ Derives npm dist-tags from NBGV metadata (`PrereleaseVersionNoLeadingHyphen`) with the confirmed validation rule `^[a-z0-9][a-z0-9-]*$`.
- ✅ Enforces the buddy non-clobber guard and (critically) uses it as an early gate for build/pack/publish work.
- ✅ Keeps PyPI + npmjs Trusted Publishing identities explicit in `official.yml` with `environment: pypi` / `environment: npmjs`.
- ✅ Makes official GitHub Release creation depend on publish + attestation (mandatory gating as confirmed).

Verdict: ✅ Approved, with one policy/compatibility clarification requested (see `.AGENTS/CLARIFY_CR_6.md`).

---

## Potentially impactful issue (needs confirmation)

### 1) `release-resolve.yml`: “PEP 440” claim vs shell-safety pre-validation

**File:** `.github/workflows/release-resolve.yml`

The workflow advertises:

- `version`: “Python: PEP440 (leading v allowed; will be stripped).”

However it applies a pre-validation “shell safety” check before running `validate_pep440_version.py`:

- `^[A-Za-z0-9][A-Za-z0-9._+-]*$`

This rejects some valid PEP 440 versions, notably epochs (e.g. `1!1.0`). The validator (`packaging.version.Version`) _does_ accept epochs.

**Why it matters:** if any Python project ever uses an epoch in its release version (or if a future versioning policy introduces it), official/buddy manual releases will fail in `release-resolve.yml` before reaching the actual PEP 440 validator.

**Recommendation (low-risk):** either remove the pre-validation regex entirely (since the value is already passed as a quoted CLI argument), or allow `!` when `project_kind == 'python'`.

Requested confirmation is recorded in `.AGENTS/CLARIFY_CR_6.md`.

---

## Non-blocking notes / polish

### 1) `release-resolve.yml`: tag regex could be tightened for clearer errors

The tag-mode regex `^release/.+/v.+$` allows project segments containing `/`, but later validation rejects `/` in `project` via `^[A-Za-z0-9._-]+$`.

Not a correctness issue (it fails safely), but tightening the tag regex to match the project constraint would make failures more actionable.

### 2) `buddy.yml`: guard implementation is deterministic but depends on `gh`

The non-clobber guard relies on `gh api ... --include` and parses the HTTP status line from the captured output.

This is acceptable on GitHub-hosted runners (gh is present), but if a future change runs these workflows on a different runner image, it would be worth explicitly ensuring `gh` availability or switching to a pure-HTTP approach.

---

## File-by-file highlights

### `.github/workflows/buddy.yml`

- ✅ Early non-clobber guard gating build/pack/publish jobs (aligned with `.AGENTS/CLARIFY_CR_2_5.md`).
- ✅ Buddy Node publishes to GitHub Packages from `out/gpr.tgz` with a step-scoped `.npmrc`.

### `.github/workflows/official.yml`

- ✅ Publishing stays in `official.yml` under `environment: pypi` / `environment: npmjs` (Trusted Publishers identity stability).
- ✅ GitHub Release creation depends on publish + attest jobs.

### `.github/workflows/release-build-node-pack.yml`

- ✅ NBGV dist-tag derivation and validation match confirmed policy.
- ✅ Deterministic tarball names and “publish from tarball” flow.

### `.github/workflows/release-build-wxt.yml`

- ✅ Explicit browser matrix `chrome firefox edge`.
- ✅ Shallow `.output/*.zip` collection with overwrite protection.

---

## Suggested validation checklist (operational)

- Validate YAML/expression correctness using an Actions workflow linter (e.g., `actionlint`).
- Exercise key paths:
    - Official tag push (Python): build → publish (PyPI OIDC) → attest → GitHub Release.
    - Official tag push (Node): pack → publish (GPR + npmjs OIDC) → attest → GitHub Release.
    - Buddy manual (Node): guard allow vs guard block; ensure blocked runs do not publish to GPR.
    - WXT: verify `chrome/firefox/edge` zips are produced and copied to `out/`.
