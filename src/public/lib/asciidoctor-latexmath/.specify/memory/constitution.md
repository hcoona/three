<!--
Sync Impact Report
Version: 3.0.0 -> 3.1.0
Modified Principles:
	P5 Deterministic Rendering, Caching & Security (replace implicit pipeline_signature notion with explicit fixed stage list + cache key field set; clarify exclusion of tool names/versions; add rule forbidding dynamic stage insertion)
Added Sections: none
Removed Sections: none
Templates Updated:
	.specify/templates/plan-template.md ✅ (no direct pipeline_signature reference; no change required)
	.specify/templates/spec-template.md ✅ (spec now references historical removal only)
	.specify/templates/tasks-template.md ✅ (tasks updated: no pipeline_signature field tests; cache key fields enumerated)
	.specify/templates/agent-file-template.md ✅ (no pipeline_signature wording)
Follow-up TODOs:
 - README / DESIGN: add short note that stage list change requires version bump (to align with updated P5)
 - Remove any stale references if future branches still mention pipeline_signature digest
Rationale: Clarify determinism mechanism without adding new mandatory data in cache key. This is an additive governance clarification ⇒ MINOR bump.
-->

# asciidoctor-latexmath Constitution

## Core Principles

### P1. Processor Duo Only (NON-NEGOTIABLE)
Rules:
- MUST implement exactly two entrypoints: BlockProcessor and InlineMacroProcessor.
- MUST NOT implement or register a BlockMacroProcessor for `latexmath` (syntax `latexmath::[]` is out-of-scope by design).
- MUST NOT register or rely on TreeProcessor (global AST mutation prohibited).
- MUST NOT depend on `Mathematical` gem or reuse its runtime image generation logic.
- Processors MUST delegate rendering to a shared, pure rendering pipeline (no side effects outside provided dirs).
- Attribute & option resolution MUST be deterministic and local to each invocation.
Out-of-Scope Examples (MUST NOT add later without MAJOR bump): Block macro syntax, auto-math environment inference via AST sweeping.
Rationale: Tighter surface reduces maintenance & cognitive load; BlockMacro adds negligible value over block form while increasing complexity.

### P2. Interface-First Waterfall with Enforced TDD
Rules:
- Public API (processor names, attribute matrix, renderer pipeline contract) MUST be specified before code.
- For each new capability: write failing RSpec contract + behavior tests BEFORE implementation (Red → Green → Refactor).
- Waterfall stages (Interface Spec → Tests → Implementation → Documentation → Release) MUST NOT be reordered.
- Any change to a declared interface REQUIRES a matching spec diff + version evaluation (see Governance).
Rationale: Freezes expectations early, prevents scope drift, elevates test artifacts to first-class design assets.

### P3. Configuration & Behavior Parity with Asciidoctor-Diagram
Rules:
- Attribute naming, precedence (block > document > global), and cache invalidation semantics MUST mirror
	Asciidoctor-Diagram where an equivalent concept exists.
- When conflicts arise between Asciidoctor-Diagram and asciidoctor-mathematical, MUST follow Asciidoctor-Diagram.
- Unsupported formats or attributes MUST fail fast with a clear message listing supported values.
- Converter discovery & external tool resolution MUST be cached per process execution and not recomputed per node.
Rationale: Lowers cognitive load for existing users and reduces documentation surface.

### P4. Quality, Style & Toolchain Discipline
Rules:
- MUST use Standard Ruby (standardrb) for formatting & linting; CI MUST fail on style violations.
- MUST maintain 100% test pass before merging; mutation or coverage tooling SHOULD guard critical rendering logic.
- Semantic Versioning of the gem: MAJOR for breaking API/attribute changes, MINOR for additive behavior, PATCH for fixes.
- Build artifacts (gem, cache metadata) MUST be reproducible (content hash = deterministic pipeline signature + sources).
- All external command invocations (e.g., `pdflatex`, `pdf2svg`) MUST be wrapped with argument sanitization & timeout.
Rationale: Enforces consistent contributor experience and dependable releases.

