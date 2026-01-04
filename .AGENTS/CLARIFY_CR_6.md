# Clarifications requested by Code Review (CR_6)

Status: NEEDS HUMAN CONFIRMATION
Date: 2026-01-04
Scope: root workflows under `/.github/workflows/*.yml`

This file captures decision points discovered during code review of the current `.github` diff (origin/main...HEAD) that are not explicitly confirmed by:

- `.AGENTS/CLARIFY_0.md` – `.AGENTS/CLARIFY_4.md`
- `.AGENTS/CLARIFY_CR_0.md`
- `.AGENTS/CLARIFY_CR_1.md`
- `.AGENTS/CLARIFY_CR_2_5.md`

---

## 1) Python version strings: do we support full PEP 440 (including epochs)?

Context:

- `release-resolve.yml` advertises Python versions as “PEP 440 (leading v allowed)”.
- The workflow then applies a shell-safety regex `^[A-Za-z0-9][A-Za-z0-9._+-]*$` before calling `validate_pep440_version.py`.
- The validator uses `packaging.version.Version`, which supports valid PEP 440 constructs such as epochs (`1!1.0`).

Question:

- Should the workflows accept any valid PEP 440 version string (as validated by `packaging.version.Version`), including epochs?

Please confirm one of:

- A) Yes. Accept full PEP 440, including epochs (`!`).
    - Implementation direction: remove the pre-validation regex for Python, or extend it to allow `!` when `project_kind == 'python'`.
- B) No. Intentionally disallow epochs (and possibly other PEP 440 constructs) even if they are valid PEP 440.
    - Implementation direction: keep (or tighten) the pre-validation regex and update user-facing descriptions to match the restricted subset.

Recommendation: A. The current docs say “PEP 440”, and the Python validator already enforces correctness safely.

Decision: B (confirmed by human)
