# Phase 1 Data Model – asciidoctor-latexmath

Generated: 2025-10-02
Spec: ./spec.md
Research: ./research.md

## Overview
The extension transforms latexmath block / inline expressions into rendered assets (SVG/PDF/PNG) through a deterministic multi-stage pipeline while enforcing attribute precedence and caching invariants.

## Entities
### 1. MathExpression
- Source: Raw LaTeX snippet captured from block `[latexmath]` or inline `latexmath:[...]`.
- Attributes: `content`, `entry_type` (`:block|:inline`), `target_basename?`, `element_attrs`, `options` (parsed option flags), `location` (file + line for diagnostics).
- Invariants:
  - `content` non-empty after trimming.
  - `entry_type` determines default delimiters ($$...$$ for block; $...$ for inline) used only within rendering stage (not stored in cache key separately).

### 2. RenderRequest
Wraps a MathExpression with resolved configuration.
- Fields: `expression`, `format` (`:svg|:pdf|:png`), `engine` (`:pdflatex|:xelatex|:lualatex|:tectonic`), `preamble`, `ppi?`, `timeout_secs`, `keep_artifacts?`, `nocache?`, `cachedir`, `artifacts_dir`, `tool_overrides` (hash for `pdf2svg`, `png-tool`).
- Derived: `content_hash` (SHA256 of normalized content), `preamble_hash`.
- Invariants:
  - If `format == :png` then `ppi` present & 72 ≤ ppi ≤ 600.
  - `timeout_secs` > 0 integer.
  - `nocache?` true implies cache read & write skipped.

### 3. PipelineSignature
Represents chosen sequence of Renderers.
- Fields: ordered list of stage identifiers (e.g., `[pdflatex,v1,dvisvgm,v1]`), plus engine & tool version strings.
- Method: `#digest` stable SHA256 of JSON(serialized structure).
- Invariants: Order fixed at build time for the request; no dynamic fallback at runtime.

### 4. CacheEntry
- Fields: `final_path`, `format`, `content_hash`, `preamble_hash`, `engine`, `ppi?`, `pipeline_sig`, `tool_versions`, `entry_type`, `created_at`, `checksum` (SHA256 of file), `size_bytes`.
- Invariants:
  - `checksum` matches current file before treating as hit.
  - Hit if all structural fields & checksum match RenderRequest expectations.

### 5. ToolchainRecord
- Fields: `available` (bool), `path`, `version` (parsed from `--version` or fallback), `id` (symbol `:pdflatex`, etc.).
- Invariants: If a tool is required for chosen format but `available=false` → build aborts early.

### 6. Renderer (Interface `IRenderer`)
- Methods: `#name`, `#signature_fragment`, `#render(tmp_dir, ctx) -> RendererResult`.
- `RendererResult`: `{ output_path:, format:, intermediate?: bool }`.
- Implementations: `PdflatexRenderer`, `PdfToSvgRenderer`, `PdfToPngRenderer` (with specific tool strategy classes: `DvisvgmAdapter`, `Pdf2SvgAdapter`, `PdftoppmAdapter`, `MagickAdapter`, `GhostscriptAdapter`).

### 7. DiskCache
- Methods: `#fetch(key_digest) -> CacheEntry?`, `#store(key_digest, CacheEntry)`, `#with_lock(key_digest){}`.
- Invariants: All writes atomic: temp file creation + rename; lock optional (advisory) for large renders.

### 8. ConflictRegistry
- Purpose: Track explicit target basenames per (format) to detect conflicts (FR-040).
- Methods: `#register!(basename, signature_digest, location)` raising `ConflictError` on mismatch.

## Relationships
- MathExpression → (1) RenderRequest
- RenderRequest → (1) PipelineSignature
- PipelineSignature + RenderRequest → Cache key (see below)
- Cache key → CacheEntry (0..1)
- ToolchainRecord influences PipelineSignature & validation
- ConflictRegistry consulted before writing final artifact

## Cache Key Composition (Ordered for hashing)
1. Extension version
2. content_hash
3. format
4. engine
5. preamble_hash
6. ppi (if format png else `-`)
7. entry_type
8. pipeline_sig.digest
9. tool_versions (joined stable order)

Hash function: SHA256(hex) over UTF-8 joined by `\n` to avoid collision across field boundaries.

## Attribute Precedence (Highest → Lowest)
1. Element named attributes (e.g., `format=png`)
2. Element options flags (`%nocache`, `options="keep-artifacts"`)
3. Deprecated aliases (accepted then normalized, log once)
4. Document-level attributes (e.g., `:latexmath-format:`)
5. Hardcoded defaults (format=svg, cache=true, ppi=300 (png), timeout=120)

## Error Conditions & Exceptions
| Condition | Exception | Message Elements |
|-----------|-----------|------------------|
| Unsupported format | `UnsupportedFormatError` | requested, supported list |
| Missing required tool | `MissingToolError` | tool id, attempted path(s), hint |
| Target basename conflict | `TargetConflictError` | basename, first location, second location |
| Timeout | `RenderTimeoutError` | stage, timeout, command line |
| PPI out of range | `InvalidAttributeError` | provided value, allowed range |
| Invalid timeout value | `InvalidAttributeError` | provided value, expected integer > 0 |
| Engine command not executable | `MissingToolError` | engine name, PATH search summary |

## State Transitions (Simplified)
`Unprocessed` → (Resolve attributes) → `RequestBuilt` → (Cache hit?) → `Finalized`
- On cache miss: `RequestBuilt` → (Pipeline stages sequential) → `RenderedStages` → (Store Cache) → `Finalized`
- On error: any stage → `Failed` (with diagnostic artifact retention if keep-artifacts)

## Non-Goals (Explicit)
- No dynamic pipeline fallback ordering.
- No inline base64 embedding logic beyond core Asciidoctor data-uri integration.
- No auto cleanup / eviction of cache entries.

## Validation Rules (Representative)
- R1: Two identical MathExpression objects MUST produce identical cache key given same attributes.
- R2: Changing preamble text MUST change cache key.
- R3: Changing `ppi` affects key only when format=png.
- R4: Changing engine or tool version MUST invalidate prior cache entries.
- R5: ConflictRegistry MUST raise when different signatures map to same explicit basename.

## Future Extensibility Hooks
- Add `Renderer` stage for SVG optimization (minification) after PdfToSvgRenderer.
- Introduce parallel executor dispatch once `:latexmath-jobs:` activated—PipelineSignature will then include `jobs` value.

## Traceability Mapping
| Spec FR | Entity / Section | Notes |
|---------|------------------|-------|
| FR-001 | MathExpression.entry_type | Block & inline only |
| FR-003 | RenderRequest.engine | Engine enumeration |
| FR-011 | Cache Key Composition | All listed fields included |
| FR-013 | DiskCache atomic semantics | Temp + rename + optional lock |
| FR-018 | RenderRequest.ppi validation | Range enforced at build |
| FR-023 | Timeout exceptions | RenderTimeoutError |
| FR-040 | ConflictRegistry | Pre-write registration |

