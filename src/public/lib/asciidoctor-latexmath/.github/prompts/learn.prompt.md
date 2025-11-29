你是一个 Ruby 工程师，你是一个 Asciidoc Extension 开发者。你的最终目的是开发 asciidoctor-latexmath extension，但是在此之前，你当前的任务是先学习 asciidoctor-mathematical 和 asciidoctor-diagram 里面的 tikz 处理流程。

<instructions>
你需要学习 asciidoctor-mathematical 是怎么处理 latexmath 内联元素和行间元素的。
你需要学习 asciidoctor-diagram tikz 是怎么处理 latexmath 内联元素和行间元素和行间宏的。
你需要学习 asciidoctor-diagram tikz 是怎么调用系统提供的 LaTeX 工具链生成 tikz 的 PDF/SVG 的。以及具体的参数，处理流程，缓存等等。
你需要将你学习到的内容写到 asciidoctor-latexmath 项目的 LEARN.md 中（未创建），标注来源文件和行号区间，便于之后写代码的时候再重新查找。
</instructions>

<reference label=官方文档>
https://docs.asciidoctor.org/asciidoc/latest/stem/
https://docs.asciidoctor.org/asciidoctor/latest/extensions/register/
https://docs.asciidoctor.org/asciidoctor/latest/extensions/logging/
https://docs.asciidoctor.org/asciidoctor/latest/extensions/tree-processor/
https://docs.asciidoctor.org/asciidoctor/latest/extensions/block-processor/
https://docs.asciidoctor.org/asciidoctor/latest/extensions/block-macro-processor/
https://docs.asciidoctor.org/asciidoctor/latest/extensions/inline-macro-processor/
</reference>

***** Begin Conversation
**You:**

我想要给 Asciidoctor 写个插件，用于处理 latexmath 内联公式和行间公式，应该用什么 processor？还是应该分别用不同的 processor

* * *

**ChatGPT:**

简短说：**要分开用两个 processor。**

* **内联公式**（例如 `latexmath:[e^{i\pi}+1=0]`）用 **InlineMacroProcessor**。

