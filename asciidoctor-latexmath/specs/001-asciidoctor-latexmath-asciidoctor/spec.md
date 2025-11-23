# Feature Specification: Asciidoctor Latexmath Offline Rendering Extension

**Feature Branch**: `001-asciidoctor-latexmath-asciidoctor`
**Created**: 2025-10-02
**Status**: Draft
**Input**: User description: "开发 asciidoctor-latexmath 插件: 作为 Asciidoctor extension 处理 latexmath 内联宏、块宏与块, 使用本地 LaTeX 工具链离线渲染为 PDF/SVG/PNG 并嵌入输出, 支持通过文档和块级 AsciiDoc attributes 配置格式、缓存、工具链选择 (pdflatex/xelatex/lualatex/tectonic)、preamble、PNG 工具 (pdftoppm/magick/gs)、SVG 工具 (dvisvgm/pdf2svg)、DPI、缓存目录、保留产物、禁用缓存、内联 data URI, 保持行为与 asciidoctor-diagram 一致, 不使用 TreeProcessor 与 Mathematical gem, 保证可重复构建、确定性缓存、TDD 工作流、语义化版本控制、安全 & 超时机制。"

## Execution Flow (main)
```
1. Parse user description from Input
   → If empty: ERROR "No feature description provided"
2. Extract key concepts from description
   → Identify: actors, actions, data, constraints
3. For each unclear aspect:
   → Mark with [NEEDS CLARIFICATION: specific question]
4. Fill User Scenarios & Testing section
   → If no clear user flow: ERROR "Cannot determine user scenarios"
5. Generate Functional Requirements
   → Each requirement must be testable
   → Mark ambiguous requirements
6. Identify Key Entities (if data involved)
7. Run Review Checklist
   → If any [NEEDS CLARIFICATION]: WARN "Spec has uncertainties"
   → If implementation details found: ERROR "Remove tech details"
8. Return: SUCCESS (spec ready for planning)
```

---

## ⚡ Quick Guidelines
- ✅ Focus on WHAT users need and WHY
- ❌ Avoid HOW to implement (no tech stack, APIs, code structure)
- 👥 Written for business stakeholders, not developers

### Section Requirements
- **Mandatory sections**: Must be completed for every feature
- **Optional sections**: Include only when relevant to the feature
- When a section doesn't apply, remove it entirely (don't leave as "N/A")

### For AI Generation
When creating this spec from a user prompt:
1. **Mark all ambiguities**: Use [NEEDS CLARIFICATION: specific question] for any assumption you'd need to make
2. **Don't guess**: If the prompt doesn't specify something (e.g., "login system" without auth method), mark it
3. **Think like a tester**: Every vague requirement should fail the "testable and unambiguous" checklist item
4. **Common underspecified areas**:
   - User types and permissions
   - Data retention/deletion policies
   - Performance targets and scale
   - Error handling behaviors
   - Integration requirements
   - Security/compliance needs

---

## Clarifications

### Session 2025-10-02
- Q: FR-033 v1 是否需要插件自带的独立 data URI 开关 / 默认是否启用内联? → A: 不提供独立开关；v1 仅继承 Asciidoctor 全局 `:data-uri:` 行为，不自行生成 `data:` URL；当检测到 `data-uri` 属性时仅改用产物绝对路径（与 `asciidoctor-diagram` 一致），未来可扩展细粒度策略。
- Q: 超时属性命名与单位选哪种方案? → A: 采用文档级 `:latexmath-timeout:` （正整数秒），元素级 `timeout=` 覆写；不支持毫秒与多属性回退。
- Q: 统计禁用设计（FR-035，已合并）采用哪种方案? → A: 通过日志级别控制统计输出；不引入专有属性；降低日志级别（quiet）即抑制统计。（FR-035 已并入 FR-022 统计与日志策略说明；编号保留，见文末保留区）
- Q: 是否支持 `latexmath::[]` 块宏语法? → A: 不支持；范围限定为 `[latexmath]` 块与 `latexmath:[...]` 内联宏两种入口。
- Q: 渲染时对 LaTeX 源（公式正文与 `preamble`）的信任级别是哪种? → A: 完全可信（受控仓库作者）；禁用 shell-escape，额外沙箱/FS 隔离不在 v1 范围。
- Q: 默认缓存目录策略? → A: 统一采用 `cachedir`（与 asciidoctor-diagram 对齐）。解析顺序：1) 元素属性 `cachedir=`；2) 文档属性 `:latexmath-cachedir:`；3) 若存在 `:imagesdir:` 且非空 → `<outdir>/<imagesdir>`；4) 否则 `<outdir>/.asciidoctor/latexmath`（内置默认）。接受 Legacy Alias：`cache-dir=` / `:latexmath-cache-dir:`（输出一次 info 级 deprecation 日志）。规范名称：`cachedir`，日志与错误消息均使用 `cachedir`。若未来取消 imagesdir 回退将通过新增 FR 公告。
- Q: 并行渲染/调度策略? → A: 不内建并行（单进程串行，Option E）；v1 仅保障跨进程并发安全（多 Asciidoctor 进程指向同一缓存目录）；预留未来扩展 `:latexmath-jobs:`（Option D 风格）但当前未实现。
- Q: 缓存回收 / 老化策略? → A: 无自动回收（Option A）；v1 不进行大小/TTL 扫描，不输出阈值告警；完全由用户手动删除；未来如引入策略将新增独立属性并保持向后兼容。
- Q: 显式目标基名冲突策略? → A: 检测差异并报错（Option B）；同名且内容/配置哈希不同立即构建失败，提示首次定义位置与冲突条目；哈希相同则复用不重复写。
- Q: 未指定目标名时自动生成文件基名采用何种稳定方案? → A: 使用前缀 `lm-` + 正规化内容 SHA256 哈希前 16 个十六进制字符 (共 19 字符)，长度与可读性平衡并提供 64bit 熵；与缓存键独立，冲突极低（<1e-11 级别针对 ≤5k 公式）。
 - Q: 正规化内容算法选哪种策略以计算文件基名与 content_hash? → A: 采用 Option E（仅去 UTF-8 BOM，保留原始所有空白、制表符、行结尾 CRLF/LF 原样），不裁剪首尾，不折叠内部空白；跨平台行结尾差异将导致不同哈希，视为可接受的构建环境差异；理由：最大限度保持 LaTeX 敏感空白（对某些宏可能影响）与调试可追溯性。
 - Q: 集成 / CLI 层测试的文件系统隔离策略? → A: 使用 Aruba (RSpec) 沙箱，每个示例独立临时目录与隔离环境变量，避免状态泄漏并便于命令行行为回归测试。
 - Q: 统计输出格式策略? → A: 选 MIN：当日志级别≥info 且至少处理 1 个表达式，在渲染会话结束时输出单行纯文本：`latexmath stats: renders=<int> cache_hits=<int> avg_render_ms=<int> avg_hit_ms=<int>`；字段顺序与名称稳定且永不新增额外键；`avg_render_ms` 为所有实际执行渲染（非缓存命中）耗时的算术平均（四舍五入到整数 ms），`avg_hit_ms` 为所有命中耗时平均（若无命中则为 0）；quiet 或低于 info 不输出；禁止在同一会话重复输出多行统计。
