<!-- markdownlint-disable MD013 MD029 -->

# CLARIFY_PLAN_3: Additional maintainer confirmations for PLAN_3

This file lists items that still require explicit maintainer decisions/confirmation before implementing `.AGENTS/PLAN_3.md`.

## 1) Ruby gem readiness in this monorepo (blocking)

The repo currently contains exactly one `.gemspec`:

- `src/public/lib/asciidoctor-latexmath/asciidoctor-latexmath.gemspec`

Status: **RESOLVED (2026-01-05)**.

The missing `lib/` tree and required version file have been restored in commit:

- `51559b8a3ac7ddf2083b90715327d72d6234de55` ("chore(asciidoctor-latexmath): Add Missing Files & Adapt Gem Spec for Migration")

This unblocks Ruby gem builds for `asciidoctor-latexmath`.

## 2) Ruby gemspec metadata: linking and repository identity (blocking / policy)

Maintainer-confirmed policy (CLARIFY_PLAN_1) requires gemspec metadata `github_repo` to be:

- `https://github.com/hcoona/three.git`

Status: **RESOLVED (2026-01-05)**.

`asciidoctor-latexmath.gemspec` now includes `github_repo` and points metadata URLs at the monorepo (including `spec.homepage`).

## 3) RubyGems.org Trusted Publishing: exact implementation path

`PLAN_3` assumes `rubygems/configure-rubygems-credentials` can complete RubyGems.org Trusted Publishing (OIDC) without providing `api-token` and without providing `role-to-assume`.

Status: **CONFIRMED (2026-01-05)**.

Hard requirement: RubyGems.org publishing is **Trusted Publishing only**.

- No long-lived secrets (no RubyGems API key).
- No alternative authentication fallback.

If `rubygems/configure-rubygems-credentials` (OIDC) does not work as assumed for this repo/workflow/environment, then the workflow must fail.

## 4) Idempotent rerun behavior: acceptable network/API footprint

Status: **CONFIRMED (2026-01-05)**.

To verify “already published” artifacts safely, `PLAN_3` uses remote lookups and (for GitHub Packages RubyGems) may need to download the remote `.gem` for SHA comparison.

Potential implementation note:

- `rubygems-await` (https://github.com/segiddins/rubygems-await) provides `gem await`, which waits until a pushed gem becomes available across RubyGems endpoints (compact index, gems, gemspecs, etc.) with a timeout (default 5 minutes).
- This is a reasonable way to handle RubyGems eventual consistency (reduce flakiness when verifying availability / downloading the gem for digest comparison).

Decision: we will use `rubygems-await` to handle RubyGems eventual consistency.

Version pinning:

- Prefer pinning via Bundler (Gemfile / Gemfile.lock) in the relevant Ruby package directory.
- If the publish job is not using Bundler for that package, install a pinned version explicitly.
