<!-- markdownlint-disable MD013 MD029 -->

# CLARIFY_CR_2: Follow-ups from CODE_REVIEW_2 execution

Date: 2026-01-07

This file captures clarifications and decisions provided after `CODE_REVIEW_2`.

## 1) GitHub Packages RubyGems: `gem fetch` authentication behavior

Clarification (not previously persisted):

- `gem fetch` can authenticate using the RubyGems credentials file resolved by `gem env credentials` (i.e., the same credentials mechanism used for `gem push`).
- Therefore, using:
    - `gem fetch ... --source "https://rubygems.pkg.github.com/<OWNER>/"`
    - together with a credentials file containing `:github: Bearer <token>`
      is a valid approach.

Implication:

- The earlier review concern that `gem fetch` must embed credentials in the `--source` URL is **not** applicable for this repository’s chosen implementation.

## 2) `fd` version drift risk

Decision:

- The risk that `fd` is installed as `latest` (via mise) is accepted for now.

## 3) Reduce duplication: adopt “Scheme A”

Decision:

- Adopt Scheme A: extract duplicated publish logic into checked-in scripts and have workflows invoke them.

Notes:

- This may require adding a lightweight checkout step in publish jobs so the scripts are available at runtime.
