using Markdig.Syntax;

namespace Hcoona.DocumentTranslatorCli;

internal sealed record MarkdownOutputValidationResult(
    string PatchedText,
    MarkdownSourceMetadata OutputMetadata,
    MarkdownParseResult? PatchedParseResult,
    IReadOnlyList<MarkdownDiagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.Count == 0;
}
