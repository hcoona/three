# PLAN_REVIEW_1: Strict review of PLAN_1 (RubyGems release support)

This review evaluates `.AGENTS/PLAN_1.md` against the repository’s existing release architecture (resolve → build artifact → publish → attest → GitHub Release) and the maintainer decisions recorded in `.AGENTS/CLARIFY_PLAN_0.md`.

## Executive summary

`PLAN_1` is a major improvement over `PLAN_0` and is broadly consistent with this repo’s release model:

- artifact-first (build once, publish downloaded `out/*`),
- Trusted Publishing (OIDC) for RubyGems.org,
- explicit gating (official release must publish to both RubyGems.org and GitHub Packages),
- hard errors on ambiguity.

However, there are still several **correctness and operability gaps** that should be addressed _in the plan_ before implementation, primarily around:

1. **Ambiguity handling semantics** (within-kind ambiguity vs cross-kind ambiguity) and how the resolver surfaces them.
2. **Ruby version policy** (accepted input format and how it maps to gem metadata / release UI).
3. **GitHub Packages RubyGems publishing details** (required `--key github`, credentials file permissions, and the “package must be linked / Actions access” gotcha).
4. **Trusted Publishing mechanics** for `rubygems/configure-rubygems-credentials` (make the intended mode explicit and stable).

None of these are conceptually large, but if left implicit they can produce confusing failures in release runs.

## What PLAN_1 gets right (alignment with repo patterns)

- **Architecture match:** It preserves the existing pattern used by Python and Node (resolve → build → publish → attest → GitHub Release).
- **Artifact-first publishing:** Explicitly avoids `rubygems/release-gem@v1` because it commonly performs a Bundler release flow that can rebuild.
- **Strong identity checks:** Verifying gem name + version from the built `.gem` via `gem specification` is the right analog to `verify_python_artifact_version.py`.
- **Explicit environment for Trusted Publishing:** `environment: rubygems` matches the maintainer decision and RubyGems guidance that environment constraints can be part of the trust configuration.
- **No fallback secrets for GitHub Packages:** Matches the maintainer decision.

## High-risk gaps / must clarify in the plan

### 1) Resolver ambiguity: distinguish “not found” vs “ambiguous”

`PLAN_1` requires three detectors (Python/Node/Ruby) and cross-kind ambiguity detection. That’s correct, but the plan currently specifies only:

- “0 matches → fail”
- “>1 matches → fail”

without specifying how the resolver will detect these cases _reliably_.

Why this matters in this repo:

- The current resolver calls detectors sequentially and treats “non-zero” as “no match” (it just proceeds to the next detector).
- If a detector exits non-zero due to **ambiguity**, and the resolver treats it as “no match”, you can end up with:
    - misleading “Unknown project” errors, or
    - accidentally selecting a different kind if another detector happens to succeed.

**Recommendation to add to PLAN_1:**

- Define a clear detector contract that the resolver can interpret:
    - exit code `0`: unique match, print `package_dir`
    - exit code `2`: ambiguous matches, print a machine-readable list (or a clearly prefixed list) to stderr
    - exit code `3`: not found
    - exit code `1`: parse errors / unexpected failures

Then make `release-resolve.yml` treat exit code `2` as an immediate hard error (even if other kinds have 0 matches).

If you don’t want special exit codes, then the plan should require that `release-resolve.yml` captures and surfaces detector stderr from _all_ detectors and fails if any detector reports ambiguity. (Exit codes are cleaner.)

### 2) Ruby version policy: accepted format and implications

Update (2026-01-05): maintainer clarified that when building and publishing Ruby gems, the pipeline accepts **only** the Ruby-style SemVer2-variant version format, and rejects:

- strict SemVer 2.0.0 strings (including `-` prerelease separator and `+build` metadata), and
- PEP 440.

Implications:

- No SemVer-to-Ruby normalization is required at publish time.
- `version` and `rubygems_version` can be identical for Ruby releases, which eliminates the earlier “user-facing version mismatch” concern.

### 3) Ruby version input scope: is it SemVer-only or RubyGems-native?

`.AGENTS/CLARIFY_PLAN_0.md` explicitly calls out “version adaptation rather than requiring strict SemVer 2.0.0”. `PLAN_1` proposes a SemVer-to-RubyGems normalization algorithm.

Update (2026-01-05): maintainer confirmed the intended policy is:

- Accept Ruby-style versions, but only those that conform to the SemVer 2.0 _core_ shape (MAJOR.MINOR.PATCH) and the Ruby-style prerelease dot form (e.g. `1.2.3.beta.1`).
- Do **not** accept broader RubyGems-native forms like `1.0`, `2026.01.05`, `1.0.pre`.
- Reject strict SemVer2.0 inputs for Ruby (no `-` prerelease separator; no `+build`).

**Recommendation (still required for PLAN_1):** write this explicitly and ensure `release-resolve.yml` does not apply `validate_semver2_version.py` to Ruby directly; instead it should validate/normalize Ruby versions according to the above policy and output both:

- `version` (release display string), and
- `rubygems_version` (canonical RubyGems version used for `gem specification` verification and `gem push`).

### 4) GitHub Packages RubyGems publishing: command + credentials details

GitHub’s RubyGems registry documentation shows publishing as:

- credentials in `~/.gem/credentials`:

    ```text
    ---
    :github: Bearer TOKEN
    ```