- Q: v1 HTML 可访问性文本策略（渲染产物标签）采用哪种方案? → A: 选 Option D：alt=原始 LaTeX（逐字），并在标签上添加 `role="math"` 与 `data-latex-original` 属性以利辅助技术与后续增强（无截断策略；与内容哈希逻辑独立）。
- Q: 典型与需支撑的最大公式数量规模假设? → A: 选 Option E：不设上限；需可流式处理任意规模（IO/缓存驱动）并保持近线性扩展，不强制内存常驻全部表达式。
- Q: 单个公式渲染失败（编译/工具错误）时文档处理策略? → A: 可配置：文档/元素属性 `latexmath-on-error`（或 `on-error=`）取值 `log|abort`；默认 `log`（继续其它公式并插入占位，整体构建成功）；`abort` 表示 fail-fast（立即终止整体失败）。块级属性覆写文档级；不支持自动重试；未识别值时报错并回落默认 `log`。

- Q: 失败占位（on-error=log）HTML 呈现策略? → A: 使用 `<pre class="highlight latexmath-error">`；内部顺序包含：1) 简短错误描述 2) 执行命令 3) stdout 4) stderr 5) 原始 AsciiDoc 文本 6) 生成的 LaTeX 源；该占位不缓存、不写入统计 renders、仅在策略=log 且单表达式失败时生成；保留换行与缩进供诊断；不截断（未来如需截断将引入独立属性并保持向后兼容）。
- Q: 当文档设置 `:stem: latexmath` 时，对 `stem:[...]` 内联宏与 `[stem]` 块应采用何种语义? → A: 完全别名（Option A）：`stem:[...]` / `[stem]` 直接视为 `latexmath:[...]` / `[latexmath]`，共享属性解析、缓存键（仍仅区分块 vs 内联，不区分 stem/latexmath 入口名）、统计与错误处理；不引入新入口类型维度；仅在 `:stem: latexmath` 时启用。
 - Q: 是否要在 v1 即刻引入可量化的性能验收阈值（用于自动化性能回归测试）？ → A: 选 C（继续延后数值化；维持“记录基准不设硬门槛”策略；沿用既有性能 Clarification 与 FR-042 触发后续硬阈值化条件，不新增 MUST 数值约束；取代最早期临时 Option E 描述）。
 - Q: 同时存在 dvisvgm 与 pdf2svg 时 SVG 默认转换工具优先级？ → A: 选 A：优先 dvisvgm（使用 `dvisvgm --pdf` 从 PDF 转 SVG；不走 DVI 流程）；缺少 dvisvgm 时回退 pdf2svg；不并行尝试；未来可考虑添加 `svg-tool=` 以显式覆写。
 - Q: tectonic 引擎在需要动态获取缺失包时的网络策略？ → A: 不特殊处理；扩展不检测 / 拦截 tectonic 在线包下载；tectonic 视为普通编译引擎；用户通过 `pdflatex=` 或文档级 `:latexmath-pdflatex:` 指定使用哪种引擎（含传入值为 `tectonic`）；若需完全离线可复现需用户自行预缓存或改用传统引擎；缓存键既不包含动态下载包列表，也不包含工具/引擎名称或版本（见 FR-011 决策）。
 - Q: 引擎选择与 pdflatex 命令覆写优先级策略？ → A: 文档级 `:latexmath-pdflatex:` 优先，其次全局 `:pdflatex:`，元素级 `pdflatex=` 覆写二者；默认基线命令 `pdflatex -interaction=nonstopmode -file-line-error`；若用户提供的任何一层命令串未包含 `-interaction=` 子串则自动追加 `-interaction=nonstopmode`；未包含 `-file-line-error` 则追加 `-file-line-error`；两者判断独立且只追加缺失项；追加顺序固定：先 `-interaction=nonstopmode` 后 `-file-line-error`；若命令串已包含相应片段（任意位置）则不重复；该自动追加仅适用于 `pdflatex`；其它引擎单独澄清见下一条。
 - Q: 其它引擎 (xelatex / lualatex / tectonic) 的命令解析与自动附加策略？ → A: 选 B：`xelatex` 与 `lualatex` 采用与 pdflatex 等价的分层与双标志自动追加策略（检测并追加 `-interaction=nonstopmode` 与 `-file-line-error`，顺序同 pdflatex；已存在任一则不重复追加）；`tectonic` 原样执行（不追加这两个标志）；三类引擎均支持元素级 `<engine>=`、文档级 `:latexmath-<engine>:`、全局 `:<engine>:` 分层；默认命令：`xelatex -interaction=nonstopmode -file-line-error`、`lualatex -interaction=nonstopmode -file-line-error`、`tectonic`；未检测到 `<engine>=` / `:latexmath-<engine>:` / `:<engine>:` 时回退默认；tectonic 跳过自动追加逻辑。
- Q: 缓存键中“工具版本签名”与引擎/转换工具差异是否纳入？ → A: 选 C：不记录任何参与工具/引擎版本，也不区分编译引擎与转换/PNG/SVG 转换工具名称；因此 pdflatex ↔ xelatex 等切换或 dvisvgm ↔ pdf2svg 切换不触发缓存失效（最大化命中）；风险：不同工具链可能产出细节差异（字体嵌入、警告、分辨率）需由用户避免在同一构建中混用；未来若出现兼容性问题将通过新增属性（例如 `:latexmath-strict-cache:`）启用严格模式而不破坏默认。

### Session 2025-10-02 (Supplemental Clarifications)
- Q: 显式目标基名中包含路径分隔符 / `..` 的安全与越界策略？ → A: 选 原问题 Option “不处理，信任用户” + 二级子选 A (= 完全放开)。即：对块首位置属性提供的显式基名不做任何清洗 / 过滤 / 沙箱；允许包含相对子目录与任意数量的 `..` 段；路径按相对 `imagesoutdir` 解析后可逃逸其根目录（写入到其上级乃至外部目录）。该决策依赖 FR-036 的“受控仓库可信”模型；README 需添加 SECURITY NOTE（实现阶段）；FR-008 / FR-009 添加例外说明；自动生成的哈希基名（FR-010）仍强制落在 `imagesoutdir` 内，不受此例外影响。
- Q: 显式基名含扩展且与目标格式不一致如何处理？ → A: 规则三分：1) 若最后一个扩展（末尾 `.` 之后子串）在 {`svg`,`pdf`,`png`} 且与所选 `format` 相同 → 原样保留；2) 若在集合内但与所选 `format` 不同 → 直接“替换”末尾扩展为目标格式（例如 `eq1.svg` + `format=png` → `eq1.png`），记录 WARN（一次表达式一次）；3) 若不在集合（如 `eq1.formula`）→ 保留原扩展并“追加”正确扩展形成双扩展（`eq1.formula.png`），记录 WARN。结果文件名（替换或追加后的）参与冲突检测与原子写；缓存键不受显式扩展差异影响（仍由 FR-011 组成）。
- Q: 多外部步骤（编译 + 转换）如何应用超时预算？ → A: 选 D：单表达式共享统一墙钟预算 N 秒（默认 120，属性见 FR-034）。计时起点 = 启动首个外部进程前；每步开始前检查剩余预算；单步运行时若累计耗尽（剩余 ≤0 或本步耗时 > 剩余）即判定超时；后续步骤不再执行。剩余时间递减直至 0；不为每步重置（区别于 per-step 模式）。
- Q: 超时触发的外部进程终止策略？ → A: 选 B：对当前“主”外部进程先发送 SIGTERM，等待固定 2s（未来可参数化），仍存活再 SIGKILL；仅针对该 PID，不向整个进程组广播（不额外 setsid）。Windows 环境使用强制终止 API。日志需包含 `timeout=1` 标记与已消耗时间。此策略整合进 FR-023。
- Q: 目标产物文件路径已存在（且本次不是缓存命中）时处理策略？ → A: 选 A：无条件覆盖（即使该文件并非由本扩展先前生成），采用“写临时 → 原子重命名”流程；不做现有内容哈希比较，也不将其视为缓存命中；记录 debug 级日志。表达式间显式同名冲突仍由 FR-040 抑制（在写入阶段前）。策略新增 FR-051。
- Q: 文档级与元素级 preamble 同时存在时合成策略？ → A: 策略 A：元素级完全替换文档级；元素级存在时忽略文档级；缓存键 preamble 哈希基于最终实际使用的单一 preamble 文本。
- Q: 缓存禁用优先级（%nocache 与 cache= 及文档级）？ → A: `%nocache` > 元素级 `cache=` > 文档级 `:latexmath-cache:`；一旦出现 `%nocache` 强制禁用；否则若元素级 `cache=` 存在按其值；否则回退文档级。
 - Q: artifacts 中间文件目录默认与优先级策略？ → A: 选 D：元素级 `artifacts-dir=` > 文档级 `:latexmath-artifacts-dir:`；默认 `cachedir/artifacts` 子目录（与缓存内容隔离）；相对路径基准=文档 outdir；未启用 `keep-artifacts` 不创建；目录不进入缓存键。
 - Q: keep-artifacts 在失败 / 禁用缓存场景下的保留范围？ → A: 选 C：失败（无论策略=log 还是 abort）仅保留 `.tex` 与 `.log`，删除（或不写入）部分生成的中间 PDF / 转换文件；成功时保留全量（含中间 PDF）；`%nocache` 不影响保留规则（仍按成功/失败分支判定）。

