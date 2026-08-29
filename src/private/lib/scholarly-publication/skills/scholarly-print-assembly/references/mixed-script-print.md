# Mixed-Script Print

Use semantic HTML and local, licensed fonts. The base stylesheet is a starting
point, not a substitute for rendered-page review.

## Language markup

- Set the document's primary BCP 47 language on `<html>`.
- Mark language changes on the smallest meaningful span.
- Keep translated prose and retained source-language quotations distinguishable.
- Preserve machine-readable headings, captions, notes, and cross-references.

## Fonts

- Use static local font files with explicit family, style, and weight.
- Bind `body-cjk` and `body-latin` roles to declared families in the assembly
  specification; do not infer roles from font array order.
- Use `body-latin` as the bounded default for accepted language tags. Select
  `body-cjk` only for `zh`, `ja`, or `ko` primary tags or `Hans`, `Hant`,
  `Hani`, `Jpan`, or `Kore` script subtags.
- Provide an exact declared face for every family/style/weight tuple selected
  by the semantic markup. A cmap in another face is not fallback evidence.
- Prefer a CJK family designed for body text and a compatible Latin family.
- Subset only after the final character inventory is known.
- Retain font licenses and hash every font file.
- Never rely on a machine-specific system-font fallback for release output.

## Line breaking

- Use CJK-aware line breaking and punctuation rules.
- Keep opening punctuation with following text and closing punctuation with
  preceding text.
- Keep bilingual terms, compact music tokens, labels, and cross-reference
  numbers atomic when an internal break would change meaning.
- Do not place ordinary inline tokens in flex containers; flex sizing and
  justification can distort word spacing.
- Use nonbreaking markup only for semantic units, not entire sentences.

## Pagination

- Encode keep-with-next and keep-together relationships on semantic nodes.
- Avoid page-number selectors, nth-page rules, and content-specific blank
  spacers.
- Prevent headings, captions, labels, and short introductory lines from being
  stranded.
- Allow long figures or tables to break only at explicit semantic boundaries.
- Re-render after every pagination change; browser layout is the evidence.

## Print geometry

Declare the page size and margins in `@page`. Use physical units for print
geometry and relative units for typography where appropriate. The independent
auditor verifies the produced PDF page boxes rather than trusting CSS alone.
