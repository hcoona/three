<!--
  Copyright 2015 Shuai Zhang
  SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
-->

# hexo-renderer-asciidoc

[![CI](https://github.com/hcoona/three/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/hcoona/three/actions/workflows/ci.yml)
[![npm version](https://img.shields.io/npm/v/hexo-renderer-asciidoc.svg?logo=npm)](https://www.npmjs.com/package/hexo-renderer-asciidoc)
[![Node support](https://img.shields.io/node/v/hexo-renderer-asciidoc.svg?logo=node.js)](package.json)
[![License: LGPL-3.0-or-later](https://img.shields.io/badge/license-LGPL--3.0--or--later-0a76d5.svg)](LICENSE)

Add first-class [AsciiDoc](https://asciidoc.org/) support to Hexo. This repository branch documents the 4.x migration, which uses `@asciidoctor/core` / Asciidoctor.js 4.0.5, returns `Promise<string>`, registers the Hexo renderer asynchronously, re-highlights recognized listing blocks with fixed `hexo-util.highlight` options, and encodes literal braces before returning the resulting HTML to Hexo.

## Rendering contract

- `renderer(data)` returns `Promise<string>`. Programmatic callers must use `await renderer(...)`.
- Hexo registration is asynchronous for `.ad`, `.adoc`, and `.asciidoc`. Use `hexo.render.render(...)` or other async Hexo paths.
- `renderSync` is unsupported for AsciiDoc input. If you force a synchronous Hexo path, Hexo may leave the source unrendered instead of throwing.
- The renderer runs Asciidoctor with `doctype: 'article'`, `safe: 'server'`, and `to_file: false`.
- The renderer does **not** set `base_dir`, so includes resolve from the conversion-time `process.cwd()`. `RendererData.path` and the Hexo site root are not used as `base_dir`.
- This renderer is **not safe for untrusted AsciiDoc**. `safe: 'server'` still permits local includes under these current-working-directory semantics, and symlink targets can escape an assumed directory boundary. The current working directory is not a jail. Render only trusted input, from an isolated or sandboxed working directory that contains no secrets.
- After highlighting, the renderer globally encodes every literal `{` / `}` in the generated HTML as `&#123;` / `&#125;`. This prevents downstream Hexo tag or template interpretation where applicable. Browsers decode numeric character references in ordinary HTML text and attribute values for display or use, but HTML raw-text elements such as `<script>` and `<style>` do not decode them: the references remain literal source text and can alter or break embedded JavaScript or CSS. Raw HTML remains unsanitized, so this package is not suitable for untrusted input. An HTML sanitizer cannot prevent an AsciiDoc include from disclosing file contents.
- Static highlighting is limited to the direct `div.listingblock > div.content > pre > code` chain. The renderer does **not** rewrite arbitrary passthrough `<pre><code>` content.
- The package does not set `source-highlighter=html-pipeline`. With the public renderer's controlled `safe: 'server'` options, a source-defined document setting is ignored, so public rendering uses Asciidoctor's default output. The internal highlighter recognizes the supported default and html-pipeline marker shapes for compatibility, but this API exposes no way to configure arbitrary highlighter options.
- The highlighting bridge always uses fixed `hexo-util.highlight` options: `autoDetect: false`, `gutter: false`, and `wrap: false`.

## Requirements & installation

| Dependency | Minimum version |
| ---------- | --------------- |
| Node.js    | 22              |
| Hexo       | 8.0.0           |

Published-prerelease users should first confirm the published dist-tags:

```bash
npm view hexo-renderer-asciidoc dist-tags
```

When the output lists the explicitly published `beta` dist-tag, install it with
the package manager that matches the Hexo project:

```bash
npm install hexo-renderer-asciidoc@beta --save
# or
pnpm add hexo-renderer-asciidoc@beta
```

The unqualified package may still resolve to stable v3 until v4 becomes
`latest`; do not use it to test v4. Testers of an unpublished candidate must
use the checkout instructions and evidence from the authoritative migration PR
or prerelease announcement. That source must designate the immutable candidate
SHA and the acceptance evidence/check URLs. Do not infer a candidate from
default `main`, an arbitrary ref, or this README. This README intentionally
cannot designate or authenticate a future candidate.

Once installed, Hexo automatically pipes `.ad`, `.adoc`, and `.asciidoc` files through this renderer, no extra glue code is required.

### Minimal Hexo configuration

The renderer does not require renderer-specific `_config.yml` settings. A typical site still keeps Hexo’s normal highlight assets enabled:

```yml
highlight:
  enable: true
  line_number: false
  wrap: false
```

For AsciiDoc content, this package still uses its own static highlighting pass after conversion. It does not forward arbitrary Hexo highlight configuration; it always uses `autoDetect: false`, `gutter: false`, and `wrap: false` for recognized AsciiDoc listing blocks.

## Example Hexo site

A source-tree contributor fixture lives at `examples/hexo-site`. It links to the
local package via `link:../..` and is not included as a runnable site in the
installed package.

If you cloned the monorepo, the demo lives under `src/public/lib/hexo-renderer-asciidoc/examples/hexo-site` from the repository root.

For an already independently verified and trusted source checkout, follow
[`examples/hexo-site/README.md`](examples/hexo-site/README.md). Reviewers of an
unpublished candidate must use the checkout instructions and acceptance
evidence designated by the authoritative migration PR or prerelease
announcement, as described above.

> [!NOTE]
> The sample site is intentionally outside the root pnpm workspace. From the repository root, follow the exact `pnpm --dir ...` sequence in `examples/hexo-site/README.md`; it builds the parent package before installing and generating the example. After that setup, either keep using `pnpm --dir src/public/lib/hexo-renderer-asciidoc/examples/hexo-site ...` from the root or `cd` into that directory and run its scripts directly. Its `pnpm-workspace.yaml` and lockfile keep those dependencies isolated.

Browse `examples/hexo-site/source/` for the maintained posts and page that exercise headings, lists, a table of renderer defaults, and highlighted source listings. The broader constructs listed below are covered by doctests, not by this example site.

## Feature highlights

- **AsciiDoc parity.** Snapshot-style doctests (`test/doctest/*.test.ts`) mirror sections from the Asciidoctor user manual so features such as admonitions, description lists, colists, inline UI macros, and complex tables stay stable between releases.
- **Async Hexo integration.** `src/hexo/register.ts` wires the renderer for `.ad`, `.adoc`, and `.asciidoc` extensions with asynchronous semantics (`sync: false`) so Hexo awaits conversion.
- **Static highlighting bridge.** `src/core/highlight.ts` rewrites only recognized Asciidoctor listing blocks and re-renders them with `hexo-util.highlight`, preserving the package’s fixed `autoDetect: false`, `gutter: false`, and `wrap: false` options.
- **Brace encoding.** After highlighting, `src/core/sanitize.ts` globally encodes every literal `{` / `}` in the generated HTML as `&#123;` / `&#125;`. Browsers decode numeric character references in ordinary HTML text and attribute values, but HTML raw-text elements such as `<script>` and `<style>` do not; the literal references can alter or break embedded JavaScript or CSS. Raw HTML remains unsanitized, so this package is not suitable for untrusted input.
- **Tested in Hexo.** `test/hexo.integration.test.ts` spins up a real Hexo instance to guarantee the renderer continues to work with Hexo’s official API.

## Programmatic usage

The default export is the pure renderer function. You can reuse it in custom build scripts or other static-site setups:

ES modules:

```ts
import renderer from 'hexo-renderer-asciidoc';

const html = await renderer({ text: '== Custom pipeline ==' });
```

CommonJS returns a module namespace object; destructure its default export rather than calling the object:

```js
const { default: renderer, registerRenderer } = require('hexo-renderer-asciidoc');

async function main() {
  const html = await renderer({ text: '== Custom pipeline ==' });
  console.log(html);
}

main().catch(console.error);
```

The named `renderer` export is the same function as `default`; `registerRenderer` is available for explicit Hexo registration.

Hexo users rarely need this, but it is handy for local testing or for wiring the renderer into other tools. Because the renderer is asynchronous, `renderSync` is not a supported integration path.

## Development workflow

Run every command in this section from the repository root.

### Toolchain bootstrap

```bash
mise trust          # Trust this repository's mise configuration.
mise install        # Install pinned versions of Node, pnpm, hk, etc.
mise exec -- pnpm install
mise exec -- hk install
```

### Common scripts

| Command                                                                                       | Purpose                                                                              |
| --------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc run lint`                      | Run Biome lint and check Markdown formatting with Prettier.                          |
| `mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc run format`                    | Check Biome formatting, then write Markdown formatting with Prettier.                |
| `mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc exec biome format --write .`   | Apply Biome formatting fixes explicitly.                                             |
| `mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc run typecheck`                 | Run `tsgo --noEmit` for strict type safety.                                          |
| `mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc run test`                      | Execute the entire Vitest suite (including doctests + integration).                  |
| `mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc run test:watch`                | Rerun the impacted tests in watch mode.                                              |
| `mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc run test-cov`                  | Generate text + LCOV coverage via V8.                                                |
| `mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc run build`                     | Bundle `src/` with `tsdown` into the publishable `dist/`.                            |
| `mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc exec nbgv get-version -f json` | Read the package version calculated by NBGV.                                         |
| `mise exec -- hk check`                                                                       | Run the root-configured linters, format checks, typo checks, and config-sync checks. |

The install and HK commands above are repository-level operations; the remaining commands are package-scoped while still being invoked from the repository root. HK provides the repository hook/check gate for linters, formatters, typo checks, and configuration checks. Tests, type checks, builds, and package probes are separate commands and CI jobs.

From a clean checkout, build `dist/` successfully before creating a local package tarball:

```bash
mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc run build
mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc pack
```

`prepublishOnly` runs `pnpm run build` during publishing, but neither npm nor pnpm pack invokes `prepublishOnly`; a clean local pack therefore still requires the explicit build above. `prepack` prepares package metadata but does not build `dist/`, and `postpack` restores the placeholder version afterward.

### Source layout overview

- `src/core/` – Asciidoctor bootstrap, syntax highlighting bridge, and HTML safety helpers.
- `src/hexo/` – The Hexo adapter that registers the renderer for `.ad` / `.adoc` / `.asciidoc` files.
- `examples/hexo-site/` – Real Hexo project wired to the local package for smoke testing and documentation screenshots.
- `scripts/npm-readme.cjs` – Swaps `README.md` with `README.npm.md` during `npm pack` so the npm landing page gets the end-user guide without disturbing this contributor-focused README.

### Tests & fixtures

- Doctests live under `test/doctest/` and load fixture snippets from `test/doctest/examples/asciidoc/*.adoc` before snapshotting the rendered HTML.
- `test/highlight.test.ts` ensures the static highlighter decodes entities before passing code to Hexo.
- `test/hexo.integration.test.ts` boots Hexo and exercises the renderer via `hexo.render.render`.

Run all doctests while iterating on Asciidoctor upgrades:

```bash
mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc exec vitest run test/doctest
```

### Release guidance

1. Update `CHANGELOG.md` with the consumer-facing release notes.
2. Manage the release line with `version.json`, not `package.json`. Keep `package.json` checked in as `0.0.0-placeholder`.
3. From the repository root, check out the target commit and run `mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc exec nbgv get-version -f json`. Use its `NpmPackageVersion` value as the release version; any manual workflow version input must exactly equal it.
4. Use the root workflow entrypoints `.github/workflows/official.yml` or `.github/workflows/buddy.yml` for release orchestration.
5. Do **not** manually bump `package.json` or run package-local `pnpm publish`. For a normal local `pnpm pack`, first run the explicit package build shown above; packing does not build `dist/`. The pack lifecycle then has `prepack` temporarily stamp the NBGV version and `postpack` run `version:reset` to restore `0.0.0-placeholder`.
6. The real release-build workflow has different lifecycle behavior in its ephemeral checkout: it manually runs `prepack` once, then official/public `both` mode packs the GPR tarball followed by the npmjs tarball with two `npm pack --ignore-scripts` commands, while Buddy `gpr-only` mode runs one such command and produces only the GPR tarball. Neither mode invokes `postpack` or `version:reset`, and neither needs to reset its disposable checkout. The local validation harness models official/public `both` mode and separately runs cleanup because it reuses its checkout. Do not change or bypass this workflow.

## Continuous integration

- The monorepo CI runs HK validation in its validation job. Separate Node matrix jobs run type checks, Vitest, the production build, and packed-artifact validation on Node 22.x / 24.x: https://github.com/hcoona/three/actions/workflows/ci.yml
- CodeQL scanning lives in the monorepo as well: https://github.com/hcoona/three/actions/workflows/codeql.yml

## License

Licensed under **LGPL-3.0-or-later WITH LGPL-3.0-linking-exception**. See `COPYING`, `COPYING.LESSER`, and `LICENSES/` for the full texts. Commercial redistribution requires compliance with the linking exception.
