# VS Code Copilot Telegram Hook

This app is the source of truth for the repository's VS Code GitHub Copilot
Telegram notification hooks.

It serves two use cases:

1. The repository-local hook entry in `.github/hooks/telegram-notify.json`, used only for pre-release testing in this repository.
2. A user-level installation that applies to any workspace in VS Code and represents the formally supported product use case.

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

- `docs/README.md`: tracks human-authored source inputs and derivation
  relationships for the documentation set in `docs/`.
- `scripts/copilot-summary-state.ps1`: initializes `.copilot/notify-session.json`
  and `.copilot/notify-summary.json` for the current workspace.
- `scripts/telegram-notify.ps1`: sends Telegram notifications for completed
  Copilot tasks.
- `Install-UserCopilotHook.ps1`: installs the scripts into the current user's
  machine-level VS Code Copilot hook configuration.

## User-level installation

Run the installer from the repository root:

```powershell
pwsh -NoLogo -NoProfile -File ./src/private/app/vscode-copilot-telegram-hook/Install-UserCopilotHook.ps1
```

For headless installation, pass the Telegram values explicitly or through
environment variables:

```powershell
pwsh -NoLogo -NoProfile -NonInteractive `
  -File ./src/private/app/vscode-copilot-telegram-hook/Install-UserCopilotHook.ps1 `
  -TelegramBotToken '<bot-token>' `
  -TelegramChatId '<chat-id>' `
  -SkipSecretPrompt
```

The installer:

- prompts for the Telegram bot token and chat ID,
- accepts `TG_BOT_TOKEN` and `TG_CHAT_ID` as non-interactive input sources,
- stores them in `gopass`,
- installs the hook scripts into a user-owned data directory,
- updates `~/.claude/settings.json` so VS Code loads the hook globally,
- installs a VS Code GitHub Copilot user instruction file under
  `~/.copilot/instructions` so task summaries are produced in every workspace.

Supported install modes:

- `Auto`: try copy-on-write first when available, then hardlink, then copy.
- `Cow`: require a reflink-style copy on Linux or WSL.
- `Hardlink`: require hardlinks.
- `Copy`: always create independent copies.

The gopass prefix is fixed at `copilot/vscode-copilot-telegram-hook` so the
user-level installation and this repository's workspace hook resolve the same
secrets.
As of the current official VS Code docs, the user-level hook file location is
still `~/.claude/settings.json`, even when the feature is used from VS Code
GitHub Copilot.

The user-level instruction file, however, is installed in the GitHub
Copilot-specific `~/.copilot/instructions` location.
The runtime honors `TG_BOT_TOKEN` and `TG_CHAT_ID` from the process
environment as explicit overrides, but `gopass` is the primary mechanism.

## Requirements and documentation map

This README is a project entry point, not the authoritative requirements
ledger. The detailed requirement, research, and provenance material lives under
[`docs/`](./docs/).

Use these documents as the authoritative sources:

- [`docs/README.md`](./docs/README.md): provenance ledger and derivation map.
- [`docs/h-001-original-requirement-brief.md`](./docs/h-001-original-requirement-brief.md): original human-authored requirement brief and reference set.
- [`docs/h-002-human-confirmation-2026-03-13.md`](./docs/h-002-human-confirmation-2026-03-13.md): later human confirmation of product decisions.
- [`docs/functional-requirements.md`](./docs/functional-requirements.md): current derived functional specification.
- [`docs/nonfunctional-and-constraints-research.md`](./docs/nonfunctional-and-constraints-research.md): current non-functional requirements and external constraints research.
- [`docs/vscode-hook-inputs-research.md`](./docs/vscode-hook-inputs-research.md): project-focused analysis of the VS Code hook input contract.

In short, the current product target is a user-level VS Code GitHub Copilot
hook that attempts Telegram delivery for each completed-turn `Stop` event and
includes a concise Chinese summary when available.
