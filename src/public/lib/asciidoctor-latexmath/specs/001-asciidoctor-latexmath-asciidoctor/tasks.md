# Tasks: Asciidoctor Latexmath Offline Rendering Extension (Regenerated)

**Input**: Design documents from `/workspace/asciidoctor-extensions/asciidoctor-latexmath/specs/001-asciidoctor-latexmath-asciidoctor/`
**Prerequisites**: plan.md, research.md, data-model.md, contracts/ (renderer_pipeline, cache_key, processors, error_handling, statistics), quickstart.md

## Legend
[P] = Parallel-capable (different files, no unmet dependency)
All contract & integration specs MUST exist and FAIL before dependent implementation (Principle P2 / P1 / P5).

## Generation Sources
- Contracts (5) → contract spec tasks (renderer pipeline, cache key, processors, error handling, statistics)
- Data Model (8 entities) → model/interface stub tasks
- Acceptance Scenarios (5) → integration spec tasks
- Research decisions → setup, security, timeout, concurrency, statistics, attribute parity tasks

## Phase 3.1: Setup
- [X] T001 Create gem skeleton & version file: `lib/asciidoctor-latexmath.rb`, `lib/asciidoctor/latexmath/version.rb` (ensure NO BlockMacro registration per P1).
- [X] T002 Add RSpec + Aruba setup: `spec/spec_helper.rb`, enable `aruba/rspec`, temp dir helpers (FR-041).
- [X] T003 [P] Add StandardRB config `.standard.yml` + Rake task `lint` + `bundle exec standardrb` CI target (P4).
- [X] T004 [P] Add GitHub Actions CI workflow `.github/workflows/ci.yml` (matrix: 3.1,3.2,3.3; steps: install, lint, spec) (P4).
- [X] T005 [P] Initialize `.rspec` with color + `--require spec_helper`.

## Phase 3.2: Tests First (Contracts & Integration) – ALL must fail initially
### Contract Specs
- [X] T006 [P] Processors contract spec: `spec/processors/processors_contract_spec.rb` (仅 2 处理器, 无 BlockMacro/TreeProcessor, 无 Mathematical, alias warn once, precedence outline) (P1,P2,FR-025).
- [X] T007 [P] Renderer pipeline contract spec: `spec/rendering/pipeline_contract_spec.rb` (stage order 固定; 变更需版本 bump; timeout 占位) (P5) *不再测试 pipeline_signature 字段*。
- [X] T008 [P] Cache key & disk cache contract spec: `spec/cache/cache_key_contract_spec.rb` (字段顺序: ext_version, content_hash, format, preamble_hash, ppi, entry_type; engine/tool 切换不变; atomic write expectations) (P5 / FR-011)。
- [X] T009 [P] Error handling contract spec: `spec/errors/error_handling_contract_spec.rb` (error classes enumerated, on-error policies abort/log behaviors pending) (FR-014, FR-045, FR-046)。
- [X] T010 [P] Statistics contract spec: `spec/statistics/statistics_contract_spec.rb` (单行格式 regex, 抑制规则, 四舍五入) (FR-022)。
### Integration (Acceptance) Specs
- [X] T011 [P] Primary story end-to-end: `spec/integration/primary_story_spec.rb` (svg default, caching across runs) (Scenario 1)。
- [X] T012 [P] Missing tool failure: `spec/integration/missing_tool_spec.rb` (simulate absent dvisvgm & pdf2svg; actionable error) (Scenario 2 / FR-004)。
- [X] T013 [P] Nocache png with ppi: `spec/integration/nocache_png_spec.rb` (%nocache twice, ppi valid & invalid → invalid pending) (Scenario 3 / FR-007, FR-018) (FR-015 merged into FR-007)。
- [X] T014 [P] Concurrency atomicity: `spec/integration/concurrency_spec.rb` (two processes same formula: single artifact) (Scenario 4 / FR-013)。
- [X] T015 [P] Stem alias equivalence: `spec/integration/stem_alias_spec.rb` (stem: vs latexmath: single render) (Scenario 5 / FR-001, FR-011)。
### Additional Behavior Specs (Pre-Implementation)
- [X] T016 [P] Accessibility markup spec: `spec/integration/accessibility_spec.rb` (alt=raw latex, role=math, data-latex-original) (FR-043)。
- [X] T017 [P] Attribute precedence spec: `spec/integration/attribute_precedence_spec.rb` (element `cachedir=` > doc `:latexmath-cachedir:` > imagesdir fallback > default; positional overrides) (FR-006, FR-009, FR-016, FR-037)。
- [X] T018 [P] Alias deprecation spec: `spec/integration/deprecated_alias_spec.rb` (using legacy `cache-dir=` once emits info log naming canonical `cachedir`) (FR-037)。
- [X] T019 [P] Conflict detection spec: `spec/integration/conflict_detection_spec.rb` (different signatures same basename error) (FR-040)。
- [X] T020 [P] Error placeholder spec: `spec/integration/error_placeholder_spec.rb` (on-error=log placeholder sections order) (FR-046)。
- [X] T077 [P] Output path resolution matrix spec: `spec/integration/output_path_resolution_spec.rb` (imagesoutdir/outdir/imagesdir precedence + basename 路径越界信任策略接受性) (FR-008)。
- [X] T078 [P] Engine selection basic spec: `spec/integration/engine_selection_basic_spec.rb` (global `:pdflatex:` vs doc `:latexmath-pdflatex:` vs block `pdflatex=` 覆写顺序；切换 pdflatex↔xelatex 不致缓存失效) (FR-003, FR-049, FR-050)。
- [X] T080 [P] Cache hit zero-spawn spec: `spec/integration/cache_hit_zero_spawn_spec.rb` (两次构建：第二次断言 0 外部进程 spawn，通过 stub 计数) (P5, FR-011, NFR-002)。

