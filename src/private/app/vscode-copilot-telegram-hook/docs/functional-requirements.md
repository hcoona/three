# Functional Requirements — VS Code Copilot Telegram Hook

## Provenance

- Kind: derived functional specification.
- Derived from:
    - [`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md)
    - [`h-002-human-confirmation-2026-03-13.md`](./h-002-human-confirmation-2026-03-13.md)
    - [`h-003-human-confirmation-2026-03-13-addendum.md`](./h-003-human-confirmation-2026-03-13-addendum.md)
    - [`nonfunctional-and-constraints-research.md`](./nonfunctional-and-constraints-research.md)
    - [`vscode-hook-inputs-research.md`](./vscode-hook-inputs-research.md)
- Purpose: translate the human-authored source inputs and supporting research
  into product-facing functional requirements.

This document defines **functional requirements only** for the VS Code GitHub Copilot Telegram hook solution in this project.

Non-functional requirements and implementation constraints are intentionally excluded from this document and are tracked separately in [`nonfunctional-and-constraints-research.md`](./nonfunctional-and-constraints-research.md).

Unless explicitly stated otherwise, this document does not standardize any
particular script structure, file layout, storage mechanism, internal field
name, or external summary-file schema.

## Terms

- **Chat turn**: one user request and Copilot response cycle that ends when Copilot attempts to stop for the current turn.
- **Session**: a VS Code GitHub Copilot chat session, which may contain multiple chat turns.
- **Workspace**: the current working folder used by VS Code when running hooks.
- **Correlation context**: the data used to associate summary generation and Telegram delivery with the correct Copilot session and workspace.
- **Tracked result**: the completed turn result that the solution correlates across summary generation and Telegram delivery.
- **Summary record**: summary content prepared for a tracked result before
  notification delivery. Its internal representation is implementation-defined
  and is not itself a stable product contract.

## Functional Scope

The solution shall provide a formally supported user-level capability for VS Code GitHub Copilot that sends Telegram notifications to one configured Telegram destination per user installation for each `Stop` event that ends the current chat turn and includes a concise Chinese task summary when available, or an explicit indication that the summary is missing, plus relevant execution context when available.

## Functional Requirements

### FR-001 User-level availability

The solution shall provide a user-level installation flow that makes the notification capability available across the user's VS Code workspaces.

### FR-002 User-level artifact and delivery configuration installation

The installation flow shall install or configure all user-level artifacts and delivery inputs required for:

1. hook execution, and
2. summary generation handoff, and
3. Telegram notification delivery.

This includes the Telegram bot token and target chat identifier required for notification authentication and routing.

### FR-003 Correlation capability

The solution shall maintain enough correlation context to associate each summary record and Telegram notification with the correct Copilot session and workspace.

### FR-004 Correlation context persistence

The correlation context required for summary generation and Telegram delivery shall remain available across the hook invocations that participate in that flow.

### FR-005 Correlation data contents

The correlation context shall provide, at minimum, enough information to distinguish:

- the current workspace,
- the current Copilot session,
- the tracked result represented by the summary record, and
- which result is the newer one when multiple turn results exist.

### FR-006 Summary availability

The solution shall make summary content available for the current tracked result before notification delivery is attempted, or otherwise surface that the summary is missing.

### FR-007 Summary handoff minimum capability

The summary handoff for a tracked result shall support, at minimum:

- a human-readable summary message when available,
- an explicit missing-summary indication when no summary is available, and
- any implementation-defined correlation or recency data needed internally for delivery coordination.

### FR-008 Summary handoff behavior

The solution shall provide a way for Copilot to update the summary content before it finishes work for the current chat turn.

### FR-009 Delivery independence from structured summary schema

Notification delivery shall not depend on the presence of a stable externally supported summary schema or a structured status field.

### FR-010 Summary language requirements

The summary content included in the notification shall be human-readable Chinese text. Any additional structure beyond that text is implementation-defined.

### FR-011 Turn completion detection

When Copilot reaches a `Stop` event for the current chat turn, the solution shall attempt Telegram notification delivery for that `Stop` event.

### FR-012 Stop-event interpretation

For this solution, the end-of-turn notification trigger shall correspond to the point where Copilot attempts to stop for the current chat turn, even if the overall session may continue with later turns.

### FR-013 Missing-summary fallback

If the current `Stop` event does not have usable summary content, the solution shall still send a notification and shall explicitly indicate that the summary is missing.

### FR-014 Telegram delivery target

The solution shall send notifications to a single Telegram destination per
user-level installation, identified by a bot token and a target chat
identifier.

### FR-015 Credential resolution for delivery

Before sending a notification, the solution shall resolve the credentials required to call the Telegram Bot API.

### FR-016 Notification format

The solution shall format the outgoing notification using a Telegram HTML-compatible text representation.

### FR-017 Required notification contents

Each notification shall include, at minimum:

- a human-readable completion heading,
- either the concise summary or an explicit missing-summary notice,
- a delivery timestamp, and
- enough identifying context to distinguish the delivered result from other notifications for the same workspace or session.

If the notification content for a tracked result cannot fit into a single
Telegram message while remaining valid under Telegram formatting rules, the
solution shall preserve the heading and identifying context and continue the
summary across additional Telegram messages.

### FR-018 Context enrichment

When available, each notification shall also include relevant execution context such as:

- Copilot session identifier,
- host name,
- execution environment,
- workspace path,
- repository display name,
- branch name,
- commit identifier, and
- transcript path.

### FR-019 Optional structured summary preview

When the implementation makes additional structured summary content available, the notification may include preview entries from that content.

### FR-020 Best-effort duplicate suppression

The solution shall prioritize notification delivery and limited retry over perfect duplicate suppression. Any duplicate suppression applied for the same chat turn shall be best-effort only and shall exist to improve user experience rather than to provide a strict correctness guarantee.

### FR-021 Delivery opportunity for later turns

Each new `Stop` event shall create a new delivery opportunity, including later turns in the same session.

### FR-022 Workspace isolation

The solution shall keep session correlation data and notification coordination state isolated per workspace.

### FR-023 Repository context probing

The solution shall inspect the current workspace to determine repository metadata when such metadata is available.

### FR-024 Operation without full repository metadata

The solution shall still be able to produce a notification when repository metadata is partially unavailable or missing.

### FR-025 Multi-turn session behavior

A single Copilot chat session may produce multiple turn-completion notifications over time, provided that each notification corresponds to a distinct completed turn result.

### FR-026 Latest-result clarity

The summary handoff mechanism shall make the latest known summary unambiguous when multiple turn results exist for the same tracked context.

### FR-027 Persistent coordination state

The solution shall preserve sufficient persistent correlation and coordination state across hook invocations so that:

1. correlation,
2. summary generation, and
3. Telegram delivery

can coordinate without requiring in-memory state to survive across hook invocations.

## Required Summary Semantics

The summary handoff data shall allow Copilot to communicate, for the current tracked result, at minimum:

- a concise Chinese description of what happened when such a description is available, or
- an explicit indication that the summary is missing.

Any additional structure, field names, changed-file lists, outcome labels, or next-step lists are implementation-defined rather than part of the product contract.
