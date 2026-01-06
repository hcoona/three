<!-- markdownlint-disable MD013 MD024 MD029 -->

# PLAN_REVIEW_6_3: Strict review of PLAN_6 (RubyGems support + idempotent publishes)

Date: 2026-01-06

This is an independent, strict review of `.AGENTS/PLAN_6.md`.

Scope focus:

- Adding Ruby gem support to the root release workflows.
- RubyGems.org publishing MUST be Trusted Publishing (OIDC) only (no fallback).
- Idempotent, digest-verified publishes (official + buddy) as stated.

## Executive summary

`PLAN_6` is directionally strong: it matches the repo’s “artifact-first” release architecture, explicitly forbids RubyGems API-token fallback, and adds the missing safety properties (buddy prerelease-only + idempotent reruns) in a coherent way.

However, there are several **implementation-critical precision gaps** that should be addressed before implementation to avoid flakey or failing releases:

1. **GitHub Packages RubyGems digest preflight**: the plan relies on `gem fetch` with an authenticated source URL (as confirmed in `CLARIFY_PLAN_4`), but it does not yet specify robust, testable mechanics for:
    - where the fetched `.gem` is written,
    - how “not found” vs “auth/permission” vs “transient” errors are distinguished,
2. **RubyGems.org digest check should prefer the v2 “specific version” endpoint**: `PLAN_6` uses the v1 list endpoint and then filters. RubyGems API v2 provides a deterministic endpoint for a specific version (and platform) including `sha`, reducing edge cases and network volume.

These are fixable in-plan, but they need to be made explicit.

Maintainer decisions captured (2026-01-06):

- Token leakage risk is explicitly out-of-scope for this plan review.
- Ruby build must fail if there is no Bundler context (e.g., missing `Gemfile`).

## Requirements compliance check (strict)

### RubyGems.org: Trusted Publishing only; no fallback

`PLAN_6` explicitly requires:

- no long-lived RubyGems API key secrets
- no `api-token` input
- no `role-to-assume` input

This is consistent with RubyGems Trusted Publishing docs and with `rubygems/configure-rubygems-credentials` inputs (it supports `trusted-publisher`, and `api-token` / `role-to-assume` are optional inputs).

Review note:

- `rubygems/configure-rubygems-credentials` _does_ expose `api-token` and `role-to-assume` as inputs. The “no fallback” requirement is met by **not wiring them** (as the plan states). Being explicit in workflow YAML is good (set `trusted-publisher: true` and do not reference any secrets).

### GitHub Packages RubyGems registry: `github.token` only

The plan’s approach (write `~/.gem/credentials` with `:github: Bearer ${{ github.token }}` and `gem push --key github --host https://rubygems.pkg.github.com/<owner>`) matches GitHub’s documentation.

Caveat (not a policy issue, but an operational one): GitHub’s RubyGems registry docs are mixed about PAT vs `GITHUB_TOKEN`. The plan already mitigates this by requiring package linkage and proper “Manage Actions access”. Good.

### Buddy safety (all kinds)

- The plan correctly adds a buddy prerelease-only guard early.
- The plan maintains the existing “buddy must not clobber official release” guard.

This aligns with `CLARIFY_PLAN_1`.

### Idempotent publishes everywhere

The plan uniformly applies “exists ⇒ compare digest ⇒ skip or fail” to:

- RubyGems.org
- GitHub Packages RubyGems
- npmjs + GitHub Packages npm
- PyPI

This aligns with `CLARIFY_PLAN_4`.

### Action pinning policy

The plan correctly states that newly introduced third-party actions must be pinned to a full commit SHA.

Implementation note: `ruby/setup-ruby` and `rubygems/configure-rubygems-credentials` must be SHA-pinned in the release workflows.

## Design-level review

### 1) Resolver hardening + unified discovery

Strengths:

- Correctly identifies the current “Python else Node” detection as unsafe once Ruby is added.
- Correctly forbids “pick shortest path” ambiguity hiding.
- Explicit exit-code contract (0/2/3/1) is a major improvement over the current scripts.

Gaps / required clarifications in the plan text:

- The stdout contract is underspecified (e.g., a `kind:<kind>` line vs structured output). This will become brittle in bash parsing.

Recommendation:

- Make the discovery script print **one JSON object on stdout** (single line), e.g. `{ "package_dir": "...", "project_kind": "ruby" }`, and parse via `jq -r` (jq is already installed in `release-resolve.yml`).
- Require that diagnostics (including multiple matches) go to **stderr**.

### 2) Ruby version validation rules

The Ruby version grammar in `PLAN_6` matches the maintainer-confirmed constraints in `CLARIFY_PLAN_1` (Ruby-style SemVer2-core + dot prerelease segments; no `-` or `+`; must include at least one letter in suffix).

One improvement:

