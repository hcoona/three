namespace Hcoona.DocumentTranslatorCli;

internal interface IDocumentTranslator
{
    ValueTask<BinaryData> TranslateAsync(
        TranslationOptions options,
        Stream inputStream,
        CancellationToken cancellationToken);
}
