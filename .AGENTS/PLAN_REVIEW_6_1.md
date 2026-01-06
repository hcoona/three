<!-- markdownlint-disable MD013 MD024 MD029 -->

# PLAN_REVIEW_6_1: Strict review of PLAN_6 (RubyGems Trusted Publishing only)

Date: 2026-01-06

## Scope and inputs

This review is based on:

- `.AGENTS/PLAN_6.md` (the plan under review)
- Maintainer-confirmed clarifications:
    - `.AGENTS/CLARIFY_PLAN_0.md`
    - `.AGENTS/CLARIFY_PLAN_1.md`
    - `.AGENTS/CLARIFY_PLAN_2.md`
    - `.AGENTS/CLARIFY_PLAN_3.md`
    - `.AGENTS/CLARIFY_PLAN_4.md`
    - `.AGENTS/CLARIFY_PLAN_5_3.md`
    - `.AGENTS/CLARIFY_PLAN_5_4.md`
- Authoritative external references (spot-checked):
    - GitHub Packages RubyGems registry documentation
    - RubyGems Trusted Publishing guides
    - RubyGems.org API documentation
    - `rubygems/configure-rubygems-credentials` action documentation (`action.yml`)

Per instructions, this review does **not** rely on any `.AGENTS/PLAN_REVIEW_*.md` documents.

## Executive summary

`PLAN_6` is directionally correct and aligns well with the repo’s existing “artifact-first” release architecture.

Most importantly, it **does** satisfy the non-negotiable constraint that RubyGems.org publishing must use **Trusted Publishing (OIDC) only** and must not introduce any API-token or PAT fallback.

However, there are a few implementation-critical precision gaps where the plan should be tightened to avoid surprises, especially around:

- Node idempotency checks (registry-specific package identity and authentication)
- RubyGems GitHub Packages idempotency (token-safe fetch + deterministic error classification)
- Editorial duplication that can lead to inconsistent implementation

None of the gaps below appear to require new maintainer _policy_ decisions, but several should be resolved in the plan (or recorded as explicit “implementation notes”) before coding.

## Maintainer feedback (incorporated)

After this review, maintainers confirmed the following implementation preferences:

- Node package identity mapping is considered already well-defined:
    - npmjs.org uses the unscoped package name (`<project>`)
    - GitHub Packages (GPR) uses the scoped name (`@<owner>/<project>`)
      Therefore, deriving the name from the tarball is not required as long as the plan makes the mapping explicit.
- For GitHub Packages RubyGems idempotency, conservative error classification is desired, but no additional stdout/stderr redaction machinery is required beyond avoiding shell tracing/echoing sensitive inputs.
- For Ruby build, the RubyGems version in use is confirmed to support `gem build --output`, so no fallback is required.

The plan (`PLAN_6`) should be updated accordingly (explicit mapping, remove duplicate lines, tighten error classification, and add the resolver-caller alignment note).

## Compliance with hard requirements

### RubyGems.org: Trusted Publishing (OIDC) only (no fallback)

✅ The plan explicitly requires:

- `environment: rubygems`
- `permissions: id-token: write`
- Using `rubygems/configure-rubygems-credentials` in trusted publisher mode
- **Not** passing `api-token` and **not** passing `role-to-assume`

✅ This matches the RubyGems Trusted Publishing model and the action’s supported inputs (`trusted-publisher` exists and defaults to true when no other auth mode is configured).

✅ The plan also correctly treats “no fallback” as an implementation wiring constraint, not a runtime self-check requirement.

### GitHub Packages RubyGems registry: `github.token` only

✅ The plan states `${{ github.token }}` only and no PAT fallback.

✅ This is consistent with GitHub Packages guidance that `GITHUB_TOKEN` can publish packages associated with the workflow repository, with the important prerequisite that package access/linkage is correctly configured.

### Buddy safety (prerelease-only + non-clobber official release)

✅ The plan introduces a resolver-derived `is_prerelease` output and an early buddy guard.

✅ The plan preserves the existing “buddy must not clobber prerelease=false GitHub Release” guard.

### Idempotency applies to all publishes

✅ The plan consistently applies “exists → compare digest → skip or fail” across Ruby/Node/Python and across official/buddy.

## High-risk gaps / changes recommended before implementation

### 1) Node idempotent publishing: missing _package identity_ mapping per registry

The plan correctly identifies:

