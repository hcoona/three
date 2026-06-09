using Markdig.Syntax;

namespace Hcoona.DocumentTranslatorCli;

internal sealed record MarkdownParseResult(
    MarkdownDocument? Document,
    IReadOnlyList<MarkdownDiagnostic> Diagnostics)
{
    public bool Succeeded => Document is not null && Diagnostics.Count == 0;
}
