# VS Code Copilot Telegram Hook

This app is the source of truth for the repository's VS Code GitHub Copilot
Telegram notification hooks.

It serves two use cases:

1. The repository-local hook entry in `.github/hooks/telegram-notify.json`,
   used only for pre-release testing in this repository.
2. A user-level installation that applies to any workspace in VS Code and
   represents the formally supported product use case.

The implementation follows the official VS Code Copilot hooks preview behavior:

- Workspace hooks are loaded from `.github/hooks/*.json`.
- User hooks are loaded from `~/.claude/settings.json` by default.
- Workspace hooks take precedence over user hooks for the same event.

The implementation also follows the official VS Code GitHub Copilot custom
instructions behavior:

- User-level `.instructions.md` files are supported under `~/.copilot/instructions`.
- These instructions are separate from hooks and are required if you want task
  summaries, not just raw Telegram notifications.

## Files

- `VSCodeCopilotTelegramHook.csproj`: Native AOT-enabled C# project integrated
  into the monorepo traversal build.
- `Program.cs`: Generic Host bootstrap and command-line entry point.
- `Commands/`: hook lifecycle commands and user-level install/diagnostic
  commands.
- `Notifications/`: Telegram message composition and delivery.
- `State/`: session-scoped `.copilot/sessions/<session_id>/*.json` state
  management.
- `instructions/copilot-notify-summary.instructions.md`: managed user
  instruction template for summary handoff.
- `docs/README.md`: tracks human-authored source inputs and derivation
  relationships for the documentation set in `docs/`.

Only the C# implementation and its managed instruction assets are shipped in
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

For headless installation, pass the Telegram values explicitly or through
environment variables:

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
- stores them in `gopass`,
- installs the published Native AOT binary into a user-owned data directory,
- updates `~/.claude/settings.json` so VS Code loads the hook globally,
- installs a VS Code GitHub Copilot user instruction file under
  `~/.copilot/instructions` so task summaries are produced in every workspace.

The CLI also provides:

- `user uninstall`: remove the managed installation.
- `user health`: validate the current installation and credential resolution.
- `user diagnose`: print a detailed diagnostic report.
- `user test-notification`: send a test Telegram message without waiting for a
  Copilot stop event.
- `hook session-start`, `hook user-prompt-submit`, and `hook stop`: internal
  lifecycle entry points used by VS Code Copilot hooks.

## Logging and troubleshooting

The runtime now writes owner-only diagnostic logs where the operating system
supports Unix file modes:

- Hook logs for valid session-scoped events:
  `.copilot/sessions/<session_id>/hook.log`
- Fallback hook log for malformed hook payloads before a session context is
  usable: `.copilot/hook.log`
- User command log for `user install`, `user uninstall`, `user health`,
  `user diagnose`, and `user test-notification`:
  `<install-root>/user-command.log`

The session-scoped hook log keeps its diagnostics next to the corresponding
session metadata. The log captures hook lifecycle activity, state-file
operations, Telegram send attempts and retries, credential-resolution outcomes,
git probing, and related operational failures. The logger intentionally records
only this application's categories, so external HTTP/resilience components do
not write their request URIs into these files. Secrets such as the Telegram bot
token are intentionally redacted from the log.

Use `user diagnose` to print the current user-command log path, workspace
session log path pattern, and workspace fallback hook log path for the
directory where you run the command.

During runtime, the hook maintains session-scoped state under
`.copilot/sessions/<session_id>/`, including:

- `notify-session.json`: session metadata for the current Copilot session.
- `notify-turn.json`: the current turn identifier generated at
  `UserPromptSubmit`.
- `notify-summary.json`: the current turn's summary handoff file.
- `notify-last-sent.json`: best-effort duplicate-suppression state for the
  current session.
- `hook.log`: always-on diagnostic log for the current Copilot session.

These `.copilot/sessions/` files are runtime state and should remain ignored in
git.

The gopass prefix is fixed at `copilot/vscode-copilot-telegram-hook` so the
user-level installation and this repository's workspace hook resolve the same
secrets.
As of the current official VS Code docs, the user-level hook file location is
still `~/.claude/settings.json`, even when the feature is used from VS Code
GitHub Copilot.

The user-level instruction file, however, is installed in the GitHub
Copilot-specific `~/.copilot/instructions` location.
The runtime honors `TG_BOT_TOKEN` and `TG_CHAT_ID` from the process
environment as explicit overrides, but `gopass` is the primary persisted
mechanism for the managed user-level installation.

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
