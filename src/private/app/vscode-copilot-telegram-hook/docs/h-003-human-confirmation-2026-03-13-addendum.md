# H-003 Human Confirmation Addendum — 2026-03-13

This file preserves a later follow-up confirmation recorded in chat on
2026-03-13 after the initial requirement-readiness review.

Because the original confirmation happened in chat rather than in a repository
file, the content here is a normalized English transcription for repository
traceability.

This document is a direct human-authored source input.

## Hook locality and support interpretation

1. The earlier concern that VS Code GitHub Copilot hooks might require an
   expanded pre-design support-matrix clarification was treated as likely a
   false positive for the current product scope.
2. The product should not broaden its formal support target merely because the
   upstream platform also discusses remote or non-local execution models.
3. If the official VS Code documentation describes broader execution models,
   later derived documents should treat that as an external platform constraint
   rather than as a new product-scope commitment.

## Secret storage prerequisite

1. Secure secret storage is a hard prerequisite rather than only a preferred
   implementation direction.
2. The product does not need to standardize or automate installation of the
   secret-storage system itself.

## Requirement and design boundary

1. Detailed lifecycle deletion, retention, or cleanup behavior may remain a
   design concern rather than a pre-design product requirement.
2. Later derived documents should avoid promoting such implementation details
   into top-level requirements unless they are explicitly confirmed later.

## Failure handling expectations

1. If Telegram delivery still fails after the configured retries are exhausted,
   useful logs are sufficient.
2. No additional local user-facing failure notification is required.
3. Notification-side failure must not affect the main Copilot workflow.

## Telegram target and scope

1. One user only needs one Telegram target.
2. Multiple workspaces are in scope.
3. Multiple sessions are in scope.

## Message length handling

1. Notification headers and identifying context should be preserved even when
   the final summary is long.
2. If a summary would exceed Telegram message limits, it may be continued
   across multiple Telegram messages instead of dropping the extra content.
3. Summary-generation guidance should account for Telegram length limits so the
   model tends to produce delivery-friendly summaries.

## Privacy and security scope

1. For this personal-use tool, privacy and security concerns do not need
   further product-definition expansion beyond the already accepted
   credential-handling prerequisite.
2. Later derived documents should avoid introducing extra privacy-policy scope
   unless it is explicitly requested.

## Provenance notes

1. This file records a follow-up clarification round after the earlier H-002
   confirmation.
2. Later derived specifications may use this file to refine or supersede parts
   of H-001 and H-002, but should not silently rewrite the earlier H-series
   documents.
