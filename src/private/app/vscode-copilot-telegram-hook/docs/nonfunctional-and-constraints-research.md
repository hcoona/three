# Non-Functional Requirements and External Constraints Research

## Provenance

- Kind: derived research and supporting specification.
- Derived from:
    - [`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md),
      especially its user-provided reference set
    - [`h-002-human-confirmation-2026-03-13.md`](./h-002-human-confirmation-2026-03-13.md)
- Purpose: ground non-functional conclusions and external constraints in the
  user-provided references from H-001 while recording later confirmed product
  decisions from H-002.

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

### External platform and API sources inherited from H-001

- [VS Code — Agent hooks in Visual Studio Code (Preview)](https://code.visualstudio.com/docs/copilot/customization/hooks)
- [VS Code — Use custom instructions in VS Code](https://code.visualstudio.com/docs/copilot/customization/custom-instructions)
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

| ID      | Candidate non-functional requirement                                                                                                                                                                                                           | Why it is non-functional                                                                                                    | Source                                                                                                                           |
| ------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| NFR-001 | The solution should be portable across the user's supported operating environments, specifically Windows and WSL Linux.                                                                                                                        | This is a portability requirement about where the solution can operate, not what user-facing business behavior it provides. | H-001 plus product decision recorded in H-002.                                                                                   |
| NFR-002 | Once Telegram delivery credentials are provided, the solution should handle their persistence and reuse through a secure user-appropriate secret-management approach rather than embedding plaintext secrets in reusable artifacts.            | This is a security and operations requirement about how credentials are handled, not whether credentials are needed.        | H-001.                                                                                                                           |
| NFR-003 | User-level installation should be safe to rerun, should preserve existing user configuration on conflict, and should avoid damaging unrelated user configuration.                                                                              | This is an idempotency and safety requirement about operational behavior during installation.                               | Derived from H-001, the VS Code user-level customization model in the official docs, and the product decision recorded in H-002. |
| NFR-004 | Notification-side failures should degrade gracefully and should not make normal Copilot chat usage unavailable.                                                                                                                                | This is an availability and robustness requirement about failure impact, not a primary feature.                             | Derived from the customization role of hooks in the official VS Code documentation and the solution scope established in H-001.  |
| NFR-005 | Notification content should remain readable and valid within Telegram formatting and message-size limits.                                                                                                                                      | This is a usability and compatibility requirement shaped by an external delivery channel.                                   | Telegram Bot API documentation.                                                                                                  |
| NFR-006 | Installation and configuration should support both interactive and unattended operation.                                                                                                                                                       | This is an operability requirement about how the solution can be deployed and maintained.                                   | H-001 plus product decision recorded in H-002.                                                                                   |
| NFR-007 | The user-level lifecycle should support repeated install, upgrade, uninstall, and operator-oriented diagnostics such as health checks, test notifications, and configuration diagnostics.                                                      | This is an operability requirement about maintenance and support tooling rather than primary notification behavior.         | H-002.                                                                                                                           |
| NFR-008 | When installation encounters user-configuration conflicts, the preferred behavior should preserve the existing user configuration and store the new candidate configuration under a timestamp-suffixed alternative path for manual resolution. | This is a conflict-safety and operator-ergonomics requirement about installation behavior.                                  | H-002.                                                                                                                           |

## Confirmed Product Decisions (2026-03-13)

These decisions were explicitly confirmed after the initial research pass and should be treated as current design inputs.

### Product boundary and support matrix

- Formal product support is limited to user-level installation.
- Repository-local workspace hook configuration exists only for pre-release testing in this repository.
- Official support matrix: Windows and WSL Linux.
- Git worktrees are in scope, but user-level installation is not expected to diverge specifically for worktrees.
- No explicit VS Code or Copilot version baseline is currently part of the product contract.

### Delivery semantics and summary contract

- Every `Stop` event is a delivery opportunity and should trigger a notification attempt.
- Notification delivery is required regardless of whether the completed turn was successful, informational, unsuccessful, interrupted, or otherwise unusual.
- The summary is treated as user-facing message content, not as a stable external summary schema.
- If the summary is unavailable, the notification should explicitly say that the summary is missing.
- Notification summary text is expected to be Chinese.
- No default summary-side sensitive-data redaction requirement is currently part of the product contract.
- Telegram delivery formatting is standardized to HTML.
- Delivery uses limited retry.
- Duplicate suppression, if implemented, is best-effort per turn and exists only to improve user experience.

### Privacy, security, and operations

- All currently contemplated execution-context fields may be included in notifications and are enabled by default.
- Secure credential storage is required; `gopass` / GNU Pass are preferred mechanisms but not exclusive requirements.
- Workspace overrides or disabled hook/instruction loading are accepted platform limitations.
- User-level lifecycle support is required for interactive install, unattended install, repeated install, upgrade, uninstall, health diagnostics, test notifications, and configuration diagnostics.

## Real-World External Constraints

These items are not product requirements by themselves. They are constraints
imposed by the current platform, external APIs, or ecosystem behavior.

### VS Code and Copilot constraints

| ID     | Constraint                                                                                                                                                 | Why it is a constraint                                                                                                                | Source                                                                                                                                                                                                                    |
| ------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| IC-001 | User-level hook configuration is loaded from the configured user hook file, whose documented default location is `~/.claude/settings.json`.                | This is current platform behavior documented by VS Code.                                                                              | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks).                                                                                                                            |
| IC-002 | Workspace hooks take precedence over user hooks for the same event type.                                                                                   | A user-level installation cannot guarantee final execution behavior if a workspace defines its own competing hook for the same event. | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks).                                                                                                                            |
| IC-003 | User-level `.instructions.md` files are loaded from the configured user instructions path, whose documented default location is `~/.copilot/instructions`. | Summary handoff behavior depends on the current instruction-loading model.                                                            | [VS Code custom instructions documentation](https://code.visualstudio.com/docs/copilot/customization/custom-instructions).                                                                                                |
| IC-004 | Hooks and customizations are currently preview features and may change.                                                                                    | The upstream platform contract is not yet fully stable.                                                                               | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks).                                                                                                                            |
| IC-005 | Hook and instruction loading locations can be disabled or reconfigured by VS Code settings.                                                                | A correct installation can still be bypassed by user or workspace settings.                                                           | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks); [VS Code custom instructions documentation](https://code.visualstudio.com/docs/copilot/customization/custom-instructions). |
| IC-006 | Hook OS selection is based on the extension host platform, which may differ from the local machine in remote scenarios.                                    | Runtime command selection must tolerate remote, WSL, and container behavior.                                                          | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks).                                                                                                                            |
| IC-007 | The `Stop` hook corresponds to the point where the agent session is attempting to stop, but the session may continue afterward.                            | This lifecycle semantic shapes how turn completion can be interpreted.                                                                | [VS Code hooks documentation](https://code.visualstudio.com/docs/copilot/customization/hooks).                                                                                                                            |

### Telegram Bot API constraints

| ID     | Constraint                                                                                                       | Why it is a constraint                                                                      | Source                                                                       |
| ------ | ---------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| IC-101 | Bot API requests must be made over HTTPS to `https://api.telegram.org/bot<token>/METHOD_NAME`.                   | The delivery transport and endpoint shape are externally fixed.                             | [Telegram Bot API — Making requests](https://core.telegram.org/bots/api).    |
| IC-102 | `sendMessage` accepts `text` values of 1-4096 characters after entities parsing.                                 | Notification length must fit the Bot API limit.                                             | [Telegram Bot API — sendMessage](https://core.telegram.org/bots/api).        |
| IC-103 | Telegram formatting rules constrain which parse modes and markup forms are valid.                                | Message formatting must obey Telegram parsing rules for the chosen delivery representation. | [Telegram Bot API — Formatting options](https://core.telegram.org/bots/api). |
| IC-104 | Telegram API responses always include an `ok` field and may include a human-readable `description` for failures. | Delivery behavior must interpret success and failure using Telegram's response contract.    | [Telegram Bot API — Making requests](https://core.telegram.org/bots/api).    |

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
