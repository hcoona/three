namespace Hcoona.DocumentTranslatorCli;

internal interface ITextSegmentTranslator
{
    ValueTask<IReadOnlyList<string>> TranslateAsync(
        TranslationOptions options,
        IReadOnlyList<TextSegmentTranslationRequest> segments,
        CancellationToken cancellationToken);
}
