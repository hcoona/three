<!-- markdownlint-disable MD013 MD024 MD029 -->

# PLAN_REVIEW_5: Strict review of PLAN_5 (RubyGems Trusted Publishing only; no fallback) + aligned, idempotent publish semantics

This review evaluates `.AGENTS/PLAN_5.md` against:

- maintainer-confirmed policies in `.AGENTS/CLARIFY_PLAN_0.md` … `.AGENTS/CLARIFY_PLAN_4.md`,
- the current root release workflows in `.github/workflows/*` (especially `release-resolve.yml`, `official.yml`, `buddy.yml`), and
- the non-negotiable requirement: **publishing to RubyGems.org must use Trusted Publishing (OIDC) only, with no fallback to long-lived API keys**.

## Executive summary

`.AGENTS/PLAN_5.md` is internally consistent, aligns with the confirmed maintainer decisions, and is materially more implementation-ready than PLAN_4.

Notable improvements over PLAN_4:

- The plan explicitly extends **idempotent reruns** to _buddy_ publishes (Node GPR and Ruby GPR), matching `CLARIFY_PLAN_4`.
- Ruby artifact naming is corrected to the confirmed versioned form `out/<project>-<version>.gem`.
- GitHub Packages RubyGems idempotency now uses the maintainer-confirmed `gem fetch` method.
- RubyGems Trusted Publishing setup is made mechanically safer by explicitly setting `trusted-publisher: true`.

However, there are still **two correctness-level underspecifications** (both easy to fix in the plan) and several “tighten before implementing” items to avoid CI surprises.

## What PLAN_5 gets right (and matches confirmed policy)

### 1) RubyGems.org publishing: Trusted Publishing only, no fallback

PLAN_5 matches the hard requirement and `CLARIFY_PLAN_3`:

- No long-lived RubyGems API key secrets.
- No alternative authentication fallback.
- Requires `permissions: id-token: write` and uses a GitHub Actions environment (`rubygems`).

Also, `rubygems/configure-rubygems-credentials` does support a `trusted-publisher` mode.
Per the upstream `action.yml`, it has an input:

- `trusted-publisher`: “Whether to configure the credentials as a trusted publisher. Defaults to true if no other configuration is given.”

So the plan’s “set `trusted-publisher: true` explicitly” is both valid and a good hardening step.

### 2) Ruby version policy and identity matching are clear and enforceable

PLAN_5 correctly incorporates `CLARIFY_PLAN_1`:

- Ruby versions must be Ruby-style `MAJOR.MINOR.PATCH` with optional prerelease dot segments (e.g. `2.1.0.alpha.1`).
- Reject SemVer hyphen prereleases (`-beta.1`) and build metadata (`+...`).
- Identity matching is explicitly defined: tag project segment, gemspec filename, and built gem name must match.

### 3) Resolver redesign is necessary and correctly motivated

The current `release-resolve.yml` is “Python → else Node → else fail” and cannot represent ambiguity. The plan’s detector contract + cross-kind resolution is the correct architecture for introducing Ruby safely.

### 4) Artifact-first model is preserved

The plan maintains the repo’s “build once → publish from downloaded artifacts” contract across Ruby / Node / Python, which is essential for attestations and for avoiding accidental rebuild drift.

## Correctness issues (must address before implementation)

### 1) GitHub Packages RubyGems publish command is missing the required host/key shape

PLAN_5 states:

- “Publish to `https://rubygems.pkg.github.com/${{ github.repository_owner }}` using `${{ github.token }}`.”
- “Write `~/.gem/credentials` with `:github: Bearer ${{ github.token }}`.”

But it does **not** specify the essential `gem push` flags needed to ensure:

- the push goes to GitHub Packages (not RubyGems.org), and
- the `:github:` credential entry is selected.

Practically, GitHub Packages RubyGems publishing typically requires both:

- `--host https://rubygems.pkg.github.com/<OWNER>`
- `--key github`

Without `--host`, `gem push` defaults to RubyGems.org.
Without `--key github`, it may not use the `:github:` bearer credential.

**Plan fix:** explicitly specify the `gem push` invocation for GPR.

Status: RESOLVED in updated `.AGENTS/PLAN_5.md`.

- PLAN_5 now requires an explicit publish command shape:
    - `gem push --key github --host https://rubygems.pkg.github.com/<owner> out/<project>-<version>.gem`
- PLAN_5 also documents the rationale (default host/key selection pitfalls).

### 2) GitHub Packages RubyGems idempotency needs a robust “already exists” discriminator

PLAN_5 currently says:

> “If `gem push` fails because the version already exists: fetch and compare SHA-256.”

In practice, `gem push` can fail for many reasons (403 due to Actions access, 401 auth, network, malformed gem, etc.). Treating _any_ failure as “maybe already exists” and then fetching could:

- hide real publish failures behind “fetch failed” noise,
- produce misleading diagnostics, and
- create a false success path if fetch returns something unexpected.

**Plan fix:** require a deterministic discriminator:

- either preflight by attempting `gem fetch` first (if it succeeds, compare digests; if it 404s/not found, then push),
- or postflight by parsing the `gem push` error output and only entering the “fetch and compare” path on the specific “repushing not allowed / version exists” failure.

