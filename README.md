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

| Project                      | Directory                       | Upstream                     | Commit                        |
| ---------------------------- | ------------------------------- | ---------------------------- | ----------------------------- |
| Asciidoctor LaTeXMath        | `asciidoctor-latexmath/`        | [Repo][asciidoctor-upstream] | [514d685][asciidoctor-commit] |
| ImageOcclusionEditor         | `ImageOcclusionEditor/`         | [Repo][ioe-upstream]         | [e08f834][ioe-commit]         |
| OneDotNet                    | `OneDotNet/`                    | [Repo][onedotnet-upstream]   | [17f2224][onedotnet-commit]   |
| OnePython                    | `OnePython/`                    | [Repo][onepython-upstream]   | [21ef6d5][onepython-commit]   |
| Steam Account History to CSV | `steam-account-history-to-csv/` | [Repo][steamhist-upstream]   | [b759a52][steamhist-commit]   |
| Hexo Renderer AsciiDoc       | `hexo-renderer-asciidoc/`       | [Repo][hexo-upstream]        | [d98f8d5][hexo-commit]        |

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
