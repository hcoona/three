<!-- markdownlint-disable MD013 MD024 MD029 -->

# PLAN_REVIEW_6_0: Independent strict review of PLAN_6

Date: 2026-01-06

This is an **independent** review of `.AGENTS/PLAN_6.md`.

Constraints followed:

- I **did not read** any `.AGENTS/PLAN_REVIEW_*.md` files.
- I **did** read the maintainer-confirmed clarification notes (`.AGENTS/CLARIFY_PLAN_*.md`) as the plan explicitly treats them as “source of truth”.
- I cross-checked the plan against the repository’s current workflows and scripts.

## Executive summary

`PLAN_6` is broadly coherent, policy-aligned, and implementable. It cleanly extends the existing “artifact-first” release architecture to Ruby gems and also tightens rerun-safety semantics for npm and PyPI.

The plan’s non-negotiables are correctly captured:

- RubyGems.org publishing is **Trusted Publishing (OIDC) only** with **no fallback** secrets or inputs.
- GitHub Packages RubyGems publishing uses `${{ github.token }}` only.
- Buddy is prerelease-only and must not clobber official releases.
- All publishes (official + buddy) become idempotent via digest verification.

However, there are several **blocking-level precision gaps** where the plan should be amended before implementation to avoid flakiness, token leakage risks, and divergence from already-confirmed clarifications.

## ✅ Policy compliance check (hard requirements)

### RubyGems.org: Trusted Publishing only

The plan satisfies the requirement _as written_:

- It uses `rubygems/configure-rubygems-credentials` in “trusted-publisher” mode and explicitly states:
    - do **not** pass `api-token`
    - do **not** pass `role-to-assume`
    - no long-lived RubyGems secrets

This matches `CLARIFY_PLAN_0` + `CLARIFY_PLAN_3` + the “no mechanical enforcement required” decision (`CLARIFY_PLAN_5_4`).

### GitHub Packages RubyGems: github.token only

The plan consistently requires `${{ github.token }}` only (no PAT fallback), aligning with `CLARIFY_PLAN_0`, `CLARIFY_PLAN_1`, and `CLARIFY_PLAN_4`.

### Buddy safety

The plan explicitly adds a prerelease-only guard and preserves the existing “non-clobber official release” guard. This matches `CLARIFY_PLAN_1`.

### Third-party action pinning

The plan states that any _new third-party_ GitHub Actions introduced must be pinned to a full commit SHA, aligning with `CLARIFY_PLAN_5_3`.

## Blocking issues / required corrections

### 1) Ruby build: use deterministic `gem build --output` (plan currently suggests `gem build` + move)

`CLARIFY_PLAN_2` already identified the strongest reproducibility pattern for Ruby builds:

- set `SOURCE_DATE_EPOCH` derived from the target commit timestamp
- run `gem build <project>.gemspec --output <deterministic-path>`

`PLAN_6` currently says:

- run `gem build "${PROJECT}.gemspec"` and then move the resulting `.gem` into `out/<project>-<version>.gem`.

Why this matters:

- The “move the resulting file” step relies on implicit output naming conventions and directory state.
- If a project ever emits multiple `.gem` files (rare, but possible via custom build steps), the plan’s “exactly one gem” constraint becomes harder to enforce.

**Required adjustment:** In the Ruby build workflow, build directly to `out/<project>-<version>.gem` using `--output`, and fail if any extra `.gem` appears.

### 2) RubyGems.org eventual consistency: plan should match the confirmed decision to use `rubygems-await`

`CLARIFY_PLAN_3` states (confirmed): we will use `rubygems-await` to handle RubyGems eventual consistency.

`PLAN_6` currently frames `rubygems-await` as optional (“If post-publish verification is required…”).

**Required adjustment:** Treat `rubygems-await` usage as the default/required mechanism when the job needs to verify availability after a publish or after a “already exists” race. Otherwise, the “idempotent rerun” guarantees may be flaky.

### 3) GitHub Packages RubyGems idempotency: specify the exact username for authenticated `gem fetch`

The plan’s GPR preflight uses:

- `gem fetch <project> -v <version> ... --source https://<user>:<token>@rubygems.pkg.github.com/<owner>/`

But it does not specify which `<user>` should be used under GitHub Actions when authenticating with `${{ github.token }}`.

This is not a policy question, but it _is_ a correctness issue:

- The GitHub Packages docs for installation/auth often use `USERNAME:TOKEN@...`.
- In Actions, the safest practical choices are typically `${{ github.actor }}` or the literal `x-access-token` username pattern.

**Required adjustment:** Pick one username convention and document it in the plan (and implement it consistently). Also explicitly require `--norc` and quiet output to reduce token exposure risk.

### 4) Resolver output contract change: update callers + acceptance criteria to include `is_prerelease`

`PLAN_6` correctly notes this is a breaking change. However, the acceptance criteria and implementation sequence should explicitly include:

- updating `official.yml` / `buddy.yml` to consume the new output
- ensuring the prerelease-only guard uses _resolver output_, not re-parsed versions