## User Scenarios & Testing *(mandatory)*

### Primary User Story
作为技术写作者 / CI 构建系统, 我希望在离线或受限网络环境中把文档中所有 `latexmath` 数学公式（块、块宏、内联）一次性渲染为所需的矢量或位图资源 (SVG / PDF / PNG), 并在重复构建时复用缓存, 以保证输出质量、构建速度和可重复性。

### Acceptance Scenarios
1. **Given** 文档设置 `:stem: latexmath` 且默认 `:latexmath-format: svg`, **When** 运行文档转换, **Then** 每个 `latexmath` 块 / 内联公式被渲染为单个 SVG 文件并写入 `imagesoutdir`, 文档中对应位置引用这些 SVG, 重复运行构建时同一内容不重新调用外部工具 (缓存命中统计≥1)。
2. **Given** 用户请求 `:latexmath-format: svg` 但系统缺少 `dvisvgm` 与 `pdf2svg`, **When** 执行构建, **Then** 构建失败并输出清晰错误: 缺失的命令列表、建议安装方式、指向禁用或切换格式的提示, 不产生半成品文件。
3. **Given** 用户在块级添加 `[%nocache]` 与 `format=png, ppi=200`, **When** 构建两次, **Then** 该块两次都重新渲染且生成 PNG (200 PPI) 文件名保持一致, 其它未加 `%nocache` 的表达式复用缓存。
4. **Given** 并行构建 (两个独立进程) 渲染同一公式文本, **When** 同时启动, **Then** 结果只有一个缓存条目被写入 (无损坏 / 无临时文件泄漏) 且两个进程均成功引用该产物。
5. **Given** 文档设置 `:stem: latexmath` 且包含 `stem:[a^2+b^2=c^2]` 与 `latexmath:[a^2+b^2=c^2]`, **When** 构建两次, **Then** 该公式仅首轮渲染一次（第二轮与两种写法均缓存命中），两种写法引用同一产物文件，无重复文件与无重复统计计数。

### Edge Cases
- 请求不支持的格式 (如 `format=gif`) → 明确错误并列出受支持集合。
- 指定工具不存在或无执行权限。
- `latexmath-preamble` 含非法 LaTeX 指令导致编译失败。
- 大型公式（>3000 字节，统一阈值，参见 FR-032；旧描述 >10KB 已废弃）或深度递归宏。
- `latexmath-cache=false` 与 元素 `%nocache` 混合使用。
- 同一文档中混合 `svg` 与 `png` 输出需求。
- 超时：外部工具长时间挂起。
- Windows / Linux 路径差异 (相对路径解析)。
- 内联公式选择 data URI (未来扩展) 与默认文件引用并存。
- `:stem: latexmath` 下混用 `latexmath:` 与 `stem:` 形式（应引用同一缓存与产物，不重复渲染）。

## Requirements *(mandatory)*

### Functional Requirements
- **FR-001**: MUST 支持入口语法：`[latexmath]` 块、`latexmath:[...]` 内联宏；当文档设置 `:stem: latexmath` 时 MUST 以完全别名方式同等处理 `[stem]` 块与 `stem:[...]` 内联（无额外入口类型区分，属性/缓存/统计/错误处理完全复用）；MUST NOT 支持 `latexmath::[]` 块宏形式。
- **FR-002**: MUST 依据文档或元素属性渲染为 `svg|pdf|png` 三种格式之一；默认 `svg`。
- **FR-003**: MUST 允许用户通过属性选择编译引擎 (pdflatex/xelatex/lualatex/tectonic)。
- **FR-004**: MUST 在缺少所需工具链时以可操作错误终止，列出缺失命令与建议解决方式（错误消息格式遵循 FR-019 模板）。
 <!-- FR-005 merged into FR-011: moved to Reserved / Merged section; 保留编号避免历史引用失效 -->
- **FR-006**: MUST 支持块级/内联 `format=`、`ppi=`、`pdflatex=`、`xelatex=`、`lualatex=`、`tectonic=`、`dvisvgm=`、`pdf2svg=`、`png-tool=`、`preamble=`、`cache=`、`cachedir=`、`artifacts-dir=` 覆写；`cache-dir=` 为 Legacy Alias（一次 info 级 deprecation 日志）。缓存目录解析/优先级与回退细节不在本条重复，统一以 **FR-037**（cachedir 解析）为准；README 仅展示规范名称并在 cachedir 行指向 FR-037。NOTE: *本条永不内联缓存目录解析逻辑*；任何 future 变更只允许修改 FR-037（去重 D2）。
- **FR-007**: MUST 支持元素选项 `%nocache` 与 `keep-artifacts`，准确控制该元素缓存与产物保留。缓存判定优先级：`%nocache` > 元素级 `cache=` > 文档级 `:latexmath-cache:`（见 Clarifications）；一旦出现 `%nocache` 则无条件禁用缓存，忽略该元素 `cache=` 值与文档级设置；否则若存在元素级 `cache=` 则按其布尔值决定；否则回退文档级。产物保留的具体文件集合与目录解析见 FR-021（避免与 FR-021 重复描述）。Includes semantics of merged **FR-015**：当任何层级（文档级或元素级）显式禁用缓存时，MUST 同时跳过缓存读取与写入（合并 D1）。
- **FR-008**: MUST 生成的输出文件路径决策顺序与 `asciidoctor-diagram` 对齐：
   1. 若存在文档/元素属性 `imagesoutdir` → 输出目录 = 解析后的 `imagesoutdir`（无需再参考 `outdir` / `to_dir` / `imagesdir`）。
   2. 否则确定根目录 R：优先级 `outdir` 属性 > 文档 options `:to_dir` > 文档 `base_dir`。
   3. 若存在 `imagesdir` 属性 → 输出目录 = R / `imagesdir`；否则输出目录 = R。
   4. 若目录不存在在写入前自动创建。
   5. 以上决策仅影响物理写入位置；HTML 引用使用 `imagesdir` / 节点级 `imagesdir`（当启用 `autoimagesdir` 时可能被节点覆写）；当启用 `data-uri` 或节点 `inline` 选项时引用改为绝对路径或内联但仍按上述位置生成文件。
   当用户通过块首位置属性显式提供基名且其中包含相对路径段（可含子目录或 `..` 段）时，按 Supplemental Clarifications 决策：不做清洗 / 限制；该相对路径以步骤 (1)/(2)/(3) 得出的“输出目录”作为解析锚点（即：若使用 `imagesoutdir` 则相对 `imagesoutdir`，否则相对 R）；这可能使最终路径越出 `imagesoutdir`（显式覆写例外）；自动生成哈希基名（FR-010）不享受此例外，始终落在步骤 (1)/(2)/(3) 计算出的基础输出目录（不附加额外越界子路径）。
