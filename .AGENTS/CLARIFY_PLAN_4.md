<!-- markdownlint-disable MD013 MD029 -->

# CLARIFY_PLAN_4: Follow-up maintainer confirmations for PLAN_4

This file captures items that require explicit maintainer confirmation to implement `.AGENTS/PLAN_4.md` without surprises.

## 1) GitHub Packages RubyGems idempotency: how do we fetch the remote `.gem` reliably?

`PLAN_4` requires rerun-idempotency via digest comparison for GitHub Packages RubyGems publishing.

However, GitHub’s documentation distinguishes:

- publishing authentication via `~/.gem/credentials` (`:github: Bearer TOKEN`), and
- installation/fetch authentication via a source URL that embeds `USERNAME:TOKEN@...`.

Please confirm the intended, supported approach for **downloading** an already-published gem to compare digests.

Options:

1. **Direct HTTP download of the gem file** (preferred if supported)
    - Determine the canonical URL for the gem file, e.g.:
        - `https://rubygems.pkg.github.com/<OWNER>/gems/<name>-<version>.gem`
    - Authenticate via HTTP header (Bearer) using `${{ github.token }}`.
    - Compute SHA-256 of the downloaded file and compare.

2. **Use `gem fetch` with an authenticated source URL**
    - Use a source URL that embeds credentials.
    - Ensure the token is not printed to logs.

3. **Use GitHub Packages APIs** (if they provide a stable checksum / download mechanism for RubyGems)

Decision (choose one):

- [ ] Option 1
- [x] Option 2
- [ ] Option 3

Status: CONFIRMED (2026-01-05)

Decision: Use `gem fetch` with an authenticated source URL for downloading an already-published `.gem` when verifying digests.

## 2) Scope of “idempotent reruns” requirement: official-only or all publishes (including buddy)?

`PLAN_4` states: “For any registry publish step: if version exists, treat as success only if digest matches.”

Today, buddy Node publishes to GitHub Packages without rerun-safe logic.

Please confirm which policy you want:

1. **Idempotency applies to all publishes (official + buddy)**
    - Update buddy Node publish (GPR) to implement the digest verification logic.
    - Implement the same logic for buddy Ruby GitHub Packages publishing.

2. **Idempotency applies to official releases only**
    - Keep buddy flows simpler; reruns may fail with “version already exists”.
    - Update the wording in `PLAN_4` to avoid stating a stronger contract than we intend to implement.

Decision:

- [x] Option 1
- [ ] Option 2

Status: CONFIRMED (2026-01-05)

Decision: Idempotency applies to all publishes (official + buddy).

## 3) Ruby artifact naming: stable filename vs versioned filename

`PLAN_4` proposes outputting a single artifact at `out/<project>.gem`.

Please confirm whether you want:

1. **Stable filename** (`out/<project>.gem`) (current plan)
2. **Versioned filename** (`out/<project>-<version>.gem`) for better manual inspection / parallelism

Decision:

- [ ] Option 1
- [x] Option 2

Status: CONFIRMED (2026-01-05)

Decision: Use a versioned artifact filename for Ruby gem outputs (e.g. `out/<project>-<version>.gem`).

## 4) RubyGems.org Trusted Publishing configuration constraints

`PLAN_4` expects the RubyGems.org trusted publisher configuration to match:

- repository: `hcoona/three`
- workflow filename: `official.yml`
- environment: `rubygems`

Please confirm that:

- the trusted publisher will be configured **for each gem** that will be released from this repository, and
- the environment constraint will be used (and is named exactly `rubygems`).

Decision:

- [x] Confirmed
- [ ] Needs change

Status: CONFIRMED (2026-01-05)