## Phase 3.3: Core Models & Interfaces (stubs only; run after T006–T020 exist)
- [X] T021 [P] Stub MathExpression: `lib/asciidoctor/latexmath/math_expression.rb` (attrs, TODO invariants).
- [X] T022 [P] Stub RenderRequest: `lib/asciidoctor/latexmath/render_request.rb`.
- [X] T023 [P] (Reserved) pipeline signature 概念已退役（参见 spec 历史说明）；编号仅占位，无需实现文件。
- [X] T024 [P] Stub CacheEntry: `lib/asciidoctor/latexmath/cache/cache_entry.rb`.
- [X] T025 [P] Skeleton DiskCache: `lib/asciidoctor/latexmath/cache/disk_cache.rb` (fetch/store/with_lock raise NotImplementedError).
- [X] T026 [P] Stub ToolchainRecord: `lib/asciidoctor/latexmath/rendering/toolchain_record.rb`.
- [X] T027 [P] Renderer interface/base: `lib/asciidoctor/latexmath/rendering/renderer.rb` (IRenderer methods + result struct).
- [X] T028 [P] ConflictRegistry: `lib/asciidoctor/latexmath/support/conflict_registry.rb` (register! skeleton).

## Phase 3.4: Core Services & Pipeline
- [X] T029 Attribute resolver: `lib/asciidoctor/latexmath/attribute_resolver.rb` (precedence chain, alias normalization, ppi/timeout validation stubs) depends: T021,T022.
- [X] T030 Tool presence detector: `lib/asciidoctor/latexmath/rendering/tool_detector.rb` (one-time presence detection, 不采集版本号) depends: T026.
- [X] T031 Cache key implementation: `lib/asciidoctor/latexmath/cache/cache_key.rb` (ordered fields, digest) depends: T023,T024,T025.
- [X] T032 Pipeline orchestrator: `lib/asciidoctor/latexmath/rendering/pipeline.rb` (sequential execution, timing hooks) depends: T027,T023.
- [X] T033 Pdflatex renderer stage: `lib/asciidoctor/latexmath/rendering/pdflatex_renderer.rb` depends: T032,T030.
- [X] T034 [P] Pdf→SVG renderer stage: `lib/asciidoctor/latexmath/rendering/pdf_to_svg_renderer.rb` depends: T033.
- [X] T035 [P] Pdf→PNG renderer stage: `lib/asciidoctor/latexmath/rendering/pdf_to_png_renderer.rb` depends: T033.
- [X] T036 Processors (block + inline): `lib/asciidoctor/latexmath/processors/{block_processor,inline_macro_processor}.rb` depends: T029,T030,T031,T032,T033–T035,T028.
- [X] T037 Extension wiring entrypoint: `lib/asciidoctor-latexmath.rb` (register only processors) depends: T036.

