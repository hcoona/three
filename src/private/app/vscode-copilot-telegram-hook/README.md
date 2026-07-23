# VS Code Copilot Telegram Hook

This app is the source of truth for the repository's VS Code and GitHub Copilot
CLI Telegram notifications.

It serves the managed/user-level installation flow:

1. A managed VS Code hook JSON file registered through supported same-host VS
   Code settings targets.
2. A GitHub Copilot CLI user extension that observes the foreground session.
3. Migration cleanup for legacy managed Copilot CLI hook entries.

There is intentionally no repository-local `.github/hooks/telegram-notify.json`
workspace hook entry.

The implementation follows the official VS Code Copilot hooks preview behavior:

- The official VS Code hooks docs currently list `~/.claude/settings.json` as
  a default user hook location.
- Workspace hooks can be loaded from `.github/hooks/*.json`, but this repository
  does not use a repo-local workspace hook for the Telegram notifier.
- Workspace hooks take precedence over user hooks for the same event when they
  exist.
- GitHub Copilot CLI loads user-level hook files from `*.json` files under
  `$COPILOT_HOME/hooks/` when `COPILOT_HOME` is set, otherwise
  `~/.copilot/hooks/` on Linux and macOS or `%USERPROFILE%\.copilot\hooks\` on
  Windows.
- GitHub Copilot CLI loads user-level extensions from
  `$COPILOT_HOME/extensions/` when `COPILOT_HOME` is set, otherwise
  `~/.copilot/extensions/` or `%USERPROFILE%\.copilot\extensions\`.

## Files

- `VSCodeCopilotTelegramHook.csproj`: Native AOT-enabled C# project integrated
  into the monorepo traversal build.
- `Program.cs`: Generic Host bootstrap and command-line entry point.
- `Commands/`: hook lifecycle commands and user-level install/diagnostic
  commands.
- `Notifications/`: Telegram message composition and delivery.
- `State/`: session-scoped `.copilot/notifications/sessions/<safe-session-id>/*.json`
  state management.
- `docs/README.md`: tracks human-authored source inputs and derivation
  relationships for the documentation set in `docs/`.

Only the C# implementation and its managed hook/runtime assets are shipped in
this directory.

## User-level installation

This integration requires GitHub Copilot CLI 1.0.41 or later. User extensions
load by default in those releases; no experimental feature flag is required.
Earlier releases exposed extensions only through experimental mode and are not
reported healthy by this application. Run `copilot update` before installation
when necessary.

Publish a Native AOT binary for the target runtime from the repository root:

```bash
app=./src/private/app/vscode-copilot-telegram-hook
project="$app/VSCodeCopilotTelegramHook.csproj"

dotnet publish "$project" -c Release -r linux-x64
```

Then install it for the current user:

```bash
app=./src/private/app/vscode-copilot-telegram-hook
binary="$app/bin/Release/net10.0/linux-x64/publish/vscode-copilot-telegram-hook"

"$binary" \
  user install \
  --binary-path "$binary"
```

For headless installation, you can seed missing Telegram values explicitly or
through environment variables:

```bash
app=./src/private/app/vscode-copilot-telegram-hook
binary="$app/bin/Release/net10.0/linux-x64/publish/vscode-copilot-telegram-hook"

"$binary" \
  user install \
  --binary-path "$binary" \
  --telegram-bot-token '<bot-token>' \
  --telegram-chat-id '<chat-id>' \
  --skip-secret-prompt
```

The installer command:

- prompts for the Telegram bot token and chat ID,
- accepts `TG_BOT_TOKEN` and `TG_CHAT_ID` as non-interactive input sources,
- stores missing values in `gopass`,
- asks before overwriting existing stored secrets and defaults to keeping them,
- installs the published Native AOT binary into a user-owned data directory,
- writes a dedicated managed hook JSON file under the install root,
- removes legacy managed GitHub Copilot CLI lifecycle and notification hooks
  while preserving unrelated hook entries,
- writes a GitHub Copilot CLI user-level `extension.mjs`,
- validates before writing side effects that the managed hook file path can be
  represented in VS Code as a supported `~/...` hook location,
- registers that managed hook file in the supported same-host VS Code
  `settings.json` targets through `chat.hookFilesLocations`,
- snapshots an existing managed installation and restores its artifacts and
  stored secrets if an upgrade fails.

This design follows the manual verification recorded in
[`docs/h-006-human-confirmation-2026-03-14-user-hook-location.md`](./docs/h-006-human-confirmation-2026-03-14-user-hook-location.md)
and the later clarification recorded in
[`docs/h-007-human-confirmation-2026-03-14-same-host-vscode-settings-targets.md`](./docs/h-007-human-confirmation-2026-03-14-same-host-vscode-settings-targets.md)
and intentionally avoids treating `~/.claude/settings.json` as the managed
steady-state install target. By default, the managed hook file lives under the
install root as `vscode-copilot-telegram-hook.hooks.json`. The installer then
registers that managed hook file in the same host's relevant VS Code settings
targets, including the host-local desktop VS Code user settings target and, on
Linux hosts, the VS Code Server Machine settings target even before that server
settings file already exists locally. Because VS Code only accepts relative
paths or `~/...` entries in
`chat.hookFilesLocations`, the installer writes the managed hook registration in
supported `~/...` form rather than as an absolute path.

For GitHub Copilot CLI, the installer inspects
`vscode-copilot-telegram-hook.json` under the configured CLI hooks directory and
removes this application's legacy `SessionStart`, `UserPromptSubmit`, `Stop`,
and `notification` entries. It does not create the file when no migration is
needed and preserves unrelated user hooks.

The installer also writes
`extensions/vscode-copilot-telegram-hook/extension.mjs` below the same Copilot
home. The extension attaches to the foreground session and listens for root
`permission.requested`, `elicitation.requested`, `user_input.requested`, and
`session.idle` events. It ignores attention and output events carrying
`agentId`, suppresses completion while human input is pending, and never uses
`agentStop`, `subagentStop`, or `assistant.idle` as completion signals.
`assistant.idle` is used only to correlate an untagged queued message with its
later turn start. This distinction prevents notifications for intermediate
main-agent pauses and subagent activity. See
[`docs/copilot-cli-lifecycle-notifications-research.md`](./docs/copilot-cli-lifecycle-notifications-research.md)
for the lifecycle rationale.

Attention jobs retain their request identity while queued. If the corresponding
permission, elicitation, or user-input request completes before delivery, the
extension drops the queued job and suppresses any later retry.

The completion summary reuses the latest root `session.task_complete` summary or
complete `assistant.message`, so no extra model request is made. If root work
occurred but neither text source was captured, the notifier still sends a
completion message with a fallback body. After installation, run `/clear` or
restart Copilot CLI so the new extension is loaded.

The extension serializes notifier processes and retries transient failures with
bounded backoff. The native notifier cancels an attempt after 25 seconds and
releases its owned claim; the extension begins child-process termination after
30 seconds as a hard fallback and waits for process exit before retrying. Claims
also record the owner process so a retry can reclaim one left by a terminated
notifier. The native notifier uses the same event ID on every attempt, so its
claim and sent-marker protocol prevents a successful retry from becoming a
duplicate Telegram notification. Claim creation, ownership-checked release, and
stale reclamation share a short-lived cross-process coordination lock so a
reclaimer cannot delete a newly replaced live claim.
If cancellation occurs after at least one Telegram chunk succeeds, the native
notifier writes the durable marker before propagating the failure, preventing a
retry from duplicating already delivered chunks.

Install, upgrade, and uninstall operations preserve snapshots of managed
artifacts, VS Code settings, Unix file/directory modes, and stored secrets. If a
later step fails, the operation restores the previous working state rather than
leaving a partial installation. Artifact rollback restores only paths that still
match the state written by the transaction, preserves concurrent user changes,
and continues restoring later artifacts if one path is blocked. Secret reads
and removals distinguish gopass `show` exit code 11 and `rm` exit code 10 as
their respective "not found" results; other failures abort the operation and
trigger rollback. Settings are included in every install rollback path, and
secrets are restored only while their current values still
match the values written by the transaction, preserving concurrent updates. A
per-user cross-process lock serializes install, upgrade, uninstall, and secret
commands from planning through success or rollback. Direct external edits to
gopass or managed files cannot participate in that lock and remain protected by
the transaction's optimistic state checks.

If you need to override the default VS Code settings targets, repeat
`--vscode-settings-path` once per target file you want the installer to manage.
If you need to override the default Copilot CLI hook file location, pass
`--copilot-cli-hook-file-path` to `user install`, `user uninstall`,
`user health`, or `user diagnose`.
An override whose parent directory is literally `hooks` also derives the sibling
Copilot home `extensions/` path. Arbitrary hook-file overrides do not relocate
the extension; use `--copilot-cli-extension-file-path` when that path must also
change.

If you need to inspect or update stored Telegram secrets after installation,
use the dedicated secret-management command:

```bash
app=./src/private/app/vscode-copilot-telegram-hook
binary="$app/bin/Release/net10.0/linux-x64/publish/vscode-copilot-telegram-hook"

"$binary" user secret
"$binary" user secret --telegram-bot-token '<bot-token>' --telegram-chat-id '<chat-id>'
```

The CLI also provides:

- `user uninstall`: remove the managed installation.
- `user health`: validate the installed binary, VS Code managed hook file,
  absence of legacy managed Copilot CLI hook entries, extension, VS Code
  settings registration, Copilot CLI user-extension runtime version, and
  credential resolution.
- `user diagnose`: print the resolved installation paths, VS Code settings
  targets, Copilot CLI hook and extension paths, credential availability, and
  log locations.
- `user secret`: read or update the stored Telegram secrets.
- `user test-notification`: send a test Telegram message without waiting for a
  Copilot stop event.
- `hook session-start`, `hook user-prompt-submit`, and `hook stop`: internal
  lifecycle entry points used by VS Code Copilot hooks.
- `hook notification`: legacy-compatible Copilot CLI notification-hook entry
  point; new installations do not register it.
- `copilot-cli session-event`: internal GitHub Copilot CLI extension entry
  point.

## Logging and troubleshooting

The runtime now writes owner-only diagnostic logs where the operating system
supports Unix file modes:

- Hook logs for valid session-scoped events:
  `.copilot/notifications/sessions/<safe-session-id>/hook.log`
- Fallback hook log for valid payloads that provide `cwd` but lack usable
  session context: `.copilot/hook.log`; fully unparsable JSON only emits
  stderr because the workspace log scope cannot be opened.
- User command log for `user install`, `user uninstall`, `user health`,
  `user diagnose`, `user secret`, and `user test-notification`:
  `<install-root>/user-command.log`

The session-scoped hook log keeps its diagnostics next to the corresponding
session metadata. The log captures hook lifecycle activity, state-file
operations, Telegram send attempts and retries, credential-resolution outcomes,
git probing, and related operational failures. The logger intentionally records
only this application's categories, so external HTTP/resilience components do
not write their request URIs into these files. Secrets such as the Telegram bot
token are intentionally redacted from the log.

Use `user diagnose` to print the current user-command log path, workspace
session log path pattern, workspace fallback hook log path, managed hook file
path, and every targeted VS Code settings path for the directory where you run
the command.

During runtime, the hook maintains session-scoped notification protocol state
under `.copilot/notifications/sessions/<safe-session-id>/`, including:

- `session.json`: session metadata for the current Copilot session.
- `current.json`: a cache pointing at the latest Notification Assignment.
- `prompts/<prompt-observation-id>.json`: all observed prompt submissions,
  including observation-only prompts that are not notifiable turns.
- `turns/<notification-turn-id>/turn.json`: a hook-created notifiable turn.
- `turns/<notification-turn-id>/summary.json`: the exact per-turn summary handoff
  file authorized by the Notification Assignment.
- `turns/<notification-turn-id>/stops/<stop-id>.json`: Stop observations.
- `turns/<notification-turn-id>/claims/delivery.claim`: atomic turn delivery claim.
- `turns/<notification-turn-id>/notifications/<notification-key>.json`: durable
  duplicate-suppression records.
- `notifications/<notification-key>.json`: session-level durable records used for
  degraded fallbacks and cross-path duplicate suppression.
- `claims/<notification-key>.claim`: atomic Stop delivery claim keyed by the raw
  Stop timestamp hash.
- `copilot-cli-events/<event-key>.claim`: in-flight ownership for a Copilot CLI
  attention or true-idle notification.
- `copilot-cli-events/<event-key>.reclaim.claim`: serialization for reclaiming a
  stale in-flight claim.
- `copilot-cli-events/<event-key>.sent`: durable duplicate suppression written
  only after Telegram delivery succeeds.
- `hook.log`: always-on diagnostic log for the current Copilot session.

These `.copilot/notifications/sessions/` files are runtime state and should
remain ignored in git.

The gopass prefix is fixed at `copilot/vscode-copilot-telegram-hook` so the
managed VS Code hook and Copilot CLI notification paths resolve the same
secrets.
As of the current official VS Code docs, the documented default user-level hook
file location is still `~/.claude/settings.json`, even when the feature is used
from VS Code GitHub Copilot. However, manual verification recorded in
[`docs/h-006-human-confirmation-2026-03-14-user-hook-location.md`](./docs/h-006-human-confirmation-2026-03-14-user-hook-location.md)
found that this path only acts as an effective user-level hook source in the
observed environment when `"chat.useClaudeHooks": true` is enabled; leaving the
path enabled in `chat.hookFilesLocations` or explicitly setting it to `true` is
not sufficient by itself. This repository therefore installs a dedicated
managed hook JSON file and registers that file through VS Code settings instead
of relying on Claude settings compatibility. The later clarification recorded in
[`docs/h-007-human-confirmation-2026-03-14-same-host-vscode-settings-targets.md`](./docs/h-007-human-confirmation-2026-03-14-same-host-vscode-settings-targets.md)
also confirms that these settings targets should be treated as distinct
same-host entry points rather than as a cross-machine installation story.
The runtime honors `TG_BOT_TOKEN` and `TG_CHAT_ID` from the process
environment as explicit overrides, but `gopass` is the primary persisted
mechanism for the managed user-level installation.

For VS Code summary generation, the hook emits a Notification Assignment for
high-confidence main user prompts. Only that assignment authorizes writing a
summary, and the agent must write only the exact per-turn `summary.json` path.
The `session_id`, `notification_turn_id`, and `notification_nonce` fields must
match the assignment; `updated_at` must be a valid UTC timestamp; and `summary`
must be non-empty for completed delivery. Write the summary in Chinese when
practical, but a usable non-Chinese summary is allowed. The default `Stop`
behavior never blocks. Valid summaries are sent normally. Truly pending
handoffs, including missing or unreadable `summary.json`, invalid JSON, JSON
`null`, or an exact pending summary for the same Stop, may defer notification
without fallback indefinitely while unresolved. Non-pending invalid, stale, or
ambiguous handoffs produce a degraded fallback notification with durable
duplicate suppression when no pending handoff can satisfy that Stop.

GitHub Copilot CLI does not use that per-turn handoff. Its extension captures
the final root response or task-completion summary already emitted by the
session, and sends it only at root `session.idle`. When root work was observed
without either summary source, it sends the same completion event with a
fallback body instead of silently dropping the notification.

## Build and validation

The project is automatically included in the repository traversal build through
the root `dirs.proj` file because the application lives under `src/` and its
tests live under `tests/`.

Recommended validation steps:

```bash
project=./src/private/app/vscode-copilot-telegram-hook/VSCodeCopilotTelegramHook.csproj
tests=./tests/private/app/vscode-copilot-telegram-hook/Hcoona.VsCodeCopilotTelegramHook.Tests.csproj

dotnet build "$project"
dotnet build "$tests"
dotnet vstest ./tests/private/app/vscode-copilot-telegram-hook/bin/Debug/net10.0/Hcoona.VsCodeCopilotTelegramHook.Tests.dll
dotnet publish "$project" -c Release -r linux-x64
```

## Requirements and documentation map

This README is a project entry point, not the authoritative requirements
ledger. The detailed requirement, research, and provenance material lives under
[`docs/`](./docs/).

Use these documents as the authoritative sources:

- [`docs/README.md`](./docs/README.md): provenance ledger and derivation map.
- [`docs/h-001-original-requirement-brief.md`](./docs/h-001-original-requirement-brief.md):
  original human-authored requirement brief and reference set.
- [`docs/h-002-human-confirmation-2026-03-13.md`](./docs/h-002-human-confirmation-2026-03-13.md):
  later human confirmation of product decisions.
- [h-003 addendum](./docs/h-003-human-confirmation-2026-03-13-addendum.md):
  follow-up human clarification on scope, failure handling, and overlength
  notifications.
- [`docs/h-004-human-confirmation-2026-03-14.md`](./docs/h-004-human-confirmation-2026-03-14.md):
  later clarification that Chinese summaries are best-effort and that runtime
  environment-variable credential overrides are acceptable.
- [`docs/h-006-human-confirmation-2026-03-14-user-hook-location.md`](./docs/h-006-human-confirmation-2026-03-14-user-hook-location.md):
  later manual confirmation that `~/.claude/settings.json` is only effective in
  the observed environment when `"chat.useClaudeHooks": true` is enabled, and
  that managed installation should still prefer an explicitly specified
  separate hook JSON path.
- [`docs/h-007-human-confirmation-2026-03-14-same-host-vscode-settings-targets.md`](./docs/h-007-human-confirmation-2026-03-14-same-host-vscode-settings-targets.md):
  later clarification that the host-local desktop VS Code settings target and
  the VS Code Server Machine settings target belong to the same host for
  managed-installation purposes, and that default installation may target both.
- [`docs/h-008-human-confirmation-2026-03-16-stop-blocking-summary-flow.md`](./docs/h-008-human-confirmation-2026-03-16-stop-blocking-summary-flow.md):
  provenance for removing the managed instruction dependency. Its original
  default `Stop`-blocking recovery direction is superseded by the current
  non-blocking degraded fallback design; blocking recovery is future
  strict/debug scope only.
- [`docs/functional-requirements.md`](./docs/functional-requirements.md):
  current derived functional specification.
- [nonfunctional constraints](./docs/nonfunctional-and-constraints-research.md):
  current non-functional requirements and external constraints research.
- [`docs/vscode-hook-inputs-research.md`](./docs/vscode-hook-inputs-research.md):
  project-focused analysis of the VS Code hook input contract.
- [`docs/copilot-cli-lifecycle-notifications-research.md`](./docs/copilot-cli-lifecycle-notifications-research.md):
  CLI-specific lifecycle research and the true-idle notification design.
- [`docs/implementation-language-evaluation.md`](./docs/implementation-language-evaluation.md):
  implementation-language comparison for PowerShell, Python, and C# based on
  the documented product scope and official platform behavior.

In short, VS Code continues to notify at its completed-turn `Stop` boundary.
GitHub Copilot CLI notifies only for human-attention requests and root
`session.idle`, with subagent activity and intermediate main-agent pauses
excluded. Both surfaces reuse the same Telegram delivery and credential
infrastructure.
