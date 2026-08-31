# Source Package Contract

`source-package.json` is the reconstruction handoff. It is translation-neutral
and contains only the source evidence downstream stages are expected to use.
The normative schema is `assets/source-package.schema.json`.

## Responsibility and trust

Reconstruction owns PDF extraction and optional replay against the original
PDF. Assembly may validate the package and consumed assets, but it does not
repeat extraction semantics. QA does not consume this schema.

The caller/workspace owner, exclusive writes, operating system, standard
library, and pinned mature PDF parser are trusted. Source bytes, supplied
maps, and stale package content are untrusted. SHA-256 values are consistency
and replay bindings, not signatures or proof of provenance.

Coherent owner rewrites, hostile concurrent writers, compromised tools,
authenticity, and nonrepudiation are outside scope.

## Top-level record

The closed schema retains:

- `schema_version`: `1.0`
- `package_id`: stable identity derived by reconstruction
- `generator`: reconstruction tool and runtime versions
- `source`: source metadata and source-byte binding
- `selection`: ordered selected PDF pages and endpoints
- `coordinate_system`: PDF points with top-left crop-box-local coordinates
- `profiles`: declared bounded source profiles
- `pages`: one record per selected page
- `sections`: optional section-map asset
- `figure_map`: optional figure-map asset
- `issues`: explicit review or failure findings
- `status`: `pass`, `review_required`, or `fail`

Relative paths resolve from the source-package manifest directory. Paths must
remain confined regular files. An asset binding contains `path`, lowercase
SHA-256, and byte length.

## Source metadata

`source` binds the authorized input PDF by file name, hash, byte length,
rights note, page count, encryption state, attachment names, and embedded
JavaScript observation. The delivered package does not need to copy the
original PDF.

Accepted packages have `encrypted: false`, an empty `attachments` array, and
`embedded_javascript: false`. Reconstruction inventories those features
without extracting payloads and rejects the source when any is present.
XFA sources are also rejected rather than inspected for scripts.

## Selection and coordinates

`selection.pdf_pages` is ordered, non-empty, and one-based. The first and last
values agree with `first_pdf_page` and `last_pdf_page`. Every page record has
the corresponding `pdf_page` and stable `pdf-NNNN` ID.

All page and figure geometry uses unrotated crop-box-local PDF points after
applying the PDF page's effective `UserUnit` scaling:

- origin: top left
- bounding-box order: `x0, y0, x1, y1`
- positive page width and height
- finite coordinates
- positive boxes contained by their referenced page

Viewer rotation is recorded separately and does not change the coordinate
space.

## Page evidence

Every page has exactly two mandatory evidence roles:

1. `blocks`: `pages/pdf-NNNN/blocks.json`, containing ordered
   translation-facing blocks conforming to `assets/source-blocks.schema.json`
2. `svg`: `pages/pdf-NNNN/page.svg`, the canonical page SVG preserving the
   selected page's vector-facing representation

No raw-text, reading-text, positioned XML, or painted-text-trace asset is
mandatory. Downstream stages must not infer that their absence weakens a
promised replay guarantee; those assets are outside this contract.

### Blocks

Blocks preserve stable IDs, source order, text, and bounded coordinates.
Their text is the extraction selected for translation. It is not proof that
every character visibly paints in the PDF.

The page record retains counts needed to interpret extraction and source
complexity, including block/text/replacement, displayed-image, vector, and
link counts as defined by the schema. Counts are observations made by
reconstruction, not a transitive replay obligation for Assembly or QA.

Suspected hidden or nonpainting OCR, replacement characters, and scan-like
pages may produce `review_required`. They need not be represented by a
cross-stage painted-glyph model.

### Canonical page SVG

The page SVG is hash-bound, local, passive, and geometry-bound to the page
record. It may be used by Assembly for declared figure crops. Reconstruction
owns canonicalization and any source-bound regeneration check.

Canonical bytes are the pinned PyMuPDF `get_svg_image(text_as_path=True)`
result for the unrotated crop box. The passive subset is exact:

- Elements are limited to `circle`, `clipPath`, `defs`, `ellipse`, `g`,
  `image`, `line`, `linearGradient`, `mask`, `path`, `pattern`, `polygon`,
  `polyline`, `radialGradient`, `rect`, `stop`, `svg`, `symbol`, and `use`.
- Unnamespaced attributes are limited to `clip-path`, `clip-rule`,
  `clipPathUnits`, `color`, `color-interpolation`, `color-rendering`, `cx`,
  `cy`, `d`, `data-text`, `display`, `fill`, `fill-opacity`, `fill-rule`,
  `fx`, `fy`, `gradientTransform`, `gradientUnits`, `height`, `id`,
  `image-rendering`, `mask`, `maskContentUnits`, `maskUnits`, `offset`,
  `opacity`, `overflow`, `pathLength`, `patternContentUnits`,
  `patternTransform`, `patternUnits`, `points`, `preserveAspectRatio`, `r`,
  `rx`, `ry`, `shape-rendering`, `spreadMethod`, `stop-color`,
  `stop-opacity`, `stroke`, `stroke-dasharray`, `stroke-dashoffset`,
  `stroke-linecap`, `stroke-linejoin`, `stroke-miterlimit`, `stroke-opacity`,
  `stroke-width`, `transform`, `vector-effect`, `version`, `viewBox`,
  `visibility`, `width`, `x`, `x1`, `x2`, `y`, `y1`, and `y2`.
