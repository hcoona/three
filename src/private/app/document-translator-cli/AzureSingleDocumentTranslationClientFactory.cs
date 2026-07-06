using Azure;
using Azure.AI.Translation.Document;
using Azure.Core;
using Azure.Identity;

namespace Hcoona.DocumentTranslatorCli;

internal sealed class AzureSingleDocumentTranslationClientFactory
    : ISingleDocumentTranslationClientFactory
{
    public ISingleDocumentTranslationClient CreateApiKeyClient(Uri endpoint, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return new AzureSingleDocumentTranslationClient(
            new SingleDocumentTranslationClient(endpoint, new AzureKeyCredential(apiKey)));
    }

    public ISingleDocumentTranslationClient CreateEntraIdClient(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return new AzureSingleDocumentTranslationClient(
            new SingleDocumentTranslationClient(endpoint, new DefaultAzureCredential()));
    }

    private sealed class AzureSingleDocumentTranslationClient(
        SingleDocumentTranslationClient client)
        : ISingleDocumentTranslationClient
    {
        private readonly SingleDocumentTranslationClient client = client
            ?? throw new ArgumentNullException(nameof(client));

        public async ValueTask<BinaryData> TranslateAsync(
            string targetLanguage,
            DocumentTranslateContent content,
            CancellationToken cancellationToken)
        {
            Response<BinaryData> response = await client
                .TranslateAsync(
                    targetLanguage,
                    content,
                    sourceLanguage: null,
                    category: null,
                    allowFallback: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Value;
        }
    }
}
