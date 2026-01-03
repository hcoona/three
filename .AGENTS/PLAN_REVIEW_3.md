# Review of `PLAN_3.md` (Release workflow refactor plan v3)

<!-- markdownlint-disable MD044 -->

This review evaluates `PLAN_3.md` against:

- Repository constraints in `.AGENTS/CLARIFY_0.md`, `.AGENTS/CLARIFY_1.md`, `.AGENTS/CLARIFY_2.md`.
- Current root workflows:
    - `.github/workflows/official.yml`
    - `.github/workflows/buddy.yml`
    - Reusable workflows already in place (`release-prepare-release-notes.yml`, `release-create-github-release.yml`).

Scope reminder (per plan and clarifications): **root workflows only** under `/.github/workflows/*.yml`.

## Executive summary

`PLAN_3.md` is materially improved versus earlier iterations and is **directionally correct**. It directly addresses the known gaps from `PLAN_REVIEW_2.md` and aligns with the decided constraints:

- Stable artifact contract: `${GITHUB_WORKSPACE}/out` as the only asset directory; `out/*` as the full asset set; flat layout.
- Buddy non-clobber: a preflight guard using `gh api .../releases/tags/...`, failing fast when an existing release is `prerelease=false`.
- npm dist-tag: derived from NBGV metadata (`PrereleaseVersionNoLeadingHyphen`) with explicit normalization and validation.
- Buddy Node correctness: pack first, publish from the packed `.tgz`, upload the same `.tgz`.
- WXT artifacts: shallow collection from `.output/*.zip`, no filename assumptions, collision-safe copy into `out/`.
- Attestations: removed from build workflows; official attests release assets via a dedicated job; buddy does not attest.
- Node quality checks: stated as required by the plan.

Overall verdict: **Approved with a few high-impact implementation caveats** (see below). These caveats are not blockers, but they should be explicitly called out in the plan because they can otherwise introduce subtle behavior drift.

## What the plan gets right (and why it matches the clarifications)

### 1) Artifact contract and flat layout

The plan correctly hardens the contract to:

- `${GITHUB_WORKSPACE}/out` only
- `out/*` complete asset set
- no subdirectories

This matches `CLARIFY_0.md` (no `dist_dir` / `dist_glob`) and the constraints inside `release-create-github-release.yml` (it errors on directories under `out/`).

### 2) Buddy non-clobber guard

The plan’s rule is explicit and matches the decisions:

- Use `gh api repos/${GITHUB_REPOSITORY}/releases/tags/${TAG_NAME}`
- If exists and `prerelease=false` → **fail fast**, regardless of `draft`

This is necessary because `release-create-github-release.yml` always uploads assets using `--clobber` and will edit an existing release.

### 3) Dist-tag via NBGV metadata (no SemVer parsing)

The algorithm in `PLAN_3.md` is well-specified and matches `CLARIFY_1.md` / decisions in `CLARIFY_2.md`:

- source: `PrereleaseVersionNoLeadingHyphen`
- empty → `latest`
- else → `channel = prerelease.split('.', 1)[0].lower()`
- validate with `^[a-z0-9][a-z0-9-]*$` and fail fast if invalid

This removes current drift between `official.yml` and `buddy.yml` (both currently parse the version string, and they do it differently).

### 4) WXT ZIP collection rules

The plan’s WXT rules are consistent with WXT documentation:

- `outDir` default is `.output`
- zip filename templates are not `${PROJECT}.zip`

So collecting `.output/*.zip` without name filters is the correct policy.

### 5) Attestation placement

The plan correctly separates concerns:

- build workflows: build + upload artifacts only
- `official.yml`: dedicated job downloads `official-<project>-dist` and attests `out/*`
- `buddy.yml`: does not attest

This matches the “publishing identity constraints” and avoids pulling attestation into reusable build workflows.

## High-impact caveats / required clarifications to avoid behavior drift

These are the main items that should be made explicit to keep the refactor low-risk.

### A) Reusable workflow context differs from entry workflow context

When `release-resolve.yml` is invoked via `workflow_call`, it runs as its own workflow context. In practice:

- `github.event_name` will be `workflow_call` inside the called workflow.
- The called workflow’s `github.run_id` / `GITHUB_RUN_ID` can differ from the entry workflow run.

Implications:

1. **Tag-triggered vs manual logic**
    - Current `official.yml` resolve step branches on `GITHUB_EVENT_NAME == push`.
    - That check will not work as-is inside `release-resolve.yml` when called.

2. **`run_url` semantics**
    - Today `run_url` points at the _entry workflow run_ (`official.yml` / `buddy.yml`).
    - If `release-resolve.yml` computes `run_url` internally, it may point at the _called resolve workflow run_ instead.
    - That changes what appears in generated release notes placeholders.

Recommendation (and likely required plan update):

- Make `release-resolve.yml` accept explicit inputs to determine “mode” (tag-driven vs manual), and avoid relying on `GITHUB_EVENT_NAME`.
- Treat `run_url` as a caller-provided input (computed in the entry workflow) if you need it to continue pointing to the entry run.

This is significant enough that it should be called out in `PLAN_3.md` (and decided in `CLARIFY_3.md`).

### B) Node quality checks in buddy Node (non-WXT) path

`PLAN_3.md` requires that Node quality checks run by default in **both** official and buddy flows. The plan introduces `release-build-node-pack.yml` that runs checks, and wires it into the official flow.

However, the buddy Node (non-WXT) flow is described as “publish job remains in `buddy.yml`”. If buddy does not call `release-build-node-pack.yml`, then quality checks must be explicitly added to the buddy publish job.

Recommendation:

- Either call `release-build-node-pack.yml` from buddy as well (build/pack + checks), then publish from the produced `.tgz`.
- Or keep buddy publish in `buddy.yml`, but explicitly add the same quality checks (`pnpm --filter <project> --if-present ...`) before packing.

Without this, the plan’s acceptance criterion #8 can be missed.

### C) `actions/download-artifact@v4` permissions

The plan’s “dedicated publish / attest jobs” require downloading the `*-dist` artifact.

In restricted-permission repositories, `actions/download-artifact@v4` requires `actions: read`. The plan mentions this in the permissions model summary, which is good; it should be consistently applied in the implementation.

## Additional implementation suggestions (optional but helpful)

These are not required by the plan, but will reduce duplication and future drift.

1. Add a small shared bash helper (or composite action) for:
    - dist-tag derivation from NBGV metadata
    - validating the dist-tag

2. Add a shared setup action (optional) to reduce repetition:
    - dotnet tool restore
    - setup-python + uv
    - setup-node + pnpm
    - jq install (only where needed)

3. Add a minimal “contract assertions” step in each reusable build workflow:
    - ensure `${GITHUB_WORKSPACE}/out` exists
    - ensure `out/*` is flat (no directories)

## Coverage check vs `PLAN_3.md` acceptance criteria

`PLAN_3.md` acceptance criteria are complete and match the constraints.

Two extra “verification bullets” are recommended to prevent subtle drift:

- Release notes placeholder `RUN_URL` continues to reference the entry workflow run (not a called sub-workflow run), if that is the intended UX.
- `release-resolve.yml` can reliably distinguish “tag-derived project/version” vs “manual inputs” when invoked via `workflow_call`.

## Conclusion

`PLAN_3.md` is solid and implementable. The only items that deserve explicit additional text (or a small clarification addendum) are:

- reusable workflow context differences (event/run URL),
- ensuring buddy Node quality checks are not accidentally skipped.

If those are made explicit, the plan should achieve the stated goal: **significantly reduce duplication between `official.yml` and `buddy.yml` with minimal behavior risk**.
