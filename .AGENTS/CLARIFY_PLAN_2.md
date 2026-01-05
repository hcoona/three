<!-- markdownlint-disable MD013 MD029 -->

# CLARIFY_PLAN_2: Maintainer confirmations needed for PLAN_2

This file records items that require explicit maintainer decisions/confirmation before implementing `.AGENTS/PLAN_2.md`.

## 1) Ruby release build: system dependencies and test scope

`PLAN_2` proposes running `bundle exec standardrb` and `bundle exec rspec` in the reusable Ruby build workflow when a `Gemfile` exists.

In this repository, at least one Ruby project’s CI installs OS-level dependencies (e.g., TeX Live / converters / ImageMagick / Ghostscript) before running specs.

Please confirm which strategy we should adopt for release builds:

1. **Install system dependencies in the release build job**
    - Pros: release build is “self-contained” and closely matches CI.
    - Cons: slower, more moving parts, more apt breakage risk.

2. **Define a project-provided release check hook** (recommended for monorepo)
    - Example: if `script/release-check` (or similar) exists, run it; otherwise run a minimal default (`standardrb` + `rspec`).
    - Pros: each Ruby project can define its own dependencies and scope.
    - Cons: requires project discipline.

3. **Reduce release-time checks**
    - Example: run lint only (or lint + fast unit tests), and treat heavy integration specs as CI-only.
    - Pros: fastest; minimal system deps.
    - Cons: weaker guarantees in the release job.

Decision:

- [x] Option 1
- [ ] Option 2
- [ ] Option 3

Minimal default apt package list (based on the existing CI for `src/public/lib/asciidoctor-latexmath` and quick apt-cache verification):

- `texlive-latex-base`
- `texlive-latex-recommended`
- `texlive-fonts-recommended`
- `dvisvgm`
- `pdf2svg`
- `poppler-utils`
- `imagemagick`
- `ghostscript`

## 2) Official publish reruns: partial publish policy

Official Ruby releases publish to both RubyGems.org and GitHub Packages. If one publish succeeds and the other fails, rerunning will often fail due to “version already exists” on the already-published registry.

Please confirm the intended policy:

1. **Accept non-idempotent behavior**
    - If partial publish happens, maintainers must fix manually (yank/bump version) before retry.

2. **Add preflight checks and fail early**
    - Before publishing, check both registries for the version and abort if either already has it.

3. **Implement idempotent retry logic**
    - Attempt publish; if version exists, treat as success if the artifact digest matches (or skip).
    - (This is most complex and may require registry APIs.)

Decision:

- [ ] Option 1
- [ ] Option 2
- [x] Option 3

## 3) Build reproducibility (Ruby / Python / npm)

This repo’s release pipeline is artifact-first and uses attestations. For reproducible builds (bit-for-bit), we must explicitly manage timestamps and other sources of nondeterminism.

### Ruby (.gem)

Observation:

- `src/public/lib/asciidoctor-latexmath/Rakefile` already implements a reproducible build helper (`release:verify`) by:
    - deriving `SOURCE_DATE_EPOCH` (from env or `git log -1 --format=%ct`),
    - invoking `gem build ... --output <path>` with `SOURCE_DATE_EPOCH` set, and
    - building twice to assert identical SHA256 digests.

Implication for `PLAN_2` implementation:

- In `release-build-ruby-gem.yml`, set `SOURCE_DATE_EPOCH` to the target commit timestamp and use `gem build <project>.gemspec --output <deterministic-path>`.
- Prefer publishing a single, explicit `.gem` path (avoid `out/*.gem` globbing).

### Python (sdist/wheel via `uv build`)

Likely nondeterminism sources:

- Archive timestamps and metadata (zip/tar headers) can vary per run.

Requirement:

- Must set `SOURCE_DATE_EPOCH` to the target commit timestamp before invoking `uv build`.
- Optionally standardize locale/timezone for tooling consistency (e.g., `TZ=UTC`, `LC_ALL=C.UTF-8`).

### npm (tarball via `npm pack`)

Likely nondeterminism sources:

- Git checkouts typically set file mtimes to checkout time; `npm pack` may embed mtimes into tar headers.

Requirement:

- Must set `SOURCE_DATE_EPOCH` to the target commit timestamp for the pack step.

Note:

- Maintainer clarified that the tar tooling already follows the `SOURCE_DATE_EPOCH` convention and will automatically normalize mtimes in the archive, so no separate “touch all files” step is required.
