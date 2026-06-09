namespace Hcoona.DocumentTranslatorCli;

internal sealed record MarkdownTranslationSegment(
    int SegmentIndex,
    TextRange SourceRange,
    string OriginalText,
    string ProtectedText,
    IReadOnlyList<ProtectedToken> ProtectedTokens);