- **FR-009**: MUST 对块首个位置属性解释为目标基名，第二个位置属性可解释为格式（与 asciidoctor-diagram 中块行为一致）；不适用块宏语法。显式基名允许包含路径分隔符与任意数量 `..` 段（不清洗，不拒绝）；当基名包含扩展：若末尾扩展 ∈ {svg,pdf,png} 且与目标格式匹配 → 保留；若末尾扩展 ∈ {svg,pdf,png} 但与目标格式不匹配 → “替换”该扩展为目标格式（WARN）；若末尾扩展不在集合 → 追加正确扩展（形成双扩展，WARN）。WARN 级别日志应指明原始名称与最终采用名称。冲突检测与缓存键逻辑基于最终文件名与 FR-011 组成。
- **FR-010**: MUST 为未指定目标名的表达式生成稳定且基于内容哈希的文件基名：算法 = 取得“正规化内容” (Normalization-E)：仅移除 UTF-8 BOM；其余字节序列（含制表符、CRLF 或混合行结尾、行尾空白、前后导空白）全部保留原样；计算 SHA256，对其十六进制串取前 16 个字符，加前缀 `lm-` 得基名（例：`lm-a1b2c3d4e5f6a7b8`）。文件扩展名由最终格式决定；用户显式提供基名时跳过此规则。冲突处理（替换旧“数字后缀”策略）：
   1. 若 16 字符截断与另一不同内容/配置表达式冲突 → 升级为前 32 字符截断（`lm-<32hex>`）并记录一次 WARN（含原前缀）。
   2. 若极低概率 32 字符仍冲突 → 使用完整 64 字符（`lm-<sha256>`）并记录第二条 WARN；再次冲突可视为实现边界，抛出与 FR-040 模板一致的可操作错误（无需再增加长度或数字后缀）。
   3. 缓存键始终使用完整 SHA256，不受截断/升级影响。
   4. 不再声明统计概率（移除“极低概率”措辞），完全以确定性升级规则定义行为。
   对应测试（T063）需模拟 16 字符冲突并断言升级到 32 字符；64 字符路径测试可选（stub）。
- **FR-011**: MUST 缓存键包含（按稳定顺序）: `ext_version`、`content_hash`（同 FR-010 Normalization-E）、`format`、`preamble_hash`、`ppi`（非 png 时记占位 `-`）、`entry_type`（块/内联）。MUST NOT 包含：编译引擎名称、转换工具名称、任何工具或引擎版本、路径（除自动生成基名外）、日志级别、超时值（仅当其实际导致输出差异时才另行引入新字段）。因此在同一表达式上切换引擎（pdflatex↔xelatex↔lualatex↔tectonic）或 SVG/PNG 转换工具（dvisvgm↔pdf2svg，pdftoppm↔magick↔gs）不会触发缓存失效；用户若需强制重渲染需修改 preamble、格式、PPI 或手动清理缓存。未来若引入“严格模式”将新增字段而不改变上述默认集合；当通过 `:stem: latexmath` 使用 stem 别名时不在缓存键中区分 stem 与 latexmath 名称。任何列入字段值变化（含 preamble、格式、PPI、entry_type 或 ext_version 升级）MUST 触发重新渲染。*本条同时吸收原 FR-005 命中复用语义。*
<!-- FR-012 merged into FR-011 → moved to Reserved / Merged Requirement Numbers section -->
- **FR-013**: MUST 在并行运行（多进程）中防止竞争条件：采用内容哈希命名 + 先写入临时文件（同目录 `<name>.tmp-<pid>`）后原子重命名；目标文件已存在即视为成功并跳过；需避免半写文件、脏读；可选基于锁文件 `<hash>.lock`（获取失败时指数退避重试 ≤ 5 次）。
- **FR-014**: MUST 在渲染失败时（非 0 退出码）输出：执行命令、退出码、日志文件路径、建议下一步。
   - NOTE: 与 FR-045/FR-046 协同；当当前作用域（元素→文档）解析 `on-error=abort` 时触发 fail-fast；否则记录错误并生成占位（占位结构见 FR-046，策略见 FR-045）。
- **FR-015**: (Merged into FR-007; no separate normative text — see Reserved / Merged Requirement Numbers)  (D1)
- **FR-016**: MUST 允许 `latexmath-preamble` 追加多行文本；空值不产生额外空行副作用。当同时存在文档级 `:latexmath-preamble:` 与元素级 `preamble=` 时采用替换策略：若元素级存在则完全忽略文档级内容（不拼接、不去重）；缓存键中 preamble 哈希使用实际生效的（元素级或文档级）文本。
- **FR-017**: MUST 默认禁止潜在危险的外部命令执行（无显式允许时不启用 shell escape）。
- **FR-018**: MUST 为 PNG 输出应用 PPI（≥72 且 ≤600）范围校验; 超出时报错。
- **FR-019**: MUST 对不支持的格式、属性值、工具名给出枚举提示信息，并满足“可操作错误 (Actionable Error)”契约（本条定义统一错误模板，供其它条款引用）：
   1. 机器可解析错误类型（Ruby 异常类 `Latexmath::UnsupportedValueError` 或等价）
   2. 消息模板：`unsupported <category>: '<value>' (supported: <list>)`；`<category>` 取值 `format|attribute|tool|engine`；`<list>` 按字母序列出受支持值
   3. 至少包含一个 remediation hint：例如 `hint: change :latexmath-format: to one of [svg,pdf,png]`
   4. 不产生部分产物文件；错误之前临时文件需清理
   5. 统计：不增加 renders/cache_hits（未来可扩展 error counter）
   6. 区分 FR-004：FR-004 针对“缺失命令” / 系统执行层；FR-019 针对“值非法” / 参数校验层
 - **FR-020**: MUST 采用“按需惰性 + 进程级缓存”方式检测外部工具：
    1. 触发：首次需要该类别工具（首次渲染 SVG 才检测 `dvisvgm|pdf2svg`；未使用 PNG 不检测 `pdftoppm|magick|gs`）。
    2. 作用域：单 Ruby 进程内缓存 ok|missing 结果（内存 Hash），所有表达式与阶段共享；后续不重复系统探测。
    3. 结果：仅记录可用性，不记录路径/版本/时间（保持 FR-011 缓存键最小）。
    4. 失效：无运行时失效；需进程重启；v1 不提供强制刷新。
    5. 安全：探测不触发网络；`tectonic` 按需下载不影响探测缓存与缓存键（参见 FR-048）。
    6. 可观测：首次（任一 latexmath 节点前）输出工具摘要行（FR-031），后续不重复。
    7. 错误：缺失必需工具立即抛出 FR-004/FR-019 actionable error；不通过动态删减阶段规避（P5）。
   该策略保证最小探测成本并与确定性缓存复用协同。
