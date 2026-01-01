# Clarifications for the release workflow refactor (CLARIFY_2)

This file captures remaining open questions discovered while reviewing `PLAN_1.md` against `CLARIFY.md`, `CLARIFY_1.md`, and the current root workflows under `/.github/workflows/*.yml`.

## Scope

Questions in this file apply only to root workflows under `/.github/workflows/*.yml`.

## 1) Buddy non-clobber: how to treat draft releases?

`CLARIFY_1.md` defines the buddy rule based on `prerelease=true|false`.

Open question:

- If a release exists for the tag and `prerelease=false` but `draft=true`, should buddy:
    - fail fast (treat draft as non-prerelease), or
    - allow (treat draft as “not official yet”), or
    - decide based on both flags?

Recommendation: fail fast if `prerelease=false` regardless of `draft` to keep the rule simple and safe.

## 2) Buddy non-clobber guard: which API contract should we standardize on?

To implement the guard, we need a stable way to query release metadata.

Options:

- `gh release view <tag> --json prerelease` (requires `gh` availability and `GH_TOKEN`)
- `gh api repos/{owner}/{repo}/releases/tags/{tag}` with `--jq '.prerelease'`

Question:

- Which approach should be standardized (for error messaging and portability)?

Recommendation: use `gh api .../releases/tags/{tag}` because it is explicit and can also read `draft` and `id` if needed.

## 3) npm dist-tag validation rules

`CLARIFY_1.md` requires deriving the tag as the first dot-separated segment of `PrereleaseVersionNoLeadingHyphen` and failing if invalid.

Open questions:

- What exact validation should be used for a dist-tag string?
    - allow only lowercase `[a-z0-9-]+`?
    - allow uppercase?
    - allow underscores?

Recommendation: restrict to npm-friendly tags and normalize to lowercase, but this must be explicitly decided to avoid accidental breaking changes.

## 4) NBGV prerelease metadata format expectations

`CLARIFY_1.md` recommends prerelease formats like `-beta.{height}` so that `PrereleaseVersionNoLeadingHyphen` begins with `beta`.

Open question:

- Do we want to accept prerelease values without a dot (e.g., `beta1`) and treat the full string as the channel, or require `channel.<number>`?

Recommendation: accept both, but require the first segment (before `.`) to be non-empty and valid.

## 5) Where should official GitHub asset attestation run?

`CLARIFY_1.md` requires:

- `buddy.yml`: no GitHub attestations for release assets
- `official.yml`: do generate GitHub attestations for release assets (`out/*`)

Open question:

- Should the attestation run:
    - (A) immediately after building `out/*` (before publication),
    - (B) after publication steps, but before GitHub Release creation,
    - (C) in a dedicated job that downloads the dist artifact and attests it?

Recommendation: (C) a dedicated job that downloads the dist artifact and attests `out/*` is the cleanest in a build/publish split, but this is a policy call.

## 6) WXT zip collisions: desired failure message and diagnostics

`CLARIFY_1.md` requires failing fast on basename collisions when copying into a flat `out/`.

Open questions:

- Should the workflow print a full list of conflicting filenames and their source paths?
- Should it mention that `.output/*.zip` is a hard requirement and no recursion occurs?

Recommendation: yes to both; make the failure actionable.
