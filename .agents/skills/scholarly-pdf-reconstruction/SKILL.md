---
name: scholarly-pdf-reconstruction
description: Reconstruct selected pages from an authorized born-digital PDF into a hash-bound source package with ordered translation-facing blocks, canonical page SVGs, geometry, sections, figures, and status. Use before translation or assembly when source coordinates and vectors are needed. Do not use for OCR, translation, page composition, or release QA.
---

# Scholarly PDF Reconstruction

Create the source-side handoff. This skill alone owns PDF extraction and
optional replay against the original PDF.

## Boundary

Use this skill only when:

- the caller is authorized to process the source;
- the PDF is unencrypted, contains no embedded JavaScript, XFA, or
  attachments, and is suitable for born-digital extraction;
- the selected one-based PDF pages are explicit;
- section and figure boundaries are supplied or human-confirmed; and
- page-level vectors are an acceptable source representation.

Do not translate, approve language, compose publication pages, audit a final
publication, perform OCR, or silently infer figure regions. Route scan
restoration to the scan-restoration skills.

Read [references/source-package-contract.md](references/source-package-contract.md)
before creating or editing a package. Read
[references/music-notation-source-profile.md](references/music-notation-source-profile.md)
when the source contains notation or vertically stacked musical annotations.

## Trust model

Trust the caller/workspace owner, exclusive writes to declared paths, the
operating system, the standard library, and pinned PDF tooling. Treat the PDF,
maps, manifests, and stale workspace content as untrusted.

Hashes bind bytes for consistency and replay, not authenticity. Coherent
workspace-owner rewrites, hostile concurrent writers, compromised tools, and
nonrepudiation are outside scope.

## Required inputs

- Source PDF
- Non-empty authorization or rights note
- Selected PDF page range
- Section map when the selection contains multiple sections
- Figure map when downstream composition needs figures, including at least
  one figure whenever `music-notation` is declared
- Optional declared profiles such as `music-notation`

## Workflow

1. Hash the source under the declared exclusive-write assumption.
2. Inventory source metadata and reject encryption, embedded JavaScript, or
   XFA or attachments before extracting selected-page geometry and content
   counts.
3. Emit one ordered `blocks` JSON asset and one canonical page `svg` asset for
   every selected page. Blocks are translation-facing text with stable IDs and
   crop-box-local coordinates; SVG is the page-level vector evidence.
4. Validate page identity, order, geometry, asset hashes, section coverage,
   figure-part bounds, and package status.
5. Fail closed on malformed extraction, SVG, or geometry. Report replacement
   characters, possible scans, and suspected hidden or nonpainting OCR as
   `review_required` instead of requiring raw-text, reading-text, XML, or
   painted-trace assets.
6. Emit `source-package.json` and run package validation before handoff.

The normal package guarantee is schema validity, ordered selection,
hash-bound blocks and SVGs, bounded geometry, and an explicit status. It does
not include a full semantic model of the PDF.

## Script

```powershell
uv run --script scripts/reconstruct_pdf.py extract `
  --pdf source.pdf `
  --output work\source-package `
  --pages 1-12 `
  --sections sections.json `
  --figure-map figures.json `
  --rights-note "Authorized project input"

uv run --script scripts/reconstruct_pdf.py validate `
  --package work\source-package\source-package.json
```

When stronger source consistency is needed, reconstruction may validate with
the original source:

```powershell
uv run --script scripts/reconstruct_pdf.py validate `
  --package work\source-package\source-package.json `
  --source source.pdf
```

Source replay is optional and remains a reconstruction responsibility.
Assembly and QA do not repeat it.

Both commands emit one JSON report. Exit zero means extraction produced a
contract-valid package or validation found a contract-valid package; use the
reported `status` as the downstream gate. Operational errors and invalid
packages exit nonzero and report errors.

The final `--output` path component may be absent or an ordinary directory.
Regular files, symlinks, junctions or other reparse points, and special nodes
are rejected. Without `--force`, any existing output directory is rejected.
With `--force`, an empty output directory may be replaced. A non-empty
directory is replaceable only when it directly contains a regular,
non-symlink, non-reparse `source-package.json` ownership marker. The marker is
not parsed or validated, so damaged or older Reconstruction output remains
recoverable.

## Handoff

Only a package with `status: pass` may enter translation or assembly.
`review_required` is a blocking inspection state, not an approval. After
translation approval, treat the source package bytes as immutable; a source
change creates a new downstream binding.

## Non-goals

- OCR or scan cleanup
- Automatic figure discovery
- Translation, terminology, MQM, or language approval
- Final HTML/PDF composition
- Publication QA or release indexing
- Authenticity or archival-completeness claims