- publish command:

    ```bash
    gem push --key github --host https://rubygems.pkg.github.com/NAMESPACE GEM_FILE.gem
    ```

`PLAN_1` mentions the credentials key `:github`, but the publish command section omits `--key github`.

**Recommendation to add to PLAN_1:**

- Include `--key github` in the publish command to ensure RubyGems uses the intended credentials.
- Ensure the workflow sets correct file permissions (`chmod 0600 ~/.gem/credentials`). RubyGems commonly refuses to use credentials files that are too permissive.

### 5) GitHub Packages: ensure Actions access / repository linkage is handled

GitHub’s docs for packages + Actions indicate that `GITHUB_TOKEN` works best when:

- the package is linked to the repository, and/or
- “Manage Actions access” is configured for user/org-scoped packages.

Because `PLAN_1` has a strict policy of “no fallback secret”, a missing linkage/access configuration becomes a release blocker.

Update (2026-01-05): maintainer confirmed we will use both protections:

- Require gemspec metadata `github_repo` to be set to `https://github.com/hcoona/three.git`.
- Require maintainers to ensure the GitHub Packages gem package is either linked to `hcoona/three` (with inheritance enabled) or explicitly configured under “Manage Actions access” to allow workflows from `hcoona/three`.

Without this, publishing may fail in some org configurations even when `packages: write` is present.

### 6) Trusted Publishing mechanics: make the intended action inputs explicit

`PLAN_1` proposes using `rubygems/configure-rubygems-credentials@v1` without `api-token` and without `role-to-assume`.

From the action’s `action.yml`, there is a `trusted-publisher` input and it states it defaults to true when no other configuration is provided.

**Recommendation:**

- Set `trusted-publisher: true` explicitly to future-proof the workflow against default behavior changes.
- Pin the action to a commit SHA (consistent with this repo’s pattern for third-party actions).

## Medium issues / should tighten

### A) Ruby build workflow: avoid accidentally packaging stale `.gem` files

`gem build` writes a `.gem` into the current directory. If the repository ever contains committed `.gem` files or leftovers from previous steps, a glob like `*.gem` may over-collect.

**Recommendation:**

- Ensure the build job operates in a clean checkout (it does), and
- Prefer `gem build ... --output <path>` (supported by modern RubyGems) so the workflow controls exactly what is produced.

### B) Ruby tests/lint policy is strict (and that’s okay, but document it)

The policy “if `Gemfile` exists, then `bundle exec standardrb` and `bundle exec rspec` are mandatory” is consistent with `.AGENTS/CLARIFY_PLAN_0.md`.

**Recommendation:** add a short note that Ruby projects intending to be released must include these tools in their Bundler dependencies if they include a `Gemfile`.

### C) Version displayed in GitHub Release title

If the tag version differs from the gem’s `rubygems_version` after normalization (e.g. `1.2.3-beta.1` vs `1.2.3.beta.1`), the GitHub Release title and the gem metadata will not match exactly.

This is not necessarily wrong, but it can confuse consumers.

**Recommendation:** decide and document which version string appears in:

- `release_title`
- release notes
- GitHub Release tag

(See `CLARIFY_PLAN_1.md`.)

## Suggested concrete edits to PLAN_1

1. Add a detector exit-code contract (or equivalent) so the resolver can reliably differentiate “not found” vs “ambiguous”.
2. In the resolver section, explicitly state the Ruby version policy:

- accept Ruby-style prerelease dot form (e.g. `1.2.3.beta.1`),
- reject strict SemVer2.0 inputs (`-` and `+...`),
- reject non-SemVer2-core RubyGems versions,
- ensure `version == rubygems_version` for Ruby releases.

1. Remove or rewrite any “drop +build” logic in the Ruby path, since `+...` is not accepted for Ruby releases.
2. Fix GitHub Packages publish details: include `--key github`, and enforce `~/.gem/credentials` permissions.
3. Extend maintainer checklist to include GitHub Packages “Actions access / linkage” requirements.
4. Make `rubygems/configure-rubygems-credentials` usage explicit: set `trusted-publisher: true` and pin to SHA.

Update (2026-01-05): maintainer confirmed buddy runs are prerelease-only and must never clobber official releases. Ensure PLAN_1 explicitly aligns this invariant across Ruby, Node (npm), and Python:

- Buddy must fail on stable/final versions.
- Buddy must not create/modify a GitHub Release where `prerelease` is `false`.

## Acceptance criteria traceability

- **Resolver uniquely resolves Ruby project + fails on ambiguity:** achievable, but only if the resolver treats ambiguity as a hard error (see gap #1).
- **Build workflow produces `out/*.gem` and verifies name/version:** good; consider using `--output` to avoid stale file risks.
- **Official publishes to RubyGems.org (Trusted Publishing only) without rebuild:** consistent with artifact-first; action configuration needs to be explicit.
- **Official publishes to GitHub Packages with `github.token` only:** matches policy; must document/manage package access requirements.
- **Buddy publishes to GitHub Packages only and creates prerelease GitHub Release:** consistent with existing buddy patterns; consider adding a guard if buddy stable versions are not intended.
- **Buddy publishes to GitHub Packages only and creates prerelease GitHub Release:** consistent with existing buddy patterns; stable/final versions are now confirmed as disallowed for buddy.
- **GitHub Release contains same `out/*` assets:** consistent with `release-create-github-release.yml`’s flat-layout requirement.
