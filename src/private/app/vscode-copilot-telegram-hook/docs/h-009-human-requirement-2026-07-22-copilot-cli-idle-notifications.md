# H-009 Human Requirement — 2026-07-22 Copilot CLI Idle Notifications

## Provenance

- Kind: direct human-authored requirement preserved from chat.
- Date recorded: 2026-07-22.
- Scope: GitHub Copilot CLI notifications delivered through the existing
  Telegram bot integration.

## Requirement

The existing GitHub Copilot CLI to Telegram delivery path is working. The
remaining requirement is to notify the user only when attention is genuinely
needed:

1. Copilot CLI is waiting for human input or approval.
2. Copilot CLI has completed the current work and will not continue by itself.

Notifications must not fire for an intermediate main-agent pause or for
subagent completion. Previous `Stop`-style hook experiments produced both kinds
of spam and are not an acceptable lifecycle signal.

A short contextual summary is desirable when completion is reported. The
summary should reuse information Copilot already produced when possible, rather
than adding a model call and token cost to every turn.

The implementation should be based on research into the actual lifecycle
contracts, solve the root signal-selection problem rather than patch individual
spam cases, and undergo independent review until no actionable findings remain.
