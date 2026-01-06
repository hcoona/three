<!-- markdownlint-disable MD013 MD024 MD029 -->

# PLAN_REVIEW_6_4: Strict review of `.AGENTS/PLAN_6.md`

Date: 2026-01-06

This is an independent, strict review of `.AGENTS/PLAN_6.md`.

Constraints respected:

- I did **not** consult any `.AGENTS/PLAN_REVIEW_*.md` documents.
- I **did** consider maintainer-confirmed decisions recorded in `.AGENTS/CLARIFY_PLAN_*.md` as authoritative.

Maintainer follow-ups recorded (2026-01-06):

- We will not update `PLAN_6.md` in this step; plan edits will be applied later in one consolidated update.
- For the resolver’s unified discovery script, `fd` should be installed via `mise` (not ad-hoc apt installs).
- The clarifications requested in this review for (resolver wiring), (Ruby attestation specifics), and (editorial self-consistency) are accepted.

## Executive summary

`PLAN_6` is close to implementable and is generally well-structured (clear goals, non-goals, hard requirements, acceptance criteria, and incremental sequence). It also correctly emphasizes the repo’s “artifact-first” release architecture and the key security constraint: **RubyGems.org publishing must use Trusted Publishing (OIDC) only, with no fallback**.

However, there are a few **must-fix** issues before implementation starts, plus several areas where the plan should be tightened to avoid brittle CI behavior or semantic drift.

Note: token leakage hardening is explicitly treated as out of scope for `PLAN_6` (see below), so it should not be interpreted as a must-fix item for this plan.

If those items are addressed, the plan should be safe to implement in a single PR or (preferably) split into 2–3 smaller PRs.

## Hard requirements check (must stay true)

### RubyGems.org publishing (Trusted Publishing only; no fallback)

The plan explicitly:

- Requires Trusted Publishing (OIDC) only.
- Forbids long-lived RubyGems API keys and forbids `api-token` / `role-to-assume`.
- States that satisfying the requirement is achieved by _not wiring fallback secrets/inputs_ (no additional runtime “self-detection” guard).

This matches the maintainer-confirmed policy in `CLARIFY_PLAN_0`, `CLARIFY_PLAN_3`, and `CLARIFY_PLAN_5_4`.

### GitHub Packages RubyGems publishing (`github.token` only; no PAT fallback)

The plan requires `${{ github.token }}` only (no PAT) and includes the correct operational caveat that package linkage / “Actions access” must be configured.

This matches `CLARIFY_PLAN_0`, `CLARIFY_PLAN_1`, and GitHub’s documentation for the RubyGems registry.

### Buddy safety

The plan introduces `is_prerelease` and enforces prerelease-only for buddy. This is currently missing in `.github/workflows/buddy.yml` and is a necessary fix.

This matches `CLARIFY_PLAN_1`.

### Idempotent reruns

The plan enforces a consistent contract across registries: version-exists is OK only if the remote digest matches; otherwise fail. This matches `CLARIFY_PLAN_4`.

### New third-party Actions pinned by full commit SHA

The plan states this explicitly and cites the relevant clarification (`CLARIFY_PLAN_5_3`).

## Must-fix issues (blocking)

### 1) Ruby build workflow: align “no Gemfile → skip tests” behavior with `CLARIFY_PLAN_0`

`PLAN_6` currently describes the Ruby build workflow as always running Bundler-based checks and treating missing Bundler context as a hard failure.

This should be changed to match the confirmed maintainer policy in `CLARIFY_PLAN_0`:

- If a `Gemfile` (Bundler context) is present: run `bundle exec standardrb` and `bundle exec rspec` (required).
- If there is no Bundler context: skip these checks and emit a clear log message explaining that the checks were skipped.

Recommended update to the plan text:

- Add an explicit “Bundler context detection” step.
- Keep `gem build` as the required artifact-producing step regardless of whether tests were skipped.

### 2) `gem build --output` support

Maintainers have confirmed that `gem build --output ...` is available.

This means the plan’s approach of producing a single versioned artifact directly at `out/<project>-<version>.gem` is acceptable.

Implementation note (optional but recommended to reduce CI surprises): document (or enforce) the minimum RubyGems version on the runner that is known to support `--output`.

### 3) Resolver redesign: good direction, but needs concrete runner prerequisites and failure-mode mapping

Moving from “Python else Node” to a unified discovery script is the right safety move.

What needs to be more explicit in `PLAN_6` is the operational wiring:

- **Runner prerequisite:** the plan should standardize on installing `fd` via `mise` (consistent with the monorepo toolchain policy). The plan should spell out:
    - how the workflow installs/activates `fd` using `mise` on the runner, and
    - whether the binary name is `fd` or `fdfind` in the chosen setup, and
    - how the discovery script chooses which binary to invoke.
