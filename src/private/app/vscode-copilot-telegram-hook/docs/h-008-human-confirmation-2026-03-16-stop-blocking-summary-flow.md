# H-008 Human Confirmation — 2026-03-16 Stop-Blocking Summary Flow

This file preserves the later clarification recorded in chat on 2026-03-16
after the first end-to-end implementation became functionally usable but the
instruction-based summary handoff proved unstable in long conversations.

Because the original confirmation happened in chat rather than in a repository
file, the content here is a normalized English transcription for repository
traceability.

This document is a direct human-authored source input.

## Summary handoff direction

1. The solution should remove the managed custom-instruction dependency for
   summary handoff.
2. The preferred summary-generation control point is the VS Code `Stop` hook
   itself.
3. When the current turn's summary file is missing or fails validation, the
   `Stop` hook should block stopping, explain the failure, and require Copilot
   to regenerate the file.
4. The `Stop` hook should allow the turn to finish once validation passes.
5. If validation still fails three times for the same turn, the `Stop` hook
   should stop blocking and allow the turn to finish.

## Compatibility direction

1. The implementation does not need to preserve backward compatibility with the
   earlier instruction-based approach.

## Provenance notes

1. This file records a later correction to the summary-handoff design after the
   earlier instruction-based implementation showed stability issues in long
   conversations.
2. Later derived specifications may use this file to refine or supersede parts
   of earlier derived documents, but should not silently rewrite the earlier
   H-series documents.
