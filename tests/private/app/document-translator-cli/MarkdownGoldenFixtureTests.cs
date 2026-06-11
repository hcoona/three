using System.Text;
using Xunit;

namespace Hcoona.DocumentTranslatorCli.Tests;

public sealed class MarkdownGoldenFixtureTests
{
    [Theory]
    [MemberData(nameof(GoldenFixtures))]
    public async Task MarkdownAwarePipelineProducesGoldenOutput(GoldenFixture fixture)
    {
        using TestDirectory testDirectory = TestDirectory.Create();
        string inputPath = testDirectory.WriteFileBytes("source.md", fixture.InputBytes);
        string outputPath = testDirectory.GetPath("translated.md");
        DeterministicTextSegmentTranslator translator = new();
        CapturingOutputWriter outputWriter = new();
        MarkdownTranslationCommand command = new(
            MarkdownDocumentParser.CreateV1(),
            translator,
            outputWriter.WriteAsync);
        MarkdownTranslationCommandResult result = await command.ExecuteAsync(
            CreateOptions(inputPath, outputPath),
            TestContext.Current.CancellationToken);

        AssertSucceeded(result.Diagnostics);
        BinaryData output = Assert.Single(outputWriter.Writes).Content;
        Assert.Equal(fixture.ExpectedRequests, translator.Requests);
        Assert.Equal(outputPath, Assert.Single(outputWriter.Writes).OutputPath);
        Assert.True(Assert.Single(outputWriter.Writes).Overwrite);
        Assert.Equal(fixture.ExpectedOutputBytes, output.ToArray());
        Assert.Equal(fixture.ExpectedOutputText, DecodeOutput(output.ToArray()));
    }

    public static TheoryData<GoldenFixture> GoldenFixtures() =>
        new()
        {
            HeadingParagraphsAndSetext(),
            EmphasisStrongAndStrikethrough(),
            ListsTasksBlockquotesAndNestedContainers(),
            LinksImagesReferencesAndDefinitions(),
            ShortcutAndCollapsedReferenceLabels(),
            CodeSpansAndFences(),
            Tables(),
            FrontMatter(),
            ProtectedHtml(),
            AutolinksBareUrlsAndEmails(),
            Footnotes(),
            BomFinalNewlineAndCrlf(),
            MixedLineEndings(),
            MachineLookingProse(),
            ThematicBreaks(),
            SoftAndHardLineBreaks(),
            EscapedDelimiters(),
            IndentedCode(),
            HtmlComments(),
            UriFragments(),
            AlignedTables(),
            ProtectedOnlyZeroSegmentMarkdown(),
            MdxJsxLookingText(),
            ImportExportLookingText(),
            Directives(),
            CustomAdmonitions(),
            TomlLookingFrontMatter(),
        };

    private static GoldenFixture HeadingParagraphsAndSetext() =>
        GoldenFixture.Utf8(
            "headings, paragraphs, and setext",
            "# Heading\n\nParagraph one.\n\nSetext heading\n===============\n",
            "# TRANSLATED[0] Heading\n\n"
                + "TRANSLATED[1] Paragraph one.\n\n"
                + "TRANSLATED[2] Setext heading\n===============\n",
            ["Heading", "Paragraph one.", "Setext heading"]);

    private static GoldenFixture EmphasisStrongAndStrikethrough() =>
        GoldenFixture.Utf8(
            "emphasis, strong, and strikethrough",
            "*Emphasis*\n\n**Strong**\n\n~~Strike~~\n",
            "*TRANSLATED[0] Emphasis*\n\n**TRANSLATED[1] Strong**\n\n~~TRANSLATED[2] Strike~~\n",
            ["Emphasis", "Strong", "Strike"]);

