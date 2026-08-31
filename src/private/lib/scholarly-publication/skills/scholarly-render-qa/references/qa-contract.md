# QA Contract

QA independently observes an assembled publication. Its normative runtime
contracts are:

- `assets/assembly-manifest.schema.json`
- `assets/publication-profile.json`
- `assets/qa-evidence.schema.json`
- `assets/release-manifest.schema.json`

QA intentionally carries no source-package, source-block, translation-bundle,
figure-map, or assembly-spec schema.

## Responsibility and trust

QA validates the assembly manifest, retained tree, HTML/CSS/PDF behavior, two
independent renders, and evidence output. It does not reconstruct upstream
semantics, repair the publication, approve language, or complete human review.

Trust the caller/workspace owner, exclusive writes to QA output paths, the
operating system, standard library, and pinned mature schema, HTML, CSS, font,
PDF, and browser tooling. Treat every publication byte and stale QA output as
untrusted. Hashes are consistency bindings, not authenticity evidence.

Coherent owner rewrites, hostile concurrent writers, compromised tools, and
nonrepudiation are outside scope.

## Input boundary

Start with `assembly-manifest.json`. Apply its exact bundled schema and verify:

- publication identity and generator/policy versions;
- confined retained-file paths and exact hashes/lengths;
- publication tree contains the manifest plus exactly the declared retained
  regular files;
- no symlink, junction, reparse point, or unsupported node;
- HTML/CSS/PDF output bindings;
- copied fragment, used page SVG, font, stylesheet, and input-manifest
  snapshot bindings; and
- initial tree fingerprint.

The assembly manifest is the authority for expected title, language, print
geometry, fragment text digests, figures, crops, fonts, stylesheets, and
outputs. QA may hash input-manifest snapshots as retained files, but it does
not load their upstream schemas or require assets to which those snapshots
refer.

## Markup and stylesheet profiles

Apply the shared closed `publication-profile.json` to copied approved
fragments and untrusted stylesheets. The Assembly and QA copies must be
byte-identical. The compact profile contains only identity, allowlists, and
global prohibitions. Assembly and QA independently implement the same fixed
positive ceilings for element/local-attribute pairs, global attributes, and
CSS properties, plus the same selector and CSS value rules. The JSON profile
may only narrow those ceilings; neither runtime interprets a configurable rule
language. Manifest visible-text digests use NFC over concatenated text followed
by whitespace collapse.

The shared conformance invariant includes rejection of mutually exclusive
`font-variant-numeric` figure/spacing/fraction keywords and
`font-variant-east-asian` variant/width keywords.

Apply the separate generated-output profile documented by the assembly
contract to assembler-owned structure:

- fixed document/head/body/main shape;
- exact fragment section projection;
- exact figure/figcaption/crop SVG structure, with only bounded non-reserved
  figure class tokens in addition to the generated class;
- local resource bindings;
- bundled base CSS;
- local declared `@font-face` rules;
- declared page size and margins; and
- validated untrusted stylesheet inclusion.

Generated markup is not treated as authored fragment markup. Conversely,
authored fragments cannot claim generated-node exceptions.

Manifest font-family and font-role strings containing Unicode control, format,
or surrogate characters are recorded as bounded manifest-integrity findings.
QA uses deterministic CSS escaping only to finish the closed generated-CSS
comparison; it does not accept those manifest values.

## Required mechanical observations

### Manifest and tree integrity

Verify the closed manifest, retained inventory, regular-file types, hashes,
lengths, path confinement, no symlinks, and unchanged before/after
manifest-declared file fingerprints. Empty-directory and directory/node-graph
proofs are not required.

### Offline HTML and profile conformance

Check static HTML and both browser DOMs for the generated document shape,
duplicate IDs, effective language, exact local stylesheet/resource bindings,
active content, remote or undeclared requests, malformed structure, fragment
visible-text digests, and shared-profile conformance.

### Figure and crop binding

Require every manifest figure and crop exactly once and no unbound figure,
caption, inline SVG, image, or crop. Inspect the declared source PDF page and
bind caption content, accessible names, part order, source box, used page SVG
hash/href/geometry, and browser geometry to the manifest when making the check
decision. Retain only compact binding summaries or bounded failure
diagnostics. QA does not replay the source PDF to re-prove the page
declaration.

Figure DOM IDs and crop IDs are unique within their respective namespaces but
need not be disjoint; crop identity uses `data-crop-id`. Source boxes in the
assembly manifest must already use canonical three-decimal coordinates.
Every crop box must have strictly positive width and height. Non-positive
geometry is a bounded `manifest.integrity` failure, and downstream ratio
checks must not divide by it.
Coordinates outside finite runtime geometry cannot produce complete evidence
and are treated as an operational publication-input failure.