- **FR-021**: MUST 在启用 `keep-artifacts` 时保留 `.tex`、`.log`、以及成功渲染时的中间 PDF 与必要转换临时文件至“工件目录”(artifacts directory)。目录解析优先级：元素级 `artifacts-dir=` > 文档级 `:latexmath-artifacts-dir:` > 默认 `<cachedir>/artifacts`（cachedir 依 FR-037 决议，与缓存文件隔离）。相对路径基准=文档 outdir。不存在时在首次需要写入前递归创建；未启用 `keep-artifacts` 不创建。失败（无论策略=log 还是 abort）仅保留 `.tex` 与 `.log`（若已生成），应删除或不写入部分 PDF / 转换临时文件；成功才保留中间 PDF。`%nocache` 或 `cache=false` 不改变上述保留规则。Cleanup Guarantee: 失败/超时后部分写入的 PDF / SVG / PNG / 中间转换临时文件（含 `.tmp-<pid>`） MUST 删除或保持为未完成临时文件且不得原子重命名为最终名；仅 `.tex` 与 `.log` 可保留；成功路径采用“写临时→原子 rename” 防止截断文件；清理行为不进入缓存键。
 - **FR-022**: MUST 统计渲染次数、缓存命中次数、平均渲染耗时并以“单行 MIN 契约”输出；输出格式固定：`latexmath stats: renders=<int> cache_hits=<int> avg_render_ms=<int> avg_hit_ms=<int>`（字段与顺序不可变，未来扩展需新增 FR）；仅当日志级别≥info 且 (renders + cache_hits) > 0 时输出；低于 info 或 quiet 不输出；单个文档渲染生命周期内最多输出一行（多文档可各自一行）；`avg_render_ms` 与 `avg_hit_ms` 为四舍五入整数毫秒（无命中则 `avg_hit_ms=0`）；若 renders=0 可省略整行；不提供文档/元素级开关属性（仅受日志级别控制）。
- **FR-023**: MUST 对单个表达式应用“统一墙钟超时”机制：默认 120s（文档级 `:latexmath-timeout:` 或元素级 `timeout=` 覆写，见 FR-034），预算从首次外部进程启动前开始计时；多个外部步骤（编译、SVG/PNG 转换等）共享同一剩余预算；任一步骤开始前若剩余 ≤0 或执行中耗尽则判定超时。超时时：对当前主子进程发送 SIGTERM，等待 2s，仍存活再 SIGKILL（Windows 使用强制终止）；不终止整组/后代进程；记录包含 `timeout=1`、已耗时、剩余=0 的日志，并依据 FR-045 失败策略生成占位或 fail-fast。建议信息包括：提高 timeout、精简公式、检查工具死锁。该行为不加入缓存键（FR-011）。
- **FR-024**: MUST 对内联公式输出参考（文件或未来 data URI）；默认文件引用。
 - **FR-025**: MUST 不使用 TreeProcessor 或依赖 Mathematical；若检测到冲突（同时启用 mathematical）提示优先级与迁移。
 - **FR-026**: MUST 遵循宪章 TDD：拒绝在无对应失败测试前合入新行为（通过 CI Gate 控制）。该要求通过提交流程 / CI 审核保障，不强制提供单独“门控”测试（属流程约束，非代码逻辑）。Governance Note (C1): 由 CI 工作流 (T004) + 治理任务 T087 佐证（提交需引用触发的 failing spec SHA）；不可完全自动静态判定，但代码变更应引用已存在的红色测试；审查者需核对引用。
 - **FR-027**: MUST 文档化所有支持属性（README/Attributes 表格同步）。
- **FR-028**: MUST 允许在同一文档中混用不同输出格式；彼此缓存隔离。
- **FR-029**: MUST 正确处理含 Unicode 字符：
   1. 支持任意有效 UTF-8 字符（含多字节数学符号、CJK、Emoji、组合附加符号）
   2. 不执行 Unicode 归一化（NFC/NFD 差异视为不同内容并影响哈希，保持可诊断性）
   3. 内容哈希按字节（去除 UTF-8 BOM 后）直接计算（参见 FR-010 / FR-011）
   4. `alt` 与 `data-latex-original` 属性（FR-043）逐字保留原始代码点序列
   5. 测试需包含：组合重音 (e.g. "e" + U+0301)、希腊字母、CJK、Emoji、数学黑板粗体 / 双线体字符，验证渲染成功且缓存命中稳定
- **FR-030**: SHOULD 在工具缺失时输出“可操作替代”提示（错误消息格式遵循 FR-019 模板）。标准消息模板：`hint: install <tool>|choose <alternative_format>|set <attribute>=<supported_value>`；当缺少 `dvisvgm` 且存在 `pdf2svg` 时模板示例：`hint: install dvisvgm (preferred) or keep using pdf2svg; or set :latexmath-format: pdf|png`。消息放入同一异常文本（FR-004 / FR-019 区别：缺失工具 vs 不支持值），位于主错误行之后换行位置，前缀固定 `hint:` 便于测试断言；多个 hint 以分号分隔。
- **FR-031**: SHOULD 在首次遇到 latexmath 节点（或注册后第一次渲染前惰性触发）输出一次工具可用性摘要（info 级）。格式：`latexmath.tools: dvisvgm=<ok|missing> pdf2svg=<ok|missing> pdflatex=<ok|missing> xelatex=<ok|missing> lualatex=<ok|missing> tectonic=<ok|missing> pdftoppm=<ok|missing> magick=<ok|missing> gs=<ok|missing>`。*不输出版本号*；缺失值为 `missing`；永不重复输出（多文档场景按进程一次）。若全部缺失与当前格式直接相关的转换工具将仍由 FR-004 抛出主错误；本摘要行主要服务诊断与测试。禁止新增字段顺序漂移（严格左到右固定顺序）。
- **FR-032**: SHOULD 为重复出现的“大型公式”记录单独耗时日志（debug 级）。*大型公式阈值*：原始（Normalization-E 之前）UTF-8 字节长度 > 3000 字节即判定（多字节字符按实际字节计）；日志格式：`latexmath.timing: key=<first8(content_hash)> bytes=<len> ms=<elapsed_ms>`；仅在该公式首次渲染与后续每次缓存命中时各记录一次（命中耗时表示从缓存读取到完成引用注入的总耗时）。禁止将 timing 行纳入统计聚合行 (FR-022)。
- **FR-033**: SHOULD（未来扩展 / Deferred）支持独立于全局 `:data-uri:` 的细粒度内联策略；v1 不提供专有 data URI 开关，仅继承 Asciidoctor 核心 `:data-uri:` 行为并通过绝对路径辅助核心内联；本版本无实现与任务（显式 Defer）。
- **FR-034**: SHOULD 允许用户自定义渲染超时：文档级属性 `:latexmath-timeout:` （正整数秒，默认 120），元素级属性 `timeout=` 可覆写当前表达式；非法或非正整数值应报错并回退默认。
- **FR-036**: MUST 采用“受控仓库作者完全可信”信任模型：假设公式与 preamble 来自可信源；实现禁用 shell-escape（见 FR-017）但不增加额外沙箱/文件系统隔离；多租/不可信输入强化措施（隔离目录、内存/CPU 限额）列为未来范围外。
- **FR-037**: MUST 缓存目录解析顺序：
    1) 元素属性 `cachedir=`（相对路径基于文档 outdir 解析）
    2) 文档属性 `:latexmath-cachedir:`
    3) 若定义了 `:imagesdir:` 且非空，则 `<outdir>/<imagesdir>`
    4) 否则 `<outdir>/.asciidoctor/latexmath`（内置默认）
 其中 `outdir` 由 Asciidoctor 决议（命令行 `-D` / 文档属性 / 执行工作目录）。若目录不存在需在首次渲染前创建。接受 Legacy Alias `cache-dir=` / `:latexmath-cache-dir:` 并输出一次 info 级 deprecation 日志。所有内部、日志、错误输出使用规范名称 `cachedir`。允许 imagesdir 回退以贴近 asciidoctor-diagram 行为；未来如需移除该回退将新增 FR 公告（语义变更）。
