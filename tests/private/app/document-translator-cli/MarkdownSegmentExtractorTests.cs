using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Xunit;

namespace Hcoona.DocumentTranslatorCli.Tests;

public sealed class MarkdownSegmentExtractorTests
{
    [Fact]
    public void ExtractsApprovedTextNodesInDocumentOrder()
    {
        const string markdown = """
            # Heading text

            Paragraph with [link text](https://example.com) and ![alt text](image.png).

            - Item text

            > Quote text

            | Left | Right |
            | ---- | ----- |
            | Cell one | **Cell two** |
            """;

        MarkdownSegmentExtractionResult result = Extract(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Segments, segment => segment.OriginalText == "Heading text");
        Assert.Contains(result.Segments, segment => segment.OriginalText == "Paragraph with ");
        Assert.Contains(result.Segments, segment => segment.OriginalText == "link text");
        Assert.Contains(result.Segments, segment => segment.OriginalText == "alt text");
        Assert.Contains(result.Segments, segment => segment.OriginalText == "Item text");
        Assert.Contains(result.Segments, segment => segment.OriginalText == "Quote text");
        Assert.Contains(result.Segments, segment => segment.OriginalText == "Cell one");
        Assert.Contains(result.Segments, segment => segment.OriginalText == "Cell two");
        Assert.Equal(
            result.Segments.Select(segment => segment.OriginalText),
            result.TranslationRequests.Select(request => request.Text));
        Assert.Equal(
            Enumerable.Range(0, result.Segments.Count),
            result.Segments.Select(segment => segment.SegmentIndex));
        Assert.Equal(
            result.Segments.Select(segment => segment.SegmentIndex),
            result.TranslationRequests.Select(request => request.SegmentIndex));
        Assert.True(result.Segments.Zip(result.Segments.Skip(1))
            .All(pair => pair.First.SourceRange.Start <= pair.Second.SourceRange.Start));
    }

