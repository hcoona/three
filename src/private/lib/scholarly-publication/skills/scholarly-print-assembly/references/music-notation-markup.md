# Music Notation Markup

Load this profile only when `music-notation` is declared upstream.
If the source package declares it, omitting `music-notation` from the assembly
specification is a contract error; additional assembly profiles remain allowed.

## Figures

- Preserve notation from source page SVGs; do not rasterize it during assembly.
- Build a continued example as one `<figure>` with ordered inline crop SVGs.
- Use one caption identity and one accessible name for the logical example.
- Keep source labels unless the approved translation bundle explicitly
  supplies a translated label or gloss.

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

Lyrics, verbal labels, and prose embedded in notation require an inventory and
an explicit translation decision. Preserve the source when the editorial
policy requires it, and add a nearby approved gloss rather than altering the
source SVG.

## Source fidelity

Do not silently correct source numbering, symbols, or spelling. Record known
source errata separately and preserve the approved editorial decision in the
assembly manifest.