### Two renders and geometry

Render twice in fresh JavaScript-disabled contexts. Install one pre-load route
per context that permits only manifest-tracked local files and the required
`about:` URL. Bind each output PDF in the stable evidence root. Inspect request
results, printable width, probe/client/scroll widths, overflow state, and
offending element boxes for check decisions. Keep raw probes transient; retain
only compact path-neutral summaries or bounded failure diagnostics in the
evidence.

Verify physical PDF page geometry for the canonical PDF and both renders.
Horizontal overflow beyond the printable width remains blocking.

### Fonts and PDF behavior

For the canonical PDF and both renders, inspect page count and geometry, font
inventory and embedding/subsetting state, Type 3 fonts, PDF actions,
extractable text count, and replacement characters. Retain compact summaries
or bounded failure diagnostics.

Combine target-aware `page.get_links` observations with low-level PDF action
subtype witnesses. Gate the action check on `unsafe_detected`. Retain bounded,
sorted unsafe kinds and kind/source-category witnesses. When `get_links`
exposes a target, it may also contribute a bounded metadata sample containing
kind, source page, target and scheme categories, length, and SHA-256. A
low-level action with no exposed target remains a kind/source witness.

Known `/S` names map to their existing fixed kinds. A missing or non-name `/S`
maps to `invalid-action`. Every other valid name maps to the fixed unsafe kind
`unknown-action`; neither its encoded nor decoded text is serialized. Its
witness adds only subtype character length, UTF-8 byte length, and SHA-256.
Bounded witness records are deduplicated and ordered deterministically.

Action evidence does not identify objects, deduplicate xrefs, claim action
cardinality, or represent a complete inventory. Equal witnesses or target
metadata do not imply one underlying action. The full PDF asset hash binds the
artifact.

Any detected action other than an internal GoTo, Type 3 font, undeclared font,
missing required embedding, or absent expected text blocks the aggregate pass
result. A direct document-local `/Dest /name` annotation that resolves to a
page is an internal GoTo. An action dictionary with `/S /Named` remains unsafe
and blocks release.

PDF image/vector object probes are not a mechanical gate and no nonzero
graphics count is required. Visual figure presence and fidelity belong to the
mandatory human review of every full-page raster.

### Repeatability and rasters

Compare the two independent renders using raw PDF hashes, normalized page
geometry, normalized extracted text, and full-page raster hashes. Raw byte
inequality may be advisory when those normalized render observations agree.
Separately compare the canonical PDF with both renders using normalized page
geometry and normalized extracted text. Export and hash one full-page raster
for every page of all three PDF sources; canonical raster equality is not
required. Each page must have finite, positive, bounded raster geometry.
There is no aggregate publication pixel budget.

## QA evidence

`qa-evidence.json` contracts stable release facts rather than duplicating
implementation telemetry. Its root contains:

- schema and publication identity;
- minimal auditor and publication-profile identity;
- the assembly-manifest asset binding;
- both independent render-PDF asset bindings;
- all full-page raster asset bindings;
- before/after publication-tree bindings and immutability state;
- the mechanical checks and status; and
- required human-review state.

Every stable asset record includes a `path_base` discriminator. The
assembly-manifest binding resolves from `publication-root`, while independent
render PDFs and rasters resolve from `evidence-root`, the directory containing
`qa-evidence.json`. Paths are canonical relative POSIX paths: absolute paths,
backslashes, `.` or `..` segments, repeated `/`, and trailing `/` are
forbidden.

Browser/library versions, request logs, DOM probes, PDF/font/figure details,
geometry diagnostics, and repeatability comparisons are not separate
mandatory root structures. The producer may retain bounded useful details in
the relevant `checks[].evidence`.

Successful request inventories, raw DOM probes, exact browser dimensions, and
image/drawing totals are not guaranteed stable evidence fields. In particular,
fragment `innerHTML`, complete visible-text arrays, and absolute `file:` crop
URLs remain transient and must not be serialized.

First-party check diagnostics are bounded and path-neutral. Raw resource URLs,
absolute source-SVG paths, parser or operating-system exception text, and raw
PDF action targets are never serialized; compact categories, lengths, and
SHA-256 digests retain failure identity instead.

CSS-unsafe manifest font-family and font-role values are likewise represented
only by Unicode categories, character and byte lengths, and SHA-256. The raw
value is not repeated in a secondary undeclared-family diagnostic.

