# CLARIFY_PLAN_1: Follow-up maintainer confirmations for PLAN_1

This file lists items that may still require explicit maintainer confirmation to avoid “surprise” failures when implementing `.AGENTS/PLAN_1.md`.

## 1) Ruby version input policy (what is allowed?)

PLAN_1 introduces `rubygems_version` derived from the release `version`, but does not explicitly state the allowed input space.

Please confirm:

1. For Ruby releases, what version formats are accepted by the resolver/build/publish pipeline?

Status: CONFIRMED (2026-01-05)

Decision: when building and publishing Ruby gems, accept **only** the SemVer2-variant Ruby-style version format (as described in question (2)), and reject:

- PEP 440 versions
- strict SemVer 2.0.0 strings

Practical meaning:

- Allowed: `MAJOR.MINOR.PATCH` and optional prerelease segments expressed in Ruby style, e.g. `1.2.3`, `1.2.3.beta.1`, `1.2.3.rc.0`.
- Disallowed examples:
    - SemVer2 prerelease separator: `1.2.3-beta.1`
    - SemVer2 build metadata: `1.2.3+build.7` or `1.2.3-beta.1+build.7`
    - RubyGems-valid but non-SemVer2-core forms: `1.0`, `2026.01.05`, `1.0.pre`

1. How should we treat SemVer build metadata (`+...`) for Ruby releases?
    - Status: RESOLVED (2026-01-05)
    - Policy: build metadata is not accepted for Ruby releases because strict SemVer2.0 inputs are rejected. (No dropping/normalization step is needed.)

## 2) Which version string is “user-facing” for Ruby?

When `version` and `rubygems_version` differ after normalization (e.g. `1.2.3-beta.1` vs `1.2.3.beta.1`), please confirm what should appear in:

- GitHub Release title (`release_title`)
- Release notes header

Options:

- Option A: show the original tag version (current pattern for other kinds)
- Option B: show `rubygems_version` for Ruby
- Option C: show both (e.g. `foo v1.2.3-beta.1 (RubyGems 1.2.3.beta.1)`)

Status: RESOLVED (2026-01-05)

Decision: for Ruby releases, `version` and `rubygems_version` are identical (no normalization from SemVer2 is performed), so the same string can be used for GitHub Release title and release notes.

## 3) GitHub Packages RubyGems: required repository linkage / Actions access

Because we intentionally have **no fallback secret** and rely only on `github.token`, please confirm whether we will:

- Require that each gem package in GitHub Packages is linked to `hcoona/three` (or that the org setting allows automatic inheritance), OR
- Add an explicit maintainer step: configure “Manage Actions access” for the package to grant this repository workflow access.

If we want the workflow to be self-service, should we require Ruby gemspec metadata `github_repo` so GitHub can auto-link the package?

Status: CONFIRMED (2026-01-05)

Decision: use both protections.

1. Require gemspec metadata for auto-linking

- Each released gem must set `github_repo` in the gemspec metadata to:
    - `https://github.com/hcoona/three.git`

1. Require GitHub Packages Actions access to be correct

- Maintainers must ensure the published gem package in GitHub Packages is either:
    - linked to `hcoona/three` with permission inheritance enabled, or
    - explicitly configured under “Manage Actions access” to allow workflows from `hcoona/three`.

Rationale: we intentionally do not provide any fallback secret/token; publishing must succeed using `github.token` only.

## 4) Buddy release safety: are stable versions allowed?

Buddy releases publish Ruby gems to GitHub Packages.

Please confirm whether buddy runs are allowed to publish a _stable_ version (no prerelease markers). If yes, an accidental buddy publish of a stable version could later cause the official release to fail publishing to GitHub Packages due to “version already exists”.

Options:

- Option A: Allow (current Node buddy has the same structural risk).
- Option B: Disallow stable versions for buddy (add a resolver-time guard for Ruby buddy runs).

Status: CONFIRMED (2026-01-05)

Decision:

- Buddy releases are allowed to publish **prerelease versions only**.
- Buddy releases must never publish a stable/final version.
- Official releases may publish stable or prerelease versions.

Non-clobber rule (source of truth):

- Buddy must not create or modify an existing GitHub Release whose `prerelease` flag is `false`.
- Buddy may create or update GitHub Releases only when `prerelease` is `true`.

Scope:

- This policy must be applied consistently to Ruby, Node (npm), and Python buddy publishing flows.

Rationale:

- Prevent buddy runs from blocking later official publishes due to “version already exists” (registries) and prevent buddy runs from overwriting an official GitHub Release.
