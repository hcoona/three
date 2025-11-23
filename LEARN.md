# asciidoctor-latexmath Extension 学习笔记

本文档记录了通过分析 asciidoctor-mathematical 和 asciidoctor-diagram/tikz 扩展所学习到的内容，为开发 asciidoctor-latexmath 扩展提供参考。

## 1. asciidoctor-mathematical 的 LaTeX 处理机制

### 1.1 扩展结构概述
**来源:** `/workspace/asciidoctor-extensions/asciidoctor-mathematical/lib/asciidoctor-mathematical/extension.rb` (行 1-10)

- 使用 `Asciidoctor::Extensions::Treeprocessor` 作为基类
- 通过 `autoload` 机制延迟加载 `Mathematical` 库
- 定义了内联数学宏的正则表达式: `/\\?(stem|(?:latex|ascii)math):([a-z,]*)\[(.*?[^\\])\]/m`

### 1.2 主处理流程
**来源:** `/workspace/asciidoctor-extensions/asciidoctor-mathematical/lib/asciidoctor-mathematical/extension.rb` (行 11-50)

```ruby
def process document
  return unless document.attr? 'stem'

  # 1. 配置参数解析
  format = ((document.attr 'mathematical-format') || 'png').to_sym
  ppi = ((document.attr 'mathematical-ppi') || '300.0').to_f
  inline = document.attr 'mathematical-inline'

  # 2. 创建 Mathematical 实例
  mathematical = ::Mathematical.new format: format, ppi: ppi

  # 3. 处理不同类型的内容
  # 3.1 处理 stem 块
  (document.find_by context: :stem, traverse_documents: true).each do |stem|
    handle_stem_block stem, mathematical, image_output_dir, image_target_dir, format, inline
  end

  # 3.2 处理散文块中的内联数学
  document.find_by(traverse_documents: true) {|b|
    (b.content_model == :simple && (b.subs.include? :macros)) || b.context == :list_item
  }.each do |prose|
    handle_prose_block prose, mathematical, image_output_dir, image_target_dir, format, inline
  end

  # 3.3 处理章节标题
  (document.find_by content: :section).each do |sect|
    handle_section_title sect, mathematical, image_output_dir, image_target_dir, format, inline
  end
end
```

### 1.3 行间数学块处理
**来源:** `/workspace/asciidoctor-extensions/asciidoctor-mathematical/lib/asciidoctor-mathematical/extension.rb` (行 53-78)