## Phase 3.5: Behavior Implementation & Edge Cases
- [X] T038 Cache store + hit logic: integrate DiskCache + CacheKey (FR-011) depends: T031,T025,T037.
- [X] T039 Atomic write & concurrency lock: temp + rename + optional lock file (FR-013) depends: T038.
- [X] T040 Conflict detection raising TargetConflictError (FR-040) depends: T028,T036,T038.
- [X] T041 Nocache & keep-artifacts flows (FR-007, FR-015, FR-021) depends: T036,T038.
- [X] T042 Timeout enforcement + process kill (FR-023, FR-034) depends: T033–T035.
- [X] T043 Security restrictions (no shell-escape, sanitized args) (FR-017, FR-036) depends: T033–T035.
- [X] T044 Attribute precedence & alias normalization full logic (makes T017/T018 green) depends: T029,T036.
- [X] T045 Error placeholder + on-error policy (FR-014, FR-045, FR-046) depends: T036,T041,T042.
- [X] T046 Statistics collection & emission (FR-022, FR-035) depends: T038,T042.
- [X] T047 Accessibility markup injection (FR-043) depends: T036.
- [X] T048 PPI validation & range errors (FR-018) depends: T029,T036.
- [X] T049 Stem alias handling (FR-001, FR-011) depends: T036.
- [X] T050 Cache key tool signature integration (FR-020) depends: T030,T031.

## Phase 3.6: Performance, Determinism & Documentation
- [X] T051 [P] Performance smoke spec: `spec/performance/render_perf_spec.rb` (skip if tools missing) depends: T042,T046.
- [X] T052 [P] Determinism spec: `spec/integration/determinism_spec.rb` (two runs identical checksum + no re-render) depends: T038,T050.
- [X] T053 [P] Statistics suppression & rounding spec (make T010/T046 green) `spec/statistics/statistics_behavior_spec.rb` depends: T046.
- [X] T054 README attributes & usage table (FR-027) depends: T044,T046,T047.
- [X] T055 Update DESIGN.md remove BlockMacro references; add cache key diagram depends: T054.
- [X] T056 Add examples: `examples/block_svg.adoc`, `examples/png_cached.adoc`, `examples/inline_mix.adoc`, `examples/error_placeholder.adoc` depends: T045,T041.
- [X] T057 Add CHANGELOG entry & set version 0.1.0 depends: T054–T056.
- [X] T058 Release rake tasks + gem build verification (reproducibility spot-check) depends: T057.
- [X] T059 [P] Performance baseline doc `specs/001-asciidoctor-latexmath-asciidoctor/performance-baseline.md` (capture p50/p95 cold/warm) depends: T051.
- [X] T060 Final verification checklist (run suite twice; capture stats line; ensure no duplicate stats output) depends: T051–T058.

