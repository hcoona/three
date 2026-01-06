<!-- markdownlint-disable MD013 MD029 -->

# CLARIFY_PLAN_6_3: Maintainer confirmations needed for PLAN_6

Date: 2026-01-06

This file lists items that appear to require **additional maintainer confirmation** (policy / compatibility level) discovered during an independent strict review of `.AGENTS/PLAN_6.md`.

## 1) Ruby toolchain version vs Ruby project compatibility

`PLAN_6` proposes setting `RUBY_VERSION` in the entry workflows to `3.3`.

However, the only current Ruby gem project in this repo (`src/public/lib/asciidoctor-latexmath`) has a Bundler Ruby requirement in `Gemfile`:

- `ruby "~> 3.2"`

Status: RESOLVED (2026-01-06)

Decision: keep the pipeline on Ruby 3.3.

Rationale:

- In RubyGems/Bundler, the pessimistic constraint operator `~>` is segment-sensitive.
- `ruby "~> 3.2"` means **$>= 3.2$ and $< 4.0$**.
    - (By contrast, `ruby "~> 3.2.0"` would mean **$>= 3.2.0$ and $< 3.3.0$**.)

Therefore, Ruby 3.3 is compatible with the existing `Gemfile` constraint, and no workflow/package change is required for this item.
