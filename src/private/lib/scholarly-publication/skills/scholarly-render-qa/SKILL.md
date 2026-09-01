---
name: scholarly-render-qa
description: Independently audit an assembled scholarly publication by validating its assembly manifest and retained tree, rendering the HTML twice, and using observable HTML/PDF, geometry, font, figure, repeatability, and raster outcomes for release checks. Use before release. Do not use to reconstruct upstream inputs, assemble or repair output, approve language, or complete human review.
---

# Scholarly Render QA

Observe an assembled publication without modifying it. QA is an independent,
outcome-oriented observer, not a second Assembly validator.

## Input and authority boundary

QA consumes:

- `assembly-manifest.json` as the authority for the assembled publication;
- the manifest-declared retained tree;
- canonical HTML, CSS, and required PDF; and
- the bundled `assets/publication-profile.json` only for closed
  identity/version/hash binding.

QA validates the Assembly manifest schema, semantic asset union, path
confinement, exact retained regular-file closure, hashes and lengths, canonical
output bindings, IDs, figure/font relations, canonical crop boxes, and initial
publication fingerprint. Retained source-package, translation-bundle, and
assembly-spec snapshots are opaque lineage assets. QA hashes them but does not
parse or replay them.

Consult [references/qa-contract.md](references/qa-contract.md) for the strict
runtime and artifact contract. When the manifest declares `music-notation`,
use [references/music-notation-qa-profile.md](references/music-notation-qa-profile.md)
to inform the required human review.

## Independence and trust

QA never imports, calls, or shares Assembly runtime validation. It does not
independently reapply Assembly's authored-fragment policy, generated markup
profile, generated CSS recipe, or exact topology checks.

Trust the caller/workspace owner, exclusive access to declared output paths,
the operating system, standard library, and pinned schema, CSS, XML, PDF, and
browser tooling. Trust Assembly's standalone validation of the exact generated
HTML/CSS profile, topology, crop attributes and children, raw ARIA attributes,
pseudo-content prohibition, opaque colors, and exact CSS composition. Manifest
hashes and retained-tree closure catch ordinary byte corruption; a coherent
rewrite by the trusted caller or workspace owner is outside scope. SHA-256
binds observed bytes for consistency; it is not a signature or proof of
authorship.

Keep evidence, independent renders, and rasters outside the publication tree.
Do not repair, delete, or rewrite publication artifacts.

## Required outcomes and decision points

This Skill does not prescribe an agent's investigation order, decomposition,
or reporting sequence. Choose an efficient approach for the publication at
hand, and use equivalent observation techniques where the runtime contract
allows them. Preserve the following decisions and outcomes.

### Publication admissibility

A completed audit binds the closed Assembly manifest, required canonical PDF,
deduplicated semantic asset union, exact retained regular-file tree, hashes,
relations, and publication fingerprints. Identical shared records are valid;
conflicting records are not.

If the manifest or required artifacts cannot be loaded far enough to create
the complete evidence contract, classify the result as operational. Integrity
findings that still permit a complete audit remain blocking mechanical
findings rather than being converted into an operational abort.

### Observable mechanical eligibility

Mechanical eligibility requires all of these outcome groups:

- narrow passive CSS/SVG resource safety, without authored/generated profile
  replay;
- exactly two independent fresh render observations with page JavaScript
  disabled, service workers blocked, one fixed bound around each complete
  render, and role-aware resource routing: canonical HTML as document,
  canonical CSS as stylesheet, declared fonts as fonts, and declared figure
  source SVGs as images;
- canonical stylesheet, effective UTF-8 without `meta[http-equiv]`, fragment
  cardinality/order and visibility-aware text outcomes, inherited
  document/title language, absence of active DOM/SVG/animation elements,
  normalized manifest-bound figure/crop `aria-label` values, crop `role=img`,
  relative crop aspect-ratio comparison, active conditional page CSS, and
  printable-width outcomes in both renders;
- canonical plus render PDF geometry, normalized text, embedded subset fonts,
  required font roles, Type 3/action safety, fixed 500-page admission, and
  bounded evidence;