## Phase 3.7: Additional Coverage (Remediation A3–A5, U4, C1–C5, C7–C9)
- [X] T061 SVG tool priority spec: `spec/integration/svg_tool_priority_spec.rb` (dvisvgm chosen when both present; logs `latexmath.svg.tool=dvisvgm`; simulate only pdf2svg present chooses pdf2svg; simulate none → FR-004 error) (FR-047).
- [X] T062 Engine precedence & normalization spec: `spec/integration/engine_precedence_spec.rb` (element > doc > global > default; adds flags if missing; no fallback to other engine on missing executable) (FR-049, FR-050 + A5)。
- [X] T063 Hash collision avoidance spec: `spec/cache/hash_collision_spec.rb` (simulate 16-char prefix collision → 升级为 32-char 基名无数字后缀；缓存键仍用全 64；可选 stub 二次冲突) (FR-010, FR-011 新策略)。
- [X] T064 Unsupported attribute values error spec: `spec/integration/unsupported_attribute_values_spec.rb` (illegal ppi, timeout non-integer, on-error invalid → actionable errors per FR-019) (FR-018, FR-034, FR-045, FR-019)。
- [ ] T065 (Removed) 路径遍历防御测试取消：信任模型允许 `..`，参见 spec A4/I1 说明。
- [X] T066 Mixed formats same doc spec: `spec/integration/mixed_formats_spec.rb` (svg + png + pdf concurrently; independent cache entries; no cross pollution) (FR-028, FR-021, FR-011)。
- [X] T067 Unicode diversity spec: `spec/integration/unicode_diversity_spec.rb` (combining marks, CJK, Emoji, blackboard bold; all cache hit second run; byte-wise hash) (FR-029 U4)。
- [X] T068 Engine normalization no-cache independence spec: `spec/integration/engine_normalization_no_cache_spec.rb` (explicit custom pdflatex already includes flags → no duplicate append; missing flag appended once; output identical except flag order deterministic) (FR-049, FR-050 determinism)。
- [X] T069 Atomic overwrite spec: `spec/integration/atomic_overwrite_spec.rb` (pre-create target file; render new non-cache-hit overwrites atomically; mtime changes; no prior hash read) (FR-051)。
- [X] T070 Processors invariants spec: `spec/processors/invariants_spec.rb` (仅两个处理器 & 无 TreeProcessor & 无 Mathematical) (FR-025, P1)。
- [X] T071 Missing tool hint spec: `spec/integration/missing_tool_hint_spec.rb` (缺失 dvisvgm 提供 hint 模板) (FR-030)。
- [X] T072 Tool summary log spec: `spec/integration/tool_summary_spec.rb` (一次 info 行, 固定顺序) (FR-031)。
- [X] T073 Large formula timing spec: `spec/performance/large_formula_timing_spec.rb` (长度>3000 bytes 输出 timing 行) (FR-032)。
- [X] T074 No eviction behavior spec: `spec/cache/no_eviction_behavior_spec.rb` (断言无后台 eviction 线程 & 日志) (FR-039)。
- [X] T075 Format variants spec: `spec/integration/format_variants_spec.rb` (pdf 与 png 明确覆盖) (FR-002)。
- [X] T076 Inline output structure spec: `spec/integration/inline_output_structure_spec.rb` (`<img>` 引用, 无 data URI 生成) (FR-024)。
- [X] T079 [P] Pipeline stage immutability spec: `spec/integration/pipeline_stage_immutability_spec.rb` (缺失首选 svg 工具时 fail-fast 而非动态删减/重排阶段；stage list fingerprint 不变) (P5, FR-047, FR-011)。
- [X] T081 [P] Terminology enforcement spec: `spec/integration/terminology_enforcement_spec.rb` (legacy alias 仅首次日志；重复无新增；内部使用 canonical 名称) (NFR-007, FR-037)。
- [X] T082 [P] No internal parallel / no dynamic stage insertion spec: `spec/integration/no_internal_parallel_spec.rb` (单进程内渲染多个表达式验证无并行线程/进程 spawn 超出未命中数；断言阶段列表不因 tool availability 变化) (FR-038, P5, complements T079)。
- [X] T083 [P] Windows path separator compatibility spec: `spec/integration/windows_path_compat_spec.rb` (使用 Windows 风格 `imagesdir`/`cachedir` 反斜杠路径 + 混合 `..` 段；同时与 POSIX 风格组合；断言最终输出/缓存目录解析一致且不依赖运行平台) (FR-008, FR-037, C1)。
- [X] T084 [P] Mixed miss spawn count spec: `spec/integration/mixed_miss_spawn_count_spec.rb` (两轮构建：首轮 N 未命中；第二轮新增 K 表达式；断言第二轮新增外部进程 = K) (FR-044 MUST(4))。
- [X] T085 [P] Cache hit no directory enumeration spec: `spec/integration/cache_hit_no_enum_spec.rb` (预填充缓存后命中运行，spy 断言未调用 Dir.glob/Find；外部进程=0) (FR-044 MUST(2)/(3))。
- [X] T086 [P] Memory retention spot-check spec: `spec/performance/memory_retention_spec.rb` (大量表达式渲染后 RSS 与对象数未线性增长；无全量产物集合驻留) (FR-044 MUST(5) 观察)。
- [X] T087 [P] Governance coverage audit task: `spec/governance/coverage_audit_spec.rb` + `scripts/governance_audit.rb` (交叉比对 FR ↔ 任务引用；输出缺失映射提示) (FR-026 C1)。

