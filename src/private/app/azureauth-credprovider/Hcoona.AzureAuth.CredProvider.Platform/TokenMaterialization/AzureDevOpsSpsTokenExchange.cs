using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

namespace Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

public sealed class AzureDevOpsSpsTokenExchange : ITokenExchange, IDisposable
{
    public const int DefaultMaxResponseBytes = 64 * 1024;
    public const int MinimumMaxResponseBytes = 256;
    public const int MaximumMaxResponseBytes = 1024 * 1024;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MinimumTimeout = TimeSpan.FromMilliseconds(10);
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RequestedSessionLifetime = TimeSpan.FromHours(4);
    public static readonly TimeSpan ExpirySafetySkew = TimeSpan.FromMinutes(5);

    private const string AuthorizationEndpointHeader = "X-VSS-AuthorizationEndpoint";
    private const string SessionTokenPath = "_apis/Token/SessionTokens";
    private const string SessionTokenQuery = "tokenType=SelfDescribing&api-version=5.0-preview.1";

    private readonly HttpClient _httpClient;
    private readonly int _maxResponseBytes;
    private readonly bool _ownsHttpClient;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;

    public AzureDevOpsSpsTokenExchange(
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null,
        TimeSpan? timeout = null,
        int maxResponseBytes = DefaultMaxResponseBytes
    )
    {
        TimeSpan effectiveTimeout = timeout ?? DefaultTimeout;
        if (effectiveTimeout < MinimumTimeout || effectiveTimeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (
            maxResponseBytes < MinimumMaxResponseBytes
            || maxResponseBytes > MaximumMaxResponseBytes
        )
        {
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));
        }

        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? CreateProductionHttpClient();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timeout = effectiveTimeout;
        _maxResponseBytes = maxResponseBytes;
    }

    public async ValueTask<AsyncTokenExchangeResult> ExchangeAsync(
        CredentialRequestV2 request,
        AcquiredAccessToken sourceToken,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceToken);

        if (
            CredentialFormPolicy.Evaluate(request).Action
                != CredentialMaterializationAction.ExchangeNuGetSessionToken
            || request.Resource is null
            || CredentialRequestV2Policy.GetViolation(request) is not null
        )
        {
            return Failure(AsyncTokenExchangeStatus.Disabled, "SpsExchangeUnsupported");
        }

        if (string.IsNullOrEmpty(sourceToken.Token?.Value))
        {
            return Failure(AsyncTokenExchangeStatus.Failed, "SpsSourceTokenInvalid");
        }

        using var timeoutSource = new CancellationTokenSource(_timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token
        );

        try
        {
            EndpointDiscoveryResult discovery = await DiscoverEndpointAsync(
                    request.Resource.ServiceEndpoint,
                    request.Resource.Organization,
                    linkedSource.Token
                )
                .ConfigureAwait(false);
            if (discovery.Status == EndpointDiscoveryStatus.NotAdvertised)
            {
                return Failure(AsyncTokenExchangeStatus.Disabled, "SpsExchangeNotAdvertised");
            }

            if (discovery.Status != EndpointDiscoveryStatus.Advertised)
            {
                return Failure(AsyncTokenExchangeStatus.Failed, "SpsEndpointRejected");
            }

            return await SendExchangeAsync(discovery.Endpoint!, sourceToken, linkedSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(AsyncTokenExchangeStatus.Canceled, "SpsExchangeCanceled");
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            return Failure(AsyncTokenExchangeStatus.TimedOut, "SpsExchangeTimedOut");
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or JsonException)
        {
            return Failure(AsyncTokenExchangeStatus.Failed, "SpsExchangeFailed");
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<EndpointDiscoveryResult> DiscoverEndpointAsync(
        Uri serviceEndpoint,
        string organization,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, serviceEndpoint);
        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (IsRedirect(response.StatusCode))
        {
            return EndpointDiscoveryResult.Rejected;
        }

        if (
            !response.Headers.TryGetValues(
                AuthorizationEndpointHeader,
                out IEnumerable<string>? endpointValues
            )
        )
        {
            return EndpointDiscoveryResult.NotAdvertised;
        }

        string[] values = endpointValues.ToArray();
        return
            values.Length == 1
            && Uri.TryCreate(values[0], UriKind.Absolute, out Uri? baseEndpoint)
            && TryCreateAllowedSessionEndpoint(baseEndpoint, organization, out Uri? endpoint)
            ? EndpointDiscoveryResult.Advertised(endpoint!)
            : EndpointDiscoveryResult.Rejected;
    }

    private async Task<AsyncTokenExchangeResult> SendExchangeAsync(
        Uri endpoint,
        AcquiredAccessToken sourceToken,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset beforePost = _timeProvider.GetUtcNow();
        DateTimeOffset requestedExpiry = AddCapped(beforePost, RequestedSessionLifetime);
        if (!IsExpiryUsable(requestedExpiry, beforePost))
        {
            return Failure(AsyncTokenExchangeStatus.Failed, "SpsRequestedExpiryInvalid");
        }

        HttpResponseMessage response = await SendSessionTokenRequestAsync(
                endpoint,
                sourceToken,
                requestedExpiry,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            response.Dispose();
            using HttpResponseMessage retryResponse = await SendSessionTokenRequestAsync(
                    endpoint,
                    sourceToken,
                    requestedExpiry: null,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return await CreateExchangeResultAsync(retryResponse, cancellationToken)
                .ConfigureAwait(false);
        }

        using (response)
        {
            return await CreateExchangeResultAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> SendSessionTokenRequestAsync(
        Uri endpoint,
        AcquiredAccessToken sourceToken,
        DateTimeOffset? requestedExpiry,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            sourceToken.Token.Value
        );
        request.Content = new StringContent(
            CreateRequestBody(requestedExpiry),
            Encoding.UTF8,
            "application/json"
        );

        return await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AsyncTokenExchangeResult> CreateExchangeResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        if (
            IsRedirect(response.StatusCode)
            || response.StatusCode < HttpStatusCode.OK
            || response.StatusCode >= HttpStatusCode.MultipleChoices
        )
        {
            return Failure(AsyncTokenExchangeStatus.Failed, "SpsExchangeHttpStatus");
        }

        byte[]? responseBody = await ReadBoundedBodyAsync(response.Content, cancellationToken)
            .ConfigureAwait(false);
        if (
            responseBody is null
            || !TryParseResponse(responseBody, out SecretText? token, out DateTimeOffset expiry)
        )
        {
            return Failure(AsyncTokenExchangeStatus.Failed, "SpsExchangeResponseInvalid");
        }

        DateTimeOffset completedAt = _timeProvider.GetUtcNow();
        if (!IsExpiryUsable(expiry, completedAt))
        {
            return Failure(AsyncTokenExchangeStatus.Failed, "SpsExchangeResponseInvalid");
        }

        return AsyncTokenExchangeResult.Success(token!, expiry);
    }

    internal static bool TryCreateAllowedSessionEndpoint(
        Uri baseEndpoint,
        string organization,
        out Uri? endpoint
    )
    {
        endpoint = null;
        if (
            !baseEndpoint.IsAbsoluteUri
            || !string.Equals(baseEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(baseEndpoint.UserInfo)
            || !baseEndpoint.IsDefaultPort
            || !string.IsNullOrEmpty(baseEndpoint.Query)
            || !string.IsNullOrEmpty(baseEndpoint.Fragment)
            || string.IsNullOrWhiteSpace(organization)
        )
        {
            return false;
        }

        string escapedOrganization = Uri.EscapeDataString(organization);
        string host = baseEndpoint.IdnHost;
        string basePath = baseEndpoint.AbsolutePath.TrimEnd('/');
        string finalPath;
        if (
            (
                string.Equals(host, "vssps.dev.azure.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".vssps.dev.azure.com", StringComparison.OrdinalIgnoreCase)
            )
            && string.Equals(
                basePath,
                "/" + escapedOrganization,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            finalPath = $"/{escapedOrganization}/{SessionTokenPath}";
        }
        else if (
            (
                string.Equals(host, "vssps.visualstudio.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    host,
                    organization + ".vssps.visualstudio.com",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            && basePath.Length == 0
        )
        {
            finalPath = "/" + SessionTokenPath;
        }
        else
        {
            return false;
        }

        var builder = new UriBuilder(baseEndpoint) { Path = finalPath, Query = SessionTokenQuery };
        endpoint = builder.Uri;
        return true;
    }

    private async Task<byte[]?> ReadBoundedBodyAsync(
        HttpContent content,
        CancellationToken cancellationToken
    )
    {
        if (content.Headers.ContentLength > _maxResponseBytes)
        {
            return null;
        }

        await using Stream stream = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            int read = await stream
                .ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > _maxResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }
    }

    private static bool TryParseResponse(
        byte[] utf8,
        out SecretText? token,
        out DateTimeOffset expiry
    )
    {
        token = null;
        expiry = default;
        using JsonDocument document = JsonDocument.Parse(
            utf8,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            }
        );
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        string? tokenValue = null;
        string? validTo = null;
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (
                !names.Add(property.Name)
                || property.Name is not ("displayName" or "scope" or "validTo" or "token")
                || property.Value.ValueKind != JsonValueKind.String
            )
            {
                return false;
            }

            if (property.Name == "token")
            {
                tokenValue = property.Value.GetString();
            }
            else if (property.Name == "validTo")
            {
                validTo = property.Value.GetString();
            }
        }

        if (
            string.IsNullOrEmpty(tokenValue)
            || tokenValue.Any(char.IsControl)
            || string.IsNullOrEmpty(validTo)
            || !DateTimeOffset.TryParse(
                validTo,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out expiry
            )
        )
        {
            return false;
        }

        token = new SecretText { Value = tokenValue };
        return true;
    }

    private static string CreateRequestBody(DateTimeOffset? requestedExpiry)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("displayName", "Azure DevOps Artifacts Credential Provider");
            writer.WriteString("scope", "vso.packaging_write vso.drop_write");
            if (requestedExpiry is not null)
            {
                writer.WriteString("validTo", requestedExpiry.Value.UtcDateTime);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static HttpClient CreateProductionHttpClient()
    {
        SocketsHttpHandler handler = CreateProductionHttpHandler();
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    internal static SocketsHttpHandler CreateProductionHttpHandler() =>
        new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = true,
            Credentials = null,
        };

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode
            is HttpStatusCode.MultipleChoices
                or HttpStatusCode.MovedPermanently
                or HttpStatusCode.Found
                or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect;

    private static bool IsExpiryUsable(DateTimeOffset expiry, DateTimeOffset now) =>
        expiry > now && expiry - now > ExpirySafetySkew;

    private static DateTimeOffset AddCapped(DateTimeOffset value, TimeSpan duration) =>
        value > DateTimeOffset.MaxValue - duration ? DateTimeOffset.MaxValue : value + duration;

    private enum EndpointDiscoveryStatus
    {
        NotAdvertised,
        Advertised,
        Rejected,
    }

    private readonly record struct EndpointDiscoveryResult(
        EndpointDiscoveryStatus Status,
        Uri? Endpoint
    )
    {
        public static EndpointDiscoveryResult NotAdvertised =>
            new(EndpointDiscoveryStatus.NotAdvertised, null);

        public static EndpointDiscoveryResult Rejected =>
            new(EndpointDiscoveryStatus.Rejected, null);

        public static EndpointDiscoveryResult Advertised(Uri endpoint) =>
            new(EndpointDiscoveryStatus.Advertised, endpoint);
    }

    private static AsyncTokenExchangeResult Failure(AsyncTokenExchangeStatus status, string code) =>
        AsyncTokenExchangeResult.Failure(status, code);
}
