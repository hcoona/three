# Three

My polyglot monorepo. It keeps the projects I rely on most under a single roof so I can share tooling, manage dependencies consistently, and clone everything in one go.

## Why this repo exists

Maintaining several language stacks across scattered repositories was slowing me down. Consolidating them here lets me:

- reuse the same CI, release, and security policies;
- version cross-cutting assets together;
- archive upstream code (including Git LFS objects) so future clones depend on this repo only.

## What’s with the name?

The name reflects the repo's original consolidation of **OnePython**, **OneDotNet**, and a growing set of other projects. Those historical roots still explain the branding, but they no longer map 1:1 to today's top-level directories.

## Canonical repository layout

The current repository contract is:

- `src/` contains active monorepo projects, organized under `lab/`, `private/`, `public/`, and `sample/`. The migrated OneDotNet public C# projects now live under `src/public/`.
- `tests/` contains matching test projects for code that follows the root monorepo layout, including the migrated OneDotNet public C# tests under `tests/public/`.
- The active C# code migrated out of the historical `OneDotNet/` subtree now lives under the canonical root `src/`, `tests/`, and `src/lab/` layout.
- There is no top-level `OnePython/` directory in this repository. Python workspace members now live under `src/` and are declared in the root `pyproject.toml`.

## Projects included

| Project                       | Directory                                      | Upstream                     | Commit                        |
| ----------------------------- | ---------------------------------------------- | ---------------------------- | ----------------------------- |
| Asciidoctor LaTeXMath         | `src/public/lib/asciidoctor-latexmath/`        | [Repo][asciidoctor-upstream] | [514d685][asciidoctor-commit] |
| ImageOcclusionEditor          | `src/public/app/ImageOcclusionEditor/`         | [Repo][ioe-upstream]         | [e08f834][ioe-commit]         |
| OneDotNet (historical import) | git history only                               | [Repo][onedotnet-upstream]   | [17f2224][onedotnet-commit]   |
| Steam Account History to CSV  | `src/public/lib/steam-account-history-to-csv/` | [Repo][steamhist-upstream]   | [b759a52][steamhist-commit]   |
| Hexo Renderer AsciiDoc        | `src/public/lib/hexo-renderer-asciidoc/`       | [Repo][hexo-upstream]        | [d98f8d5][hexo-commit]        |

## JavaScript/pnpm workspace