- Remote digest: `dist.integrity` from `npm view`
- Local digest: SRI computed over the tarball bytes

But it does not specify how the publish job derives the correct remote package identifier for each registry:

- npmjs.org: typically unscoped `name` (e.g. `foo`)
- GitHub Packages (npm.pkg.github.com): typically scoped `@<owner>/<name>` in this repo’s packing model

Maintainer clarification: the intended rule is already defined and should be stated explicitly in `PLAN_6`:

- npmjs.org package name is `<project>` (unscoped)
- GitHub Packages (GPR) package name is `@<owner>/<project>` with `<owner>` lowercased from `${{ github.repository_owner }}`

With this explicit mapping, tarball introspection is optional and not required.

### 2) GitHub Packages RubyGems idempotency: error classification + token hygiene must be fully specified

The plan’s preferred preflight is:

1. `gem fetch` from an authenticated source URL
2. If fetch succeeds → compare SHA-256
3. If fetch fails “not found” → attempt push
4. If push fails “already exists” → retry fetch with backoff

This is the right shape, but the key precision gap to resolve is:

- **Error classification**: `gem fetch` failure modes are not limited to “not found”. Network errors, auth failures, and server errors must fail fast rather than being treated as “not found”.

**Recommendation (make explicit in PLAN_6):**

- Treat only a narrow set of errors as “not found” (e.g. HTTP 404-like messages).
- Any auth-related failure must be a hard failure with actionable guidance (“package linkage / Actions access is misconfigured”).
- Avoid shell tracing (`set -x`) and avoid echoing any authenticated source URLs.

### 3) Ruby build workflow: be explicit about `gem build --output` availability / fallback

`PLAN_6` requires building a single `.gem` artifact directly at `out/<project>-<version>.gem` using `gem build ... --output ...`.

Maintainer clarification: `gem build --output` support is confirmed for the RubyGems version in use, so no fallback is required.

### 4) Resolver detector contract changes: ensure callers remain aligned

The plan correctly notes that changing resolver outputs is a breaking contract.

**Recommendation:** add one sentence to the plan:

- `release-create-github-release.yml` and `release-prepare-release-notes.yml` are unaffected, but _all_ entry workflows that call `release-resolve.yml` must be updated together (including any not listed under “current baseline”).

This is mostly bookkeeping, but it prevents partial PRs.

## Medium/low-risk issues and editorial fixes

### Duplicate lines / copy-paste artifacts

The plan contains visible duplications that can cause inconsistent implementations:

- “Deterministic preflight (preferred):” appears twice in the GitHub Packages RubyGems section.
- “Continue using `pypa/gh-action-pypi-publish` with OIDC.” appears twice.
- “Registry eventual consistency:” appears twice.

**Recommendation:** remove duplicates to prevent implementers from missing the “real” version of the text.

### Ruby version validation semantics are OK but should mention `Gem::Version#prerelease?`

The plan’s Ruby prerelease detection (`is_prerelease=true` iff suffix segments exist) matches the enforced grammar (numeric-only suffixes are rejected).

**Recommendation:** optionally mention that the intent mirrors RubyGems’ prerelease notion (letters in the version), to prevent future “allow numeric-only suffix” changes from silently breaking buddy gating semantics.

### Pinning actions to full SHAs

The plan states all new third-party actions must be pinned to commit SHAs.

**Recommendation:** ensure the plan explicitly lists all _new_ actions it introduces (at minimum: `ruby/setup-ruby`, `rubygems/configure-rubygems-credentials`) and calls out that first-party actions (e.g., `actions/*`) follow existing repo practice.

## Additional suggestions (non-blocking)

- For RubyGems.org idempotency, explicitly state that RubyGems API `sha` is the SHA-256 of the `.gem` file (as implied by the API format) and compare hex-to-hex.
- For Node SRI computation, explicitly state that the remote `dist.integrity` is typically `sha512-...` and the local computation must therefore use SHA-512.
- For publish jobs, explicitly state “checkout-free” means the job must not require repo files for correctness, not merely as an optimization.

## Verdict

**Approve with changes.**

`PLAN_6` is solid and implementable, and it correctly enforces the key policy: **RubyGems.org publishing via Trusted Publishing only with no fallback**.

Before implementation, tighten the plan in the highlighted areas (especially Node idempotency package identity mapping and GPR Ruby error classification/token hygiene) and remove editorial duplications to reduce implementation ambiguity.
