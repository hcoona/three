# Music Notation Source Profile

Load this profile only when the source package declares `music-notation`.

## Positional text

Plain extraction can flatten vertically stacked figured-bass digits,
counterpoint intervals, accidentals, and scale-degree annotations. Use ordered
block coordinates and visual page inspection to preserve the observed order.
Do not infer a musically plausible order when the evidence is ambiguous.

## Coordinates

Blocks, page SVGs, and figure boxes use unrotated crop-box-local PDF points.
Apply viewer rotation only when presenting the page. Derive any later
transform from recorded page geometry and the SVG view box; never hardcode a
tool-specific ratio.

## Figure mapping

- Map the complete semantic example, including labels needed to interpret it.
- Exclude adjacent examples and running headers.
- Preserve the canonical page SVG.
- Model cross-page continuations as ordered parts of one figure.
- Inventory natural-language text embedded in notation for translation review.
- Stop handoff when visual inspection finds ambiguous symbols or overlapping
  text; resolve the source decision upstream and rebuild. Possible hidden OCR
  remains covered by the runtime-derived review status.
- Provide at least one mapped figure when `music-notation` is declared.

For each figure, record its stable source label, part order, source page,
bounding box, embedded language inventory, and continuation status.

This profile does not translate notation, silently correct source errata, or
replace source symbols with modernized equivalents.
