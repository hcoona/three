# Review of `PLAN_5.md` (Release workflow refactor plan v5)

<!-- markdownlint-disable MD013 -->

This review evaluates `.AGENTS/PLAN_5.md` against the confirmed constraints in:

- `.AGENTS/CLARIFY_0.md`
- `.AGENTS/CLARIFY_1.md`
- `.AGENTS/CLARIFY_2.md`
- `.AGENTS/CLARIFY_3.md`
- `.AGENTS/CLARIFY_4.md`

It also cross-checks the current root workflows:

- `.github/workflows/official.yml`
- `.github/workflows/buddy.yml`

Scope reminder: **root workflows only** under `/.github/workflows/*.yml`.

## Executive summary

`PLAN_5.md` is **approved**.

It is a meaningful improvement over the current state of `buddy.yml` and `official.yml` because it:

- removes the biggest duplicated blocks (resolve + build/pack logic),
- makes the release artifact contract explicit and enforced (`${GITHUB_WORKSPACE}/out` + `out/*` + flat layout),
- preserves Trusted Publishing identity constraints by keeping publish steps in `official.yml`,
- hardens buddy safety with a mandatory non-clobber preflight guard,
- unifies npm dist-tag derivation using NBGV metadata (no SemVer parsing drift).

The plan is also consistent with the “workflow_call context differences” constraints and explicitly fixes the gaps called out in `.AGENTS/PLAN_REVIEW_4.md`.

## What the plan gets right (matches constraints)

### 1) Scope and policy layering are correct

- The scope is correctly limited to root workflows only.
- Entry workflows (`official.yml` / `buddy.yml`) remain the policy layer (environments, trusted publishing identity, prerelease behavior).
- New reusable workflows are build/resolve building blocks only.

This is aligned with `CLARIFY_0.md` (tool versions source of truth) and the identity constraints in `CLARIFY_0.md`.

### 2) Artifact contract matches the existing release workflow

`release-create-github-release.yml` enforces a flat `out/` layout and errors on directories.

`PLAN_5.md` correctly standardizes:

- `${GITHUB_WORKSPACE}/out` as the only release asset directory
- `out/*` as the complete payload
- flat layout only

This eliminates today’s ambiguous `dist_dir` / `dist_glob` output usage and makes it harder to accidentally ship a nested directory.

### 3) Buddy non-clobber is placed correctly and uses the right API

The plan’s buddy guard:

- runs before any release modification steps,
- queries `repos/${GITHUB_REPOSITORY}/releases/tags/${TAG_NAME}` (the agreed contract),
- fails fast when `prerelease=false` regardless of `draft`.

This is mandatory given `release-create-github-release.yml` uses `gh release upload --clobber` and edits release metadata.

### 4) Dist-tag derivation is consistent and no longer parses SemVer

The plan’s algorithm based on NBGV `PrereleaseVersionNoLeadingHyphen`:

- is identical for buddy and official,
- avoids SemVer parsing drift,
- includes the important “version is prerelease but prerelease metadata is empty → fail fast” guard.

This resolves the current mismatch:

- `official.yml` derives dist-tag from SemVer-ish parsing,
- `buddy.yml` derives dist-tag from a simpler `VERSION` split.

### 5) WXT zip handling fixes current behavior drift

Current root workflows collect WXT artifacts with:

- recursion under `.output/**` (depth-limited but still recursive), and
- project-name-based filtering assumptions.

`PLAN_5.md` matches `CLARIFY_1.md`:

- only `.output/*.zip` (shallow, no recursion)
- copy all zips
- fail on basename collisions with actionable diagnostics

This is a safer and more stable contract.

### 6) Attestation placement is aligned with constraints

The plan correctly:

- removes attestation from build-only workflows,
- keeps buddy without attestations,
- introduces a dedicated official attestation job that attests `out/*` after downloading the build artifact.

This follows the policy in `CLARIFY_1.md` and prevents accidental attestation behavior drift when reusing build workflows.

## Implementation notes / minor plan improvements (non-blocking)

These are not contradictions, but tightening them in the implementation (or adding a small note to the plan) will reduce risk.

### A) Ensure target commit resolution is robust in `release-resolve.yml`

`release-resolve.yml` will need to resolve `target` for both:

- `source=tag` (from `ref` / `ref_name`), and
- `source=manual` (from the optional `target` input).

Recommendation:

- In the resolve workflow, explicitly `git fetch --force --tags` (and, if needed, `git fetch --force --prune --all`) before resolving `target`.

Assessment: **true positive**.

Fix applied:

- Updated `.github/workflows/release-resolve.yml` to run:
    - `git fetch --force --tags`
    - `git fetch --force --prune --all`
      before resolving/checkout of the target commit.

Rationale:

- `actions/checkout` with `fetch-depth: 0` is usually sufficient, but resolution can still fail for targets not reachable from the initially fetched ref. Being explicit avoids hard-to-debug edge cases.

### B) Node pack reproducibility: keep the “prepack + npm pack --ignore-scripts” shape

Current `official.yml` does:

- `pnpm --filter <project> run --if-present prepack`
- `npm pack --ignore-scripts`

Recommendation:

- In `release-build-node-pack.yml`, keep the same convention to avoid behavior drift (and avoid executing arbitrary lifecycle scripts during packing).

Assessment: **false positive**.

Rationale:

- `.github/workflows/release-build-node-pack.yml` already runs `pnpm --filter <project> run --if-present prepack` prior to packing.
- It packs with `npm pack --ignore-scripts` (and uses `--pack-destination` to place tarballs into `${GITHUB_WORKSPACE}/out`).

If the plan intends a different behavior, call it out explicitly as a conscious change (but that would be out of scope per `PLAN_5.md`).

### C) Keep `run_url` strictly computed in entry workflows

The plan already states this correctly.

Implementation reminder:

- Do not let `release-resolve.yml` compute `run_url` internally; it should be passed through unchanged.

Assessment: **false positive**.

Rationale:

- Both entry workflows compute `run_url` as the entry workflow run URL and pass it as an input.
- `.github/workflows/release-resolve.yml` treats `run_url` as a strict pass-through (it writes `run_url=${RUN_URL}` to outputs) and does not derive it internally.

This avoids “called workflow run URL” regressions under `workflow_call`.

## Conclusion

`PLAN_5.md` is ready to implement.

It achieves the stated goal (reduce duplication between `buddy.yml` and `official.yml`) while preserving all confirmed constraints:

- stable `out/*` artifact contract,
- trusted publishing identity stays in `official.yml` with environments,
- buddy non-clobber is enforced early and unconditionally for `prerelease=false` releases,
- npm dist-tag derivation is unified and based on NBGV metadata,
- build workflows remain build-only and do not attest.
