# Three

My polyglot monorepo. It keeps the projects I rely on most under a single roof so I can share tooling, manage dependencies consistently, and clone everything in one go.

## Documentation and contribution

Start with the [documentation portal](docs/README.md) for repository and project
records, and [CONTRIBUTING.md](CONTRIBUTING.md) for contribution and review.
The [Delivery Wave](docs/delivery-wave.md) identifies current authorized work.

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

## Repository toolchain

Use the [workspace and toolchain setup guide](docs/engineering/workspaces.md)
for pnpm, uv, .NET, mise and HK setup. Workspace manifests remain the source of
build membership and package configuration.

## C# analyzer and compatibility exception inventory

See the [shared compatibility rationale](docs/engineering/dotnet-compatibility.md)
for existing analyzer exceptions, build-property ownership and their consumers.

## GitHub Copilot Telegram notifications

The [Telegram notification project](src/private/app/vscode-copilot-telegram-hook/README.md)
owns supported host integration, installation, secret storage, session identity
and notification behavior. Use its project documentation for current instructions.

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
