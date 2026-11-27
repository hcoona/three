<!--
  Copyright 2015 Shuai Zhang
  SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
-->

# Hexo + Asciidoctor Semantic Bridge Models

The TLA+ specification in [`models/`](models/) (~395 lines across four modules)
captures the semantic boundary between Hexo `8.1.2`, `hexo-front-matter`
`4.2.1`, `@asciidoctor/core` `4.3.0`, `hexo-util` `4.0.0`, and HTML5 before
re-implementing `src/`. Deterministic single-document passes (`PostPipeline`,
`CodeHighlighting`) are modeled as pure function compositions rather than
multi-stage state machines; temporal interleaving (`Next`) is used only in
`IngestionAndScope`, where Hexo's `Box` concurrently races post and asset
processors. These pre-implementation models do not alter current 4.x package
behavior or [`README.md`](../../README.md) commitments; any runtime or contract
change requires separate Delivery Wave authorization.

## Unified algebraic structure (`BridgeAlgebra.tla`)

Nearly all cross-ecosystem failures reduce to three shared laws in
[`BridgeAlgebra.tla`](models/BridgeAlgebra.tla):

1. **Non-fixed-point sentinel round-trip (`RoundTripSentinel`):** stashing a
   token as a structured placeholder $s = \langle \text{open}, k, \text{close}
   \rangle$ across an intermediate pass fails whenever that pass is not a fixed
   point on $s$:
   - Hexo `PostRenderEscape` (`<!--flag\uFFFCk-->`): stashes Swig tokens into
     brace-free sentinels _before_ `renderer()` runs (so renderer-internal brace
     encoding cannot touch them!). Asciidoctor prose `replacements` rewrites
     `-->` to `-&#8594;`, and code lexers wrap `k` in `<span
class="number">k</span>`, leaking broken sentinels; where sentinels survive
     outside `<code>` (plain `----` `<pre>` or raw `[stem]`), `onRenderEnd`
     restores unencoded `{{...}}` / `{%...%}`, which bypasses `tag.js`'s
     `<code>`-only shield (`rCodeTag`) and crashes or is erased by Nunjucks
     (`{#...#}` is deleted up front). Also, anonymous `hexo.post.render(path,
{ content })` calls pass `path` to renderer lookup without copying it onto
     `data` (`post.js:295`), leaving `hexo-hooks` filters blind to `.adoc`
     unless `data.source` or `data.engine` is present.
   - Asciidoctor `pass:q[...]` (`+macros`, `\u0096k\u0097`): server-side code
     lexers wrap `k` in `<span class="number">k</span>` while leaving C1
     delimiters `\u0096`/`\u0097` bare, missing both `PASS_SLOT_RX` and
     `HIGHLIGHTED_PASS_SLOT_RX` (`substitutors.js:108-116`) and leaking raw C1
     control characters into HTML.
2. **Stack-blind operations over a Dyck tag language (`DyckEval`, `UniqueIds`,
   `ResolveId`):** HTML5 fragments form a typed parenthesis language with global
   `id` uniqueness and first-match `#target` resolution:
   - _Prefix/suffix slicing (`<!-- more -->`)_: `excerpt.js` slices raw HTML
     without closing open ancestors in `excerpt` or reopening stripped-`id`
     ancestors in `more`, and injects `<span id="more">` (colliding with
     authored `[[more]]`).
   - _Line splitting (`\n`)_: `hexo-util.prismHighlight` (`preprocess: true`)
     omits per-line span closing (trapping line-1 `<b class="conum">` inside
     open `<span>`s and crossing `<span class="line">` wrappers) and defaults
     `lineNumber` to `true` (appending `.line-numbers-rows` before `</code>`).
     Conversely, `hexo-util.highlight` unconditionally runs internal
     `closeTags()` (`highlight.js:50, 95`), which pushes via `unshift` and
     reopens without reversing—balancing `<span>` tags per line while inverting
     nested cross-line token classes (`string` > `subst`).
   - _Delimiter rewriting (`+quotes`)_: `subQuotes()` runs after `highlight()`
     and rewrites `*...*` across `<span class="... operator">*</span>`
     boundaries into crossed `<span class="op"><strong></span>` tags.
   - _Fragment concatenation (`p1 \o p2`)_: within `post.render` (excluding
     site-wide `_after_html_render` / `after_render:html` page rewriters),
     scoping `_footnotedef_k` / `_footnoteref_k` + `href`s by `data.path` while
     keeping `<div id="footnotes">` preserves `#footnotes` CSS and single-post
     `#_section` deep-links, whereas multi-post index pages inherently share
     canonical `#preamble` / `#toc` / `#_intro` / `#footnotes` IDs.
3. **Context-dependent entity decoding (`ObservesLiteral`):** numeric character
   references (`&#123;`, `&#125;`) decode in normal HTML elements, whereas HTML5
   raw-text elements (`<script>`, `<style>`) and raw-HTML server filters observe
   literal `&#123;` characters.