    private static GoldenFixture ListsTasksBlockquotesAndNestedContainers() =>
        GoldenFixture.Utf8(
            "lists, task lists, blockquotes, and nested containers",
            """
            - [x] Done task
            - [ ] Open task
            - Item one
              - Nested item

            > Quote line
            >
            > - Quoted item
            """,
            """
            - [x] TRANSLATED[0] Done task
            - [ ] TRANSLATED[1] Open task
            - TRANSLATED[2] Item one
              - TRANSLATED[3] Nested item

            > TRANSLATED[4] Quote line
            >
            > - TRANSLATED[5] Quoted item
            """,
            ["Done task", "Open task", "Item one", "Nested item", "Quote line", "Quoted item"]);

    private static GoldenFixture LinksImagesReferencesAndDefinitions() =>
        GoldenFixture.Utf8(
            "links, images, reference links, and reference definitions",
            """
            [Link text](https://example.com "Title") and ![Alt text](image.png).

            ![Titled alt](image.png "Image title")

            [Reference link][ref]

            ![Reference alt][imgref]

            [ref]: https://example.org "Reference title"
            [imgref]: images/reference.png "Reference image title"
            """,
            "[TRANSLATED[0] Link text](https://example.com \"Title\")TRANSLATED[1]  and "
                + "![TRANSLATED[2] Alt text](image.png)TRANSLATED[3] .\n\n"
                + "![TRANSLATED[4] Titled alt](image.png \"Image title\")\n\n"
                + "[TRANSLATED[5] Reference link][ref]\n\n"
                + "![TRANSLATED[6] Reference alt][imgref]\n\n"
                + "[ref]: https://example.org \"Reference title\"\n"
                + "[imgref]: images/reference.png \"Reference image title\"",
            [
                "Link text",
                " and ",
                "Alt text",
                ".",
                "Titled alt",
                "Reference link",
                "Reference alt",
            ]);

    private static GoldenFixture ShortcutAndCollapsedReferenceLabels() =>
        GoldenFixture.Utf8(
            "shortcut and collapsed reference labels",
            """
            [id]

            [id][]

            ![img]

            ![img][]

            [id]: https://example.org
            [img]: image.png
            """,
            """
            [id]

            [id][]

            ![img]

            ![img][]

            [id]: https://example.org
            [img]: image.png
            """,
            []);

    private static GoldenFixture CodeSpansAndFences() =>
        GoldenFixture.Utf8(
            "code spans and fenced code blocks",
            """
            Before `inline code` after.

            ```csharp
            Console.WriteLine("hi");
            ```

            After fence.
            """,
            """
            TRANSLATED[0] Before `inline code`TRANSLATED[1]  after.

            ```csharp
            Console.WriteLine("hi");
            ```

            TRANSLATED[2] After fence.
            """,
            ["Before ", " after.", "After fence."]);

    private static GoldenFixture Tables() =>
        GoldenFixture.Utf8(
            "tables",
            """
            | Left | Right |
            | --- | --- |
            | Cell one | **Cell two** |
            """,
            """
            | TRANSLATED[0] Left | TRANSLATED[1] Right |
            | --- | --- |
            | TRANSLATED[2] Cell one | **TRANSLATED[3] Cell two** |
            """,
            ["Left", "Right", "Cell one", "Cell two"]);

    private static GoldenFixture FrontMatter() =>
        GoldenFixture.Utf8(
            "front matter",
            """
            ---
            title: Sample
            tags:
              - one
            ---

            Body paragraph.
            """,
            """
            ---
            title: Sample
            tags:
              - one
            ---

            TRANSLATED[0] Body paragraph.
            """,
            ["Body paragraph."]);

    private static GoldenFixture ProtectedHtml() =>
        GoldenFixture.Utf8(
            "raw block and inline HTML protected cases",
            """
            Before <span>inline</span> after.

            <div>
            block html
            </div>

            After block.
            """,
            """
            TRANSLATED[0] Before <span>inline</span>TRANSLATED[1]  after.

            <div>
            block html
            </div>

            TRANSLATED[2] After block.
            """,
            ["Before ", " after.", "After block."]);