- one bounded full-page raster for every page of all three PDF sources;
- normalized render repeatability, with raw byte inequality advisory when the
  normalized observations agree; and
- an unchanged final publication fingerprint.

Visibility-aware fragment text may be established through rendered text
nodes, ranges, computed visibility, or an equivalent browser observation. Do
not substitute exact `innerHTML`, subtree, generated-markup, or generated-CSS
reproduction.

The sole admitted page rule is one active unqualified `@page` rule. Inactive
stylesheet/media/supports branches do not compete; active qualified or
additional unqualified rules block. Every admitted PDF is nonempty and has at
most 500 pages, and every page of an admitted PDF is still rasterized.

### Artifact and release decision

Every completed audit emits schema-valid evidence beginning with the nine
ordered blocking core checks and records required human-review state. The
schema permits additional namespaced blocking or advisory checks after that
core. A release manifest is valid only for a mechanical pass. A changed
publication is a completed blocking result that may publish evidence; it is
not release eligible.

The legacy check ID `html.offline-profile` means bundled profile identity plus
observable offline, passive-resource, stylesheet, language, and semantic
content outcomes. It does not claim authored or generated profile
conformance.

## Fresh review transaction

The parent of `--release-manifest` is the dedicated final review root. The
requested evidence, rasters, independent renders, and release manifest must be
beneath it and canonically disjoint from the publication. On Windows, reject
output components ending in a dot or space.

The final review root must be absent. Any existing entry—file, directory,
symlink, reparse point, or special node—is rejected unchanged. Reruns require a
new root or caller-managed deletion.

A publishable candidate must already contain the complete schema-valid
pass/fail artifact set. Publication is exactly one sibling
candidate-to-review-root rename:

- pass: evidence, two renders, every raster, and release; exit `0`;
- completed blocking failure: evidence, two renders, every raster, and no
  release; exit `1`;
- operational failure: no final review root; clean the candidate when one was
  created and exit `2`. If cleanup fails, report the orphan candidate
  explicitly.

There is no ownership marker, prior-review replacement, backup, rollback,
hostile-writer defense, or crash-recovery protocol.

## Runtime invocation

The package runtime is the canonical interpreter of the machine contract. One
valid invocation is:

```powershell
uv run --script scripts/audit_publication.py `
  --html work\publication\index.html `
  --assembly-manifest work\publication\assembly-manifest.json `
  --evidence work\review\qa-evidence.json `
  --release-manifest work\review\release-manifest.json `
  --rasters work\review\pages `
  --page-size letter `
  --render-twice
```

`--page-size` is an assertion, not an override.

## Required mechanical core

Evidence begins with these core checks once, in order, with
`severity: blocking`:

1. `manifest.integrity`
2. `html.offline-profile`
3. `render.geometry-overflow`
4. `pdf.fonts`
5. `pdf.actions-type3-text`
6. `figures.crop-bindings`
7. `render.repeatability`
8. `rasters.complete`
9. `publication.tree-unchanged`

Any additional namespaced blocking or advisory checks follow this core.
A mechanical pass forbids failed blocking checks.

Stable asset records name their base: `publication-root` for the Assembly
manifest, `evidence-root` for render PDFs and rasters, and `release-root` for
QA evidence in the release index.

## Human review

`qa-evidence.json` always records `human_review.status: required`. A person
must inspect every full-page raster at readable zoom for crop loss, overflow,
pagination, mixed-script typography, figures, captions, continuation order,
notation fidelity, source labels, translations, glosses, and errata.

## Non-guarantees

QA does not guarantee:

- independent authored-fragment/profile conformance;
- exact generated markup, topology, or CSS reproduction;
- upstream snapshot, recipe, source PDF, or source-semantic replay;
- visual, editorial, subject-matter, translation, or accessibility approval;
- authenticity, nonrepudiation, or archival completeness;
- safety against hostile concurrent writers or compromised tooling; or
- rollback, power-loss atomicity, or crash recovery.
