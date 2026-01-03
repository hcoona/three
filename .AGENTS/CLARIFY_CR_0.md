# Clarifications requested by Code Review (CR_0)

Status: NEEDS HUMAN CONFIRMATION
Date: 2026-01-02
Scope: root workflows under `/.github/workflows/*.yml`

This file captures decision points discovered during code review of the staged workflow refactor. These are _not_ fully decided by `.AGENTS/CLARIFY_0.md`–`.AGENTS/CLARIFY_4.md`, or require explicit confirmation to avoid accidental behavior changes.

## 1) Action major versions (`actions/checkout@v6`, `actions/setup-node@v6`, ...)

Several workflows use major versions that may or may not exist (or may not be generally available) on GitHub Actions.

Please confirm one of:

- A) These major versions are valid and intentionally used.
- B) We must pin to currently available major versions.
- C) We must pin to commit SHAs for all critical actions.

Rationale: if the major tags do not exist, the workflows fail immediately.

## 2) Node pack workflow permissions for dependency install

`release-build-node-pack.yml` configures `.npmrc` for GitHub Packages and runs `pnpm install`.

Question:

- Do we expect Node dependency installation to ever pull from `npm.pkg.github.com` using `GITHUB_TOKEN`?

If yes, please confirm that we should add `packages: read` to the pack job permissions.
If no, please confirm we should remove the GitHub Packages `.npmrc` wiring during install to avoid confusing runtime failures.

## 3) Official attestation policy: must it gate GitHub Release creation?

`official.yml` adds `attest-*` jobs, but the `release-*` jobs do not currently depend on them.

Please confirm the intended policy:

- A) Attestation is mandatory for official releases and must block `release-*` when it fails.
- B) Attestation is best-effort and should not block `release-*`.

(Clarify_1 suggests A, but this should be explicitly confirmed.)

## 4) Project naming: do we support scoped Node package names?

`release-resolve.yml` validates `project` using `^[A-Za-z0-9._-]+$`, which _rejects_ `@scope/name`.

Please confirm one of:

- A) Release `project` inputs are always unscoped workspace names (current intent). Scoped names are intentionally unsupported.
- B) We must support scoped package names for Node projects (would require changing validation and tag parsing conventions).

## 5) Buddy non-clobber guard: how to treat draft releases?

Current implementation blocks buddy if an existing release for the tag has `prerelease=false`.

Please confirm:

- A) Draft releases with `prerelease=false` are treated as protected (buddy must fail).
- B) Draft releases are considered safe to update by buddy.

Default recommendation remains A (safer), but confirm explicitly.

## 6) Npm dist-tag validation rules

`release-build-node-pack.yml` derives the dist-tag from `PrereleaseVersionNoLeadingHyphen` (first segment, lowercased) and validates using:

- `^[a-z0-9][a-z0-9-]*$`

Please confirm whether this is the desired rule (and whether we should allow additional characters such as `_` or upper-case).

## 7) GitHub Packages scope casing

The pack workflow uses a lowercased scope for `.npmrc` (`@${OWNER,,}`) but passes the raw owner (`github.repository_owner`) into `prepare_npm_publish.py`.

Please confirm whether the scope/name rewrite should always be lowercase to match `.npmrc` and typical npm scope conventions.
