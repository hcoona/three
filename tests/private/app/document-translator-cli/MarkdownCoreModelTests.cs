using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DocumentParserDecodesStrictUtf8AndCapturesBomMetadata(bool emitBom)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();
        byte[] markdownBytes = Encoding.UTF8.GetBytes("# Heading");
        byte[] bytes = emitBom
            ? [0xEF, 0xBB, 0xBF, .. markdownBytes]
            : markdownBytes;

        MarkdownParseResult result = parser.Parse(bytes);

        Assert.True(result.Succeeded);
        Assert.Equal(emitBom, result.SourceMetadata.HasUtf8Bom);
        Assert.Equal("# Heading", result.SourceText);
        Assert.IsType<HeadingBlock>(Assert.Single(result.Document!));
    }

    [Fact]
    public void DocumentParserUsesDecodedOffsetsAfterBomAndMultibyteCharacters()
    {
        const string markdown = "é😀 before\r\n```text\nprotected\n```\nAfter `code`.\n";
        byte[] markdownBytes = Encoding.UTF8.GetBytes(markdown);
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. markdownBytes];
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(bytes);

        Assert.True(result.Succeeded);
        Assert.True(result.SourceMetadata.HasUtf8Bom);
        Assert.Equal(markdown, result.SourceText);
        Assert.False(result.SourceText.StartsWith('\uFEFF'));
        int fencedCodeStart = result.SourceText.IndexOf("```text", StringComparison.Ordinal);
        ProtectedSlice fencedCodeSlice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.FencedCodeBlock);
        ProtectedSlice detectorFencedCodeSlice = Assert.Single(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.FencedCodeBlock);
        Assert.Equal(fencedCodeStart, fencedCodeSlice.SourceRange.Start);
        Assert.Equal(fencedCodeStart, detectorFencedCodeSlice.SourceRange.Start);
        foreach (MarkdownLineEnding lineEnding in result.SourceMetadata.LineEndings)
        {
            Assert.Equal(
                lineEnding.Text,
                result.SourceText.Substring(
                    lineEnding.SourceRange.Start,
                    lineEnding.SourceRange.Length));
        }
    }

    [Fact]
    public void DocumentParserRejectsInvalidUtf8BeforeMarkdownParsing()
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse([0x23, 0x20, 0xC3, 0x28]);

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.InvalidUtf8, diagnostic.Kind);
        Assert.Null(result.Document);
        Assert.Equal(string.Empty, result.SourceText);
    }

    [Fact]
    public void DocumentParserRejectsInvalidUtf8BeforeLeadingJsonFrontMatter()
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse([(byte)'{', 0xC3, 0x28]);

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.InvalidUtf8, diagnostic.Kind);
        Assert.Null(result.Document);
        Assert.Equal(string.Empty, result.SourceText);
    }

    [Fact]
    public void DocumentParserStringOverloadRejectsLoneHighSurrogate()
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(new string('\uD800', 1));

        AssertInvalidUtf8ParseFailure(result);
    }

    [Fact]
    public void DocumentParserStringOverloadRejectsLoneLowSurrogate()
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(new string('\uDC00', 1));

        AssertInvalidUtf8ParseFailure(result);
    }

    [Fact]
    public void DocumentParserStringOverloadAcceptsValidSurrogatePairs()
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse("Emoji \U0001F600");

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("Emoji \U0001F600", result.SourceText);
    }

    [Fact]
    public void DocumentParserRejectsLeadingJsonFrontMatterBeforeMarkdownParsing()
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse([0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}']);

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.UnsupportedSyntax, diagnostic.Kind);
        Assert.Null(result.Document);
        Assert.True(result.SourceMetadata.HasUtf8Bom);
    }

    [Fact]
    public void DocumentParserStringOverloadRejectsLeadingJsonFrontMatterAfterBom()
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse("\uFEFF{}");

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.UnsupportedSyntax, diagnostic.Kind);
        Assert.Null(result.Document);
        Assert.True(result.SourceMetadata.HasUtf8Bom);
        Assert.Equal("{}", result.SourceText);
    }

    [Theory]
    [InlineData("a\nb\n", true, "\n|\n")]
    [InlineData("a\r\nb", false, "\r\n")]
    [InlineData("a\nb\r\nc\rd", false, "\n|\r\n|\r")]
    public void DocumentParserCapturesLineEndingMetadataWithoutNormalization(
        string markdown,
        bool hasFinalNewline,
        string expectedLineEndingText)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(Encoding.UTF8.GetBytes(markdown));

        Assert.True(result.Succeeded);
        Assert.Equal(hasFinalNewline, result.SourceMetadata.HasFinalNewline);
        Assert.Equal(
            expectedLineEndingText.Split('|', StringSplitOptions.RemoveEmptyEntries),
            result.SourceMetadata.LineEndings.Select(lineEnding => lineEnding.Text));
        foreach (MarkdownLineEnding lineEnding in result.SourceMetadata.LineEndings)
        {
            Assert.Equal(
                markdown.Substring(lineEnding.SourceRange.Start, lineEnding.SourceRange.Length),
                lineEnding.Text);
        }
    }

    [Fact]
    public void DocumentParserCollectsPreliminaryProtectedRanges()
    {
        const string markdown = """
            ---
            title: Example
            ---

            ```csharp
            var path = "${HOME}";
            ```

                indented

            Paragraph with `inline` and [link](https://example.com/path#frag "Title")
            plus <span>raw text</span>.

            Reference [label][ref].

            [ref]: mailto:user@example.com "Mail"

            <!-- comment -->
            <div>block</div>

            - List item
            Escaped \* delimiter and ${name}.
            """;
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(Encoding.UTF8.GetBytes(markdown));

        Assert.True(result.Succeeded);
        string[] expectedProtectedKinds =
        [
            MarkdownProtectedRangeKinds.YamlFrontMatter,
            MarkdownProtectedRangeKinds.FencedCodeBlock,
            MarkdownProtectedRangeKinds.IndentedCodeBlock,
            MarkdownProtectedRangeKinds.InlineCode,
            MarkdownProtectedRangeKinds.LinkDestination,
            MarkdownProtectedRangeKinds.LinkTitle,
            MarkdownProtectedRangeKinds.ReferenceLabel,
            MarkdownProtectedRangeKinds.ReferenceDefinition,
            MarkdownProtectedRangeKinds.HtmlComment,
            MarkdownProtectedRangeKinds.RawHtmlBlock,
            MarkdownProtectedRangeKinds.UrlLiteral,
            MarkdownProtectedRangeKinds.EmailLiteral,
            MarkdownProtectedRangeKinds.UriFragment,
            MarkdownProtectedRangeKinds.EscapedMarkdownDelimiter,
            MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
        ];
        foreach (string expectedProtectedKind in expectedProtectedKinds)
        {
            Assert.Contains(
                result.ProtectedSlices,
                slice => slice.Kind == expectedProtectedKind);
        }

        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.InlineHtmlEnclosureText
                && slice.OriginalText == "raw text");
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MachineToken
                && slice.OriginalText == "${name}");
    }

    [Theory]
    [InlineData("1. First\n2) Second\n", "1.|2)")]
    [InlineData("- First\n+ Second\n* Third\n", "-|+|*")]
    [InlineData("- [x] Done\n- [X] Done\n- [ ] Todo\n", "[x]|[X]|[ ]")]
    public void StructuralSyntaxProtectionKeepsContextualMarkersWhole(
        string markdown,
        string expectedMarkersValue)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        string[] protectedStructuralSyntax = result.ProtectedSlices
            .Where(slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax)
            .Select(slice => slice.OriginalText)
            .ToArray();
        foreach (string expectedMarker in expectedMarkersValue.Split('|'))
        {
            Assert.Contains(expectedMarker, protectedStructuralSyntax);
        }
    }

    [Fact]
    public void StructuralSyntaxProtectionDoesNotProtectOrdinaryProsePunctuation()
    {
        const string markdown =
            "This is state-of-the-art (beta), C#nullable, foo_bar, a * b, "
            + "ordinary | pipe, and --- prose dashes.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax);
    }

    [Theory]
    [InlineData("hard\\\nnext\n", "\\")]
    [InlineData("hard\\\r\nnext\r\n", "\\")]
    [InlineData("hard  \nnext\n", "  ")]
    [InlineData("hard   \r\nnext\r\n", "   ")]
    public void StructuralSyntaxProtectionProtectsHardLineBreakMarkers(
        string markdown,
        string expectedMarker)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == expectedMarker);
    }

    [Fact]
    public void StructuralSyntaxProtectionPreservesOrdinarySoftBreaks()
    {
        const string markdown = "soft\nnext\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax);
    }

    [Fact]
    public void StructuralSyntaxProtectionDoesNotProtectNonParagraphTrailingSpacesAsHardBreaks()
    {
        const string markdown = "# heading  \nnext\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "  ");
    }

    [Fact]
    public void StructuralSyntaxProtectionSkipsHardLineBreakMarkersInsideOpaqueRanges()
    {
        const string markdown = "```\ncode\\\ncode  \n```\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText is "\\" or "  ");
    }

    [Theory]
    [InlineData("| --- |\n")]
    [InlineData("--- | ---\n")]
    public void StructuralSyntaxProtectionDoesNotProtectUnparsedPipeTableSeparators(
        string markdown)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax);
    }

    [Fact]
    public void StructuralSyntaxProtectionProtectsParsedPipeTableDelimiters()
    {
        const string markdown = """
            | Left | Right |
            | :--- | ---: |
            | Cell | Cell |
            """;
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "|");
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "---");
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == ":");
    }

    [Fact]
    public void StructuralSyntaxProtectionSkipsEscapedPipeTableCellPipes()
    {
        const string markdown = """
            | Left | Right |
            | --- | --- |
            | a \| b | c |
            """;
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        int escapedPipeIndex = markdown.IndexOf(@"\|", StringComparison.Ordinal) + 1;
        ProtectedSlice[] structuralPipes = result.ProtectedSlices
            .Where(slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "|")
            .ToArray();
        Assert.DoesNotContain(
            structuralPipes,
            slice => slice.SourceRange.Start == escapedPipeIndex);
        Assert.Contains(structuralPipes, slice => slice.SourceRange.Start == markdown.IndexOf('|'));
        Assert.Contains(
            structuralPipes,
            slice => slice.SourceRange.Start == markdown.LastIndexOf('|'));
    }

    [Theory]
    [InlineData(">> Quote", 0, 1)]
    [InlineData("> > Quote", 0, 2)]
    [InlineData(">    > Quote", 0, 5)]
    [InlineData("   > > Quote", 3, 5)]
    public void StructuralSyntaxProtectionProtectsNestedBlockquoteMarkers(
        string markdown,
        int firstMarkerStart,
        int secondMarkerStart)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice[] blockquoteMarkers = result.ProtectedSlices
            .Where(slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == ">")
            .ToArray();
        Assert.Contains(blockquoteMarkers, slice => slice.SourceRange.Start == firstMarkerStart);
        Assert.Contains(blockquoteMarkers, slice => slice.SourceRange.Start == secondMarkerStart);
    }

    [Theory]
    [InlineData("- > Quote", 2)]
    [InlineData("1. > Quote", 3)]
    public void StructuralSyntaxProtectionProtectsListItemBlockquoteMarkers(
        string markdown,
        int markerStart)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == ">"
                && slice.SourceRange.Start == markerStart);
    }

    [Theory]
    [InlineData("> # Heading", "#")]
    [InlineData("- # Heading", "#")]
    [InlineData("> ---", "---")]
    [InlineData("- ---", "---")]
    public void StructuralSyntaxProtectionIsContainerAwareForNestedBlockSyntax(
        string markdown,
        string expectedMarker)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == expectedMarker);
    }

    [Theory]
    [InlineData("> | Left | Right |\n> | --- | --- |\n> | Cell | Cell |\n")]
    [InlineData("- | Left | Right |\n  | --- | --- |\n  | Cell | Cell |\n")]
    public void StructuralSyntaxProtectionIsContainerAwareForNestedTables(string markdown)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "|");
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "---");
    }

    [Theory]
    [InlineData("Title\n=====\n", "=====")]
    [InlineData("Title\n--\n", "--")]
    public void StructuralSyntaxProtectionProtectsSetextHeadingMarkers(
        string markdown,
        string marker)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == marker);
    }

    [Theory]
    [InlineData("- Item heading\n  =====\n", "=====")]
    [InlineData("- Parent\n  - Nested heading\n    -----\n", "-----")]
    public void StructuralSyntaxProtectionProtectsSetextHeadingMarkersInListContinuations(
        string markdown,
        string marker)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == marker);
    }

    [Fact]
    public void StructuralSyntaxProtectionDoesNotTreatNonListOverIndentedSetextMarkerAsHeading()
    {
        const string markdown = "Title\n    =====\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "=====");
    }

    [Fact]
    public void StructuralSyntaxProtectionDoesNotDuplicateSetextThematicMarkerRanges()
    {
        const string markdown = "Title\n---\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice[] setextMarkerSlices = result.ProtectedSlices
            .Where(slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.SourceRange.Start == markdown.IndexOf("---", StringComparison.Ordinal)
                && slice.SourceRange.Length == 3)
            .ToArray();
        Assert.Single(setextMarkerSlices);
    }

    [Fact]
    public void StructuralSyntaxProtectionStillProtectsStandaloneThematicBreaks()
    {
        const string markdown = "---\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.SourceRange.Start == 0
                && slice.SourceRange.Length == 3
                && slice.OriginalText == "---");
    }

    [Fact]
    public void StructuralSyntaxProtectionCoversSupportedMarkdownDelimiters()
    {
        const string markdown = """
            # Heading #
            > Quote

            ---

            | Left | Right |
            | :--- | ---: |
            | Cell | Cell |

            Paragraph with **strong**, *emphasis*, __bold__, _em_, and ~~strike~~.
            """;
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        string[] protectedStructuralSyntax = result.ProtectedSlices
            .Where(slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax)
            .Select(slice => slice.OriginalText)
            .ToArray();
        foreach (string expectedMarker in new[]
        {
            "#",
            ">",
            "---",
            "|",
            ":",
            "**",
            "*",
            "__",
            "_",
            "~~",
        })
        {
            Assert.Contains(expectedMarker, protectedStructuralSyntax);
        }
    }

    [Fact]
    public void StructuralSyntaxProtectionDoesNotPairEmphasisAcrossParagraphs()
    {
        const string markdown = "*not closed\n\nnot open*";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "*");
    }

    [Fact]
    public void StructuralSyntaxProtectionUsesRealListItems()
    {
        const string markdown =
            "Paragraph\n    - continuation\n\t- tab continuation\n\n- real\n  - nested\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        int continuationMarker = markdown.IndexOf("- continuation", StringComparison.Ordinal);
        int tabContinuationMarker = markdown.IndexOf(
            "- tab continuation",
            StringComparison.Ordinal);
        int realMarker = markdown.IndexOf("- real", StringComparison.Ordinal);
        int nestedMarker = markdown.IndexOf("- nested", StringComparison.Ordinal);
        ProtectedSlice[] listMarkers = result.ProtectedSlices
            .Where(slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "-")
            .ToArray();

        Assert.DoesNotContain(listMarkers, slice => slice.SourceRange.Start == continuationMarker);
        Assert.DoesNotContain(
            listMarkers,
            slice => slice.SourceRange.Start == tabContinuationMarker);
        Assert.Contains(listMarkers, slice => slice.SourceRange.Start == realMarker);
        Assert.Contains(listMarkers, slice => slice.SourceRange.Start == nestedMarker);
    }

    [Fact]
    public void StructuralSyntaxProtectionCoversLinkAndImageDelimitersWithoutDisplayText()
    {
        const string markdown =
            "[Hello](https://example.com) [See][ref] ![Alt text](image.png)"
            + "\n\n[ref]: https://example.com\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        string[] structuralSyntax = result.ProtectedSlices
            .Where(slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax)
            .Select(slice => slice.OriginalText)
            .ToArray();

        foreach (string expected in new[] { "[", "]", "(", ")", "!" })
        {
            Assert.Contains(expected, structuralSyntax);
        }

        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText is "Hello" or "See" or "Alt text");
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceLabel
                && slice.OriginalText.Contains("ref", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[a `[`](dest)", "[", "dest")]
    [InlineData("[a `]`](dest)", "]", "dest")]
    [InlineData("![a `[`](img.png)", "[", "img.png")]
    [InlineData("![a `]`](img.png)", "]", "img.png")]
    public void LinkAndImageDelimiterScanningSkipsInlineCodeBrackets(
        string markdown,
        string codeBracket,
        string destination)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice inlineCode = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.InlineCode);
        Assert.Equal($"`{codeBracket}`", inlineCode.OriginalText);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && RangeContains(
                    slice.SourceRange,
                    new TextRange(inlineCode.SourceRange.Start + 1, 1)));
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.LinkDestination
                && slice.OriginalText == destination);

        AssertLinkOrImageStructuralDelimiterIsProtected(markdown, result, "[");
        AssertLinkOrImageStructuralDelimiterIsProtected(markdown, result, "]");
        AssertLinkOrImageStructuralDelimiterIsProtected(markdown, result, "(");
        AssertLinkOrImageStructuralDelimiterIsProtected(markdown, result, ")");
        if (markdown.StartsWith('!'))
        {
            AssertLinkOrImageStructuralDelimiterIsProtected(markdown, result, "!");
        }
    }

    [Theory]
    [InlineData("[a <span title=\"[\">x</span>](dest)", "[", "dest")]
    [InlineData("[a <span title=\"]\">x</span>](dest)", "]", "dest")]
    [InlineData("![a <span title=\"[\">x</span>](img.png)", "[", "img.png")]
    [InlineData("![a <span title=\"]\">x</span>](img.png)", "]", "img.png")]
    public void LinkAndImageDelimiterScanningSkipsInlineHtmlBrackets(
        string markdown,
        string htmlBracket,
        string destination)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice htmlTag = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.InlineHtmlTag
                && slice.OriginalText.Contains(htmlBracket, StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && RangeContains(slice.SourceRange, new TextRange(
                    htmlTag.SourceRange.Start
                        + htmlTag.OriginalText.IndexOf(htmlBracket, StringComparison.Ordinal),
                    1)));
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.LinkDestination
                && slice.OriginalText == destination);

        AssertLinkOrImageStructuralDelimiterIsProtected(markdown, result, "[");
        AssertLinkOrImageStructuralDelimiterIsProtected(markdown, result, "]");
        AssertLinkOrImageStructuralDelimiterIsProtected(markdown, result, "(");
        AssertLinkOrImageStructuralDelimiterIsProtected(markdown, result, ")");
        if (markdown.StartsWith('!'))
        {
            AssertLinkOrImageStructuralDelimiterIsProtected(markdown, result, "!");
        }
    }

    [Theory]
    [InlineData("***strong emphasis***", "***")]
    [InlineData("___strong emphasis___", "___")]
    public void StructuralSyntaxProtectionCoversCompositeEmphasisDelimiterRuns(
        string markdown,
        string delimiterRun)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Equal(
            2,
            result.ProtectedSlices.Count(slice =>
                slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == delimiterRun));
    }

    [Theory]
    [InlineData("[Hello](https://example.com)")]
    [InlineData("![Alt](image.png)")]
    public void InlineLinksAndImagesDoNotProtectInlineTextAsReferenceLabels(string markdown)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceLabel);
    }

    [Fact]
    public void ReferenceStyleLinksProtectReferenceLabels()
    {
        const string markdown = "[Hello][ref]\n\n[ref]: https://example.com\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceLabel);
    }

    [Theory]
    [InlineData("<ftp://example.com>")]
    [InlineData("<tel:+123>")]
    public void AutolinkProtectionSupportsCommonMarkSchemes(string autolink)
    {
        string markdown = $"Contact {autolink}.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.Autolink
                && slice.OriginalText == autolink);
    }


    [Fact]
    public void ReferenceDefinitionsDoNotProtectLabelsLongerThanCommonMarkLimit()
    {
        string label = new('a', 1000);
        string markdown = $"[{label}]: https://example.com\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
    }

    [Fact]
    public void ReferenceDefinitionsInListContinuationLinesUseListItemContentIndent()
    {
        const string markdown = "- item\n    [ref]: https://example.com\n";
        int labelStart = markdown.IndexOf("[ref]:", StringComparison.Ordinal);
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition
                && slice.SourceRange.Start <= labelStart
                && slice.SourceRange.End > labelStart);
    }

    [Fact]
    public void FootnoteDefinitionsInListContinuationLinesUseListItemContentIndent()
    {
        const string markdown = "- item\n    [^n]: note\n";
        int markerStart = markdown.IndexOf("[^n]:", StringComparison.Ordinal);
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.FootnoteDefinition
                && slice.SourceRange.Start == markerStart);
    }

    [Fact]
    public void AtxHeadingsInListContinuationLinesUseListItemContentIndent()
    {
        const string markdown = "- item\n    # Heading\n";
        int markerStart = markdown.IndexOf('#');
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "#"
                && slice.SourceRange.Start == markerStart);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ListContinuationScanningPreservesContextAfterWhitespaceOnlyBlankLines(
        string blankLineWhitespace)
    {
        string markdown =
            $"- item\n{blankLineWhitespace}\n    # Heading\n    [ref]: https://example.com\n";
        int headingMarkerStart = markdown.IndexOf('#');
        int referenceLabelStart = markdown.IndexOf("[ref]:", StringComparison.Ordinal);
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "#"
                && slice.SourceRange.Start == headingMarkerStart);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition
                && slice.SourceRange.Start <= referenceLabelStart
                && slice.SourceRange.End > referenceLabelStart);
    }

    [Fact]
    public void BlockquotesInListContinuationLinesUseListItemContentIndent()
    {
        const string markdown = "- item\n    > quote\n";
        int markerStart = markdown.IndexOf('>');
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == ">"
                && slice.SourceRange.Start == markerStart);
    }

    [Fact]
    public void ListContinuationScanningPreservesIndentedCodeRelativeToListContent()
    {
        const string markdown = "- item\n      # code\n";
        int hashStart = markdown.IndexOf('#');
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.SourceRange.Start == hashStart);
    }

    [Fact]
    public void ContainerAwareScanningDoesNotPromoteNonListOverIndentedParagraphLines()
    {
        const string markdown = "Paragraph\n    # not a heading\n";
        int hashStart = markdown.IndexOf('#');
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.SourceRange.Start == hashStart);
    }

    [Fact]
    public void OrderedListInterruptionWithNonOneStartDoesNotPromoteAtxHeading()
    {
        const string markdown = "Paragraph\n2. # not heading\n";
        int hashStart = markdown.IndexOf('#');
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "#"
                && slice.SourceRange.Start == hashStart);
    }

    [Theory]
    [InlineData("> Paragraph\n> 2. # not heading\n")]
    [InlineData("- Paragraph\n  2. # not heading\n")]
    public void OrderedListInterruptionWithNonOneStartInsideContainersDoesNotPromoteAtxHeading(
        string markdown)
    {
        int markerStart = markdown.IndexOf("2.", StringComparison.Ordinal);
        int hashStart = markdown.IndexOf('#');
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "2."
                && slice.SourceRange.Start == markerStart);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "#"
                && slice.SourceRange.Start == hashStart);
    }

    [Fact]
    public void OrderedListInterruptionWithNonOneStartDoesNotPromoteReferenceDefinition()
    {
        const string markdown = "Paragraph\n2. [ref]: https://example.com\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
    }

    [Theory]
    [InlineData("> Paragraph\n> 2. [ref]: https://example.com\n")]
    [InlineData("- Paragraph\n  2. [ref]: https://example.com\n")]
    public void
        OrderedListInterruptionWithNonOneStartInsideContainersDoesNotPromoteReferenceDefinition(
        string markdown)
    {
        int markerStart = markdown.IndexOf("2.", StringComparison.Ordinal);
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "2."
                && slice.SourceRange.Start == markerStart);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
    }

    [Fact]
    public void OrderedListInterruptionWithNonOneStartDoesNotPromoteFootnoteDefinition()
    {
        const string markdown = "Paragraph\n2. [^n]: note\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.FootnoteDefinition);
    }

    [Theory]
    [InlineData("> Paragraph\n> 2. [^n]: note\n")]
    [InlineData("- Paragraph\n  2. [^n]: note\n")]
    public void
        OrderedListInterruptionWithNonOneStartInsideContainersDoesNotPromoteFootnoteDefinition(
        string markdown)
    {
        int markerStart = markdown.IndexOf("2.", StringComparison.Ordinal);
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "2."
                && slice.SourceRange.Start == markerStart);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.FootnoteDefinition);
    }

    [Fact]
    public void OrderedListInterruptionWithOneStartFollowsMarkdigListParsing()
    {
        const string markdown = "Paragraph\n1. # heading\n";
        int hashStart = markdown.IndexOf('#');
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Document);
        if (result.Document.Last() is ListBlock)
        {
            Assert.Contains(
                result.ProtectedSlices,
                slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                    && slice.OriginalText == "#"
                    && slice.SourceRange.Start == hashStart);
        }
    }

    [Theory]
    [InlineData(@"Text \[^term]", "")]
    [InlineData(@"Text \\[^term]", "[^term]")]
    [InlineData(@"Text \\\[^term]", "")]
    public void FootnoteReferenceProtectionUsesBackslashParity(
        string markdown,
        string expectedReferencesValue)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Equal(
            expectedReferencesValue.Split('|', StringSplitOptions.RemoveEmptyEntries),
            result.ProtectedSlices
                .Where(slice => slice.Kind == MarkdownProtectedRangeKinds.FootnoteReference)
                .Select(slice => slice.OriginalText)
                .ToArray());
    }

    [Fact]
    public void ProtectedRangeCollectionFailsClosedForRequiredOutOfBoundsSourceSpan()
    {
        MarkdownDocument document = Markdown.Parse(
            "`code`",
            MarkdownParserFactory.CreateV1Pipeline());

        MarkdownProtectedRangeCollectionResult result =
            MarkdownProtectedRangeCollector.Collect(document, string.Empty);

        Assert.Empty(result.ProtectedSlices);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.UnreliableSourceSpan, diagnostic.Kind);
        Assert.Contains(
            MarkdownProtectedRangeKinds.InlineCode,
            diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DetectorExclusionRangesKeepCandidateRangesVisibleForUnsupportedDetection()
    {
        const string markdown = "Text [link](https://example.com/{id}) with `code` and ${name}.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.InlineCode);
        Assert.Contains(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MachineToken);
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.OriginalText == "{id}");
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.LinkDestination);
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UrlLiteral);
    }

    [Fact]
    public void DetectorExclusionKindsMatchUnsupportedDetectorHandoffAllowList()
    {
        const string markdown = """
            ---
            title: Example
            ---

            ```text
            fenced
            ```

                indented

            `inline` ${name}

            <!-- comment -->

            <section>
            raw html
            </section>

            [link](https://example.com/{id}) https://example.com/path <span>inline html</span>
            """;
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        string[] expectedKinds =
        [
            MarkdownProtectedRangeKinds.FencedCodeBlock,
            MarkdownProtectedRangeKinds.IndentedCodeBlock,
            MarkdownProtectedRangeKinds.InlineCode,
            MarkdownProtectedRangeKinds.YamlFrontMatter,
            MarkdownProtectedRangeKinds.RawHtmlBlock,
            MarkdownProtectedRangeKinds.HtmlComment,
            MarkdownProtectedRangeKinds.MachineToken,
        ];
        Assert.Equal(
            expectedKinds.Order(StringComparer.Ordinal),
            result.DetectorExclusionSlices
                .Select(slice => slice.Kind)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.Kind is MarkdownProtectedRangeKinds.LinkDestination
                or MarkdownProtectedRangeKinds.UrlLiteral
                or MarkdownProtectedRangeKinds.InlineHtmlTag
                or MarkdownProtectedRangeKinds.InlineHtmlEnclosureText);
    }

    [Fact]
    public void DetectorExclusionRangesDoNotTreatPathPlaceholdersAsMachineTokens()
    {
        const string markdown =
            "Paths docs/${name}.md, {locale}.json, {{locale}}.json, "
            + "docs/README-${lang} and ./docs/{0}; standalone ${name}.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MachineToken);
        Assert.Equal("${name}", slice.OriginalText);
        Assert.Equal(
            markdown.LastIndexOf("${name}", StringComparison.Ordinal),
            slice.SourceRange.Start);
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.OriginalText == "{0}");
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.OriginalText == "{locale}");
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.OriginalText == "{{locale}}");
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.OriginalText == "{lang}");
    }

    [Fact]
    public void DetectorExclusionRangesDoNotTreatSingleBraceIdentifiersAsMachineTokens()
    {
        const string markdown =
            "Track ID-{id}, standalone {name}, and path-like docs/README-{lang}.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MachineToken
                && slice.OriginalText is "{id}" or "{name}" or "{lang}");
    }

    [Theory]
    [InlineData("${id}")]
    [InlineData("{{id}}")]
    [InlineData("{0}")]
    public void DetectorExclusionRangesProtectSupportedEarlyMachineTokens(string token)
    {
        string markdown = $"Text {token}.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MachineToken);
        Assert.Equal(token, slice.OriginalText);
    }

    [Theory]
    [InlineData("{0:N2}")]
    [InlineData("{0,10}")]
    [InlineData("{0:<Component />}")]
    public void DetectorExclusionRangesDoNotProtectFormattedNumericReplacementFields(string token)
    {
        string markdown = $"Text {token}.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MachineToken);
    }

    [Theory]
    [InlineData("{１２}")]
    [InlineData("{١}")]
    public void DetectorExclusionRangesDoNotProtectUnicodeDecimalDigitReplacementFields(
        string token)
    {
        string markdown = $"Text {token}.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MachineToken);
    }

    [Theory]
    [InlineData("#section-{id}")]
    [InlineData("#{id}")]
    [InlineData("#section.{id}")]
    [InlineData("#section?tab={id}")]
    [InlineData("#section?tab={id}&mode={mode}")]
    public void UriFragmentProtectionDoesNotProtectCompleteSingleBraceIdentifierFragments(
        string fragment)
    {
        string markdown = $"Jump to {fragment}.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UriFragment
                && slice.OriginalText == fragment);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MachineToken);
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MachineToken);
    }

    [Theory]
    [InlineData("#{0}")]
    [InlineData("#sec-{0}")]
    [InlineData("#section/{0}?tab:details%2Fmore")]
    [InlineData("#{{id}}")]
    [InlineData("#section-{{id}}")]
    [InlineData("#section-{{user.name}}")]
    [InlineData("#section-{{ name }}")]
    [InlineData("#section-{{foo-bar}}")]
    [InlineData("#section-${name}")]
    [InlineData("#section?tab={{id}}")]
    [InlineData("#foo+bar")]
    [InlineData("#user@example")]
    [InlineData("#a;b")]
    public void DetectorExclusionRangesDoNotTreatUriFragmentPlaceholdersAsMachineTokens(
        string fragment)
    {
        string markdown = $"Jump to {fragment} then use standalone ${{id}}.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UriFragment
                && slice.OriginalText == fragment);
        ProtectedSlice slice = Assert.Single(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MachineToken);
        Assert.Equal(
            markdown.LastIndexOf("${id}", StringComparison.Ordinal),
            slice.SourceRange.Start);
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.SourceRange.Start >= markdown.IndexOf(
                fragment,
                StringComparison.Ordinal)
                && slice.SourceRange.Start < markdown.IndexOf(
                    fragment,
                    StringComparison.Ordinal) + fragment.Length);
    }

    [Theory]
    [InlineData("#{0:N2}")]
    [InlineData("#section/{0,10}")]
    [InlineData("#{0:<Component />}")]
    public void UriFragmentProtectionDoesNotProtectCompleteFormattedNumericReplacementFields(
        string fragment)
    {
        string markdown = $"Jump to {fragment}.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UriFragment
                    && slice.OriginalText == fragment);
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MachineToken);
    }

    [Theory]
    [InlineData("#{１２}")]
    [InlineData("#sec-{١}")]
    public void UriFragmentProtectionDoesNotProtectUnicodeDecimalDigitReplacementFields(
        string fragment)
    {
        string markdown = $"Jump to {fragment}.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UriFragment
                    && slice.OriginalText == fragment);
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MachineToken);
    }

    [Theory]
    [InlineData("[x](#sec)", "#sec")]
    [InlineData("[x](#sec(a))", "#sec(a)")]
    public void UriFragmentProtectionExcludesInlineLinkClosingDelimiter(
        string markdown,
        string expectedFragment)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice fragment = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UriFragment);
        Assert.Equal(expectedFragment, fragment.OriginalText);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UriFragment
                && slice.OriginalText.EndsWith(')')
                && slice.OriginalText != expectedFragment);
    }

    [Theory]
    [InlineData("Contact user@example.com.", "user@example.com")]
    [InlineData("Contact user@example.com.au", "user@example.com.au")]
    public void EmailLiteralProtectionHandlesTrailingPunctuationAndDomainContinuation(
        string markdown,
        string expectedEmail)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice email = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.EmailLiteral);
        Assert.Equal(expectedEmail, email.OriginalText);
    }

    [Fact]
    public void UrlLiteralProtectionIncludesBalancedParentheses()
    {
        const string markdown = "Visit https://example.com/a(b)c then continue.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice url = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UrlLiteral);
        Assert.Equal("https://example.com/a(b)c", url.OriginalText);
    }

    [Fact]
    public void UrlLiteralProtectionTrimsTrailingUnmatchedClosingParenthesis()
    {
        const string markdown = "Visit (https://example.com/a(b)) then continue.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice url = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UrlLiteral);
        Assert.Equal("https://example.com/a(b)", url.OriginalText);
    }

    [Theory]
    [InlineData("Visit (https://example.com/a(b)).", "https://example.com/a(b)")]
    [InlineData("Visit https://example.com/path.)", "https://example.com/path")]
    [InlineData("Visit https://example.com/path?!", "https://example.com/path")]
    [InlineData("""See "https://example.com".""", "https://example.com")]
    [InlineData("See 'https://example.com'.", "https://example.com")]
    public void UrlLiteralProtectionTrimsGfmTrailingPunctuationBeforeUnmatchedClosingParenthesis(
        string markdown,
        string expectedUrl)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice url = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UrlLiteral);
        Assert.Equal(expectedUrl, url.OriginalText);
    }

    [Fact]
    public void UrlLiteralProtectionStopsAtMarkdownLinkBoundary()
    {
        const string markdown = "[https://example.com](dest)";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice url = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UrlLiteral);
        Assert.Equal("https://example.com", url.OriginalText);
    }

    [Fact]
    public void UrlLiteralProtectionStopsBeforeInlineCodeBacktickBoundary()
    {
        const string markdown = "Visit https://example.com/`code` now.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice url = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UrlLiteral);
        ProtectedSlice inlineCode = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.InlineCode);
        Assert.Equal("https://example.com/", url.OriginalText);
        Assert.Equal("`code`", inlineCode.OriginalText);
        Assert.False(RangesOverlap(url.SourceRange, inlineCode.SourceRange));
    }

    [Theory]
    [InlineData("See #section.", "#section")]
    [InlineData("[x](#section.)", "#section.")]
    [InlineData("Visit https://example.com/page#section.", "#section")]
    public void UriFragmentProtectionTrimsStandaloneGfmTrailingPunctuation(
        string markdown,
        string expectedFragment)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice fragment = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UriFragment);
        Assert.Equal(expectedFragment, fragment.OriginalText);
    }

    [Theory]
    [InlineData("Entity &#169; is not a fragment.")]
    [InlineData("C#nullable is not a fragment.")]
    [InlineData("foo#bar is not a fragment.")]
    public void UriFragmentProtectionRequiresSafeStandaloneLeftBoundary(string markdown)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UriFragment);
    }

    [Fact]
    public void UriFragmentProtectionAllowsSafeStandaloneLeftBoundary()
    {
        const string markdown = "See #section.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice fragment = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UriFragment);
        Assert.Equal("#section", fragment.OriginalText);
    }

    [Fact]
    public void UriFragmentProtectionKeepsBalancedParenthesesInsideUrlLiteral()
    {
        const string markdown = "Visit https://example.com/page#sec(a)b then continue.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice fragment = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UriFragment);
        Assert.Equal("#sec(a)b", fragment.OriginalText);
    }

    [Theory]
    [InlineData(@"Text \#escaped and #real", "#real", true)]
    [InlineData(@"Text \\#frag and #real", "#frag|#real", false)]
    [InlineData(@"Text \\\#escaped and #real", "#real", true)]
    public void UriFragmentProtectionUsesBackslashParity(
        string markdown,
        string expectedFragmentsValue,
        bool expectEscapedHashDelimiter)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        string[] expectedFragments = expectedFragmentsValue.Split('|');
        Assert.Equal(
            expectedFragments,
            result.ProtectedSlices
                .Where(slice => slice.Kind == MarkdownProtectedRangeKinds.UriFragment)
                .Select(slice => slice.OriginalText)
                .ToArray());

        bool hasEscapedHashDelimiter = result.ProtectedSlices.Any(
            slice => slice.Kind == MarkdownProtectedRangeKinds.EscapedMarkdownDelimiter
                && slice.OriginalText == @"\#");
        Assert.Equal(expectEscapedHashDelimiter, hasEscapedHashDelimiter);
    }

    [Theory]
    [InlineData(@"1\. not a list", @"\.")]
    [InlineData("Title\n\\=\n", @"\=")]
    public void EscapedMarkdownDelimiterProtectionCoversCommonMarkEscapablePunctuation(
        string markdown,
        string expectedEscapeSequence)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.EscapedMarkdownDelimiter
                && slice.OriginalText == expectedEscapeSequence);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.SourceRange.Start == markdown.IndexOf(
                    expectedEscapeSequence,
                    StringComparison.Ordinal) + 1);
    }

    [Fact]
    public void SourceLevelScannersIgnoreOpaqueProtectedRanges()
    {
        const string markdown = """
            ---
            [yaml]: https://yaml.example/#yaml
            ---

            ```markdown
            [code]: https://code.example/#code
            [^code]: https://footnote.example
            Visit https://code.example/#visit and code@example.com.
            ```

                [indented]: https://indented.example/#indented
                [^indented]: https://footnote.example
                Visit https://indented.example/#visit and indented@example.com.

            `[inline]: https://inline.example/#inline and [^inline]`

            <!-- [comment]: https://comment.example/#comment and comment@example.com -->

            <div>
            [html]: https://html.example/#html
            Visit https://html.example/#visit and html@example.com.
            </div>

            [real]: https://real.example/#real
            Visit https://outside.example/#outside and [^note].

            [^note]: outside
            """;
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        string[] sourceLevelKinds =
        [
            MarkdownProtectedRangeKinds.ReferenceDefinition,
            MarkdownProtectedRangeKinds.FootnoteDefinition,
            MarkdownProtectedRangeKinds.FootnoteReference,
            MarkdownProtectedRangeKinds.Autolink,
            MarkdownProtectedRangeKinds.UrlLiteral,
            MarkdownProtectedRangeKinds.EmailLiteral,
            MarkdownProtectedRangeKinds.UriFragment,
        ];
        ProtectedSlice[] opaqueSlices = result.ProtectedSlices
            .Where(slice => slice.Kind is MarkdownProtectedRangeKinds.FencedCodeBlock
                or MarkdownProtectedRangeKinds.IndentedCodeBlock
                or MarkdownProtectedRangeKinds.InlineCode
                or MarkdownProtectedRangeKinds.YamlFrontMatter
                or MarkdownProtectedRangeKinds.RawHtmlBlock
                or MarkdownProtectedRangeKinds.HtmlComment)
            .ToArray();

        Assert.Contains(
            opaqueSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.IndentedCodeBlock
                && slice.OriginalText.Contains(
                    "https://indented.example/#indented",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => sourceLevelKinds.Contains(slice.Kind)
                && opaqueSlices.Any(opaqueSlice => RangesOverlap(
                    slice.SourceRange,
                    opaqueSlice.SourceRange)));
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition
                && slice.OriginalText.Contains("[real]:", StringComparison.Ordinal));
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UrlLiteral
                && slice.OriginalText == "https://outside.example/#outside");
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UriFragment
                && slice.OriginalText == "#outside");
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.FootnoteReference
                && slice.OriginalText == "[^note]");
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.FootnoteDefinition
                && slice.OriginalText == "[^note]:");
    }

    [Fact]
    public void SourceLevelScannersIgnoreInlineHtmlProtectedRanges()
    {
        const string markdown =
            "Text <span data-url=\"https://tag.example/#tag\" data-email=\"tag@example.com\" "
            + "data-ref=\"[tag]\" data-footnote=\"[^tag]\">Visit "
            + "https://inner.example/#inner and inner@example.com [^inner]</span>.\n"
            + "\n"
            + "Visit https://outside.example/#outside and outside@example.com [^outside].";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        string[] sourceLevelKinds =
        [
            MarkdownProtectedRangeKinds.ReferenceDefinition,
            MarkdownProtectedRangeKinds.FootnoteDefinition,
            MarkdownProtectedRangeKinds.FootnoteReference,
            MarkdownProtectedRangeKinds.Autolink,
            MarkdownProtectedRangeKinds.UrlLiteral,
            MarkdownProtectedRangeKinds.EmailLiteral,
            MarkdownProtectedRangeKinds.UriFragment,
        ];
        ProtectedSlice[] inlineHtmlSlices = result.ProtectedSlices
            .Where(slice => slice.Kind is MarkdownProtectedRangeKinds.InlineHtmlTag
                or MarkdownProtectedRangeKinds.InlineHtmlEnclosureText)
            .ToArray();

        Assert.Contains(
            inlineHtmlSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.InlineHtmlTag
                && slice.OriginalText.StartsWith("<span", StringComparison.Ordinal));
        Assert.Contains(
            inlineHtmlSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.InlineHtmlEnclosureText
                && slice.OriginalText.Contains(
                    "https://inner.example/#inner",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => sourceLevelKinds.Contains(slice.Kind)
                && inlineHtmlSlices.Any(inlineHtmlSlice => RangesOverlap(
                    slice.SourceRange,
                    inlineHtmlSlice.SourceRange)));
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.Kind is MarkdownProtectedRangeKinds.InlineHtmlTag
                or MarkdownProtectedRangeKinds.InlineHtmlEnclosureText);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UrlLiteral
                && slice.OriginalText == "https://outside.example/#outside");
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.EmailLiteral
                && slice.OriginalText == "outside@example.com");
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.UriFragment
                && slice.OriginalText == "#outside");
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.FootnoteReference
                && slice.OriginalText == "[^outside]");
    }

    [Fact]
    public void InlineHtmlMachineTokensAreNotDetectorExclusions()
    {
        const string markdown = "<span>${foo} {{foo}} {0}</span>";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.InlineHtmlEnclosureText
                && slice.OriginalText == "${foo} {{foo}} {0}");
        foreach (string machineToken in new[] { "${foo}", "{{foo}}", "{0}" })
        {
            Assert.DoesNotContain(
                result.DetectorExclusionSlices,
                slice => slice.Kind == MarkdownProtectedRangeKinds.MachineToken
                    && slice.OriginalText == machineToken);
        }
    }

    [Fact]
    public void InlineHtmlSimplePairProtectsInnerText()
    {
        const string markdown = """Text <span data-x="a > b">raw text</span>.""";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.InlineHtmlEnclosureText);
        Assert.Equal("raw text", slice.OriginalText);
        Assert.Equal(
            ["""<span data-x="a > b">""", "</span>"],
            result.ProtectedSlices
                .Where(slice => slice.Kind == MarkdownProtectedRangeKinds.InlineHtmlTag)
                .Select(slice => slice.OriginalText)
                .ToArray());
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.InlineHtmlEnclosureText);
        Assert.DoesNotContain(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.InlineHtmlTag);
    }

    [Theory]
    [InlineData("<span>[label](url)</span>", "[label](url)")]
    [InlineData("<span>![alt](url)</span>", "![alt](url)")]
    [InlineData("<span>[ref][id]</span>", "[ref][id]")]
    public void InlineHtmlPairRemovesContainedLinkAndStructuralSlices(
        string markdown,
        string expectedEnclosureText)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice enclosure = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.InlineHtmlEnclosureText);
        Assert.Equal(expectedEnclosureText, enclosure.OriginalText);
        Assert.Equal(
            ["<span>", "</span>"],
            result.ProtectedSlices
                .Where(slice => slice.Kind == MarkdownProtectedRangeKinds.InlineHtmlTag)
                .Select(slice => slice.OriginalText)
                .ToArray());

        string[] removedInsideEnclosureKinds =
        [
            MarkdownProtectedRangeKinds.LinkDestination,
            MarkdownProtectedRangeKinds.LinkTitle,
            MarkdownProtectedRangeKinds.ReferenceLabel,
            MarkdownProtectedRangeKinds.MarkdownStructuralSyntax,
            MarkdownProtectedRangeKinds.UrlLiteral,
            MarkdownProtectedRangeKinds.EmailLiteral,
            MarkdownProtectedRangeKinds.UriFragment,
        ];
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => removedInsideEnclosureKinds.Contains(slice.Kind)
                && RangeContains(enclosure.SourceRange, slice.SourceRange));
    }

    [Fact]
    public void InlineHtmlCommentIsProtectedWithoutPairedTagParsing()
    {
        const string markdown = "Text <!-- keep -->";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.HtmlComment);
        Assert.Equal("<!-- keep -->", slice.OriginalText);
        Assert.Contains(
            result.DetectorExclusionSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.HtmlComment);
    }

    [Theory]
    [InlineData("Text <!DOCTYPE html>")]
    [InlineData("Text <?keep?>")]
    [InlineData("Text <![CDATA[keep]]>")]
    public void InlineHtmlDeclarationsAreProtectedWithoutPairedTagParsing(string markdown)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.InlineHtmlTag
                && markdown.EndsWith(slice.OriginalText, StringComparison.Ordinal));
    }

    [Fact]
    public void InlineHtmlQuotedAttributeOpenTagWithoutCloseFailsClosed()
    {
        const string markdown = """Text <span data-x="a > b">raw text.""";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.UnsupportedSyntax, diagnostic.Kind);
        Assert.DoesNotContain("raw text", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineHtmlNestedPairFailsClosed()
    {
        const string markdown = "Text <span><em>raw text</em></span>.";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.UnsupportedSyntax, diagnostic.Kind);
        Assert.DoesNotContain("<span>", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("raw text", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineHtmlPairAcrossParagraphBoundaryFailsClosed()
    {
        const string markdown = "<span>first\n\nsecond</span>";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.UnsupportedSyntax, diagnostic.Kind);
        Assert.DoesNotContain(markdown, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineHtmlPairAcrossListItemsFailsClosed()
    {
        const string markdown = "- <span>first\n- second</span>";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.UnsupportedSyntax, diagnostic.Kind);
        Assert.DoesNotContain(markdown, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineHtmlPairAcrossLeafBlocksFailsClosed()
    {
        const string markdown = "## <span>Heading\nParagraph</span>";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.UnsupportedSyntax, diagnostic.Kind);
        Assert.DoesNotContain(markdown, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineHtmlPairAcrossInlineContainersFailsClosed()
    {
        const string markdown = "[<span>x](url)</span>";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.UnsupportedSyntax, diagnostic.Kind);
        Assert.DoesNotContain(markdown, diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Text <span>raw text.")]
    [InlineData("Text </span>raw text.")]
    [InlineData("Text <span>raw text</em>.")]
    public void InlineHtmlMalformedOrMismatchedPairFailsClosed(string markdown)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.UnsupportedSyntax, diagnostic.Kind);
        Assert.DoesNotContain(markdown, diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("${name}")]
    [InlineData("${foo}")]
    public void MachineTokenProtectionRecognizesDollarBraceIdentifiers(string token)
    {
        IReadOnlyList<ProtectedSlice> slices =
            MarkdownTokenProtector.ScanEarlyMachineTokens($"Value {token}.");

        ProtectedSlice slice = Assert.Single(slices);
        Assert.Equal(MarkdownProtectedRangeKinds.MachineToken, slice.Kind);
        Assert.Equal(token, slice.OriginalText);
    }

    [Theory]
    [InlineData("{0}")]
    public void MachineTokenProtectionRecognizesNumericReplacementFields(string token)
    {
        IReadOnlyList<ProtectedSlice> slices =
            MarkdownTokenProtector.ScanEarlyMachineTokens($"Value {token}.");

        ProtectedSlice slice = Assert.Single(slices);
        Assert.Equal(MarkdownProtectedRangeKinds.MachineToken, slice.Kind);
        Assert.Equal(token, slice.OriginalText);
    }

    [Theory]
    [InlineData("{0:N2}")]
    [InlineData("{0:}")]
    [InlineData("{0,10}")]
    [InlineData("{0,5:}")]
    [InlineData("{0,-10:N2}")]
    [InlineData("{0:<Component />}")]
    public void MachineTokenProtectionIgnoresFormattedNumericReplacementFields(string token)
    {
        IReadOnlyList<ProtectedSlice> slices =
            MarkdownTokenProtector.ScanEarlyMachineTokens($"Value {token}.");

        Assert.Empty(slices);
    }

    [Theory]
    [InlineData("{{name}}")]
    [InlineData("{{user.name}}")]
    [InlineData("{{ name }}")]
    [InlineData("{{foo-bar}}")]
    public void MachineTokenProtectionRecognizesDoubleBraceTemplateVariablesAsSingleToken(
        string token)
    {
        IReadOnlyList<ProtectedSlice> slices =
            MarkdownTokenProtector.ScanEarlyMachineTokens($"Value {token}.");

        ProtectedSlice slice = Assert.Single(slices);
        Assert.Equal(MarkdownProtectedRangeKinds.MachineToken, slice.Kind);
        Assert.Equal(token, slice.OriginalText);
    }

    [Theory]
    [InlineData("{{}}")]
    [InlineData("{{name\r}}")]
    [InlineData("{{name\n}}")]
    public void MachineTokenProtectionIgnoresEmptyOrMultilineDoubleBraceTemplateVariables(
        string token)
    {
        IReadOnlyList<ProtectedSlice> slices =
            MarkdownTokenProtector.ScanEarlyMachineTokens($"Value {token}.");

        Assert.Empty(slices);
    }

    [Fact]
    public void MachineTokenProtectionDoesNotTreatSingleBraceIdentifiersOrExpressionsAsTokens()
    {
        IReadOnlyList<ProtectedSlice> identifierSlices =
            MarkdownTokenProtector.ScanEarlyMachineTokens("Value {foo}.");
        IReadOnlyList<ProtectedSlice> expressionSlices =
            MarkdownTokenProtector.ScanEarlyMachineTokens("Value {1 + 2}.");

        Assert.Empty(identifierSlices);
        Assert.Empty(expressionSlices);
    }

    [Theory]
    [InlineData("{１２}")]
    [InlineData("{١}")]
    public void MachineTokenProtectionDoesNotTreatUnicodeDecimalDigitsAsNumericReplacementFields(
        string token)
    {
        IReadOnlyList<ProtectedSlice> slices =
            MarkdownTokenProtector.ScanEarlyMachineTokens($"Value {token}.");

        Assert.Empty(slices);
    }

    [Fact]
    public void MachineTokenProtectionRequiresPathEvidenceAfterHyphen()
    {
        IReadOnlyList<ProtectedSlice> proseSlices =
            MarkdownTokenProtector.ScanEarlyMachineTokens("Value ${name}-specific.");
        IReadOnlyList<ProtectedSlice> extensionPathSlices =
            MarkdownTokenProtector.ScanEarlyMachineTokens("Value ${name}-specific.md.");
        IReadOnlyList<ProtectedSlice> directoryPathSlices =
            MarkdownTokenProtector.ScanEarlyMachineTokens("Value docs/${name}-specific.");

        ProtectedSlice slice = Assert.Single(proseSlices);
        Assert.Equal(MarkdownProtectedRangeKinds.MachineToken, slice.Kind);
        Assert.Equal("${name}", slice.OriginalText);
        Assert.Empty(extensionPathSlices);
        Assert.Empty(directoryPathSlices);
    }

    [Fact]
    public void MachineTokenProtectionScansLeftOfHyphenForPathEvidence()
    {
        IReadOnlyList<ProtectedSlice> proseSlices =
            MarkdownTokenProtector.ScanEarlyMachineTokens("Value ID-${id}.");
        IReadOnlyList<ProtectedSlice> pathSlices =
            MarkdownTokenProtector.ScanEarlyMachineTokens("Value docs/README-${lang}.");

        ProtectedSlice slice = Assert.Single(proseSlices);
        Assert.Equal(MarkdownProtectedRangeKinds.MachineToken, slice.Kind);
        Assert.Equal("${id}", slice.OriginalText);
        Assert.Empty(pathSlices);
    }

    [Fact]
    public void ReferenceDefinitionsMayEndWithCarriageReturnOnly()
    {
        const string markdown = "[ref]: https://example.com\r";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
        Assert.Equal(markdown, slice.OriginalText);
    }

    [Fact]
    public void ReferenceDefinitionsProtectLabelsWithEscapedClosingBracket()
    {
        const string markdown = "[a\\]]: https://example.com\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
        Assert.Equal(markdown, slice.OriginalText);
    }

    [Fact]
    public void ReferenceDefinitionsProtectAngleDestinationWithEscapedClosingBracket()
    {
        const string markdown = "[ref]: <a\\>b> \"title\"\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
        Assert.Equal(markdown, slice.OriginalText);
    }

    [Fact]
    public void ReferenceDefinitionsProtectBalancedUnbracketedDestinationParentheses()
    {
        const string markdown = "[ref]: a(b)c \"title\"\n";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
        Assert.Equal(markdown, slice.OriginalText);
    }

    [Theory]
    [InlineData("[ref]: a(b\n")]
    [InlineData("[ref]: a)b\n")]
    public void ReferenceDefinitionsRejectUnbalancedUnbracketedDestinationParentheses(
        string markdown)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
    }

    [Theory]
    [InlineData("\"Title\"", "\n")]
    [InlineData("'Title'", "\r\n")]
    [InlineData("(Title)", "\r")]
    public void ReferenceDefinitionsProtectContinuationTitleLine(string title, string lineEnding)
    {
        string markdown = string.Concat(
            "[ref]: https://example.com",
            lineEnding,
            "  ",
            title,
            lineEnding,
            "Body");
        string expected = string.Concat(
            "[ref]: https://example.com",
            lineEnding,
            "  ",
            title,
            lineEnding);
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
        Assert.Equal(expected, slice.OriginalText);
    }

    [Theory]
    [InlineData("\"", "\"", "\n")]
    [InlineData("'", "'", "\r\n")]
    [InlineData("(", ")", "\r")]
    public void ReferenceDefinitionsProtectMultilineContinuationTitle(
        string opener,
        string closer,
        string lineEnding)
    {
        string multilineTitle = string.Concat(
            opener,
            "First line",
            lineEnding,
            "second line",
            closer);
        string markdown = string.Concat(
            "[ref]: https://example.com",
            lineEnding,
            "  ",
            multilineTitle,
            lineEnding,
            "Body");
        string expected = string.Concat(
            "[ref]: https://example.com",
            lineEnding,
            "  ",
            multilineTitle,
            lineEnding);
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
        Assert.Equal(expected, slice.OriginalText);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ReferenceDefinitionsDoNotConsumeMultilineTitleAcrossWhitespaceOnlyBlankLines(
        string blankLineWhitespace)
    {
        string markdown = string.Concat(
            "[ref]: https://example.com\n",
            "  \"First line\n",
            blankLineWhitespace,
            "\n",
            "second line\"\n");
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
        Assert.Equal("[ref]: https://example.com\n", slice.OriginalText);
    }

    [Theory]
    [InlineData("\"", "\\\"", "\"")]
    [InlineData("'", "\\'", "'")]
    [InlineData("(", "\\)", ")")]
    public void ReferenceDefinitionsIgnoreEscapedMultilineTitleClosers(
        string opener,
        string escapedCloser,
        string closer)
    {
        const string lineEnding = "\n";
        string multilineTitle = string.Concat(
            opener,
            "First line ",
            escapedCloser,
            lineEnding,
            "second line",
            closer);
        string markdown = string.Concat(
            "[ref]: https://example.com",
            lineEnding,
            "  ",
            multilineTitle,
            lineEnding,
            "Body");
        string expected = string.Concat(
            "[ref]: https://example.com",
            lineEnding,
            "  ",
            multilineTitle,
            lineEnding);
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
        Assert.Equal(expected, slice.OriginalText);
    }

    [Fact]
    public void ReferenceDefinitionsMayFollowCarriageReturnOnlyLineEndings()
    {
        const string markdown = "[a]: x\r[b]: y\r";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition
                && slice.OriginalText == "[a]: x\r");
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition
                && slice.OriginalText == "[b]: y\r");
    }

    [Theory]
    [InlineData("> [ref]: https://example.com\n")]
    [InlineData("- [ref]: https://example.com\n")]
    public void ReferenceDefinitionsAreCollectedInsideContainers(string markdown)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition
                && slice.OriginalText == "[ref]: https://example.com\n");
    }

    [Theory]
    [InlineData(
        "> [ref]: https://example.com\n>   \"First line\n>   second line\"\nBody",
        "[ref]: https://example.com\n>   \"First line\n>   second line\"\n")]
    [InlineData(
        "- [ref]: https://example.com\n  \"First line\n  second line\"\nBody",
        "[ref]: https://example.com\n  \"First line\n  second line\"\n")]
    public void ReferenceDefinitionsProtectMultilineContinuationTitlesInsideContainers(
        string markdown,
        string expectedDefinition)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
        Assert.Equal(expectedDefinition, slice.OriginalText);
    }

    [Theory]
    [InlineData(
        "> [ref]: https://example.com\n\"Outside\"\n",
        "[ref]: https://example.com\n")]
    [InlineData(
        "- [ref]: https://example.com\n\"Outside\"\n",
        "[ref]: https://example.com\n")]
    public void ReferenceDefinitionsDoNotConsumeOutsideContainerContinuationTitle(
        string markdown,
        string expectedDefinition)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
        Assert.Equal(expectedDefinition, slice.OriginalText);
    }

    [Fact]
    public void ReferenceDefinitionsAfterCarriageReturnOnlyTextProtectReferenceLabel()
    {
        const string markdown = "text\r[ref]: https://example.com\r";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition
                && slice.OriginalText == "[ref]: https://example.com\r");
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == "[");
    }

    [Fact]
    public void FootnoteDefinitionsAreNotReferenceDefinitions()
    {
        const string markdown = "[^1]: footnote";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.ReferenceDefinition);
    }

    [Fact]
    public void NamedFootnoteDefinitionsProtectOnlyMarker()
    {
        const string markdown = "[^term]: footnote text";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.FootnoteDefinition);
        Assert.Equal("[^term]:", slice.OriginalText);
        Assert.DoesNotContain(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.FootnoteDefinition
                && slice.OriginalText.Contains("footnote text", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("> [^n]: note")]
    [InlineData("- [^n]: note")]
    public void FootnoteDefinitionsAreCollectedInsideContainers(string markdown)
    {
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.FootnoteDefinition
                && slice.OriginalText == "[^n]:");
    }

    [Fact]
    public void NamedFootnoteReferencesProtectMarker()
    {
        const string markdown = "Text[^term]";
        MarkdownDocumentParser parser = MarkdownDocumentParser.CreateV1();

        MarkdownParseResult result = parser.Parse(markdown);

        Assert.True(result.Succeeded);
        ProtectedSlice slice = Assert.Single(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.FootnoteReference);
        Assert.Equal("[^term]", slice.OriginalText);
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

    private static void AssertLinkOrImageStructuralDelimiterIsProtected(
        string markdown,
        MarkdownParseResult result,
        string delimiter)
    {
        int delimiterStart = delimiter switch
        {
            "!" => 0,
            "[" => markdown.StartsWith("![", StringComparison.Ordinal) ? 1 : 0,
            "]" => markdown.IndexOf("](", StringComparison.Ordinal),
            "(" => markdown.IndexOf("](", StringComparison.Ordinal) + 1,
            ")" => markdown.LastIndexOf(')'),
            _ => throw new ArgumentOutOfRangeException(nameof(delimiter)),
        };

        Assert.Contains(
            result.ProtectedSlices,
            slice => slice.Kind == MarkdownProtectedRangeKinds.MarkdownStructuralSyntax
                && slice.OriginalText == delimiter
                && slice.SourceRange.Start == delimiterStart);
    }

    private static bool RangesOverlap(TextRange left, TextRange right) =>
        left.Start < right.End && right.Start < left.End;

    private static bool RangeContains(TextRange outer, TextRange inner) =>
        inner.Start >= outer.Start && inner.End <= outer.End;

    private static void AssertInvalidUtf8ParseFailure(MarkdownParseResult result)
    {
        Assert.False(result.Succeeded);
        MarkdownDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MarkdownFailureKind.InvalidUtf8, diagnostic.Kind);
        Assert.Null(result.Document);
        Assert.Equal(string.Empty, result.SourceText);
    }
}