    private static GoldenFixture AutolinksBareUrlsAndEmails() =>
        GoldenFixture.Utf8(
            "autolinks, bare URLs, and emails",
            """
            <https://example.com>

            https://example.org/path

            <user@example.com>

            user@example.org

            Contact links.
            """,
            """
            <https://example.com>

            https://example.org/path

            <user@example.com>

            user@example.org

            TRANSLATED[0] Contact links.
            """,
            ["Contact links."]);

    private static GoldenFixture Footnotes() =>
        GoldenFixture.Utf8(
            "footnotes",
            "Footnote reference[^1].\n\n[^1]: Footnote definition text.\n",
            "TRANSLATED[0] Footnote reference[^1]TRANSLATED[1] .\n\n"
                + "[^1]: TRANSLATED[2] Footnote definition text.\n",
            ["Footnote reference", ".", "Footnote definition text."]);

    private static GoldenFixture BomFinalNewlineAndCrlf() =>
        GoldenFixture.Utf8WithBom(
            "BOM, final newline, and CRLF line endings",
            "# Heading\r\n\r\nParagraph.\r\n",
            "# TRANSLATED[0] Heading\r\n\r\nTRANSLATED[1] Paragraph.\r\n",
            ["Heading", "Paragraph."]);

    private static GoldenFixture MixedLineEndings() =>
        GoldenFixture.Utf8PreservingLineEndings(
            "mixed CRLF and LF line endings",
            "# Heading\r\n\r\n"
                + "First paragraph line.\n"
                + "Second paragraph line.\r\n\r\n"
                + "Final paragraph.\n",
            "# TRANSLATED[0] Heading\r\n\r\n"
                + "TRANSLATED[1] First paragraph line.\n"
                + "TRANSLATED[2] Second paragraph line.\r\n\r\n"
                + "TRANSLATED[3] Final paragraph.\n",
            ["Heading", "First paragraph line.", "Second paragraph line.", "Final paragraph."]);

    private static GoldenFixture MachineLookingProse() =>
        GoldenFixture.Utf8(
            "machine-looking prose has no special handling",
            "Use ${name}, {{name}}, {0}, package.name, ./path/file.txt, and --flag.\n",
            "TRANSLATED[0] Use ${name}, {{name}}, {0}, "
                + "package.name, ./path/file.txt, and --flag.\n",
            [
                "Use ${name}, {{name}}, {0}, package.name, ./path/file.txt, and --flag.",
            ]);

    private static GoldenFixture ThematicBreaks() =>
        GoldenFixture.Utf8(
            "thematic breaks",
            "Before break.\n\n---\n\nAfter break.\n",
            "TRANSLATED[0] Before break.\n\n---\n\nTRANSLATED[1] After break.\n",
            ["Before break.", "After break."]);

    private static GoldenFixture SoftAndHardLineBreaks() =>
        GoldenFixture.Utf8(
            "soft and hard line breaks",
            "Soft line one\n"
                + "soft line two\n\n"
                + "Hard spaces  \n"
                + "next line\n\n"
                + "Hard slash\\\n"
                + "next line\n",
            "TRANSLATED[0] Soft line one\n"
                + "TRANSLATED[1] soft line two\n\n"
                + "TRANSLATED[2] Hard spaces  \n"
                + "TRANSLATED[3] next line\n\n"
                + "TRANSLATED[4] Hard slash\\\n"
                + "TRANSLATED[5] next line\n",
            [
                "Soft line one",
                "soft line two",
                "Hard spaces",
                "next line",
                "Hard slash",
                "next line",
            ]);

    private static GoldenFixture EscapedDelimiters() =>
        GoldenFixture.Utf8(
            "escaped delimiters",
            @"\*Not emphasis\* and \[not link\] plus \# not heading.",
            @"\*TRANSLATED[0] Not emphasis\*TRANSLATED[1]  and "
                + @"\[TRANSLATED[2] not link\]TRANSLATED[3]  plus "
                + @"\#TRANSLATED[4]  not heading.",
            ["Not emphasis", " and ", "not link", " plus ", " not heading."]);

