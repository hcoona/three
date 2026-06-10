using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Hcoona.DocumentTranslatorCli.Tests;

public sealed class AzureTextSegmentTranslatorTests
{
    [Fact]
    public async Task BatchesByHundredItemsAndRestoresSegmentIndexOrdering()
    {
        TranslatingHandler handler = new();
        using HttpClient httpClient = new(handler);
        AzureTextSegmentTranslator translator = new(
            new AzureTextTranslationClient(httpClient));
        List<TextSegmentTranslationRequest> segments = [];
        for (int i = 104; i >= 0; i--)
        {
            segments.Add(new TextSegmentTranslationRequest(i, $"segment-{i}"));
        }

        IReadOnlyList<string> translations = await translator.TranslateAsync(
            CreateOptions(),
            segments,
            CancellationToken.None);

        Assert.Equal(2, handler.Bodies.Count);
        Assert.Equal(100, CountRequestItems(handler.Bodies[0]));
        Assert.Equal(5, CountRequestItems(handler.Bodies[1]));
        Assert.Equal(105, translations.Count);
        for (int i = 0; i < translations.Count; i++)
        {
            Assert.Equal($"translated:segment-{i}", translations[i]);
        }
    }

    [Fact]
    public async Task BatchesByUnicodeScalarLimit()
    {
        TranslatingHandler handler = new();
        using HttpClient httpClient = new(handler);
        AzureTextSegmentTranslator translator = new(
            new AzureTextTranslationClient(httpClient));
        string thirtyThousandScalars = new('a', 30_000);
        string twentyThousandScalars = new('b', 20_000);
        string oneScalarTwoUtf16CodeUnits = char.ConvertFromUtf32(0x1F600);

        IReadOnlyList<string> translations = await translator.TranslateAsync(
            CreateOptions(),
            [
                new TextSegmentTranslationRequest(0, thirtyThousandScalars),
                new TextSegmentTranslationRequest(1, twentyThousandScalars),
                new TextSegmentTranslationRequest(2, oneScalarTwoUtf16CodeUnits),
            ],
            CancellationToken.None);

        Assert.Equal(2, handler.Bodies.Count);
        Assert.Equal(2, CountRequestItems(handler.Bodies[0]));
        Assert.Equal(1, CountRequestItems(handler.Bodies[1]));
        Assert.Equal(
            1,
            AzureTextSegmentTranslator.CountUnicodeScalars(oneScalarTwoUtf16CodeUnits));
        Assert.Equal(2, oneScalarTwoUtf16CodeUnits.Length);
        Assert.Equal($"translated:{oneScalarTwoUtf16CodeUnits}", translations[2]);
    }

    [Fact]
    public async Task SegmentOverScalarLimitFailsBeforeHttpRequest()
    {
        TranslatingHandler handler = new();
        using HttpClient httpClient = new(handler);
        AzureTextSegmentTranslator translator = new(
            new AzureTextTranslationClient(httpClient));
        string tooLarge = new('a', 50_001);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await translator.TranslateAsync(
                CreateOptions(),
                [new TextSegmentTranslationRequest(0, tooLarge)],
                CancellationToken.None));

        Assert.Contains("scalar limit", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task SparseSegmentIndexFailsBeforeHttpRequest()
    {
        TranslatingHandler handler = new();
        using HttpClient httpClient = new(handler);
        AzureTextSegmentTranslator translator = new(
            new AzureTextTranslationClient(httpClient));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await translator.TranslateAsync(
                CreateOptions(),
                [
                    new TextSegmentTranslationRequest(0, "segment 0"),
                    new TextSegmentTranslationRequest(2, "segment 2"),
                ],
                CancellationToken.None));

        Assert.Contains("contiguous", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task ServiceResultCountMismatchUsesTypedRedactedException()
    {
        MismatchedResultCountHandler handler = new();
        using HttpClient httpClient = new(handler);
        AzureTextSegmentTranslator translator = new(
            new AzureTextTranslationClient(httpClient));

        TextTranslationServiceException exception =
            await Assert.ThrowsAsync<TextTranslationServiceException>(
            async () => await translator.TranslateAsync(
                CreateOptions(),
                [
                    new TextSegmentTranslationRequest(0, "sensitive segment 0"),
                    new TextSegmentTranslationRequest(1, "sensitive segment 1"),
                ],
                CancellationToken.None));

        string diagnostic = exception.ToString();
        Assert.Contains("unexpected result count", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive segment", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("api-secret", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void DisposeDisposesOwnedClient()
    {
        DisposableHandler handler = new();
        HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(
            httpClient,
            tokenCredential: null,
            ownsHttpClient: true);
        AzureTextSegmentTranslator translator = new(client, ownsClient: true);

        translator.Dispose();

        Assert.True(handler.Disposed);
    }

    [Fact]
    public void DisposeDoesNotDisposeInjectedClient()
    {
        DisposableHandler handler = new();
        using HttpClient httpClient = new(handler);
        using AzureTextTranslationClient client = new(
            httpClient,
            tokenCredential: null,
            ownsHttpClient: true);
        AzureTextSegmentTranslator translator = new(client);

        translator.Dispose();

        Assert.False(handler.Disposed);
    }

    private static int CountRequestItems(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.GetArrayLength();
    }

    private static TranslationOptions CreateOptions() =>
        new(
            "source.md",
            "target.md",
            "fr",
            new Uri("https://resource.cognitiveservices.azure.com"),
            AuthMode.ApiKey,
            "api-secret",
            MarkdownMode.Aware,
            TranslationRoute.MarkdownAware,
            IsMarkdownExtension: true,
            Force: false,
            OriginalFileName: "source.md",
            LegacyDocumentContentType: null);

    private sealed class TranslatingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Bodies.Add(body);

            using JsonDocument document = JsonDocument.Parse(body);
            List<object> results = [];
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                string text = item.GetProperty("Text").GetString()!;
                results.Add(new
                {
                    translations = new[]
                    {
                        new { text = $"translated:{text}" },
                    },
                });
            }

            string responseJson = JsonSerializer.Serialize(results);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class MismatchedResultCountHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"translations":[{"text":"translated"}]}]""",
                    Encoding.UTF8,
                    "application/json"),
            });
    }

    private sealed class DisposableHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
