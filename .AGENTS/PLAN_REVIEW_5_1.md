<!-- markdownlint-disable MD013 MD024 MD029 -->

# PLAN_REVIEW_5_1: Strict review addendum for PLAN_5 (RubyGems Trusted Publishing only; no fallback)

This is an **addendum** to `.AGENTS/PLAN_REVIEW_5.md`.

- `PLAN_REVIEW_5.md` already validated that `.AGENTS/PLAN_5.md` is broadly consistent with the maintainer-confirmed policies in `.AGENTS/CLARIFY_PLAN_0.md` … `.AGENTS/CLARIFY_PLAN_4.md`.
- This addendum focuses on **remaining underspecifications** discovered by re-evaluating PLAN_5 against the **current** root workflows:
    - `.github/workflows/release-resolve.yml`
    - `.github/workflows/official.yml`
    - `.github/workflows/buddy.yml`
    - `.github/workflows/release-build-python.yml`
    - `.github/workflows/release-build-node-pack.yml`

Non-negotiable constraint (re-stated): **publishing to RubyGems.org must use Trusted Publishing (OIDC) only, and must not fall back to long-lived API keys**.

## What remains solid and policy-aligned

### RubyGems.org publishing: Trusted Publishing only, no fallback

PLAN_5 satisfies the “Trusted Publishing only” requirement:

- It requires `permissions: id-token: write`.
- It uses a GitHub Actions environment (`rubygems`).
- It forbids RubyGems API key secrets and any alternative authentication fallback.

This matches the already-confirmed policy in `.AGENTS/CLARIFY_PLAN_3.md`.

### GitHub Packages RubyGems: no PAT fallback

PLAN_5 correctly holds the line:

- publish auth is `${{ github.token }}` + `packages: write`
- no PAT fallback secret

This matches `.AGENTS/CLARIFY_PLAN_0.md` and `.AGENTS/CLARIFY_PLAN_1.md`.

### Resolver architecture: the current repo requires this redesign

Today’s `release-resolve.yml` is still sequential (“Python → else Node → else fail”) and cannot represent ambiguity.

PLAN_5’s detector exit-code contract + cross-kind resolution is the right structure for safely introducing Ruby.

## Correctness-level gaps to fix in PLAN_5 before implementation

### 1) Python idempotent reruns are underspecified for multi-file publishes (partial publish handling)

Status: RESOLVED in `.AGENTS/PLAN_5.md` (section “8) Align Python publishing semantics (PyPI)”).

PLAN_5 states:

> “If version exists: compare PyPI-provided file digests to local artifacts in `out/*`; if all match: skip publish; else: fail.”

This is not sufficient to satisfy the repo’s stated idempotent contract in the presence of **partial publishes**, because a PyPI release commonly consists of multiple files (sdist + wheel(s)).

Example failure mode:

- First run successfully uploads `foo-1.2.3.tar.gz` but fails before uploading `foo-1.2.3-py3-none-any.whl`.
- On rerun, the version exists, but only a subset of files exist.
- A strict “skip publish if version exists” would incorrectly skip uploading the missing file.
- A strict “fail if not all digests match” would incorrectly fail even though the already-uploaded file matches and the missing file should simply be uploaded.

**Required plan tightening:** define Python publish idempotency at the **file** level:

- For each local artifact file in `out/*`:
    - If a file with the same filename exists on PyPI:
        - If its digest matches: treat that file as already-published and do not block the run.
        - If its digest does not match: fail (never overwrite).
    - If it does not exist on PyPI: upload it.

Implementation hint (not a hard requirement of the plan): you can preflight via `GET https://pypi.org/pypi/<project>/json` and build a `filename -> sha256` map from the `urls` list, then run the publish step with a “skip existing files” mode (but only after verifying that any existing filenames match expected digests).

### 2) GitHub Packages RubyGems idempotency should explicitly handle eventual-consistency races

Status: RESOLVED in `.AGENTS/PLAN_5.md` (section “5) Ruby publish: GitHub Packages (buddy + official)”, idempotency step 4).

PLAN_5’s GitHub Packages RubyGems idempotency uses a **preflight `gem fetch`**:

- If fetch succeeds: compare SHA-256; success only on match.
- If fetch reports “not found”: push.

This is directionally good, but it should explicitly cover the race where:

- the gem exists remotely, but `gem fetch` cannot see it yet (index propagation delay), and
- `gem push` then fails with “version already exists”.

Without handling that scenario, reruns can fail even though the already-published gem is correct.

**Required plan tightening:** add a second-chance branch:

- If `gem push` fails with an “already exists / repush not allowed” error, then:
    - retry `gem fetch` with bounded backoff (or a short wait),
    - then compare digests.

This keeps the “deterministic discriminator” property while avoiding false negatives due to eventual consistency.

## High-value tightening (recommended, reduces implementation surprises)

### A) Update workflow input descriptions to reflect Ruby’s version rules

Current root workflows (`official.yml`, `buddy.yml`) describe manual `version` input as:

- Python: PEP 440 subset
- Others: SemVer 2.0.0

Once Ruby support is added, that text becomes misleading.

Recommended: update the input descriptions to include Ruby’s rule:

- Ruby: `MAJOR.MINOR.PATCH` + optional RubyGems-style prerelease dot segments (no `-`, no `+`).

### B) New actions and tools should be pinned consistently

PLAN_5 says “pinned” for:

- `ruby/setup-ruby@v1`
- `rubygems/configure-rubygems-credentials`
- `rubygems-await`

The repository already pins some third-party actions to SHAs. PLAN_5 should specify the required pinning policy for **new** actions/tools introduced by this plan so implementation does not drift.

### C) Clarify how GitHub Release asset clobbering relates to “no overwrite”

The reusable GitHub Release job uploads assets with `--clobber` (by design) to update release assets on reruns.

PLAN_5’s “never overwrite” rule must be explicitly scoped to **registries** (PyPI/npm/RubyGems/RubyGems GPR), not to GitHub Release assets.

## Conclusion

PLAN_5 remains a strong and policy-aligned blueprint for adding Ruby gem release support with Trusted Publishing only.

To be fully “rerun-idempotent” (as the plan promises) without introducing operational dead-ends, PLAN_5 should be tightened in two places:

1. Python publish idempotency must be defined at **file granularity** to support partial publishes safely.
2. GitHub Packages RubyGems idempotency should include an explicit “push says exists → fetch+compare with retries” path to handle propagation delays.

With these adjustments, PLAN_5 should be safe to implement while preserving:

- artifact-first publishing,
- least-privilege permissions,
- buddy prerelease-only safety,
- and “RubyGems.org Trusted Publishing only; no fallback”.
