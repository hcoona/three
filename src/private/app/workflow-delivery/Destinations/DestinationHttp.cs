using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record DestinationRead(string Stage, int? Status, string Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url = null);

internal sealed class DestinationHttpSession : IDisposable
{
    private readonly HttpClient client;
    private readonly TimeProvider clock;
    private readonly TimeSpan requestTimeout;
    private readonly CancellationTokenSource timer;
    private readonly CancellationTokenSource lifetime;
    private readonly int? maxRequests;
    private readonly bool noCache;
    private readonly bool recordUrl;
    private readonly bool skipUnexpectedStatus;
    private readonly string headerDetail;
    private int sends;

    internal DestinationHttpSession(HttpClient client, TimeProvider? clock,
        TimeSpan operationTimeout, TimeSpan requestTimeout, CancellationToken token,
        int? maxRequests = null, bool noCache = false, bool recordUrl = false,
        bool skipUnexpectedStatus = false, string headerDetail = "Received response headers.")
    {
        this.client = client;
        this.clock = clock ?? TimeProvider.System;
        this.requestTimeout = requestTimeout;
        this.maxRequests = maxRequests;
        this.noCache = noCache;
        this.recordUrl = recordUrl;
        this.skipUnexpectedStatus = skipUnexpectedStatus;
        this.headerDetail = headerDetail;
        timer = new(operationTimeout, this.clock);
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, timer.Token);
    }

    internal CancellationToken Token => lifetime.Token;
    internal List<DestinationRead> Reads { get; } = [];

    internal Task<(int Status, byte[] Body)> MetadataAsync(string stage, Uri uri, string host,
        string accept, int limit, AuthenticationHeaderValue? authorization = null,
        bool allowQuery = true, Action<HttpRequestMessage>? configure = null) =>
        ReadAsync(stage, uri, host, accept, (stream, length, token) =>
            RegistryBody.LimitedAsync(stream, length, limit, token), authorization, allowQuery,
            null, Array.Empty<byte>(), configure);

    internal Task<(int Status, RegistryOriginalBytes Body)> OriginalAsync(string stage, Uri uri,
        string host, long size, bool retain, AuthenticationHeaderValue? authorization = null,
        TimeSpan? timeout = null, Action<HttpRequestMessage>? configure = null) =>
        ReadAsync(stage, uri, host, "application/octet-stream", (stream, length, token) =>
            RegistryBody.OriginalAsync(stream, length, size, retain, token), authorization,
            true, timeout, new RegistryOriginalBytes(null, null, null), configure);

    private async Task<(int Status, T Body)> ReadAsync<T>(string stage, Uri uri, string host,
        string accept, Func<Stream, long?, CancellationToken, Task<T>> read,
        AuthenticationHeaderValue? authorization, bool allowQuery, TimeSpan? timeout, T unread,
        Action<HttpRequestMessage>? configure)
    {
        RequireHttpsOrigin(uri, host, allowQuery);
        if (maxRequests is int limit && ++sends > limit)
            throw new IOException("Observation read allowance exhausted.");
        using var requestTimer = new CancellationTokenSource(timeout ?? requestTimeout, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token,
            requestTimer.Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        Configure(request, accept, authorization);
        if (noCache) request.Headers.CacheControl = new() { NoCache = true };
        configure?.Invoke(request);
        using HttpResponseMessage response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        int status = (int)response.StatusCode;
        Reads.Add(new(stage, status, headerDetail, recordUrl ? uri.AbsoluteUri : null));
        if (skipUnexpectedStatus && status is not (200 or 404)) return (status, unread);
        if (response.Content.Headers.ContentEncoding.Any(value =>
                !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Unsupported response encoding.");
        using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        T body = await read(stream, response.Content.Headers.ContentLength, deadline.Token);
        deadline.Token.ThrowIfCancellationRequested();
        return (status, body);
    }

    internal static void RequireHttpsOrigin(Uri uri, string expectedHost, bool allowQuery = true)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Host != expectedHost ||
            uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            (!allowQuery && uri.Query.Length != 0))
            throw new InvalidDataException("Unsupported native origin.");
    }

    internal static void RequireOk(int status)
    {
        if (status != 200) throw new IOException("Required native response not successful.");
    }

    internal static void Configure(HttpRequestMessage request, string accept,
        AuthenticationHeaderValue? authorization)
    {
        request.Headers.Authorization = authorization;
        request.Headers.UserAgent.ParseAdd("WorkflowDelivery");
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.AcceptEncoding.ParseAdd("identity");
    }

    public void Dispose()
    {
        lifetime.Dispose();
        timer.Dispose();
    }
}

