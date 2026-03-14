# H-002 Human Confirmation — 2026-03-13

This file preserves the later human confirmation recorded in chat on
2026-03-13.

Because the original confirmation happened in chat rather than in a repository
file, the content here is a normalized English transcription for repository
traceability.

This document is a direct human-authored source input.

## Product boundary and support matrix

1. Formal support is only for user-level installation.
2. Workspace-level mode exists only for pre-release testing in this repository.
3. Supported environments are Windows and WSL Linux.
4. Git worktrees are in scope, but user-level installation is not expected to
   differ specifically for worktrees.
5. No explicit VS Code or Copilot version baseline is part of the current
   product contract.

## When notifications should be sent

1. `Stop` is the delivery trigger.
2. A notification should be attempted whenever a `Stop` event is triggered,
   regardless of outcome.
3. The summary should be trusted when present. If it is missing, the
   notification should explicitly say that the summary is missing.

## Summary contract

1. The summary is only a user-facing message and is not required to have a
   stable structured schema.
2. Summary content should use Chinese.
3. No sensitive-data filtering requirement is imposed on the summary by product
   contract.

## Delivery semantics

1. Delivery should use limited retry.
2. Duplicate suppression, if used, is per turn and exists to improve user
   experience rather than to provide a strong guarantee.
3. A new `Stop` event is a new delivery opportunity.
4. Telegram formatting should use HTML.

## Privacy and security

1. All contemplated execution-context fields are allowed in notifications.
2. Those fields are enabled by default.
3. Secure storage is required; `gopass` / GNU Pass are preferred.
4. Workspace overrides and disabled platform customization settings are
   acceptable platform limitations.

## Installation and operations

1. Installation success means the configuration was written successfully.
2. The lifecycle must support interactive install, unattended install, repeated
   install, upgrade, and uninstall.
3. The lifecycle must also provide health diagnostics, test notifications, and
   configuration diagnostics.
4. When new configuration conflicts with existing user configuration, the
   default behavior should preserve the user's current configuration, store the
   new candidate configuration under a timestamp-suffixed alternative, and ask
   the user to resolve the conflict manually.

## Provenance notes

1. This file records later human confirmation that refines the product scope
   and delivery semantics.
2. Later derived specifications may use this file to clarify or supersede parts
   of H-001, but should not silently rewrite H-001 itself.
