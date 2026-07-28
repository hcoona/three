<!--
  Copyright 2015 Shuai Zhang
  SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
-->

# Example Hexo site for `hexo-renderer-asciidoc`

This folder contains a self-contained Hexo site that consumes the local
`hexo-renderer-asciidoc` package via a link dependency. Use it to test the
renderer end-to-end without publishing to npm. It is **not** part of the root
pnpm workspace so that its Hexo dependencies stay isolated. The clean-checkout
sequence below targets it from the repository root with `pnpm --dir`; after
setup, you may instead `cd` into this directory and run its scripts directly.
Hexo auto-discovers the plugin and registers it asynchronously for `.ad`, `.adoc`,
and `.asciidoc`.

## Prerequisites

- [mise](https://mise.jdx.dev/) installed
- Node.js 22 or newer (matching the main project requirements)
- pnpm 10.x (already pinned in the repo)

## Clean-checkout setup and generation

The example links to the parent package's generated `dist/` files. From the
repository root, run this exact sequence so the parent package is built before
the example is installed or generated:

<!-- linked-example-validation-sequence:start -->

```bash
mise trust
mise exec -- pnpm install --frozen-lockfile
mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc run build
mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc/examples/hexo-site install --frozen-lockfile
mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc/examples/hexo-site run generate
```

<!-- linked-example-validation-sequence:end -->

Skipping the parent-package build can leave Hexo unable to load the linked
renderer; Hexo may still exit successfully while reporting that `.adoc` files
have no renderer.

If you call the package directly in your own scripts while experimenting with
this demo, remember that `renderer(...)` returns `Promise<string>`. `renderSync`
is unsupported for AsciiDoc input.

## Usage

After completing the root-invoked clean-checkout setup, either start the Hexo
server from the repository root:

```bash
mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc/examples/hexo-site run dev
```

or change into the example directory and run the equivalent local command:

```bash
cd src/public/lib/hexo-renderer-asciidoc/examples/hexo-site
pnpm dev
```

Then open <http://localhost:4000> to browse the site. Its authored AsciiDoc
source page and posts use this renderer; Hexo generates index, archive,
category, tag, and theme pages separately. Modify the posts under
`source/_posts/*.adoc` to experiment with renderer features.

To generate again from inside the example directory:

```bash
pnpm generate
```

## Structure

- `_config.yml` – Minimal Hexo configuration with the `minimalism` theme and
  Hexo's built-in highlighter enabled. The renderer still applies its own fixed
  static-highlighting pass only to recognized AsciiDoc listing blocks.
- `source/_posts/` – Sample AsciiDoc posts referenced on the home page.
- `source/about/` – Example standalone page describing how the demo links to
  the local renderer build.
- `pnpm-workspace.yaml` / `pnpm-lock.yaml` – Tiny helper files that force pnpm
  to treat this folder as its own workspace and capture the resolved Hexo
  dependency tree. Keep them checked in so `pnpm install` remains local.

Every authored source page and post in this example uses the `.adoc` extension,
so Hexo routes those source files through `hexo-renderer-asciidoc`. Hexo's
generated index, archive, category, tag, and theme pages are not renderer
inputs.

## Behavior notes

- The package uses `@asciidoctor/core` 4.0.5 and awaits conversion before
  post-processing.
- Includes resolve from the conversion-time current working directory because
  the renderer does not pass `base_dir`; neither `data.path` nor the Hexo site
  root changes that behavior.
- This renderer is **not safe for untrusted AsciiDoc**. `safe: server` still
  permits local includes, and symlink targets can escape an assumed directory
  boundary. The current working directory is not a jail. Use only trusted input
  in an isolated or sandboxed working directory containing no secrets.
- The package does not set `source-highlighter=html-pipeline`. The controlled
  public options ignore a document-level setting and use Asciidoctor's default
  output. The internal highlighter recognizes supported marker shapes only for
  compatibility; this API exposes no arbitrary highlighter configuration.
- After highlighting, the renderer globally encodes every literal brace in the
  generated HTML as `&#123;` or `&#125;`. Browsers decode numeric character
  references in ordinary HTML text and attribute values for display or use, but
  HTML raw-text elements such as `<script>` and `<style>` do not: the references
  remain literal source text and can alter or break embedded JavaScript or CSS.
  Raw HTML remains unsanitized, so this package is not suitable for untrusted
  input. Sanitizing output markup cannot prevent an include from disclosing file
  contents.
