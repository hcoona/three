# H-004 Human Confirmation — 2026-03-14

This file preserves a later clarification recorded in chat on 2026-03-14 after
the first implementation-compliance review.

Because the original confirmation happened in chat rather than in a repository
file, the content here is a normalized English transcription for repository
traceability.

This document is a direct human-authored source input.

## Summary language interpretation

1. Chinese remains the preferred notification-summary language.
2. The Chinese-language expectation is best-effort rather than a hard delivery
   gate.
3. If a usable summary is available in another language, the solution may still
   deliver that summary instead of treating it as missing solely because it is
   not Chinese.

## Secret storage interpretation

1. Secure secret storage remains the required persisted credential-management
   mechanism for the managed user-level installation flow.
2. Explicit runtime credential injection through environment variables is an
   acceptable operator-controlled override and does not violate the product
   contract.
3. Later derived documents should distinguish persisted credential-management
   requirements from transient runtime overrides.

## Review follow-up note

1. The earlier implementation review correctly identified the runtime distinction
   between managed secret persistence and explicit environment-variable
   overrides.
2. Later derived documents should reflect that distinction instead of implying
   that every runtime invocation must rely on the secret store.

## Provenance notes

1. This file records a post-review clarification after H-002 and H-003.
2. Later derived specifications may use this file to refine or supersede parts
   of H-002 and H-003, but should not silently rewrite the earlier H-series
   documents.
