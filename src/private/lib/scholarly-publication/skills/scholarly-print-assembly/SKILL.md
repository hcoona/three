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
The script owns fixed positive ceilings for element/local-attribute pairs,
global attributes, CSS properties, and value semantics. The JSON profile may
only remove entries from those ceilings. Reject anything outside both the
profile surface and those fixed rules, including external URLs, active or
hidden content, event/style attributes, parser-changing markup, generated
content, visibility, positioning, transforms, and opacity mechanisms.

Assembler-generated document wrappers, figure/crop markup, font rules, and
page CSS use the separate closed generated-output profile defined in
[references/assembly-contract.md](references/assembly-contract.md).

## Workflow

1. Snapshot and hash the source package, translation bundle, and assembly
   recipe.
2. Validate the source package and translation bundle. Resolve only consumed
   block IDs, selected figure-map entries, page SVGs, and approved fragments.
3. Validate the recipe's publication identity, order, language, local fonts,
   stylesheets, profiles, and finite print geometry.
4. Sanitize fragments and stylesheets with the shared publication profile.
5. Compose semantic HTML, generated CSS, continued figures, and local
   resources in a fresh staging tree.
6. Copy only retained replay/audit assets: the three input manifest
   snapshots, approved fragments actually used, page SVGs used by figures,
   declared fonts and stylesheets, HTML, CSS, and PDF when rendered.
7. Emit `assembly-manifest.json` with the complete retained-file inventory,
   byte bindings, fragment and figure projections, geometry, policy identity,
   and tool versions.
8. Validate the staged tree, publish it atomically within the trusted
   exclusive-write model, and optionally render its canonical HTML to PDF.
9. Hand the immutable tree to `scholarly-render-qa`.

The output is replayable for assembly/QA observations from retained inputs.
It is not a complete source or approval archive.

## Script

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

The final `--output` path component may be absent or an ordinary directory; a
symlink, junction, dangling link, or other reparse point is rejected. Trusted
links in ancestor components retain ordinary filesystem semantics.
`build --force` may replace an existing empty output directory. A non-empty
output is replaceable only when it contains a regular, non-symlink
`assembly-manifest.json` ownership marker; the old manifest need not be valid,
so damaged or older Assembly output remains recoverable.

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
