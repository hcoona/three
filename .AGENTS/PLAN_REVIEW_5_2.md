<!-- markdownlint-disable MD013 MD024 MD029 -->

# PLAN_REVIEW_5_2: Strict independent review of PLAN_5 (RubyGems support; Trusted Publishing only)

Date: 2026-01-06

Scope: Review `.AGENTS/PLAN_5.md` for correctness, completeness, safety, and implementability with the stated hard requirements:

- RubyGems.org publishing must be **Trusted Publishing (OIDC) only**.
- **No fallback** to RubyGems API key secrets.
- Artifact-first (publish from `out/*` only).
- Idempotent reruns via digest verification.

Non-scope / constraints:

- This is an **independent** review. I did **not** read any `.AGENTS/PLAN_REVIEW_*.md` files.
- I **did** consider maintainer-confirmed policies from `.AGENTS/CLARIFY_PLAN_*.md` as the source of truth.

## Executive summary

PLAN_5 is directionally solid and aligns well with the maintainer-confirmed policies:

- It is explicit about **Trusted Publishing only** for RubyGems.org and forbids any secret/token fallback.
- It adopts a consistent artifact-first, least-privilege, rerun-safe contract across PyPI/npm/RubyGems.
- It captures practical idempotency mechanics for GitHub Packages RubyGems (via `gem fetch`) and RubyGems.org (via API `sha`).

However, there are a few **blocking correctness gaps** that should be fixed in the plan before implementation, mostly around Ruby version/prerelease semantics and RubyGems CLI behavior.

Verdict: **APPROVE WITH REQUIRED CORRECTIONS** (see “Blocking issues”).

## ✅ What is strong / correct

### Trusted Publishing only is clearly enforced

- RubyGems.org flow uses `rubygems/configure-rubygems-credentials` and intends to set `trusted-publisher: true` explicitly.
- No `api-token` is provided; the plan explicitly forbids long-lived RubyGems API keys and any fallback.

This matches RubyGems Trusted Publishing guidance and the `configure-rubygems-credentials` action interface (it has an explicit `trusted-publisher` input).

### RubyGems.org idempotency via API `sha` is well-founded

- The RubyGems.org API endpoint `GET /api/v1/versions/<gem>.json` returns a `sha` field.
- The plan’s approach (compare remote `sha` to local SHA-256 of `out/<project>-<version>.gem`) is appropriate.

### GitHub Packages RubyGems idempotency approach is reasonable

- Preflight with `gem fetch` and SHA-256 comparison of the downloaded `.gem` vs the local artifact is a sound pattern.
- The propagation-delay mitigation (push fails “already exists”, then bounded retry fetch + digest verify) is pragmatic.

### Artifact-first is consistently respected

- Ruby build produces exactly one file: `out/<project>-<version>.gem`.
- Publish jobs are specified to publish the downloaded artifact and must not rebuild.

## 🚫 Blocking issues (must fix in PLAN_5)

### 1) GitHub Packages idempotency fetch must isolate sources (`--clear-sources`) and ideally avoid user config (`--norc`)

Current PLAN_5 specifies:

- `gem fetch <project> -v <version> --source https://<user>:<token>@rubygems.pkg.github.com/<owner>/`

Problem:

- RubyGems CLI treats `--source` as “append URL to list of remote gem sources”. Without clearing default sources, `gem fetch` may still consult `https://rubygems.org` (and any other sources), which can produce:
    - false “already exists” results from the wrong registry,
    - incorrect digest comparisons,
    - or even a silent success that validates the wrong artifact.

RubyGems’ command reference explicitly supports `--clear-sources` for `gem fetch`.

Required correction:

- Update the plan to fetch using (conceptually):
    - `gem fetch ... --clear-sources --source <GPR_AUTH_SOURCE> ...`
- Strongly recommended: add `--norc` to avoid any `.gemrc` interference.

This is necessary to make the idempotency check _registry-correct_.

Update (after PLAN_5 edits on 2026-01-06):

- PLAN_5 now specifies `gem fetch ... --norc --clear-sources --source <GPR_AUTH_SOURCE>` for the GitHub Packages preflight and for the retry fetch after a potential propagation-delay “already exists” push failure.
- Status: RESOLVED in PLAN_5.

### 2) Ruby version policy + `is_prerelease` derivation is underspecified and risks misclassification

PLAN_5 defines Ruby versions as:

- allowed: `MAJOR.MINOR.PATCH` and optional prerelease dot segments (examples: `1.2.3.beta.1`, `1.2.3.rc.0`)
- rejected: SemVer hyphen prerelease (`1.2.3-beta.1`), build metadata (`+...`), etc.

But it also states:

- `is_prerelease=true` iff version has any segment beyond `MAJOR.MINOR.PATCH`.

