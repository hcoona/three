<!-- markdownlint-disable MD013 MD024 MD029 -->

# PLAN_REVIEW_6_5: Strict review of PLAN_6 (RubyGems support; Trusted Publishing only)

This is an independent strict review of `.AGENTS/PLAN_6.md`.

Scope focus:

- Add Ruby gem build/publish support to the root release workflows.
- RubyGems.org publishing MUST be Trusted Publishing (OIDC) only (no API key secrets, no fallback).
- GitHub Packages RubyGems publishing MUST use `github.token` only (no PAT fallback).
- Reruns must be idempotent across registries by verifying digests.

This review deliberately does **not** consult any `.AGENTS/PLAN_REVIEW_*.md` files.

## Update after maintainer confirmations (2026-01-06)

Maintainers confirmed and/or manually applied the following decisions, and `PLAN_6` has been updated accordingly:

- `fd` is present in the repository `mise` toolchain (declared in `.mise.toml`, locked in `.mise.lock`).
- Artifact determinism has been manually confirmed for the supported build workflows.
- Detector migration uses **Approach A**: introduce the unified detector first and switch the resolver to it (no retrofit of legacy detectors).
- Ruby gem build MUST rely on `gem build --output` and MUST NOT introduce a fallback path.
- Logging hardening notes are intentionally ignored.

## High-level verdict

`PLAN_6` is directionally correct and consistent with the confirmed policy constraints. The architecture (“build once, publish from artifact” + digest-based idempotency) is coherent.

With the maintainer confirmations above incorporated, the earlier review concerns are either resolved or explicitly decided, and `PLAN_6` is now sufficiently precise to implement without needing additional policy clarifications.

## Requirements compliance check

### RubyGems.org publishing: Trusted Publishing (OIDC) only

Plan status: **PASS**

- Explicitly states Trusted Publishing only.
- Explicitly disallows long-lived RubyGems API key secrets.
- Uses `rubygems/configure-rubygems-credentials` in Trusted Publisher mode and recommends setting `trusted-publisher: true`.

Notes / tightening:

- The plan should explicitly require `permissions: id-token: write` on the RubyGems.org publish job (it does).
- The plan’s “no fallback” requirement is satisfied by wiring no token inputs/secrets; this is consistent.

### GitHub Packages RubyGems registry: `github.token` only

Plan status: **PASS (policy), RISK (practical)**

- Plan forbids PAT fallback.
- Plan includes the maintainer prerequisites (package linkage / Actions access).

Important practical note:

- GitHub’s own documentation for the RubyGems registry is internally inconsistent: it often frames PATs as the supported method, while also recommending `GITHUB_TOKEN` for workflows that have `admin` access to the package. `PLAN_6` correctly accepts “fail if not configured” and avoids fallback.

### Buddy prerelease-only (all kinds)

Plan status: **PASS**

- Adds `is_prerelease` to resolver output.
- Adds an early guard in `buddy.yml` based on that output.

### Idempotent reruns for all publishes

Plan status: **PASS (concept), NEEDS PRECISION (implementation)**

- Defines the right rule: existing version => success only if digest matches.
- Applies to official and buddy.

Implementation precision issues are covered below.

### Pinning policy for newly introduced third-party actions

Plan status: **PASS (stated)**

- Requires full commit SHA pinning for any new third-party actions.

Plan improvement:

- Where the plan says “pinned to SHA”, it should also emphasize that the SHA must be the _full_ commit SHA (not a shortened prefix) to match the repo policy.

## Resolver / discovery changes

### Unified project discovery via `fd`

Good:

- Moving away from “Python else Node” is necessary once Ruby is added.
- Failing on ambiguity is the correct safety posture.
- JSON-on-stdout with explicit exit codes is a strong contract.

`fd` is now part of the repository `mise` toolchain. The remaining requirement is to keep the resolver job using a minimal install strategy (install only `fd`, not a full toolchain install) to avoid unnecessary runtime cost.

### Exit code contract

Good:

- `0/2/3/1` is clear and supports stable caller behavior.

Update:

- The plan now explicitly chooses **Approach A**: introduce the unified detector first and switch `release-resolve.yml` to it.

## Ruby: build workflow design

### Build-from-artifact correctness

Plan status: **GOOD**

- Uses `gem build` (not `release-gem`) to preserve artifact-first architecture.
- Verifies the built gem’s name/version.
- Enforces “exactly one gem” output.

Update:

- Maintainers confirmed `gem build --output` is available in the release environment.
- The plan now requires relying on `--output` and explicitly forbids adding a fallback path.

### Bundler-based checks

Plan status: **PASS**

