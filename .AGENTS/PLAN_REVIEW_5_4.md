<!-- markdownlint-disable MD013 MD024 MD029 MD044 -->

# PLAN_REVIEW_5_4: Strict review of PLAN_5 (RubyGems support; RubyGems.org Trusted Publishing only; no fallback)

This is an **independent** review of `.AGENTS/PLAN_5.md`.

Constraints followed:

- I did **not** use any prior `.AGENTS/PLAN_REVIEW_*.md` files as input.
- I **did** use maintainer-confirmed policies from `.AGENTS/CLARIFY_PLAN_*.md` as the source of truth.

## Executive verdict

`PLAN_5` is **directionally correct** and it encodes the most important security requirement clearly:

- **RubyGems.org publishing must use Trusted Publishing (OIDC) only**.
- **No RubyGems API token secrets**.
- **No fallback** to any long-lived credential.

The plan is also strong on artifact-first publishing, least privilege, and rerun idempotency.

However, for a “strict” readiness bar, I see **two blocking gaps** and several non-blocking improvements.

### Blocking gaps

1. **The “no fallback” requirement is not yet mechanically enforceable as-written.**

    PLAN_5 requires a fail-fast guard that makes later introduction of `api-token` / secret fallback fail the workflow.
    - The plan states the requirement and the minimum guardrails, but it does **not** specify a concrete, robust mechanism.
    - Without a concrete enforcement mechanism, this requirement can regress silently in the future.

2. **The resolver output contract changes are correctly flagged as breaking, but the plan does not list the full update surface.**

    PLAN_5 will change:
    - project kind detection (now: Python→Node; future: Python/Node/Ruby with ambiguity detection)
    - output schema (`project_kind` adds `ruby`; new `is_prerelease`)
    - version validation rules (Ruby no longer uses SemVer2)

    The plan warns about breaking change, but a strict plan should enumerate all known callsites and affected `if:` expressions that must be updated in the same PR (including any nested workflows under `src/**/.github/workflows/*` if they reference the reusable resolver).

## What is already solid (✅)

### 1) Security posture is aligned with maintainer policy

- RubyGems.org Trusted Publishing (OIDC) only is explicitly stated as non-negotiable.
- “No fallback secret” is consistent with:
    - `.AGENTS/CLARIFY_PLAN_0.md` (no RubyGems API key; no fallback)
    - `.AGENTS/CLARIFY_PLAN_3.md` (if OIDC path fails, workflow must fail)

### 2) `rubygems/configure-rubygems-credentials` inputs match the intended posture

From the upstream action’s `action.yml`:

- Inputs include: `api-token`, `role-to-assume`, and `trusted-publisher`.
- `trusted-publisher` defaults to “true if no other configuration is given”.

So PLAN_5’s approach (“set `trusted-publisher: true` explicitly; set neither `api-token` nor `role-to-assume`”) is consistent with the action interface.

### 3) RubyGems.org API `sha` field exists for idempotency

RubyGems.org API docs for `GET /api/v1/versions/<gem>.json` show each version object contains a `sha` field.

PLAN_5’s strategy (compare remote `sha` vs local SHA-256) is therefore feasible.

### 4) GitHub Packages RubyGems publish flow is correct at the CLI level

GitHub’s docs support:

- publishing via `gem push --key github --host https://rubygems.pkg.github.com/<NAMESPACE> <file>.gem`
- using `~/.gem/credentials` with `:github: Bearer TOKEN`

PLAN_5 correctly distinguishes publish auth (credentials file) from fetch/install auth (authenticated source URL).

### 5) Artifact-first model is consistent with the repo’s existing release architecture

Current root workflows already:

- build artifacts in a reusable build workflow
- download `out/*` in publish jobs
- publish from artifacts

Adding Ruby builds/publishes in the same shape is the least risky approach.

## Strict issues / risks (⚠️) and recommended fixes

### A) Make “no fallback” mechanically enforceable (BLOCKING)

PLAN_5 requires this, but does not define how to implement it.

A strict plan should specify **exactly one** enforcement mechanism and make it part of the acceptance criteria.

Recommended mechanism (runtime guard, no checkout required):

- Before invoking `rubygems/configure-rubygems-credentials`, fetch the _workflow file content_ from the repository via GitHub API (using `${{ github.token }}` with `contents: read`) and fail if any of the following are present:
    - `secrets.RUBYGEMS` (or broader: `secrets.*RUBYGEMS.*`)
    - `api-token:` under the `configure-rubygems-credentials` step
    - `role-to-assume:` under the `configure-rubygems-credentials` step

This keeps the publish job “checkout-free” while still enforcing the policy.

Alternative mechanism (merge-gate, not runtime):

