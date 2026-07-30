using System.Text;

namespace Hcoona.DocumentTranslatorCli;

internal sealed class MarkdownTranslationCommand
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private readonly MarkdownDocumentParser parser;
    private readonly ITextSegmentTranslator translator;
    private readonly OutputWriter outputWriter;

    public MarkdownTranslationCommand(
        MarkdownDocumentParser parser,
        ITextSegmentTranslator translator,
        OutputWriter outputWriter)
    {
        this.parser = parser ?? throw new ArgumentNullException(nameof(parser));
        this.translator = translator ?? throw new ArgumentNullException(nameof(translator));
        this.outputWriter = outputWriter ?? throw new ArgumentNullException(nameof(outputWriter));
    }

    public async ValueTask<MarkdownTranslationCommandResult> ExecuteAsync(
        TranslationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] inputBytes;
        try
        {
            inputBytes = await File
                .ReadAllBytesAsync(options.InputPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsFileIoException(ex))
        {
            throw new MarkdownTranslationFileIoException(ex);
        }
        MarkdownParseResult parseResult = parser.Parse(inputBytes);
        if (!parseResult.Succeeded)
        {
            return MarkdownTranslationCommandResult.Failed(parseResult.Diagnostics);
        }

        MarkdownSegmentExtractionResult extractionResult = MarkdownSegmentExtractor.Extract(
            parseResult);
        if (!extractionResult.Succeeded)
        {
            return MarkdownTranslationCommandResult.Failed(extractionResult.Diagnostics);
        }

        IReadOnlyList<string> translatedTexts = extractionResult.TranslationRequests.Count == 0
            ? []
            : await translator
                .TranslateAsync(options, extractionResult.TranslationRequests, cancellationToken)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        MarkdownSourcePatchResult patchResult = MarkdownSourcePatcher.Patch(
            parseResult,
            extractionResult.Segments,
            translatedTexts);
        if (!patchResult.Succeeded)
        {
            return MarkdownTranslationCommandResult.Failed(patchResult.Diagnostics);
        }

        MarkdownOutputValidationResult validationResult = MarkdownOutputValidator.Validate(
            parseResult,
            patchResult);
        if (!validationResult.Succeeded)
        {
            return MarkdownTranslationCommandResult.Failed(validationResult.Diagnostics);
        }

        try
        {
            await outputWriter(
                    options.OutputPath,
                    CreateOutputContent(
                        validationResult.PatchedText,
                        validationResult.OutputMetadata),
                    options.Force,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsFileIoException(ex))
        {
            throw new MarkdownTranslationFileIoException(ex);
        }

        return MarkdownTranslationCommandResult.Succeeded();
    }

    private static bool IsFileIoException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or PathTooLongException
            or NotSupportedException
            or ArgumentException;

    private static BinaryData CreateOutputContent(
        string text,
        MarkdownSourceMetadata outputMetadata)
    {
        byte[] content = Encoding.UTF8.GetBytes(text);
        if (!outputMetadata.HasUtf8Bom)
        {
            return BinaryData.FromBytes(content);
        }

        byte[] contentWithBom = new byte[Utf8Bom.Length + content.Length];
        Buffer.BlockCopy(Utf8Bom, 0, contentWithBom, 0, Utf8Bom.Length);
        Buffer.BlockCopy(content, 0, contentWithBom, Utf8Bom.Length, content.Length);
        return BinaryData.FromBytes(contentWithBom);
    }
}

internal sealed class MarkdownTranslationFileIoException(Exception innerException)
    : Exception(innerException.Message, innerException);

internal sealed record MarkdownTranslationCommandResult(
    IReadOnlyList<MarkdownDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.Count == 0;

    public static MarkdownTranslationCommandResult Succeeded() => new([]);

    public static MarkdownTranslationCommandResult Failed(
        IReadOnlyList<MarkdownDiagnostic> diagnostics) =>
        new(diagnostics.Count == 0
            ? [new MarkdownDiagnostic(
                MarkdownFailureKind.ReconstructionChanged,
                "Markdown-aware translation failed.")]
            : diagnostics);
}
