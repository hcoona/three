# Review of `PLAN_3.md` (Release workflow refactor plan v3) — follow-up

<!-- markdownlint-disable MD044 -->

This review evaluates `.AGENTS/PLAN_3.md` against the repository constraints and implementation clarifications in:

- `.AGENTS/CLARIFY_0.md`
- `.AGENTS/CLARIFY_1.md`
- `.AGENTS/CLARIFY_2.md`
- `.AGENTS/CLARIFY_3.md`

It also cross-checks against the current root workflows:

- `.github/workflows/official.yml`
- `.github/workflows/buddy.yml`
- Existing reusable workflows:
    - `.github/workflows/release-prepare-release-notes.yml`
    - `.github/workflows/release-create-github-release.yml`

Scope reminder: root workflows only under `/.github/workflows/*.yml`.

## Executive summary

`PLAN_3.md` is directionally correct and aligns with the core policy constraints (artifact contract, trusted publishing identity, buddy non-clobber, dist-tag derivation). However, there are **two plan-vs-constraints mismatches** that should be made explicit in `PLAN_3.md` to avoid silent behavior drift when the work is implemented:

1. **Reusable workflow context differences under `workflow_call`** (confirmed in `CLARIFY_3.md`).
2. **Buddy Node (non-WXT) must run default quality checks**; the cleanest enforcement is calling `release-build-node-pack.yml` from buddy too (confirmed in `CLARIFY_3.md`).

Additionally, there is one **practical contract gap** that will otherwise cause awkward implementation choices:

- **How the Node pack workflow exposes tarball identity** (which `.tgz` is for npmjs vs GPR) once packing is moved out of the publish job.

Overall verdict: **Approved, but `PLAN_3.md` should be amended to incorporate the `CLARIFY_3.md` confirmations and to pin down Node tarball identification.**

## What the plan gets right (matches clarifications)

### 1) Stable artifact contract and flat layout

The plan correctly standardizes on:

- `${GITHUB_WORKSPACE}/out` as the only asset directory
- `out/*` as the complete release asset set
- flat layout (no subdirectories)

This matches `CLARIFY_0.md` and is compatible with `release-create-github-release.yml`, which errors if directories exist under `out/`.

### 2) Buddy non-clobber guard is mandatory (because release upload uses `--clobber`)

`release-create-github-release.yml` always uploads assets with `gh release upload ... --clobber` and also edits release metadata.

Therefore, the plan’s buddy preflight guard is not optional. `PLAN_3.md` correctly states:

- Query by tag using `gh api repos/${GITHUB_REPOSITORY}/releases/tags/${TAG_NAME}`.
- If the release exists and `prerelease=false`, buddy fails fast (regardless of `draft`).

This matches the decisions in `CLARIFY_1.md` and `CLARIFY_2.md`.

### 3) Dist-tag derivation must not parse SemVer

The plan’s dist-tag derivation via NBGV metadata (`PrereleaseVersionNoLeadingHyphen`) with normalization + regex validation matches `CLARIFY_1.md` and resolves current drift between `official.yml` and `buddy.yml`.

### 4) WXT zip collection must be shallow and name-agnostic

The plan’s WXT policy (“only `.output/*.zip`, no recursion, no filename assumptions, collision-safe copy into `out/`”) matches `CLARIFY_1.md` and `CLARIFY_2.md`.

### 5) Attestation placement: official yes, buddy no; build workflows never attest

The plan’s separation is correct:

- Build-only reusable workflows do **not** run GitHub attestations.
- `official.yml` runs a dedicated job to attest `out/*`.
- `buddy.yml` does not attest.

This matches `CLARIFY_1.md` and `CLARIFY_2.md`.

## Required plan amendments (high impact)

### A) `release-resolve.yml` cannot branch on `github.event_name` under `workflow_call`

`PLAN_3.md` currently describes `release-resolve.yml` as if it can distinguish:

- tag-triggered runs (`push`), vs
- manual runs (`workflow_dispatch`).

But `CLARIFY_3.md` confirms that when invoked via `workflow_call`, the called workflow sees `github.event_name == workflow_call`, so branching on `push`/`workflow_dispatch` inside `release-resolve.yml` will not behave like today.

**Plan update required:** incorporate the explicit input contract from `CLARIFY_3.md`:

- Add required `source: tag | manual` input.
- When `source=tag`, require `ref_name` (caller passes `${{ github.ref_name }}`).
- When `source=manual`, require `project` and `version`, optional `target`.
- Normalize `force_update_tag` as string output and force it to `"false"` when `source=tag`.

Without this, the refactor will either duplicate logic back into entry workflows (defeating the goal) or accidentally change semantics.

### B) `run_url` must point to the entry workflow run (not the called workflow run)

`release-prepare-release-notes.yml` uses `run_url` for placeholder notes when no changelog exists.

`CLARIFY_3.md` confirms that computing `run_url` inside `release-resolve.yml` can point at the _called_ workflow run, not the entry workflow run.

**Plan update required:** treat `run_url` as a caller-provided input computed in `official.yml`/`buddy.yml`, and pass through unchanged.

### C) Buddy Node (non-WXT) quality checks must be enforced structurally

The plan requires quality checks by default in both official and buddy. Today:

- `official.yml` runs checks in the Node publish job.
- `buddy.yml` (Node/GPR) currently does not run them.

`CLARIFY_3.md` confirms the preferred enforcement:

- Buddy should call `release-build-node-pack.yml` too (so checks are guaranteed), then publish from the packed `.tgz`.

**Plan update required:** update the “buddy Node (non-WXT)” section to either:

- explicitly call `release-build-node-pack.yml` from buddy (preferred), or
- explicitly add the same `pnpm --filter ... --if-present` checks into the buddy publish job.

As written, `PLAN_3.md` describes buddy Node publish as remaining “in `buddy.yml`”, which is easy to implement in a way that accidentally skips checks.

## Contract gap to clarify (practical implementation blocker)

### D) Node tarball identity when packing is extracted

Once Node packing is moved into `release-build-node-pack.yml`, the entry workflow needs a deterministic way to choose:

- which `.tgz` to publish to GitHub Packages (scoped name), and
- which `.tgz` to publish to npmjs.org (public name).

Right now, `official.yml` computes `gpr_tgz` and `npm_tgz` inside a single job, so this identity is local state.

**Plan update recommended:** define a stable convention, for example:

- Standardize the filenames in `out/` (e.g., rename to `npmjs.tgz` and `gpr.tgz`), or
- Have the pack workflow emit outputs `npmjs_tgz_name` / `gpr_tgz_name` that the publish job uses after downloading the artifact.

Without this, the publish job ends up guessing based on filename patterns (fragile) or re-packing (reintroduces duplication and breaks “publish from the packed `.tgz`”).

## Other notes (lower risk)

- Ensure every job that downloads artifacts has `actions: read` when token permissions are restricted (`CLARIFY_1.md` already calls this out).
- For dist-tag derivation, consider failing (not defaulting) if the workflow resolves a prerelease `version` but NBGV returns an empty prerelease metadata string; otherwise it is possible to incorrectly publish prereleases to `latest`.

## Conclusion

`PLAN_3.md` is solid and should achieve the stated goal (reduce duplication between `buddy.yml` and `official.yml`) with low behavior risk, **provided it explicitly incorporates the confirmed constraints from `CLARIFY_3.md` and clarifies the Node tarball identity contract.**
