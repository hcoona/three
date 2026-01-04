# Clarifications requested by Code Review (CR_2_5)

Status: CONFIRMED
Date: 2026-01-04
Scope: root workflows under `/.github/workflows/*.yml`

This file captures decision points discovered during follow-up review that are **not** already decided by:

- `.AGENTS/CLARIFY_0.md` – `.AGENTS/CLARIFY_4.md`
- `.AGENTS/CLARIFY_CR_0.md`
- `.AGENTS/CLARIFY_CR_1.md`

---

## 1) Buddy non-clobber guard: should it gate build/pack/publish jobs?

Context:

- `buddy.yml` implements the required non-clobber guard (`guard-non-clobber`) to prevent modifying an existing **non-prerelease** GitHub Release.
- Today, `guard-non-clobber` is only in the `needs` chain of the final `release-*` jobs.
- The build/pack jobs (`build-python`, `pack-node`, `build-wxt`) and the buddy GPR publish job (`publish-node-gpr`) do **not** depend on the guard.

Question:

- If the guard would fail (i.e., a protected official release already exists), should the workflow fail **before** doing any build/pack/publish work?

Please confirm one of:

- A) Yes. `guard-non-clobber` must be an early gate. Add it to `needs` for build/pack/publish jobs so buddy fails fast and avoids wasted CI and side effects (e.g., publishing to GPR).
- B) No. It is acceptable to build/pack/publish first and only block GitHub Release creation.

Recommendation: A (safer and cheaper). If a run is doomed, it should fail before publishing any artifacts to registries.

Decision: A (confirmed).

---

## No other new clarifications identified

All other reviewed behaviors appear to be already covered by existing clarified decisions (including WXT browser explicitness, WXT quality checks, official attestation gating, action major versions, and dist-tag derivation/validation).
