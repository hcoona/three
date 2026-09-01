---
name: scholarly-render-qa
description: Independently audit an assembled scholarly publication by validating its assembly manifest and retained tree, rendering the HTML twice, and using observable HTML/PDF, geometry, font, figure, repeatability, and raster data for release checks. Use before release. Do not use to reconstruct upstream inputs, assemble or repair output, approve language, or complete human review.
---

# Scholarly Render QA

Observe the assembled publication from a separate work area. QA is read-only
with respect to the publication tree and does not repair failures.

## Input boundary

QA consumes:

- `assembly-manifest.json`;
- the manifest-declared retained publication tree;
- canonical HTML/CSS/PDF; and
- the shared `assets/publication-profile.json`.

QA does not load source-package, source-block, translation-bundle, figure-map,
or assembly-spec schemas. It does not replay the original PDF or reconstruct
unretained upstream semantics. Fragment, figure, font, geometry, and output
expectations come from the validated assembly manifest.

Read [references/qa-contract.md](references/qa-contract.md) before auditing.
Read [references/music-notation-qa-profile.md](references/music-notation-qa-profile.md)
when the assembly manifest declares `music-notation`.

## Trust and independence

Trust the caller/workspace owner, exclusive writes to QA output paths, the
operating system, standard library, and pinned mature parser, font, PDF, and
browser tooling. Treat the publication tree and its content as untrusted.
Hashes bind observed bytes; they do not prove authorship.

Write evidence, independent renders, and rasters outside the publication
tree. Do not:

- modify HTML, CSS, PDF, assets, or the assembly manifest;
- regenerate assembly evidence;
- turn a failure into a pass by mutation;
- delete publication artifacts; or
- claim editorial, subject-matter, or visual approval.

The parent directory of `--release-manifest` is the dedicated review
transaction root. The evidence, release manifest, rasters, and independent
renders must all be beneath that root.

## Workflow

1. Validate the assembly manifest schema, policy identity, manifest-declared
   semantic asset closure, hashes, path confinement, node types, and initial
   tree fingerprint.
2. Apply the shared publication profile to copied fragments and untrusted
   stylesheets. Apply the documented generated-output profile to the
   assembler-owned document wrapper, figure/crop markup, and generated CSS.
3. Validate offline resource closure within the retained tree, semantic HTML,
   IDs, language, fragment text bindings, figure/caption/crop bindings, used
   page SVG hashes, local fonts, and declared print geometry.
4. Render the canonical HTML twice in fresh JavaScript-disabled contexts with
   nonlocal requests blocked.
5. Inspect both render PDFs, request behavior, printable geometry, horizontal
   overflow, fonts, page boxes, unsafe PDF action witnesses, Type 3 fonts,
   extractable text, replacement characters, figures, and crops. Combine
   target-aware page-link observations with low-level kind/source witnesses;
   do not claim action cardinality or a complete action inventory. PDF
   image/vector object counts are not a mechanical gate. Visual figure presence
   and fidelity belong to mandatory human review of the full-page rasters.
   Known action subtype names use fixed kinds, missing or non-name subtypes use
   `invalid-action`, and every other valid name uses `unknown-action`. Unknown
   subtype witnesses retain only bounded length/hash metadata, never encoded or
   decoded subtype text. Retain only compact path-neutral summaries or bounded
   failure diagnostics.
6. Compare raw bytes and normalized geometry, text, and full-page rasters for
   repeatability.
7. Export a full-page raster for every page of the canonical PDF and both
   independent renders.
8. Re-fingerprint the publication tree and fail if it changed.
9. Write schema-valid `qa-evidence.json`. Its stable root binds the assembly
   manifest, both render PDFs, every raster, the before/after publication
   tree, tool/profile identity, nine blocking core checks, and human-review
   state. Stable asset records explicitly name `publication-root` or
   `evidence-root` as their relative path base. Full browser probes remain
   transient; check evidence may retain compact summaries or bounded failure
   diagnostics. Asset paths are canonical relative POSIX paths: they contain
   no absolute or URI prefix, backslash, `.` or `..` segment, repeated `/`, or
   trailing `/`.
10. When all blocking checks pass, write a thin `release-manifest.json`
    indexing only the assembly manifest, QA evidence, mechanical status, and
    interpreting tool identity/version. Its QA-evidence binding uses
    `release-root`; its assembly-manifest binding uses `publication-root`.
11. Build the complete review candidate in a unique sibling staging directory,
    then replace the dedicated review root. A completed blocking failure
    publishes evidence, renders, and rasters without a release manifest.

Successful request inventories, raw DOM probes, exact browser dimensions, and
image/drawing totals are not guaranteed stable evidence fields.
PDF image/vector object counts must not be used as a nonzero graphics gate;
visual figure presence is established by mandatory human full-page raster
review.

Text-read diagnostics for generated HTML/CSS, retained stylesheets, and
fragments contain only a stable category, manifest-relative logical path,
`os`/`unicode` failure category, optional fragment ID, and available `errno`.
PDF action observations contain `unsafe_detected`, bounded sorted unsafe kinds
and kind/source witnesses, plus bounded target metadata samples when page-link
inspection exposes a target. They contain no action totals or exact
action-list digest.

## Script

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

Exit codes:

- `0`: blocking mechanical checks passed.
- `1`: the audit completed with blocking findings.
- `2`: arguments, dependencies, or operation failed.

An existing non-empty review root is replaceable only when the requested
evidence path is a regular non-symlink ownership marker within it. Empty and
nonexistent roots are allowed. Pre-publication exit-2 failures remove only
staging and preserve any prior review. Publication uses sibling staging, a
temporary sibling backup rename, and in-process rollback. This preserves prior
results for ordinary operational failures and rollback handled by the running
process; it is not atomic against power loss or process termination.

## Required mechanical core

Evidence must include checks for:

- manifest and retained-tree integrity;
- offline HTML and publication-profile conformance;
- two-render geometry and overflow;
- fonts;
- PDF actions, Type 3 fonts, and extractable text;
- figure and crop binding;
- repeatability;
- raster completeness; and
- unchanged publication tree.

All nine core checks are blocking. A failed core check cannot coexist with
`mechanical_status: pass`. Additional checks may use stable namespaced IDs.

## Human review

`qa-evidence.json` always records `human_review.status: required`. Mechanical
success does not replace readable-zoom inspection of every raster for
cropping, page breaks, mixed-script spacing, figures, captions, continuation
order, notation fidelity, source labels, embedded-language inventories,
translations, glosses, and errata notes.

## Non-goals

- Upstream source, block, translation, figure-map, or recipe replay
- Translation or editorial revision
- HTML/PDF assembly or repair
- Artifact deletion or archival finalization
- Authenticity, nonrepudiation, or accessibility-conformance claims