internal sealed class GithubApi(DestinationHttpSession session, string credential,
    int metadataLimit)
{
    internal const string Host = "api.github.com";
    internal const string Repository = Owner + "/three";
    private const string Owner = "hcoona";
    private const string JsonMediaType = "application/vnd.github+json";
    private const int PageSize = 100;
    private readonly AuthenticationHeaderValue authorization = new("Bearer", credential);

    internal static Uri UserPackage(string packageType, string name) =>
        new($"https://{Host}/users/{Owner}/packages/{packageType}/{Uri.EscapeDataString(name)}");

    private static void SetVersion(HttpRequestMessage request) =>
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

    internal static void ConfigureJsonWrite(HttpRequestMessage request, string credential)
    {
        DestinationHttpSession.Configure(request, JsonMediaType, new("Bearer", credential));
        SetVersion(request);
    }

    internal Task<(int Status, byte[] Body)> MetadataAsync(string stage, Uri uri) =>
        session.MetadataAsync(stage, uri, Host, JsonMediaType, metadataLimit, authorization,
            configure: SetVersion);

    internal Task<(int Status, RegistryOriginalBytes Body)> OriginalAsync(string stage, Uri uri,
        long size, TimeSpan timeout) => session.OriginalAsync(stage, uri, Host, size, retain: false,
            authorization, timeout: timeout, configure: SetVersion);

    // Exact means the control record identifies the intended package, not that any version exists.
    internal async Task<ReleaseFileState> CheckPackageAsync(string stage, string packageType,
        string packageName, Func<string?, bool> matchesName, bool allowAbsent)
    {
        (int status, byte[] body) = await MetadataAsync(stage,
            UserPackage(packageType, packageName));
        if (allowAbsent && status == 404) return ReleaseFileState.Absent;
        if (status != 200) return ReleaseFileState.Unknown;
        using JsonDocument document = StrictJson.Parse(body);
        JsonElement package = document.RootElement;
        string name = PackageText(package, "name");
        string type = PackageText(package, "package_type");
        string repository = PackageText(StrictJson.Property(package, "repository"), "full_name");
        string? login = package.TryGetProperty("owner", out JsonElement owner) &&
            owner.ValueKind != JsonValueKind.Null ? PackageText(owner, "login") : null;
        return matchesName(name) && type == packageType && repository == Repository &&
            (login is null || login == Owner) ? ReleaseFileState.Exact : ReleaseFileState.Conflict;

        static string PackageText(JsonElement value, string key) =>
            StrictJson.Text(StrictJson.Property(value, key), allowEmpty: true);
    }

    internal async Task ReadArrayAsync(string stage, Uri uri, Action<JsonElement> consume)
    {
        for (int page = 1; ; page++)
        {
            var paged = new UriBuilder(uri);
            string query =
                $"per_page={PageSize}&page={page.ToString(CultureInfo.InvariantCulture)}";
            paged.Query = uri.Query.Length == 0 ? query : uri.Query[1..] + "&" + query;
            (int status, byte[] body) = await MetadataAsync(stage, paged.Uri);
            DestinationHttpSession.RequireOk(status);
            using JsonDocument document = StrictJson.Parse(body);
            JsonElement items = document.RootElement;
            if (items.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Expected GitHub page array.");
            if (items.GetArrayLength() > PageSize)
                throw new InvalidDataException("Oversized GitHub page.");
            foreach (JsonElement item in items.EnumerateArray()) consume(item);
            if (items.GetArrayLength() < PageSize) return;
        }
    }
}