(Preflight is usually simpler and produces cleaner logs.)

Status: RESOLVED in updated `.AGENTS/PLAN_5.md`.

- PLAN_5 now specifies a deterministic preflight strategy:
    - attempt `gem fetch` first (authenticated source URL),
    - if found: compare SHA-256 and succeed only on match,
    - if not found: perform `gem push` to the explicit host/key,
    - otherwise: fail (do not treat auth/network errors as “already exists”).

## Tighten before implementing (recommended)

### A) Node idempotency must account for registry-specific tarballs

This repo’s pack workflow intentionally produces **two different tarballs**:

- `out/gpr.tgz` is packed after temporarily rewriting package name/scope for GitHub Packages.
- `out/npmjs.tgz` is the npmjs-compatible tarball.

Therefore, digest checks must be registry-specific:

- GPR: compare remote `dist.integrity` against the SRI computed from `out/gpr.tgz`.
- npmjs: compare remote `dist.integrity` against the SRI computed from `out/npmjs.tgz`.

PLAN_5 implies “one integrity check” per registry, but should **state this explicitly** to avoid accidentally comparing the wrong tarball.

### B) RubyGems.org idempotency: define exact selection rules

The RubyGems.org API explicitly includes the SHA256 digest in the versions endpoint:

- `GET https://rubygems.org/api/v1/versions/<gem>.json` → each version object includes `number`, `platform`, and `sha`.

PLAN_5 should tighten selection rules:

- select the entry where `number == <version>` and `platform == "ruby"` (unless the package is known to produce platform gems),
- if multiple entries match (e.g. platform variants), fail with diagnostics rather than guessing.

This avoids silent false matches when platform-specific gems are introduced later.

### C) Detector exit-code contract requires updating existing scripts materially

The current detectors (`eng/scripts/find_python_project_path.py`, `eng/scripts/find_node_project_path.py`) do **not** detect ambiguity; they select the shortest path and exit with code 1 for all failures.

PLAN_5 is correct to introduce a contract (0/2/3/1), but should call out:

- ambiguity must be detected and printed (list of all matches),
- “not found” must exit 3 (not 1),
- and “unexpected error” must exit 1.

Also, the resolver implementation must capture _both_ stdout and the exit code per detector without being short-circuited by `set -e`.

### D) Reproducibility baseline: apply before the build-producing command only

PLAN_5 mandates:

- `TZ=UTC`, `LC_ALL=C.UTF-8`, and `SOURCE_DATE_EPOCH` derived from target commit.

This is reasonable and consistent with `CLARIFY_PLAN_2`. The plan should specify that these must be set in the same step environment (or exported) **immediately before**:

- `uv build` (Python)
- `npm pack` (Node)
- `gem build` (Ruby)

Avoid implying the variables must be global for the entire job; keep the blast radius small.

### E) Publish jobs should avoid checkout (and should stay minimal)

Current `official.yml` publishes Node with a checkout primarily to run a “private package” check.

PLAN_5 already recommends moving that check into the pack workflow. Once done, Node publish jobs can (and should) avoid checkout:

- download artifacts
- authenticate
- publish
- verify

Apply the same discipline to Ruby publish jobs.

### F) Action pinning policy should be stated consistently

PLAN_5 mentions pinning actions, but the repo currently uses a mix:

- some actions are pinned to a SHA (e.g. `astral-sh/setup-uv@...`, `dcarbone/install-jq-action@...`),
- many GitHub-owned actions are referenced by major version (e.g. `actions/checkout@v6`, `actions/setup-node@v6`).

Before implementation, decide whether:

- “pin everything to SHA” is required, or
- “pin third-party actions to SHA; allow GitHub-owned actions by major version” is acceptable.

PLAN_5 should match the repo’s chosen policy.

### G) GitHub Release asset handling remains “clobbering” by design

The reusable workflow `release-create-github-release.yml` uploads assets with `gh release upload --clobber`.

That is fine for GitHub assets, but it is **not** the same as the registry idempotency contract.

PLAN_5 should explicitly scope the “never overwrite” policy to registries (PyPI/npm/RubyGems), not GitHub Release assets.

## Items requiring additional maintainer confirmation

None are strictly required beyond `CLARIFY_PLAN_4`.

Optional (future-proofing) confirmation to consider:

- Are any Ruby packages in this repo expected to publish platform-specific gems (non-`platform: ruby`)?
    - If yes, the RubyGems.org idempotency selection logic must be extended accordingly.

## Conclusion

PLAN_5 is a solid, policy-aligned blueprint.

To make it unambiguous and safer to implement, update the plan to:

1. specify the exact `gem push` command shape for GitHub Packages (`--host` + `--key`),
2. define a robust discriminator for the “already exists” path in GitHub Packages idempotency, and
3. tighten the “which tarball/digest” rules for Node and the platform-selection rules for RubyGems.org.

With these small clarifications, PLAN_5 should be safe to implement without reintroducing fallback auth and without violating the artifact-first/idempotent rerun contracts.
