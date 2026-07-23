# GitHub Copilot CLI Lifecycle Notification Research

## Provenance

- Kind: derived technical research and design decision.
- Derived from:
  - [`h-009-human-requirement-2026-07-22-copilot-cli-idle-notifications.md`](./h-009-human-requirement-2026-07-22-copilot-cli-idle-notifications.md)
  - [GitHub Copilot hooks reference](https://docs.github.com/en/copilot/reference/hooks-reference)
  - [Copilot SDK streaming session events](https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/streaming-events)
  - [Copilot CLI extensions](https://github.com/github/copilot-sdk/blob/main/nodejs/docs/extensions.md)
  - [Generated Node.js session event types](https://github.com/github/copilot-sdk/blob/main/nodejs/src/generated/session-events.ts)
  - [Copilot CLI changelog](https://github.com/github/copilot-cli/blob/main/changelog.md)
  - [Copilot CLI configuration directory](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-config-dir-reference)

## The lifecycle problem

The desired completion condition is global quiescence: the foreground session
has no remaining work that can continue without the user.

That condition is not equivalent to a main-agent turn boundary:

- `agentStop` fires when the main agent finishes a turn.
- `subagentStop` fires when a subagent completes.
- `assistant.idle` is emitted whenever the main agent processing loop goes idle,
  including while background agents or attached shell commands are still
  pending.
- `session.idle` is the narrower event whose generated SDK contract states that
  no background agents or attached shell commands are in flight.

Using `agentStop`, `Stop`, or `assistant.idle` therefore observes a local pause,
not the state the user wants. Trying to repair those signals with timers or
subagent counters would duplicate lifecycle state already owned by Copilot CLI
and would remain vulnerable to races.

## Human-attention signals

The public `notification` hook is asynchronous and supports a regular-expression
`matcher` over `notification_type`. Its documented notification types include:

- `permission_prompt`
- `elicitation_dialog`
- background shell completion
- background agent completion or idle

Although a matcher can exclude background progress types, the hook payload does
not identify whether a permission or elicitation prompt originated from the root
agent or a subagent. A managed notification hook would therefore reintroduce the
subagent spam this design is intended to remove.

The extension instead handles root `permission.requested`,
`elicitation.requested`, and `user_input.requested` events after checking the
SDK envelope-level `agentId`. Installation removes older managed notification
and lifecycle hooks rather than registering a replacement CLI hook.
Each queued attention job carries its pending-request key. The key is checked
before the job leaves the delivery queue and before every retry, so a request
that completes while waiting cannot produce a stale Telegram alert.

## Completion signal

The managed user extension calls `joinSession()`, which attaches it to the
current foreground session. It listens for root/session-level `session.idle`
events and ignores any event with `agentId`, because the SDK event envelope uses
`agentId` for subagent-originated events and omits it for the root agent and
session-level events.

The extension does not send a completion notification when:

- the idle event reports an aborted loop,
- a root permission, elicitation, or user-input request is still pending, or
- no root work has been observed since the preceding completion.

This makes the notification represent stable quiescence instead of an
intermediate pause.

## Summary without an additional model call

The extension retains the latest root:

1. `session.task_complete` summary, when it is newer than the final response, or
2. complete `assistant.message` content.

That existing text is delivered as the Telegram summary. No additional model
request is made. Subagent messages are excluded through the envelope-level
`agentId` filter.

If root work occurred but neither text source was captured, completion is still
delivered with a fallback body. Summary availability enriches the notification;
it is not used as a proxy for whether work completed.

The notifier truncates an unusually long summary before composing Telegram
messages. Delivery itself remains subject to the existing Telegram chunking and
retry behavior.

## Duplicate suppression and failure behavior

The extension serializes notifier subprocess calls so attention and completion
events cannot race each other inside one extension process. Completion state
distinguishes a pending subprocess call from a successfully delivered event, so
a failed notifier process can be retried. Each queued event receives a bounded
set of backoff retries with the same event ID before the failure is logged. Each
native attempt has a 25-second cooperative timeout that releases its claim. The
extension begins terminating the child after 30 seconds as a hard fallback,
waits for process exit, and then retries so a hung process cannot overlap later
delivery attempts.

The native notifier uses separate per-session event files:

- `.claim` for in-flight ownership,
- `.reclaim.claim` to serialize stale-claim recovery,
- `.coordination.lock` to serialize managed claim replacement and deletion, and
- `.sent` for post-delivery durable suppression.

If no Telegram message is sent, the owned claim is released so a later retry can
succeed. A stale claim can be reclaimed; Copilot CLI claims also record the
native owner process so a retry can immediately reclaim a claim left by a
terminated process. A live claim without a `.sent` marker returns a retryable
exit code rather than success. `.sent` is written only after at least one
Telegram message succeeds. Successfully delivered events therefore remain
durably suppressed without treating an interrupted attempt as delivery.
Claim creation, ownership-checked release, and stale reclamation use the same
per-claim coordination lock, preventing a stale reclaimer from unlinking a new
claim that replaced the file it originally inspected.
Cancellation after a partial multi-chunk delivery is wrapped with the successful
chunk count, allowing the native notifier to write `.sent` before propagating
the failure and preventing duplicate chunks on retry.

Notification failures are logged and do not block Copilot CLI.

## Installation and reload behavior

The user installer manages one steady-state Copilot CLI artifact and one
migration surface:

- `extensions/vscode-copilot-telegram-hook/extension.mjs`, and
- removal of legacy managed entries from the user hook file when present.

Extensions are reloaded when the foreground session is replaced, on `/clear`,
or after Copilot CLI restarts. A newly installed extension therefore requires
one of those reload boundaries before it begins observing the session.

Extensions were introduced behind experimental mode in Copilot CLI 1.0.3. The
1.0.41 release changed user extensions to load by default, and current CLI
documentation lists the user `extensions/` directory without an experimental
prerequisite. This integration therefore requires Copilot CLI 1.0.41 or later
instead of depending on an undocumented individual feature-flag environment
variable. `user health` runs `copilot version` and fails the user-extension
runtime check when the CLI is missing, its version cannot be parsed, or it is
older than 1.0.41.

When a hook-file override is below a directory literally named `hooks`, the
installer derives the sibling Copilot home `extensions/` path. Other hook-file
overrides do not imply a different Copilot home; those layouts require an
explicit extension-path override.

Install, upgrade, and uninstall operations snapshot the existing managed binary,
hook files, extension, companions, VS Code settings, Unix file/directory modes,
and stored Telegram secrets before mutation. A later artifact, reporting, or
settings failure restores that snapshot; a fresh install retains only newly
created VS Code artifacts that may still be referenced when settings rollback
itself fails. Artifact restoration is mutation-aware: it restores only files
that still match the state written by the transaction, preserves concurrent
user changes, and attempts every remaining artifact after an individual
failure. Stored-secret reads and removals distinguish their operation-specific
gopass not-found results (`show` exit code 11 and `rm` exit code 10) from
decryption, I/O, and other failures so rollback cannot mistake an unreadable
secret for an absent one. Settings plans are rolled back on every
install failure after planning, including reporting/logging failures. Secret
mutations use optimistic value checks before writes/removals and before rollback
so a concurrent update is preserved instead of overwritten. Managed install,
upgrade, uninstall, and secret commands also hold one per-user cross-process
file lock from before planning through success or rollback. This closes the
check/write race between application processes, including commands using
different install-root overrides. External tools such as direct gopass commands
cannot honor the application lock, so optimistic checks remain necessary to
preserve externally concurrent changes.

## Decision

Use the foreground-session extension for root permission, elicitation,
user-input, and `session.idle` events. Keep the native notification-hook command
only for backward compatibility; do not register it in new managed
installations because its payload cannot distinguish root and subagent prompts.

Do not use `agentStop`, `Stop`, `subagentStop`, background-agent notification
types, or `assistant.idle` as completion signals.