- **FR-038**: MUST 不内建并行渲染调度（单进程串行队列）；跨进程并发仅依赖 FR-013 原子写保障；预留文档属性 `:latexmath-jobs:`（保留字，当前解析后记录 Warning 并忽略）以便未来扩展为可配置并行度（默认 cores）。Test Coverage 注：串行特性不做专门线程枚举测试；通过代码审查 + 性能基线（FR-042/044）间接验证；禁止基于工具可用性动态插入/删减阶段（与 P5 一致，另见即将新增阶段不变测试任务）。
- **FR-039**: MUST 不实施任何自动缓存逐出/清理：不基于大小、文件数或 TTL 扫描删除；插件不对缓存目录做周期遍历。用户如需清理，需手动删除目录（安全：再生成时按键重建）。未来策略（大小 / TTL / LRU）将通过新属性显式启用，保持默认行为不变。Test Coverage 注：无需专门验证清理器缺失（Implementation-Defined, U5）。
- **FR-040**: MUST 当两个以上表达式（内容或配置不同 → 缓存键不同）显式请求相同目标基名 + 相同格式时：在首次检测到第二个冲突时抛出可操作错误（错误消息格式遵循 FR-019 模板），列出：目标名、原始定义（行/块标识）、新定义摘要（前 80 字符哈希前缀）、建议（移除显式目标名或改名）。若缓存键相同（完全同一内容与配置）则视为幂等：不重写文件亦不警告。检测需在写入前完成（结合 FR-013 原子策略）。
 - **FR-041**: MUST 集成与端到端命令行测试使用 Aruba（或功能等价沙箱）确保：每测试示例独立临时工作目录、环境变量清理、无跨示例残留文件；测试可通过 helper 提供对渲染产物与日志的断言；不得依赖真实用户 HOME / 全局缓存副作用。
- **FR-042**: SHOULD 在首次实现后生成一份性能基准（≥30 个简单公式批量：SVG 冷/热 + PNG）并记录：冷启动 p50/p95、缓存命中追加开销、平均渲染耗时；“简单公式”定义：Normalization-E 前 UTF-8 原始字节长度 ≤ 120；若 SVG 冷 p95 > 3000ms 或 PNG 冷 p95 > 3500ms 则需在后续迭代将明确量化阈值添加为 MUST（更新本 spec 与 README）。当前版本不锁定硬阈值；升级触发条件与 near-linear 说明见 FR-044（互相引用）。Escalation Workflow (A1): 一旦任一 exploratory 指标超过阈值 → (1) 创建 issue 标记 `performance-escalate` (2) 在下一次 minor 迭代前提出新增 MUST FR（列出硬阈值与测量方法）(3) 将该 FR 添加到 "Reserved / Merged" 区之前的 Requirements 列表 (4) 更新 README 性能章节 (5) 若第二次基线仍超阈值则在随后的次要版本强制 fail gate。
- **FR-043**: MUST 生成 HTML 时，对由扩展替换的数学公式引用（`<img>` 或等效占位）添加可访问性元数据：`alt` 属性内容 = 原始 LaTeX 源（逐字保留，不截断，不做命令剥离）；附加 `role="math"` 与 `data-latex-original`（同 alt 内容）属性；若用户已手动提供 `alt` 元素属性（块/内联属性）则优先用户值且仍附加 `role` 与 `data-latex-original`（不覆盖用户 alt）。该行为适用于三种输出格式 (svg/pdf/png)；与缓存 / 基名 / 哈希策略无副作用；测试应验证：存在 alt、role、data-latex-original 且三者一致（当未用户覆写）。
- **FR-044**: 性能 & 复杂度（精炼）
   MUST:
   1. 不对公式总数 N 设置硬上限。
   2. 缓存命中路径不得启动任何外部渲染/转换进程（与 P5, FR-011 一致）。
   3. 判定缓存命中不得对缓存目录做全量枚举扫描（允许：构造路径直接 stat/read）。
   4. 外部进程调用次数 = 未命中表达式数 M（无重试机制）。
   5. 不常驻保留中间产物二进制内容于内存（避免 O(N * artifact_size) 内存占用）。
   SHOULD:
   6. 生成 `performance-baseline.json`（冷/热各≥1 条记录）供基线追踪；新增字段需新 FR。
   NON-NORMATIVE: 性能抖动与观测阈值转移至后续基线文档（T059）。
   说明：
   - 时间近线性：第二次（无新增表达式）构建外部进程数=0；新增 K 表达式仅新增 K 次进程。
   - 空间近线性：不把全部已渲染产物二进制常驻内存，只保留当前上下文；不对该点写专门测试，属设计约束。
   - 阈值升级与基线文件生成流程：见 FR-042（互相引用）。
   Test Coverage Mapping (C2):
   - T080: 全缓存命中场景外部进程数=0（覆盖 MUST(2)/(4) 在 M=0 情况）
   - T084: 混合新增表达式场景（第一次渲染 N; 第二次新增 K）断言第二次仅 K 次新增进程（覆盖 MUST(4) 在 M>0 线性特性）
   - T085: 缓存命中路径无目录枚举（通过 instrumentation/spy 断言未调用 Dir.glob/Find）覆盖 MUST(3)
   - T086: 内存占用点查（渲染大量表达式后 RSS 未线性积累 / 无全量产物二进制数组）观察性验证 MUST(5)（非门控；失败仅提示优化）
   - T051/T059: 提供性能数字基线支撑 near-linear 说明文字（间接佐证）
   说明：MUST(5) 仅做 spot-check（T086），不构建精确内存轮廓；若未来发现回归再升级为强制阈值 FR。
