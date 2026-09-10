# Asciidoctor Latexmath Records

The [package README](../README.md), [changelog](../CHANGELOG.md), and
[license](../LICENSE) retain their package-facing roles. Repository work follows
[the contribution workflow](../../../../../CONTRIBUTING.md); these project
records do not create a separate work-authorization or governance system.

- [Rendering and quality contract](architecture/rendering-and-quality-contract.md):
  retained product constraints and interface/test gates, including P1–P5.
- [Requirements](../specs/001-asciidoctor-latexmath-asciidoctor/spec.md):
  imported requirements, clarifications, scenarios and permanent local IDs;
  their declared evidence level is unchanged.
- [Architecture input](architecture/overview.md) and
  [design rationale](architecture/design-rationale.md): retained design and
  assumptions, with their original source identities.
- [Data model](../specs/001-asciidoctor-latexmath-asciidoctor/data-model.md).
- Contracts: [cache key](../specs/001-asciidoctor-latexmath-asciidoctor/contracts/cache_key.md),
  [errors](../specs/001-asciidoctor-latexmath-asciidoctor/contracts/error_handling.md),
  [processors](../specs/001-asciidoctor-latexmath-asciidoctor/contracts/processors.md),
  [rendering pipeline](../specs/001-asciidoctor-latexmath-asciidoctor/contracts/renderer_pipeline.md),
  and [statistics](../specs/001-asciidoctor-latexmath-asciidoctor/contracts/statistics.md).
- [Quickstart](../specs/001-asciidoctor-latexmath-asciidoctor/quickstart.md).
- [Design research](../specs/001-asciidoctor-latexmath-asciidoctor/research.md),
  [upstream rendering notes](research/upstream-rendering-notes.md),
  [Diagram attribute input](research/diagram-attribute-input.md), and
  [Latexmath attribute input](research/latexmath-attribute-input.md): source
  evidence and earlier proposals, not additional current requirements.
- [Performance evidence](../specs/001-asciidoctor-latexmath-asciidoctor/performance-baseline.md)
  and [verification evidence](../specs/001-asciidoctor-latexmath-asciidoctor/final-verification.md)
  retain their measurement and historical acceptance limits.
- [Traceability input](../specs/001-asciidoctor-latexmath-asciidoctor/tasks.md):
  the existing advisory audit consumes these legacy task IDs and requirement
  links. This is not a current task queue or authorization record.

The `specs/` contract and traceability paths remain because the current audit
and tests consume that namespace. No duplicate requirement or task catalog is
introduced. New work changes its owning concern and preserves local IDs.

The [original processor-selection learning input](research/processor-selection-input.txt)
retains the attributed human questions and assistant discussion behind the
[upstream research](research/upstream-rendering-notes.md). It is historical
source input; the research record states its provenance and authority limits.
