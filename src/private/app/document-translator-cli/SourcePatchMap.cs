namespace Hcoona.DocumentTranslatorCli;

internal sealed record SourcePatchMap(
    int SegmentIndex,
    TextRange OriginalRange,
    TextRange PatchedRange,
    int LengthDelta);
