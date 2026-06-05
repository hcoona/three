using Azure.AI.Translation.Document;

namespace Hcoona.DocumentTranslatorCli;

internal interface ISingleDocumentTranslationClient
{
    ValueTask<BinaryData> TranslateAsync(
        string targetLanguage,
        DocumentTranslateContent content,
        CancellationToken cancellationToken);
}