### P5. Deterministic Rendering, Caching & Security
Rules:
- Rendering pipeline MUST be pure: output = f(content, normalized_attributes, fixed_stage_list) with no ambient state or hidden globals.
- Stage list MUST be fixed per output format (svg|pdf|png) for a released version. Any addition, removal or reordering of stages MUST trigger an extension version bump (SemVer) before release.
- Implementation MUST NOT dynamically insert, drop or reorder stages at runtime based on tool availability, environment, cache hit status, or heuristics. Missing required tools MUST raise an actionable error (fail fast) rather than mutating the stage list.
- Cache key MUST include ONLY: content hash, normalized attribute signature (sorted & filtered), output format, preamble hash (if any), PPI (for raster), entrypoint type (block|inline), extension (gem) version. (See spec FR-011 for ordering.)
- Cache key MUST NOT include: LaTeX engine or converter tool names, nor their versions, nor absolute filesystem paths, nor timeout values, nor stage list fingerprints (the version bump encodes those changes implicitly).
- Switching LaTeX engine or converter tool (with included cache key fields unchanged) MUST yield a cache hit (no external process invocation).
- Documentation MUST warn that differing toolchains MAY produce byte-level output diffs (fonts/metadata). Teams needing strict byte-for-byte reproducibility MUST pin a single toolchain or opt into a future strict mode.
- On cache hit MUST skip external tool execution entirely (zero process spawn).
- Shell execution MUST enforce argument allow‑listing, timeout, and resource limits; MUST NOT enable shell-escape.
- Inline embedding (data URI / embedded SVG) is OPTIONAL and disabled by default; enabling it is an additive behavior not altering cache key unless new differentiating fields are introduced via future principle update.
Rationale: Fixed stage list + minimal cache key guarantees deterministic reuse while maximizing cache stability across engine/tool switches. Version bumps form the explicit contract boundary for structural pipeline changes; excluding tool identity prevents unnecessary cache invalidations.

## Development Workflow & Quality Gates
Phases (Waterfall):
1. Interface Definition: Attribute matrix + processor responsibilities + renderer pipeline documented.
2. Test Authoring: RSpec specs (contract + behavior + error cases) created & failing.
3. Implementation Stage 1: Processor scaffolds & pipeline skeleton returning placeholders to make tests executable.
4. Implementation Stage 2: Full rendering logic, caching, external tool integration, performance tuning.
5. Documentation: Update README, DESIGN.md, class diagrams, usage examples, CHANGELOG entries.
6. Release: Version bump rationale recorded; tag + packaged gem.

Quality Gates (must PASS before advancing):
- Gate A (after Phase 1): All declared interfaces documented; no hidden attributes.
- Gate B (after Phase 2): All tests exist & fail for correct reasons (no pending or false positives).
- Gate C (after Stage 2 impl): All contract + behavior tests pass; style & security checks pass.
- Gate D (release): Changelog, version bump classification, reproducible build verification, cache determinism spot check.

## Documentation & Traceability Standards
Requirements:
- DESIGN.md MUST reflect latest pipeline architecture (update concurrently with implementation changes).
- `class-digram-v2.plantuml` (typo kept if present) MUST remain consistent with implemented classes or be updated.
- Every principle reference in specs MUST cite Principle ID (e.g., `# P2`) in test description for traceability.
- README MUST list supported attributes in a table including default, type, example, parity note (if from Diagram).
- LEARN.md serves as evolving knowledge log; entries MUST summarize new decisions with date + principle impact.
Artifacts Mapping:
- Interface Matrix → spec/support/interface_matrix.yml
- Cache Key Definition → documented in DESIGN.md & verified by spec/renderer/cache_spec.rb
- External Toolchain Requirements → README (Installation) section + CI check job.

## Governance
Authority & Scope:
- This Constitution supersedes ad-hoc conventions and individual contributor preferences.
Amendments:
- Proposal via PR including: diff, principle impact summary, version bump type justification (MAJOR/MINOR/PATCH).
- Requires approval by ≥2 maintainers OR unanimous approval if maintainer count <3.
Versioning (Constitution):
- MAJOR: Removal or redefinition of a Principle, or backward-incompatible governance change.
- MINOR: Addition of a new Principle, new mandatory workflow gate, or expansion adding new enforceable rule text.
- PATCH: Clarifications, typo fixes, wording improvements without semantic rule change.
Compliance Review:
- Per PR: Automated lints verify no TreeProcessor usage, disallowed dependencies, and interface documentation presence.
- Quarterly: Manual audit ensuring parity matrix matches current Asciidoctor-Diagram state.
Violation Handling:
- Open issue labeled `constitution-violation` referencing failing Principle IDs + reproduction.
- Fix MUST include regression test when applicable.
Record Keeping:
- Each release PR MUST reference Constitution version and list principle-related changes in CHANGELOG.
Guidance File:
- Primary runtime guidance: LEARN.md (living log) + DESIGN.md (architecture source of truth).

**Version**: 3.1.0 | **Ratified**: 2025-10-02 | **Last Amended**: 2025-10-03
