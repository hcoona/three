# Non-Functional Requirements and External Constraints Research

## Provenance

- Kind: derived research and supporting specification.
- Derived from:
    - [`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md),
      especially its user-provided reference set
    - [`h-002-human-confirmation-2026-03-13.md`](./h-002-human-confirmation-2026-03-13.md)
    - [`h-003-human-confirmation-2026-03-13-addendum.md`](./h-003-human-confirmation-2026-03-13-addendum.md)
    - [`h-004-human-confirmation-2026-03-14.md`](./h-004-human-confirmation-2026-03-14.md)
    - [`h-006-human-confirmation-2026-03-14-user-hook-location.md`](./h-006-human-confirmation-2026-03-14-user-hook-location.md)
    - [`h-007-human-confirmation-2026-03-14-same-host-vscode-settings-targets.md`](./h-007-human-confirmation-2026-03-14-same-host-vscode-settings-targets.md)
    - [`h-008-human-confirmation-2026-03-16-stop-blocking-summary-flow.md`](./h-008-human-confirmation-2026-03-16-stop-blocking-summary-flow.md)
    - [`h-009-human-requirement-2026-07-22-copilot-cli-idle-notifications.md`](./h-009-human-requirement-2026-07-22-copilot-cli-idle-notifications.md)
- Purpose: ground non-functional conclusions and external constraints in the
  user-provided references from H-001 while recording later confirmed product
  decisions from H-002, H-003, H-004, H-006, and H-007.

This document captures the items that are intentionally kept separate from
[`functional-requirements.md`](./functional-requirements.md):

1. candidate non-functional requirements, and
2. real-world platform or API constraints.

The document is intentionally written to stay decoupled from any single
repository implementation. Concrete script structure, file layout, storage
mechanism, and internal identifier choices are treated as non-normative unless
they are explicitly standardized elsewhere.

## Evidence Basis

### Human-authored source inputs

- [`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md)
- [`h-002-human-confirmation-2026-03-13.md`](./h-002-human-confirmation-2026-03-13.md)
- [`h-003-human-confirmation-2026-03-13-addendum.md`](./h-003-human-confirmation-2026-03-13-addendum.md)
- [`h-004-human-confirmation-2026-03-14.md`](./h-004-human-confirmation-2026-03-14.md)
- [`h-006-human-confirmation-2026-03-14-user-hook-location.md`](./h-006-human-confirmation-2026-03-14-user-hook-location.md)
- [`h-007-human-confirmation-2026-03-14-same-host-vscode-settings-targets.md`](./h-007-human-confirmation-2026-03-14-same-host-vscode-settings-targets.md)
- [`h-008-human-confirmation-2026-03-16-stop-blocking-summary-flow.md`](./h-008-human-confirmation-2026-03-16-stop-blocking-summary-flow.md)
- [`h-009-human-requirement-2026-07-22-copilot-cli-idle-notifications.md`](./h-009-human-requirement-2026-07-22-copilot-cli-idle-notifications.md)

### External platform and API sources inherited from H-001

