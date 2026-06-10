namespace Hcoona.DocumentTranslatorCli;

internal sealed record MarkdownSourcePatchResult(
    string PatchedText,
    MarkdownSourceMetadata SourceMetadata,
    IReadOnlyList<SourcePatchMap> PatchMaps,
    IReadOnlyList<MarkdownDiagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.Count == 0;
}