Updated Dependencies (Additions / Adjustments)
T061 → T047
T062 → T033,T036
T063 → T031,T038
T064 → T029,T036
T066 → T036,T038
T067 → T036,T038
T068 → T033,T036
T069 → T038,T039
T070 → T006 (shares processors contract) & T002
T071 → T012
T072 → T030 (tool detector) & T011 (ensures at least one render path triggers)
T073 → T051 (performance infra) or directly after T046 (stats) — choose after T046
T074 → T038 (cache implemented)
T075 → T011 (baseline) & T036
T076 → T011,T036
T077 → T017
T078 → T002
T079 → T007,T061
T080 → T008
T081 → T018
T082 → T007,T036
T083 → T017
T084 → T080
T085 → T038
T086 → T051
T087 → T060

Validation Checklist (Additions / Revised)
- [X] SVG tool priority (T061) green before renderer fallback logic changes.
- [X] Engine precedence & normalization (T062,T068) covers A5 no-fallback & flag append.
- [X] Unicode diversity (T067) covers U4 enumerated set.
- [X] Hash collision scenario (T063) 16→32 字符升级策略落实（无数字后缀）。
- [X] Unsupported value actionable errors (T064) align FR-019 contract.
- [X] Tool summary log (T072) matches FR-031 format.
- [X] Large formula timing (T073) matches FR-032 format & threshold.
- [X] Mixed formats isolation (T066) coverage for FR-028.
- [X] Atomic overwrite (T069) validates FR-051.
- [X] Processors invariants (T070) enforces FR-025 / P1.
- [X] Missing tool hint (T071) covers FR-030.
- [X] No eviction behavior (T074) covers FR-039.
- [X] Output path resolution matrix (T077) covers FR-008 precedence & traversal acceptance.
- [X] Pipeline stage immutability (T079) enforces P5 no dynamic stage change.
- [X] Cache hit zero-spawn (T080) validates P5 / FR-011 / NFR-002。
- [X] Terminology enforcement (T081) single deprecation log (NFR-007, FR-037)。
- [X] Engine selection basic precedence (T078) covers FR-003 existence。
- [X] No internal parallel execution (T082) covers FR-038 (串行保证)。
- [X] Windows path compatibility (T083) covers FR-008, FR-037 跨平台路径解析。
- [X] Mixed miss spawn count (T084) covers FR-044 MUST(4) (M>0)。
- [X] No directory enumeration on cache hit (T085) covers FR-044 MUST(3)。
- [X] Memory retention spot-check (T086) observes FR-044 MUST(5) (非门控)。
- [X] Governance coverage audit (T087) covers FR-026 流程治理映射。

