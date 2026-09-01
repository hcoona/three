# QA Contract

QA independently observes an assembled publication. It is an outcome-oriented
release gate, not a second implementation of Assembly.

This reference defines machine-observable artifact/runtime invariants and
failure classifications. It is not a mandatory agent workflow. Agents may
choose how to gather context, divide investigation, invoke the runtime, and
explain results. Ordering is normative only where the machine contract says
so: two independent renders, ordered evidence check IDs, schema/path bindings,
and one final candidate-to-review-root rename.

Its normative runtime inputs are:

- `assets/assembly-manifest.schema.json`;
- `assets/publication-profile.json` for identity/hash binding only;
- `assets/qa-evidence.schema.json`; and
- `assets/release-manifest.schema.json`.

QA carries no upstream source, block, translation, figure-map, or recipe
schema and never imports or calls Assembly runtime validation.

## Authority and trust

`assembly-manifest.json` is the authority for publication identity, document
metadata, print geometry, fragment text digests, figures, crops, fonts,
stylesheets, and output assets. QA observes those declared outcomes. It does
not reconstruct the recipe or infer authority from retained input snapshots.

Trust the caller/workspace owner, exclusive output access, the operating
system, Python standard library, and pinned schema, CSS, XML, PDF, and browser
tooling. Trust Assembly's standalone validation of its exact generated
HTML/CSS profile and composition. Manifest hashes and exact retained-tree
closure detect ordinary byte corruption between stages. A coherent rewrite by
the trusted caller or workspace owner is outside scope. Hashes prove byte
consistency only; they do not authenticate an owner, author, or toolchain.

## Manifest and retained-tree boundary

A completed audit's manifest/tree result must establish:

- bundled publication-profile ID, schema version, closed state, and exact
  manifest hash binding;
- the deduplicated semantic union of input snapshots, fragment assets, figure
  source SVGs, fonts, stylesheets, and non-null outputs;
- rejection of conflicting records for a shared logical path while accepting
  identical shared records;
- canonical relative paths confined beneath the publication root;
- exact hash and byte-length bindings;
- exact retained regular-file closure consisting of the semantic union plus
  `assembly-manifest.json`;
- no symlink, junction, reparse point, or special retained node;
- canonical HTML/CSS/PDF output bindings and a required canonical PDF;
- unique IDs in their manifest namespaces, canonical fragment/crop selectors,
  contiguous part order, figure-profile membership, declared font-role
  families, and caption SHA-256 relation;
- finite, three-decimal canonical crop boxes with positive area; and
- an initial publication inventory/fingerprint.

Retained source-package, translation-bundle, and assembly-spec files are
opaque lineage assets. QA verifies their manifest records and tree presence
but does not parse them, load their schemas, follow their references, or
replay their semantics.

QA does not implement the publication profile's authored HTML/CSS ceilings or
conformance corpus. It does not reconstruct Assembly's wrapper, fragment
projection, figure classes/attributes/child order, caption subtree, generated
font/page/rule sequence, or exact CSS and markup bytes.

Assembly owns and standalone-validates exact generated HTML/CSS profile
conformance, topology, generated crop attributes and children, raw ARIA
attributes, pseudo-content prohibition, opaque colors, and exact CSS
composition. QA trusts that boundary rather than replaying it.

## Narrow passive boundaries

### CSS resources

The runtime's narrow `tinycss2` resource observation covers canonical
generated CSS and each retained stylesheet. Blocking outcomes are:

- every `@import`;
- parser or controlled recursion failure; and
- every `url(...)` that does not resolve, under publication confinement, to a
  manifest-declared font asset.

This scan is not stylesheet-property, selector, cascade, or value-profile
validation.

### Source SVG

Each declared figure source SVG must satisfy an independently observed safe
XML boundary:

- no DTD, entity declaration, or non-declaration processing instruction;
- an SVG namespace root;
- finite positive `width` and `height` and a matching `viewBox`;
- every manifest crop box confined to that source geometry;
- no script, foreign active content, event attribute, or animation element;
- no nonlocal direct or CSS resource reference; and
- resource references limited to existing local fragments or validated
  embedded PNG/JPEG data.

Direct resource handling is limited to `href`, `src`, and `xlink:href`. CSS
tokenization is limited to `<style>`, `style`, and the finite URL-capable
presentation set `clip-path`, `fill`, `mask`, `stroke`, `filter`,
`marker-start`, `marker-mid`, `marker-end`, and `cursor`. Metadata and
arbitrary data/text attributes are not interpreted as CSS.

The SVG check does not reproduce Assembly's full element, attribute, or
presentation profile.

## Fresh browser observations

Two independent fresh Chromium render observations are required. Their
resource boundary must be active before publication navigation, with page
JavaScript disabled and service workers blocked. Permitted browser resources
are role-specific:

- canonical HTML as `document`;
- canonical CSS as `stylesheet`;
- manifest-declared font assets as `font`; and
- manifest-declared figure source SVGs as `image`.