Problem:

- In RubyGems, versions like `1.2.3.4` are not “prerelease” in the same sense as `1.2.3.rc.0` (prerelease is typically signaled by alphanumeric segments).
- With the current rule, any extra dot segment becomes “prerelease”, which can unintentionally allow “stable-ish” versions through buddy flows and/or mislabel official releases.

Required correction:

- Tighten `validate_rubygems_version.py` grammar to match the intended policy.
    - At minimum: forbid purely-numeric extra segments (e.g. reject `1.2.3.4`).
- Derive `is_prerelease` from the _presence of pre-release markers_ (e.g. any letter-containing segment) rather than “has extra segments”.

If you want to keep PLAN_5’s “dot segments” model, it still needs an explicit grammar (what tokens are allowed in prerelease segments).

Update (after PLAN_5 edits on 2026-01-06):

- Maintainers resolved the ambiguity in `.AGENTS/CLARIFY_PLAN_5_2.md`:
    - Reject numeric-only extra dot segments (e.g. `1.2.3.1` is invalid).
    - Keep `is_prerelease=true` iff there are extra dot segments.
- PLAN_5 now includes an explicit grammar that enforces “at least one letter in the suffix” and rejects numeric-only suffixes.
- With that validator restriction, the “extra dot segments => prerelease” rule is acceptable in practice for valid inputs (it aligns with RubyGems-style prerelease markers because the suffix must contain letters).
- Status: RESOLVED in PLAN_5 (with maintainer-confirmed policy).

### 3) Credentials file path for `gem push --key github` should not be hard-coded to `~/.gem/credentials`

PLAN_5 says:

- write `~/.gem/credentials` containing `:github: Bearer ${{ github.token }}`.

Risk:

- RubyGems command reference indicates the key file can be under `~/.local/share/gem/credentials` (XDG-style), and the exact location can vary by RubyGems version/platform.
- Hard-coding a single path can lead to “credentials not found” failures.

Required correction:

- Use the RubyGems-reported credentials path (e.g., via `gem env credentials`) and write the file there, chmod `0600`.

This is a reliability fix and does not weaken the “no fallback secret” requirement.

Update (after PLAN_5 edits on 2026-01-06):

- PLAN_5 now specifies writing the credentials file to `$(gem env credentials)` rather than hard-coding `~/.gem/credentials`.
- Status: RESOLVED in PLAN_5.

### 4) Publish jobs need explicit Ruby toolchain setup even when avoiding repository checkout

PLAN_5 recommends avoiding checkout in publish jobs (good), but the Ruby publish jobs still need:

- a Ruby runtime (so `gem`, `ruby`, and plugins are available)
- potentially `rubygems-await` installation (when used)

Required correction:

- Ensure the plan explicitly calls out `ruby/setup-ruby@v1` (pinned) in Ruby publish jobs (both GitHub Packages and RubyGems.org publishing).

Update (after PLAN_5 edits on 2026-01-06):

- PLAN_5 now explicitly calls out `ruby/setup-ruby@v1` (pinned) in both Ruby publish sections (GitHub Packages + RubyGems.org), with the rationale that publish jobs may skip checkout but still require a Ruby toolchain.
- Status: RESOLVED in PLAN_5.

## ⚠️ High-risk assumptions / areas to validate early

### GitHub Packages RubyGems auth with `GITHUB_TOKEN`

- GitHub’s docs contain some tension: they emphasize PATs for RubyGems registry auth, but also state `GITHUB_TOKEN` can publish packages associated with the workflow repository.
- Your maintainer policy explicitly requires **no PAT fallback** and relies on correct package linkage / Actions access.

Recommendation:

- Keep the maintainer checklist (linking + Manage Actions access) as “required”, and validate end-to-end publishing in CI as early as possible.

## 🧩 Minor improvements (non-blocking but worth doing)

- Consider explicitly pinning third-party actions by commit SHA (supply-chain hardening), if that is the repo norm.
- RubyGems.org idempotency: when comparing against `sha`, also log the matched `platform` and ensure `platform == "ruby"` (PLAN_5 already suggests this, good).
- For `gem push` and `gem fetch`, consider adding `--silent` / `--norc` to reduce noise and avoid user-level configuration surprises.

## Suggested minimal edits to PLAN_5 (summary)

1. Add `--clear-sources` (and preferably `--norc`) to GitHub Packages `gem fetch` idempotency checks.
2. Tighten Ruby version grammar and change `is_prerelease` derivation to align with RubyGems prerelease semantics.
3. Replace hard-coded `~/.gem/credentials` with `$(gem env credentials)`.
4. Explicitly include Ruby setup steps in publish jobs.

Status (after PLAN_5 edits on 2026-01-06): all four items are addressed in PLAN_5.
