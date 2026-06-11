namespace Hcoona.DocumentTranslatorCli;

internal static class MarkdownSourcePatcher
{
    public static MarkdownSourcePatchResult Patch(
        MarkdownParseResult parseResult,
        IReadOnlyList<MarkdownTranslationSegment> segments,
        IReadOnlyList<string> translatedTexts)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        if (!parseResult.Succeeded)
        {
            IReadOnlyList<MarkdownDiagnostic> diagnostics = parseResult.Diagnostics.Count > 0
                ? parseResult.Diagnostics
                : [new MarkdownDiagnostic(
                    MarkdownFailureKind.SourcePatchError,
                    "The Markdown parse result failed without diagnostics.")];

            return new MarkdownSourcePatchResult(
                parseResult.SourceText,
                parseResult.SourceMetadata,
                [],
                diagnostics);
        }

        return Patch(
            parseResult.SourceText,
            parseResult.SourceMetadata,
            segments,
            translatedTexts);
    }

    public static MarkdownSourcePatchResult Patch(
        string sourceText,
        IReadOnlyList<MarkdownTranslationSegment> segments,
        IReadOnlyList<string> translatedTexts)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        return Patch(
            sourceText,
            new MarkdownSourceMetadata(false, HasFinalNewline(sourceText), []),
            segments,
            translatedTexts);
    }

    public static MarkdownSourcePatchResult Patch(
        string sourceText,
        MarkdownSourceMetadata sourceMetadata,
        IReadOnlyList<MarkdownTranslationSegment> segments,
        IReadOnlyList<string> translatedTexts)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(sourceMetadata);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(translatedTexts);

        List<MarkdownDiagnostic> diagnostics = [];
        if (segments.Count != translatedTexts.Count)
        {
            diagnostics.Add(new MarkdownDiagnostic(
                MarkdownFailureKind.SourcePatchError,
                "The translated text count must match the extracted Markdown segment count."));
        }

        ValidateTranslatedTexts(translatedTexts, diagnostics);
        ValidateSegments(sourceText, segments, diagnostics);
        if (diagnostics.Count > 0)
        {
            return new MarkdownSourcePatchResult(sourceText, sourceMetadata, [], diagnostics);
        }

        if (segments.Count == 0)
        {
            return new MarkdownSourcePatchResult(sourceText, sourceMetadata, [], []);
        }

        MarkdownTranslationSegment[] sourceOrderedSegments = segments
            .OrderBy(static segment => segment.SourceRange.Start)
            .ThenBy(static segment => segment.SourceRange.Length)
            .ToArray();
        SourcePatchMap[] patchMaps = CreatePatchMaps(sourceOrderedSegments, translatedTexts);

        string patchedText = sourceText;
        foreach (MarkdownTranslationSegment segment in sourceOrderedSegments
            .OrderByDescending(static segment => segment.SourceRange.Start)
            .ThenByDescending(static segment => segment.SourceRange.Length))
        {
            patchedText = string.Concat(
                patchedText.AsSpan(0, segment.SourceRange.Start),
                translatedTexts[segment.SegmentIndex],
                patchedText.AsSpan(segment.SourceRange.End));
        }

        return new MarkdownSourcePatchResult(patchedText, sourceMetadata, patchMaps, []);
    }

    private static void ValidateTranslatedTexts(
        IReadOnlyList<string> translatedTexts,
        List<MarkdownDiagnostic> diagnostics)
    {
        for (int i = 0; i < translatedTexts.Count; i++)
        {
            string? translatedText = translatedTexts[i];
            if (translatedText is null)
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownFailureKind.SourcePatchError,
                    $"Markdown segment {i} has no translated text."));
                continue;
            }

            if (!MarkdownTextMetrics.IsValidUnicodeScalarSequence(translatedText))
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownFailureKind.SourcePatchError,
                    $"Markdown translation for segment index {i} is not a valid Unicode scalar "
                    + "sequence."));
            }
        }
    }

    private static void ValidateSegments(
        string sourceText,
        IReadOnlyList<MarkdownTranslationSegment> segments,
        List<MarkdownDiagnostic> diagnostics)
    {
        bool[] seenIndexes = new bool[segments.Count];
        MarkdownTranslationSegment[] sourceOrderedSegments = segments
            .OrderBy(static segment => segment.SourceRange.Start)
            .ThenBy(static segment => segment.SourceRange.Length)
            .ToArray();

        for (int i = 0; i < segments.Count; i++)
        {
            MarkdownTranslationSegment segment = segments[i];
            if (segment.SegmentIndex < 0 || segment.SegmentIndex >= segments.Count)
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownFailureKind.SourcePatchError,
                    $"Markdown segment index {segment.SegmentIndex} is outside the valid "
                    + "translation result range."));
                continue;
            }

            if (seenIndexes[segment.SegmentIndex])
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownFailureKind.SourcePatchError,
                    $"Markdown segment index {segment.SegmentIndex} is duplicated."));
            }

            seenIndexes[segment.SegmentIndex] = true;
            if (segment.SourceRange.Length == 0
                || !segment.SourceRange.IsOnUnicodeScalarBoundaries(sourceText))
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownFailureKind.SourcePatchError,
                    $"Markdown segment {segment.SegmentIndex} has an invalid source range."));
                continue;
            }

            string sourceSlice = sourceText.Substring(
                segment.SourceRange.Start,
                segment.SourceRange.Length);
            if (!string.Equals(sourceSlice, segment.OriginalText, StringComparison.Ordinal))
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownFailureKind.SourcePatchError,
                    $"Markdown segment {segment.SegmentIndex} does not match its source range."));
            }
        }

        for (int i = 0; i < seenIndexes.Length; i++)
        {
            if (!seenIndexes[i])
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownFailureKind.SourcePatchError,
                    $"Markdown segment index {i} is missing."));
            }
        }

        for (int i = 1; i < sourceOrderedSegments.Length; i++)
        {
            MarkdownTranslationSegment previous = sourceOrderedSegments[i - 1];
            MarkdownTranslationSegment current = sourceOrderedSegments[i];
            if (TryRangesOverlap(previous.SourceRange, current.SourceRange, out bool rangesOverlap)
                && rangesOverlap)
            {
                diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownFailureKind.SourcePatchError,
                    $"Markdown segments {previous.SegmentIndex} and {current.SegmentIndex} "
                    + "overlap."));
            }
        }
    }

    private static SourcePatchMap[] CreatePatchMaps(
        MarkdownTranslationSegment[] sourceOrderedSegments,
        IReadOnlyList<string> translatedTexts)
    {
        SourcePatchMap[] patchMaps = new SourcePatchMap[sourceOrderedSegments.Length];
        int cumulativeDelta = 0;
        for (int i = 0; i < sourceOrderedSegments.Length; i++)
        {
            MarkdownTranslationSegment segment = sourceOrderedSegments[i];
            string translatedText = translatedTexts[segment.SegmentIndex];
            int patchedStart = checked(segment.SourceRange.Start + cumulativeDelta);
            int lengthDelta = checked(translatedText.Length - segment.SourceRange.Length);
            patchMaps[i] = new SourcePatchMap(
                segment.SegmentIndex,
                segment.SourceRange,
                new TextRange(patchedStart, translatedText.Length),
                lengthDelta);
            cumulativeDelta = checked(cumulativeDelta + lengthDelta);
        }

        return patchMaps;
    }

    private static bool TryRangesOverlap(TextRange left, TextRange right, out bool rangesOverlap)
    {
        rangesOverlap = false;
        if (!TryGetEnd(left, out int leftEnd) || !TryGetEnd(right, out int rightEnd))
        {
            return false;
        }

        rangesOverlap = left.Start < rightEnd && right.Start < leftEnd;
        return true;
    }

    private static bool TryGetEnd(TextRange range, out int end)
    {
        try
        {
            end = range.End;
            return true;
        }
        catch (OverflowException)
        {
            end = 0;
            return false;
        }
    }

    private static bool HasFinalNewline(string sourceText) =>
        sourceText.EndsWith('\n') || sourceText.EndsWith('\r');
}
