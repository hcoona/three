# asciidoctor-latexmath

Offline `latexmath` rendering for Asciidoctor documents powered by your local LaTeX toolchain.

## Table of Contents

- [asciidoctor-latexmath](#asciidoctor-latexmath)
  - [Table of Contents](#table-of-contents)
  - [Overview](#overview)
  - [Features](#features)
  - [Security Note](#security-note)
  - [How It Works](#how-it-works)
  - [Output Formats](#output-formats)
  - [Prerequisites](#prerequisites)
  - [Installation](#installation)
    - [Using RubyGems](#using-rubygems)
    - [Using Bundler](#using-bundler)
    - [From Source](#from-source)
  - [Quick Start](#quick-start)
  - [Document Attributes](#document-attributes)
    - [Document-level configuration](#document-level-configuration)
      - [Font size control](#font-size-control)
    - [Element attributes \& options](#element-attributes--options)
  - [Caching](#caching)
  - [Why asciidoctor-latexmath?](#why-asciidoctor-latexmath)
  - [Accessibility \& Semantics](#accessibility--semantics)
  - [Statistics Line](#statistics-line)
  - [Troubleshooting](#troubleshooting)
  - [Contributing](#contributing)
  - [Author](#author)
  - [License](#license)

## Overview

`asciidoctor-latexmath` is an Asciidoctor extension that renders `latexmath` blocks and inline macros into static images. It leverages the LaTeX toolchain already installed on your system so you can produce consistent formulas in environments where MathJax or similar browser-side renderers are not an option.

## Features

- **Offline-first rendering** – no external services or JavaScript runtime required.
- **LaTeX fidelity** – relies on the same `pdflatex`, `xelatex`, or `lualatex` tooling used for high-quality print workflows.
- **Multiple output formats** – generate PDF, SVG, or PNG assets to match your target backend.
- **Drop-in integration** – register the extension once and keep authoring with the familiar `latexmath` syntax.
- **Intelligent caching** – reuse previously rendered block and inline formulas; cache hits skip the LaTeX toolchain entirely.

## Security Note

This extension assumes a trusted documentation source (version-controlled repository you control). Explicit output basenames (first positional block attribute) may include relative path segments including `..` and can therefore write outside the images directory intentionally (spec FR-008/FR-009 under trust model FR-036). If you process untrusted / user‑supplied AsciiDoc:

1. Disable asciidoctor-latexmath for that pipeline; OR
2. Run in a container / chroot / sandbox restricting filesystem writes; OR
3. (Future) Enable the planned strict mode (will reject path traversal).

Shell escape is never enabled (FR-017). Cache key excludes engine & converter tool names/versions (P5 / FR-011); pin a single toolchain if you need identical byte output across environments.

## How It Works

1. The extension intercepts `latexmath` blocks and inline macros during the Asciidoctor conversion pipeline.
2. The captured TeX snippet is compiled with your preferred LaTeX engine (`pdflatex`, `xelatex`, `lualatex`, or `tectonic`) into a minimal standalone document.
3. The resulting PDF is post-processed into the requested target format (PDF/SVG/PNG).
4. The rendered asset is embedded back into the document output so it behaves like any other image.

All processing happens locally, which keeps your documentation builds reproducible and secure.

## Output Formats

| Format | Description | Typical Use Cases |
| ------ | ----------- | ----------------- |
| `pdf`  | Keeps the raw LaTeX PDF output for backends that accept vector PDFs. | Asciidoctor PDF, print-ready workflows |
| `svg`  | Converts the PDF to scalable vector graphics. | Responsive HTML output, retina displays |
| `png`  | Rasterizes the PDF into a bitmap image. | Legacy HTML pipelines, slide decks |

## Prerequisites

Make sure the following tools are available on your system `PATH` before enabling the extension:

- **Ruby 3.3+** and **Asciidoctor 2.0+**
- A LaTeX engine such as **`pdflatex`**, **`xelatex`**, **`lualatex`**, or **`tectonic`** (provided by TeX Live, MiKTeX, MacTeX, etc.)
- Optional: **`dvisvgm`** for SVG conversion and **ImageMagick** or **ghostscript** for PNG conversion
- Computer Modern fonts (usually bundled with the TeX distribution)

You can verify `pdflatex` availability with:

```bash
pdflatex --version
```

Swap in another engine if that is your default workflow—for example, `xelatex --version` or `tectonic --help`.

## Installation

### Using RubyGems

```bash
gem install asciidoctor-latexmath
```

### Using Bundler

```ruby
gem 'asciidoctor-latexmath'
```

Run `bundle install` to make the extension available to your project.

### From Source

Clone this repository and point Asciidoctor at the local checkout:

```bash
git clone https://github.com/your-org/asciidoctor-latexmath.git
cd asciidoctor-latexmath
bundle install
```

Then require the extension via a relative path (see [Quick Start](#quick-start)).

## Quick Start

Render a document with offline `latexmath` support:

```bash
asciidoctor -r asciidoctor-latexmath -a latexmath-format=svg sample.adoc
```

Select a different engine by providing the `pdflatex` attribute at runtime. For example, to build with `tectonic`:

```bash
asciidoctor -r asciidoctor-latexmath -a pdflatex=tectonic -a latexmath-format=svg sample.adoc
```

During development, load the registration file directly from your working tree:

```bash
asciidoctor -r ./lib/asciidoctor-latexmath.rb -a latexmath-format=svg sample.adoc
```

Sample AsciiDoc snippet:

```adoc
The famous mass-energy equivalence equation is shown below.

[latexmath]
+++
E = mc^2
+++

Einstein also described spacetime curvature with the inline latexmath:[E_{\mu\nu} = 8 \pi T_{\mu\nu}] variant.
```

The extension replaces both expressions with rendered images that match the format specified in `latexmath-format`.

## Document Attributes

### Document-level configuration

| Attribute | Aliases / CLI | Description | Values | Default |
| --------- | ------------- | ----------- | ------ | ------- |
| `stem` | `-a stem=latexmath` | Enables global stem support so bare `stem:[...]` calls delegate to this extension. | `latexmath`, `tex` | *(not set)* |
| `latexmath-format` | `-a latexmath-format=svg` | Desired output format for every rendered asset. | `svg`, `pdf`, `png` | `svg` |
| `latexmath-preamble` | `-a latexmath-preamble=...` | Additional LaTeX preamble injected before `\begin{document}`. Per-expression `preamble=` overrides the document value. | Raw LaTeX | *(empty)* |
| `latexmath-fontsize` | `-a latexmath-fontsize=12pt` | Appends a font-size option to the standalone `\documentclass`. Expressions can override with `fontsize=`. | Values ending with `pt` (e.g., `10pt`, `12pt`) | `12pt` |
| `latexmath-ppi` | `-a latexmath-ppi=300` | Pixels-per-inch for PNG renders. Ignored for SVG/PDF. | Integer 72–600 | `300` |
| `latexmath-timeout` | `-a latexmath-timeout=120` | Maximum wall-clock time (seconds) each expression may consume before the renderer aborts and raises/places a placeholder. | Positive integer | `120` |
| `latexmath-cache` | `-a latexmath-cache=false` | Toggle the on-disk cache. `false` forces regeneration without persisting results. | `true`, `false` | `true` |
| `latexmath-cachedir` | `-a latexmath-cachedir=build/.cache/latexmath` | Cache location precedence: element `cachedir=` → `:latexmath-cachedir:` → `<outdir>/<imagesdir>` (when present) → `<outdir>/.asciidoctor/latexmath`. | Path | `<outdir>/.asciidoctor/latexmath` |
| `latexmath-keep-artifacts` | `-a latexmath-keep-artifacts=true` | Retain intermediate `.tex`, `.log`, and PDF files for debugging. | `true`, `false` | `false` |
| `latexmath-artifacts-dir` | `-a latexmath-artifacts-dir=build/latexmath-artifacts` | Destination for kept artifacts when `latexmath-keep-artifacts` is enabled. | Path | `<cachedir>/artifacts` |
| `pdflatex` / `latexmath-pdflatex` | `-a pdflatex=tectonic` | Document-wide LaTeX engine command. Elements can override with `pdflatex=`. | `pdflatex`, `xelatex`, `lualatex`, `tectonic`, or absolute path | `pdflatex` |
| `latexmath-svg-tool` | `-a latexmath-svg-tool=pdf2svg` | Preferred SVG converter (`dvisvgm` or `pdf2svg`). Paths are allowed. | Tool id or absolute path | auto-detect (`dvisvgm` then `pdf2svg`) |
| `latexmath-pdf2svg` | `-a latexmath-pdf2svg=/opt/bin/dvisvgm` | Legacy alias for the SVG converter attribute. Logs a one-time info message then normalizes to `latexmath-svg-tool`. | Path | *(same as above)* |
| `latexmath-png-tool` | `-a latexmath-png-tool=magick` | Preferred PNG conversion tool. | `pdftoppm`, `magick`, `gs`, or path | auto-detect (`pdftoppm`, `magick`, `gs`) |
| `latexmath-pdftoppm` | `-a latexmath-pdftoppm=/opt/bin/pdftoppm` | Legacy alias for PNG converter selection. Logs a one-time info message. | Path | *(same as above)* |
| `latexmath-on-error` | `-a latexmath-on-error=abort` | Rendering failure policy. `log` inserts an HTML placeholder, `abort` stops the build. | `log`, `abort` | `log` |

> Deprecated alias: `latexmath-cache-dir` / `cache-dir` is still accepted (emits a one-time INFO log) but you should prefer `latexmath-cachedir` / `cachedir`.

#### Font size control

The renderer always emits `\documentclass[preview,border=2pt,<size>]{standalone}`. The document attribute `:latexmath-fontsize:` sets the default size (12pt); blocks and inline macros can override it with `fontsize=` while still participating in caching. Values must end with `pt`—otherwise the extension raises `Asciidoctor::Latexmath::UnsupportedValueError` with an actionable hint.

```adoc
:latexmath-fontsize: 10pt

[latexmath, fontsize=18pt]
+++
\int_a^b f(x)\,dx
+++
```

### Element attributes & options

| Attribute / Option | Applies To | Description | Values / Notes |
| ------------------ | ---------- | ----------- | -------------- |
| `target=` (first positional attribute) | Block | Explicit output basename (may include subdirectories). | Filename |
| `format` (second positional attribute) | Block | Overrides output format for this expression only. | `svg`, `pdf`, `png` |
| `format=` | Block / Inline | Keyword attribute equivalent to the positional format override. | `svg`, `pdf`, `png` |
| `preamble=` | Block / Inline | Replaces the document-level preamble for this expression. | Raw LaTeX |
| `fontsize=` | Block / Inline | Overrides the `\documentclass` font-size option for this expression. | Values ending with `pt` (e.g., `10pt`, `12pt`) |
| `ppi=` | Block / Inline | Per-expression PNG density (only used when `format=png`). | Integer 72–600 |
| `timeout=` | Block / Inline | Overrides the timeout for the current expression. | Positive integer |
| `cache=` | Block / Inline | Enables/disables cache usage for the expression. | `true`, `false` |
| `%nocache` option | Block | Shortcut that skips both cache read and write. | Use as `[latexmath%nocache]` |
| `keep-artifacts` option | Block | Preserve intermediate files for this expression. | `[latexmath, options="keep-artifacts"]` |
| `artifacts-dir=` / `artifactsdir=` | Block / Inline | Custom artifact directory when `keep-artifacts` is active. | Path |
| `cachedir=` / `cache-dir=` | Block / Inline | Store/read cache entries for this expression in a custom directory. Logs a deprecation warning when `cache-dir=` is used. | Path |
| `pdflatex=` | Block / Inline | Per-expression engine command (allows flags or absolute paths). | Command string |
| `latexmath-svg-tool=` / `latexmath-pdf2svg=` | Block / Inline | Choose a specific SVG converter or executable path. | Tool id / path |
| `latexmath-png-tool=` / `latexmath-pdftoppm=` | Block / Inline | Choose a specific PNG converter or executable path. | Tool id / path |
| `on-error=` | Block / Inline | Override the error handling policy locally. | `log`, `abort` |
| `role=` | Block / Inline | Adds additional roles/CSS classes; the extension always reapplies the `math` role and accessible markup. | Space-separated roles |
| `align=` | Block | Aligns the enclosing block (`left`, `center`, `right`). | CSS alignment keyword |

Inline macros support the same keyword attributes except for positional `target=`/`format`. Attributes follow asciidoctor-diagram precedence: element attribute → element options → document attribute → default.

Set attributes via the CLI (`-a latexmath-format=png`) or inside the document header. Positional attributes follow the asciidoctor-diagram convention: `[latexmath, basename, format]`.

All generated images respect Asciidoctor's standard image directory rules. Use `imagesoutdir` to control the physical output location and `imagesdir` to influence how assets are referenced in HTML. Inline math inside literal table cells is also supported—the processor injects the rendered `<span class="image math">…</span>` markup automatically.

## Caching

The renderer persists every successful compilation so repeated conversions can reuse the existing SVG/PNG/PDF payloads without invoking your LaTeX toolchain again. Cache entries hash the following ordered fields with SHA256: extension version, normalized content hash, output format, preamble hash, font-size hash, PPI (PNG only, otherwise `-`), and entry type (`block` | `inline`). Delimiter changes, engine switches, or tool swaps do **not** affect the cache key (FR-011 / P5), so switching `pdflatex` → `xelatex` with other factors constant reuses the same cache entry. Inline rendering via `-a latexmath-inline` reuses the cached inline markup; enabling `-a data-uri` does not invalidate cached images.

By default, cache files live under `<outdir>/.asciidoctor/latexmath`. Override this location with `-a latexmath-cachedir=path/to/cache` (legacy `-a latexmath-cache-dir=...` still works but logs a deprecation message) or disable caching altogether with `-a latexmath-cache=false` when you need a clean rebuild. Removing the cache directory forces the next run to regenerate every formula.

## Why asciidoctor-latexmath?

| Feature | asciidoctor-latexmath | asciidoctor-mathematical |
| ------- | --------------------- | ------------------------- |
| Input types | `latexmath` (block + inline) | `latexmath` and `stem` |
| Rendering backend | Local LaTeX engine (`pdflatex` / `xelatex` / `lualatex` / `tectonic`) | Native Mathematical library |
| Output formats | PDF, SVG, PNG | PNG (default) / SVG |
| Accessibility defaults | `role="math"` + `alt` attributes derived from source | Requires manual markup |
| External dependencies | Leverages standard LaTeX installation | Requires the Mathematical gem and Cairo stack |

## Accessibility & Semantics

Rendered output keeps formulas accessible by emitting `<img>` elements with `role="math"`, `alt` text set to the raw LaTeX snippet, and a `data-latex-original` attribute for tooling. Blocks inherit the usual Asciidoctor figure roles, while inline expressions blend into text content without breaking line height.

## Statistics Line

When logging at INFO (the default), the extension prints a single summary line per run:

```
latexmath stats: renders=<int> cache_hits=<int> avg_render_ms=<int> avg_hit_ms=<int>
```

Fields are fixed in name and order (FR-022 / FR-035). Use this line to monitor warm-cache behaviour in CI or to flag unexpected cache misses.

Choose `asciidoctor-latexmath` when you already depend on a LaTeX distribution and want fully offline builds with minimal additional dependencies.

## Troubleshooting

- **LaTeX engine not found** – install a LaTeX distribution (TeX Live, MiKTeX, MacTeX, or Tectonic) and ensure the binaries are on your `PATH`.
- **Blank or clipped formulas** – enable `-a latexmath-keep-artifacts=true` and inspect the generated `.log` file for LaTeX errors.
- **Missing glyphs** – install Computer Modern or other math-capable fonts that ship with TeX Live/MacTeX.
- **Slow builds** – caching is enabled by default; ensure `<outdir>/.asciidoctor/latexmath` is writable. Use `-a latexmath-cache=false` if you need to force a full regeneration.

## Contributing

Bug reports, feature requests, and patches are welcome. Please open an issue describing the use case before submitting large changes. If you plan to contribute code, follow the established Ruby style guidelines and add documentation or samples for new behaviors.

## Author

Created and maintained by Shuai Zhang.

## License

Licensed under the **LGPL-3.0-or-later WITH LGPL-3.0-linking-exception**. See `LICENSE` for the full text.
