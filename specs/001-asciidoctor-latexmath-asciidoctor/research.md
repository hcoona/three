# Phase 0 Research – Asciidoctor Latexmath Offline Rendering

Date: 2025-10-02
Spec: ./spec.md
Branch: 001-asciidoctor-latexmath-asciidoctor

## Objectives
Establish concrete design & decisions for v1 implementation of the asciidoctor-latexmath extension under Constitution v2.0.0, resolving any residual unknowns and aligning previous design drafts (DESIGN.md, AsciidoctorLatexmathAttributes.md, LEARN.md) with clarified scope (notably P1 contraction to *Processor Duo Only*).

## Resolved Unknowns & Decisions
| Topic | Decision | Rationale | Alternatives Considered |
|-------|----------|-----------|--------------------------|
| Entry Points | ONLY BlockProcessor `[latexmath]` + InlineMacroProcessor `latexmath:[...]` | Aligns with Constitution P1; simplification & parity with clarified scope (removes BlockMacro overhead). | Keep BlockMacro (rejected: adds surface w/o unique value) |
| BlockMacro in DESIGN.md | Will remove `LatexmathBlockMacroProcessor` references in next DESIGN.md update PR | Keep docs consistent with Constitution; avoids confusion. | Leave deprecated (rejected: ambiguity) |
| External Engines | Support `pdflatex`, `xelatex`, `lualatex`, `tectonic` selectable via `pdflatex` attribute | Matches FR-003; reuse common attribute; user familiarity from asciidoctor-diagram/TikZ. | Separate attribute per engine (rejected: noisy) |
| SVG Conversion | Prefer `dvisvgm`; fallback allowed via explicit `pdf2svg` attribute override; NO silent runtime fallback | Determinism (P5); explicit failure improves diagnosability. | Auto chain detection fallback (rejected: hidden variability) |
| PNG Tools | Ordered probe at startup: `pdftoppm` > `magick` > `gs`; user can force with `latexmath-png-tool` | Predictable consistent priority; identical to documented attribute table. | Runtime per-request re-probe (rejected: perf) |
| Cache Directory | Precedence: element `cachedir=` > doc `:latexmath-cachedir:` > default `<outdir>/.asciidoctor/latexmath` | FR-037; parity with diagram style; avoids imagesdir mixing. | Use imagesoutdir (rejected: pollutes user asset dir) |
| Attribute Alias (`cache-dir`) | Accept as deprecated alias, log one-time INFO deprecation message | Maintain backward tolerance (some earlier draft docs). | Hard fail (rejected: unnecessary friction) |
| Cache Key Fields | content hash + format + engine + pipeline signature + preamble hash + PPI + tool versions + entry type + extension version | FR-011/012/013/017/037; ensures no stale collisions. | Exclude tool versions (rejected: tool upgrade invalidates rendering) |
| Concurrency | File-level optimistic creation: temp file + atomic rename; optional `.lock` (flock) for long renders | Minimizes contention; POSIX-safe; satisfies FR-013. | Global mutex (rejected: serializes unrelated formulas) |
| Timeout | Default 120s; doc attr `:latexmath-timeout:`; element `timeout=`; integer seconds; kill process tree | FR-023/034; simple mental model. | ms granularity (rejected: over-precision) |
| Security | Enforce no shell escape (`-no-shell-escape` or absence of `-shell-escape`); whitelist args; no user path interpolation beyond resolved commands | FR-017/036; reduces injection risk. | Sandboxing (future) |
| PPI Range | Accept 72..600 inclusive; error otherwise | FR-018; prevents pathological sizes. | Auto clamp (rejected: silent surprises) |
| Statistics | Logged at INFO: counts (renders, cache hits), avg wall time; suppressed on quiet | FR-022/035; lightweight & optional. | Separate attribute flag (rejected: complexity) |
| Target Name Conflicts | Detect & error if same target base + format but different signature; if identical signature, reuse silently | FR-040; prevents silent overwrites. | Overwrite (rejected: data loss) |
| Inline Output Strategy | v1 file-based only; leverage core `:data-uri:` for data URI scenario (no custom toggle) | FR-024/033; reduces scope; leverages existing behavior. | Custom inline strategy attribute (future) |
| Tool Detection Strategy | One-time detection at extension load; store availability & versions; raise early if required tool missing (for chosen format) | FR-020; fail fast before heavy processing. | Per expression detection (rejected: overhead) |