* **行间/块级公式**（例如

    ```
    [latexmath]
    ++++
    \int_0^\infty e^{-x^2} dx
    ++++
    ```

    ）用 **BlockProcessor**。如果你更喜欢 `latexmath::[]` 这种“块宏”语法，则用 **BlockMacroProcessor**。之所以要分开，是因为 Asciidoctor 的扩展 API 把“内联”和“块级”当成两类节点与生命周期来处理，返回的节点类型也不同。[Asciidoctor Docs+2Asciidoctor Docs+2](https://docs.asciidoctor.org/asciidoc/latest/stem/?utm_source=chatgpt.com)


推荐实现思路
======

1. **注册两个处理器，共享同一个渲染函数**（把 LaTeX 交给 MathJax/KaTeX，或转成你需要的 HTML/MathML/SVG）。

2. **HTML 后端**：在内联用 `\(...\)` 包起来，块级用 `\[...\]`；再用 **DocinfoProcessor** 在 `<head>` 注入 MathJax/KaTeX。[Asciidoctor Docs+1](https://docs.asciidoctor.org/asciidoctor/latest/extensions/inline-macro-processor/?utm_source=chatgpt.com)

3. **PDF 后端**：优先走现成的 **asciidoctor-mathematical**（它会处理 `latexmath` 与 `stem` 的块与内联，并把表达式渲染成 SVG/PNG），或参考 Asciidoctor PDF 的 STEM 支持。[GitHub+1](https://github.com/asciidoctor-contrib/asciidoctor-mathematical?utm_source=chatgpt.com)


极简 Ruby 骨架（可放在自定义扩展里）
=====================

```ruby
require 'asciidoctor'
require 'asciidoctor/extensions'

module MyMath
  def self.render_latex(expr, display:)
    # 这里调用你的渲染器：MathJax/KaTeX 服务端、或直接包 \(...\)/\[...\]
    display ? "\\[#{expr}\\]" : "\\(#{expr}\\)"
  end

  class LatexInline < Asciidoctor::Extensions::InlineMacroProcessor
    use_dsl
    named :latexmath
    name_positional_attributes 'expr'  # 允许 latexmath:[...]
    def process parent, target, attrs
      expr = attrs['expr'] || target
      html = MyMath.render_latex(expr, display: false)
      create_inline_pass parent, html
    end
  end

  class LatexBlock < Asciidoctor::Extensions::BlockProcessor
    use_dsl
    named :latexmath
    on_context :pass, :literal, :paragraph
    parse_content_as :raw
    def process parent, reader, attrs
      expr = reader.read
      html = MyMath.render_latex(expr, display: true)
      create_pass_block parent, html, attrs
    end
  end

  class MathDocinfo < Asciidoctor::Extensions::DocinfoProcessor
    at_location :head
    def process doc
      %(<script src="https://cdn.jsdelivr.net/npm/mathjax@3/es5/tex-chtml.js"></script>)
    end
  end
end

Asciidoctor::Extensions.register do
  inline_macro MyMath::LatexInline
  block        MyMath::LatexBlock
  docinfo_processor MyMath::MathDocinfo
end
```

> 备注
>
> * 选择 **BlockProcessor** 是为了匹配 `[latexmath] ++++ ... ++++` 这种常见写法；若你偏好 `latexmath::[]`，把上面的 `LatexBlock` 改成 **BlockMacroProcessor** 即可（逻辑共用同一渲染函数）。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoctor/latest/extensions/block-macro-processor/?utm_source=chatgpt.com)
>
> * Asciidoctor 本身把 STEM 元素当作**直通（passthrough）**，具体渲染交给后端/扩展，所以也可以用 **Treeprocessor** 在 AST 上统一找出 stem/latex 节点再替换为你想要的 HTML。[Asciidoctor Docs+1](https://docs.asciidoctor.org/asciidoc/latest/stem/?utm_source=chatgpt.com)
>

如果你告诉我目标后端（HTML/静态站、PDF、或两者）和期望语法（`[latexmath]` 还是 `latexmath::`），我可以把骨架改成可直接使用的版本。

* * *

**You:**

latexmath::[] 这种“块宏”语法 是指什么

* * *

**ChatGPT:**

在 Asciidoctor 里，`latexmath::[]` 这种写法叫 **块宏（block macro）**。它和 `image::[]`、`include::[]` 这样的语法属于同一类。

### 语法特点

* **形态**：`名称::[参数]`
    例如：

    ```adoc
    latexmath::[ \int_0^\infty e^{-x^2} dx ]
    ```

* **行为**：会被当作一个**独立的块**来处理，而不是内联元素。

* **用途**：适合一整段数学公式的显示，而不是句子里插一句公式。


### 区别

* **内联宏（inline macro）**：比如 `latexmath:[e^{i\pi}+1=0]`，它会出现在一行文字中。

* **块宏（block macro）**：比如 `latexmath::[ ... ]`，整行就是这个宏调用，渲染时变成一个块级元素。

* **普通块（block）**：比如 `[latexmath] ++++ ... ++++`，这种写法是“带角色的块”，和块宏不同，它可以跨多行。


### 扩展开发时的处理器

* 如果要支持 `latexmath::[]` 这种写法，应写 **BlockMacroProcessor**。

* 如果要支持 `latexmath:[...]`（内联），就写 **InlineMacroProcessor**。

* 如果要支持 `[latexmath] ++++ ... ++++` 这种成块内容，则要用 **BlockProcessor**。


* * *

要不要我给你写一个对比示例，展示三种写法在 Asciidoctor 里渲染出来分别是什么效果？

* * *

**You:**

要支持这三个语法，需要3个不同的 processor 吗

* * *

**ChatGPT:**

是的，要同时支持这三种语法，通常需要 **三个不同的 processor**，因为它们在 Asciidoctor 的 AST 里是不同的节点类型：

### 1. 内联宏

* **语法**：`latexmath:[ e^{i\pi}+1=0 ]`

* **Processor**：`InlineMacroProcessor`

* **原因**：它嵌在段落文字中，要返回的是一个 inline 节点。


### 2. 块宏

* **语法**：`latexmath::[ \int_0^\infty e^{-x^2} dx ]`

* **Processor**：`BlockMacroProcessor`

* **原因**：整行就是一个宏调用，要生成一个 block 节点。


### 3. 标记块

* **语法**：

    ```adoc
    [latexmath]
    ++++
    \int_0^\infty e^{-x^2} dx
    ++++
    ```

* **Processor**：`BlockProcessor`

* **原因**：这是一个带 role 的显式块（`[latexmath]`），需要处理多行原始内容。


* * *

### 可以共用的部分

这三个 Processor 内部都可以调用**同一个渲染函数**（比如把 LaTeX 包装成 `\(...\)` 或 `\[...\]`，或者交给 MathJax/KaTeX），只是入口不同。这样维护成本不会太高。

* * *

要不要我帮你写一个完整的扩展示例，把三个 processor 都注册好，并且共用一个渲染函数？

* * *

**You:**

Treeprocessor 好像计算时机和 InlineMacroProcessor, BlockProcessor, BlockMacroProcessor 不一样，确认这几个 processor 计算时机的差别和影响

* * *

**ChatGPT:**

结论先说：**这几类处理器触发的“时机”确实不同**，而且会直接影响你能拿到的文本形态、能不能看到内联标记、以及要不要自己再做二次替换。

处理顺序（精简版）
=========

1. **Preprocessor / IncludeProcessor**：读源文前期处理。

2. **解析阶段（build AST）**：

    * **BlockMacroProcessor**（如 `latexmath::[]`）

    * **BlockProcessor**（如 `[latexmath] ++++...++++`）
        这俩都是**在解析时**触发，按文档顺序出现就调用。[Asciidoctor Docs+2Asciidoctor Docs+2](https://docs.asciidoctor.org/asciidoctorj/latest/extensions/conversion-process-overview/?utm_source=chatgpt.com)

3. **Treeprocessor**：**AST 构建完成之后、内联标记还没解释之前**运行；此时块节点已有了，但块里的**内联内容尚未做替换/拆分**。[Asciidoctor Docs+1](https://docs.asciidoctor.org/asciidoctor/latest/extensions/tree-processor/?utm_source=chatgpt.com)

4. **转换阶段（convert blocks）**：对每个块做内联替换（substitutions），其中 **macros** 是靠后的步骤，这里才会触发
    **InlineMacroProcessor**（如 `latexmath:[...]`）。因此它见到的内容通常已经做过大部分替换（属性、引号等）。[Asciidoctor Docs+1](https://docs.asciidoctor.org/asciidoc/latest/subs/?utm_source=chatgpt.com)

5. **（可选）DocinfoProcessor / Postprocessor**：前者在生成文档时插入 `<head>`/`<footer>` 片段，后者对最终输出再加工（比如注入脚本）。官方文档亦有说明。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoctorj/latest/extensions/extensions-introduction/?utm_source=chatgpt.com)


对你插件设计的具体影响
===========

* **为什么三种语法最好分开写处理器？**

    * `latexmath::[]`（块宏）和 `[latexmath] ++++...++++`（块）在**解析阶段**就被拦截；

    * `latexmath:[...]`（内联宏）要到**转换阶段**才触发。
        如果你只用 Treeprocessor 想“一网打尽”，会发现它**看不到**内联宏已经被解析成的节点，因为那一步还没发生；它只能改块的原始文本，之后内联替换再发生。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoctor/latest/extensions/tree-processor/?utm_source=chatgpt.com)

* **InlineMacroProcessor 的“晚触发”副作用**
    它运行时，内联替换已大多完成（`macros` 本来就靠后），所以诸如属性引用 `{foo}` 常已展开、引号/特殊字符替换多已做完。你如果需要控制对你返回节点再做哪些替换，要在返回的节点上设置 `subs`。[Asciidoctor Docs+1](https://docs.asciidoctor.org/asciidoctor/latest/extensions/inline-macro-processor/?utm_source=chatgpt.com)

* **BlockMacro / BlockProcessor 的“早触发”特点**
    它们是在把行读成块时调用：

    * 适合把一行块宏或一个显式块直接变成你想要的块节点（例如直接产出 passthrough 块）。

    * 对于显式块，你还能通过内容模型/`subs` 控制后续是否再跑内联替换。[Asciidoctor Docs+1](https://docs.asciidoctor.org/asciidoctorj/latest/extensions/block-processor/?utm_source=chatgpt.com)

* **Treeprocessor 的定位**
    适合**在统一的 AST 层面**做跨块的收尾工作（如给所有数学块打标、把源码中的占位文本替换掉、统计清单等），但不要指望它拿到已经“内联展开”的片段——那些在随后的转换阶段才发生。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoctor/latest/extensions/tree-processor/?utm_source=chatgpt.com)


实操建议
====

* **三个语法，三种 processor**：`InlineMacroProcessor`（内联）、`BlockMacroProcessor`（块宏）、`BlockProcessor`（显式块）。核心渲染逻辑共享一个函数即可。[Asciidoctor Docs+1](https://docs.asciidoctor.org/asciidoctorj/latest/extensions/block-processor/?utm_source=chatgpt.com)

* **控制二次替换**：内联处理器返回的节点若是原样注入（如 HTML/MathJax 标记），请给节点/块设 `subs`，避免再次被替换（或只保留你需要的替换）。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoc/latest/subs/apply-subs-to-blocks/?utm_source=chatgpt.com)

* **需要统一收尾**（比如统计/注入资源）：再加一个 Treeprocessor；真正的脚本注入放到 Docinfo/Postprocessor。[Asciidoctor Docs+1](https://docs.asciidoctor.org/asciidoctor/latest/extensions/tree-processor/?utm_source=chatgpt.com)


如果你愿意，我可以把你现有的目标（KaTeX/MathJax、HTML/PDF 后端）代入，给出一份按上述时机精确设置 `subs` 与返回节点类型的最小可用扩展示例。

* * *

**You:**

假设是多文件互相 include，并且在每个文件中都有可能重新对文档 attribute 赋值，这种情况是不是 tree processor 在处理的时候不会知道 attribute 变化？

* * *

**ChatGPT:**

短答：**Treeprocessor 看不到“随文变化”的属性历史**。它运行在**AST 构建完成、但还没开始转换（convert）**的时候；此时 include 都已展开，块级节点已建好，但**属性条目（`:foo:` 这类赋值）不会作为节点进 AST**，也不会附带“在每个位置当时的取值”。你在 Treeprocessor 里读到的通常只是“当前（解析结束时）文档属性表”的状态，而不是“某个块在源文件处的属性快照”。[Asciidoctor Docs+2Asciidoctor Docs+2](https://docs.asciidoctor.org/asciidoctorj/latest/extensions/treeprocessor/?utm_source=chatgpt.com)

再补两点相关时机差异（影响你怎么设计插件）：

* **InlineMacroProcessor**（处理 `latexmath:[...]`）是在**内联替换阶段**触发；宏替换本来就在内联替换的靠后步骤，所以它看到的文本多半已经做过大部分替换。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoctor/latest/extensions/inline-macro-processor/?utm_source=chatgpt.com)

* **BlockMacroProcessor / BlockProcessor**（处理 `latexmath::[]` 和 `[latexmath] ++++...++++`）是在**解析阶段**就触发，把行/块直接变成节点。[Asciidoctor Docs+1](https://docs.asciidoctor.org/asciidoctorj/latest/extensions/block-macro-processor/?utm_source=chatgpt.com)


关于 include 与作用域（为什么 Treeprocessor 很难“还原每个文件里的重定义”）：

* include 并**不引入新的属性作用域**；被包含文件里的属性赋值等价于在**包含点**写入，从那一点**向后生效，直到被再次修改或 unset**。[discuss.asciidoctor.org+1](https://discuss.asciidoctor.org/Understanding-include-directive-and-file-path-td3137.html?utm_source=chatgpt.com)

* 少数结构（例如**表格单元格**）会形成“嵌套文档”，其中定义的新属性**不会影响父文档**；但这不适用于 include。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoc/latest/tables/format-cell-content/?utm_source=chatgpt.com)


对插件设计的实际建议
----------

1. **不要指望在 Treeprocessor 里按“源位置”拿到属性值**。如果你的渲染依赖某些开关（比如 `:mathjax:`, `:eqnum:` 等），请在相应的 Processor 里**当场读取并固化**：

    * 在 `BlockMacroProcessor` / `BlockProcessor` 里，把 `doc.attr('foo')` 的结果存进你创建的块的属性（例如 `attrs['foo_snapshot']`）。

    * 在 `InlineMacroProcessor` 里同理，把当时的值附着在返回的 inline 节点上。之后即使属性又被改了，你的节点也有自己的“快照”。（内联宏会晚触发，但你依然能在触发时读取当前有效值。）[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoctor/latest/extensions/inline-macro-processor/?utm_source=chatgpt.com)

2. **尽量用元素属性代替全局属性**：比如允许 `[latexmath, numbered]`、`latexmath::[..., opts=...]` 直接控制当前块/宏，减少对全局属性漂移的依赖。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoctorj/latest/extensions/block-processor/?utm_source=chatgpt.com)

3. **确实需要“每个文件各自生效”时**：

    * 约定**在 include 前后成对设置/还原**（作者手动 `:foo: x`…`:!foo:` 或恢复旧值）；

    * 或写一个 **Preprocessor / IncludeProcessor**，在展开 include 时**白名单地保存/恢复**相关属性（把旧值压栈，include 返回后弹栈还原）。这样就不用 Treeprocessor 去“猜历史”。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoc/latest/directives/include/?utm_source=chatgpt.com)


如果你告诉我具体有哪些属性需要“快照”（例如编号策略、渲染后端选择等），我可以把上述策略改成一份最小可用的示例代码给你。

* * *

**You:**

如果还是同样的场景，使用 InlineMacroProcessor, BlockProcessor, BlockMacroProcessor 能很好解决吗？

* * *

**ChatGPT:**

可以的——在“多文件 include 且各处会重设属性”的场景里，用 **InlineMacroProcessor / BlockProcessor / BlockMacroProcessor** 依然能很好地工作，只要你把**需要的属性在各自处理器触发时读取并固化（snapshot）**到生成的节点上即可。原因与做法如下：

### 为什么可行（触发时机 & 属性可见性）

* **BlockMacroProcessor / BlockProcessor**：在**解析阶段**按文档出现顺序触发；也就是说，当它们运行时，前面（含被 include 进来的上文）对属性做的赋值都已经生效，它们读取 `doc.attr(...)` 就能拿到**当时**有效的值。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoctorj/latest/extensions/conversion-process-overview/)

* **InlineMacroProcessor**：在**输出生成阶段**、转换块内容时触发，但仍按文档顺序处理；它在所在段落转换的那一刻读取属性，也能拿到**该位置**的有效值（不会被后文的重新赋值“穿越”影响）。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoctorj/latest/extensions/conversion-process-overview/)

* **include 的属性作用域**：`include::` 不是子文档，不引入新作用域；被包含文件里的属性赋值等同于在**包含点**写入，并从那里向后生效（直到再次修改或 unset）。因此各处理器在触发点读取到的就是你想要的“此处此刻”的属性状态。[Asciidoctor Docs+1](https://docs.asciidoctor.org/asciidoc/latest/directives/include/?utm_source=chatgpt.com)


### 实操要点（如何“固化”以避免后续变化影响）

1. **在处理器中即时读取并附着**

    * 在 `process` 里用 `doc.attr('foo')` 取值，然后写入你创建的 block/inline 节点的属性（如 `attrs['foo_snapshot'] = ...`），或直接体现在生成的 HTML/MathML 里。

    * 这样即使文档后面又改了 `:foo:`，之前生成的节点也不受影响。

2. **控制后续替换（subs）**

    * 对内联：用“内联直通”节点并指定替换策略，例如：

        ```ruby
        create_inline_pass parent, output_html, attributes: { 'subs' => :normal }
        ```

        这样可以精确决定是否再让它跑引号/属性替换等。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoctor/latest/extensions/inline-macro-processor/?utm_source=chatgpt.com)

    * 对块：用 `create_pass_block` 或设置块的 `subs`，防止你插入的标记被二次替换。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoctor/latest/extensions/tree-processor/?utm_source=chatgpt.com)

3. **注意宏与属性的替换顺序**

    * 属性条目（`:name: value`）在赋值前会先做“header substitutions”，所以 `{attr}` 在属性值里会提前被展开；宏（含自定义宏）的内容则在 **macros** 步骤参与替换。设计参数解析时要考虑这一点。[Asciidoctor Docs+1](https://docs.asciidoctor.org/asciidoc/latest/attributes/attribute-entry-substitutions/?utm_source=chatgpt.com)


### 什么时候需要更进一步的手段？

* 如果你想要“每个 include 文件各自成作用域、退出还原”的**强隔离语义**（Asciidoctor 默认没有），就需要自己加一层：

    * 写 **IncludeProcessor** 或 **Preprocessor** 在进入/离开 include 时做属性**压栈/出栈**；或者约定在 include 前后手动 `:attr:` / `:!attr:`。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoctorj/latest/extensions/extensions-introduction/?utm_source=chatgpt.com)

* 另外，**Treeprocessor** 运行在块级解析结束、内联尚未处理之前；它看不到“内联宏已解析后的节点”，也不保留“属性随文演化历史”。不要指望用它来回溯每个位置的属性值，改用上面“在各 processor 处即时固化”的策略。[Asciidoctor Docs](https://docs.asciidoctor.org/asciidoctor/latest/extensions/tree-processor/?utm_source=chatgpt.com)


### 小结

* **结论**：只用 InlineMacroProcessor、BlockProcessor、BlockMacroProcessor 就能覆盖三种语法，并正确响应跨文件的属性重赋值——关键是**在触发点读取并固化属性**。

* **若需 include 级作用域**：再配合 Include/Preprocessor 做属性栈管理即可。[Asciidoctor Docs+1](https://docs.asciidoctor.org/asciidoctorj/latest/extensions/conversion-process-overview/)


需要的话，我可以给你一份最小可用的示例：三类 processor 都把 `:math-engine:`, `:eqnum:` 等配置在触发时读出并固化到节点（含合适的 `subs` 设置），开箱即用。
***** End Conversation

结合我上面提供给你的内容，阅读源代码进行学习，补充 LEARN.md