    private static GoldenFixture IndentedCode() =>
        GoldenFixture.Utf8(
            "indented code",
            "Before code.\n\n    Console.WriteLine(\"hi\");\n\nAfter code.\n",
            "TRANSLATED[0] Before code.\n\n"
                + "    Console.WriteLine(\"hi\");\n\n"
                + "TRANSLATED[1] After code.\n",
            ["Before code.", "After code."]);

    private static GoldenFixture HtmlComments() =>
        GoldenFixture.Utf8(
            "HTML comments",
            "Before <!-- inline note --> after.\n\n<!-- block note -->\n\nAfter block.\n",
            "TRANSLATED[0] Before <!-- inline note -->TRANSLATED[1]  after.\n\n"
                + "<!-- block note -->\n\n"
                + "TRANSLATED[2] After block.\n",
            ["Before ", " after.", "After block."]);

    private static GoldenFixture UriFragments() =>
        GoldenFixture.Utf8(
            "URI fragments",
            "See #section-${id}\n\n[details](guide.md#details).\n",
            "TRANSLATED[0] See #section-${id}\n\n"
                + "[TRANSLATED[1] details](guide.md#details)TRANSLATED[2] .\n",
            ["See ", "details", "."]);

    private static GoldenFixture AlignedTables() =>
        GoldenFixture.Utf8(
            "aligned tables",
            """
            | Left | Center | Right |
            | :--- | :---: | ---: |
            | One | Two | Three |
            """,
            """
            | TRANSLATED[0] Left | TRANSLATED[1] Center | TRANSLATED[2] Right |
            | :--- | :---: | ---: |
            | TRANSLATED[3] One | TRANSLATED[4] Two | TRANSLATED[5] Three |
            """,
            ["Left", "Center", "Right", "One", "Two", "Three"]);

    private static GoldenFixture ProtectedOnlyZeroSegmentMarkdown() =>
        GoldenFixture.Utf8(
            "protected-only zero-segment Markdown",
            """
            ---
            title: Sample
            ---

            <!-- protected comment -->

            ```text
            protected fence
            ```
            """,
            """
            ---
            title: Sample
            ---

            <!-- protected comment -->

            ```text
            protected fence
            ```
            """,
            []);

    private static GoldenFixture MdxJsxLookingText() =>
        GoldenFixture.Utf8(
            "MDX/JSX-looking text is accepted",
            "Use <Component prop={value} /> in MDX-looking prose.\n",
            "TRANSLATED[0] Use <Component prop={value} />TRANSLATED[1]  in MDX-looking prose.\n",
            ["Use ", " in MDX-looking prose."]);

    private static GoldenFixture ImportExportLookingText() =>
        GoldenFixture.Utf8(
            "import/export-looking text is accepted",
            "import Button from './Button';\n\nexport const value = 1;\n",
            "TRANSLATED[0] import Button from './Button';\n\n"
                + "TRANSLATED[1] export const value = 1;\n",
            ["import Button from './Button';", "export const value = 1;"]);

    private static GoldenFixture Directives() =>
        GoldenFixture.Utf8(
            "directives are accepted",
            "::note\nDirective body\n::\n",
            "TRANSLATED[0] ::note\nTRANSLATED[1] Directive body\nTRANSLATED[2] ::\n",
            ["::note", "Directive body", "::"]);

    private static GoldenFixture CustomAdmonitions() =>
        GoldenFixture.Utf8(
            "custom admonitions are accepted",
            "!!! note \"Title\"\n    Admonition body.\n",
            "TRANSLATED[0] !!! note \"Title\"\n    TRANSLATED[1] Admonition body.\n",
            ["!!! note \"Title\"", "Admonition body."]);

    private static GoldenFixture TomlLookingFrontMatter() =>
        GoldenFixture.Utf8(
            "TOML-looking front matter is accepted",
            "+++\ntitle = \"Sample\"\n+++\n\nBody paragraph.\n",
            "TRANSLATED[0] +++\nTRANSLATED[1] title = \"Sample\"\n"
                + "TRANSLATED[2] +++\n\nTRANSLATED[3] Body paragraph.\n",
            ["+++", "title = \"Sample\"", "+++", "Body paragraph."]);

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