## Attribute Parity vs asciidoctor-diagram
| Concept | Diagram Attribute | Latexmath Attribute | Parity | Notes |
|---------|-------------------|---------------------|--------|-------|
| Output format | `diagram-format` / element `format=` | `latexmath-format` / element `format=` | Yes | Same semantic precedence |
| Cache toggle | `diagram-cache` / `%nocache` | `latexmath-cache` / `%nocache` | Yes | Same fallback to nocache option |
| Cache dir | `diagram-cachedir` | `latexmath-cachedir` | Yes | Same default pattern under outdir |
| Engine override | N/A (tikz uses `pdflatex`) | `pdflatex` | Extended | Additional engines supported |
| Preamble | `preamble` (tikz semantics) | `latexmath-preamble` / element `preamble=` | Yes | Allows injecting packages |
| Keep artifacts | `keepfiles` (diagram) | `latexmath-keep-artifacts` / option `keep-artifacts` | Similar | Name aligned to explicitness |
| PNG DPI | Some diagram converters use `dpi` | `latexmath-ppi` / element `ppi=` | Divergent term | Chose `ppi` to emphasize pixel density; documented clearly |
| Inline data URI | Built-in via Asciidoctor | Same (reuse) | Yes | No custom attribute added |
| BlockMacro support | Provided for many diagram types | Dropped | Intentional difference | Constitution P1 contraction |

## Risk Assessment
| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Missing external tools | Build failure | High | Early detection & actionable error (install hints) |
| Cache corruption in race | Incorrect reuse | Low-Med | Atomic write + optional lock, checksum verify |
| Long-running LaTeX hang | CI timeout | Med | Enforced timeout kill + error context |
| Platform-specific path quirks | Failure on Windows | Med (future) | Normalize paths; initial CI Linux/macOS, add Windows later |
| Large preamble performance | Slower compile | Low | Cache key includes preamble hash; encourage minimal preamble |

## Performance & Scale Expectations
- Cold render target (pdflatex+svg) typical < 1500 ms per formula; goal p95 < 2500 ms with moderate preamble (<20 lines).
- Warm cache retrieval O(1) file copy / reference; goal < 5 ms overhead per formula.
- Designed for documents up to ~5,000 formulas (memory footprint minimal; streaming pipeline — no bulk retention).

## Open Items (Deferred / Future Work)
| Item | Reason for Deferral |
|------|---------------------|
| Parallel in-process jobs (`:latexmath-jobs:`) | Constitution & FR-038: reserved keyword, future optimization |
| Fine-grained data URI policy | FR-033 future enhancement |
| Cache eviction strategy | FR-039 out-of-scope v1 |
| MathJax fallback | Non-goal per spec |
| SVG optimization stage | Possible post-v1 plugin stage |

## Decisions Summary (Traceability to Principles)
- P1: Removed BlockMacro → update DESIGN.md (MAJOR already accounted).
- P2: Interfaces enumerated (Processors, Renderer, Cache) prior to implementation; tests first plan in tasks.md.
- P3: Attribute parity matrix drafted (see above) with explicit differences.
- P4: Style (add standardrb), reproducible build (deterministic cache key), semantic versioning compliance.
- P5: Deterministic pure pipeline; cache key enumerated; tool & timeout sanitization defined.

## Next Steps
1. Author contract & behavior RSpec specs for: attribute resolution, cache key composition, tool detection, pipeline assembly, conflict detection, timeout behavior, option flags.
2. Implement skeleton modules returning placeholder values to satisfy constant loading for specs.
3. Incrementally drive implementation to green.

All identified ambiguities resolved; proceed to Phase 1.
