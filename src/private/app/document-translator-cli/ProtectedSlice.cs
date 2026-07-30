namespace Hcoona.DocumentTranslatorCli;

internal sealed record ProtectedSlice(
    string SliceId,
    string Kind,
    TextRange SourceRange,
    string OriginalText);
