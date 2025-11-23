# Contract: Processors (Block & Inline)

## Scope (P1)
Exactly two processors are registered:
- `LatexmathBlockProcessor` (context: listing/literal/open) for `[latexmath]` blocks.
- `LatexmathInlineMacroProcessor` for `latexmath:[...]` inline macros.

No BlockMacroProcessor; attempts to use `latexmath::[]` should leave source untouched or emit a warning (decision: emit WARN once per doc).

## Responsibilities
| Aspect | BlockProcessor | InlineMacroProcessor |
|--------|----------------|----------------------|
| Capture Content | Raw body (reader.lines.join) | Target text between brackets |
| Attribute Resolution | Merge element attrs → doc attrs → defaults | Same |
| Positional Attributes | 1st: target basename; 2nd: format | (none) |
| Options | `%nocache`, `keep-artifacts` | `nocache` (option attr), `keep-artifacts` (ignored or error? decision: allow) |
| Build RenderRequest | yes | yes |
| ConflictRegistry | register explicit basename | register explicit basename |
| Return Node | Image block node | Inline image macro or passthrough node |

## Normalization Rules
- Element boolean attributes: strings matching `/^(true|false|yes|no)$/i` converted to boolean.
- Deprecated aliases (`cache-dir`) issue single deprecation log and normalize to `cachedir`.
- PPI parsed as float → integer; validated range.
- Timeout parsed as integer seconds; invalid -> error with guidance.

## Error Propagation
- Processor rescues only *known* validation errors to enrich with location context; re-raises renderer errors (tool missing, timeout) unchanged.
- For warnings (deprecated alias, block macro usage) log via Asciidoctor logger at `:info` or `:warn` level.

## Tests
| Spec | Coverage |
|------|----------|
| `spec/processors/block_attribute_precedence_spec.rb` | Element overrides doc-level |
| `spec/processors/inline_attribute_precedence_spec.rb` | Inline overrides doc-level |
| `spec/processors/deprecated_alias_spec.rb` | Alias normalization + single log |
| `spec/processors/positional_attributes_spec.rb` | Basename + format parsing |
| `spec/processors/nocache_option_spec.rb` | Skip cache lookup when set |
| `spec/processors/conflict_detection_spec.rb` | Duplicate basename diff signature error |
| `spec/processors/error_wrapping_spec.rb` | Validation errors annotated with location |

## Open Questions (All Resolved Now)
- Should inline support `keep-artifacts`? Decision: yes (consistency) but artifacts primarily useful for blocks.