- **FR-045**: MUST 提供失败策略属性：文档级 `:latexmath-on-error:`，元素级 `on-error=`；允许值 `log` 与 `abort`；默认 `log`。`abort` → 在首次失败立即终止转换并返回错误；`log` → 记录错误（与 FR-014 输出一致）并在输出中插入结构化占位（见 FR-046），继续处理剩余表达式，最终构建成功且统计中不计入成功渲染次数（renders 不含失败项）。非法值时报错并回退默认 `log`。缓存不记录失败产物。
 - **FR-046**: MUST 当失败策略=log 且单表达式渲染失败时插入 `<pre class="highlight latexmath-error" role="note" data-latex-error="1">` 占位，内部文本段落按顺序包含：
    1. `Error:` + 简短错误描述（单行）
    2. `Command:` + 完整执行命令字符串
    3. `Stdout:` 原样（空则写 `<empty>`）
    4. `Stderr:` 原样（空则写 `<empty>`）
    5. `Source (AsciiDoc):` 原始表达式（如为块包含多行）
    6. `Source (LaTeX):` 生成的 `.tex` 文件主内容（preamble + 公式）
   分节之间以单个空行分隔；仅进行 HTML 必要转义（`&`, `<`, `>` 等）；不额外截断；不写入缓存；不计入成功渲染统计；未来若需截断/精简将通过新增属性控制并保持向后兼容。

 - **FR-047**: MUST 对 `format=svg` 的转换采用确定性工具优先策略：启动时探测可用工具（结合 FR-020），若存在 `dvisvgm` 则使用 `dvisvgm --pdf`（始终通过 PDF 中间产物转换为 SVG；不依赖 DVI 流程）；若缺少 `dvisvgm` 且存在 `pdf2svg` 则使用 `pdf2svg`；二者皆缺失时按 FR-004 生成缺失工具错误；当首选工具成功时不得回退或尝试次级工具；本策略不引入并行尝试。追加 Logging / 可观测要求：
   1. 启动阶段（首次处理到 `latexmath` 节点前）记录一次 info 日志：`latexmath.svg.tool=<dvisvgm|pdf2svg|missing>`；缺失时继续按 FR-004 报错终止。
   2. 若首选工具探测到但执行失败（进程非零退出），不得尝试次级工具；直接走 FR-014 错误格式，错误消息含 `svg-tool=<name>` 字样，便于测试断言。
   3. 不允许静默降级：任何从首选到次级的实际切换都视为违规（测试可通过模拟首选失败验证无次级调用）。
   4. 选择或失败均不影响缓存键（与 FR-011 一致）。
   5. 未来若需用户显式覆写将新增属性（`svg-tool=`）引入：默认值 `auto` 保持当前优先顺序；该未来变更不会改变现有默认语义。
   6. Debug 级别可额外记录探测矩阵（可用工具列表），但非 MUST；测试只断言 info 主行与错误行稳定格式。
 - **FR-048**: MUST 将 `tectonic` 视为普通可选编译引擎之一，不引入专有网络策略：扩展不阻断其在线按需包获取，也不强制离线；未缓存包导致的首次网络下载行为不写入或修改缓存键（缓存键不包含工具/引擎名称或版本，见 FR-011）；若在严格离线 / 断网环境构建失败应提示用户改用其它引擎或预先本地预热 tectonic 缓存；网络失败（超时、无法解析域名等）按引擎失败处理（FR-014/FR-045）。实现不探测“是否使用网络”或包下载事件——仅依据进程退出码；未来如需策略化（强制离线等）将新增独立属性；切换至/离开 tectonic 不会使既有缓存失效（Clarifications 已阐述风险）。Test Coverage: v1 **不提供专门自动化测试**（U2 决策）；通过其它引擎切换与缓存键固定策略测试 (T062, 未来可能的严格模式) 间接验证；如出现回归再追加专用任务。
 - **FR-049**: MUST 解析 `pdflatex` 基线命令时采用层级优先级：元素级 `pdflatex=` > 文档级 `:latexmath-pdflatex:` > 全局 `:pdflatex:` > 默认 `pdflatex -interaction=nonstopmode -file-line-error`；解析出首个可用命令串后执行规范化：
    1. 若不含子串 `-interaction=`（任意形式，如 `-interaction=batchmode` 亦视为已含）则追加 `-interaction=nonstopmode`；
    2. 再检查是否含 `-file-line-error`；若缺失追加 `-file-line-error`；
    3. 追加顺序固定（interaction 优先，file-line-error 次之），确保生成命令稳定；
    4. 只追加缺失标志，不改写已存在值；
    5. 追加不改变缓存键（见 FR-011 键组成），视为命令规范化；
    6. 若用户命令包含管道/重定向亦整体扫描子串；
    7. 该规范化当前仅适用于 `pdflatex`（其它引擎规则见 FR-050）。
 - **FR-050**: MUST 对 `xelatex` 与 `lualatex` 采用与 FR-049 等价的分层与双标志自动附加逻辑：元素级 `xelatex=` / `lualatex=` > 文档级 `:latexmath-xelatex:` / `:latexmath-lualatex:` > 全局 `:xelatex:` / `:lualatex:` > 默认 `xelatex -interaction=nonstopmode -file-line-error` / `lualatex -interaction=nonstopmode -file-line-error`；规范化流程：若缺少 `-interaction=` 则追加 `-interaction=nonstopmode`，随后若缺少 `-file-line-error` 再追加；顺序与 FR-049 一致；仅追加缺失项，不改写已有；追加不改变缓存键（见 FR-011）；`tectonic` 层级：元素级 `tectonic=` > 文档级 `:latexmath-tectonic:` > 全局 `:tectonic:` > 默认 `tectonic`，无任何自动追加（参见 FR-048）；切换上述任意引擎不使缓存失效（Clarifications 说明风险与理由）。

#### Engine Normalization Common Rules (D4)
适用于 FR-049 / FR-050：
1. 规范化仅“追加”缺失标志，不替换已存在值。
2. 标志追加顺序固定：`-interaction=nonstopmode` → `-file-line-error`。
3. 追加操作不进入缓存键（与 FR-011 保持稳定）。
4. 任意引擎命令串包含管道或重定向时仍基于子串扫描判定是否需要追加。
5. `tectonic` 不做自动追加；其非零退出不触发对其它引擎的回退尝试（与 FR-047 的“无静默降级”一致）。

 - **FR-051**: MUST 当渲染需写入产物且该目标文件路径已存在而当前不是缓存命中（即需实际渲染）时，无条件覆盖：使用临时文件写入后原子重命名替换旧文件；不计算旧文件哈希、不提示冲突、不计为 cache hit；记录 debug 级日志（含旧文件存在的提示）。此策略不影响 FR-040（仅针对同一文档多表达式显式同名冲突的早期检测）。

### Error Handling Summary (FR-014 / FR-045 / FR-046) (D3)
| Aspect | FR-014 (Error Output) | FR-045 (Policy) | FR-046 (Placeholder Structure) |
|--------|-----------------------|-----------------|--------------------------------|
| 触发条件 | 外部渲染进程非零退出 | 用户选择 `on-error=log|abort` | 仅在策略=log 且单表达式失败 |
| 行为 | 记录命令/退出码/日志提示 | `abort` 早停；`log` 继续 | 插入 `<pre>` 占位 6 段内容 |
| 缓存影响 | 不写入失败产物 | 失败不计 renders | 占位不缓存，不计 renders |
| 测试引用 | T009, T045 | T009, T045 | T020, T045 |
| 关系 | 提供原始信息 | 决定是否生成占位 | 具体占位格式实现 |

