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

This repo uses the Visual Studio Code GitHub Copilot agent hooks preview under `.github/hooks/telegram-notify.json`.
The hook file lives in the official workspace location `.github/hooks/*.json`, which VS Code loads automatically after the file is saved.
The hooks call `.github/hooks/scripts/copilot-summary-state.ps1` and `.github/hooks/scripts/telegram-notify.ps1` with `pwsh`, so the same setup works in Windows and WSL as long as the repository toolchain is bootstrapped with Mise.

The current configuration uses only the VS Code hook events that this repo actually relies on:

- `SessionStart` initializes `.copilot/notify-session.json` and the placeholder `.copilot/notify-summary.json`
- `Stop` sends the Telegram notification for the latest completed summary snapshot

There are no extra compatibility branches for older Copilot CLI event names in this repository.

A chat session can still contain multiple prompts. To avoid losing notifications for later completed tasks in the same session, the Telegram hook records both the stable session `run_id` and the latest `.copilot/notify-summary.json` `updated_at` value.
That means a new completed task in the same session still produces a new Telegram message, while an identical replay of the same `Stop` payload is ignored.

Each notification includes a self-describing `run_id` stored in `.copilot/notify-session.json`, plus the VS Code `sessionId` when available, so concurrent worktrees and machines stay easy to tell apart.
For GitHub remotes, the repo field is displayed as `owner/repo` (for example, `hcoona/three`). If the remote URL does not match the GitHub patterns, the script falls back to the local repository folder name.

To enable notifications:

1. Create a bot with `@BotFather`.
2. Send `/start` to the bot once from the target chat.
3. Fill in `TG_BOT_TOKEN` and `TG_CHAT_ID` in the local `.env` file at the repo root.

The PowerShell script loads the repo-root `.env` file automatically when those environment variables are not already present in the current process.
If behavior seems stale in an already-running session, reloading the VS Code window is still a reasonable fallback.

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
