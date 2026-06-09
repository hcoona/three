using Markdig.Syntax;

namespace Hcoona.DocumentTranslatorCli;

internal sealed record MarkdownParseResult(
    MarkdownDocument? Document,
    IReadOnlyList<MarkdownDiagnostic> Diagnostics,
    string SourceText,
    MarkdownSourceMetadata SourceMetadata,
    IReadOnlyList<ProtectedSlice> ProtectedSlices,
    IReadOnlyList<ProtectedSlice> DetectorExclusionSlices)
{
    public bool Succeeded => Document is not null && Diagnostics.Count == 0;
}