- Checks are required when a `Gemfile` exists; skipped otherwise.

Minor improvement:

- Consider also checking for `Gemfile.lock` (optional) to stabilize dependency resolution. This is not a policy requirement, but it reduces release-time flakiness.

### System dependencies

Plan status: **PASS**

- The dependency list matches the existing project CI needs.

Trade-off note:

- Installing LaTeX toolchains for _all_ Ruby gem builds could be costly if more Ruby gems are added later. This is acceptable now, but the plan could note a future optimization (conditional install).

## Ruby: publish workflows

### GitHub Packages RubyGems publish + idempotency

Plan status: **GOOD DIRECTION, NEEDS ERROR-CLASSIFICATION DETAIL**

Positive:

- Uses `github.token` only.
- Uses `gem push --key github --host https://rubygems.pkg.github.com/<owner>` consistent with GitHub docs.
- Uses `gem fetch` with authenticated source URL, consistent with the confirmed approach.

Critical precision gap:

- The plan’s idempotency preflight says:
    - treat “not found” only when the expected file does not exist after `gem fetch`,
    - but also says auth/network errors must fail fast.

Those conditions are not compatible unless the plan also specifies how to interpret `gem fetch` exit status and stderr.

Recommendation:

- Amend the plan to require:
    - capture `gem fetch` exit code and stderr,
    - classify `not found` only for the specific “gem not found” message patterns (or HTTP 404 indicators),
    - treat any other non-zero exit as failure (auth/permission/timeout),
    - and never “fall through to push” on ambiguous errors.

### RubyGems.org publish + idempotency (Trusted Publishing)

Plan status: **PASS, with small corrections**

Positive:

- Uses Trusted Publishing (OIDC) only.
- Uses the RubyGems API v2 version endpoint and compares `sha`.

Corrections / tightening:

- The plan’s API endpoint format should match RubyGems’ documented API v2 path:
    - `GET /api/v2/rubygems/<gem>/versions/<version>.json?platform=ruby`
    - This appears correct, but the plan should explicitly state how to handle:
        - HTTP 429 (rate limit) and 5xx => fail (do not treat as “not found”).

`rubygems-await` usage:

- Using `rubygems-await` to mitigate eventual consistency is reasonable.
- The plan should clarify how `rubygems-await` interacts with digest verification:
    - waiting does not itself verify the SHA, so the final gate should still be the API `sha` compare (or a verified download+hash).

## Node: publish semantics and idempotency

Plan status: **PASS (concept), NEEDS FAILURE-MODE DETAIL**

- Using `npm view ... dist.integrity` as the authoritative remote digest is correct.
- Computing local SRI from the exact tarball bytes is correct.

Precision gaps:

- The plan should specify the exact behavior when `npm view` fails:
    - 404 / E404 => “not found”
    - auth / 401 / 403 => fail
    - network => fail

Without this, “treat missing output as not found” could incorrectly attempt to publish during transient failures.

## Python (PyPI): publish semantics and idempotency

Plan status: **PASS**

- File-level digest verification + `skip-existing` after verification is the correct pattern.

Plan tightening:

- Add an explicit rule for “project does not exist on PyPI” (HTTP 404 for the project JSON): treat as no remote files and proceed to publish.

## Reproducibility baseline

Plan status: **PASS (determinism confirmed)**

- Setting `TZ`, `LC_ALL`, and `SOURCE_DATE_EPOCH` is a good start.

Maintainers confirmed byte-for-byte determinism for the supported packaging workflows, which is a prerequisite for digest-gated idempotency.

## Maintainer setup checklist

Plan status: **PASS**

- RubyGems Trusted Publisher configuration is correctly specified (repo, workflow filename, environment name).
- GitHub Packages linkage requirement is stated.

Suggested improvement:

- Add an explicit _preflight validation_ step in the Ruby build workflow that checks gemspec metadata includes the required repository linkage field (to fail early with a precise message).

## Implementation sequence

Plan status: **MOSTLY OK, can be simplified**

- The sequencing is incremental and testable.

Suggested simplification:

- Decide whether the unified detector replaces the two existing detectors or wraps them.
- If replacing: introduce `find_project_path.py` + `fd` toolchain changes early, then delete/stop calling the old scripts.

## Summary of concrete changes needed in PLAN_6 (to reduce implementation risk)

1. Specify robust error classification rules for:
    - `gem fetch` (GPR Ruby),
    - `npm view` (npmjs + GPR),
    - HTTP API calls (RubyGems.org + PyPI).
2. Ensure the resolver uses the minimal `fd` install strategy (`mise install fd`).

No new maintainer confirmations appear necessary.
