# Imported Design Rationale

These sections preserve the unique rationale and baseline assumptions from the
[completed implementation plan](https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/public/lib/asciidoctor-latexmath/specs/001-asciidoctor-latexmath-asciidoctor/plan.md).
Historical platform matrices and check results retain their original scope;
the package manifest and current checks establish current implementation facts.
The old plan generator and progress ledger are retired. Use the
[record portal](../README.md) for current concern routing.

## Summary

离线（本地 LaTeX 工具链）渲染 `latexmath` 块与内联宏表达式为 `svg|pdf|png`，提供与 `asciidoctor-diagram` 风格一致的属性/缓存/目录/冲突处理语义，仅维持 Processor Duo（Block + InlineMacro）。缓存键遵循宪章 P5 / 规范 FR-011：包含内容哈希、归一化属性签名、输出格式、preamble 哈希、PPI（位图）、入口类型、扩展版本；显式不包含任何编译引擎或转换工具的名称 / 版本（引擎或工具切换不应导致缓存失效）。失败策略(`on-error`)与统计输出(单行 MIN 格式)保持可预测、可追踪、可重复构建。所有模糊点已通过 Clarifications 解决；无剩余 NEEDS CLARIFICATION。

## Technical Context

**Language/Version**: Ruby 3.1–3.3 (tested matrix)
**Primary Dependencies**: asciidoctor (~>2.0), standardrb, rspec, aruba (integration sandbox), external CLI tools (`pdflatex|xelatex|lualatex|tectonic`, `dvisvgm|pdf2svg`, `pdftoppm|magick|gs`).
**Storage**: 本地文件系统（缓存与产物目录），无数据库。
**Testing**: RSpec（单元/契约/集成/性能），Aruba（文件系统隔离），Pending 性能基准脚本。
**Target Platform**: Linux / macOS（初始），Windows 后续评估。
**Project Type**: 单库（Ruby gem + Asciidoctor extension）。
**Performance Goals (Exploratory / Non-Binding v1)**: 仅收集基线（非验收门槛）。简单公式定义：Normalization-E 前 UTF-8 字节长度 ≤ 120（与 FR-042 / FR-044 一致）。Exploratory target: 冷启动简单公式 SVG p95 ≤3000ms，PNG p95 ≤3500ms；缓存命中平均附加开销 <5ms（非 fail gate）。若 FR-042 基线输出任一指标超阈值 ⇒ 触发 FR-042 Escalation Workflow（开 issue→新增 MUST FR→README/Spec 更新）。
**Constraints**: 纯离线、无网络依赖；禁止 TreeProcessor & BlockMacro；确定性缓存、可重复构建、超时强制 120s 默认。
**Scale/Scope**: 支撑 ≥5k 公式近线性扩展；无内建上限；内存与状态按表达式流式处理。时间近线性定义：第二次（无新增表达式）构建外部进程数=0；新增 K 个表达式只新增 K 次外部进程；空间近线性：不常驻全部产物二进制（设计约束，参见 FR-044 说明）。
**Outstanding Clarifications**: None (全部已解决)。

## Constitution Check

Initial + Post-Design Review (all pass)

| Principle                 | Verification                                                                                                                                                                 | Status | Notes                                                                    |
| ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ | ------------------------------------------------------------------------ |
| P1 Processor Duo Only     | 仅列出 Block + InlineMacro，任务 T028 明确不注册 BlockMacro；计划/契约无 TreeProcessor                                                                                       | PASS   | DESIGN.md 后续更新去除旧引用 (T040)                                      |
| P2 Interface-First TDD    | contracts/ 已包含 renderer_pipeline, cache_key, processors；tasks.md Phase 3.2 全部先写测试                                                                                  | PASS   | 无实现代码先行任务                                                       |
| P3 Diagram Parity         | research.md 含属性对照表；差异（无 BlockMacro, `ppi` 术语）已记录                                                                                                            | PASS   | 差异有意识且文档化                                                       |
| P4 Quality & Toolchain    | Tasks T003 (standardrb), T004 (CI), T022/T034 (tool detect/timeout), T035 (security)                                                                                         | PASS   | 覆盖 lint/CI/安全/超时                                                   |
| P5 Determinism & Security | cache key 合成列于 contracts/cache_key.md（字段=FR-011: ext_version, content_hash, format, preamble_hash, ppi, entry_type）；原子写 + 锁 (T030–T031)；无 shell-escape (T035) | PASS   | 统计与错误占位不污染缓存；阶段不变 T079；串行/无动态阶段新增 T082 (计划) |

Complexity Deviations: None (表格留空)。

## Project Structure

### Documentation (feature specs directory)

```text
specs/001-asciidoctor-latexmath-asciidoctor/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── renderer_pipeline.md
│   ├── cache_key.md
│   ├── processors.md
│   ├── error_handling.md
│   └── statistics.md
├── tasks.md
└── (future) performance-baseline.md (FR-042 follow-up)
```

### Source (Ruby Gem)

```text
lib/
├── asciidoctor-latexmath.rb          # Extension entry, registration, version
├── asciidoctor/latexmath/
│   ├── version.rb
│   ├── math_expression.rb
│   ├── render_request.rb
│   ├── attribute_resolver.rb
│   ├── support/
│   │   └── conflict_registry.rb
│   ├── cache/
│   │   ├── cache_key.rb
│   │   ├── cache_entry.rb
│   │   └── disk_cache.rb
│   └── rendering/
│       # pipeline_signature.rb (removed; concept unified with cache key – see spec I5)
│       ├── pipeline.rb
│       ├── renderer.rb
│       ├── pdflatex_renderer.rb
│       ├── pdf_to_svg_renderer.rb
│       ├── pdf_to_png_renderer.rb
│       └── tool_detector.rb
└── asciidoctor/latexmath/processors/
      ├── block_processor.rb
      └── inline_macro_processor.rb

spec/
├── processors/
├── rendering/
├── cache/
├── integration/
├── performance/
└── support/
```

**Structure Decision**: 单项目 Ruby gem 布局（无前后端分离）；符合 Constitution P1–P5，最大程度保持简单与易测试性。
