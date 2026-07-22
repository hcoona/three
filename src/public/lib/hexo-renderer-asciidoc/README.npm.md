<!--
  Copyright 2015 Shuai Zhang
  SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
-->

# hexo-renderer-asciidoc

Add native [AsciiDoc](https://asciidoc.org/) rendering to Hexo using `@asciidoctor/core` / Asciidoctor.js 4.0.4. The plugin auto-registers asynchronous renderers for `.ad`, `.adoc`, and `.asciidoc`, re-highlights recognized AsciiDoc listing blocks with Hexo-compatible markup, and encodes literal `{` / `}` before returning HTML to Hexo.

## Requirements

- Node.js 20.19.0 or newer (matches Hexo 8’s baseline)
- Hexo 8.0.0 or newer

## Installation

This repository branch documents the upcoming 4.x beta migration. As of July 22,
2026, npm publishes only stable v3 on the `latest` dist-tag; no v4 prerelease or
`beta` dist-tag is available yet. Do not install the unqualified package to test
these v4 behaviors, because it resolves to v3. Before publication, contributors
and testers must use the clean repository checkout, build, and local-link example
workflow below.

Once a v4 prerelease is published under the `beta` dist-tag, consumers will
install it with:

```bash
npm install hexo-renderer-asciidoc@beta --save
# or
pnpm add hexo-renderer-asciidoc@beta
```

After installing, Hexo picks up the renderer automatically. There is nothing to configure or register manually.

## Usage

1. Drop AsciiDoc sources (e.g., `hello-world.adoc`) anywhere under your Hexo site’s `source/` directory.
2. Run `hexo generate`, `hexo server`, or any other async Hexo command as usual.
3. If you call the renderer yourself, `await` it:

   ES modules:

   ```ts
   import renderer from 'hexo-renderer-asciidoc';

   const html = await renderer({ text: '== Hello from AsciiDoc ==' });
   ```

   CommonJS returns a module namespace object, so destructure its default export:

   ```js
   const { default: renderer, registerRenderer } = require('hexo-renderer-asciidoc');

   async function main() {
     const html = await renderer({ text: '== Hello from AsciiDoc ==' });
     console.log(html);
   }

   main().catch(console.error);
   ```

   The named `renderer` export is the same function as `default`; `registerRenderer` supports explicit Hexo registration.

The renderer does not add renderer-specific `_config.yml` sections. Feature toggles such as admonitions, callouts, or TOCs are handled directly by standard AsciiDoc attributes in your documents.

> [!IMPORTANT]
> `renderer(...)` returns `Promise<string>`. Hexo async render paths work normally, but `renderSync` is unsupported for AsciiDoc input. If you force a synchronous path, Hexo may leave the source unrendered instead of throwing.

### Example site

Need a ready-made playground? Clone the GitHub repository and open `examples/hexo-site`:

```bash
git clone https://github.com/hcoona/three.git
cd three
mise trust
mise exec -- pnpm install --frozen-lockfile
mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc run build
mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc/examples/hexo-site install --frozen-lockfile
mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc/examples/hexo-site run generate
cd src/public/lib/hexo-renderer-asciidoc/examples/hexo-site
pnpm dev
```

That sample Hexo project depends on the local package via `link:../..`, so a fresh package build is required before the example is installed or generated from a clean checkout. The maintained posts and page exercise headings, lists, a table of renderer defaults, and highlighted source listings.

## Behavior summary

- Uses exact runtime dependency `@asciidoctor/core` 4.0.4 and awaits Asciidoctor.js 4 conversion before any post-processing.
- Registers `.ad`, `.adoc`, and `.asciidoc` with Hexo as asynchronous renderers.
- Runs Asciidoctor with `doctype: article`, `safe: server`, and `to_file: false`. It does **not** set `base_dir`, so includes resolve from the conversion-time `process.cwd()`. `data.path` and the Hexo site root are not used as `base_dir`.
- Is **not safe for untrusted AsciiDoc**. `safe: server` still permits local includes under these current-working-directory semantics, and symlink targets can escape an assumed directory boundary. The current working directory is not a jail. Render only trusted input, from an isolated or sandboxed working directory that contains no secrets.
- Does not set `source-highlighter=html-pipeline`. With the public renderer's controlled `safe: server` options, a source-defined document setting is ignored, so rendering uses Asciidoctor's default output. The internal highlighter recognizes the supported default and html-pipeline marker shapes for compatibility, but this API exposes no way to configure arbitrary highlighter options.
- Re-renders only the recognized direct listing-block chain `div.listingblock > div.content > pre > code` with `hexo-util.highlight`, using fixed options `autoDetect: false`, `gutter: false`, and `wrap: false`.
- After highlighting, globally encodes every literal `{` / `}` in the generated HTML as `&#123;` / `&#125;` to prevent downstream Hexo tag or template interpretation where applicable. Browsers decode numeric character references in ordinary HTML text and attribute values for display or use, but HTML raw-text elements such as `<script>` and `<style>` do not decode them: the references remain literal source text and can alter or break embedded JavaScript or CSS.
- Does **not** sanitize arbitrary HTML, so this package is not suitable for untrusted input. An HTML sanitizer cannot prevent an AsciiDoc include from disclosing file contents.

## License

LGPL-3.0-or-later WITH LGPL-3.0-linking-exception. See `COPYING` and `COPYING.LESSER` in the published package for the full terms.
