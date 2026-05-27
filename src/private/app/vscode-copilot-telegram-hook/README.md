# VS Code Copilot Telegram Hook

This app is the source of truth for the repository's VS Code GitHub Copilot
Telegram notification hooks.

It serves the managed/user-level installation flow:

1. A managed VS Code hook JSON file registered through supported same-host VS
   Code settings targets.
2. A GitHub Copilot CLI hook JSON file under the CLI hooks directory.

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
- writes a GitHub Copilot CLI user-level hook file under the CLI hooks
  directory,
- validates before writing side effects that the managed hook file path can be
  represented in VS Code as a supported `~/...` hook location,
- registers that managed hook file in the supported same-host VS Code
  `settings.json` targets through `chat.hookFilesLocations`.

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

For GitHub Copilot CLI, the installer writes
`vscode-copilot-telegram-hook.json` into `$COPILOT_HOME/hooks/` when
`COPILOT_HOME` is set, otherwise into `~/.copilot/hooks/` on Linux and macOS or
`%USERPROFILE%\.copilot\hooks\` on Windows. That file uses the Copilot CLI hook
schema with `version: 1`, `timeoutSec`, PascalCase event names
(`SessionStart`, `UserPromptSubmit`, and `Stop`), and
`HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE=copilot-cli`. The PascalCase
event names make Copilot CLI send VS Code-compatible snake_case input payloads;
the app-specific surface marker lets this application select
Copilot CLI-compatible hook output.

If you need to override the default VS Code settings targets, repeat
`--vscode-settings-path` once per target file you want the installer to manage.
If you need to override the default Copilot CLI hook file location, pass
`--copilot-cli-hook-file-path` to `user install`, `user uninstall`,
`user health`, or `user diagnose`.

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
  Copilot CLI managed hook file, VS Code settings registration, and credential
  resolution.
- `user diagnose`: print the resolved installation paths, VS Code settings
  targets, Copilot CLI hook path, credential availability, and log locations.
- `user secret`: read or update the stored Telegram secrets.
- `user test-notification`: send a test Telegram message without waiting for a
  Copilot stop event.
- `hook session-start`, `hook user-prompt-submit`, and `hook stop`: internal
  lifecycle entry points used by VS Code Copilot hooks.

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
- `hook.log`: always-on diagnostic log for the current Copilot session.

These `.copilot/notifications/sessions/` files are runtime state and should
remain ignored in git.

The gopass prefix is fixed at `copilot/vscode-copilot-telegram-hook` so the
managed VS Code hook file and Copilot CLI hook file resolve the same secrets.
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

For summary generation, the hook emits a Notification Assignment for
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

## Build and validation

The project is automatically included in the repository traversal build through
the root `dirs.proj` file because the application lives under `src/` and its
tests live under `tests/`.

Recommended validation steps:

```bash
project=./src/private/app/vscode-copilot-telegram-hook/VSCodeCopilotTelegramHook.csproj
tests=./tests/private/app/vscode-copilot-telegram-hook/Hcoona.VsCodeCopilotTelegramHook.Tests.csproj

dotnet build "$project"
dotnet test "$tests"
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
- [`docs/implementation-language-evaluation.md`](./docs/implementation-language-evaluation.md):
  implementation-language comparison for PowerShell, Python, and C# based on
  the documented product scope and official platform behavior.

In short, the current product target is a user-level VS Code GitHub Copilot
hook that attempts Telegram delivery for each completed-turn `Stop` event and
includes a concise task summary when available, preferring Chinese on a
best-effort basis, continuing across multiple Telegram messages when needed to
stay within Telegram limits.
