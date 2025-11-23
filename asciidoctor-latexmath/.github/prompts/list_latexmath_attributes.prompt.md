阅读 #AsciidoctorDiagramAttributes.md 以了解 asciidoctor-diagram 支持的属性及其用法。

由于 asciidoctor-latexmath 需要兼容 latexmath 标准语法，所以可以在块级别添加属性，但是不能改变块内容的语义，例如不能像 asciidoctor-diagram tikz 的 preamble 一样解析内容中 ~~~~ 前面的部分。

根据 asciidoctor-latexmath 使用的外部命令，结合 asciidoctor-diagram 的属性设计，整理出 asciidoctor-latexmath 支持的属性列表，记录在 #AsciidoctorLatexmathAttributes.md 中。

你不得阅读 asciidoctor-latexmath 的任何代码。
