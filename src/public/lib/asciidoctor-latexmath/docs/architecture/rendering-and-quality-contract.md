# Rendering and Quality Contract

The product constraints P1–P5 and their merge/release quality gates are adapted
from the
[imported contract](https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/public/lib/asciidoctor-latexmath/.specify/memory/constitution.md).
They govern this extension's domain contracts; repository work authorization,
development workflow, local iteration freedom, concurrent branch/stacked-PR
evolution of specs, tests, code, and docs, and record lifecycle follow the
[repository record policy](../../../../../../docs/governance/record-system.md#project-namespaces).
Migration preserves these product constraints and introduces no new behavior or
release authorization. The former constitution-wide versioning, maintainer
quorum, waterfall stage-ordering lock, recurring process audit and living-log
machinery are retired.

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

### P2. Declared Interface and Contract-Test Alignment

Rules:

- Every public API capability (processor names, attribute matrix, renderer pipeline contract) MUST be explicitly specified in the declared interface records and covered by RSpec contract + behavior specs (including failure/error cases) at merge into `main`.
- Any change to a declared interface REQUIRES a matching spec/contract update and version evaluation (the versioning rule in P4).
  Rationale: Prevents undocumented interface drift and keeps test artifacts aligned with declared public behavior without imposing waterfall stage gates on local iteration.

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

## Merge and Release Quality Gates

Current integration examples use `SpecSupport::TmpDir` (`Dir.mktmpdir`) and
block-scoped `Dir.chdir` in the
[RSpec suite](../../spec/spec_helper.rb), not Aruba. Removing unused Aruba
setup does not relax
[FR-041](../../specs/001-asciidoctor-latexmath-asciidoctor/spec.md)'s per-example
temporary-directory, environment-cleanup and real-HOME/global-cache isolation
requirements. Aruba references in dated clarifications, imported baseline
rationale and completed tasks retain their historical scope.

Merge and release gates (evaluated at merge into `main` or gem release rather
than imposing local step-ordering preconditions):

- **Interface & documentation alignment (merge):** All declared interfaces,
  processor responsibilities, attribute matrix entries, and renderer-pipeline
  contracts are documented with no hidden attributes, and `README.md`,
  `docs/architecture/overview.md`, and class diagrams agree with the code.
- **Contract & behavior verification (merge):** RSpec contract, behavior, and
  error-case specs cover the capability with no pending/false-positive specs,
  pass 100%, and `standardrb` style and security checks pass.
- **Release readiness (release):** `CHANGELOG.md`, SemVer bump classification
  rationale, reproducible gem build verification, and cache-determinism spot
  checks pass.

## Documentation and Traceability

These domain obligations survive retirement of the generic constitution
workflow:

- [Architecture](overview.md) MUST reflect the latest pipeline architecture and
  be updated concurrently with implementation changes.
- [`class-digram-v2.plantuml`](../../class-digram-v2.plantuml) MUST remain
  consistent with implemented classes or be updated. Its existing filename is
  retained.
- Every principle reference in specs MUST cite the Principle ID, such as
  `# P2`, in the test description for traceability. This preserves the original
  condition on principle references; it does not add an ID requirement to every
  test.
- The [README](../../README.md) MUST list supported attributes in a table with
  default, type, example and a parity note where derived from Diagram.

| Concern                         | Retained carrier and validation consumer                                                                                                                                                                                                                                     |
| ------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Interface matrix                | [README attribute table](../../README.md), [requirements](../../specs/001-asciidoctor-latexmath-asciidoctor/spec.md), and the existing [contracts](../../specs/001-asciidoctor-latexmath-asciidoctor/contracts/processors.md) own the declared interface.                    |
| Cache-key definition            | [Architecture](overview.md) and the [cache-key contract](../../specs/001-asciidoctor-latexmath-asciidoctor/contracts/cache_key.md); [cache-key contract specs](../../spec/cache/cache_key_contract_spec.rb) and retained cache/integration tests supply executable evidence. |
| External toolchain requirements | README prerequisites/installation and the repository [CI workflow](../../../../../../.github/workflows/ci.yml), whose `ruby-tests` job installs rendering dependencies and runs the discovered gem's RSpec suite.                                                            |

The imported contract named `spec/support/interface_matrix.yml` and
`spec/renderer/cache_spec.rb`; neither path exists in its accepted source tree.
The table corrects those nominal mappings to actual retained carriers without
creating a duplicate interface matrix or claiming comprehensive compliance.
The project-nested workflow file is not independently scheduled by GitHub;
the root workflow is the executable CI route. No current run or additional
platform/tool support is established by these links.

## Domain Review, Violations and Release Evidence

For a relevant PR, establish the absence of prohibited TreeProcessor/dependency
usage and the presence of declared interface documentation. The retained
[processor contract specs](../../spec/processors/processors_contract_spec.rb)
and [invariant specs](../../spec/processors/invariants_spec.rb) check the
processor/dependency restrictions, and the root Ruby job invokes the suite.
The [governance audit](../../scripts/governance_audit.rb) checks FR/task
traceability. The source described special per-PR lints covering all these
assurances; that complete automated mechanism was not established during this
migration. Use actual applicable test results and contextual documentation
review for their respective concerns, while preserving P4 and the quality
gates above. Do not report an unexecuted lint or a full compliance result.

A domain violation report retains the failing Principle IDs and reproduction
evidence in the repository's ordinary Issue/PR carrier. Its fix MUST include a
regression test when applicable. This preserves the diagnostic and regression
obligations without retaining a separate constitution-label workflow.

Changes affecting interfaces or principles retain a principle-impact summary
and the gem SemVer justification required by P2/P4/P5. Each release PR MUST
identify the applicable contract baseline and list principle-related changes in
[CHANGELOG.md](../../CHANGELOG.md). The imported baseline is constitution 3.1.0
at the source linked above; subsequent accepted contract changes are identified
by their canonical path and Git revision. This replaces the separate
constitution version counter without dropping release traceability or granting
a release. The [project portal](../README.md) routes current architecture and
retained research; it does not reinstate the former dated living log.
