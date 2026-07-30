namespace Hcoona.DocumentTranslatorCli;

internal sealed record MarkdownLineEnding(
    TextRange SourceRange,
    string Text);
