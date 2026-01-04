# Clarifications requested by Code Review (CR_1)

Status: NEEDS HUMAN CONFIRMATION
Date: 2026-01-03
Scope: root workflows under `/.github/workflows/*.yml`

This file captures **remaining** decision points found during follow-up review. It intentionally avoids duplicating items already confirmed in `.AGENTS/CLARIFY_CR_0.md`.

---

## 1) WXT: required browser matrix (and whether default implies Chrome)

Context:

- `.github/workflows/release-build-wxt.yml` runs:
    - `wxt zip` (default)
    - `wxt zip -b firefox`
    - `wxt zip -b edge`

Question:

- What is the required browser set for official/buddy WXT releases?

Please confirm one of:

- A) Default `wxt zip` is sufficient for the “main” browser output (treat as Chrome implicitly), plus explicit `firefox` and `edge`.
- B) We must be explicit and run `wxt zip -b chrome` (and define the complete list explicitly, e.g. `chrome,firefox,edge`).
- C) Other (please specify the exact browser list).

Decision: B

---

## 2) WXT: should we run the standard Node quality checks?

Context:

- `.AGENTS/CLARIFY_1.md` / `.AGENTS/CLARIFY_3.md` require Node pack builds to run quality checks by default.
- `release-build-wxt.yml` currently installs deps and builds zips, but does **not** run `lint/typecheck/test/build` scripts.

Question:

- Should WXT builds run the same default quality checks as Node pack builds?

Please confirm one of:

- A) Yes. Run `pnpm --filter <project> --if-present lint|typecheck|test|build` before producing zips.
- B) No. WXT builds are “artifact-only” and do not run quality checks.

Decision: A

---

## 3) Buddy GPR publishing: standardize the authentication mechanism

Context:

- `buddy.yml` uses `actions/setup-node@v6` with `registry-url`/`scope`, but the actual `npm publish` step does not explicitly set `NODE_AUTH_TOKEN` or a step-scoped `.npmrc`.

Question:

- Which auth pattern should we standardize on for publishing to `npm.pkg.github.com` in buddy workflows?

Please confirm one of:

- A) Always set `NODE_AUTH_TOKEN` on the `npm publish` step (simple and explicit).
- B) Always write an explicit `.npmrc` (step-scoped) and use `NPM_CONFIG_USERCONFIG` for the publish step (most deterministic).
- C) Rely on `actions/setup-node` side effects (least explicit; only choose if this is known to work reliably in this repo).

Decision: B (confirmed).

Rationale:

- GitHub Packages docs for the npm registry describe authentication via `.npmrc` and an auth token line (`//npm.pkg.github.com/:_authToken=...`) and recommend using `GITHUB_TOKEN` in GitHub Actions workflows.
- Using an explicit, step-scoped `.npmrc` + `NPM_CONFIG_USERCONFIG` makes the publish step independent of implicit side effects from other steps, and makes it obvious that the token must be present at publish time.

---

## 4) Official attestations: ordering relative to publishing (policy nuance)

Context:

- `.AGENTS/CLARIFY_CR_0.md` confirms: attestation is mandatory for official releases and must gate GitHub Release creation.
- Current structure supports running `attest-*` in parallel with `publish-*` (both consume the same `out/*` artifact).

Question:

- Should the attestation job be required to run **after** the corresponding publish job succeeds (so attestation only happens for artifacts that were successfully published), or is parallel execution acceptable as long as the GitHub Release is gated on both?

Please confirm one of:

- A) Parallel is OK; GitHub Release must depend on both `publish-*` and `attest-*`.
- B) Attestation must run after publish succeeds; GitHub Release depends on `publish-*` -> `attest-*`.

Decision: A
