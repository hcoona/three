# Assembly Contract

Assembly composes approved content into a retained publication tree. The
normative schemas are in `assets/`, and the shared untrusted-content policy is
`assets/publication-profile.json`.

## Responsibility and authority

The plugin has five handoff concepts: source package, translation bundle,
assembly manifest, QA evidence, and release manifest.

`assembly-spec.json` is a recipe, not another authority. It tells the
assembler which approved fragments, figures, fonts, stylesheets, profiles,
and geometry to use. After a successful build, `assembly-manifest.json`
authoritatively describes the assembled tree. QA validates the manifest and
observes that tree; it does not reinterpret the recipe.

Assembly owns:

- validation of the upstream manifests and the assets it consumes;
- fragment and stylesheet policy enforcement;
- semantic HTML/CSS composition;
- declared figure/crop construction;
- local font and asset copying;
- rendering; and
- the complete inventory of retained publication files.

It does not own PDF extraction, translation approval, independent QA, or
release certification.

## Trust model

Trust the caller/workspace owner, exclusive writes to declared paths, the
operating system, standard library, and pinned mature JSON Schema, HTML, CSS,
font, PDF, and browser tooling. Treat manifests, fragments, stylesheets,
SVGs, fonts, and stale output as untrusted until validated.

Hashes bind exact bytes and lengths for consistency. They do not authenticate
the workspace owner or toolchain. Coherent owner rewrites, hostile concurrent
writers, compromised tools, and nonrepudiation are outside scope.

## Inputs

### Source package

Apply `assets/source-package.schema.json`, require effective `status: pass`,
and verify the source-package snapshot hash. Resolve and validate only:

- block files containing source block IDs used by selected fragments;
- figure-map entries used by emitted figures; and
- page SVGs used by emitted figure parts.

Do not regenerate PDF extraction, recompute a painted-text model, or require
the unconsumed package closure.

### Translation bundle

Apply `assets/translation-bundle.schema.json`, require its closed approved
state, and verify its source-package binding. For each selected fragment,
verify its path, hash, length, visible-text hash, and source block IDs.

Translation approval is an upstream declaration. Assembly does not reproduce
terminology, MQM, reviewer, or approval-evidence workflows. Approval-evidence
files are not required in the publication tree.

### Assembly recipe

Apply `assets/assembly-spec.schema.json`. The recipe declares publication
identity, title and language, source and translation manifest paths, fragment
order, selected figures, local fonts and roles, additional stylesheets, page
geometry, and profiles.

The assembler snapshots the exact recipe as an input record. Recipe values
become authoritative for the output only after the assembler validates and
records their effective form in the assembly manifest.

Font family and role strings are generated CSS string values. They must not
contain Unicode control, format, or surrogate characters.

## Shared publication profile

`assets/publication-profile.json` is a compact closed allowlist shared
byte-for-byte with QA. It carries only policy identity, HTML element and
attribute allowlists, CSS property/at-rule/selector allowlists, and explicit
global prohibitions. The profile is the structural authority. Attribute
values, supported selector syntax, and CSS values are fixed Assembly runtime
policy rather than an interpreted profile language. Assembly rejects a
profile entry when the runtime has no corresponding value policy. Package
validation binds the byte-identical profile copies. QA binds the bundled
profile identity and hash but does not replay those authored-content
validators.

### Fragment HTML

The profile permits a bounded scholarly subset:

- prose and headings;
- quotations and inline semantics;
- code/preformatted text;
- ordered, unordered, and definition lists;
- tables and captions;
- ruby annotations without fallback `rp`;
- internal anchors;
- language and direction changes;
- bounded IDs, classes, ARIA references, and table/list metadata.

Elements and attributes not listed are rejected. In particular, the allowlist
does not contain document wrappers, active content, media, forms, scripts,
templates, browser-default-hidden elements, parser-changing elements, event
attributes, inline `style`, external resources, or arbitrary `data-*`
attributes. The only URL-bearing authored attribute is an internal
same-document anchor.

### Untrusted stylesheets

Untrusted CSS is restricted to profile-listed selectors and properties, then
checked by Assembly's fixed value rules. QA independently scans dormant CSS
resources and observes browser outcomes without replaying those rules. The
policy permits
practical typography, mixed-script line breaking, list/table styling,
borders, opaque foreground/text-decoration colors, and break, widow/orphan,
and keep properties.

Fixed font-variant validation permits compatible keyword combinations but
rejects mutually exclusive numeric figure/spacing/fraction choices and
East-Asian variant/width choices.

The profile has no at-rules, imports, custom properties, pseudo-elements,
`!important`, or URL-bearing values. Its positive property allowlist omits
generated content, visibility/display suppression, clipping/overflow,
positioning, transforms, opacity, filters, masks, animation, transitions, and
resource-producing properties. Values use bounded keywords, numbers,
physical/relative lengths, opaque colors, or declared local font families.

Everything outside the positive surface and fixed value rules is rejected;
implementations must not grow an open-ended denylist or profile DSL.

## Separate generated-output profile

Assembler-generated markup and CSS are not authorized by the fragment
profile. They use a separate closed profile:

- one `html` root with declared language;
- one `head` containing charset metadata, one plain-text title, and one local
  stylesheet link;
- one `body` whose only element child is one `main`;
- ordered assembler-owned fragment sections;
- assembler-owned `figure`/`figcaption` structures;
- assembler-owned inline crop `svg`, `title`, and `image` nodes with exact
  geometry and local used-page-SVG bindings;
