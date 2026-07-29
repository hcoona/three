<!--
  Copyright 2015 Shuai Zhang
  SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
-->

# hexo-renderer-asciidoc

Add native [AsciiDoc](https://asciidoc.org/) rendering to Hexo using `@asciidoctor/core` / Asciidoctor.js 4.x. The plugin auto-registers asynchronous renderers for `.ad`, `.adoc`, and `.asciidoc`, re-highlights recognized listing blocks with Hexo-compatible markup, and encodes literal `{` / `}` before returning HTML to Hexo.

## Requirements

- Node.js 22 or newer
- Hexo 8.0.0 or newer

## Installation

Published-prerelease users should first confirm the published dist-tags:

```bash
npm view hexo-renderer-asciidoc dist-tags
```

When the output lists the explicitly published `beta` dist-tag, install it with:

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

The example is a source-tree contributor fixture that depends on the local
package via `link:../..`; it is not included as a runnable site in the installed
package. For an already independently verified and trusted source checkout,
follow `src/public/lib/hexo-renderer-asciidoc/examples/hexo-site/README.md`. Reviewers of an unpublished candidate
must use the checkout instructions and acceptance evidence designated by the
authoritative migration PR or prerelease announcement, as described above.

Published-package users can instead create a regular Hexo site and install the
explicit `@beta` package as described in [Installation](#installation).

## Behavior summary

- Uses the exact `@asciidoctor/core` 4.x runtime version declared in the package manifest and awaits conversion before any post-processing.
- Registers `.ad`, `.adoc`, and `.asciidoc` with Hexo as asynchronous renderers.
- Runs Asciidoctor with `doctype: article`, `safe: server`, and `to_file: false`. It does **not** set `base_dir`, so includes resolve from the conversion-time `process.cwd()`. `data.path` and the Hexo site root are not used as `base_dir`.
- Is **not safe for untrusted AsciiDoc**. `safe: server` still permits local includes under these current-working-directory semantics, and symlink targets can escape an assumed directory boundary. The current working directory is not a jail. Render only trusted input, from an isolated or sandboxed working directory that contains no secrets.
- Does not set `source-highlighter=html-pipeline`. With the public renderer's controlled `safe: server` options, a source-defined document setting is ignored, so rendering uses Asciidoctor's default output. The internal highlighter recognizes the supported default and html-pipeline marker shapes for compatibility, but this API exposes no way to configure arbitrary highlighter options.
- Re-renders only the recognized direct listing-block chain `div.listingblock > div.content > pre > code` with `hexo-util.highlight`, using fixed options `autoDetect: false`, `gutter: false`, and `wrap: false`.
- After highlighting, globally encodes every literal `{` / `}` in the generated HTML as `&#123;` / `&#125;` to prevent downstream Hexo tag or template interpretation where applicable. Browsers decode numeric character references in ordinary HTML text and attribute values for display or use, but HTML raw-text elements such as `<script>` and `<style>` do not decode them: the references remain literal source text and can alter or break embedded JavaScript or CSS.
- Does **not** sanitize arbitrary HTML, so this package is not suitable for untrusted input. An HTML sanitizer cannot prevent an AsciiDoc include from disclosing file contents.

## License

LGPL-3.0-or-later WITH LGPL-3.0-linking-exception. See `COPYING` and `COPYING.LESSER` in the published package for the full terms.