- [VS Code — Agent hooks in Visual Studio Code (Preview)](https://code.visualstudio.com/docs/copilot/customization/hooks)
- [GitHub Copilot hooks reference](https://docs.github.com/en/copilot/reference/hooks-reference)
- [Copilot SDK streaming session events](https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/streaming-events)
- [Copilot CLI extensions](https://github.com/github/copilot-sdk/blob/main/nodejs/docs/extensions.md)
- [Telegram Bot API](https://core.telegram.org/bots/api)

These are the user-provided references preserved in
[`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md).

### Evidence intentionally excluded from this document

Current repository scripts, hook files, and instruction files may illustrate one
implementation, but they are not used here as normative evidence for
requirements or constraints. This avoids promoting current implementation
choices into an accidental specification.

## Candidate Non-Functional Requirements

These items describe quality goals, operational expectations, or design targets
rather than primary business behavior.

| ID      | Candidate non-functional requirement                                                                                                                                                                                                                                                                                         | Why it is non-functional                                                                                                    | Source                                                                                                                           |
| ------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| NFR-001 | The solution should be portable across the user's supported operating environments, specifically Windows and WSL Linux.                                                                                                                                                                                                      | This is a portability requirement about where the solution can operate, not what user-facing business behavior it provides. | H-001 plus product decision recorded in H-002.                                                                                   |
| NFR-002 | Once Telegram delivery credentials are provided, the solution shall handle persisted credential reuse through a secure user-appropriate secret-management approach rather than embedding plaintext secrets in reusable artifacts, while allowing explicit operator-supplied runtime overrides such as environment variables. | This is a security and operations requirement about how credentials are handled, not whether credentials are needed.        | H-001 plus product decisions recorded in H-003 and H-004.                                                                        |
| NFR-003 | User-level installation should be safe to rerun, should preserve existing user configuration on conflict, and should avoid damaging unrelated user configuration.                                                                                                                                                            | This is an idempotency and safety requirement about operational behavior during installation.                               | Derived from H-001, the VS Code user-level customization model in the official docs, and the product decision recorded in H-002. |
| NFR-004 | Notification-side failures should degrade gracefully and should not make normal Copilot chat usage unavailable.                                                                                                                                                                                                              | This is an availability and robustness requirement about failure impact, not a primary feature.                             | Derived from the customization role of hooks in the official VS Code documentation and the solution scope established in H-001.  |
| NFR-005 | Notification content should remain readable and valid within Telegram formatting and message-size limits, including continuation across multiple Telegram messages when needed.                                                                                                                                              | This is a usability and compatibility requirement shaped by an external delivery channel.                                   | Telegram Bot API documentation plus product decision recorded in H-003.                                                          |
| NFR-006 | Installation and configuration should support both interactive and unattended operation.                                                                                                                                                                                                                                     | This is an operability requirement about how the solution can be deployed and maintained.                                   | H-001 plus product decision recorded in H-002.                                                                                   |
| NFR-007 | The user-level lifecycle should support repeated install, upgrade, uninstall, and operator-oriented diagnostics such as health checks, test notifications, and configuration diagnostics.                                                                                                                                    | This is an operability requirement about maintenance and support tooling rather than primary notification behavior.         | H-002.                                                                                                                           |
| NFR-008 | When installation encounters user-configuration conflicts, the preferred behavior should preserve the existing user configuration and store the new candidate configuration under a timestamp-suffixed alternative path for manual resolution.                                                                               | This is a conflict-safety and operator-ergonomics requirement about installation behavior.                                  | H-002.                                                                                                                           |

## Confirmed Product Decisions (2026-03-13 to 2026-03-14)

These decisions were explicitly confirmed after the initial research pass and should be treated as current design inputs.

### Product boundary and support matrix

- Formal product support is limited to user-level installation.
- Repository-local workspace hook configuration is superseded and intentionally
  absent; supported hook entry points are the managed VS Code hook file and the
  Copilot CLI extension, with the CLI hook path retained only for legacy managed
  entry migration.
- Official support matrix: Windows and WSL Linux.
- The earlier concern that hook locality had to be settled before design is not
  treated as an open product-level requirement gap; broader upstream execution
  models are handled as external platform constraints rather than supported
  surface commitments.
- Git worktrees are in scope, but user-level installation is not expected to diverge specifically for worktrees.
- No explicit VS Code or Copilot version baseline is currently part of the product contract.

### VS Code delivery semantics and summary contract

- For VS Code, every `Stop` event is a delivery opportunity and should trigger a notification attempt.
- Notification delivery is required regardless of whether the completed turn was successful, informational, unsuccessful, interrupted, or otherwise unusual.
- The summary is treated as user-facing message content, not as a stable external summary schema.
- If the summary is unavailable, the notification should explicitly say that the summary is missing.
- Notification summary text should prefer Chinese on a best-effort basis, but a usable non-Chinese summary is still acceptable and should not block delivery.
- No default summary-side sensitive-data redaction requirement is currently part of the product contract.
- Telegram delivery formatting is standardized to HTML.
- Delivery uses limited retry.
- If delivery ultimately fails after limited retry, useful logs are sufficient
  and no extra local user-facing failure notification is required.
- Duplicate suppression, if implemented, is best-effort per turn and exists only to improve user experience.
- One installed user configuration targets one Telegram destination.
- Multiple workspaces and multiple sessions are in scope.
- If one logical notification would exceed Telegram limits, the required
  heading and identifying context should be preserved and the summary may
  continue across additional Telegram messages.
- Summary-generation guidance should account for Telegram message-size limits.

### GitHub Copilot CLI lifecycle semantics

- Human-attention notifications should be limited to permission prompts,
  elicitation dialogs, and direct user-input requests.
- Background shell and subagent completion or idle notifications are progress
  signals and should not be forwarded.
- `agentStop`, `subagentStop`, and `assistant.idle` are not completion signals
  for this requirement.
- Root/session-level `session.idle` is the completion boundary because the
  generated SDK contract states that no background agents or attached shell
  commands remain in flight.
- Events with `agentId` are subagent-originated and should be excluded from
  root-response and completion handling.
- Completion context should reuse an existing root task summary or final
  assistant message rather than requiring a new model call.

### Privacy, security, and operations

- All currently contemplated execution-context fields may be included in notifications and are enabled by default.
- Secure credential storage remains the required persisted mechanism for the
  managed installation flow; `gopass` / GNU Pass remain preferred mechanisms,
  but the product does not need to standardize or automate installation of the
  secret-storage system itself.
- Explicit runtime environment-variable overrides are acceptable for
  operator-controlled scenarios and do not violate the product contract.
- Manual VS Code verification showed that `~/.claude/settings.json` does not
  currently take effect as a user-level hook source for the supported scenario
  when `chat.useClaudeHooks` is not enabled, even when
  `chat.hookFilesLocations` relies on its default enabled value or explicitly
  sets `"~/.claude/settings.json": true`.
- The same manual verification also showed that hooks in
  `~/.claude/settings.json` do take effect when `"chat.useClaudeHooks": true`
  is configured.
- Managed user-level installation should still avoid treating
  `~/.claude/settings.json` plus `chat.useClaudeHooks` as the preferred steady-
  state target and should move to another explicitly registered user-level hook
  JSON path instead.
- For managed installation purposes, the host-local desktop VS Code settings
  target and the VS Code Server Machine settings target are same-host entry
  points rather than a client-machine versus remote-machine split.
- Managed installation may register the dedicated managed hook JSON file in
  both same-host settings targets by default when both runtime styles matter on
  the same host.
- Any managed `chat.hookFilesLocations` registration must use a supported
  relative or `~/` path form rather than an absolute path.
- For this personal-use tool, no additional privacy-policy scope is currently
  introduced beyond the accepted credential-handling prerequisite.
- Workspace overrides or disabled hook loading are accepted platform limitations.
- User-level lifecycle support is required for interactive install, unattended install, repeated install, upgrade, uninstall, health diagnostics, test notifications, and configuration diagnostics across the managed hook and extension artifacts.
- The default summary path should avoid a separately installed
  custom-instruction artifact and should use hook-emitted notification
  assignments. Pending assigned summaries may defer without degraded fallback;
  fallback applies only when no pending handoff can satisfy the `Stop`.
- Stop-blocking summary recovery from H-008 is superseded for default behavior;
  it may only be considered as future strict/debug scope and is not implemented
  by the current redesign.

### Requirement and design boundary

- Detailed lifecycle deletion, retention, or cleanup behavior may remain a
  design choice rather than a pre-design product requirement.

## Real-World External Constraints

These items are not product requirements by themselves. They are constraints
imposed by the current platform, external APIs, or ecosystem behavior.

### VS Code and Copilot constraints

| ID     | Constraint                                                                                                                                                                                                                                                                                                  | Why it is a constraint                                                                                                                                                                                   | Source                                                                                                                      |
| ------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| IC-001 | User-level hook configuration is loaded from the configured user hook file, whose documented default location is `~/.claude/settings.json`.                                                                                                                                                                 | This is current platform behavior documented by VS Code.                                                                                                                                                 | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks).                              |
| IC-002 | Workspace hooks take precedence over user hooks for the same event type.                                                                                                                                                                                                                                    | A user-level installation cannot guarantee final execution behavior if a workspace defines its own competing hook for the same event.                                                                    | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks).                              |
| IC-003 | Hooks and customizations are currently preview features and may change.                                                                                                                                                                                                                                     | The upstream platform contract is not yet fully stable.                                                                                                                                                  | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks).                              |
| IC-004 | Hook loading locations can be disabled or reconfigured by VS Code settings.                                                                                                                                                                                                                                 | A correct installation can still be bypassed by user or workspace settings.                                                                                                                              | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks).                              |
| IC-005 | Official VS Code docs describe hooks as working across local agents, background agents, and cloud agents, and OS-specific hook commands are selected based on the extension host platform, which may differ from the local machine in remote scenarios.                                                     | Hook behavior should not be documented as a purely local execution model; runtime command selection must tolerate broader platform behavior even if the current product support target remains narrower. | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks).                              |
| IC-006 | The `Stop` hook corresponds to the point where the agent session is attempting to stop, but the session may continue afterward.                                                                                                                                                                             | This lifecycle semantic shapes how turn completion can be interpreted.                                                                                                                                   | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks).                              |
| IC-007 | In the current observed VS Code environment for this project, `~/.claude/settings.json` becomes an effective user-level hook source only when `"chat.useClaudeHooks": true` is enabled; `chat.hookFilesLocations` leaving that path enabled or explicitly setting it to `true` is not sufficient by itself. | This is a measured platform condition that limits which user-level hook locations can be treated as reliable installation targets without extra compatibility settings.                                  | [`h-006-human-confirmation-2026-03-14-user-hook-location.md`](./h-006-human-confirmation-2026-03-14-user-hook-location.md). |
| IC-008 | `chat.hookFilesLocations` only supports relative paths or paths that start with `~/`; absolute paths and `\` separators are not supported.                                                                                                                                                                  | Managed installation cannot rely on raw absolute filesystem paths when it registers a dedicated hook JSON file in VS Code settings.                                                                      | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks).                              |
| IC-009 | The `Stop` hook can return `hookSpecificOutput` with `decision: "block"` and `reason` to prevent stopping, and the input field `stop_hook_active` indicates when the agent is already continuing because of a previous stop hook.                                                                           | This is a platform capability only. The current default notification flow must not use it for missing or invalid summaries; any blocking recovery belongs to future strict/debug scope.                  | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks).                              |
| IC-010 | Copilot CLI `agentStop` fires when the main agent finishes a turn, while `subagentStop` is a separate subagent boundary. | A main-agent turn boundary does not prove that background work is globally quiescent. | [GitHub Copilot hooks reference](https://docs.github.com/en/copilot/reference/hooks-reference). |
| IC-011 | The asynchronous `notification` hook supports a matcher on `notification_type`, including `permission_prompt` and `elicitation_dialog`, but its payload does not provide the SDK envelope `agentId`. | A matcher can exclude background progress types but cannot reliably exclude subagent-originated permission or elicitation prompts, so the managed CLI path uses extension events instead. | [GitHub Copilot hooks reference](https://docs.github.com/en/copilot/reference/hooks-reference). |
| IC-012 | SDK events use envelope-level `agentId` for subagent-originated events and omit it for root/main-agent and session-level events. | Root-only notification content can be selected without reconstructing the agent hierarchy. | [Copilot SDK streaming session events](https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/streaming-events). |
| IC-013 | The generated SDK contract distinguishes `assistant.idle`, which may occur while related background work remains, from `session.idle`, which has no background agents or attached shell commands in flight. | Only `session.idle` satisfies the requested global-quiescence boundary. | [Generated Node.js session event types](https://github.com/github/copilot-sdk/blob/main/nodejs/src/generated/session-events.ts). |
| IC-014 | Copilot CLI extensions attach to the current foreground session with `joinSession()` and reload on `/clear`, foreground-session replacement, or CLI restart. | A newly installed extension is not active in an already-running session until an extension reload boundary occurs. | [Copilot CLI extensions](https://github.com/github/copilot-sdk/blob/main/nodejs/docs/extensions.md). |

### Telegram Bot API constraints

| ID     | Constraint                                                                                                        | Why it is a constraint                                                                      | Source                                                                       |
| ------ | ----------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| IC-101 | Bot API requests must be made over HTTPS to `https://api.telegram.org/bot<token>/METHOD_NAME`.                    | The delivery transport and endpoint shape are externally fixed.                             | [Telegram Bot API — Making requests](https://core.telegram.org/bots/api).    |
| IC-102 | `sendMessage` accepts `text` values of 1-4096 characters after entities parsing.                                  | Notification length must fit the Bot API limit.                                             | [Telegram Bot API — sendMessage](https://core.telegram.org/bots/api).        |
| IC-103 | Telegram formatting rules constrain which parse modes and markup forms are valid.                                 | Message formatting must obey Telegram parsing rules for the chosen delivery representation. | [Telegram Bot API — Formatting options](https://core.telegram.org/bots/api). |
| IC-104 | Telegram API responses always include an `ok` field and may include a human-readable `description` for failures.  | Delivery behavior must interpret success and failure using Telegram's response contract.    | [Telegram Bot API — Making requests](https://core.telegram.org/bots/api).    |
| IC-105 | The Bot API documents the message-length limit per individual `sendMessage` request, not per logical task result. | Overlength notifications must be shortened or emitted as multiple `sendMessage` requests.   | [Telegram Bot API — sendMessage](https://core.telegram.org/bots/api).        |

### Telegram length-limit interpretation for this project

The official `sendMessage` contract applies the `1-4096` character limit to
each individual API request after entities parsing. The Bot API does not
document an automatic overflow or pagination mechanism for overlength text.
Therefore, a logical notification that would be too long for one request must
either be shortened before delivery or emitted as multiple `sendMessage` calls,
each of which stays within the per-message limit and remains valid for the
chosen parse mode.

For this project's confirmed direction, preserving the notification heading and
identifying context while continuing the summary across additional messages is
consistent with the upstream Telegram API contract.

## Items Intentionally Not Standardized Here

The following topics are intentionally left open in this document because they
are design choices, not externally imposed constraints:

- the concrete secret-storage product or CLI beyond the stated preference for `gopass` / GNU Pass,
- the concrete language used to implement hooks or installers,
- the exact internal file layout or file names used for summary and correlation state,
- the exact internal identifier name or format used for correlation,
- the exact retry schedule or dedupe key used for delivery coordination, and
- the exact file-materialization strategy used by installation.

## Remaining Low-Risk Design Choices

The major product-level decisions are now closed. The remaining open items are
primarily implementation choices:

1. The exact retry schedule, backoff policy, and retry stop conditions.
2. The exact internal state layout and per-turn dedupe key, if any.
3. The exact retention and cleanup policy for timestamp-suffixed conflict copies.
4. The exact diagnostics surface (commands, script names, or UI entry points).
5. Whether an explicit version-baseline policy should be introduced later if upstream preview behavior changes.
6. The exact chunking policy and summary-generation guidance used to keep
   notifications delivery-friendly under Telegram message-size limits.