The root [pnpm workspace](https://pnpm.io/workspaces) currently includes:

- `src/public/lib/hexo-renderer-asciidoc/`
- `src/public/lib/steam-account-history-to-csv/`
- `src/private/app/im-acp-gateway/poc/*`

The workspace stays at the repo root so these packages can share one lockfile (`sharedWorkspaceLockfile: true`) and workspace linking (`linkWorkspacePackages: true`) even though they live under different subtrees.

Development flow:

1. Install a compatible Node.js and pnpm toolchain. `mise.toml` is the repo-wide source of tool version intent; otherwise use a local toolchain that satisfies each package's `engines` constraints.
2. From the repo root, run `pnpm install` to hydrate every workspace project and refresh the single `pnpm-lock.yaml`.
3. Use the root scripts from `package.json`:
    - `pnpm run build` → runs `build` in every workspace package.
    - `pnpm run test` / `pnpm run lint` / `pnpm run format` → fan out with `--if-present`, so packages missing a script are skipped.
4. When pnpm warns about blocked install scripts (for example `hexo-util`), review and allow them with `pnpm approve-builds` to stay compliant with pnpm 10’s hardened defaults.

## Python/uv workspace

The root `pyproject.toml` is the source of truth for the repository's Python workspace membership. The current workspace is rooted under `src/`; there is no separate top-level `OnePython/` tree in this checkout.

## .NET traversal

The root `dirs.proj` is now the only active C# traversal for this repository.
It treats `src/` and `tests/` as the canonical build roots, including the migrated OneDotNet public, private, and lab C# slices now rooted under `src/`, `src/lab/`, and `tests/`.

## C# analyzer and compatibility exception inventory

The normalization cleanup restores `StyleCop.Analyzers` for the migrated public libraries under `src/public/lib/` through `src/public/lib/Directory.Build.props` and the shared root `stylecop.json`.
The public test tree under `tests/public/lib/` is intentionally still outside that restored StyleCop scope for now, because turning it back on cleanly would require broader legacy test-source cleanup than this normalization follow-up owns.

### Pre-existing before the root-layout migration

| Scope                                                     | Exact files/settings                                                                                                                                                                                                       | Rationale                                                                                                      | Expected exit path                                                                                                                          |
| --------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| `Hjg.Pngcs` library                                       | `src/public/lib/Hjg.Pngcs/Hjg.Pngcs.csproj`: `AnalysisMode=Minimum`, `NoWarn+=CS1591`, `GlobalPackageReference Remove="StyleCop.Analyzers"`                                                                                | Upstream-style maintenance code was already being kept on a lighter analyzer profile before the monorepo move. | Re-enable the default analyzer profile and StyleCop only after a dedicated cleanup pass over the imported source and documentation surface. |
| `Hjg.Pngcs` manual tests                                  | `tests/public/lib/Hjg.Pngcs.ManualTests/Hjg.Pngcs.ManualTests.csproj`: `AnalysisMode=Minimum`, `GlobalPackageReference Remove="StyleCop.Analyzers"`                                                                        | The manual/sample test app already carried the same maintenance-project downgrade before migration.            | Either normalize the sample sources and re-enable the defaults, or retire the manual test app if it stops being actively used.              |
| `Hjg.Pngcs` package validation                            | `src/public/lib/Hjg.Pngcs/Hjg.Pngcs.csproj`: `EnablePackageValidation=true`, `PackageValidationBaselineName=Hjg.Pngcs`, `PackageValidationBaselineVersion=1.1.4`; `src/public/lib/Hjg.Pngcs/CompatibilitySuppressions.xml` | The package was already preserving compatibility against the previously shipped `Hjg.Pngcs` surface.           | Refresh or remove the baseline only as part of an intentional compatibility-change release.                                                 |
| `Memoization` package validation                          | `src/public/lib/Memoization/Memoization.csproj`: `EnablePackageValidation=true`, `PackageValidationBaselineVersion=1.0.0`                                                                                                  | This baseline also predates the migration and protects an already-published package contract.                  | Refresh the baseline when the next planned compatibility boundary changes.                                                                  |
| `DedupChangeExtensions` compatibility warning suppression | `src/public/lib/MicrosoftExtensions.Options.DedupChangeExtensions/MicrosoftExtensions.Options.DedupChangeExtensions.csproj`: `NoWarn+=SYSLIB0011` on `net8.0`/`net9.0`                                                     | The package still carries BinaryFormatter-based compatibility code for those TFMs.                             | Remove the suppression once the legacy serialization path is replaced.                                                                      |
| Public test analyzer suppressions                         | `tests/public/Directory.Build.props`: `NoWarn+=CA1848;CA1707`; `tests/public/lib/MicrosoftExtensions.Logging.Xunit.v2.Tests/MicrosoftExtensions.Logging.Xunit.v2.Tests.csproj`: `MSTestAnalysisMode=None`                  | These test-tree relaxations already existed before the root-layout move.                                       | Remove them when the affected tests are updated to the desired logging/naming/test-analyzer posture.                                        |

### Introduced or expanded by the migration and follow-up cleanup

| Scope                           | Representative scopes/settings                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | Rationale                                                                                                                                                                                                                                                         | Expected exit path                                                                                                                            |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| Public test StyleCop scope      | `tests/public/Directory.Build.props` still does **not** add `StyleCop.Analyzers` after the migration, even though the old `OneDotNet` tree inherited it                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         | Re-enabling StyleCop for the full migrated public test tree currently surfaces broader legacy issues (for example `tests/public/lib/WebHdfs.Extensions.FileProviders.UnitTest/*` file headers, using ordering, and `SA1101`) that are outside this cleanup slice. | Land a follow-up that normalizes the migrated public test sources, then restore `StyleCop.Analyzers` at `tests/public/Directory.Build.props`. |
| `Memoization.Generators` SA0001 | `src/public/lib/Memoization.Generators/Memoization.Generators.csproj`: `NoWarn+=SA0001`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         | The migrated source-generator project currently needs a local `SA0001` carve-out so the repo-wide StyleCop restore can stay enabled without blocking on generator-specific analyzer configuration follow-up.                                                      | Remove the suppression once a dedicated follow-up confirms the generator project passes the shared StyleCop configuration without `SA0001`.   |
| EditorConfig checker exclusions | `.editorconfig-checker.json`: summarized migrated-root exclusion scopes including `src/public/lib/Hjg.Pngcs/**`, `src/public/lib/Memoization/_Memoization.cs`, `src/public/lib/Memoization.Generators/MemoizationGenerator.cs`, `src/public/lib/MicrosoftExtensions.Logging.MSTest/*`, `src/public/lib/MicrosoftExtensions.Options.DedupChangeExtensions/OptionsDedupChangeExtensions.cs`, `src/public/lib/PhiFailureDetector/*`, `src/public/lib/WebHdfs.Extensions.FileProviders/**`, `tests/public/lib/Hjg.Pngcs.ManualTests/**`, `tests/public/lib/Memoization.Tests/TestMemoization.cs`, `tests/public/lib/MicrosoftExtensions.Logging.Xunit.v2.Tests/UnitTest1.cs`, `tests/public/lib/PhiFailureDetector.UnitTest/PhiFailureDetector.UnitTest.csproj`, and `tests/public/lib/WebHdfs.Extensions.FileProviders.UnitTest/*` | These exclusions were added after migration so the repo-level checker could tolerate imported legacy formatting and header differences without blocking the broader root-layout cleanup.                                                                          | Remove the exclude entries incrementally as each affected file set is normalized to the root repo conventions.                                |
| Typos exclusions                | `.typos.toml`: explicit file-level excludes for the noisiest imported files under `src/public/lib/Hjg.Pngcs/`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | The migrated PNG/zlib code currently produces too much spell-check noise for the repo-wide `typos` step, but only in a subset of imported files.                                                                                                                  | Continue shrinking the file-level list or replacing entries with targeted dictionary additions as each noisy imported file is reviewed.       |

### Final `Directory.Build.props` ownership model

After Groups N1/N2/N3 plus the corrected low-risk `GitVersionBaseDirectory` follow-up, the live props graph is now override-oriented: the root `Directory.Build.props` owns the shared analyzer baseline, while child `Directory.Build.props` files only keep subtree-local deltas and the minimal repo-root version-base reuse/reset points still needed to preserve local `version.json` boundaries.

| Owner scope            | Live owner file                         | Shared settings/packages still owned there                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | Why it remains there                                                                                                                                                                                         |
| ---------------------- | --------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Repo-wide baseline     | `Directory.Build.props`                 | `AnalysisLevel=latest`, `AnalysisMode=Recommended`, `TreatWarningsAsErrors=true`, `CodeAnalysisTreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`; `GlobalPackageReference Include="DotNet.ReproducibleBuilds" Version="2.0.2"`; `GlobalPackageReference Include="Microsoft.SourceLink.GitHub" Version="8.0.0"`; `GlobalPackageReference Include="Nerdbank.GitVersioning" Version="3.9.50"`; `Authors=Shuai Zhang`, `Company=Shuai Zhang`, `Copyright=Copyright (c) Shuai Zhang 2025`, `PackageLicenseExpression=LGPL-3.0-or-later WITH LGPL-3.0-linking-exception`, `PublishRepositoryUrl=true`, `IncludeSymbols=true`, `DebugType=portable`, `SymbolPackageFormat=snupkg` | These settings now apply once for the whole C# repo and are no longer repeated in child props files.                                                                                                         |
| Source subtree routing | `src/Directory.Build.props`             | `GitVersionBaseDirectory=$(RepoRoot)` for `src/**`; `IsPackable=true` for `src/public/lib/**` and `src/private/lib/**`; `IsPackable=false` for `src/public/app/**`, `src/private/app/**`, `src/lab/**`, and `src/sample/**`                                                                                                                                                                                                                                                                                                                                                                                                                                                          | Source-subtree packability is routed centrally here, while deeper subtree overrides only need to re-scope versioning where the public tree diverges from the repo root.                                      |
| Public subtree         | `src/public/Directory.Build.props`      | `GitVersionBaseDirectory=$(MSBuildProjectDirectory)` reset for `src/public/**` projects that have a local `version.json`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             | Public projects with a local `version.json` still need project-dir version-base semantics so their local version boundaries win, while projects without a local file continue to fall back to the repo root. |
| Private app subtree    | `src/private/app/Directory.Build.props` | `JsonSerializerIsReflectionEnabledByDefault=false`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | Private apps still inherit the shared repo-root version base from `src/Directory.Build.props`, while the serializer setting remains private-app-specific.                                                    |
| Public library subtree | `src/public/lib/Directory.Build.props`  | `CentralPackageTransitivePinningEnabled=false`; `IsAotCompatible` helper; `StyleCop.Analyzers`; linked `stylecop.json`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               | Public libraries still keep their library-specific packaging/analyzer settings while inheriting the shared public-tree version-base override.                                                                |
| Test baseline          | `tests/Directory.Build.props`           | `GitVersionBaseDirectory=$(RepoRoot)` for `tests/**`; `IsPackable=false`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             | The entire `tests` subtree still shares the root `version.json` today, so tests can reuse one repo-root version calculation without any lower reset.                                                         |
| Public test exceptions | `tests/public/Directory.Build.props`    | `MSTestAnalysisMode=Recommended`; `NoWarn+=CA1848;CA1707`; package-version override/test-runner toggles                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              | These are intentional public-test-only exceptions that remain local after normalization.                                                                                                                     |

Within the migrated public/test inventory above, the intentionally remaining local exceptions after normalization are unchanged:

- `Hjg.Pngcs` and `Hjg.Pngcs.ManualTests` still keep their project-local `AnalysisMode=Minimum` and `StyleCop.Analyzers` removals.
- `Memoization.Generators` still keeps `NoWarn+=SA0001`.
- `MicrosoftExtensions.Options.DedupChangeExtensions` still keeps `NoWarn+=SYSLIB0011` on `net8.0`/`net9.0`.
- The public test tree still keeps its local analyzer suppressions and remains outside the restored `StyleCop.Analyzers` scope.
- Package-validation baselines and the tracked `.editorconfig-checker.json` / `.typos.toml` carve-outs remain local until their dedicated cleanup work lands.

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

The installer prompts for the Telegram bot token and chat ID when run interactively, validates their basic format, stores them in `gopass`, validates before side effects that the managed hook file path stays representable as a supported `~/...` VS Code hook location, installs the published binary into a stable user-owned directory, writes a dedicated managed hook JSON file, registers that managed hook file in the same host's supported VS Code settings targets through `chat.hookFilesLocations`, and installs a user-level VS Code GitHub Copilot instruction file under `~/.copilot/instructions` so task summaries are generated consistently.
The `gopass` prefix is fixed at `copilot/vscode-copilot-telegram-hook` so the user-level installation and this repository's workspace hook stay aligned.
The managed installation intentionally avoids relying on `~/.claude/settings.json` as its steady-state target. On Linux, the default settings targets are `~/.config/Code/User/settings.json` and `~/.vscode-server/data/Machine/settings.json`; the managed hook file is registered there in supported `~/...` form rather than as an absolute path, even when the VS Code Server settings file does not exist yet.

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
[steamhist-upstream]: https://github.com/hcoona/steam-account-history-to-csv
[steamhist-commit]: https://github.com/hcoona/steam-account-history-to-csv/commit/b759a520b2d1edf8560a45cbcb70b403c77cecd1
[hexo-upstream]: https://github.com/hcoona/hexo-renderer-asciidoc
[hexo-commit]: https://github.com/hcoona/hexo-renderer-asciidoc/commit/d98f8d5461c37db229d05a2b32d5aa8e122ec423

Each subtree was imported with `git subtree add --squash`, so future pulls can use `git subtree pull --prefix=<dir> <remote> main --squash` to stay in sync.