- Define the exact regex in the plan (or in the validator docstring) to avoid drift between plan and implementation.

### 3) Buddy prerelease-only enforcement

Good:

- The plan enforces prerelease-only using the resolver’s computed `is_prerelease`, avoiding re-parsing in `buddy.yml`.

Make sure the plan also states:

- For **manual** buddy runs, the prerelease-only guard happens before any build/publish jobs are allowed to run.

### 4) Reproducibility baseline

Good:

- Computing `SOURCE_DATE_EPOCH` from the target commit timestamp is consistent with the repo’s existing Ruby Rake task (`release:verify`).

Risk:

- For Node, only setting `SOURCE_DATE_EPOCH` on `npm pack` steps might still leave nondeterminism if `prepack` (or build steps whose outputs land in the tarball) embed timestamps.

Recommendation:

- Either set `SOURCE_DATE_EPOCH`, `TZ`, and `LC_ALL` for the **entire pack job**, or at minimum for both `prepack` and `npm pack` steps.

### 5) Ruby build workflow (`release-build-ruby-gem.yml`)

Strengths:

- Builds a single `.gem` artifact in `out/` and verifies `name` and `version` from the `.gem`.
- Installs the known system dependencies required by the only current Ruby project.
- Scopes the build to `working-directory: package_dir`.

Compatibility note:

- The current Ruby gem project uses `ruby "~> 3.2"` in its `Gemfile`. Under RubyGems/Bundler semantics, this allows Ruby 3.3 (it means $>= 3.2$ and $< 4.0$), so `RUBY_VERSION=3.3` is acceptable.

Additionally:

- The plan currently states “do not pre-check for Gemfile; if no Bundler context, build is expected to fail.” This is now maintainer-confirmed as the intended behavior.

### 6) Ruby publish to GitHub Packages (GPR RubyGems)

Correctness:

- Using `:github: Bearer ${{ github.token }}` and `gem push --key github --host ...` matches GitHub docs.

Idempotency preflight:

- The plan relies on `gem fetch` with an authenticated source URL, consistent with `CLARIFY_PLAN_4`.

Missing precision:

- Exact `gem fetch` flags and output directory handling should be specified. In particular:
    - ensure the fetch writes to a deterministic temp directory
    - ensure the local filename used for hashing is deterministic

Recommendation:

- Add an explicit algorithm section for GPR Ruby preflight:
    1. Create temp dir.
    2. Run `gem fetch ... --source "https://USER:TOKEN@rubygems.pkg.github.com/OWNER/"` with `--silent` and `--norc`.
    3. Detect “not found” by checking for the expected `.gem` file existence, not just by parsing stderr.
    4. Hash the downloaded file and compare.

### 7) Ruby publish to RubyGems.org (Trusted Publishing)

Authentication:

- Using `rubygems/configure-rubygems-credentials` with `trusted-publisher: true` and job-level `id-token: write` matches the Trusted Publishing model.

Idempotency:

- The plan currently uses `GET https://rubygems.org/api/v1/versions/<project>.json` and filters by version/platform.

Recommendation (strong):

- Prefer RubyGems API v2 specific-version endpoint:

    `GET https://rubygems.org/api/v2/rubygems/<project>/versions/<version>.json?platform=ruby`

This returns a single object with `sha`, reduces scanning, and avoids potential “list truncation” concerns.

Eventual consistency:

- The plan correctly calls out `rubygems-await` and uses the `gem await` command (as documented by the project).

### 8) Node idempotent publish semantics

The plan’s digest model (compare `dist.integrity` vs SRI computed from the tarball bytes) is correct.

Two items to ensure are captured in the implementation:

- For GPR, query the correct scoped package name (`@<owner>/<project>`) and registry.
- Ensure the job sets up auth for `npm view` against GPR (not just for `npm publish`).

### 9) Python idempotent publish semantics

The plan’s file-level idempotency check (remote filename→sha256 mapping) is correct and is the right way to handle partial publishes.

One refinement:

- In the “already published” case, the plan should state whether it skips the publish action entirely, or runs it with `skip-existing: true` after verification. Both are okay; skipping is faster and less error-prone.

## Specific inconsistencies / edits recommended to PLAN_6 text

1. Add a “Data contract” subsection for `find_project_path.py` output (recommended: JSON line) and parsing details.
2. In RubyGems.org idempotency section, switch from API v1 versions list to API v2 specific-version endpoint.
3. Make `gem fetch` mechanics explicit (download directory, expected filename, and failure classification).

## Conclusion

`PLAN_6` is implementable and largely well-aligned with the clarified policies, including the non-negotiable “Trusted Publishing only” constraint for RubyGems.org.

Before implementation, the plan should be tightened in the few places above to prevent release-blocking failures and reduce ambiguity in scripting/parsing.
