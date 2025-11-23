# Performance Baseline – asciidoctor-latexmath v0.1.0

Date: 2025-10-05
Feature Branch: `001-asciidoctor-latexmath-asciidoctor`

## Environment

| Component | Value |
| --------- | ----- |
| Host | Linux TDC3774121741 6.11.0-1018-azure x86_64 |
| CPU | Cloud VM (Azure-hosted, 4 vCPU) |
| Ruby | `ruby 3.3.9 (2025-07-24 revision f5c772fc7c) +jemalloc [x86_64-linux]` |
| Bundler | `Bundler version 2.4.20` |
| pdflatex | `pdfTeX 3.141592653-2.6-1.40.28 (TeX Live 2025)` |
| dvisvgm | `dvisvgm 3.4.3` |
| pdf2svg | Present (`pdf2svg --version` usage output) |
| pdftoppm | `pdftoppm version 24.02.0` |

> Commands were executed inside the devcontainer; timings use `/usr/bin/time` (wall clock seconds).

## Methodology

1. Generated a temporary document containing 50 `[latexmath]` blocks with moderate integrals (`/tmp/latexmath-perf.adoc`).
2. Rendered using the extension via:
   ```bash
   bundle exec asciidoctor -r ./lib/asciidoctor-latexmath.rb \
     -a outdir=/tmp/latexmath-perf-out \
     -a imagesoutdir=/tmp/latexmath-perf-out/images \
     /tmp/latexmath-perf.adoc
   ```
3. Collected three **cold** samples (cache directory removed before each run).
4. Collected five **warm** samples (cache retained) after the initial cold sequence.
5. Statistics were derived from the recorded wall-clock times (seconds). Cache hits were observed via the generated artifacts (no external processes after warm-up).

## Results

| Scenario | Sample Count | P50 (s) | P95 (s) | Notes |
| -------- | ------------ | ------- | ------- | ----- |
| Cold render (SVG, 50 blocks) | 3 | 0.46 | 0.46 | Cache cleared between runs (`rm -rf /tmp/latexmath-perf-out`). |
| Warm render (SVG, 50 blocks) | 5 | 0.45 | 0.45 | All formulas served from cache; no external LaTeX processes spawned. |

Raw timing samples:

- Cold: `0.46`, `0.46`, `0.46`
- Warm: `0.45`, `0.45`, `0.45`, `0.44`, `0.45`

## Observations & Follow-ups

- Warm performance stays within 20 ms of cold startup, dominated by Asciidoctor parsing overhead.
- Statistics postprocessor did not emit a visible log line during CLI execution; investigate logger wiring to ensure the single-line summary appears in non-quiet mode (tracked under T072/T080).
- Future baselines should repeat the measurement on macOS and Windows once CI coverage expands, and include PNG/PDF format runs for completeness.