- generated CSS composed from the bundled base, declared local `@font-face`
  rules, exactly one assembler-emitted unqualified `@page` rule carrying the
  declared page geometry, role rules, and validated untrusted stylesheet
  content.

Generated nodes and declarations have exact assembler-owned attribute and
property sets. They are not a general exception that fragments may imitate.
QA does not independently reapply this generated-output profile; it checks
manifest-bound browser and PDF outcomes instead.

## Figure and text bindings

Each selected fragment has one stable ID, copied asset, DOM selector,
normalized visible-text SHA-256, and source block ID list. The final authored
text for that section must match the manifest binding after the declared
normalization. Assembler-generated figure subtrees are excluded from that
authored-text digest.

Each logical figure has one stable DOM identity, one caption, one accessible
alternative, and ordered parts. Each part binds:

- source PDF page and positive source box canonicalized to three decimals;
- one generated crop selector;
- one retained canonical page SVG asset; and
- exact outer-SVG and image geometry derived from the source page and box.

Figure DOM IDs and crop IDs are each unique within their own namespace. The
namespaces need not be disjoint because crop identity is carried by
`data-crop-id`, not the HTML `id` attribute. Manifest validation rejects source
boxes that are not already in their canonical three-decimal form.

During build, the retained page SVG width and height must serialize to the same
three-decimal values as the source-package page dimensions. During standalone
validation, dimensions come from the retained SVG itself. Its `viewBox` must
match its own positive finite width and height, and every canonical
three-decimal crop box must be finite, positive, and within those dimensions.

Only page SVGs actually used by emitted figure parts are retained by default.

## Retained publication tree

The publication tree contains only:

- `assembly-manifest.json`;
- source-package, translation-bundle, and assembly-spec snapshots;
- copied approved fragments actually composed;
- canonical page SVGs used by emitted figures;
- declared local fonts;
- retained validated stylesheets;
- generated HTML and CSS; and
- PDF when rendered.

Source block files, unused page SVGs, complete section/figure map copies,
approval-evidence files, and unrelated upstream files are not retained by
default. The snapshots may therefore refer to upstream assets absent from the
publication tree. This is intentional and must not be represented as archival
closure.

During build, selected figure source labels, profiles, embedded-language
inventories, parts, and boxes are projected from the validated figure map.
Because that map is not retained, standalone validation checks manifest
structure, figure-profile membership in the manifest, and retained crop/SVG
bindings, but does not replay source labels or language inventories.

## Assembly manifest

`assets/assembly-manifest.schema.json` is closed and records:

- publication identity, language, title, profiles, and print geometry;
- assembler/runtime and policy identifiers;
- exact input-manifest snapshots;
- copied fragment and visible-text bindings;
- figure source-label, profile, embedded-language-inventory, caption, crop,
  and used-page-SVG bindings;
- font roles and copied font records;
- copied stylesheet records;
- HTML, CSS, and optional rendered PDF;
- assembled status.

The manifest does not repeat full source-package or translation-bundle
semantics. Its authoritative asset inventory is the deduplicated union of
`inputs.*`, fragment assets, figure-part source SVGs, font assets, stylesheet
assets, and non-null outputs. Shared paths are valid only when every complete
asset record is identical. That union must exactly cover every retained
regular file except the self-referential assembly manifest. No unlisted file,
link, reparse point, or special filesystem node is part of a valid publication
tree.

## Build, render, and validation

Build remains the strict upstream boundary. Validate the source, bundle, and
recipe schemas; effective source pass state; closed bundle approval and source
binding; selected fragments and source blocks; selected figures, page SVGs,
and boxes; local fonts; authored stylesheets; and only consumed upstream
material. Build writes one sibling candidate, validates it, and publishes it
to an absent output through exactly one `candidate.rename(output)`. Any
existing output entry is rejected unchanged before staging. Failure removes
the candidate, and a cleanup failure is surfaced. Replacement, ownership
markers, backups, rollback, alias machinery, and `--force` are not part of the
contract.

After build, standalone validation loads only `assembly-manifest.json` and the
files declared by its semantic asset union. The three retained input snapshots
are opaque hash/length-bound lineage assets: validation does not parse the
source package, translation bundle, or recipe; replay the recipe; reconstruct
manifest values; or regenerate and byte-compare composed HTML/CSS. Retained
fragments and stylesheets are also opaque post-build lineage copies.
Standalone validation checks the manifest schema, status, policy identity,
semantic declaration consistency, exact retained-tree closure, hashes and
lengths, generated document topology and local-resource bindings,
figure/crop/SVG geometry, declared font relations, generated CSS font-resource
closure, and bounded PDF parseability. The recorded Python runtime need only
satisfy the manifest schema; the validating patch version need not be
identical.

Assembly validates declared font files and authored/generated font bindings,
but it does not simulate CSS cascade inheritance or prove glyph selection.
Chromium rendering and downstream QA/PDF inspection own actual glyph
selection.

Rendering uses JavaScript-disabled Chromium with one request route that permits
only manifest-declared semantic assets and `about:blank`, with bounded
navigation and PDF generation. Render requires both a null
`outputs.draft_pdf` and an absent destination. It validates a temporary,
non-empty, parseable PDF with at most 500 pages, moves it into place, then
atomically rewrites only `outputs.draft_pdf`. A handled failure removes the
new PDF and restores the prior manifest bytes. Browser path, hash, and
provenance are not recorded. A second render is rejected unchanged.

## Non-guarantees

Assembly does not guarantee:

- source authenticity;
- correctness of translation approval;
- full source semantic replay;
- complete source or approval archival closure;
- human visual approval;
- accessibility conformance; or
- safety against hostile concurrent writers or compromised tools.