- `href` or `xlink:href` is allowed only on `image`, `linearGradient`,
  `pattern`, `radialGradient`, and `use`. It must be a local fragment, except
  that `image` may contain a signature-checked base64 PNG or JPEG data URI.
- `url(...)` is allowed only as a local fragment in `clip-path`, `fill`,
  `mask`, or `stroke`. Event attributes, external references, CSS imports,
  CSS variables, CSS escapes/comments, scripts, animation, foreign content,
  element text, processing instructions, comments, and CDATA are rejected.
- Inline `style` is limited to one standard `mix-blend-mode` declaration on a
  `g` element. For PyMuPDF OCG output, only
  `inkscape:groupmode="layer"` and passive `inkscape:label` are allowed on
  `g`, in the `http://www.inkscape.org/namespaces/inkscape` namespace.
- Root `width` and `height` must serialize to the same three-decimal values as
  the page record. `viewBox` must match the root dimensions within `0.001`.

Assembly validates only SVGs it consumes. It does not need to open every
selected page SVG when the recipe uses no content from that page.

## Sections and figures

`sections` is null for a single undivided selection or an asset conforming to
`assets/section-map.schema.json`. When present, its ordered, non-overlapping
ranges cover every selected PDF page exactly once and refer to no unselected
page.

`figure_map` is null when no source figure is needed or an asset conforming to
`assets/figure-map.schema.json`. Figure discovery is never automatic. Each
logical figure has one or more ordered parts with page-local positive boxes.
Every figure explicitly records a nullable source label, a nullable
figure-specific profile, and an embedded-language inventory with no exact
duplicate tag strings. A non-null figure profile must also appear in the
source package `profiles`. Null and empty values mean the fact was checked and
is absent, not that review was skipped.

Assembly may read only the section/figure entries required by its recipe. The
manifest hash still binds the complete map bytes.

## Status

- `pass`: no blocking issue remains and the package may enter translation or
  assembly.
- `review_required`: extraction exists but a human or reconstruction rerun
  must resolve an ambiguity before downstream use.
- `fail`: the requested package was not successfully reconstructed.

Zero aggregate block text derives `fail`. Positive aggregate text below
`max(32, selected_page_count * 8)`, a page with fewer than 32 text characters
and displayed images, a replacement character, or suspected hidden or
nonpainting text derives `review_required`. Failure of the advisory PyMuPDF
text-trace inspection also derives `review_required`, as does declaring the
`music-notation` profile without a non-empty figure map.

Page status and package status must agree with `issues`. A downstream stage
must reject any effective status other than `pass`; it must not repair or
reinterpret the source status.

## Validation and optional replay

Normal package validation guarantees:

- schema-valid closed records;
- ordered page identity and bounded geometry;
- exact hashes and lengths for manifest-referenced assets;
- valid ordered blocks;
- valid canonical page SVGs;
- agreement between positive `image_count` and raster-image presence in the
  validated SVG, so possible-scan status cannot be suppressed;
- valid section and figure maps when present; and
- internally consistent counts, issues, and status.

Malformed blocks, SVG, or geometry are validation failures rather than review
issues.

When the caller supplies the original PDF, reconstruction may additionally
verify source hash, byte length, selected-page geometry, and exact regenerated
block JSON and SVG bytes. That stronger check remains inside reconstruction.
Neither Assembly nor QA performs full source replay.

## Extraction publication and replacement

Extraction builds and validates a candidate in a sibling staging directory,
then publishes it with same-filesystem replacement. The final output path
component may be absent or an ordinary directory. Regular files, symlinks,
junctions or other reparse points, and special nodes are rejected unchanged.
On Windows, a final component ending in a dot or space is rejected because
Win32 aliases it to a different lexical name. Before replacement, an existing
output is compared by filesystem identity with every source ancestor, so
case-insensitive, Unicode-normalizing, and 8.3 aliases cannot bypass source
containment. Windows also resolves an existing final component to its canonical
filesystem name before publication.

Without `--force`, any existing output directory is rejected. With `--force`,
an empty directory may be replaced. A non-empty directory is replaceable only
when it directly contains a regular, non-symlink, non-reparse
`source-package.json` ownership marker. Reconstruction does not parse or
validate that marker before replacement, allowing damaged or older owned
packages to be recovered without granting replacement authority over an
unrelated directory.

Publication renames an existing owned output to a sibling backup before
renaming the validated candidate into place. An in-process publication failure
attempts to restore that backup. Once the candidate rename commits, failure to
remove the old backup is reported as a warning and does not retroactively fail
the successful extraction.

## Immutability and non-guarantees

After approval work binds the package, changing any manifest or consumed
asset creates a new source-package binding.

The source package does not guarantee:

- authenticity or authorship;
- OCR correctness;
- a complete semantic document model;
- complete recovery of hidden or nonpainting text;
- an archival copy of the source PDF; or
- that a later publication retains the package's full transitive closure.
