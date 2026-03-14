# Three

My polyglot monorepo. It keeps the projects I rely on most under a single roof so I can share tooling, manage dependencies consistently, and clone everything in one go.

## Why this repo exists

Maintaining several language stacks across scattered repositories was slowing me down. Consolidating them here lets me:

- reuse the same CI, release, and security policies;
- version cross-cutting assets together;
- archive upstream code (including Git LFS objects) so future clones depend on this repo only.

## What’s with the name?

The first two pillars were **OnePython** and **OneDotNet**. Adding the rest of my “other” projects made it a trio, hence **Three = OnePython + OneDotNet + Others**. In Daoist philosophy, “三生万物” (“Three begets all things”) symbolizes how diversity emerges from a balanced trio—exactly how this monorepo grows.

## Projects included

| Project                      | Directory                                      | Upstream                     | Commit                        |
| ---------------------------- | ---------------------------------------------- | ---------------------------- | ----------------------------- |
| Asciidoctor LaTeXMath        | `src/public/lib/asciidoctor-latexmath/`        | [Repo][asciidoctor-upstream] | [514d685][asciidoctor-commit] |
| ImageOcclusionEditor         | `src/public/app/ImageOcclusionEditor/`         | [Repo][ioe-upstream]         | [e08f834][ioe-commit]         |
| OneDotNet                    | `OneDotNet/`                                   | [Repo][onedotnet-upstream]   | [17f2224][onedotnet-commit]   |
| OnePython                    | `OnePython/`                                   | [Repo][onepython-upstream]   | [21ef6d5][onepython-commit]   |
| Steam Account History to CSV | `src/public/lib/steam-account-history-to-csv/` | [Repo][steamhist-upstream]   | [b759a52][steamhist-commit]   |
| Hexo Renderer AsciiDoc       | `src/public/lib/hexo-renderer-asciidoc/`       | [Repo][hexo-upstream]        | [d98f8d5][hexo-commit]        |

## JavaScript/pnpm workspace

