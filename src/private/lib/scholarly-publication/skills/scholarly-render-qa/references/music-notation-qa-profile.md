# Music Notation QA Profile

Load this profile only when the assembly manifest declares `music-notation`.

## Mechanical checks

- Every manifest logical figure appears exactly once in HTML.
- Every manifest figure part maps to one ordered inline crop.
- Continued examples have one caption and one accessible name.
- Every crop binds the manifest-declared retained page SVG and source box.
- Crop SVGs remain local vector content with the generated-output shape.
- Every figure carries an explicit nullable source label and profile plus an
  embedded-language inventory; non-null figure profiles are declared globally.
- Bounded figure source-fact samples appear in QA evidence; the bound assembly
  manifest retains the complete facts for editorial review.
- Figured-bass stacks, intervals, scale degrees, accidentals, and cadence
  labels retain atomic semantic markup.

QA does not reopen a source figure map or source package. The assembly
manifest is the expected figure/crop authority. It does not approve language
or infer a treatment decision from source facts.

## Visual checks

Inspect every notation raster for cropped symbols, wrong continuation order,
caption or gloss overlap, broken vertical stacks, unintended rasterization,
blur, silently changed source symbols, and the editorial correctness of
labels, embedded-language inventories, translations, glosses, and errata
notes.

PDF image/vector object counts are not a mechanical gate. Visual notation
presence, completeness, and fidelity belong to mandatory human review of the
full-page rasters.
