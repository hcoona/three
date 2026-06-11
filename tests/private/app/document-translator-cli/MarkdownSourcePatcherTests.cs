using Xunit;

namespace Hcoona.DocumentTranslatorCli.Tests;

public sealed class MarkdownSourcePatcherTests
{
    [Fact]
    public void AppliesMultipleReplacementsInDescendingOrderAndReturnsSourceOrderedPatchMap()
    {
        const string source = "One two three four";
        MarkdownTranslationSegment[] segments =
        [
            Segment(0, source, "One"),
            Segment(1, source, "two"),
            Segment(2, source, "four"),
        ];

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            segments,
            ["111111", "2", "4444"]);

        Assert.True(result.Succeeded);
        Assert.Equal("111111 2 three 4444", result.PatchedText);
        Assert.Equal([0, 1, 2], result.PatchMaps.Select(static map => map.SegmentIndex));
        Assert.Equal([0, 4, 14], result.PatchMaps.Select(static map => map.OriginalRange.Start));
        Assert.Equal([0, 7, 15], result.PatchMaps.Select(static map => map.PatchedRange.Start));
        Assert.Equal([3, -2, 0], result.PatchMaps.Select(static map => map.LengthDelta));
    }

    [Fact]
    public void CalculatesPatchedRangesForLongerAndShorterTranslations()
    {
        const string source = "a bb ccc dddd";
        MarkdownTranslationSegment[] segments =
        [
            Segment(0, source, "bb"),
            Segment(1, source, "dddd"),
        ];

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            segments,
            ["B", "LONGER"]);

        Assert.True(result.Succeeded);
        Assert.Equal("a B ccc LONGER", result.PatchedText);
        Assert.Equal(new TextRange(2, 1), result.PatchMaps[0].PatchedRange);
        Assert.Equal(-1, result.PatchMaps[0].LengthDelta);
        Assert.Equal(new TextRange(8, 6), result.PatchMaps[1].PatchedRange);
        Assert.Equal(2, result.PatchMaps[1].LengthDelta);
    }

    [Fact]
    public void HandlesUnsortedSegmentsAndReturnsSourceOrderedPatchMaps()
    {
        const string source = "Alpha beta gamma delta";
        MarkdownTranslationSegment[] segments =
        [
            Segment(2, source, "gamma"),
            Segment(0, source, "Alpha"),
            Segment(3, source, "delta"),
            Segment(1, source, "beta"),
        ];

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            segments,
            ["A", "BETA!!!", "G", "DELTADELTA"]);

        Assert.True(result.Succeeded);
        Assert.Equal("A BETA!!! G DELTADELTA", result.PatchedText);
        Assert.Equal([0, 1, 2, 3], result.PatchMaps.Select(static map => map.SegmentIndex));
        Assert.Equal(
            [0, 6, 11, 17],
            result.PatchMaps.Select(static map => map.OriginalRange.Start));
        Assert.Equal([0, 2, 10, 12], result.PatchMaps.Select(static map => map.PatchedRange.Start));
        Assert.Equal([-4, 3, -4, 5], result.PatchMaps.Select(static map => map.LengthDelta));
    }

    [Fact]
    public void LeavesProtectedSurroundingTextUnchanged()
    {
        const string source = "Keep `code` and [label](https://example.com) end.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();
        MarkdownParseResult parsed = parser.Parse(source);
        Assert.True(parsed.Succeeded);
        MarkdownSegmentExtractionResult extracted = MarkdownSegmentExtractor.Extract(parsed);
        Assert.True(extracted.Succeeded);

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            parsed.SourceText,
            parsed.SourceMetadata,
            extracted.Segments,
            extracted.Segments
                .Select(static segment => segment.OriginalText.ToUpperInvariant())
                .ToArray());

        Assert.True(result.Succeeded);
        Assert.Contains("`code`", result.PatchedText, StringComparison.Ordinal);
        Assert.Contains("https://example.com", result.PatchedText, StringComparison.Ordinal);
        Assert.Contains("[LABEL]", result.PatchedText, StringComparison.Ordinal);
    }

    [Fact]
    public void ZeroSegmentsReturnsOriginalTextMetadataAndEmptyPatchMap()
    {
        MarkdownSourceMetadata metadata = Metadata(hasFinalNewline: true);

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            "```text\ncode\n```\n",
            metadata,
            [],
            []);

        Assert.True(result.Succeeded);
        Assert.Equal("```text\ncode\n```\n", result.PatchedText);
        Assert.Same(metadata, result.SourceMetadata);
        Assert.Empty(result.PatchMaps);
    }

    [Fact]
    public void CountMismatchFailsClosed()
    {
        const string source = "hello";

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            [Segment(0, source, "hello")],
            []);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Empty(result.PatchMaps);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind == MarkdownFailureKind.SourcePatchError);
    }

    [Fact]
    public void SourceRangeOriginalTextMismatchFailsClosed()
    {
        const string source = "hello world";
        MarkdownTranslationSegment segment = new(0, new TextRange(6, 5), "hello");

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            [segment],
            ["monde"]);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Empty(result.PatchMaps);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind == MarkdownFailureKind.SourcePatchError
            && diagnostic.Message.Contains("match", StringComparison.Ordinal)
            && diagnostic.Message.Contains("source range", StringComparison.Ordinal));
    }

    [Fact]
    public void LoneHighSurrogateTranslationFailsClosed()
    {
        const string source = "hello";

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            [Segment(0, source, "hello")],
            ["invalid \ud800 translation"]);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Empty(result.PatchMaps);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind == MarkdownFailureKind.SourcePatchError
            && diagnostic.Message.Contains("segment index 0", StringComparison.Ordinal)
            && diagnostic.Message.Contains(
                "valid Unicode scalar sequence",
                StringComparison.Ordinal));
    }

    [Fact]
    public void NullTranslatedTextFailsClosed()
    {
        const string source = "hello";

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            [Segment(0, source, "hello")],
            [null!]);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Empty(result.PatchMaps);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind == MarkdownFailureKind.SourcePatchError
            && diagnostic.Message.Contains("has no translated text", StringComparison.Ordinal));
    }

    [Fact]
    public void LoneLowSurrogateTranslationFailsClosed()
    {
        const string source = "hello";

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            [Segment(0, source, "hello")],
            ["invalid \udc00 translation"]);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Empty(result.PatchMaps);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind == MarkdownFailureKind.SourcePatchError
            && diagnostic.Message.Contains("segment index 0", StringComparison.Ordinal)
            && diagnostic.Message.Contains(
                "valid Unicode scalar sequence",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ValidEmojiTranslationSucceeds()
    {
        const string source = "hello";

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            [Segment(0, source, "hello")],
            ["hello 😀"]);

        Assert.True(result.Succeeded);
        Assert.Equal("hello 😀", result.PatchedText);
        Assert.Single(result.PatchMaps);
    }

    [Fact]
    public void CalculatesPatchMapsWithUtf16CodeUnitOffsetsAfterEmojiReplacement()
    {
        const string source = "a b";
        const string emoji = "😀";
        MarkdownTranslationSegment[] segments =
        [
            Segment(0, source, "a"),
            Segment(1, source, "b"),
        ];

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            segments,
            [emoji, "BC"]);

        Assert.Equal(2, emoji.Length);
        Assert.True(result.Succeeded);
        Assert.Equal("😀 BC", result.PatchedText);
        Assert.Equal(new TextRange(0, 2), result.PatchMaps[0].PatchedRange);
        Assert.Equal(1, result.PatchMaps[0].LengthDelta);
        Assert.Equal(new TextRange(3, 2), result.PatchMaps[1].PatchedRange);
        Assert.Equal(1, result.PatchMaps[1].LengthDelta);
    }

    [Fact]
    public void FailedParseWithZeroSegmentsFailsClosedAndPropagatesDiagnostics()
    {
        const string source = "```text\nunterminated";
        MarkdownSourceMetadata metadata = Metadata();
        MarkdownDiagnostic diagnostic = new(
            MarkdownFailureKind.UnsupportedSyntax,
            "The Markdown source could not be parsed.");
        MarkdownParseResult parseResult = new(
            null,
            [diagnostic],
            source,
            metadata,
            [],
            []);

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(parseResult, [], []);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Same(metadata, result.SourceMetadata);
        Assert.Empty(result.PatchMaps);
        Assert.Same(parseResult.Diagnostics, result.Diagnostics);
    }

    [Fact]
    public void FailedParseWithEmptyDiagnosticsFailsClosedWithSourcePatchError()
    {
        const string source = "```text\nunterminated";
        MarkdownSourceMetadata metadata = Metadata();
        MarkdownParseResult parseResult = new(
            null,
            [],
            source,
            metadata,
            [],
            []);

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(parseResult, [], []);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Same(metadata, result.SourceMetadata);
        Assert.Empty(result.PatchMaps);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.SourcePatchError, diagnostic.Kind);
    }

    [Fact]
    public void NegativeSegmentIndexFailsClosedWithDiagnostic()
    {
        const string source = "hello";
        MarkdownTranslationSegment segment = Segment(-1, source, "hello");

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            [segment],
            ["bonjour"]);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Empty(result.PatchMaps);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains(
                "outside the valid translation result range",
                StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateSegmentIndexesFailClosedWithDiagnostic()
    {
        const string source = "hello world";
        MarkdownTranslationSegment[] segments =
        [
            Segment(0, source, "hello"),
            Segment(0, source, "world"),
        ];

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            segments,
            ["bonjour", "monde"]);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Empty(result.PatchMaps);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("is duplicated", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingSegmentIndexFailsClosedWithDiagnostic()
    {
        const string source = "hello";
        MarkdownTranslationSegment segment = Segment(1, source, "hello");

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            [segment],
            ["bonjour"]);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Empty(result.PatchMaps);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("index 0 is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void OverlappingSegmentsFailClosed()
    {
        const string source = "abcdef";
        MarkdownTranslationSegment[] segments =
        [
            new(0, new TextRange(1, 3), "bcd"),
            new(1, new TextRange(3, 2), "de"),
        ];

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            segments,
            ["BCD", "DE"]);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Empty(result.PatchMaps);
    }

    [Fact]
    public void OutOfRangeSegmentFailsClosed()
    {
        const string source = "abc";
        MarkdownTranslationSegment segment = new(0, new TextRange(4, 1), "x");

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            [segment],
            ["y"]);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Empty(result.PatchMaps);
    }

    [Fact]
    public void ZeroLengthSegmentRangeFailsClosed()
    {
        const string source = "abc";
        MarkdownTranslationSegment segment = new(0, new TextRange(1, 0), string.Empty);

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            [segment],
            ["x"]);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Empty(result.PatchMaps);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind == MarkdownFailureKind.SourcePatchError
            && diagnostic.Message.Contains("invalid source range", StringComparison.Ordinal));
    }

    [Fact]
    public void SegmentRangeSplittingEmojiSurrogatePairFailsClosed()
    {
        const string source = "a 😀 b";
        int emojiStart = source.IndexOf("😀", StringComparison.Ordinal);
        MarkdownTranslationSegment segment = new(
            0,
            new TextRange(emojiStart, 1),
            source.Substring(emojiStart, 1));

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            [segment],
            ["x"]);

        Assert.False(result.Succeeded);
        Assert.Equal(source, result.PatchedText);
        Assert.Empty(result.PatchMaps);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind == MarkdownFailureKind.SourcePatchError
            && diagnostic.Message.Contains("invalid source range", StringComparison.Ordinal));
    }

    [Fact]
    public void MachineLookingProseHasNoSpecialHandling()
    {
        const string source =
            "Use ${name}, {{name}}, {0}, package.name, ./path/file.txt, and --flag.";

        MarkdownSourcePatchResult result = MarkdownSourcePatcher.Patch(
            source,
            Metadata(),
            [Segment(0, source, source)],
            ["Keep ${name}, {{name}}, {0}, package.name, ./path/file.txt, and --flag."]);

        Assert.True(result.Succeeded);
        Assert.Equal(
            "Keep ${name}, {{name}}, {0}, package.name, ./path/file.txt, and --flag.",
            result.PatchedText);
        Assert.Single(result.PatchMaps);
    }

    private static MarkdownTranslationSegment Segment(
        int segmentIndex,
        string source,
        string text) =>
        new(
            segmentIndex,
            new TextRange(source.IndexOf(text, StringComparison.Ordinal), text.Length),
            text);

    private static MarkdownSourceMetadata Metadata(bool hasFinalNewline = false) =>
        new(false, hasFinalNewline, []);
}
