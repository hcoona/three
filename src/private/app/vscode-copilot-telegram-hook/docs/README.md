# Documentation Provenance and Derivation Map

This file records which documents in this folder are direct human-authored
inputs and which documents are later derived artifacts.

Use this file as the first stop when you need to answer questions such as:

- Which statements came directly from the user?
- Which documents were derived later from those statements?
- Which repository document should be treated as a provenance ledger instead of
  a derived specification?

## Scope of this file

- This file is the provenance ledger for the documentation set under `docs/`.
- Human-authored source inputs are preserved here, even when the original chat
  exchange is not itself stored in the repository.
- Derived documents should be listed here together with their upstream inputs.

## Human-authored source inputs

The items in this section are treated as direct human inputs and are preserved
as standalone H-series source documents.

| ID    | Document                                                                                               | Summary                                                                       | Notes                                                                                                                                  |
| ----- | ------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| H-001 | [`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md)                         | Preserves the original requirement brief and the user-provided reference set. | This is the upstream source for the external references later used by research documents.                                              |
| H-002 | [`h-002-human-confirmation-2026-03-13.md`](./h-002-human-confirmation-2026-03-13.md)                   | Preserves the later confirmed product decisions from chat.                    | This clarifies and, where necessary, supersedes parts of H-001 for later derived documents.                                            |
| H-003 | [`h-003-human-confirmation-2026-03-13-addendum.md`](./h-003-human-confirmation-2026-03-13-addendum.md) | Preserves the follow-up clarification round from chat.                        | This tightens product assumptions around hook-locality interpretation, secret storage, failure handling, and overlength notifications. |

## Reference derivation rule

Research documents in this folder should be grounded in the user-provided
references recorded in H-001.

Human confirmation in H-002 and H-003 may refine the interpretation, scope,
and design inputs for those research documents, but the external research
claims should be traced back to the reference set listed in H-001 rather than
reconstructed from derived repository documents.

## Derived repository documents

The items in this section are derived from the human-authored inputs above,
from official external documentation, or from both.

| ID    | Document                                                                                   | Kind                                          | Derived from                                                                                                | Notes                                                                                                                                                                                    |
| ----- | ------------------------------------------------------------------------------------------ | --------------------------------------------- | ----------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| D-001 | [`functional-requirements.md`](./functional-requirements.md)                               | Derived functional specification              | H-001, H-002, H-003, D-002, and D-003                                                                       | Defines product-facing functional requirements after interpretation and normalization.                                                                                                   |
| D-002 | [`nonfunctional-and-constraints-research.md`](./nonfunctional-and-constraints-research.md) | Derived research and supporting specification | H-001 reference set, H-002, and H-003                                                                       | Grounds research claims in the user-provided references from H-001 while recording later confirmed product decisions from H-002 and H-003.                                               |
| D-003 | [`vscode-hook-inputs-research.md`](./vscode-hook-inputs-research.md)                       | Derived technical research note               | H-001 reference set, H-003, and non-normative repository context                                            | Explains hook input capabilities and the summary-correlation boundary using the VS Code references provided in H-001, including the later clarification on hook-locality interpretation. |
| D-004 | [`../README.md`](../README.md)                                                             | Derived project overview                      | H-001, H-002, H-003, D-001, D-002, and D-005                                                                | Convenient project entry point, but not the primary provenance ledger.                                                                                                                   |
| D-005 | [`implementation-language-evaluation.md`](./implementation-language-evaluation.md)         | Derived implementation design research note   | H-001, H-002, H-003, D-002, D-003, and official docs reviewed during the implementation-language evaluation | Compares PowerShell, Python, and C# (including native AOT) for the supported product scope without turning language choice into a product requirement.                                   |

## Current derivation chain

The current documentation flow is:

1. H-001 establishes the original project brief and records the user-provided
   external reference set.
2. H-002 records later human confirmation that clarifies and, where necessary,
   supersedes parts of H-001.
3. H-003 records a follow-up clarification round that further tightens the
   product boundary, failure expectations, and overlength-message treatment.
4. D-002 and D-003 research the H-001 reference set, with D-002 and D-003 also
   incorporating the later confirmed decisions from H-002 and H-003 where
   needed.
5. D-001 derives the functional specification from H-001, H-002, H-003, and
   the research outputs.
6. D-005 records the later implementation-language evaluation using the H-series
   inputs, the research outputs, and the official documentation reviewed during
   that evaluation.
7. D-004 synchronizes the repository overview with the current derived
   specification set.
8. Future design and architecture documents should cite this file together with
   the specific H-series and D-series documents they derive from.

## Maintenance rules

To keep the traceability chain useful:

1. Add every new direct human-authored requirement or confirmation here first.
2. Do not silently rewrite older human-authored sections to match later derived
   documents. Add a new H-series entry instead.
3. Add every new derived document to the table above with its upstream sources.
4. Prefer explicit statements such as "derived from H-001 and H-002" over vague
   wording such as "based on previous discussion".
5. When a repository overview file is synchronized with derived documents, keep
   using this file as the provenance ledger rather than treating the overview as
   the authoritative source of human input.