This prevents divergent prerelease parsing logic across workflows.

## High-value improvements (strongly recommended)

### A) Detector performance and correctness

The new detector contract is a major safety win.

However, the current Python detector (`eng/scripts/find_python_project_path.py`) does not skip large directories, and both current Python/Node detectors pick “shortest path” which `PLAN_6` intends to remove.

Recommendations to bake into implementation details (even if not spelled out in the plan text):

- apply the same directory-skip logic to Python and Ruby detectors (`.git`, `node_modules`, `obj`, `bin`, `.venv`, `.tox`, etc.)
- do not silently prefer a “shortest” match; always treat multiple matches as ambiguity

### B) RubyGems.org idempotency selection: platform scope

The plan selects `(number == version && platform == "ruby")`, which is correct given `CLARIFY_PLAN_5` (“platform-specific gems are confirmed out of scope”). Good.

Recommendation: encode this as a clear precondition in the Ruby publish job logs (“platform gems are not supported by this workflow”). This reduces future confusion.

### C) Node idempotency: define the exact SRI computation method

The plan correctly uses `dist.integrity` as the canonical remote digest.

Recommendation: state how to compute local SRI deterministically (e.g., `sha512-<base64>` over the tarball bytes) to avoid accidental mismatches due to formatting or tooling differences.

### D) Python partial publish: include the “fail-fast on mismatch” rule as a dedicated step

The plan describes the rule, but it should explicitly require that:

- digest validation runs **before** calling `pypa/gh-action-pypi-publish`
- `skip-existing: true` is only used **after** validation proves any existing files match

This prevents accidental silent acceptance of “same filename, different content” situations.

## Risk review

### Token leakage in logs

Risk hotspots introduced by the plan:

- embedding `${{ github.token }}` into a `--source` URL for `gem fetch`

Mitigations to make explicit:

- ensure the command does not echo the URL
- use quiet flags where available
- ensure no `set -x` is used in publish jobs

### Eventual consistency / race windows

The plan already acknowledges eventual consistency and proposes bounded retries.

Given the explicit “idempotent reruns” contract, treat “push says already exists but fetch doesn’t see it yet” as a first-class case for both:

- GitHub Packages RubyGems
- RubyGems.org

(`rubygems-await` is the right hammer here.)

### Maintenance burden

The plan adds non-trivial complexity for “digest-based idempotency” across three ecosystems.

Mitigation suggestion: keep the digest verification logic in scripts (Python), not inline bash, so it can be unit-tested and reused.

## Fit with current repository state

Cross-check notes:

- Current resolver uses “Python else Node” and existing detectors return exit code `1` on “not found”. Adding Ruby without changing this would be unsafe; `PLAN_6` fixes that.
- Buddy currently has no prerelease-only guard; `PLAN_6` fixes that.
- Build workflows currently do not set `SOURCE_DATE_EPOCH`; `PLAN_6` adds a minimal baseline.

## Conclusion

With the blocking corrections above (especially Ruby build output determinism, `rubygems-await` alignment, and specifying the GPR `gem fetch` username convention), `PLAN_6` is a strong blueprint and should be safe to implement without violating the “Trusted Publishing only / no fallback” constraint.

## Follow-up: applied fixes to PLAN_6 (this review addressed)

The items below were blocking at the time this review was written. They have now been incorporated into `.AGENTS/PLAN_6.md`.

### Blocking issues

1. **Ruby build output determinism** — **Addressed**
    - `PLAN_6` section “4) Ruby build: new reusable workflow”, step 6 now requires building directly to `out/<project>-<version>.gem` via `gem build ... --output ...` and failing on extra `*.gem` outputs.

2. **RubyGems.org eventual consistency via `rubygems-await`** — **Addressed**
    - `PLAN_6` section “6) Ruby publish: RubyGems.org …”, “Eventual consistency” now treats `rubygems-await` as a required mechanism (not optional) for reliable verification/idempotency.

3. **GPR `gem fetch` authenticated username convention** — **Addressed**
    - `PLAN_6` section “5) Ruby publish: GitHub Packages RubyGems registry …”, “Deterministic preflight” now specifies:
        - `<user> = ${{ github.actor }}`
        - `<token> = ${{ github.token }}`
          and adds explicit guidance to avoid echoing the authenticated URL or enabling shell tracing.

4. **Resolver output contract change must update callers / avoid re-parsing** — **Addressed**
    - `PLAN_6` section “Implementation sequence”, step 3 now explicitly requires updating `official.yml` and `buddy.yml` in the same PR to consume `is_prerelease`, and step 4 states the guard must use the resolver output.

### Optional improvements (review suggestions) that were also clarified in PLAN_6

- **Detector skip list**: `PLAN_6` now names a minimum skip set beyond just `.git`/`node_modules`.
- **Node SRI computation**: `PLAN_6` now defines how to compute local SRI from tarball bytes.
- **PyPI validation ordering**: `PLAN_6` now explicitly requires digest validation before invoking the publish action.
