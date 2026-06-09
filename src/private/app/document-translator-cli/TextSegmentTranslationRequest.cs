namespace Hcoona.DocumentTranslatorCli;

internal sealed record TextSegmentTranslationRequest(
    int SegmentIndex,
    string ProtectedText,
    IReadOnlyList<ProtectedToken> ProtectedTokens);
