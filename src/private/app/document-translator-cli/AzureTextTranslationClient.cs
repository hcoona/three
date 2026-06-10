using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace Hcoona.DocumentTranslatorCli;

internal sealed class AzureTextTranslationClient : IDisposable
{
    internal const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly TokenCredential? tokenCredential;
    private bool disposed;

    public AzureTextTranslationClient()
        : this(new HttpClient(), new DefaultAzureCredential(), ownsHttpClient: true)
    {
    }

    internal AzureTextTranslationClient(
        HttpClient httpClient,
        TokenCredential? tokenCredential = null)
        : this(httpClient, tokenCredential, ownsHttpClient: false)
    {
    }

    internal AzureTextTranslationClient(
        HttpClient httpClient,
        TokenCredential? tokenCredential,
        bool ownsHttpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.tokenCredential = tokenCredential;
        this.ownsHttpClient = ownsHttpClient;
    }

    public async ValueTask<IReadOnlyList<string>> TranslateAsync(
        TranslationOptions options,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(texts);
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            BuildRequestUri(options.Endpoint, options.TargetLanguage));
        request.Content = CreateJsonContent(texts);

        if (options.AuthMode == AuthMode.ApiKey)
        {
            request.Headers.Add(
                "Ocp-Apim-Subscription-Key",
                options.ApiKey
                    ?? throw new InvalidOperationException(
                        "An API key is required for api-key authentication."));
            if (!string.IsNullOrWhiteSpace(options.Region))
            {
                request.Headers.Add("Ocp-Apim-Subscription-Region", options.Region);
            }
        }
        else if (options.AuthMode == AuthMode.EntraId)
        {
            AccessToken token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
        else
        {
            throw new InvalidOperationException("Unsupported authentication mode.");
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new TextTranslationServiceException(
                "Azure Text Translation service request failed.",
                ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new TextTranslationServiceException(
                    $"Azure Text Translation service returned HTTP {(int)response.StatusCode}.");
            }

            AzureTextTranslationResult[]? results;
            try
            {
                await using Stream responseStream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                results = await JsonSerializer
                    .DeserializeAsync(
                        responseStream,
                        AzureTextTranslationJsonContext.Default.AzureTextTranslationResultArray,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new TextTranslationServiceException(
                    "Azure Text Translation service returned malformed JSON.",
                    ex);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is HttpRequestException or IOException or OperationCanceledException)
            {
                throw new TextTranslationServiceException(
                    "Azure Text Translation service response read failed.",
                    ex);
            }

            return ValidateAndReadTranslations(results, texts.Count);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }

        disposed = true;
    }

    internal static Uri BuildRequestUri(Uri endpoint, string targetLanguage)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);

        Uri baseUri = endpoint.AbsolutePath.EndsWith('/')
            ? endpoint
            : new Uri(endpoint.AbsoluteUri + "/", UriKind.Absolute);
        Uri pathUri = new(baseUri, "translator/text/v3.0/translate");
        UriBuilder builder = new(pathUri)
        {
            Query = $"api-version=3.0&to={Uri.EscapeDataString(targetLanguage)}",
        };

        return builder.Uri;
    }

    private static StringContent CreateJsonContent(IReadOnlyList<string> texts)
    {
        AzureTextTranslationRequest[] request = new AzureTextTranslationRequest[texts.Count];
        for (int i = 0; i < texts.Count; i++)
        {
            request[i] = new AzureTextTranslationRequest(texts[i]);
        }

        string json = JsonSerializer.Serialize(
            request,
            AzureTextTranslationJsonContext.Default.AzureTextTranslationRequestArray);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private async ValueTask<AccessToken> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (tokenCredential is null)
        {
            throw new InvalidOperationException(
                "A token credential is required for entra-id authentication.");
        }

        return await tokenCredential
            .GetTokenAsync(
                new TokenRequestContext([CognitiveServicesScope]),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string[] ValidateAndReadTranslations(
        AzureTextTranslationResult[]? results,
        int expectedCount)
    {
        if (results is null || results.Length != expectedCount)
        {
            throw new TextTranslationServiceException(
                "Azure Text Translation service returned an unexpected result count.");
        }

        string[] translatedTexts = new string[expectedCount];
        for (int i = 0; i < results.Length; i++)
        {
            AzureTextTranslationResult? result = results[i];
            AzureTextTranslation[]? translations = result?.Translations;
            if (translations is null)
            {
                throw new TextTranslationServiceException(
                    "Azure Text Translation service returned a missing translation.");
            }

            if (translations.Length != 1)
            {
                throw new TextTranslationServiceException(
                    "Azure Text Translation service returned an unexpected translation count.");
            }

            string? translatedText = translations[0]?.Text;
            if (string.IsNullOrEmpty(translatedText))
            {
                throw new TextTranslationServiceException(
                    "Azure Text Translation service returned an empty translation.");
            }

            translatedTexts[i] = translatedText;
        }

        return translatedTexts;
    }
}