    private static string DecodeOutput(byte[] bytes)
    {
        ReadOnlySpan<byte> utf8Bom = [0xEF, 0xBB, 0xBF];
        return bytes.AsSpan().StartsWith(utf8Bom)
            ? Encoding.UTF8.GetString(bytes.AsSpan(utf8Bom.Length))
            : Encoding.UTF8.GetString(bytes);
    }

    private static void AssertSucceeded(IReadOnlyList<MarkdownDiagnostic> diagnostics) =>
        Assert.Empty(diagnostics);

    public sealed record GoldenFixture(
        string Name,
        byte[] InputBytes,
        string ExpectedOutputText,
        byte[] ExpectedOutputBytes,
        string[] ExpectedRequests)
    {
        public override string ToString() => Name;

        public static GoldenFixture Utf8(
            string name,
            string inputText,
            string expectedOutputText,
            string[] expectedRequests) =>
            new(
                name,
                Encoding.UTF8.GetBytes(NormalizeDefaultFixtureLineEndings(inputText)),
                NormalizeDefaultFixtureLineEndings(expectedOutputText),
                Encoding.UTF8.GetBytes(NormalizeDefaultFixtureLineEndings(expectedOutputText)),
                expectedRequests);

        public static GoldenFixture Utf8PreservingLineEndings(
            string name,
            string inputText,
            string expectedOutputText,
            string[] expectedRequests) =>
            new(
                name,
                Encoding.UTF8.GetBytes(inputText),
                expectedOutputText,
                Encoding.UTF8.GetBytes(expectedOutputText),
                expectedRequests);

        public static GoldenFixture Utf8WithBom(
            string name,
            string inputText,
            string expectedOutputText,
            string[] expectedRequests) =>
            new(
                name,
                WithBom(inputText),
                expectedOutputText,
                WithBom(expectedOutputText),
                expectedRequests);

        private static string NormalizeDefaultFixtureLineEndings(string text) =>
            text
                .ReplaceLineEndings("\n");

        private static byte[] WithBom(string text)
        {
            byte[] content = Encoding.UTF8.GetBytes(text);
            byte[] bytes = new byte[3 + content.Length];
            bytes[0] = 0xEF;
            bytes[1] = 0xBB;
            bytes[2] = 0xBF;
            Buffer.BlockCopy(content, 0, bytes, 3, content.Length);
            return bytes;
        }
    }

    private sealed class DeterministicTextSegmentTranslator : ITextSegmentTranslator
    {
        public List<string> Requests { get; } = [];

        public ValueTask<IReadOnlyList<string>> TranslateAsync(
            TranslationOptions options,
            IReadOnlyList<TextSegmentTranslationRequest> segments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.AddRange(segments.Select(static segment => segment.Text));
            IReadOnlyList<string> translatedTexts = segments
                .Select(static segment => Translate(segment.SegmentIndex, segment.Text))
                .ToArray();
            return new ValueTask<IReadOnlyList<string>>(translatedTexts);
        }

        private static string Translate(int segmentIndex, string text) =>
            $"TRANSLATED[{segmentIndex}] {text}";
    }

    private sealed class CapturingOutputWriter
    {
        public List<OutputWrite> Writes { get; } = [];

        public ValueTask WriteAsync(
            string outputPath,
            BinaryData content,
            bool overwrite,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes.Add(new OutputWrite(outputPath, content, overwrite));
            return ValueTask.CompletedTask;
        }
    }

    private sealed record OutputWrite(string OutputPath, BinaryData Content, bool Overwrite);

    private sealed class TestDirectory : IDisposable
    {
        private TestDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TestDirectory Create()
        {
            string path = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "markdown-golden-fixture-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TestDirectory(path);
        }

        public string GetPath(string relativePath) => System.IO.Path.Combine(Path, relativePath);

        public string WriteFileBytes(string relativePath, byte[] content)
        {
            string path = GetPath(relativePath);
            string? directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

}