- Add a repo-level validation (HK gate / CI lint) that rejects workflow changes that reference RubyGems API tokens.

This is useful, but it does **not** satisfy PLAN_5’s stated requirement (“workflow must fail if fallback is wired in later”), so it should only be additional.

### B) Clarify “OIDC role” vs “trusted publisher” terminology

The upstream action supports both:

- OIDC API token roles (`role-to-assume`)
- Trusted Publisher exchange (`trusted-publisher`)

PLAN_5 wants Trusted Publishing only and forbids `role-to-assume`.

Recommendation:

- Add a short note that **Trusted Publisher mode must be used** and why `role-to-assume` is disallowed in this repo (policy + reduced configuration surface).

### C) Fix minor spec duplication

In “Hard requirements / RubyGems.org publishing must be Trusted Publishing only (no fallback)”, the sentence:

- “If OIDC trusted publishing cannot be established at runtime, the workflow must fail.”

appears twice.

Not functionally harmful, but it is noise in a “strict” plan.

### D) Update workflow_dispatch input text for Ruby

Today, `official.yml` and `buddy.yml` say:

- “Others: SemVer 2.0.0 (no leading v).”

After PLAN_5, Ruby versions are **not** SemVer2. That description must be updated, otherwise maintainers will input `1.2.3-beta.1` and hit validator failures.

### E) Detector exit-code contract needs an explicit mapping strategy

Current detectors:

- exit with code `1` on not-found or parse errors, via `sys.exit("...")`.

PLAN_5 wants:

- 0 / 2 / 3 / 1

Recommendation:

- Explicitly document how each detector distinguishes:
    - “not found” (exit 3)
    - “ambiguous matches” (exit 2)
    - “unexpected failure” (exit 1)

And add a minimal test strategy (even just a tiny “self-test” mode or unit tests) because these scripts are now policy-critical.

### F) RubyGems tool behavior / version assumptions

PLAN_5 relies on:

- `gem build --output ...`
- `gem fetch --clear-sources --source ... --norc`
- `gem env credentials`

These are reasonable, but they depend on the Ruby/RubyGems versions installed by `ruby/setup-ruby`.

Recommendation:

- Add a compatibility note: “We require RubyGems >= X (runner default should satisfy; verify in CI).”
- In the publish jobs, log `ruby -v` and `gem -v` (non-sensitive) for supportability.

### G) `rubygems-await` invocation should be made concrete

PLAN_5 says “Use `rubygems-await` … run `rubygems-await`”. In practice, the common interface is a RubyGems plugin command (`gem await ...`).

Recommendation:

- Specify the exact command line(s) that will be used, and what endpoint readiness it is waiting for (e.g., “wait until `GET /api/v1/versions/<gem>.json` includes the version”).

### H) GitHub Packages RubyGems auth docs are internally tense

GitHub’s docs simultaneously:

- emphasize PAT usage for RubyGems registry, and
- state that `GITHUB_TOKEN` can publish packages associated with the workflow repository.

The repo’s policy is “no fallback secret; fail if it doesn’t work”, which is fine.

Recommendation:

- Ensure the plan explicitly calls out that publishing can fail if the package is not linked / Actions access is not inherited, and that this is expected (maintainer checklist already includes this).

## Coverage check against maintainer-confirmed policies

- Buddy:
    - Ruby publishes **GitHub Packages only** ✅
    - prerelease-only enforcement ✅ (plan adds guard)
    - non-clobber official release ✅ (existing guard pattern already exists; Ruby should use the same)

- Official:
    - Ruby publishes to **RubyGems.org + GitHub Packages** ✅
    - RubyGems.org uses Trusted Publishing only ✅

- Ruby identity matching (tag ↔ gemspec ↔ gem name) ✅

- Ruby version policy (reject numeric-only extra segments; prerelease derived from suffix segments) ✅ (matches CLARIFY_PLAN_5_2)

## Suggested acceptance criteria addenda (tighten “strictness”)

To close the blocking gaps and prevent regressions, add these acceptance criteria:

1. **No-fallback guard** is implemented and tested:
    - A deliberate workflow edit adding `api-token:` must cause the job to fail early with a clear message.

2. Resolver outputs are updated everywhere:
    - root `official.yml`, `buddy.yml`, and any other callers must be updated in the same PR.

3. Ruby publish jobs log non-sensitive version info:
    - `ruby -v`, `gem -v`.

4. `rubygems-await` usage is pinned and invoked explicitly.

## Final recommendation

Proceed with PLAN_5 **after**:

- making the “no fallback” requirement mechanically enforceable (blocking)
- tightening the resolver change surface list (blocking)

Everything else is either already aligned with maintainer policy or is a non-blocking improvement that can be handled during implementation.
