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

## Authoritative external documentation and cache policy

Repository source code is authoritative for repository behavior.

Research documents in this folder are condensed local caches of external
authoritative documentation. They reduce repeated upstream lookups, but they do
not replace the upstream sources or repository source code as the ultimate
authority.

The current authoritative external references for this documentation set are:

- [VS Code custom instructions documentation](https://code.visualstudio.com/docs/copilot/customization/custom-instructions)
- [VS Code hooks documentation](https://code.visualstudio.com/docs/agent-customization/hooks)
- [Telegram Bot API](https://core.telegram.org/bots/api)

Cache refresh behavior follows two rules:

1. **Read-through refresh**: when a needed fact is not already cached locally,
   consult the authoritative upstream source and update the relevant research
   document.
2. **Explicit verification refresh**: when a user explicitly requests
   verification or a refresh, re-check the authoritative upstream source and
   correct the local cache as needed.

## Human-authored source inputs

The items in this section are treated as direct human inputs and are preserved
as standalone H-series source documents.

| ID    | Document                                                                                                                                                 | Summary                                                                            | Notes                                                                                                                                                                                                                                                   |
| ----- | -------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| H-001 | [`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md)                                                                           | Preserves the original requirement brief and the user-provided reference set.      | This is the upstream source for the external references later used by research documents.                                                                                                                                                               |
| H-002 | [`h-002-human-confirmation-2026-03-13.md`](./h-002-human-confirmation-2026-03-13.md)                                                                     | Preserves the later confirmed product decisions from chat.                         | This clarifies and, where necessary, supersedes parts of H-001 for later derived documents.                                                                                                                                                             |
| H-003 | [`h-003-human-confirmation-2026-03-13-addendum.md`](./h-003-human-confirmation-2026-03-13-addendum.md)                                                   | Preserves the follow-up clarification round from chat.                             | This tightens product assumptions around hook-locality interpretation, secret storage, failure handling, and overlength notifications.                                                                                                                  |
| H-004 | [`h-004-human-confirmation-2026-03-14.md`](./h-004-human-confirmation-2026-03-14.md)                                                                     | Preserves the post-review clarification round from chat.                           | This relaxes the Chinese-summary requirement to best-effort and distinguishes persisted secret storage from explicit runtime overrides.                                                                                                                 |
| H-005 | [`h-005-human-verification-2026-03-14-hook-input-field-names.md`](./h-005-human-verification-2026-03-14-hook-input-field-names.md)                       | Preserves the manual verification of current hook input field names.               | This records that the current observed runtime uses `session_id` and `hook_event_name`, and that implementation should follow measurement when it conflicts with current external docs.                                                                 |
| H-006 | [`h-006-human-confirmation-2026-03-14-user-hook-location.md`](./h-006-human-confirmation-2026-03-14-user-hook-location.md)                               | Preserves the manual confirmation of current VS Code user-hook loading conditions. | This records that `~/.claude/settings.json` only took effect in the observed VS Code environment when `chat.useClaudeHooks` was enabled, and that future installation should still prefer an explicitly specified separate hook JSON path.              |
| H-007 | [`h-007-human-confirmation-2026-03-14-same-host-vscode-settings-targets.md`](./h-007-human-confirmation-2026-03-14-same-host-vscode-settings-targets.md) | Preserves the later clarification about same-host VS Code settings targets.        | This records that the relevant desktop and VS Code Server settings targets belong to the same host for managed installation purposes, and that default installation may target both rather than treating the problem as cross-machine.                  |
| H-008 | [`h-008-human-confirmation-2026-03-16-stop-blocking-summary-flow.md`](./h-008-human-confirmation-2026-03-16-stop-blocking-summary-flow.md)               | Preserves a superseded correction to the summary handoff design.                   | This remains provenance for removing the managed custom-instruction dependency. Its default `Stop`-blocking recovery direction is superseded by the current non-blocking degraded fallback design; blocking recovery is future strict/debug scope only. |
| H-009 | [`h-009-human-confirmation-2026-07-25-root-lifecycle-notifications.md`](./h-009-human-confirmation-2026-07-25-root-lifecycle-notifications.md)           | Preserves the root-only completion and `ask_user` attention boundary.              | This supersedes derived statements that treated subagent `Stop` events as notification opportunities and records the supplied tool-call-session spam evidence.                                                                                          |

## Reference derivation rule

Research documents in this folder should be grounded in the user-provided
references recorded in H-001.

Research documents are condensed local caches of the authoritative external
references listed above. They should be refreshed through the documented
read-through workflow and explicit verification refresh workflow rather than by
mixing repository-local runtime notes into the external-doc cache.

Human confirmation in H-002 through H-009 may refine the
interpretation, scope, and design inputs for those research documents, but the
external research claims should be traced back to the reference set listed in
H-001 rather than reconstructed from derived repository documents.

## Derived repository documents

The items in this section are derived from the human-authored inputs above,
from official external documentation, or from both.

| ID    | Document                                                                                   | Kind                                          | Derived from                                                                                                 | Notes                                                                                                                                                  |
| ----- | ------------------------------------------------------------------------------------------ | --------------------------------------------- | ------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| D-001 | [`functional-requirements.md`](./functional-requirements.md)                               | Derived functional specification              | H-001, H-002, H-003, H-004, H-007, H-008, H-009, D-002, and D-003                                            | Defines product-facing functional requirements after interpretation and normalization.                                                                 |
| D-002 | [`nonfunctional-and-constraints-research.md`](./nonfunctional-and-constraints-research.md) | Derived research and supporting specification | H-001 reference set, H-002, H-003, H-004, H-006, H-007, H-008, and H-009                                     | Grounds research claims in the user-provided references from H-001 while recording later confirmed product decisions.                                  |
| D-003 | [`vscode-hook-inputs-research.md`](./vscode-hook-inputs-research.md)                       | Derived technical research note               | H-001 reference set, H-003, H-005, H-008, H-009, current official hook docs, and repository runtime evidence | Explains hook inputs, root/subagent lifecycle boundaries, `ask_user` observation through `PreToolUse`, and the summary-correlation boundary.           |
| D-004 | [`../README.md`](../README.md)                                                             | Derived project overview                      | H-001 through H-009, D-001, D-002, and D-005                                                                 | Convenient project entry point, but not the primary provenance ledger.                                                                                 |
| D-005 | [`implementation-language-evaluation.md`](./implementation-language-evaluation.md)         | Derived implementation design research note   | H-001, H-002, H-003, D-002, D-003, and official docs reviewed during the implementation-language evaluation  | Compares PowerShell, Python, and C# (including native AOT) for the supported product scope without turning language choice into a product requirement. |

## Current derivation chain

The current documentation flow is:

1. H-001 establishes the original project brief and records the user-provided
   external reference set.
2. H-002 records later human confirmation that clarifies and, where necessary,
   supersedes parts of H-001.
3. H-003 records a follow-up clarification round that further tightens the
   product boundary, failure expectations, and overlength-message treatment.
4. H-004 records a later clarification that Chinese summary language is
   best-effort and that secure secret storage remains the managed persistence
   path while explicit runtime environment-variable overrides are acceptable.
5. H-005 records the later manual verification that the current observed hook
   runtime uses `session_id` and `hook_event_name`, and that current
   implementation should follow the measured runtime contract when it conflicts
   with the current external documentation.
6. H-006 records the later manual confirmation that `~/.claude/settings.json`
   only takes effect as a user-level hook source in the observed VS Code
   environment when `chat.useClaudeHooks` is enabled, that
   `chat.hookFilesLocations` alone is insufficient, and that future
   installation should still prefer an explicitly specified separate hook JSON
   path.
7. H-007 records the later clarification that the host-local desktop VS Code
   settings target and the VS Code Server Machine settings target are distinct
   same-host installation entry points, and that managed installation may
   target both by default without turning the problem into a cross-machine
   client-versus-server story.
8. H-008 records the later correction that the unstable instruction-based
   summary handoff should be removed. Default `Stop` validation defers while
   a pending handoff may satisfy `Stop`; non-blocking degraded fallback
   notifications apply only when no pending handoff can satisfy it. Any
   blocking recovery is future strict/debug scope only.
9. H-009 records that completion notifications belong only to root sessions and
   that root `ask_user` calls are attention-notification opportunities.
10. D-002 and D-003 research the H-001 reference set, with D-002 also
    incorporating the later confirmed decisions from H-002, H-003, H-004,
    H-006, H-007, and H-008 where needed, and D-003 incorporating the later
    verification from H-005 and the later design correction from H-008 where
    needed.
11. D-001 derives the functional specification from the H-series decisions and
    the research outputs.
12. D-005 records the later implementation-language evaluation using the
    H-series inputs, the research outputs, and the official documentation
    reviewed during that evaluation.
13. D-004 synchronizes the repository overview with the current derived
    specification set.
14. Future design and architecture documents should cite this file together with
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
6. Refresh research documents through the documented read-through workflow or an
   explicit user-requested verification refresh, and keep repository-local
   runtime notes separate from the external-doc cache.
