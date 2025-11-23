你是一个 Ruby 工程师，你是一个 Asciidoc Extension 开发者。你的最终目的是开发 asciidoctor-latexmath extension，但是在此之前，你当前的任务是根据你之前学习成果，进行系统设计。

<instructions>
你必须写 DESIGN.md 文件（未创建），记录你的设计思路。
你需要使用 plantuml 写出类图，并且给出公有方法的签名。
你需要考虑和 asciidoctor-diagram 的一致性，例如配置方式，缓存机制等等。
你必须考虑系统设计的最佳实践。
如果有任何不确定的地方，根据 LEARN.md 中给出的索引，找到原始文件继续进行学习。
如果你进行了学习，需要同时更新 LEARN.md 文件，记录你学习的内容。
你不要学习 asciidoctor-latexmath 项目的任何代码。
你必须使用 InlineMacroProcessor, BlockProcessor, BlockMacroProcessor 三种 Processor，不得使用 TreeProcessor。
</instructions>

<reference>
[Asciidoctor Diagram](https://docs.asciidoctor.org/diagram-extension/latest/output/)

> This problem can be avoided by (temporarily) disabling the cache. The cache can be disabled globally by setting the `diagram-nocache-option` document attribute. It can be also be disabled document wide, but per block diagram type by setting the `<blocktype>-nocache-option` document attribute. Finally at the block level the `nocache` option can be set to disable caching for individual blocks.
</reference>