The `src/public/lib/hexo-renderer-asciidoc/` and `src/public/lib/steam-account-history-to-csv/` folders share a [pnpm workspace](https://pnpm.io/workspaces) that still lives at the repo root even though the projects moved under `src/public/lib/`. The nested layout keeps the repo top level tidy while preserving predictable dependency resolution (`sharedWorkspaceLockfile: true`) and automatic linking between workspace packages (`linkWorkspacePackages: true`). As before, the workspace does not pin a Node version—each package’s own `engines` entry (Hexo still wants Node ≥ 20.19) remains authoritative.

Development flow:

1. Enable Corepack (once per machine) so the `packageManager` setting can download pnpm for you.
2. From the repo root, run `pnpm install` to hydrate every workspace project and refresh the single `pnpm-lock.yaml`.
3. Use the root scripts from `package.json`:
    - `pnpm run build` → runs `build` in every workspace package.
    - `pnpm run test` / `pnpm run lint` / `pnpm run format` → fan out with `--if-present`, so packages missing a script are skipped.
4. When pnpm warns about blocked install scripts (for example `hexo-util`), review and allow them with `pnpm approve-builds` to stay compliant with pnpm 10’s hardened defaults.

For publishing/versioning, follow pnpm’s [Changesets guide](https://pnpm.io/using-changesets) so both packages can share a single release workflow.

## GitHub Copilot Telegram notifications

This repo keeps the Copilot Telegram hook implementation in `src/private/app/vscode-copilot-telegram-hook`.
The workspace hook entry remains at `.github/hooks/telegram-notify.json`, which is the official workspace location that VS Code loads automatically.
That workspace file now calls the shared C# CLI entry points under `src/private/app/vscode-copilot-telegram-hook/` directly.

The implementation uses the VS Code Copilot hook events that this repo actually needs:

- `SessionStart` initializes session-scoped hook metadata and injects summary-handoff context
- `UserPromptSubmit` starts a new turn and refreshes the placeholder summary state
- `Stop` sends the Telegram notification for the matching session-and-turn summary snapshot

Per the official VS Code hooks documentation, user-level hooks are loaded from `~/.claude/settings.json` by default, and workspace hooks take precedence over user hooks for the same event.
That is why this repository keeps its own workspace hook entry while the installer also supports a machine-level user hook for other workspaces.

The user-level installer is the C# CLI. Publish the app and run:

```bash
app=./src/private/app/vscode-copilot-telegram-hook
binary="$app/bin/Release/net10.0/linux-x64/publish/vscode-copilot-telegram-hook"

"$binary" user install --binary-path "$binary"
```

For headless installation, pass `--telegram-bot-token` and `--telegram-chat-id` (or set `TG_BOT_TOKEN` and `TG_CHAT_ID`) together with `--skip-secret-prompt`.

The installer prompts for the Telegram bot token and chat ID when run interactively, validates their basic format, stores them in `gopass`, installs the published binary into a stable user-owned directory, updates `~/.claude/settings.json` so VS Code loads the user-level hooks globally, and installs a user-level VS Code GitHub Copilot instruction file under `~/.copilot/instructions` so task summaries are generated consistently.
The `gopass` prefix is fixed at `copilot/vscode-copilot-telegram-hook` so the user-level installation and this repository's workspace hook stay aligned.
As of the current official VS Code docs, the hook location is still the compatibility path `~/.claude/settings.json`, while the instruction file is installed in the GitHub Copilot-specific `~/.copilot/instructions` location.

At runtime, the hook resolves the active workspace from the hook input payload instead of the binary location.
That keeps `.copilot/sessions/<session_id>/notify-session.json`, `notify-turn.json`, `notify-summary.json`, and `notify-last-sent.json` scoped to the actual workspace even after a user-level installation.

A chat session can still contain multiple prompts. To avoid stale-summary reuse across turns, the Telegram hook correlates the official `sessionId` with a repository-defined `turn_id` created at `UserPromptSubmit`.
That means a later completed task in the same session still produces a new Telegram message, while an identical replay of the same `Stop` payload is ignored.

Each notification includes the VS Code `sessionId` together with the internal `turn_id`, so concurrent worktrees, sessions, and machines stay easy to tell apart.
For GitHub remotes, the repo field is displayed as `owner/repo` (for example, `hcoona/three`). If the remote URL does not match the GitHub patterns, the script falls back to the local repository folder name.

The runtime still honors `TG_BOT_TOKEN` and `TG_CHAT_ID` from the process environment as explicit overrides, but the default installation path uses `gopass`.

[asciidoctor-upstream]: https://github.com/hcoona/asciidoctor-latexmath
[asciidoctor-commit]: https://github.com/hcoona/asciidoctor-latexmath/commit/514d685558dc1c8215d0b1e42ff5ea2762ecd3b2
[ioe-upstream]: https://github.com/hcoona/ImageOcclusionEditor
[ioe-commit]: https://github.com/hcoona/ImageOcclusionEditor/commit/e08f8348e58b83d04801212e55bace30a9126072
[onedotnet-upstream]: https://dev.azure.com/zhangshuai89/Public/_git/OneDotNet
[onedotnet-commit]: https://dev.azure.com/zhangshuai89/Public/_git/OneDotNet/commit/17f2224ab5f25c2149f7d5e9fd184c632afa0c3a
[onepython-upstream]: https://dev.azure.com/zhangshuai89/Public/_git/OnePython
[onepython-commit]: https://dev.azure.com/zhangshuai89/Public/_git/OnePython/commit/21ef6d519c5d35ac1c2e0694dd11f3c256c68756
[steamhist-upstream]: https://github.com/hcoona/steam-account-history-to-csv
[steamhist-commit]: https://github.com/hcoona/steam-account-history-to-csv/commit/b759a520b2d1edf8560a45cbcb70b403c77cecd1
[hexo-upstream]: https://github.com/hcoona/hexo-renderer-asciidoc
[hexo-commit]: https://github.com/hcoona/hexo-renderer-asciidoc/commit/d98f8d5461c37db229d05a2b32d5aa8e122ec423

Each subtree was imported with `git subtree add --squash`, so future pulls can use `git subtree pull --prefix=<dir> <remote> main --squash` to stay in sync.
