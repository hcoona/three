using Xunit;

namespace Hcoona.DocumentTranslatorCli.Tests;

public sealed class MarkdownOutputValidatorTests
{
    [Fact]
    public void PatchedOutputStartingWithJsonGuardFailsReparseValidation()
    {
        MarkdownParseResult parsed = Parse("Hello");
        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(
            parsed,
            "{\"title\":true}",
            parsed.SourceMetadata,
            [new SourcePatchMap(0, new TextRange(0, 5), new TextRange(0, 14), 9)]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind == MarkdownFailureKind.UnsupportedSyntax);
    }

    [Fact]
    public void PatchedOutputWithInvalidSurrogateFailsReparseValidation()
    {
        MarkdownParseResult parsed = Parse("Hello");
        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(
            parsed,
            "Hello \ud800",
            parsed.SourceMetadata,
            [new SourcePatchMap(0, new TextRange(0, 5), new TextRange(0, 7), 2)]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind == MarkdownFailureKind.InvalidUtf8);
    }

    [Theory]
    [MemberData(nameof(StructuralMutations))]
    public void StructuralMutationsFailValidation(string source, string patched)
    {
        MarkdownParseResult parsed = Parse(source);

        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(
            parsed,
            patched,
            parsed.SourceMetadata,
            []);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind is MarkdownFailureKind.StructuralChanged
                or MarkdownFailureKind.ReconstructionChanged);
    }

    [Fact]
    public void TranslatedTextContentChangesAreAllowed()
    {
        const string source = """
            # Heading text

            Paragraph with [link text](https://example.com) and ![alt text](image.png).

            - Item text

            > Quote text

            | Left | Right |
            | ---- | ----- |
            | Cell one | **Cell two** |
            """;

        MarkdownOutputValidationResult result = TranslateAndValidate(
            source,
            static text => text.Length > 0 && text[0] == ' '
                ? $"{text}Translated"
                : $"Translated {text}");

        Assert.True(
            result.Succeeded,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
    }

    [Fact]
    public void BracketedFakeTranslatorMarkersAreAllowed()
    {
        MarkdownParseResult parsed = Parse("# Heading\n\nParagraph.\n");
        MarkdownSegmentExtractionResult extracted = MarkdownSegmentExtractor.Extract(parsed);
        Assert.True(extracted.Succeeded);
        MarkdownSourcePatchResult patched = MarkdownSourcePatcher.Patch(
            parsed.SourceText,
            parsed.SourceMetadata,
            extracted.Segments,
            extracted.Segments
                .Select(
                    static segment =>
                        $"TRANSLATED[{segment.SegmentIndex}] {segment.OriginalText}")
                .ToArray());

        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(parsed, patched);

        Assert.True(
            result.Succeeded,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
    }

    [Fact]
    public async Task MarkdownAwareCommandRejectsTranslatedEmptyInlineLink()
    {
        string workDirectory = CreateWorkDirectory();
        try
        {
            string inputPath = Path.Combine(workDirectory, "source.md");
            string outputPath = Path.Combine(workDirectory, "translated.md");
            await File.WriteAllTextAsync(
                inputPath,
                "Plain prose only.",
                TestContext.Current.CancellationToken);

            bool outputWritten = false;
            MarkdownTranslationCommand command = new(
                MarkdownDocumentParser.CreateV1(),
                new FixedTextSegmentTranslator("[text]()"),
                (_, _, _, _) =>
                {
                    outputWritten = true;
                    return ValueTask.CompletedTask;
                });

            MarkdownTranslationCommandResult result = await command.ExecuteAsync(
                CreateOptions(inputPath, outputPath),
                TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.False(outputWritten);
            Assert.Contains(result.Diagnostics, static diagnostic =>
                diagnostic.Kind is MarkdownFailureKind.StructuralChanged
                    or MarkdownFailureKind.ReconstructionChanged);
        }
        finally
        {
            DeleteDirectoryBestEffort(workDirectory);
        }
    }

    [Theory]
    [InlineData("Alphabet", 8, 9, 14)]
    [InlineData("A", 1, 2, 7)]
    public void SuccessfulValidationOutputMetadataUsesPatchedLineEndingOffsets(
        string translatedFirstParagraph,
        int firstLineEndingOffset,
        int secondLineEndingOffset,
        int thirdLineEndingOffset)
    {
        MarkdownOutputValidationResult result = TranslateAndValidate(
            "Alpha\n\nBeta\n",
            text => text == "Alpha" ? translatedFirstParagraph : text);

        Assert.True(
            result.Succeeded,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Collection(
            result.OutputMetadata.LineEndings,
            lineEnding => Assert.Equal(
                new TextRange(firstLineEndingOffset, 1),
                lineEnding.SourceRange),
            lineEnding => Assert.Equal(
                new TextRange(secondLineEndingOffset, 1),
                lineEnding.SourceRange),
            lineEnding => Assert.Equal(
                new TextRange(thirdLineEndingOffset, 1),
                lineEnding.SourceRange));
    }

    [Fact]
    public void BomMetadataPreservedByPatchMetadataSucceedsWhenReparseSeesBomlessText()
    {
        MarkdownParseResult parsed = Parse("\uFEFFHello");
        MarkdownSegmentExtractionResult extracted = MarkdownSegmentExtractor.Extract(parsed);
        Assert.True(extracted.Succeeded);
        MarkdownSourcePatchResult patched = MarkdownSourcePatcher.Patch(
            parsed,
            extracted.Segments,
            ["Bonjour"]);

        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(parsed, patched);

        Assert.True(
            result.Succeeded,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.True(parsed.SourceMetadata.HasUtf8Bom);
        Assert.True(patched.SourceMetadata.HasUtf8Bom);
        Assert.True(result.OutputMetadata.HasUtf8Bom);
        Assert.False(result.PatchedParseResult!.SourceMetadata.HasUtf8Bom);
    }

    [Fact]
    public void NoBomMetadataPreservedInValidationResult()
    {
        MarkdownOutputValidationResult result = TranslateAndValidate("Hello", _ => "Bonjour");

        Assert.True(
            result.Succeeded,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.False(result.OutputMetadata.HasUtf8Bom);
        Assert.False(result.PatchedParseResult!.SourceMetadata.HasUtf8Bom);
    }

    [Fact]
    public void InjectedLeadingBomInFirstTranslationSegmentFails()
    {
        MarkdownParseResult parsed = Parse("Hello");
        MarkdownSegmentExtractionResult extracted = MarkdownSegmentExtractor.Extract(parsed);
        Assert.True(extracted.Succeeded);
        MarkdownSourcePatchResult patched = MarkdownSourcePatcher.Patch(
            parsed,
            extracted.Segments,
            ["\uFEFFBonjour"]);

        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(parsed, patched);

        Assert.False(result.Succeeded);
        Assert.False(parsed.SourceMetadata.HasUtf8Bom);
        Assert.False(patched.SourceMetadata.HasUtf8Bom);
        Assert.False(result.OutputMetadata.HasUtf8Bom);
        Assert.True(result.PatchedParseResult!.SourceMetadata.HasUtf8Bom);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("BOM", StringComparison.Ordinal));
    }

    [Fact]
    public void PreservedProtectedSliceAfterLengthChangingEarlierPatchSucceeds()
    {
        const string source = "Alpha `code` beta";

        MarkdownOutputValidationResult result = TranslateAndValidate(
            source,
            text => text == "Alpha " ? "Longer Alpha " : text.ToUpperInvariant());

        Assert.True(result.Succeeded);
        Assert.Contains("`code`", result.PatchedText, StringComparison.Ordinal);
    }

    [Fact]
    public void SameRangeDifferentKindProtectedSlicesAfterLengthChangingEarlierPatchSucceeds()
    {
        const string source = "Alpha [x](https://example.com) beta";
        MarkdownParseResult parsed = Parse(source);
        Assert.Contains(
            parsed.ProtectedSlices.GroupBy(static slice => (slice.SourceRange, slice.OriginalText)),
            static group => group.Select(static slice => slice.Kind).Distinct().Count() > 1);

        MarkdownOutputValidationResult result = TranslateAndValidate(
            source,
            static text => text == "Alpha " ? "Longer Alpha " : text);

        Assert.True(
            result.Succeeded,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Contains("https://example.com", result.PatchedText, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedProtectedSliceAfterLengthChangingEarlierPatchFails()
    {
        const string source = "Alpha `code` beta";
        MarkdownParseResult parsed = Parse(source);
        MarkdownSegmentExtractionResult extracted = MarkdownSegmentExtractor.Extract(parsed);
        Assert.True(extracted.Succeeded);
        MarkdownSourcePatchResult patched = MarkdownSourcePatcher.Patch(
            parsed.SourceText,
            parsed.SourceMetadata,
            extracted.Segments,
            extracted.Segments
                .Select(static segment => segment.OriginalText == "Alpha "
                    ? "Longer Alpha "
                    : segment.OriginalText)
                .ToArray());
        string tampered = patched.PatchedText.Replace("`code`", "`CODE`", StringComparison.Ordinal);

        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(
            parsed,
            tampered,
            patched.SourceMetadata,
            patched.PatchMaps);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind == MarkdownFailureKind.ReconstructionChanged);
    }

    [Fact]
    public void BomMetadataMismatchFails()
    {
        MarkdownParseResult parsed = Parse("\uFEFFHello");

        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(
            parsed,
            "Hello",
            parsed.SourceMetadata with { HasUtf8Bom = false },
            []);

        Assert.False(result.Succeeded);
        Assert.False(result.OutputMetadata.HasUtf8Bom);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("BOM", StringComparison.Ordinal));
    }

    [Fact]
    public void FinalNewlineMismatchFails()
    {
        MarkdownParseResult parsed = Parse("Hello\n");

        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(
            parsed,
            "Hello",
            parsed.SourceMetadata,
            []);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("final newline", StringComparison.Ordinal));
    }

    [Fact]
    public void LineEndingMismatchOutsideTranslatedTextFails()
    {
        MarkdownParseResult parsed = Parse("Hello\r\nWorld\n");

        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(
            parsed,
            "Hello\nWorld\n",
            parsed.SourceMetadata,
            []);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("line ending", StringComparison.Ordinal));
    }

    [Fact]
    public void LineEndingMetadataMismatchWithUnchangedTextFails()
    {
        MarkdownParseResult parsed = Parse("Hello\r\nWorld\n");
        MarkdownSourceMetadata mismatchedMetadata = parsed.SourceMetadata with
        {
            LineEndings =
            [
                new MarkdownLineEnding(parsed.SourceMetadata.LineEndings[0].SourceRange, "\n"),
                parsed.SourceMetadata.LineEndings[1],
            ],
        };

        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(
            parsed,
            parsed.SourceText,
            mismatchedMetadata,
            []);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("line ending metadata", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtraTranslatedNewlineWithPreservedSourceLineEndingsFails()
    {
        MarkdownParseResult parsed = Parse("Alpha\nBeta\n");

        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(
            parsed,
            "Alpha\nextra\nBeta\n",
            parsed.SourceMetadata,
            [new SourcePatchMap(0, new TextRange(0, 5), new TextRange(0, 11), 6)]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("line endings", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Visit https://example.com now.")]
    [InlineData("Contact user@example.com now.")]
    [InlineData("See #anchor now.")]
    [InlineData("Visit <https://example.com> now.")]
    [InlineData("[text]()")]
    public void TranslatedTextIntroducingProtectedLiteralFails(string translated)
    {
        MarkdownOutputValidationResult result = TranslateAndValidate(
            "Plain prose only.",
            _ => translated);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind == MarkdownFailureKind.ReconstructionChanged);
    }

    [Fact]
    public void ValidationBoundarySlicesAreNotProtectedByteSources()
    {
        MarkdownParseResult parsed = Parse("Hello World");
        MarkdownParseResult boundaryOnly = parsed with
        {
            ProtectedSlices = [],
            ValidationBoundarySlices =
            [
                new ProtectedSlice(
                    "validation-only-0",
                    "validation-only",
                    new TextRange(6, 5),
                    "World"),
            ],
        };

        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(
            boundaryOnly,
            "Hello There",
            parsed.SourceMetadata,
            []);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void MachineLookingProseHasNoSpecialOutputValidationHandling()
    {
        const string source = "Use --flag, ${name}, {0}, package.name, and ./path/file.txt.";

        MarkdownOutputValidationResult result = TranslateAndValidate(
            source,
            _ => "Keep --changed, ${other}, {1}, other.package, and ./other/file.txt.");

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(
        "[id]\n\n[id]: https://example.com \"same\"\n[fr]: https://example.com \"same\"\n",
        "[fr]\n\n[id]: https://example.com \"same\"\n[fr]: https://example.com \"same\"\n",
        1)]
    [InlineData(
        "![id]\n\n[id]: image.png \"same\"\n[fr]: image.png \"same\"\n",
        "![fr]\n\n[id]: image.png \"same\"\n[fr]: image.png \"same\"\n",
        2)]
    public void ShortcutReferenceLabelsChangingDestinationKeyFailValidation(
        string source,
        string patched,
        int labelStart)
    {
        MarkdownParseResult parsed = Parse(source);

        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(
            parsed,
            patched,
            parsed.SourceMetadata,
            [new SourcePatchMap(0, new TextRange(labelStart, 2), new TextRange(labelStart, 2), 0)]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind == MarkdownFailureKind.StructuralChanged);
    }

    [Theory]
    [InlineData(
        "[id][]\n\n[id]: https://example.com \"same\"\n[fr]: https://example.com \"same\"\n",
        "[fr][]\n\n[id]: https://example.com \"same\"\n[fr]: https://example.com \"same\"\n",
        1)]
    [InlineData(
        "![id][]\n\n[id]: image.png \"same\"\n[fr]: image.png \"same\"\n",
        "![fr][]\n\n[id]: image.png \"same\"\n[fr]: image.png \"same\"\n",
        2)]
    public void CollapsedReferenceLabelsChangingDestinationKeyFailValidation(
        string source,
        string patched,
        int labelStart)
    {
        MarkdownParseResult parsed = Parse(source);

        MarkdownOutputValidationResult result = MarkdownOutputValidator.Validate(
            parsed,
            patched,
            parsed.SourceMetadata,
            [new SourcePatchMap(0, new TextRange(labelStart, 2), new TextRange(labelStart, 2), 0)]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Kind is MarkdownFailureKind.StructuralChanged
                or MarkdownFailureKind.ReconstructionChanged);
    }

    [Theory]
    [InlineData(
        "[id] Outro\n\n[id]: https://example.com\n",
        "[id] Bonjour\n\n[id]: https://example.com\n")]
    [InlineData(
        "[id][] Outro\n\n[id]: https://example.com\n",
        "[id][] Bonjour\n\n[id]: https://example.com\n")]
    [InlineData(
        "![id] Outro\n\n[id]: image.png\n",
        "![id] Bonjour\n\n[id]: image.png\n")]
    [InlineData(
        "![id][] Outro\n\n[id]: image.png\n",
        "![id][] Bonjour\n\n[id]: image.png\n")]
    public void ShortcutAndCollapsedReferenceLabelsCanPassWhenLabelTextIsPreserved(
        string source,
        string expectedPatchedText)
    {
        MarkdownOutputValidationResult result = TranslateAndValidate(
            source,
            static text => text == " Outro" ? " Bonjour" : text);

        Assert.True(
            result.Succeeded,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Equal(expectedPatchedText, result.PatchedText);
    }

    [Theory]
    [InlineData(
        "[visible][id][]\n\n[id]: https://example.com\n",
        "[translated][id][]\n\n[id]: https://example.com\n")]
    [InlineData(
        "![visible][img][]\n\n[img]: image.png\n",
        "![translated][img][]\n\n[img]: image.png\n")]
    public void FullReferenceVisibleTextCanChangeBeforeLiteralEmptyBrackets(
        string source,
        string expectedPatchedText)
    {
        MarkdownOutputValidationResult result = TranslateAndValidate(
            source,
            static text => text == "visible" ? "translated" : text);

        Assert.True(
            result.Succeeded,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Equal(expectedPatchedText, result.PatchedText);
    }

    public static TheoryData<string, string> StructuralMutations() =>
        new()
        {
            { "# Title\n", "## Title\n" },
            { "- Item\n", "1. Item\n" },
            { "| A | B |\n| - | - |\n| 1 | 2 |\n", "| A |\n| - |\n| 1 |\n" },
            {
                "[Text](https://example.com \"title\")\n",
                "[Text](https://example.net \"title\")\n"
            },
            {
                "[Text](https://example.com \"title\")\n",
                "[Text](https://example.com \"other\")\n"
            },
            { "![Alt](image.png \"title\")\n", "![Alt](other.png \"title\")\n" },
            { "![Alt](image.png \"title\")\n", "![Alt](image.png \"other\")\n" },
            {
                "[ref]: https://example.com \"title\"\n\n[Text][ref]\n",
                "[ref]: https://example.net \"title\"\n\n[Text][ref]\n"
            },
            {
                "[ref]: https://example.com \"title\"\n\n[Text][ref]\n",
                "[ref]: https://example.com \"other\"\n\n[Text][ref]\n"
            },
            { "```csharp\ncode\n```\n", "```text\ncode\n```\n" },
            { "---\ntitle: Old\n---\n\nText\n", "---\ntitle: New\n---\n\nText\n" },
            { "<div class=\"a\">raw</div>\n\nText\n", "<div class=\"b\">raw</div>\n\nText\n" },
            { "- [ ] Task\n", "- [x] Task\n" },
            { "Text[^a]\n\n[^a]: Note\n", "Text[^b]\n\n[^b]: Note\n" },
            { "*emphasis* text\n", "_emphasis_ text\n" },
            { "*emphasis* text\n", "* emphasis* text\n" },
            { "**strong** text\n", "__strong__ text\n" },
            { "~~strike~~ text\n", "~strike~ text\n" },
            { "<https://example.com> text\n", "https://example.com text\n" },
            { "<span>text</span>\n", "<strong>text</strong>\n" },
            { "`code` text\n", "``code`` text\n" },
        };

    private static MarkdownOutputValidationResult TranslateAndValidate(
        string source,
        Func<string, string> translate)
    {
        MarkdownParseResult parsed = Parse(source);
        MarkdownSegmentExtractionResult extracted = MarkdownSegmentExtractor.Extract(parsed);
        Assert.True(extracted.Succeeded);
        MarkdownSourcePatchResult patched = MarkdownSourcePatcher.Patch(
            parsed.SourceText,
            parsed.SourceMetadata,
            extracted.Segments,
            extracted.Segments.Select(segment => translate(segment.OriginalText)).ToArray());
        Assert.True(patched.Succeeded);
        return MarkdownOutputValidator.Validate(parsed, patched);
    }

    private static MarkdownParseResult Parse(string source)
    {
        MarkdownParseResult parsed = MarkdownDocumentParser.CreateV1().Parse(source);
        Assert.True(parsed.Succeeded);
        return parsed;
    }

    private static TranslationOptions CreateOptions(string inputPath, string outputPath) =>
        new(
            inputPath,
            outputPath,
            "fr",
            new Uri("https://resource.cognitiveservices.azure.com"),
            AuthMode.ApiKey,
            "not-used",
            MarkdownMode.Aware,
            TranslationRoute.MarkdownAware,
            IsMarkdownExtension: true,
            Force: true,
            OriginalFileName: Path.GetFileName(inputPath),
            LegacyDocumentContentType: null);

    private static string CreateWorkDirectory()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            nameof(MarkdownOutputValidatorTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class FixedTextSegmentTranslator(string translatedText) : ITextSegmentTranslator
    {
        public ValueTask<IReadOnlyList<string>> TranslateAsync(
            TranslationOptions options,
            IReadOnlyList<TextSegmentTranslationRequest> segments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<string> translatedTexts = segments
                .Select(_ => translatedText)
                .ToArray();
            return new ValueTask<IReadOnlyList<string>>(translatedTexts);
        }
    }
}
