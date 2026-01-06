<!-- markdownlint-disable MD013 MD029 -->

# CLARIFY_PLAN_5_2: Follow-up maintainer confirmations for PLAN_5 (new questions from independent review)

Date: 2026-01-06

This file lists _new_ potential ambiguities identified during an independent strict review of `.AGENTS/PLAN_5.md`.

If maintainers confirm that these are already implied by existing policy, they can be treated as implementation details.

## 1) Ruby version grammar: are numeric-only extra dot segments allowed?

PLAN_5 says Ruby releases accept:

- `MAJOR.MINOR.PATCH` and optional prerelease dot segments (examples: `1.2.3.beta.1`, `1.2.3.rc.0`).

But this leaves ambiguity for versions like:

- `1.2.3.1` (numeric-only extra segment)

In RubyGems, versions with extra numeric segments can behave like “stable” versions rather than prereleases, which interacts with buddy’s prerelease-only enforcement.

Please confirm the intended policy:

- Option A (recommended): **Reject** numeric-only extra segments (e.g. `1.2.3.1` is invalid).
- Option B: **Allow** numeric-only extra segments and treat them as prerelease for buddy gating.
- Option C: **Allow** numeric-only extra segments but treat them as _not prerelease_ for buddy gating.

Status: RESOLVED (2026-01-06)

Decision: Option A.

Rationale: numeric-only extra segments (e.g. `1.2.3.1`) are too close to “stable-ish” versions in RubyGems and create avoidable ambiguity with buddy’s prerelease-only enforcement.

## 2) Ruby prerelease detection rule for `is_prerelease`

PLAN_5 currently defines Ruby `is_prerelease=true` iff the version has any segment beyond `MAJOR.MINOR.PATCH`.

Given RubyGems’ prerelease semantics (typically signaled by letter-containing segments), please confirm which rule we should use in `validate_rubygems_version.py`:

- Option A (recommended): `is_prerelease=true` iff any segment contains letters (RubyGems-style prerelease).
- Option B: `is_prerelease=true` iff the version has any extra dot segments.

This affects:

- buddy prerelease-only enforcement
- consistency with RubyGems’ `prerelease` concept

Status: RESOLVED (2026-01-06)

Decision: Option B.

Notes:

- Under Decision (1) Option A (reject numeric-only extra dot segments), any accepted version that has “extra dot segments” is expected to include prerelease markers (letters) somewhere in the suffix. In that policy regime, Option A and Option B become equivalent in practice for valid inputs.
- Option B is simpler and less error-prone to implement as long as the validator enforces Decision (1).
