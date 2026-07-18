# Celesphonia Modifier `.copilot` index

> **Warning**
> `research-tools/save-research-decoder.js` is a research-only tool.
> Run it only on immutable copies in the ignored `.private/` workspace.
> Its inputs and outputs can contain private save data and must never be committed.

This folder contains Copilot-assisted planning, remediation, and research artifacts for the
future Celesphonia Modifier project root.

No application project has been scaffolded here yet. There are no source files, project files,
solution entries, or package references under this root.

## Normative baseline

- `plans/project-operating-model.md` governs investment, sequencing, accountability, gates,
  staffing, scope cuts, supported life, and deferred decisions.
- `plans/save-semantic-atlas-plan.md` governs the comprehensive preliminary save survey,
  evidence model, privacy boundary, human value selection, and focused semantic research.
- `plans/atlas-v0-execution-plan.md` defines the C# implementation policy and measurable
  acceptance criteria for each Atlas v0 increment.
- `plans/atlas-v0-a0-research-contract.md` records the approved finite corpus, definition,
  privacy, redaction, Agent-egress, artifact-lifecycle, and handoff contract for increment A0.
  Human scope approval alone does not establish increment completion.
- `plans/atlas-v0-a1-foundation-plan.md` defines the exact three-project C# scaffold,
  deterministic empty-survey contract, command behavior, tests, validation, and release gate for
  increment A1.
- `plans/celesphonia-modifier-plan.md` is the detailed product, UX, transaction, recovery,
  test, and packaging hypothesis set. The operating and Atlas plans govern where they conflict
  with its older progression assumptions.
- `plans/project-perspective-map.md` is the normative first-level decision-perspective
  taxonomy used before workstream design.

## Current gate evidence

- `reviews/atlas-v0-a0-scope-review.md` is the repository-safe record of the approved Atlas V0 A0
  project-leader decision.
- `reviews/atlas-v0-a0-release-gate.md` is created only after an exact committed A0 candidate
  receives an independent `No findings` result. That record establishes A0 completion.
- `reviews/atlas-v0-a1-plan-review.md` is created only after the exact A1 plan receives an
  independent `No findings` result. That record authorizes A1 implementation.
- `reviews/atlas-v0-a1-release-gate.md` is created only after an exact committed A1 implementation
  receives an independent `No findings` result. That record establishes A1 completion.

## Historical and supporting artifacts

- `reviews/engineering-plan-remediation.md`
- `reviews/project-perspective-map-review.md`
- `reviews/transaction-retirement-protocol.md`
- `reviews/transaction-state-validation.md`
- `reviews/winui-context-mode-remediation.md`

These review documents are supporting remediation or adjudication artifacts. Treat them as
historical or supporting analysis unless and until their guidance is adopted into the main
plan or normative taxonomy.

`reviews/project-perspective-map-review.md` is the review/adjudication record for the
normative perspective taxonomy.

- `research/game-and-save-format-summary.md` is a non-sensitive supporting summary derived from
  the main plan.
- `research-tools/save-research-decoder.js` is a research-only source copy for private analysis.
  Use only immutable copies in the ignored `.private/` workspace, never the live save directory,
  and do not commit generated outputs.

## Document lifecycle

Use lifecycle markings before moving files:

- **Active normative:** Governs the decisions in its declared scope.
- **Active subordinate:** Remains applicable, but a newer normative document controls
  progression or resolves conflicts.
- **Partially superseded:** Keeps valid content in place and names the exact replacement and
  superseded scope in a prominent banner.
- **Historical supporting:** Preserves review, remediation, or adjudication provenance but does
  not govern current work.
- **Archived:** Has no remaining operative content.

Do not move a partially superseded document into `archive/`; doing so would hide still-valid
requirements and break stable references. If a document becomes fully superseded, move it under
`archive/`, retain its original title and date, and add a top-level banner linking to its
replacement. Never archive merely because a document is old.

## Privacy exclusions

This repository copy intentionally excludes live or historical saves, decoded save JSON,
private evidence payloads, private input provenance, database indexes, Steam cloud metadata,
raw game files, extracted game data, save hashes or private fingerprints, account metadata,
and real save values.

Commit only schema-validated, redacted Atlas records and safe generated views. Keep source
research inputs, temporary decoded outputs, and retained E2/E3 evidence in protected working
storage excluded from repository history. See `plans/save-semantic-atlas-plan.md` for the
authoritative research and evidence boundary.

The local `src/private/app/celesphonia-modifier/.private/` workspace is inside the checkout for
handoff convenience but is fully Git-ignored and remains private working storage. Its contents
are never repository artifacts.
