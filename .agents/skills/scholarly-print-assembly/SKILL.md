---
name: scholarly-print-assembly
description: Compose one bounded scholarly publication from a passing source package, an approved translation bundle, and a build recipe into offline HTML/CSS/PDF plus an assembly manifest. Use when approved fragments, local fonts, figures, and print geometry must be built and rendered. Do not use for source extraction, translation approval, or independent release QA.
---

# Scholarly Print Assembly

Build approved content. Assembly owns composition, publication-tree creation,
and rendering; it does not become a second source-reconstruction or
translation-governance stage.

## Boundary

The v0.1 unit is one bounded document unit, one target language, one print
geometry, and one HTML/CSS/PDF output set. Continued figures are supported.
Collection navigation, generated indexes, OCR, and release certification are
not supported.

Required inputs:

- a schema-valid `source-package.json` whose effective status is `pass`;
- an approved `translation-bundle.json`; and
- an `assembly-spec.json` recipe selecting order, figures, fonts,
  stylesheets, and geometry.

The recipe is not an authority. It instructs a build. The resulting
`assembly-manifest.json` is the authority for the assembled tree.

Read [references/assembly-contract.md](references/assembly-contract.md) before
building. Read [references/mixed-script-print.md](references/mixed-script-print.md)
for mixed-script work and
[references/music-notation-markup.md](references/music-notation-markup.md)
when `music-notation` is declared.

## Trust and input policy

Trust the caller/workspace owner, exclusive writes, the operating system,
standard library, and pinned mature parser, font, PDF, and browser tooling.
Treat manifests, fragments, stylesheets, SVGs, and stale output as untrusted.
Hashes are consistency bindings, not signatures.

Validate source and translation manifest schemas, status, and hashes. Open
and validate only the source block files, maps, page SVGs, approved fragments,
fonts, and stylesheets selected by the recipe. Do not replay the source PDF or
prove the unconsumed transitive closure of either upstream workspace.

Apply `assets/publication-profile.json` to every approved fragment, caption,
and untrusted stylesheet. Its compact data lists the permitted HTML,
attribute, property, at-rule, and selector surface plus global prohibitions.
The profile is the structural authority. The script owns attribute, selector,
and CSS value semantics and rejects profile entries for which it has no value
policy. Reject anything outside the profile surface or those runtime value
rules, including external URLs, active or hidden content, event/style
attributes, parser-changing markup, generated content, visibility,
positioning, transforms, and opacity mechanisms.

Assembler-generated document wrappers, figure/crop markup, font rules, and
page CSS use the separate closed generated-output profile defined in
[references/assembly-contract.md](references/assembly-contract.md).

## Required outcomes and decision points

This Skill does not prescribe one build investigation or composition
sequence. Choose an efficient approach while preserving these decisions and
outcomes.

### Input admission

Admit only a passing source package, an approved translation bundle, and a
schema-valid recipe with finite geometry and local declared resources.
Validate only the blocks, figure entries, page SVGs, fragments, fonts, and
stylesheets the recipe consumes. A failure in an admitted input is a build
failure; do not compensate by widening the recipe or loading unrelated
upstream assets.

### Build outcome

Apply the publication profile to every authored fragment, caption, and
untrusted stylesheet. Compose semantic HTML, generated CSS, continued figures,
and local resources in a fresh candidate.

Retain only the three input snapshots, approved fragments actually used, page
SVGs used by figures, declared fonts and stylesheets, HTML, CSS, and the PDF
when rendered. `assembly-manifest.json` must bind the semantic input,
fragment, figure-SVG, font, stylesheet, and non-null output asset union, which
is the complete retained regular-file tree other than the manifest itself.

### Standalone validation boundary

After build, standalone validation uses only the Assembly manifest and its
declared files. The retained input snapshots, fragments, and stylesheets are
opaque hash/length-bound lineage copies. Build has already validated and
composed them.

Standalone validation does not replay upstream schemas, the recipe,
authored-content policy, or the composer. It checks manifest and tree
integrity, semantic relations, source-SVG and crop geometry, generated HTML
topology and local resources, generated CSS font-resource closure, and bounded
PDF parseability. The output is not a complete source or approval archive.
Actual browser glyph selection belongs to rendering and downstream QA, not an
Assembly CSS-cascade simulator.

### Publication and handoff decision

Publish a contract-valid build only through the fresh candidate transaction
defined below. Rendering may add the one canonical PDF only while the manifest
PDF output is null. A completed assembled tree is immutable input to
`scholarly-render-qa`; Assembly does not certify release eligibility.

## Runtime invocation

Translation-bundle creation and approval are upstream responsibilities. This
Skill exposes only build, render, and validation commands:

```powershell
uv run --script scripts/assemble_print.py build `
  --spec assembly-spec.json `
  --output work\publication

uv run --script scripts/assemble_print.py render `
  --html work\publication\index.html `
  --pdf work\publication\publication.pdf

uv run --script scripts/assemble_print.py validate `
  --manifest work\publication\assembly-manifest.json
```

Both build output and the initial render destination must be absent. Build
validates one sibling candidate and publishes it with one final rename. Render
is allowed only while `outputs.draft_pdf` is null; on success it adds that one
asset record and atomically rewrites the manifest. A rerun requires a new path
or caller-managed deletion of the prior output.

## Required publication payload

- `assembly-manifest.json`
- HTML and generated CSS
- PDF when rendered
- Source-package, translation-bundle, and assembly-spec snapshots
- Copied approved fragments actually used
- Page SVGs used by emitted figure crops
- Declared local fonts and retained stylesheets

Source block files, complete page sets, approval-evidence files, and other
unconsumed upstream assets are not retained by default.

## Non-goals

- Source PDF extraction or OCR
- Translation, terminology, MQM, or approval decisions
- Full upstream semantic replay
- Complete source or approval archival closure
- Independent QA, repair, or release certification
- Defense against hostile concurrent writers or compromised tooling