- **Exit-code contract propagation:** the plan’s contract (0 success / 2 ambiguous / 3 not found / 1 unexpected) is good, but it must be preserved end-to-end. The reusable workflow should:
    - capture detector stderr into a temp file,
    - branch on the exit code,
    - print the diagnostics on ambiguity/not-found,
    - fail with a message that clearly distinguishes “ambiguous project” from “unknown project”.
- **Diagnostics quality:** ambiguity errors should list all matching candidates (paths + kind) so maintainers can resolve the ambiguity intentionally (rename, adjust package metadata, or remove duplicate manifests).

Why this matters:

- The current resolver code path treats “anything failed” as “unknown project”, which will become actively misleading once Ruby is added and cross-kind ambiguity becomes more likely.
- Keeping a crisp ambiguity/not-found distinction is one of the main safety wins of adding the unified discovery mechanism.

### 4) Ruby attestation is referenced but not specified

The plan wires `attest-ruby` in both official and buddy flows, but does not specify:

- Which attestation action is used (likely `actions/attest-build-provenance@v3`, matching Python/Node).
- The `subject-path` to attest (probably `out/*`).
- Required permissions (`attestations: write`, `id-token: write`, etc.).

Without these details, implementers may create inconsistent attestation behavior across languages.

## High-risk / correctness-sensitive details (should be tightened)

### 1) Token leakage hardening (out of scope for this plan)

`PLAN_6` uses authenticated `gem fetch` (source URLs embedding credentials) for GitHub Packages idempotency.

Hardening this against accidental token exposure in logs is a real concern, but per maintainer direction, it is **out of scope** for `PLAN_6`.

Recommendation: track token masking / logging hardening as a separate follow-up item outside this plan.

### 2) GitHub Packages docs are internally confusing about PAT vs `GITHUB_TOKEN`

GitHub’s RubyGems registry docs contain both:

- A general note that “GitHub Packages only supports authentication using a personal access token (classic)”, and
- A specific statement that `GITHUB_TOKEN` can publish packages associated with the workflow repository.

The plan correctly chooses the “`github.token` only; no fallback secret” policy.

To reduce maintainer confusion, the plan should explicitly state:

- The workflow will work only when the package is linked / accessible to this repository’s workflows.
- If publishing fails due to package access, the job must fail with a targeted message pointing to the “Actions access / package linkage” requirement.

### 3) RubyGems.org idempotency: endpoint choice is good, but checksum meaning should be stated

`PLAN_6` uses the RubyGems.org API v2 endpoint:

- `GET /api/v2/rubygems/<gem>/versions/<version>.json?platform=ruby`

This endpoint exists and returns:

- `sha` (64-hex)
- `spec_sha`

The plan should explicitly document that `sha` is treated as the SHA-256 of the `.gem` file bytes (and that `spec_sha` is not the artifact digest), to avoid future implementers “optimizing” the comparison incorrectly.

### 4) Scope creep risk: Ruby support is bundled with Node + PyPI idempotency redesign

The plan’s core goal (Ruby gem release support with Trusted Publishing only) is clear, but `PLAN_6` also adds substantial work:

- Node idempotency across npmjs + GPR (official) and GPR (buddy)
- Python idempotency with partial publish handling

This is logically consistent with “idempotent reruns apply to all publishes”, but it increases delivery risk.

Recommendation:

- Split into at least two PRs:
    1. Resolver hardening + `is_prerelease` output + buddy prerelease guard (no Ruby yet).
    2. Ruby build + GPR publish + RubyGems.org publish (Trusted Publishing only) + Ruby attestation.
    3. Node/Python idempotency refinements.

This still keeps the plan’s architecture but reduces blast radius.

## Smaller nits / editorial improvements

- `PLAN_6` declares it is “regenerated from `.AGENTS/PLAN_REVIEW_5*.md`”. That provenance note is fine, but the plan should remain self-contained and not rely on readers having access to prior reviews.
- The maintainer checklist is good; consider adding a single “failure playbook” section for the most common misconfigurations:
    - RubyGems Trusted Publisher missing/mismatched environment name
    - GitHub Packages RubyGems package not linked / Actions access missing
    - npm Trusted Publisher missing (already noted in current `official.yml`)

## Conclusion

`PLAN_6` correctly encodes the core security requirement (“RubyGems.org Trusted Publishing only; no fallback”) and generally matches the maintainer-confirmed clarifications.

Before implementation, the plan should be updated to:

1. Align the Ruby build job’s Bundler/test behavior with `CLARIFY_PLAN_0`:
    - run `bundle exec standardrb` / `bundle exec rspec` when a `Gemfile` is present, and
    - skip these checks (with a clear log message) when there is no Bundler context.

2. Make the resolver redesign fully actionable by specifying:
    - `fd` installation via `mise`,
    - `fd` vs `fdfind` binary selection, and
    - strict propagation of the exit-code contract (0/2/3/1) with clear diagnostics.

3. Specify Ruby attestation steps and required permissions.

Per maintainer direction, token leakage hardening is not a requirement for this plan.

After these adjustments, the plan is implementable and should fit the repo’s existing “artifact-first” workflow design.
