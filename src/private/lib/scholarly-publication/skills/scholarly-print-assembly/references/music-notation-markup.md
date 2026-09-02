# Music Notation Markup

Load this profile only when `music-notation` is declared upstream.
If the source package declares it, omitting `music-notation` from the assembly
specification is a contract error; additional assembly profiles remain allowed.

## Figures

- Preserve notation from source page SVGs; do not rasterize it during assembly.
- Build a continued example as one `<figure>` with ordered inline crop SVGs.
- Use one caption identity and one accessible name for the logical example.
- Preserve labels embedded in the source SVG. Supply any upstream-approved
  translation or gloss through the figure caption instead of altering the
  SVG. Assembly validates and records the exact caption but does not approve
  its language.

## Inline tokens

Represent semantically atomic notation as inline-block spans:

- Figured-bass stacks
- Counterpoint interval stacks
- Scale-degree numbers with hats
- Accidentals bound to pitches or degrees
- Cadence labels and compact bilingual terms

Use nested spans for vertical stacks instead of spaces, line breaks, or flex
layout. Keep the text representation available to assistive technology.

## Embedded language

Lyrics, verbal labels, and prose embedded in notation require an explicit
source inventory. Assembly projects that inventory into the manifest and
always preserves the source SVG. Add any upstream-approved gloss in the
caption; its language and editorial correctness remain human-review concerns.

## Source fidelity

Do not silently correct source numbering, symbols, or spelling. Put any
upstream-approved errata note in a caption or approved fragment so the
manifest binds the exact published content. Assembly does not create or infer
an editorial decision record.
