namespace Hcoona.DocumentTranslatorCli;

internal interface ISingleDocumentTranslationClientFactory
{
    ISingleDocumentTranslationClient CreateApiKeyClient(Uri endpoint, string apiKey);

    ISingleDocumentTranslationClient CreateEntraIdClient(Uri endpoint);
}