Text-read failures for generated HTML, generated CSS, retained stylesheets,
and fragments use a `text-read` diagnostic with a stable content category,
manifest-relative logical path, `os` or `unicode` failure category, optional
fragment ID, and `errno` when the operating system exposes one. Exception
text, `filename`, `filename2`, absolute paths, and `file:` URLs are excluded.

PDF target samples retain only kind, page, target/scheme categories, lengths,
and SHA-256. Action totals, exact action-list digests, and complete-inventory
claims are excluded.

The required core IDs are:

- `manifest.integrity`
- `html.offline-profile`
- `render.geometry-overflow`
- `pdf.fonts`
- `pdf.actions-type3-text`
- `figures.crop-bindings`
- `render.repeatability`
- `rasters.complete`
- `publication.tree-unchanged`

Each core check appears exactly once in the documented order and is
`blocking`. Additional checks may be added with stable namespaced IDs. Every
check records severity, pass/fail, message, and structured evidence.

`mechanical_status: pass` is valid only when no blocking check failed and the
publication tree remained unchanged.

## Human review

Evidence always records:

```json
{
    "human_review": {
        "status": "required",
        "required_scope": [
            "Inspect every full-page raster at readable zoom.",
            "Check crop loss, overflow, page breaks, and continuation order.",
            "Check mixed-script typography, figures, captions, and notation fidelity."
        ]
    }
}
```

Mechanical QA cannot change that state. A human must inspect every raster at
readable zoom for crop loss, overflow, pagination, mixed-script typography,
figure/caption order, continuation order, and notation fidelity. Human review
records belong to a later editorial process, not an inferred QA pass.

## Release manifest

`release-manifest.json` is a thin index. It contains only:

- schema version;
- publication identity;
- assembly-manifest asset binding;
- QA-evidence asset binding;
- passing mechanical status; and
- auditor name/version needed to interpret the index.

The assembly-manifest binding keeps `path_base: publication-root`. The
QA-evidence binding uses `path_base: release-root`, the directory containing
`release-manifest.json`. The current layout places the evidence and release
roots in the same review directory, but their machine-readable semantics
remain distinct. Paths are canonical relative POSIX paths and cannot contain
`.` or `..` segments, repeated `/`, trailing `/`, backslashes, or an absolute
or URI-style prefix.

It does not repeat HTML, PDF, page, figure, crop, font, stylesheet, or tracked
file inventories. Those remain in the assembly manifest and QA evidence.

## Output isolation

The parent directory of the requested release manifest is the dedicated review
transaction root. The requested evidence, release manifest, rasters, and
independent render PDFs must all remain beneath it and outside the publication
tree under the existing confinement and non-overlap rules. On Windows, output
components ending in a dot or space and case-folded aliases are rejected
before staging.

Validate the publication inputs and complete output layout without mutating the
final review root. Build the whole candidate tree in a unique sibling staging
directory on the same filesystem, preserving every output path relative to the
review root so `path_base` records are unchanged after publication.
Revalidate existing candidate outputs against resolved filesystem identity
after directory creation, after evidence publication, and after release
publication. No two outputs may collapse to the same filesystem object or
become physical ancestors of one another through case, Unicode normalization,
trailing-dot/space, or short-name aliases. Ancestor detection compares
filesystem identity against each existing parent rather than relying only on
resolved path spelling.

A completed mechanical pass publishes evidence, release manifest, renders, and
rasters. A completed mechanical failure publishes its new failing evidence,
renders, and rasters without a release manifest, replacing any prior successful
review. If publication inputs cannot be loaded or the canonical PDF cannot be
inspected far enough to emit the complete evidence contract, the audit exits 2.
Any pre-publication exit-2 failure removes only staging and leaves the prior
review tree unchanged.

An existing non-empty review root is replaceable only when the requested
evidence path already exists there as a regular non-symlink, non-reparse
ownership marker. Empty and nonexistent roots are allowed. Other old review
content is not validated, so damaged or older owned review sets can be
replaced. Unrelated non-empty roots are rejected unchanged.

Publication uses the same minimal sibling staging, sibling backup rename, and
in-process rollback pattern as Assembly. It preserves prior results on
pre-publication operational failure and restores the prior root when a handled
publication rename fails. It does not provide power-loss or process-kill
atomicity and has no crash-recovery protocol.

Re-snapshot the publication before final evidence and fail if any input
changed.

QA may emit a proposed publication cleanup plan when requested, but it does
not delete publication artifacts.

## Non-guarantees

QA does not guarantee:

- source-package or translation-bundle replay;
- original PDF provenance;
- translation or editorial correctness;
- human visual approval;
- archival completeness;
- authenticity or nonrepudiation; or
- accessibility conformance.
