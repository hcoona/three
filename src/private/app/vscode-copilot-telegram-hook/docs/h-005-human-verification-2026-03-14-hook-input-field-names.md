# H-005 Human Verification — 2026-03-14 Hook Input Field Names

This file preserves a later human verification recorded in chat on 2026-03-14
after runtime hook-payload inspection.

Because the original verification happened in chat rather than in a repository
file, the content here is a normalized English transcription for repository
traceability.

This document is a direct human-authored source input.

## Verified runtime field names

1. In the current observed runtime environment, VS Code hook payloads use
   `session_id` rather than `sessionId`.
2. In the current observed runtime environment, VS Code hook payloads use
   `hook_event_name` rather than `hookEventName`.
3. This conclusion is based on actual observed hook payload diagnostics during
   execution, not on inference from repository source code alone.

## Conflict with external documentation

1. The current official VS Code hooks documentation still describes the common
   hook input fields with camelCase names such as `sessionId` and
   `hookEventName`.
2. For this project, when the current measured runtime behavior conflicts with
   the current external documentation, implementation should follow the
   measured runtime behavior until a later explicit verification proves
   otherwise.
3. Later derived documents should record this discrepancy clearly instead of
   silently rewriting the external documentation.

## Implementation interpretation

1. The repository implementation for current hook input parsing should treat
   `session_id` and `hook_event_name` as the current correct runtime contract.
2. This confirmation is specific to the currently observed product environment
   and should be re-verified if the VS Code hook runtime changes later.

## Provenance notes

1. This file records a later human verification after H-004.
2. Later derived specifications and research notes may use this file to refine
   or supersede earlier interpretations that relied only on external
   documentation.
3. This file does not change what the external documentation says; it records
   the human-confirmed project decision that current implementation should use
   the measured runtime contract when the two conflict.
