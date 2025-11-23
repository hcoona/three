# Asciidoctor Diagram 属性开关整理

本文基于 `/workspace/asciidoctor-extensions/asciidoctor-diagram` 源码汇总 Asciidoctor Diagram 扩展中可用的控制开关，仅涵盖对所有图类型生效的公共属性，以及 TikZ 图形引擎的私有属性。

以下分类遵循 Asciidoctor 的属性术语：

- **文档级属性（Document Attribute）**：以 `:diagram-*:` 或 `:tikz-*:` 形式声明，影响整篇文档或其后出现的所有相应图块。
- **块级属性（Element Attribute）**：在方括号属性列表中声明，可进一步划分为 *位置属性（Positional Attribute）*、*命名属性（Named Attribute）* 与 *选项（Option）*。

参考资料：

- [Document Attributes](https://docs.asciidoctor.org/asciidoc/latest/attributes/document-attributes/)
- [Element Attributes](https://docs.asciidoctor.org/asciidoc/latest/attributes/element-attributes/)
- [Positional & Named Attributes](https://docs.asciidoctor.org/asciidoc/latest/attributes/positional-and-named-attributes/)
- [Options](https://docs.asciidoctor.org/asciidoc/latest/attributes/options/)

## 公共开关（对所有图类型生效）

| 功能 | 文档级语法 | 块级语法 | 块级属性类型 | 默认值 / 行为 | 作用说明 | 代码位置 |
| --- | --- | --- | --- | --- | --- | --- |
| 输出格式 | `:diagram-format: svg` | `[diagram-type, target, svg]` 或 `[diagram-type, format=svg]` | 第二位置属性（可用命名属性覆写） | 渲染器支持列表中的第一个格式（HTML 后端会优先选择非 PDF 格式） | 指定生成文件的格式；块级设定优先于文档级设定。 | `lib/asciidoctor-diagram/diagram_processor.rb` |
| 错误处理 | `:diagram-on-error: abort` | `[diagram-type, on-error=abort]` | 命名属性 | `log` | 设置生成失败时的处理方式：`log` 记录错误并显示源代码，`abort` 抛异常并终止。 | `lib/asciidoctor-diagram/diagram_processor.rb` |
| 缓存目录 | `:diagram-cachedir: output/diagrams` | `[diagram-type, cachedir=output/diagrams]` | 命名属性 | `.asciidoctor/diagram`（位于输出目录） | 自定义缓存目录，缓存渲染产物及元数据。 | `lib/asciidoctor-diagram/diagram_processor.rb` |
| 禁用缓存 | `:diagram-nocache-option:` | `[diagram-type, options="nocache"]` 或 `[%nocache]` | 选项（Option） | 关闭 | 启用后每次都重新渲染图像，忽略缓存。 | `lib/asciidoctor-diagram/diagram_processor.rb` |
| 禁用优化 | `:diagram-nooptimise-option:` | `[diagram-type, options="nooptimise"]` 或 `[%nooptimise]` | 选项（Option） | 关闭 | 跳过 PNG/SVG/GIF/PDF 的后处理优化。 | `lib/asciidoctor-diagram/diagram_processor.rb` |
| 远程服务 URL | `:diagram-server-url: https://kroki.io` | `[diagram-type, server-url=https://kroki.io]` | 命名属性 | 未设置 | 配置远程渲染服务的基础 URL。 | `lib/asciidoctor-diagram/diagram_processor.rb` |
| 远程服务类型 | `:diagram-server-type: kroki_io` | `[diagram-type, server-type=kroki_io]` | 命名属性 | 未设置（使用远程服务时需指定） | 指定远程服务类型，目前实现支持 `plantuml` 与 `kroki_io`。 | `lib/asciidoctor-diagram/diagram_processor.rb` |
| 远程 GET 长度阈值 | `:diagram-max-get-size: 4096` | `[diagram-type, max-get-size=4096]` | 命名属性 | `1024` 字节 | 远程服务请求路径超过阈值且目标主机不是官方 PlantUML 时，自动改用 POST。 | `lib/asciidoctor-diagram/http/converter.rb` |
| SVG 嵌入方式 | `:diagram-svg-type: inline` | `[diagram-type, svg-type=inline]` 或 `[diagram-type, options="inline"]`/`[%interactive]` | 命名属性；另可通过选项 `inline`/`interactive` 直接设置 | `static` | 控制 SVG 输出方式：`static` 链接文件、`inline` 内联、`interactive` 允许交互。 | `lib/asciidoctor-diagram/diagram_processor.rb` |
| 自动计算 imagesdir | `:diagram-autoimagesdir:` | `[diagram-type, autoimagesdir=true]` | 命名布尔属性（可写成 `autoimagesdir=true` 或直接写 `autoimagesdir`） | 关闭 | 生成图像时自动为节点设置相对 `imagesdir`。 | `lib/asciidoctor-diagram/diagram_processor.rb` |

> 表中 `diagram-type` 表示具体的图类型（如 `plantuml`、`tikz` 等）。当同一属性同时在文档级与块级声明时，块级值具有更高优先级。
>
> 对于布尔型命名属性（如 `autoimagesdir`），若同时需要指定 `target` 等位置属性，建议使用 `autoimagesdir=true` 的形式，避免与位置属性发生歧义。

## TikZ 私有开关

| 功能 | 文档级语法 | 块级语法 | 块级属性类型 | 默认值 / 行为 | 作用说明 | 代码位置 |
| --- | --- | --- | --- | --- | --- | --- |
| 启用 preamble 分段 | `:tikz-preamble-option:` 或 `:tikz-preamble: true` | `[tikz, options="preamble"]`/`[%preamble]` 或 `[tikz, preamble=true]` | 选项（Option）或命名布尔属性 | 关闭 | 开启后会在首个 `~~~~` 处分割 TikZ 源码，前半段作为 LaTeX preamble，后半段作为主体。 | `lib/asciidoctor-diagram/tikz/converter.rb` |

TikZ 的前导模式被开启后，源代码会按首个 `~~~~` 进行拆分，前半段作为 LaTeX preamble 追加到自动生成的 `standalone` 文档头，其余内容继续作为 `tikzpicture` 主体渲染。该特性允许在单个图块中注入宏包或 `\tikzset` 配置。