```ruby
def handle_stem_block(stem, mathematical, image_output_dir, image_target_dir, format, inline)
  equation_type = stem.style.to_sym

  case equation_type
  when :latexmath
    content = stem.content  # 直接使用原始内容
  when :asciimath
    content = AsciiMath.parse(stem.content).to_latex  # 转换为 LaTeX
  else
    return
  end

  # 生成图像
  img_target, img_width, img_height = make_equ_image content, stem.id, false, mathematical, image_output_dir, image_target_dir, format, inline

  # 替换原块为图像块
  parent = stem.parent
  if inline
    stem_image = create_pass_block parent, %{<div class="stemblock"> #{img_target} </div>}, {}
  else
    alt_text = stem.attr 'alt', (equation_type == :latexmath ? %($$#{content}$$) : %(`#{content}`))
    attrs = {'target' => img_target, 'alt' => alt_text, 'align' => 'center'}
    if format == :png
      attrs['width'] = %(#{img_width})
      attrs['height'] = %(#{img_height})
    end
    stem_image = create_image_block parent, attrs
  end
  parent.blocks[parent.blocks.index stem] = stem_image
end
```

### 1.4 内联数学处理
**来源:** `/workspace/asciidoctor-extensions/asciidoctor-mathematical/lib/asciidoctor-mathematical/extension.rb` (行 110-144)

关键特性:
- 使用正则表达式 `StemInlineMacroRx` 匹配内联数学宏
- 支持转义 (`\stem:` 不会被处理)
- 支持替换参数 (substitution parameters)
- 根据文档默认类型处理 `stem:` 宏

```ruby
# 处理内联数学的核心逻辑
if text && text.include?(':') && (text.include?('stem:') || text.include?('math:'))
  text = text.gsub(StemInlineMacroRx) do
    if (m = $~)[0].start_with? '\\'
      next m[0][1..-1]  # 转义处理
    end

    next '' if (eq_data = m[3].rstrip).empty?

    equation_type = default_equation_type if (equation_type = m[1].to_sym) == :stem
    if equation_type == :asciimath
      eq_data = AsciiMath.parse(eq_data).to_latex
    else # :latexmath
      eq_data = eq_data.gsub('\]', ']')
      subs = m[2].nil_or_empty? ? (to_html ? [:specialcharacters] : []) : (node.resolve_pass_subs m[2])
      eq_data = node.apply_subs eq_data, subs unless subs.empty?
    end

    source_modified = true
    img_target, img_width, img_height = make_equ_image eq_data, nil, true, mathematical, image_output_dir, image_target_dir, format, inline
    if inline
      %(pass:[<span class="steminline">#{img_target}</span>])
    else
      %(image:#{img_target}[width=#{img_width},height=#{img_height}])
    end
  end
end
```

### 1.5 图像生成与缓存
**来源:** `/workspace/asciidoctor-extensions/asciidoctor-mathematical/lib/asciidoctor-mathematical/extension.rb` (行 153-171)

```ruby
def make_equ_image(equ_data, equ_id, equ_inline, mathematical, image_output_dir, image_target_dir, format, inline)
  input = equ_inline ? %($#{equ_data}$) : %($$#{equ_data}$$)  # 添加 LaTeX 定界符

  result = mathematical.parse input
  if inline
    result[:data]  # 内联模式直接返回数据
  else
    unless equ_id
      equ_id = %(stem-#{::Digest::MD5.hexdigest input})  # 基于内容生成 ID
    end
    image_ext = %(.#{format})
    img_target = %(#{equ_id}#{image_ext})
    img_file = ::File.join image_output_dir, img_target

    ::IO.write img_file, result[:data]  # 写入文件

    img_target = ::File.join image_target_dir, img_target unless image_target_dir == '.'
    [img_target, result[:width], result[:height]]
  end
end
```

## 2. asciidoctor-diagram TikZ 处理机制

### 2.1 扩展注册结构
**来源:** `/workspace/asciidoctor-extensions/asciidoctor-diagram/lib/asciidoctor-diagram/tikz.rb`

```ruby
Asciidoctor::Extensions.register do
  block Asciidoctor::Diagram::TikZBlockProcessor, :tikz
  block_macro Asciidoctor::Diagram::TikZBlockMacroProcessor, :tikz
  inline_macro Asciidoctor::Diagram::TikZInlineMacroProcessor, :tikz
end
```

**来源:** `/workspace/asciidoctor-extensions/asciidoctor-diagram/lib/asciidoctor-diagram/tikz/extension.rb`

```ruby
class TikZBlockProcessor < DiagramBlockProcessor
  use_converter TikZConverter
end

class TikZBlockMacroProcessor < DiagramBlockMacroProcessor
  use_converter TikZConverter
end

class TikZInlineMacroProcessor < DiagramInlineMacroProcessor
  use_converter TikZConverter
end
```

### 2.2 TikZ Converter 实现
**来源:** `/workspace/asciidoctor-extensions/asciidoctor-diagram/lib/asciidoctor-diagram/tikz/converter.rb` (行 8-20)

```ruby
class TikZConverter
  include DiagramConverter
  include CliGenerator

  def supported_formats
    [:pdf, :svg]  # 支持 PDF 和 SVG 输出
  end

  def collect_options(source)
    {
        :preamble => source.opt('preamble') || source.attr('preamble') == 'true'
    }
  end
end
```

### 2.3 LaTeX 文档生成与工具链调用
**来源:** `/workspace/asciidoctor-extensions/asciidoctor-diagram/lib/asciidoctor-diagram/tikz/converter.rb` (行 22-66)

```ruby
def convert(source, format, options)
  latexpath = source.find_command('pdflatex')

  if format == :svg
    svgpath = source.find_command('pdf2svg')  # SVG 需要额外的转换工具
  else
    svgpath = nil
  end

  # 处理 preamble 选项
  if options[:preamble]
    preamble, body = source.to_s.split(/^~~~~$/, 2)  # 用 ~~~~ 分割前导码和主体
    unless body
      body = preamble
      preamble = ''
    end
  else
    preamble = ''
    body = source.to_s
  end

  # 生成完整的 LaTeX 文档
  latex = <<'END'
\documentclass[border=2bp, tikz]{standalone}
\usepackage{tikz}
END
  latex << preamble
  latex << <<'END'
\begin{document}
\begingroup
\tikzset{every picture/.style={scale=1}}
END
  latex << body
  latex << <<'END'
\endgroup
\end{document}
END

  # 生成 PDF
  pdf = generate_file(latexpath, 'tex', 'pdf', latex) do |tool_path, input_path, output_path|
    {
        :args => [tool_path, '-shell-escape', '-file-line-error', '-interaction=nonstopmode', '-output-directory', Platform.native_path(File.dirname(output_path)), Platform.native_path(input_path)],
        :out_file => "#{File.dirname(input_path)}/#{File.basename(input_path, '.*')}.pdf"
    }
  end

  # 如果需要 SVG，则转换 PDF 到 SVG
  if svgpath
    generate_file(svgpath, 'pdf', 'svg', pdf) do |tool_path, input_path, output_path|
      {
        :args => [tool_path, Platform.native_path(input_path), Platform.native_path(output_path)],
        :chdir => source.base_dir
      }
    end
  else
    pdf
  end
end
```

## 3. 通用 Diagram 处理框架 (diagram_processor.rb)

### 3.1 处理器类型与继承关系
**来源:** `/workspace/asciidoctor-extensions/asciidoctor-diagram/lib/asciidoctor-diagram/diagram_processor.rb` (行 384-474)

```ruby
# 块处理器 (用于 [tikz] 块)
class DiagramBlockProcessor < Asciidoctor::Extensions::BlockProcessor
  include DiagramProcessor

  def self.inherited(subclass)
    subclass.use_dsl
    subclass.name_positional_attributes ['target', 'format']
    subclass.contexts [:listing, :literal, :open]
    subclass.content_model :simple
  end
end

# 块宏处理器 (用于 tikz::filename[] 宏)
class DiagramBlockMacroProcessor < Asciidoctor::Extensions::BlockMacroProcessor
  include DiagramProcessor

  def self.inherited(subclass)
    subclass.use_dsl
    subclass.name_positional_attributes ['format']
  end
end

# 内联宏处理器 (用于 tikz:filename[] 内联宏)
class DiagramInlineMacroProcessor < Asciidoctor::Extensions::InlineMacroProcessor
  include DiagramProcessor
end
```

### 3.2 核心处理流程
**来源:** `/workspace/asciidoctor-extensions/asciidoctor-diagram/lib/asciidoctor-diagram/diagram_processor.rb` (行 63-130)

```ruby
def process(parent, reader_or_target, attributes)
  # 1. 属性标准化
  normalised_attributes = attributes.inject({}) { |h, (k, v)| h[normalise_attribute_name(k)] = v; h }

  # 2. 创建转换器
  converter = config[:converter].new
  supported_formats = supported_formats(converter)

  # 3. 创建源对象
  source = create_source(parent, reader_or_target, normalised_attributes)
  source = converter.wrap_source(source)

  # 4. 格式选择
  format = source.attributes.delete('format') || source.global_attr('format', supported_formats[0])
  format = format.to_sym if format.respond_to?(:to_sym)

  # 5. 创建最终块
  case format
  when *TEXT_FORMATS
    block = create_literal_block(parent, source, format, converter)
  else
    block = create_image_block(parent, source, format, converter)  # 大多数情况
  end

  # 6. 设置标题和说明
  block.title = title
  block.assign_caption(caption, 'figure')
  block
end
```

### 3.3 图像生成与缓存机制
**来源:** `/workspace/asciidoctor-extensions/asciidoctor-diagram/lib/asciidoctor-diagram/diagram_processor.rb` (行 169-225)

```ruby
def create_image_block(parent, source, format, converter)
  image_name = "#{source.image_name}.#{format}"

  # 路径设置
  image_file = parent.normalize_system_path(image_name, image_output_dir(parent))
  metadata_file = parent.normalize_system_path("#{image_name}.cache", cache_dir(source, parent))

  use_cache = !source.global_opt('nocache')

  # 缓存检查
  if use_cache && File.exist?(metadata_file)
    metadata = File.open(metadata_file, 'r') {|f| JSON.load(f, nil, :symbolize_names => true, :create_additions => false) }
  else
    metadata = {}
  end

  image_attributes = source.attributes
  options = converter.collect_options(source)

  # 重新生成条件: 文件不存在 || 需要重新处理 || 选项改变
  if !File.exist?(image_file) || source.should_process?(image_file, metadata) || options != metadata[:options]
    # 图像生成
    result = converter.convert(source, format, options)
    if result.is_a? Hash
      image = result[:result]
      extra = result[:extra]
    else
      image = result
      extra = {}
    end

    # 元数据更新
    metadata = source.create_image_metadata
    metadata[:options] = options

    # 图像优化
    allow_image_optimisation = !source.global_opt('nooptimise')
    image, metadata[:width], metadata[:height] = params[:decoder].post_process_image(image, allow_image_optimisation)

    # 文件写入
    FileUtils.mkdir_p(File.dirname(image_file)) unless Dir.exist?(File.dirname(image_file))
    File.open(image_file, 'wb') {|f| f.write image}

    # 额外文件处理
    extra.each do |name, data|
      File.open(image_file + ".#{name}", 'wb') {|f| f.write data}
    end

    # 缓存元数据
    if use_cache
      FileUtils.mkdir_p(File.dirname(metadata_file)) unless Dir.exist?(File.dirname(metadata_file))
      File.open(metadata_file, 'w') { |f| JSON.dump(metadata, f) }
    end
  end

  # ... 其余图像块创建逻辑
end
```

## 4. 关键设计要点总结

### 4.1 扩展类型选择
- **TreeProcessor**: 适用于需要全文档扫描和处理的场景 (如 mathematical)
- **BlockProcessor + BlockMacroProcessor + InlineMacroProcessor**: 适用于特定语法块的处理 (如 tikz)

### 4.2 图像生成策略
- **asciidoctor-mathematical**: 依赖外部库 (Mathematical gem) 直接生成图像数据
- **asciidoctor-diagram**: 通过 CLI 工具链 (pdflatex, pdf2svg) 生成图像文件

### 4.3 缓存机制
- **mathematical**: 基于内容 MD5 哈希生成文件名，自动缓存
- **diagram**: 使用 `.cache` 文件存储元数据，支持 `nocache` 选项

### 4.4 格式支持
- **mathematical**: PNG/SVG, 通过 Mathematical 库配置
- **tikz**: PDF/SVG, 通过 LaTeX 工具链生成

### 4.5 LaTeX 处理流程
1. **内容解析**: 识别 latexmath/asciimath 语法
2. **LaTeX 包装**: 添加适当的定界符 ($...$ 或 $$...$$)
3. **图像生成**: 调用相应的渲染引擎
4. **文件管理**: 处理输出路径、缓存、元数据
5. **块替换**: 将原始内容替换为图像块

## 5. Processor 组合策略与触发时机

### 5.1 同时覆盖三种语法入口
- `tikz` 扩展同时注册了块、块宏与内联宏处理器，并复用同一个 `TikZConverter`，从而覆盖 `[tikz]....`、`tikz::[]` 与 `tikz:[]` 三种语法形式。参见 `tikz/extension.rb` (行 1-16)。
- 这一结构说明：要为 `latexmath` 提供一致体验，也可以定义三个处理器并共用同一渲染核心（例如统一的 AST→SVG/MathML 逻辑），再在注册阶段把它们绑定到同名宏。[Asciidoctor Extensions Register](https://docs.asciidoctor.org/asciidoctor/latest/extensions/register/)

### 5.2 不同处理器的触发时机
- **Treeprocessor**：`MathematicalTreeprocessor#process` 会在文档 AST 构建完后遍历所有 `:stem` 节点，再去处理散文块与章节标题，这表明 Treeprocessor 获取的是“全局 AST 视图”。参见 `asciidoctor-mathematical/extension.rb` (行 11-50)。官方文档也说明 Treeprocessor 在内联替换之前运行，可用于跨块分析。[Tree Processor 文档](https://docs.asciidoctor.org/asciidoctor/latest/extensions/tree-processor/)
- **BlockProcessor / BlockMacroProcessor**：在解析阶段就会被触发，从 `DiagramBlockProcessor` 与 `DiagramBlockMacroProcessor` 继承结构可见，它们直接将源内容包装成 `DiagramSource` 并交由 `DiagramProcessor#process` 生成块节点。参见 `diagram_processor.rb` (行 380-419)。[Block Processor 文档](https://docs.asciidoctor.org/asciidoctor/latest/extensions/block-processor/)、[Block Macro Processor 文档](https://docs.asciidoctor.org/asciidoctor/latest/extensions/block-macro-processor/)
- **InlineMacroProcessor**：在块转换的内联替换阶段调用。`DiagramInlineMacroProcessor#process` 先复用块转换逻辑，再把结果转成内联节点，说明其工作发生在块内容即将写出之前。参见 `diagram_processor.rb` (行 432-457)。[Inline Macro Processor 文档](https://docs.asciidoctor.org/asciidoctor/latest/extensions/inline-macro-processor/)

### 5.3 属性快照与替换控制
- `DiagramInlineMacroProcessor#process` 会复制块级结果的属性，并显式移除 `subs`，避免 Asciidoctor 对已渲染好的内联节点再次做替换。参见 `diagram_processor.rb` (行 439-455)。
- `handle_inline_stem` 在处理每个内联公式时都会重新读取文档的 `stem` 相关属性，并按需决定替换列表 (`node.resolve_pass_subs`) 与输出包装格式。参见 `asciidoctor-mathematical/extension.rb` (行 118-141)。这提供了一个“即时读取并固化配置”的范式，适合在多文件 include 场景下捕获当时的属性值。
- 结合以上两点，实现 latexmath 扩展时，可在对应 Processor 内部将关键属性写回节点或渲染结果，避免后续文档中的属性改动影响已经处理的内容。

## 6. asciidoctor-latexmath 开发建议

基于以上分析，建议 asciidoctor-latexmath 采用以下处理策略:

1. **扩展类型组合**: 直接为三种语法入口分别实现 `InlineMacroProcessor`、`BlockProcessor` 与 `BlockMacroProcessor`，复用同一个渲染核心（参考 `tikz/extension.rb` 行 1-16 与 `diagram_processor.rb` 行 360-457）。
2. **图像/输出生成**: 固定走本地 LaTeX 工具链，与 `TikZConverter` 相同使用 `pdflatex`（必要时串联 `pdf2svg`）将表达式渲染为最终图像。参见 `tikz/converter.rb` (行 22-66)。
3. **属性快照**: 在各 processor 的 `process` 方法里即时读取需要的文档属性并写入生成节点，沿用 `handle_inline_stem` 中的做法（`asciidoctor-mathematical/extension.rb` 行 118-141），避免后续属性改动影响既有公式。
4. **缓存策略**: 使用 diagram 框架的 `.cache` 元数据机制（`diagram_processor.rb` 行 169-225），或基于表达式内容的散列生成文件名，以减少重复渲染。

## 7. 2025-09-30 设计演进记录

- **阅读材料**: `README.md`（项目概览、属性列表、缓存章节）与 `asciidoctor-diagram/README.adoc`（配置及处理流程简介）。
- **关键结论**:
  - 保持与 asciidoctor-diagram 一致的属性命名与缓存目录结构（`<outdir>/.asciidoctor/...`）。
  - 渲染流程需显式分离“捕获 → 渲染 → 集成”三个阶段，并针对内联模式提供 data URI 与嵌入式 SVG 双策略。
  - 允许用户通过 `pdflatex`、`latexmath-pdf2svg` 等属性覆盖命令，确保跨平台兼容。
- **后续行动**: 在 `DESIGN.md` 中固化整体架构、组件职责、PlantUML 类图；实现阶段遵循该文档拆分模块。

## 8. 2025-10-01 设计抽象优化

- **目的**: 进一步提升渲染管线的可组合性、缓存可替换性，以及对命令链路的扩展能力。
- **新增思路**:
  - 在注册阶段引入 `ToolchainDetector`，一次性确定 pdf2svg/dvisvgm/latexmath-png-tool 可用性；
  - 通过 `RendererCatalog` 维护目标格式到 Renderer 的映射，块/宏/内联公用同一套装饰后的 Renderer；
  - `RendererFactory` 依据探测结果拼接 `RendererPipeline`，将 `LatexEngineStage` 与多个 `ExternalToolStage` 组合成 LaTeX→PDF→目标格式链路；
  - `CachingRenderer` 继续以装饰器形式包裹，缓存键中囊括块级属性（格式覆盖、工具选择）；
  - 三个 Processor（块、块宏、内联）直接使用 Catalog，不再依赖独立的 RenderManager 层。
- **产出**: `DESIGN.md` 更新后的类图、时序图与正文说明，改写 Renderer 抽象以匹配组合式渲染策略。

## 9. 2025-10-02 架构细化与属性对齐

- **阅读材料**:
  - `class-digram-v2.plantuml`（Lines 1-170）：确认最新分层架构，包含 `ExtensionRegistry`→`RendererBuilder`→`IRenderer` 的拓扑关系以及 `DiskCache` 缓存包装。
  - `AsciidoctorLatexmathAttributes.md`（Lines 1-180）：梳理 `latexmath-format`、`latexmath-cache`、`latexmath-preamble`、`latexmath-png-tool` 等全量属性及块级 `nocache`/`keep-artifacts` 选项。
- **关键结论**:
  - 采用六层设计（入口、配置、处理器、请求、渲染抽象、具体渲染器），每层保持单一职责，满足系统设计最佳实践。
  - `RendererBuilder` 负责生成确定性管线，不做运行时回退，缓存键须含 pipeline 签名、内容哈希、模式、ppi 与引擎版本，确保与 asciidoctor-diagram 的缓存一致性。
  - Processor 全面依赖 `BlockProcessor` / `BlockMacroProcessor` / `InlineMacroProcessor`，拒绝 TreeProcessor，实现目标语法覆盖。
  - `Configuration` 需对 `latexmath-cache-dir`、`latexmath-artifacts-dir`、`imagesoutdir` 做统一解析，并提供 `inline_data_uri?` 钩子以支持内联渲染策略。
- **后续行动**:
  - 在 `DESIGN.md` 中替换旧类图与描述，记录新的管线层次、缓存与元数据需求。
  - 制定属性→组件映射矩阵，明确每个属性的解析与消费位置，为实现阶段提供检查清单。
