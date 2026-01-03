# Additional clarifications for implementing `PLAN_3.md` (CLARIFY_3)

<!-- markdownlint-disable MD013 -->

This file captures implementation-level clarifications discovered while reviewing `PLAN_3.md` against the current GitHub Actions behavior of reusable workflows (`workflow_call`).

Status: **Confirmed** (2026-01-02)

Scope reminder: applies only to root workflows under `/.github/workflows/*.yml`.

## 1) `release-resolve.yml`: how to distinguish tag-triggered vs manual runs under `workflow_call`

### Problem

`PLAN_3.md` describes `release-resolve.yml` as handling:

- tag-triggered runs (`push` on `release/*/v*`)
- manual runs (`workflow_dispatch`)

However, when `release-resolve.yml` is invoked via `workflow_call`, the called workflow’s `github.event_name` is `workflow_call`, not `push` or `workflow_dispatch`. This means logic like:

- `if [[ "${GITHUB_EVENT_NAME}" == "push" ]] ...`

will not behave as it does in the entry workflows today.

### Decision

`release-resolve.yml` must not rely on `GITHUB_EVENT_NAME`/`github.event_name` to detect its mode.

Instead, standardize on an explicit input contract:

- `source`: required string enum, one of:
    - `tag` (meaning: derive `project` and `version` from the release tag)
    - `manual` (meaning: `project` and `version` are provided as inputs)

Recommended additional inputs:

- `ref_name`: required string when `source=tag` (caller passes `${{ github.ref_name }}`)
- `project`: required string when `source=manual`
- `version`: required string when `source=manual`
- `target`: optional string when `source=manual` (defaults to HEAD)
- `force_update_tag`: optional boolean when `source=manual`; ignored when `source=tag` and output must be normalized to `"false"`.

Rationale:

- Keeps the resolve logic single-sourced without duplicating tag parsing in the entry workflow.
- Removes ambiguity stemming from reusable workflow contexts.

## 2) `run_url`: which workflow run should release notes link to?

### Problem

`release-prepare-release-notes.yml` uses `run_url` to populate placeholder release notes when no `CHANGELOG.md` exists.

In today’s implementation, `run_url` points to the entry workflow run (official/buddy). If `release-resolve.yml` computes `run_url` internally as:

- `https://github.com/${GITHUB_REPOSITORY}/actions/runs/${GITHUB_RUN_ID}`

then under `workflow_call` it may point to the called resolve workflow run instead.

### Decision

Preserve current UX: `run_url` should refer to the **entry workflow run**.

Implementation choice:

- Compute `run_url` in `official.yml` / `buddy.yml` and pass it directly to `release-prepare-release-notes.yml`.
- If `release-resolve.yml` still needs to output `run_url`, then `run_url` should be a required `workflow_call` input that is passed through unchanged.

Rationale:

- Avoids unintentionally changing the “Build:” link users see in placeholder notes.

## 3) Buddy Node (non-WXT) quality checks placement

### Problem

`PLAN_3.md` requires Node quality checks by default in both flows. The official plan naturally enforces this via `release-build-node-pack.yml`.

Buddy keeps a “publish job in `buddy.yml`” for Node (non-WXT), so checks can be missed unless explicitly inserted.

### Decision

Buddy Node (non-WXT) must run the same default quality checks prior to packing/publishing:

- `pnpm --filter <project> --if-present lint`
- `pnpm --filter <project> --if-present typecheck`
- `pnpm --filter <project> --if-present test`
- `pnpm --filter <project> --if-present build`

Implementation choice (confirmed):

- Call `release-build-node-pack.yml` from buddy too (preferred), then publish from the produced `.tgz`.

Rationale:

- Enforces acceptance criterion #8 without depending on which workflow owns the pack step.

## Decision log

| Item                   | Confirmed decision                                                                            |
| ---------------------- | --------------------------------------------------------------------------------------------- | ----------------------------------------------- |
| Resolve mode detection | Use explicit `workflow_call` inputs (`source=tag                                              | manual`); do not branch on `github.event_name`. |
| `run_url` target       | Must point to the entry workflow run; compute in `official.yml`/`buddy.yml` and pass through. |
| Buddy Node checks      | Buddy calls `release-build-node-pack.yml` (option A).                                         |