Input snapshots, copied fragments, retained standalone stylesheets, the PDF,
and unrelated semantic assets are not browser resources. Any other attempted
load, including a declared path requested in the wrong role, is blocked. Both
blocked and independently failed requests block the offline check.

Each render observation must establish these outcomes:

### Document and content

- the canonical stylesheet link is present and loaded exactly once;
- there is no `<style>` sheet or inline `style` attribute;
- there is exactly one effective `main` whose ID is the publication ID;
- effective document and title languages match the manifest after inheritance
  from the nearest explicit `lang`, with an explicit empty `lang` stopping
  inheritance, and the normalized title matches;
- effective `document.characterSet` is UTF-8, and no `meta[http-equiv]` is
  present; passive named metadata such as author or description is allowed;
- DOM IDs are unique;
- active DOM/SVG/animation elements, event attributes, and `<base>` are
  absent;
- manifest fragments occur exactly once under `main` in manifest order;
- there are no unbound fragment nodes; and
- no rendered-visible text or media exists outside bound fragments and their
  bound figure content.

For each fragment, derive text through rendered text nodes, ranges, computed
visibility, or an equivalent visibility-aware browser observation. Exclude
manifest figure subtrees, normalize the result, and compare its SHA-256 with
`visible_text_sha256`. Do not serialize or compare fragment `innerHTML` or
exact subtrees. Visibility observes ancestor `display`, `visibility`,
`content-visibility`, and `opacity`; non-whitespace text also requires a
nonzero rendered range. Whitespace is retained only to preserve normalized
text boundaries.

The only permitted DOM URLs are:

- a valid same-document anchor to an existing ID;
- the exact canonical stylesheet link; and
- each crop image's exact declared source SVG.

All other live or dormant URL-bearing attributes are findings.

### Figures and crops

Require every manifest figure, caption, crop SVG, and crop image exactly once
in manifest order, with no unbound figure/data-ID/caption/SVG/image nodes.
Require:

- figures and crops to remain inside bound fragments with correct nesting;
- the manifest figure DOM ID and normalized raw `aria-label`;
- one visible owned caption whose normalized text equals text derived inertly
  from `caption_html`;
- each crop to have `role=img` and its normalized manifest-derived raw
  `aria-label`;
- one crop image resolving to the declared source SVG;
- the observed crop `viewBox` to equal the manifest box; and
- finite positive rendered geometry whose cross-product aspect ratio matches
  within a symmetric 1% relative tolerance.

QA does not compare complete class lists, attribute maps, child arrays, or
caption markup.

### Page CSS and overflow

Under emulated print, the CSSOM outcome must expose exactly one active
unqualified `@page` rule with the manifest page size and all four margins, and
no active qualified competitor. Disabled or nonmatching stylesheet media and
inactive media/supports groups are ignored; active media/supports and
unconditional layer blocks are traversed. An indeterminate or unknown group
containing page rules is blocking. Multiple unqualified rules are not merged
or cascaded by QA.

Overflow must be evaluated against the manifest printable width using genuine
rendered layout. Viewport/probe orchestration is an implementation choice
provided document overflow and bounded offending element boxes are observed.

The legacy core ID `html.offline-profile` covers these observable offline,
passive-resource, stylesheet, language, and semantic content outcomes.
Manifest-to-bundled profile identity and hash binding belong to
`manifest.integrity`. Neither check claims authored/generated profile parity.

## PDF observations

The canonical PDF and both independent browser PDFs must satisfy these
PyMuPDF-observed outcomes:

- nonempty, inspectable, unencrypted PDFs with at most 500 pages;
- equal page counts and manifest physical page geometry;
- normalized extracted text corresponding to rendered visible text;
- no replacement characters;
- manifest-declared, embedded, subset fonts satisfying required role
  families;
- no Type 3 font; and
- no unsafe PDF action.

For text correspondence, text from consecutive PDF pages is joined with one
line separator after discarding at most one terminal extractor separator from
each page. An extraction-only line separator immediately between adjacent Han,
Hiragana, Katakana, or Hangul characters is an ambiguous visual-wrap marker:
it may match either no normalized boundary or one normalized whitespace
boundary. Spaces or tabs that survive extraction, and all other whitespace,
continue to preserve definite normalized boundaries.

The character set follows Unicode 17 `Script` and `Script_Extensions`.
Candidate search and boundary verification receive a deterministic allowance
of 16 times the combined normalized PDF and expected-segment character count;
exhausting it fails the audit closed instead of permitting unbounded matching.

Page-link and bounded low-level action observations jointly classify actions.
Internal GoTo and direct document-local destinations are safe only when they
resolve to an in-range page. Direct destinations on link annotations, outline
items, and the catalog `OpenAction` are classified from their low-level
containers; `named` is emitted only for an explicit `/Named` action. Other
known actions use fixed kinds such as `goto-remote`, `uri`, `launch`,
`javascript`, `import-data`, and `submit-form`. Missing/non-name action
subtypes and unresolved local destinations use `invalid-action`; other valid
names use `unknown-action`. No alternative stable action spellings are
accepted or emitted. A named or string local destination remains safe only
when its underlying PDF destination starts with an indirect page object.

