# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Added
- Cross-platform path utilities (`PathUtils`) to normalize Windows separators and
  honor `imagesoutdir`/`cachedir` precedence across platforms.
- New governance audit script (`scripts/governance_audit.rb`) with corresponding
  coverage spec to ensure every FR is referenced by implementation tasks.
- Extended integration and performance coverage for FR-032/039/044 via large
  formula timing, format variants, spawn counting, cache enumeration guards, and
  memory retention spot checks.

## [0.1.0] - 2025-10-05
### Added
- Initial public release of the `asciidoctor-latexmath` extension.
- Offline LaTeX rendering pipeline for `latexmath` blocks and inline macros with
  deterministic caching and atomic writes.
- SVG, PDF, and PNG output stages with tool selection and timeout enforcement.
- Accessibility defaults (`role="math"` and `alt` text) and structured
  placeholders for logged errors.
- Statistics collection that reports render counts and cache hits via a single
  log line.
- Comprehensive RSpec + Aruba suite covering contracts, integration scenarios,
  performance smoke tests, and governance checks.
- Documentation set including README attribute tables, design reference,
  quickstart samples, and runnable examples in `examples/`.

