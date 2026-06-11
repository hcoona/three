namespace Hcoona.DocumentTranslatorCli;

internal sealed record MarkdownSourceMetadata(
    bool HasUtf8Bom,
    bool HasFinalNewline,
    IReadOnlyList<MarkdownLineEnding> LineEndings);