    [Fact]
    public void ExcludesProtectedSlicesButNotValidationBoundarySlices()
    {
        const string markdown = """
            Visible `code` [label](https://example.com "title") <https://example.net>

            ```text
            fenced code
            ```
            """;
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();
        MarkdownParseResult parsed = parser.Parse(markdown);
        Assert.True(parsed.Succeeded);

        MarkdownSegmentExtractionResult result = MarkdownSegmentExtractor.Extract(parsed);

        Assert.True(result.Succeeded);
        string combinedText = string.Concat(
            result.Segments.Select(segment => segment.OriginalText));
        Assert.Contains("Visible ", combinedText, StringComparison.Ordinal);
        Assert.Contains("label", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("code", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("https://example.com", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("title", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("https://example.net", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("fenced code", combinedText, StringComparison.Ordinal);

        MarkdownParseResult validationBoundaryOnly = parsed with
        {
            ProtectedSlices = [],
            ValidationBoundarySlices =
            [
                new ProtectedSlice(
                    "validation-only-0",
                    MarkdownProtectedRangeKinds.FencedCodeBlock,
                    new TextRange(0, markdown.Length),
                    markdown),
            ],
        };
        MarkdownSegmentExtractionResult validationBoundaryResult =
            MarkdownSegmentExtractor.Extract(validationBoundaryOnly);

        Assert.True(validationBoundaryResult.Succeeded);
        Assert.NotEmpty(validationBoundaryResult.Segments);
    }

    [Theory]
    [InlineData("Visit https://example.com now.", "Visit ", " now.")]
    [InlineData("Contact user@example.com.", "Contact ", ".")]
    [InlineData("Before <span>raw text</span> after.", "Before ", " after.")]
    public void ExcludesProtectedSlicesInsideApprovedLiteralText(
        string markdown,
        params string[] expectedSegments)
    {
        MarkdownSegmentExtractionResult result = Extract(markdown);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedSegments, result.Segments.Select(segment => segment.OriginalText));
        Assert.Equal(
            expectedSegments,
            result.TranslationRequests.Select(request => request.Text));
    }

    [Fact]
    public void StartsTaskListTextAfterCheckboxSeparator()
    {
        const string markdown = """
            - [x] Done task
            - [ ]	Open task
            """;

        MarkdownSegmentExtractionResult result = Extract(markdown);

        Assert.True(result.Succeeded);
        Assert.Equal(
            ["Done task", "Open task"],
            result.Segments.Select(static segment => segment.OriginalText));
        Assert.Equal(
            result.Segments.Select(static segment => segment.OriginalText),
            result.TranslationRequests.Select(static request => request.Text));
    }

    [Fact]
    public void ExcludesShortcutAndCollapsedReferenceLabels()
    {
        const string markdown = """
            [id]

            [id][]

            ![img]

            ![img][]

            [id]: https://example.com
            [img]: image.png
            """;

        MarkdownSegmentExtractionResult result = Extract(markdown);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Segments);
        Assert.Empty(result.TranslationRequests);
    }

    [Fact]
    public void KeepsFullReferenceLinkVisibleTextTranslatable()
    {
        const string markdown = """
            [visible text][id]

            ![visible alt][img]

            [id]: https://example.com
            [img]: image.png
            """;

        MarkdownSegmentExtractionResult result = Extract(markdown);

        Assert.True(result.Succeeded);
        Assert.Equal(
            ["visible text", "visible alt"],
            result.Segments.Select(static segment => segment.OriginalText));
    }

    [Fact]
    public void KeepsFullReferenceVisibleTextTranslatableBeforeLiteralEmptyBrackets()
    {
        const string markdown = """
            [visible text][id][]

            ![visible alt][img][]

            [id]: https://example.com
            [img]: image.png
            """;

        MarkdownSegmentExtractionResult result = Extract(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Segments, static segment => segment.OriginalText == "visible text");
        Assert.Contains(result.Segments, static segment => segment.OriginalText == "visible alt");
    }

    [Fact]
    public void UsesDecodedSourceTextOffsetsInSourceOrder()
    {
        const string markdown = "é First\n\nSecond 😀 text\n";

        MarkdownSegmentExtractionResult result = Extract(markdown);

        Assert.True(result.Succeeded);
        MarkdownTranslationSegment first = Assert.Single(
            result.Segments,
            segment => segment.OriginalText == "é First");
        MarkdownTranslationSegment second = Assert.Single(
            result.Segments,
            segment => segment.OriginalText == "Second 😀 text");
        Assert.Equal(
            markdown.IndexOf("é First", StringComparison.Ordinal),
            first.SourceRange.Start);
        Assert.Equal(
            markdown.IndexOf("Second 😀 text", StringComparison.Ordinal),
            second.SourceRange.Start);
        Assert.True(first.SourceRange.IsOnUnicodeScalarBoundaries(markdown));
        Assert.True(second.SourceRange.IsOnUnicodeScalarBoundaries(markdown));
        Assert.True(first.SourceRange.Start < second.SourceRange.Start);
    }

    [Fact]
    public void FailsClosedWhenApprovedTextNodeHasNoReliableSourceSpan()
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();
        MarkdownParseResult parsed = parser.Parse("# Heading");
        Assert.True(parsed.Succeeded);
        LiteralInline literal = parsed.Document!.Descendants().OfType<LiteralInline>().Single();
        literal.Span = new SourceSpan(-1, -1);

        MarkdownSegmentExtractionResult result = MarkdownSegmentExtractor.Extract(parsed);

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.UnreliableSourceSpan, diagnostic.Kind);
        Assert.Empty(result.Segments);
        Assert.Empty(result.TranslationRequests);
    }

    [Fact]
    public void AllowsSegmentAtUnicodeScalarLimit()
    {
        string markdown = new('a', 50_000);

        MarkdownSegmentExtractionResult result = Extract(markdown);

        Assert.True(result.Succeeded);
        MarkdownTranslationSegment segment = Assert.Single(result.Segments);
        Assert.Equal(markdown, segment.OriginalText);
        Assert.Equal(50_000, MarkdownTextMetrics.CountUnicodeScalarValues(segment.OriginalText));
    }

    [Fact]
    public void AllowsEmojiHeavySegmentAtUnicodeScalarLimit()
    {
        string markdown = string.Concat(Enumerable.Repeat("😀", 50_000));

        MarkdownSegmentExtractionResult result = Extract(markdown);

        Assert.True(result.Succeeded);
        Assert.True(markdown.Length > MarkdownTextMetrics.CountUnicodeScalarValues(markdown));
        MarkdownTranslationSegment segment = Assert.Single(result.Segments);
        Assert.Equal(markdown, segment.OriginalText);
        Assert.Equal(50_000, MarkdownTextMetrics.CountUnicodeScalarValues(segment.OriginalText));
    }

    [Fact]
    public void EnforcesSegmentUnicodeScalarLimit()
    {
        string markdown = new('a', 50_001);

        MarkdownSegmentExtractionResult result = Extract(markdown);

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.SegmentSizeViolation, diagnostic.Kind);
    }

    [Fact]
    public void EnforcesEmojiHeavySegmentUnicodeScalarLimit()
    {
        string markdown = string.Concat(Enumerable.Repeat("😀", 50_001));

        MarkdownSegmentExtractionResult result = Extract(markdown);

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.SegmentSizeViolation, diagnostic.Kind);
    }

    [Fact]
    public void ProtectedOnlyMarkdownReturnsZeroSegments()
    {
        const string markdown = """
            ---
            title: Protected
            ---

            ```text
            code
            ```

            <div>html</div>
            """;

        MarkdownSegmentExtractionResult result = Extract(markdown);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Segments);
        Assert.Empty(result.TranslationRequests);
    }

    [Fact]
    public void MachineLookingProseRemainsInSegmentText()
    {
        const string markdown =
            "Use ${name}, {{name}}, {0}, package.name, ./path/file.txt, and --flag.";

        MarkdownSegmentExtractionResult result = Extract(markdown);

        Assert.True(result.Succeeded);
        string combinedText = string.Concat(
            result.Segments.Select(segment => segment.OriginalText));
        Assert.Contains("${name}", combinedText, StringComparison.Ordinal);
        Assert.Contains("{{name}}", combinedText, StringComparison.Ordinal);
        Assert.Contains("{0}", combinedText, StringComparison.Ordinal);
        Assert.Contains("package.name", combinedText, StringComparison.Ordinal);
        Assert.Contains("./path/file.txt", combinedText, StringComparison.Ordinal);
        Assert.Contains("--flag", combinedText, StringComparison.Ordinal);
    }

    private static MarkdownSegmentExtractionResult Extract(string markdown)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();
        MarkdownParseResult parsed = parser.Parse(markdown);
        Assert.True(parsed.Succeeded);
        return MarkdownSegmentExtractor.Extract(parsed);
    }
}