> 该汇总不引入新语义，仅消除跳转阅读负担。

### Pipeline Signature （历史说明）
已退役：独立字段被移除；阶段集合/顺序变化通过扩展版本号（P5, FR-011）体现。任何 legacy 引用 = “缓存键字段集 + 版本号”。T023 仅占位，无实现。

### Non-Functional Requirements (NFR)
NFR-001 性能：满足 FR-044 MUST 条款；≥5k 表达式命中路径仍 0 外部进程。时间近线性观测：首次构建外部进程数=M（未命中数），第二次(无改动)为0，新增 K 表达式后第三次增量=K；空间近线性=不常驻全部产物于内存（设计约束，不单测）。
NFR-002 确定性：同一 (content + 规范化属性 + format + preamble + ppi + entry_type + ext_version) 条件二次构建不触发外部进程（P5）。
NFR-003 可访问性：`alt` / `role="math"` / `data-latex-original` 三要素齐备（FR-043）。
NFR-004 安全（信任模型）：禁用 shell-escape（FR-017, FR-036）；显式基名越界允许仅在可信仓库模式下使用。
NFR-005 可观测性：统计行（FR-022）、工具摘要（FR-031）、大型公式 timing（FR-032）。
NFR-006 可维护性：仅 BlockProcessor + InlineMacroProcessor（P1）。
NFR-007 术语一致性：`cachedir` 为规范；`cache-dir` / `latexmath-cache-dir` 为兼容别名（弃用，日志一次性提示；单进程最多一次 deprecation 日志——Terminology enforcement 测试覆盖）。

### Terminology Note
Canonical 名称：`cachedir` / `:latexmath-cachedir:`；兼容别名：`cache-dir` / `:latexmath-cache-dir:`（弃用）。
Enforcement (T1 / NFR-007):
- 实现与日志 MUST 始终使用 canonical `cachedir`；别名仅在解析入口归一化后立即抛弃。
- Alias 使用仅触发单次 info 级弃用日志（覆盖测试：T018 验证 alias 行为 + T081 术语执行一次性日志）。
- 若内部出现对 alias 形式的再次引用视为缺陷（不编写专门检测逻辑，依代码审查 + T081 log 次数断言）。
- 文档（spec / README / tasks）后续新增引用若使用 alias 需在 review 中拒绝；本说明即执行准则。

#### Terminology Table (C3)
| English | 中文 | 说明 |
|---------|------|------|
| cache hit | 缓存命中 | 不触发外部进程（FR-011, NFR-002）|
| rendering pipeline | 渲染流水线 | 按固定阶段顺序执行（P5）|
| external tool | 外部工具 | `pdflatex` / `dvisvgm` 等，可缺失时报错 |
| timeout budget | 超时预算 | 单表达式统一墙钟 (FR-023) |
| artifacts directory | 工件目录 | 保存 `.tex`/`.log`/中间 PDF (FR-021) |
| deterministic cache key | 确定性缓存键 | 固定字段集合 (FR-011) |
| conflict detection | 冲突检测 | 同名不同签名报错 (FR-040) |
| accessibility metadata | 可访问性元数据 | `alt` / `role` / `data-latex-original` (FR-043) |

> 所有文档今后应优先使用英文术语 + 中文解释一次，避免混用风格（执行 C3）。

### 路径越界 / 基名信任策略（A4/I1）
维持 Clarifications 既定“受控仓库可信”模型：显式基名允许包含子目录与 `..`，可能写出 images 基础目录之外；这是有意设计（调试 & 自定义产物布局）且 *不* 视为安全缺陷。**不再编写/保留** “防御性拒绝路径遍历” 测试；任务列表中对应防御测试已移除。

SECURITY NOTE: 处理不可信（用户上传 / 外部来源）文档时应禁用扩展或在未来 strict mode 下运行，以避免显式基名路径逃逸风险；当前模型假设仓库受控（FR-036）。

### 工具可用性摘要 (U2 / FR-031) & 计时日志 (U3 / FR-032)
参见 FR-031 / FR-032 规范化格式；实现若无法检测到任一工具仍应输出摘要行（值=missing）。

### 无 Mathematical / TreeProcessor 反射验证 (U1 / C1)
新增测试将通过：
1. 检查 gemspec 依赖列表与运行期已加载常量，确保未加载 `Mathematical` 常量。
2. 使用 Asciidoctor::Extensions.registry 断言未注册 TreeProcessor；仅注册 BlockProcessor 与 InlineMacroProcessor。
3. 若将来错误引入额外处理器类型，该测试红灯提醒（符合 P1 & FR-025）。

### 缓存逐出缺失验证 (C6)
简化策略：测试构建完成后无后台线程对缓存目录执行扫描/删除（例如通过线程列表 & 监控日志关键字），确认实现未暗含自动 eviction；如实现添加 eviction 需新增 FR。

### Inline 输出 (A5 / FR-024)
当前 v1 始终生成物理文件并通过 `<img>` 引用（不做 data URI 内联工作）；`:data-uri:` 属性的行为由核心 Asciidoctor 负责解析，扩展不主动构造 data: URL。未来若添加细粒度内联策略将新增独立 FR（参见 FR-033 占位）。


### Reserved / Merged Requirement Numbers
- FR-005: 并入 FR-011（缓存命中语义）；编号保留不复用。
- FR-012: 已合并入 FR-010 / FR-011；编号保留不再单独复用。
- FR-035: 已合并入 FR-022；编号保留不再复用。
- FR-015: 并入 FR-007（缓存禁用语义；D1 处理）。



### Key Entities *(include if feature involves data)*
- **Math Expression**: 用户在文档中的原始 LaTeX 公式文本（块/宏/内联）。
- **Rendering Request**: 一次独立渲染操作的抽象，绑定表达式、格式、工具链选择与归一化属性集合。
- **Output Artifact**: 最终产物文件 (svg/pdf/png) 及可选调试文件集合。
- **Cache Entry**: 由缓存键映射到产物路径与元数据（命中次数、生成时间）；不存储或解析工具/引擎版本或名称（见 Clarifications 工具版本签名决策）。
- **Toolchain Configuration**: 用户声明的引擎与转换工具组合；决定管线步骤。
- **Statistics Record**: 可选聚合指标（渲染次数、平均耗时、命中率）。
- **Pipeline Signature**: 渲染阶段序列与关键规范字段的摘要（不含引擎/工具名称），用于与缓存键字段集合一致性校验。
- **Disk Cache Store**: 管理多个缓存条目的持久化、原子写入与并发控制的抽象（区别于单个 Cache Entry）。

---

## Review & Acceptance Checklist
（当前版本：全部满足；新增或修改 FR 需重新审查下列条目）

### Content Quality
- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

### Requirement Completeness
- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

---

## Execution Status
（稳定快照）

- [x] User description parsed
- [x] Key concepts extracted
- [x] Ambiguities marked & resolved
- [x] User scenarios defined
- [x] Requirements generated
- [x] Entities identified
- [x] Review checklist passed

---

*Based on Constitution v3.1.0 - See `.specify/memory/constitution.md` (v3.1.0: 固定阶段列表澄清 / pipeline_signature 历史说明)*
