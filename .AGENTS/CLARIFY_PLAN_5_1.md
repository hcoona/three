<!-- markdownlint-disable MD013 MD029 -->

# CLARIFY_PLAN_5_1: Additional maintainer confirmations (if needed) for PLAN_5 implementation

This file lists potential follow-up questions discovered during a strict addendum review (`.AGENTS/PLAN_REVIEW_5_1.md`).

If the maintainer policy remains “no additional confirmations required” (as stated in `.AGENTS/CLARIFY_PLAN_5.md`), these items can be treated as implementation details and validated empirically in CI.

## 1) PyPI rerun-idempotency: partial publish handling

When rerunning an official Python publish where **some** distribution files for a version already exist on PyPI (e.g., sdist exists but wheel upload previously failed):

- Should the workflow be allowed to upload only the **missing** files, as long as any already-existing filenames have matching digests?

Options:

- Option A (recommended): Yes. Verify digest for any existing filenames; upload only missing files.
- Option B: No. If any file exists for the version, treat the entire publish as “already published” and do not upload missing files.
- Option C: No. If version exists but the full local set does not already exist, fail the run.

Status: RESOLVED (2026-01-05)

Decision: Option A.

Rationale: aligns with the PLAN_5 “idempotent reruns” contract while still enforcing “never overwrite” via per-filename digest verification.

## 2) GitHub Packages RubyGems idempotency: authenticated `gem fetch` username

PLAN_5 uses `gem fetch` with an authenticated source URL of the form:

- `https://<user>:<token>@rubygems.pkg.github.com/<owner>/`

When using `${{ github.token }}` (no PAT fallback), which `<user>` should we standardize on?

- Option A: `${{ github.actor }}`
- Option B: `${{ github.repository_owner }}`
- Option C: a fixed username (if RubyGems client/server ignores it)

Goal: choose the value that works reliably with GitHub Packages RubyGems and does not introduce a fallback credential path.

Status: RESOLVED (2026-01-05)

Decision: Option A (`${{ github.actor }}`).

Evidence: GitHub Docs “Working with the RubyGems registry” specifies that for authenticated RubyGems source URLs, `USERNAME` must be your GitHub username.
Reference: https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-rubygems-registry

Implementation note: use `${{ github.actor }}` as the `USERNAME` and `${{ github.token }}` as the `TOKEN` in the authenticated `--source` URL.
