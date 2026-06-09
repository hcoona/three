using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Xunit;

namespace Hcoona.DocumentTranslatorCli.Tests;

public sealed class MarkdownCoreModelTests
{
    [Fact]
    public void TextRangeUsesDecodedStringOffsetsAndRejectsNegativeValues()
    {
        TextRange range = new(start: 3, length: 5);

        Assert.Equal(3, range.Start);
        Assert.Equal(5, range.Length);
        Assert.Equal(8, range.End);
        Assert.True(range.IsWithin("0123456789"));
        Assert.True(range.IsOnUnicodeScalarBoundaries("0123456789"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextRange(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextRange(0, -1));
    }

    [Fact]
    public void TextRangeDetectsSurrogatePairBoundarySplits()
    {
        const string text = "a😀b";

        Assert.True(new TextRange(1, 2).IsOnUnicodeScalarBoundaries(text));
        Assert.False(new TextRange(2, 0).IsOnUnicodeScalarBoundaries(text));
        Assert.False(new TextRange(0, 2).IsOnUnicodeScalarBoundaries(text));
    }

    [Theory]
    [InlineData(0, int.MaxValue)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void TextRangeOutOfBoundsChecksDoNotOverflow(int start, int length)
    {
        TextRange range = new(start, length);

        Assert.False(range.IsWithin("abc"));
        Assert.False(range.IsOnUnicodeScalarBoundaries("abc"));
    }

    [Fact]
    public void TextMetricsCountUnicodeScalarValues()
    {
        Assert.Equal(3, MarkdownTextMetrics.CountUnicodeScalarValues("a😀b"));
    }

    [Fact]
    public void SegmentRequestModelPreservesSegmentOrderContractMetadata()
    {
        ProtectedToken token = new(
            new TextRange(6, 4),
            "__DTCLI_PH_0_0__",
            "{name}");
        MarkdownTranslationSegment segment = new(
            SegmentIndex: 0,
            SourceRange: new TextRange(0, 12),
            OriginalText: "Hello {name}",
            ProtectedText: "Hello __DTCLI_PH_0_0__",
            ProtectedTokens: [token]);
        TextSegmentTranslationRequest request = new(
            segment.SegmentIndex,
            segment.ProtectedText,
            segment.ProtectedTokens);

        Assert.Equal(0, request.SegmentIndex);
        Assert.Equal("Hello __DTCLI_PH_0_0__", request.ProtectedText);
        Assert.Same(segment.ProtectedTokens, request.ProtectedTokens);
    }

    [Fact]
    public void ProtectedSliceAndPatchMapUseStableSourceRanges()
    {
        ProtectedSlice slice = new(
            SliceId: "html-0",
            Kind: "html",
            SourceRange: new TextRange(10, 7),
            OriginalText: "<br />");
        SourcePatchMap patchMap = new(
            SegmentIndex: 1,
            OriginalRange: new TextRange(20, 5),
            PatchedRange: new TextRange(20, 9),
            LengthDelta: 4);

        Assert.Equal("html-0", slice.SliceId);
        Assert.Equal("html", slice.Kind);
        Assert.Equal(17, slice.SourceRange.End);
        Assert.Equal(4, patchMap.LengthDelta);
        Assert.Equal(29, patchMap.PatchedRange.End);
    }

    [Fact]
    public void DocumentParserReturnsSuccessfulParseResultForMarkdownText()
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse("# Heading");

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        Assert.IsType<HeadingBlock>(Assert.Single(result.Document!));
    }

    [Fact]
    public void ParserFactoryEnablesRequiredV1Syntax()
    {
        MarkdownPipeline pipeline = MarkdownParserFactory.CreateV1Pipeline();
        string markdown = string.Join(
            "\n",
            "---",
            "title: Example",
            "---",
            "",
            "| A | B |",
            "| - | - |",
            "| 1 | 2 |",
            "",
            "- [x] done",
            "",
            "~~gone~~",
            "",
            "note[^1]",
            "",
            "[^1]: footnote",
            "",
            "<div>raw</div>",
            "",
            "<!-- comment -->");

        string html = Markdown.ToHtml(markdown, pipeline);

        Assert.Contains("<table>", html, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal);
        Assert.Contains("<del>gone</del>", html, StringComparison.Ordinal);
        Assert.Contains("footnote", html, StringComparison.Ordinal);
        Assert.Contains("<div>raw</div>", html, StringComparison.Ordinal);
        Assert.Contains("<!-- comment -->", html, StringComparison.Ordinal);
        Assert.DoesNotContain("title: Example", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ParserFactoryEnablesPreciseInlineSourceLocations()
    {
        MarkdownDocument document = Markdown.Parse(
            "Paragraph with **strong** text.",
            MarkdownParserFactory.CreateV1Pipeline());
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document));
        EmphasisInline emphasis = Assert.IsType<EmphasisInline>(
            paragraph.Inline!.FirstChild!.NextSibling);

        Assert.True(paragraph.Span.Start >= 0);
        Assert.True(emphasis.Span.Start >= 0);
        Assert.True(emphasis.Span.End >= emphasis.Span.Start);
    }

    [Theory]
    [InlineData("++inserted++", "<ins>")]
    [InlineData("==marked==", "<mark>")]
    [InlineData("H~2~O", "<sub>")]
    public void ParserFactoryDoesNotEnableAdvancedEmphasisExtras(
        string markdown,
        string unexpectedHtml)
    {
        string html = Markdown.ToHtml(markdown, MarkdownParserFactory.CreateV1Pipeline());

        Assert.DoesNotContain(unexpectedHtml, html, StringComparison.OrdinalIgnoreCase);
    }
}
