namespace Hcoona.DocumentTranslatorCli;

internal sealed record TextSegmentTranslationRequest(
    int SegmentIndex,
    string Text);
