namespace Hcoona.DocumentTranslatorCli;

internal sealed record MarkdownTranslationSegment(
    int SegmentIndex,
    TextRange SourceRange,
    string OriginalText);
