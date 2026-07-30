using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Xunit;

namespace Hcoona.DocumentTranslatorCli.Tests;

public sealed class AzureTextTranslationClientTests
{
    [Fact]
    public async Task ApiKeyRequestHasExpectedUriHeadersAndBody()
    {
        CapturingHandler handler = new(static request =>
            JsonResponse("""[{"translations":[{"text":"bonjour"}]}]"""));
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);
        TranslationOptions options = CreateOptions(
            authMode: AuthMode.ApiKey,
            apiKey: "api-secret");

        IReadOnlyList<string> translations = await client.TranslateAsync(
            options,
            ["hello"],
            CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://resource.cognitiveservices.azure.com/translator/text/v3.0/translate" +
            "?api-version=3.0&to=fr",
            request.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "api-secret",
            Assert.Single(request.Headers.GetValues("Ocp-Apim-Subscription-Key")));
        Assert.False(request.Headers.Contains("Authorization"));
        Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", request.Content?.Headers.ContentType?.CharSet);
        Assert.Equal("""[{"Text":"hello"}]""", handler.Bodies[0]);
        Assert.Equal(["bonjour"], translations);
    }

    [Fact]
    public async Task ApiKeyRequestIncludesRegionHeaderWhenConfigured()
    {
        CapturingHandler handler = new(static _ =>
            JsonResponse("""[{"translations":[{"text":"bonjour"}]}]"""));
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);
        TranslationOptions options = CreateOptions(
            authMode: AuthMode.ApiKey,
            apiKey: "api-secret",
            region: "eastus");

        await client.TranslateAsync(options, ["hello"], CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(
            "eastus",
            Assert.Single(request.Headers.GetValues("Ocp-Apim-Subscription-Region")));
    }

    [Fact]
    public async Task ApiKeyRequestOmitsRegionHeaderWhenNotConfigured()
    {
        CapturingHandler handler = new(static _ =>
            JsonResponse("""[{"translations":[{"text":"bonjour"}]}]"""));
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);
        TranslationOptions options = CreateOptions(
            authMode: AuthMode.ApiKey,
            apiKey: "api-secret");

        await client.TranslateAsync(options, ["hello"], CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.False(request.Headers.Contains("Ocp-Apim-Subscription-Region"));
    }

    [Fact]
    public async Task EntraRequestUsesTokenCredentialAndBearerHeader()
    {
        CapturingHandler handler = new(static request =>
            JsonResponse("""[{"translations":[{"text":"bonjour"}]}]"""));
        using HttpClient httpClient = new(handler);
        RecordingTokenCredential credential = new("entra-token");
        AzureTextTranslationClient client = new(httpClient, credential);
        TranslationOptions options = CreateOptions(authMode: AuthMode.EntraId);

        IReadOnlyList<string> translations = await client.TranslateAsync(
            options,
            ["hello"],
            CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("entra-token", request.Headers.Authorization?.Parameter);
        Assert.NotNull(credential.Scopes);
        Assert.Equal([AzureTextTranslationClient.CognitiveServicesScope], credential.Scopes);
        Assert.False(request.Headers.Contains("Ocp-Apim-Subscription-Key"));
        Assert.False(request.Headers.Contains("Ocp-Apim-Subscription-Region"));
        Assert.Equal(["bonjour"], translations);
    }

    [Theory]
    [InlineData("""[]""", "unexpected result count")]
    [InlineData(
        """[{"translations":[{"text":"bonjour"}]},{"translations":[{"text":"salut"}]}]""",
        "unexpected result count")]
    [InlineData("""[null]""", "missing translation")]
    [InlineData("""[{"translations":null}]""", "missing translation")]
    [InlineData("""[{"translations":[]}]""", "unexpected translation count")]
    [InlineData(
        """[{"translations":[{"text":"bonjour"},{"text":"salut"}]}]""",
        "unexpected translation count")]
    [InlineData("""[{"translations":[{"text":"bonjour"},null]}]""", "unexpected translation count")]
    [InlineData("""[{"translations":[null]}]""", "empty translation")]
    [InlineData("""[{"translations":[{"text":null}]}]""", "empty translation")]
    [InlineData("""[{"translations":[{"text":""}]}]""", "empty translation")]
    public async Task InvalidSuccessfulResponsesFailClosed(
        string responseJson,
        string expectedMessage)
    {
        CapturingHandler handler = new(_ => JsonResponse(responseJson));
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);
        TranslationOptions options = CreateOptions(
            authMode: AuthMode.ApiKey,
            apiKey: "api-secret");

        TextTranslationServiceException exception =
            await Assert.ThrowsAsync<TextTranslationServiceException>(
                async () => await client.TranslateAsync(
                    options,
                    ["sensitive segment"],
                    CancellationToken.None));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive segment", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("api-secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedJsonDiagnosticsAreRedacted()
    {
        CapturingHandler handler = new(_ => JsonResponse("malformed sensitive response"));
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);
        TranslationOptions options = CreateOptions(
            authMode: AuthMode.ApiKey,
            apiKey: "api-secret");

        TextTranslationServiceException exception =
            await Assert.ThrowsAsync<TextTranslationServiceException>(
                async () => await client.TranslateAsync(
                    options,
                    ["sensitive segment"],
                    CancellationToken.None));

        Assert.Contains("malformed JSON", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive segment", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive response", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("api-secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonSuccessDiagnosticsAreRedacted()
    {
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "service body contains sensitive segment api-secret bearer-token",
                Encoding.UTF8,
                "text/plain"),
        });
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);
        TranslationOptions options = CreateOptions(
            authMode: AuthMode.ApiKey,
            apiKey: "api-secret");

        TextTranslationServiceException exception =
            await Assert.ThrowsAsync<TextTranslationServiceException>(
                async () => await client.TranslateAsync(
                    options,
                    ["sensitive segment"],
                    CancellationToken.None));

        string diagnostic = exception.ToString();
        Assert.Contains("HTTP 400", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive segment", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("api-secret", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-token", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("service body", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpRequestFailureUsesTypedServiceException()
    {
        CapturingHandler handler = new(static _ =>
            throw new HttpRequestException("network unavailable"));
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);
        TranslationOptions options = CreateOptions(
            authMode: AuthMode.ApiKey,
            apiKey: "api-secret");

        TextTranslationServiceException exception =
            await Assert.ThrowsAsync<TextTranslationServiceException>(
            async () => await client.TranslateAsync(options, ["hello"], CancellationToken.None));

        Assert.Contains("request failed", exception.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task NonUserCancellationDuringSendUsesTypedServiceException()
    {
        CapturingHandler handler = new(static _ =>
            throw new OperationCanceledException("timeout"));
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);
        TranslationOptions options = CreateOptions(
            authMode: AuthMode.ApiKey,
            apiKey: "api-secret");

        TextTranslationServiceException exception =
            await Assert.ThrowsAsync<TextTranslationServiceException>(
            async () => await client.TranslateAsync(options, ["hello"], CancellationToken.None));

        Assert.Contains("request failed", exception.Message, StringComparison.Ordinal);
        Assert.IsType<OperationCanceledException>(exception.InnerException);
    }

    [Theory]
    [InlineData(nameof(IOException))]
    [InlineData(nameof(HttpRequestException))]
    [InlineData(nameof(OperationCanceledException))]
    public async Task PostHeaderContentStreamFailureUsesTypedServiceException(string exceptionType)
    {
        Exception innerException = CreateResponseReadException(exceptionType);
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new FailingReadContent(innerException),
        });
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);
        TranslationOptions options = CreateOptions(
            authMode: AuthMode.ApiKey,
            apiKey: "api-secret");

        TextTranslationServiceException exception =
            await Assert.ThrowsAsync<TextTranslationServiceException>(
            async () => await client.TranslateAsync(options, ["hello"], CancellationToken.None));

        Assert.Contains("response read failed", exception.Message, StringComparison.Ordinal);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public async Task PostHeaderReadAsStreamFailureUsesTypedServiceException()
    {
        IOException innerException = new("read stream unavailable");
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new FailingReadAsStreamContent(innerException),
        });
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);
        TranslationOptions options = CreateOptions(
            authMode: AuthMode.ApiKey,
            apiKey: "api-secret");

        TextTranslationServiceException exception =
            await Assert.ThrowsAsync<TextTranslationServiceException>(
            async () => await client.TranslateAsync(options, ["hello"], CancellationToken.None));

        Assert.Contains("response read failed", exception.Message, StringComparison.Ordinal);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public async Task UserCancellationDuringPostHeaderReadRemainsCancellation()
    {
        using CancellationTokenSource cancellation = new();
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UserCancellationReadContent(cancellation),
        });
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);
        TranslationOptions options = CreateOptions(
            authMode: AuthMode.ApiKey,
            apiKey: "api-secret");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.TranslateAsync(options, ["hello"], cancellation.Token));
    }

    [Fact]
    public async Task UserCancellationDuringSendRemainsCancellation()
    {
        using CancellationTokenSource cancellation = new();
        UserCancellationHandler handler = new(cancellation);
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);
        TranslationOptions options = CreateOptions(
            authMode: AuthMode.ApiKey,
            apiKey: "api-secret");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.TranslateAsync(options, ["hello"], cancellation.Token));
    }

    [Fact]
    public async Task EntraTokenAcquisitionFailureDoesNotExposeTokenOrSegments()
    {
        CapturingHandler handler = new(static _ =>
            throw new InvalidOperationException("HTTP should not be called."));
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(
            httpClient,
            new ThrowingTokenCredential(new AuthenticationFailedException("credential failed")));
        TranslationOptions options = CreateOptions(authMode: AuthMode.EntraId);

        AuthenticationFailedException exception =
            await Assert.ThrowsAsync<AuthenticationFailedException>(
                async () => await client.TranslateAsync(
                    options,
                    ["sensitive segment"],
                    CancellationToken.None));

        Assert.Contains("credential failed", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive segment", exception.ToString(), StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RequestBodyDoesNotUseMachineTokenPlaceholders()
    {
        string machineLookingText = "Hello {name}, keep $(Variable) and %TOKEN% as prose.";
        CapturingHandler handler = new(static _ =>
            JsonResponse("""[{"translations":[{"text":"translated"}]}]"""));
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);
        TranslationOptions options = CreateOptions(
            authMode: AuthMode.ApiKey,
            apiKey: "api-secret");

        await client.TranslateAsync(options, [machineLookingText], CancellationToken.None);

        string expectedBody =
            "[{\"Text\":\"Hello {name}, keep $(Variable) and " +
            "%TOKEN% as prose.\"}]";
        Assert.Equal(expectedBody, handler.Bodies[0]);
        Assert.DoesNotContain("placeholder", handler.Bodies[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisposeDisposesOwnedHttpClient()
    {
        DisposableHandler handler = new();
        HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(
            httpClient,
            tokenCredential: null,
            ownsHttpClient: true);

        client.Dispose();

        Assert.True(handler.Disposed);
    }

    [Fact]
    public void DisposeDoesNotDisposeInjectedHttpClient()
    {
        DisposableHandler handler = new();
        using HttpClient httpClient = new(handler);
        AzureTextTranslationClient client = new(httpClient);

        client.Dispose();

        Assert.False(handler.Disposed);
    }

    private static TranslationOptions CreateOptions(
        AuthMode authMode,
        string? apiKey = null,
        string? region = null) =>
        new(
            "source.md",
            "target.md",
            "fr",
            new Uri("https://resource.cognitiveservices.azure.com"),
            authMode,
            apiKey,
            MarkdownMode.Aware,
            TranslationRoute.MarkdownAware,
            IsMarkdownExtension: true,
            Force: false,
            OriginalFileName: "source.md",
            LegacyDocumentContentType: null,
            Region: region);

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static Exception CreateResponseReadException(string exceptionType) =>
        exceptionType switch
        {
            nameof(IOException) => new IOException("response stream failed"),
            nameof(HttpRequestException) => new HttpRequestException("response stream failed"),
            nameof(OperationCanceledException) =>
                new OperationCanceledException("response stream timed out"),
            _ => throw new ArgumentOutOfRangeException(nameof(exceptionType), exceptionType, null),
        };

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> respond = respond;

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return respond(request);
        }
    }

    private sealed class FailingReadContent(Exception exception) : HttpContent
    {
        private readonly Exception exception = exception;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(exception);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new FailingReadStream(exception));

        protected override Task<Stream> CreateContentReadStreamAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new FailingReadStream(exception));
    }

    private sealed class FailingReadAsStreamContent(Exception exception) : HttpContent
    {
        private readonly Exception exception = exception;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(exception);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync(
            CancellationToken cancellationToken) =>
            Task.FromException<Stream>(exception);
    }

    private sealed class UserCancellationReadContent(
        CancellationTokenSource cancellation) : HttpContent
    {
        private readonly CancellationTokenSource cancellation = cancellation;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new UserCancellationReadStream(cancellation));
    }

    private sealed class FailingReadStream(Exception exception) : Stream
    {
        private readonly Exception exception = exception;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw exception;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(exception);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class UserCancellationReadStream(CancellationTokenSource cancellation) : Stream
    {
        private readonly CancellationTokenSource cancellation = cancellation;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromException<int>(
                new OperationCanceledException(cancellation.Token));
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class UserCancellationHandler(CancellationTokenSource cancellation)
        : HttpMessageHandler
    {
        private readonly CancellationTokenSource cancellation = cancellation;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromException<HttpResponseMessage>(
                new OperationCanceledException(cancellationToken));
        }
    }

    private sealed class DisposableHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(JsonResponse("""[{"translations":[{"text":"bonjour"}]}]"""));

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class RecordingTokenCredential(string token) : TokenCredential
    {
        private readonly string token = token;

        public string[]? Scopes { get; private set; }

        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            Scopes = requestContext.Scopes;
            return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            Scopes = requestContext.Scopes;
            return new ValueTask<AccessToken>(
                new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    private sealed class ThrowingTokenCredential(Exception exception) : TokenCredential
    {
        private readonly Exception exception = exception;

        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            throw exception;

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            throw exception;
    }
}