## Dependencies (Summary)
T002 → T001
T003,T004,T005 → T001
T006–T020 → T002
T021–T028 → T006–T020 & T002
T029 → T021,T022
T030 → T026
T031 → T023,T024,T025
T032 → T027,T023
T033 → T032,T030
T034,T035 → T033
T036 → T029,T030,T031,T032,T033,T034,T035,T028
T037 → T036
T038 → T031,T025,T037
T039 → T038
T040 → T028,T036,T038
T041 → T036,T038
T042 → T033,T034,T035
T043 → T033,T034,T035
T044 → T029,T036
T045 → T036,T041,T042
T046 → T038,T042
T047 → T036
T048 → T029,T036
T049 → T036
T050 → T030,T031
T051 → T042,T046
T052 → T038,T050
T053 → T046
T054 → T044,T046,T047
T055 → T054
T056 → T045,T041
T057 → T054,T055,T056
T058 → T057
T059 → T051
T060 → T051,T052,T053,T058
T077 → T017
T078 → T002
T079 → T007,T061
T080 → T008
T081 → T018

## Parallel Execution Examples
Group A (Contract Specs): T006 T007 T008 T009 T010
Group B (Acceptance Specs): T011 T012 T013 T014 T015
Group C (Behavior Pre-impl Specs): T016 T017 T018 T019 T020 T077 T078 T080
Group D (Entity Stubs): T021 T022 T023 T024 T025 T026 T027 T028
Group E (Renderer Stages): T034 T035
Group F (Perf/Determinism/Stats later): T051 T052 T053

## Validation Checklist
- [ ] 5 contract files → 5 contract spec tasks (T006–T010)
- [ ] 8 entities → 8 stub tasks (T021–T028)
- [ ] 5 acceptance scenarios → 5 integration tests (T011–T015)
- [ ] Additional critical behaviors (accessibility, alias, conflict, placeholder, precedence) covered (T016–T020)
- [ ] Tests precede implementation (T006–T020 before T021+)
- [ ] External tool detection + timeout tasks (T030,T042)
- [ ] Security restrictions task (T043)
- [ ] Concurrency & atomic write tasks (T039,T014 integration, T039 impl, plus serial guarantee T082)
- [ ] Statistics tasks (T010 contract, T046 impl, T053 behavior)
- [ ] Accessibility task (T047 impl + T016 spec)
- [ ] Determinism & performance tasks (T051,T052)
- [ ] Documentation & release tasks (T054–T058)
- [ ] Performance baseline doc (T059) for FR-042 future hard thresholds
- [ ] Final verification (T060)

### Reserved / Deferred Requirement Tracking
- FR-005: merged into FR-011; coverage delivered via T038 (cache store) and T050 (tool signature integration).
- FR-012: merged into FR-010 / FR-011; addressed through T031 (cache key implementation) and T063 (hash collision strategy).
- FR-033: deferred granular data URI strategy; documented in spec Clarifications, to be scheduled in future milestone.

## Notes
- Each task commit message: "T0xx: <short description>".
- Specs may use `pending` for behavior dependent on future tasks (reduce brittle red states).
- Aruba specs MUST isolate FS; never rely on global HOME (FR-041).
- Keep contract specs minimal—expand only after base green to avoid over-constraining implementation order.
- Statistics line is immutable contract; any future field addition requires new FR + version bump.
 - T077 为后补测试未重排历史编号；新增任务使用顺序递增 (T078+)。
 - 新增任务：T078 (引擎选择基础), T079 (阶段不变), T080 (缓存命中零进程), T081 (术语弃用一次性日志)。
 - 新增任务（本次分析修复）：T082 (串行/无动态阶段补充 FR-038), T083 (Windows 路径兼容 C1)。
 - FR-048 (引擎网络策略) 依据产品决策 **暂不设专门测试**（U2），若后续出现差异/回归将追加任务。

---
*Based on Constitution v3.1.0 – see `.specify/memory/constitution.md`*

