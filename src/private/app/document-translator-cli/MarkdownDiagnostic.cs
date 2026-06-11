namespace Hcoona.DocumentTranslatorCli;

internal sealed record MarkdownDiagnostic(
    MarkdownFailureKind Kind,
    string Message,
    int? Line = null);
