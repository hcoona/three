using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Hcoona.DocumentTranslatorCli;

internal static class MarkdownSegmentExtractor
{
    private const int SegmentUnicodeScalarLimit = 50_000;

    public static MarkdownSegmentExtractionResult Extract(MarkdownParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        if (!parseResult.Succeeded || parseResult.Document is null)
        {
            return new MarkdownSegmentExtractionResult([], [], parseResult.Diagnostics);
        }

        List<MarkdownDiagnostic> diagnostics = [];
        List<MarkdownTranslationSegment> segments = [];
        ProtectedRangeCursor protectedRanges = new(parseResult.ProtectedSlices);
        HashSet<LiteralInline> approvedTextNodes = CollectApprovedTextNodes(parseResult.Document);
        HashSet<LiteralInline> tableCellTextNodes = CollectTableCellTextNodes(parseResult.Document);

        foreach (LiteralInline literalInline in parseResult.Document
            .Descendants()
            .OfType<LiteralInline>()
            .OrderBy(static inline => inline.Span.Start))
        {
            if (!IsApprovedTextNode(literalInline, approvedTextNodes))
            {
                continue;
            }

            if (!TryCreateTextRange(
                literalInline.Span,
                parseResult.SourceText,
                out TextRange literalRange))
            {
                diagnostics.Add(CreateUnreliableTextSpanDiagnostic(literalInline.Span));
                continue;
            }

            foreach (TextRange rawSegmentRange in protectedRanges.GetUnprotectedRanges(
                literalRange))
            {
                TextRange segmentRange = tableCellTextNodes.Contains(literalInline)
                    ? TrimTableCellPadding(parseResult.SourceText, rawSegmentRange)
                    : rawSegmentRange;
                if (segmentRange.Length == 0)
                {
                    continue;
                }

                string text = parseResult.SourceText.Substring(
                    segmentRange.Start,
                    segmentRange.Length);
                if (MarkdownTextMetrics.CountUnicodeScalarValues(text) > SegmentUnicodeScalarLimit)
                {
                    diagnostics.Add(new MarkdownDiagnostic(
                        MarkdownFailureKind.SegmentSizeViolation,
                        "A Markdown translation segment exceeds 50,000 Unicode scalar values."));
                    continue;
                }

                segments.Add(new MarkdownTranslationSegment(
                    segments.Count,
                    segmentRange,
                    text));
            }
        }

        if (diagnostics.Count > 0)
        {
            return new MarkdownSegmentExtractionResult([], [], diagnostics);
        }

        MarkdownTranslationSegment[] orderedSegments = segments
            .OrderBy(static segment => segment.SourceRange.Start)
            .ThenBy(static segment => segment.SourceRange.Length)
            .Select(static (segment, index) => segment with { SegmentIndex = index })
            .ToArray();

        TextSegmentTranslationRequest[] requests = orderedSegments
            .Select(static segment => new TextSegmentTranslationRequest(
                segment.SegmentIndex,
                segment.OriginalText))
            .ToArray();

        return new MarkdownSegmentExtractionResult(orderedSegments, requests, []);
    }

    private static TextRange TrimTableCellPadding(string sourceText, TextRange range)
    {
        int start = range.Start;
        int end = range.End;
        while (start < end && sourceText[start] is ' ' or '\t')
        {
            start++;
        }

        while (end > start && sourceText[end - 1] is ' ' or '\t')
        {
            end--;
        }

        return new TextRange(start, end - start);
    }

    private static bool IsApprovedTextNode(
        LiteralInline literalInline,
        HashSet<LiteralInline> approvedTextNodes) =>
        approvedTextNodes.Contains(literalInline)
        || GetParentBlock(literalInline) is HeadingBlock or ParagraphBlock;

    private static HashSet<LiteralInline> CollectApprovedTextNodes(MarkdownDocument document)
    {
        HashSet<LiteralInline> approvedTextNodes = [];
        foreach (MarkdownObject container in document.Descendants()
            .Where(static descendant => descendant is HeadingBlock or ParagraphBlock or TableCell))
        {
            foreach (LiteralInline literalInline in container.Descendants().OfType<LiteralInline>())
            {
                approvedTextNodes.Add(literalInline);
            }
        }

        return approvedTextNodes;
    }

    private static HashSet<LiteralInline> CollectTableCellTextNodes(MarkdownDocument document)
    {
        HashSet<LiteralInline> tableCellTextNodes = [];
        foreach (TableCell tableCell in document.Descendants().OfType<TableCell>())
        {
            foreach (LiteralInline literalInline in tableCell.Descendants().OfType<LiteralInline>())
            {
                tableCellTextNodes.Add(literalInline);
            }
        }

        return tableCellTextNodes;
    }

    private static LeafBlock? GetParentBlock(Inline inline)
    {
        for (Inline? current = inline; current is not null; current = current.Parent as Inline)
        {
            if (current is ContainerInline { ParentBlock: LeafBlock leafBlock }
                && current.Parent is null)
            {
                return leafBlock;
            }
        }

        return null;
    }

    private static bool TryCreateTextRange(SourceSpan span, string sourceText, out TextRange range)
    {
        range = default;
        if (span.Start < 0 || span.End < span.Start)
        {
            return false;
        }

        range = new TextRange(span.Start, checked(span.End - span.Start + 1));
        return range.IsOnUnicodeScalarBoundaries(sourceText);
    }

    private static MarkdownDiagnostic CreateUnreliableTextSpanDiagnostic(SourceSpan span) =>
        new(
            MarkdownFailureKind.UnreliableSourceSpan,
            $"Required Markdown text node has no reliable source span ({span.Start}..{span.End}).");

    private static bool RangesOverlap(TextRange left, TextRange right) =>
        left.Start < right.End && right.Start < left.End;

    private sealed class ProtectedRangeCursor
    {
        private readonly TextRange[] ranges;

        public ProtectedRangeCursor(IReadOnlyList<ProtectedSlice> protectedSlices)
        {
            ArgumentNullException.ThrowIfNull(protectedSlices);
            ranges = protectedSlices
                .Select(static slice => slice.SourceRange)
                .OrderBy(static range => range.Start)
                .ThenBy(static range => range.Length)
                .ToArray();
        }

        public IEnumerable<TextRange> GetUnprotectedRanges(TextRange candidateRange)
        {
            int currentStart = candidateRange.Start;
            foreach (TextRange protectedRange in ranges)
            {
                if (!RangesOverlap(candidateRange, protectedRange))
                {
                    if (protectedRange.Start >= candidateRange.End)
                    {
                        break;
                    }

                    continue;
                }

                if (protectedRange.Start > currentStart)
                {
                    yield return new TextRange(currentStart, protectedRange.Start - currentStart);
                }

                currentStart = Math.Max(currentStart, protectedRange.End);
                if (currentStart >= candidateRange.End)
                {
                    yield break;
                }
            }

            yield return new TextRange(currentStart, candidateRange.End - currentStart);
        }
    }
}

internal sealed record MarkdownSegmentExtractionResult(
    IReadOnlyList<MarkdownTranslationSegment> Segments,
    IReadOnlyList<TextSegmentTranslationRequest> TranslationRequests,
    IReadOnlyList<MarkdownDiagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.Count == 0;
}
