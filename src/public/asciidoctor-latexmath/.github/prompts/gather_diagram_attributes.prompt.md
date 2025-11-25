阅读 Asciidoctor-diagram 源代码，整理所有控制开关（一般是通过 Asciidoc 属性控制），并总结到 asciidoctor-latexmath 下的 AsciidoctorDiagramAttributes.md 文件（未创建）中。

你需要找到所有公共开关。你需要找到 tikz 的所有私有开关。你不得列出其他 diagram types 的开关。你需要注明开关是文档级别的还是块级别的，还是两个都可以。

你要整理的文档应该包括

呈现给最终用户的视角
1. 文档级别 attribute 还是块级别 attribute
2. 如果是块级别 attribute，还需要进一步看 positional, named, options, etc.

参考
https://docs.asciidoctor.org/asciidoc/latest/attributes/document-attributes/
https://docs.asciidoctor.org/asciidoc/latest/attributes/element-attributes/
https://docs.asciidoctor.org/asciidoc/latest/attributes/positional-and-named-attributes/
https://docs.asciidoctor.org/asciidoc/latest/attributes/options/
