using Azure.AI.Translation.Document;

namespace Hcoona.DocumentTranslatorCli;

internal sealed class AzureDocumentTranslator : IDocumentTranslator
{
    private readonly ISingleDocumentTranslationClientFactory clientFactory;

    public AzureDocumentTranslator()
        : this(new AzureSingleDocumentTranslationClientFactory())
    {
    }

    internal AzureDocumentTranslator(ISingleDocumentTranslationClientFactory clientFactory)
    {
        this.clientFactory = clientFactory
            ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public async ValueTask<BinaryData> TranslateAsync(
        TranslationOptions options,
        Stream inputStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(inputStream);
        cancellationToken.ThrowIfCancellationRequested();

        ISingleDocumentTranslationClient client = CreateClient(options);
        MultipartFormFileData document = new(
            options.OriginalFileName,
            inputStream,
            options.LegacyDocumentContentType
                ?? throw new InvalidOperationException(
                    "Legacy document content type is required for Azure document translation."));
        DocumentTranslateContent content = new(document);

        return await client
            .TranslateAsync(options.TargetLanguage, content, cancellationToken)
            .ConfigureAwait(false);
    }

    private ISingleDocumentTranslationClient CreateClient(TranslationOptions options)
    {
        return options.AuthMode switch
        {
            AuthMode.ApiKey => clientFactory.CreateApiKeyClient(
                options.Endpoint,
                options.ApiKey
                    ?? throw new InvalidOperationException(
                        "An API key is required for api-key authentication.")),
            AuthMode.EntraId => clientFactory.CreateEntraIdClient(options.Endpoint),
            _ => throw new InvalidOperationException("Unsupported authentication mode."),
        };
    }
}
