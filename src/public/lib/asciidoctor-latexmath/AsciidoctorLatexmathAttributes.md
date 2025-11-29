# Asciidoctor Latexmath 属性整理

本文基于现有文档（`README.md`、`DESIGN.md` 等）以及 asciidoctor-diagram 的属性设计，按照渲染流水线对 asciidoctor-latexmath 计划支持的配置开关进行分组：

```
源 LaTeX 片段 --> pdflatex (或替代引擎) --> PDF
															│
															├─(可选) pdf2svg → SVG
															└─(可选) PNG 工具 → PNG
```

> **兼容性说明：** asciidoctor-latexmath 必须遵循 Asciidoctor 的 `latexmath` 语法，不允许像 TikZ 那样读取 `~~~~` 前后的内容来改变语义。任何行为调整都应通过属性完成，保持块内容的数学语义纯净。

文中属性依旧分为 **文档级**（`:attr:`）、**块级 / 块宏**（`[latexmath,...]` / `latexmath::target[]`）与 **内联宏**（`latexmath:[...]`）三种作用域，但展示顺序与渲染步骤一致，便于快速定位某个阶段的控制项。

参考资料：

- [Document Attributes](https://docs.asciidoctor.org/asciidoc/latest/attributes/document-attributes/)
- [Element Attributes](https://docs.asciidoctor.org/asciidoc/latest/attributes/element-attributes/)
- [Positional & Named Attributes](https://docs.asciidoctor.org/asciidoc/latest/attributes/positional-and-named-attributes/)
- [Options](https://docs.asciidoctor.org/asciidoc/latest/attributes/options/)

## 渲染流水线总览

| 阶段 | 步骤 | 默认命令 | 典型输出 | 关键属性 |
| --- | --- | --- | --- | --- |
| 全局控制 | 解析 Asciidoctor 属性、决定缓存策略与输出格式 | *(无)* | *(配置预处理)* | `stem`, `latexmath-format`, `latexmath-cache`, `latexmath-cachedir` |
| 阶段一 | 生成临时 `.tex` 文档 | *(模板生成)* | `.tex` | `latexmath-preamble`, `latexmath-fontsize`, `latexmath-keep-artifacts`, `latexmath-artifacts-dir` |
| 阶段二 | 调用 LaTeX 引擎编译 | `pdflatex` | `.pdf` | `pdflatex`（可指向 `xelatex`/`lualatex`/`tectonic` 等） |
| 阶段三（A） | 直接输出 PDF | *(跳过额外命令)* | `.pdf` | `latexmath-format=pdf`、块/内联 `format=pdf` |
| 阶段三（B） | PDF → SVG | `dvisvgm` | `.svg` | `latexmath-pdf2svg`, 元素级 `pdf2svg=` |
| 阶段三（C） | PDF → PNG | 自动探测 (`pdftoppm`/`magick`/`gs`) | `.png` | `latexmath-png-tool`, `latexmath-ppi`, 元素级 `png-tool=` |

## 全局控制（适用于所有阶段）

| 属性 | 文档级语法 | 元素级覆写 | 默认值 | 作用说明 |
| --- | --- | --- | --- | --- |
| `stem` | `:stem: latexmath` 或 `:stem: tex` | *(无)* | *(未设置)* | 启用 Asciidoctor 的 STEM 管线，让 latexmath 扩展接管块与内联渲染。 |
| `latexmath-format` | `:latexmath-format: svg` | `[latexmath, format=png]` / `latexmath:[...,format=pdf]` | `svg` | 选择最终输出格式：`pdf`、`svg`、`png`。决定后续阶段是否需要额外转换。 |
| `latexmath-cache` | `:latexmath-cache: false` | `[latexmath, cache=false]` / `latexmath:[...,cache=false]` | `true` | 控制缓存使用；关闭后每次重新调用工具链。 |
| `latexmath-cachedir` | `:latexmath-cachedir: .cache/latexmath` | `[latexmath, cachedir=...]` *(可选)* | `<outdir>/.asciidoctor/latexmath`（若设置 `:imagesdir:` 则回退 `<outdir>/<imagesdir>`，见规范 FR-037） | 指定缓存目录位置。相对路径基于文档目录解析。 |

> 缓存相关选项同样可以通过块级 `%nocache` 开关控制，详见后文“可用选项”。
>
> 兼容性：旧名 `latexmath-cache-dir`（文档级）与块级 `cache-dir=` 仍被接受并输出一次 info 级弃用提示；推荐使用规范名称 `latexmath-cachedir` / `cachedir=`。

## 阶段一：LaTeX 文档组装（LaTeX → `pdflatex` 输入）

| 属性 | 文档级语法 | 元素级覆写 | 默认值 | 作用说明 | 关联产物 |
| --- | --- | --- | --- | --- | --- |
| `latexmath-preamble` | `:latexmath-preamble: \usepackage{bm}` | `[latexmath, preamble="\\usepackage{bm}"]` | *(空)* | 为生成的独立 `.tex` 文档追加前导宏包或自定义命令。 | 影响 `.tex` 输入 |
| `latexmath-fontsize` | `:latexmath-fontsize: 12pt` | `[latexmath, fontsize=10pt]` | `12pt` | 设置 `\documentclass` 的字体尺寸选项（如 10pt、12pt）。 | 影响 `.tex` 输入 |
| `latexmath-keep-artifacts` | `:latexmath-keep-artifacts: true` | `[latexmath, keep-artifacts=true]` / `[%keep-artifacts]` | `false` | 控制是否保留 `.tex`、`.log`、中间 PDF 等文件，便于调试。 | `.tex`、`.log`、临时 `.pdf` |
| `latexmath-artifacts-dir` | `:latexmath-artifacts-dir: tmp/latexmath` | `[latexmath, artifacts-dir=tmp/scratch]` | `imagesoutdir`（或文档目录） | 当保留产物时指定保存路径，可使用相对或绝对路径。 | 文件系统 |

## 阶段二：LaTeX 引擎执行（`pdflatex` → PDF）

切换编译引擎只需改写 `pdflatex` 属性，无需调整其它阶段。

| 属性 | 文档级语法 | 元素级覆写 | 默认值 | 作用说明 | 关联命令 |
| --- | --- | --- | --- | --- | --- |
| `pdflatex` | `:pdflatex: pdflatex`、`:pdflatex: xelatex`、`:pdflatex: tectonic` | `[latexmath, pdflatex=tectonic]` / `latexmath:[...,pdflatex=xelatex]` | `pdflatex` | 指定用于编译 `.tex` 的命令，可填命令名或绝对路径。 | LaTeX 引擎 (`pdflatex`/`xelatex`/`lualatex`/`tectonic` 等) |

## 阶段三：PDF 后处理与格式输出

### 3.1 直接保留 PDF

| 属性 | 文档级语法 | 元素级覆写 | 默认值 | 作用说明 |
| --- | --- | --- | --- | --- |
| `latexmath-format` | `:latexmath-format: pdf` | `[latexmath, format=pdf]` / `latexmath:[...,format=pdf]` | `svg` | 选定 `pdf` 后，流程在阶段二结束；生成的 PDF 可直接嵌入 Asciidoctor 图像节点。 |

块/块宏的首个位置属性同样可命名输出文件：`[latexmath, einstein-eq, pdf]` 会产出 `einstein-eq.pdf`。

### 3.2 PDF → SVG 转换

| 属性 | 文档级语法 | 元素级覆写 | 默认值 | 作用说明 | 关联命令 |
| --- | --- | --- | --- | --- | --- |
| `latexmath-format` | `:latexmath-format: svg` | `[latexmath, format=svg]` / `latexmath:[...,format=svg]` | `svg` | 触发 PDF → SVG 流程。 |
| `latexmath-pdf2svg` | `:latexmath-pdf2svg: /usr/bin/dvisvgm` | `[latexmath, pdf2svg=/opt/bin/pdf2svg]` / `latexmath:[...,pdf2svg=/opt/bin/pdf2svg]` | `dvisvgm` | 指定 PDF→SVG 转换命令，默认优先 `dvisvgm`，若不可用再回退到 `pdf2svg`。 | `dvisvgm`、`pdf2svg` |

### 3.3 PDF → PNG 转换

| 属性 | 文档级语法 | 元素级覆写 | 默认值 | 作用说明 | 关联命令 |
| --- | --- | --- | --- | --- | --- |
| `latexmath-format` | `:latexmath-format: png` | `[latexmath, format=png]` / `latexmath:[...,format=png]` | `svg` | 触发 PDF → PNG 流程。 |
| `latexmath-png-tool` | `:latexmath-png-tool: magick` | `[latexmath, png-tool=pdftoppm]` / `latexmath:[...,png-tool=gs]` | *(自动探测)* | 强制选择 PNG 渲染工具，默认为依次探测 `pdftoppm`、`magick`、`gs`。 | Poppler、ImageMagick、Ghostscript |
| `latexmath-ppi` | `:latexmath-ppi: 300` | `[latexmath, ppi=200]` / `latexmath:[...,ppi=200]` | `300` | 配置 PNG 输出的像素密度（DPI）。 | 所选 PNG 工具 |

## 元素级属性速查

### 块级 / 块宏

- **位置属性**：
	1. `target`：`[latexmath, einstein-eq]` 或 `latexmath::einstein-eq[]` 指定输出基名。
	2. `format`：`[latexmath, einstein-eq, svg]` 可快捷覆盖格式，相当于 `format=svg`。
- **命名属性**：在表格中出现的所有属性均可在块内覆写，例如 `pdflatex=tectonic`、`preamble="..."`。
- **选项（Options）**：

	| 选项名 | 默认行为 | 作用 |
	| --- | --- | --- |
	| `nocache` | 启用缓存 | 禁用缓存并强制重新渲染当前公式。 |
	| `keep-artifacts` | 清理中间文件 | 保留 `.tex`、`.log`、临时 `.pdf` 等调试产物。 |

### 内联宏

- 语法：`latexmath:[公式,attr=value,...]`，仅支持命名属性，不接受位置属性。
- 常用覆写：`format=svg`、`pdflatex=xelatex`、`pdf2svg=/path/to/pdf2svg`、`png-tool=magick`、`ppi=200`、`cache=false`。

## 属性交互与实践建议

- **输出目录**：生成文件遵循 Asciidoctor 的 `imagesdir` / `imagesoutdir`。建议显式设置 `imagesoutdir` 以集中产物目录。
- **引擎切换**：需要 Unicode 或 OpenType 字体时，将 `:pdflatex:` 设为 `xelatex` 或 `lualatex`；CI 场景可使用 `tectonic`（单命令下载依赖）。
- **SVG 工具替换**：默认优先使用 `dvisvgm`；若不可用，可将 `:latexmath-pdf2svg:` 指向 `pdf2svg` 或其它兼容转换器。
- **PNG 工具备用**：推荐 Poppler (`pdftoppm`) 作为首选，其次是 ImageMagick (`magick`)，最后回退到 Ghostscript (`gs`)。
- **调试流程**：遇到渲染异常时，结合 `keep-artifacts` 与 `nocache` 选项定位问题，检查保留下的 `.log` 文件。

## 示例

```adoc
:stem: latexmath
:latexmath-format: svg
:latexmath-preamble: \usepackage{bm}
:latexmath-fontsize: 11pt

[latexmath, einstein-eq, png, pdflatex=tectonic, ppi=200, options="keep-artifacts"]
+++
E = mc^2
+++

The inline form latexmath:[\bm{F} = m \times a,format=svg] keeps vector quality in HTML outputs.
```

## 后续工作建议

- 根据最终实现补充更多调试/诊断属性（如命令超时、日志级别）。
- 增设示例目录，分别演示 PDF / SVG / PNG 工作流。
- 在 CI 中检测所依赖的外部命令（`pdflatex`、`pdf2svg`、`magick` 等）是否可用。