Action evidence is deterministic and bounded. It may contain fixed kind,
source category, page, target/scheme categories, lengths, and SHA-256 values.
It never serializes raw targets, decoded/encoded subtype text, PDF object
identifiers/xrefs, action totals, or a complete-inventory claim. Equal
witnesses do not imply one underlying action.

Rasterization is permitted only for finite positive page geometry within the
per-page bound. The evidence must bind exactly one full-page raster for every
page of the canonical PDF and both renders. There is no image/vector-count
gate and no aggregate pixel-budget framework.

Canonical PDF admission occurs before browser work. Each browser render
applies one fixed deadline to asynchronous Chromium operations from context
creation through PDF formation. Page-count admission and same-directory
output publication then run as bounded, trusted synchronous local work. The
context closes normally in `finally`; Playwright and the browser use their
normal context-manager and `finally` lifecycle without separate QA launch or
cleanup deadlines.
Browser PDF formation requests only the first 501 pages as an oversize
sentinel, then rejects any generated result above the 500-page ceiling before
page iteration or rasterization.

Repeatability is decided from normalized page geometry, normalized text, and
every-page raster hashes across the two fresh renders. Canonical/render
comparison uses normalized geometry and text. Raw browser-PDF byte inequality
is advisory when all normalized render observations agree.

## Evidence and core checks

`qa-evidence.json` contains stable release facts:

- publication and auditor/profile identity;
- the Assembly manifest binding from `publication-root`;
- exactly two independent render PDF bindings from `evidence-root`;
- complete canonical/render-1/render-2 raster bindings from `evidence-root`;
- initial and final publication-tree fingerprints and unchanged state;
- nine ordered blocking core checks followed by any namespaced tail checks;
- aggregate mechanical status; and
- `human_review.status: required` with a nonempty scope.

The required check IDs, each exactly once and in order, are:

1. `manifest.integrity`
2. `html.offline-profile`
3. `render.geometry-overflow`
4. `pdf.fonts`
5. `pdf.actions-type3-text`
6. `figures.crop-bindings`
7. `render.repeatability`
8. `rasters.complete`
9. `publication.tree-unchanged`

Every core check is blocking. Additional namespaced checks may be blocking or
advisory. `mechanical_status: pass` forbids any failed blocking check and
requires the final publication fingerprint to equal the initial one. A
publication mutation discovered after rendering is a completed mechanical
failure and may publish failing evidence.

First-party check diagnostics are bounded and path-neutral. Resource URLs and
PDF targets use categories, lengths, and hashes instead of raw values.
Source/CSS paths are manifest-relative. Parser and operating-system exception
text and absolute paths are not stable evidence. Successful raw request
inventories, full DOM probes, exact transient browser dimensions, and
image/vector counts are not contracted root fields.

## Thin release index

A passing audit also emits `release-manifest.json`, containing only schema and
publication identity, the Assembly manifest binding from `publication-root`,
the QA evidence binding from `release-root`, `mechanical_status: pass`, and
auditor name/version. It does not repeat publication inventories or claim
human approval.

A blocking audit emits no release manifest.

## Fresh-only output transaction

The parent of `release-manifest.json` is the dedicated final review root. The
evidence, raster, render, and release paths must all be beneath that root and
canonically disjoint from the publication tree. Reject lexical/canonical
output overlap using a portable case-insensitive comparison. On Windows,
reject requested components ending in a dot or space before path
normalization.

The final review root must not exist. Reject every existing entry unchanged,
including a file, empty or nonempty directory, symlink, junction/reparse
point, or special node. A rerun uses a new root or caller-managed deletion.

The publication boundary is strict: one unique same-filesystem sibling
candidate contains the complete validated/serialized pass/fail artifact set
before exactly one `candidate.rename(review_root)`. This ordering is normative
because it defines the transaction, not because it dictates the surrounding
agent audit workflow.

- Completed pass: evidence, two renders, all rasters, and release; exit `0`.
- Completed blocking failure: evidence, two renders, all rasters, no release;
  exit `1`.
- Operational failure before successful rename: publish no review root, remove
  the candidate if created, and exit `2`.
- Candidate cleanup failure: exit `2` and explicitly report that cleanup
  failed and identify the orphan candidate.

There is no ownership marker, prior-review replacement, backup, rollback,
repeated output-identity race check, hostile-writer defense, or crash-recovery
protocol.

## Human review and non-guarantees

Mechanical QA never completes review. Every page raster still requires
readable-zoom human inspection for crop loss, overflow, pagination,
mixed-script typography, figure identity/fidelity, caption correspondence,
continuation order, notation, source labels, translations, glosses, and
errata.

QA does not guarantee:

- independent authored-profile conformance;
- exact generated markup, topology, or CSS reproduction;
- input snapshot, recipe, source-PDF, or source-semantic replay;
- visual, editorial, translation, subject-matter, or accessibility approval;
- archival completeness, authenticity, or nonrepudiation;
- protection from hostile concurrent writers or compromised tooling; or
- rollback, power-loss atomicity, or crash recovery.