## Upstream equivalence classes & `Box` concurrency (`IngestionAndScope.tla`)

Rather than transcribing third-party regexes branch-by-branch, upstream inputs
are partitioned into behavioral equivalence classes verified against
`hexo-front-matter@4.2.1` and `hexo@8.1.2`:

- **`yfmClass` (`hexo-front-matter.parse`):** `"valid-mapping"` (fenced
  `---\nkey: val\n---` or `---\n{}\n---` isolating `_content` cleanly; plain
  unfenced `= Title\n\nBody` with no `---`/`----` delimiter also stays intact,
  though the authoring contract requires a fenced mapping so adding a `----`
  listing block later cannot break ingestion); `"corrupts-body"` (unfenced
  `key: val` / `:author:` before `----` truncating the header, comment-only
  `---\n# c\n---` / scalar / unmatched `---` fences leaking raw `---` into
  `_content`, empty `---\n---` stealing up to a subsequent `\n---` or `\n----`
  prefix, or leading `----` listing blocks at line 1 matching `\1 = "----"` as
  YAML); and `"throws-error"` (malformed YAML or unfenced `:showtitle:` /
  `NOTE: a: b` before `----` throwing `YAMLException`, logged per file by
  `box/index.js`).
- **`partialState` (`include::` fragment exposure):** public
  `hexo.extend.processor` is additive-only (`register` without `unregister`).
  `skip_render` still copies raw `.adoc` files to `public/`; `config.include`
  overrides `_`-hiding across page dirs and `_posts/**`; site-path
  `config.exclude` isolates page-directory partials (`guide/fragment.adoc`) via
  `assetProcessor`, whereas `_posts/**` (`postProcessor` checks only
  `isHiddenFile`) and `scanAssetDir` (which passes asset-relative `item` to
  `isExcludedFile`) still expose non-hidden partials. Naming partials
  `_part.adoc` (unmatched by `config.include`) or placing them outside
  `source_dir` maps to `"isolated"` across all `source/` locations.
- **Concurrent `Box` post/asset race (`ProcessPost \/ ProcessAsset`):** when
  `post_asset_folder: true`, built-in `postProcessor` requires
  `extname(new_post_name) === extname(path)` and runs `scanAssetDir` only when
  that gate passes. An additive processor rescuing `.adoc` posts under default
  `:title.md` without duplicating `scanAssetDir` loses assets whenever `Box`
  runs `processAsset` (`post.js:239`) before `Post.save`.

## Checked finite models

Every invariant defined across all checkable modules is mounted in the 15
configurations below (4 positive + 1 witness + 10 counterexamples):

