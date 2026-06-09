namespace Hcoona.DocumentTranslatorCli;

internal sealed record ProtectedToken(
    TextRange SourceRange,
    string Placeholder,
    string OriginalText);
