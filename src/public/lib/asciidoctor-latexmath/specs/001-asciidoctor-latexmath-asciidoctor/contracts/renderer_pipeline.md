# Contract: Renderer Pipeline

## Purpose
Defines deterministic transformation from LaTeX source → final asset (svg/pdf/png) with no side effects beyond declared output & artifact directories. (Note: legacy standalone pipeline signature removed; determinism now enforced via fixed stage list + extension version in cache key.)

## Interfaces
```ruby
module Asciidoctor
  module Latexmath
    module Rendering
      # Every concrete stage implements this interface
      class IRenderer
        # @return [String] human readable name (e.g., 'pdflatex')
        def name; end
  # (signature fragment removed – stage stability validated by tests)
        # @param req [RenderRequest]
        # @param ctx [RenderContext]
        # @return [RendererResult] { output_path:, format:, intermediate?: } output_path absolute
        # @raise [MissingToolError, RenderTimeoutError, StageFailureError]
        def render(req, ctx); end
      end

      # Rendering Orchestrator (Composite)
      class Pipeline
        # @param stages [Array<IRenderer>]
        def initialize(stages); end
        # Executes sequentially; passes intermediate output as input to next stage.
        # Aborts fast on first failure.
        # @return final output absolute path
        def execute(req, ctx); end
      end
    end
  end
end
```

## Stage Responsibilities
| Stage | Input | Output | Errors |
|-------|-------|--------|--------|
| PdflatexRenderer | `.tex` file (constructed) | `.pdf` | Missing tool, non-zero exit, timeout |
| PdfToSvgRenderer (dvisvgm/pdf2svg) | `.pdf` | `.svg` | Missing tool, invalid pdf |
| PdfToPngRenderer (pdftoppm/magick/gs) | `.pdf` | `.png` | Missing tool, conversion failure |

## Context Object
```ruby
RenderContext = Struct.new(
  :tmp_dir,          # base temp working dir
  :artifacts_dir,    # nullable; when keep-artifacts enabled
  :logger,           # Asciidoctor logger wrapper
  :toolchain,        # Hash<Symbol, ToolchainRecord>
  keyword_init: true
)
```

## Determinism Guarantees
- No mutation of global ENV.
- No dependency on system clock except for metadata `created_at` (excluded from cache key).
- Stage list & order fixed; any change REQUIRES extension version bump (cache key change) and corresponding spec/test update.

## Timeouts
- Each external command wrapped with controller that enforces `req.timeout_secs`.
- On timeout: process group killed; partial files moved to artifacts if requested; raise `RenderTimeoutError`.

## Logging Requirements
- INFO: start + completion of each stage (duration ms).
- DEBUG: full command line (sanitized), working directory, tool presence flags (no versions recorded).
- ERROR: standardized failure record with extracted tail of stderr (≤ 2KB) for context.

## Test Contracts
- `spec/rendering/pipeline_contract_spec.rb`: verifies stage order stable & change without version bump causes test failure.
- `spec/pipeline/timeout_spec.rb`: simulates hanging process (mock) triggers timeout.
- `spec/pipeline/determinism_spec.rb`: identical inputs produce identical outputs + cache behavior.

## Violations (Failure Modes)
| Violation | Detection Test |
|-----------|----------------|
| Stage writes outside tmp_dir | sandbox path assertion test |
| Stage mutates ENV | snapshot ENV diff test |
| Stage order nondeterministic | randomized invocation test must still produce identical outputs & unchanged cache key |