| Module and configuration                                                                                          | Checked scope and established property                                                                                                            | Distinct states |
| ----------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- | --------------: |
| [`PostPipeline`](models/PostPipeline.tla) ([`PostPipeline.cfg`](models/PostPipeline.cfg))                         | Pure 4-entry Warehouse composition under `hexo-hooks` (`DisableNunjucks = TRUE`, fence mask, allowlisted top/`#preamble` `<!-- more -->`).        |          12,222 |
| [`PostPipeline`](models/PostPipeline.tla) ([`PostPipelineRendererOnly.cfg`](models/PostPipelineRendererOnly.cfg)) | Restricted host/author envelope (`hlEnabled = FALSE`, flat `<!-- more -->`, including anonymous `post.render`) where `renderer-only` is sound.    |           1,134 |
| [`CodeHighlighting`](models/CodeHighlighting.tla) ([`CodeHighlighting.cfg`](models/CodeHighlighting.cfg))         | AST adapter over encapsulated `hexo-util.highlight` + span-balanced `prismHighlight`, `%nohighlight`, callouts, fail-closed `+quotes`/`pass:q`.   |           9,216 |
| [`IngestionAndScope`](models/IngestionAndScope.tla) ([`IngestionAndScope.cfg`](models/IngestionAndScope.cfg))     | Interleaved `Box` post/asset ingestion (`hexo-hooks`), `dirname(data.path)` `base_dir`, `#footnotes` CSS, and `data.path`-scoped footnotes.       |          18,432 |
| [`PostPipelineWitness.cfg`](models/PostPipelineWitness.cfg)                                                       | Witness (`NeverRendersPost`): confirms `hexo-post` reaches `"rendered"` without vacuity.                                                          |             cex |
| [`PostTagTradeoffCounterexample.cfg`](models/PostTagTradeoffCounterexample.cfg)                                   | Counterexample (`HexoTagExpansionSupported`): disabling Nunjucks preserves AsciiDoc/STEM literals at the cost of `{% asset_img %}` expansion.     |             cex |
| [`PostRawNunjucksCounterexample.cfg`](models/PostRawNunjucksCounterexample.cfg)                                   | Counterexample (`LiteralAndStemFidelity`): in `pre-plain` (`----`), stashed `{{...}}` bypasses `EncodeBraces` and hits Nunjucks in `onRenderEnd`. |             cex |
| [`AnonPostRenderCounterexample.cfg`](models/AnonPostRenderCounterexample.cfg)                                     | Counterexample (`LiteralAndStemFidelity`): anonymous `hexo.post.render(path, { content })` omits `data.source`/`engine`, blinding `hexo-hooks`.   |             cex |
| [`SyncRenderCounterexample.cfg`](models/SyncRenderCounterexample.cfg)                                             | Counterexample (`SyncRenderSupported`): `hexo.render.renderSync` / `render()` bypass async Asciidoctor 4 renderers.                               |             cex |
| [`HljsCloseTagsCounterexample.cfg`](models/HljsCloseTagsCounterexample.cfg)                                       | Counterexample (`StrictNestedTokenClassOrder`): `hexo-util.highlight`'s internal `closeTags()` inverts nested cross-line token classes.           |             cex |
| [`InlineSubsTradeoffCounterexample.cfg`](models/InlineSubsTradeoffCounterexample.cfg)                             | Counterexample (`UnconditionalInlineSubsOnHighlighted`): `+quotes` crosses operator `<span>`s and `pass:q` leaks C1 `\u0096<span>0</span>\u0097`. |             cex |
| [`AdditiveProcessorAssetRaceCounterexample.cfg`](models/AdditiveProcessorAssetRaceCounterexample.cfg)             | Counterexample (`IngestionAndAssetLaw`): `ProcessAsset -> ProcessPost` drops `post_asset_folder` assets under an additive processor.              |             cex |
| [`UntrustedInputCounterexample.cfg`](models/UntrustedInputCounterexample.cfg)                                     | Counterexample (`UntrustedInputSafety`): `safe: 'server'` symlinks make untrusted AsciiDoc unsafe without `safe: 'secure'` + HTML sanitization.   |             cex |
| [`MultiPostDomIdCounterexample.cfg`](models/MultiPostDomIdCounterexample.cfg)                                     | Counterexample (`StrictMultiPostDomIdUniqueness`): `#preamble` / `#intro` / `#footnotes` collide across multi-post index pages.                   |             cex |
| [`MultiPostSectionXrefCounterexample.cfg`](models/MultiPostSectionXrefCounterexample.cfg)                         | Counterexample (`MultiPostSectionXrefIntegrity`): on a full-body index page, Post 2's `#intro` link resolves to Post 1's heading.                 |             cex |

Run SANY and TLC from the repository root via the
[locked TLA+ toolchain](../../../../../../docs/engineering/workspaces.md#tla):

```sh
mise exec -- python eng/scripts/tla.py sany \
  -- src/public/lib/hexo-renderer-asciidoc/docs/design/models/*.tla
mise run tla:tlc -- -workers 1 \
  -config src/public/lib/hexo-renderer-asciidoc/docs/design/models/PostPipeline.cfg \
  src/public/lib/hexo-renderer-asciidoc/docs/design/models/PostPipeline.tla
```

## Settled contracts & open product choices for re-implementation

1. **Plugin surface (`hexo-hooks`):** use `renderer` + `before/after_post_render`
   with documented upstream Hexo contracts (valid non-null `---` YAML mapping,
   `_part.adoc` partials unmatched by `config.include` or outside `source_dir`,
   and `new_post_name: :title.adoc` when `post_asset_folder: true`).
2. **Nunjucks, braces & filter metadata:** set `renderer.disableNunjucks =
true`, emit raw `{`/`}` without entity encoding, mask ` ``` ` / `~~~` across
   `backtick_code_block`, fail closed if front-matter forces
   `disableNunjucks: false`, and require `data.source` or `data.engine` on
   `hexo.post.render` calls (always populated on Warehouse `Post`/`Page`
   documents; direct `hexo.post.render(path, { content })` without metadata is
   outside filter visibility).
3. **Highlighter & inline subs:** use an AST `SyntaxHighlighterBase` adapter
   over official `hexo-util.highlight` / `prismHighlight` (`stripIndent: false`,
   `lineNumber: false` + FIFO span balancing on Prism, `subSpecialchars()` on
   `%nohighlight`), decide whether to follow `hexo.config.syntax_highlighter` vs
   fixed Highlight.js, and fail closed on `+quotes` / `+macros` when server
   grammar highlighting is active.
4. **Excerpts, includes & DOM scope:** allow `<!-- more -->` only at top level
   or between `#preamble` paragraphs (stripping `id` when reopening in
   `data.more` and failing closed on `sect1`/composite containers, pre-fold
   footnotes, or `[[more]]`; standard Asciidoctor `:nofootnotes:` is preserved
   untouched); resolve includes from `dirname(data.path)` (`safe: 'secure'` when
   `data.path` is absent); scope `_footnotedef_k` / `_footnoteref_k` + `href`s
   by `data.path` while leaving `#footnotes`, `#preamble`, and `#_section`
   untouched.
