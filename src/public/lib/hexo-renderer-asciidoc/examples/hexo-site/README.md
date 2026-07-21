<!--
  Copyright 2015 Shuai Zhang
  SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
-->

# Example Hexo site for `hexo-renderer-asciidoc`

This folder contains a self-contained Hexo site that consumes the local
`hexo-renderer-asciidoc` package via a link dependency. Use it to test the
renderer end-to-end without publishing to npm. It is **not** part of the root
pnpm workspace so that its Hexo dependencies stay isolated—run its pnpm
commands from `examples/hexo-site` or target that directory with `pnpm --dir`.

## Prerequisites

- [mise](https://mise.jdx.dev/) installed
- Node.js 20.19.0 or newer (matching the main project requirements)
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

## Usage

After completing the clean-checkout setup, start the Hexo server from inside
this folder:

```bash
pnpm dev
```

Then open <http://localhost:4000> to browse the site rendered from AsciiDoc
sources. Modify the posts under `source/_posts/*.adoc` to experiment with
renderer features.

To generate the static site without running a server:

```bash
pnpm generate
```

## Structure

- `_config.yml` – Minimal Hexo configuration with the stock `landscape` theme
  and Hexo's built-in highlighter enabled. No extra AsciiDoc overrides are
  declared so the sample stays truthful to the renderer's defaults.
- `source/_posts/` – Sample AsciiDoc posts referenced on the home page.
- `source/about/` – Example standalone page describing how the demo links to
  the local renderer build.
- `pnpm-workspace.yaml` / `pnpm-lock.yaml` – Tiny helper files that force pnpm
  to treat this folder as its own workspace and capture the resolved Hexo
  dependency tree. Keep them checked in so `pnpm install` remains local.

All Markup content uses the `.adoc` extension so Hexo routes every page
through `hexo-renderer-asciidoc`.
