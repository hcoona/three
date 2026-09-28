using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace WorkflowDeliveryV3NuGetConsumer;

internal sealed class BoundedHttpHandler : DelegatingHandler
{
    private readonly ConsumerRequest _request;
    private readonly List<byte[]> _secrets;
    private readonly string _basic;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _deadline;
    private bool _stopped;

    internal BoundedHttpHandler(
        ConsumerRequest request,
        string credential,
        HttpMessageHandler inner
    )
        : this(request, credential, inner, TimeProvider.System)
    {
    }

    internal BoundedHttpHandler(
        ConsumerRequest request,
        string credential,
        HttpMessageHandler inner,
        TimeProvider timeProvider
    )
        : base(inner)
    {
        request.Validate();
        ConsumerRequest.Require(
            !string.IsNullOrEmpty(credential)
                && !credential.Contains('\r')
                && !credential.Contains('\n'),
            "Invalid read credential."
        );
        _request = request;
        _deadline = new CancellationTokenSource(
            TimeSpan.FromSeconds(request.TimeoutSeconds), timeProvider
        );
        _basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("hcoona:" + credential));
        _secrets = [Encoding.UTF8.GetBytes(credential), Encoding.UTF8.GetBytes(_basic)];
    }

    internal int Requests { get; private set; }
    internal long ResponseBytes { get; private set; }
    internal bool PackageReturned { get; private set; }
    internal int PackageResponseIndex { get; private set; }

    internal void Check(ReadOnlySpan<byte> content, bool credentialsOnly = false)
    {
        byte[] decoded = Encoding.UTF8.GetBytes(
            Uri.UnescapeDataString(Encoding.UTF8.GetString(content))
        );
        foreach (byte[] secret in credentialsOnly ? _secrets.Take(2) : _secrets)
        {
            ConsumerRequest.Require(
                content.IndexOf(secret) < 0 && decoded.AsSpan().IndexOf(secret) < 0,
                "Read credential or storage capability reflected in consumer evidence."
            );
        }
    }

    private void Check(string text, bool credentialsOnly = false) =>
        Check(Encoding.UTF8.GetBytes(text), credentialsOnly);

    private void RememberLocation(string location)
    {
        if (location.Length == 0) return;
        Check(location, credentialsOnly: true);
        // Derive comparison forms from raw text, never from a normalized URI.
        string target = location.Split('#')[0];
        int scheme = target.IndexOf("://", StringComparison.Ordinal);
        int authorityStart = scheme >= 0 ? scheme + 3
            : target.StartsWith("//", StringComparison.Ordinal) ? 2 : 0;
        if (authorityStart > 0)
        {
            int start = target.IndexOfAny(['/', '?'], authorityStart);
            target = start < 0 ? string.Empty : target[start..];
        }
        if (target.Length == 0 || target[0] == '?') target = "/" + target;
        int question = target.IndexOf('?');
        string query = question < 0 ? string.Empty : target[(question + 1)..];
        // Match read_location_secrets: raw/once-decoded URL, target and query,
        // each in plain and HTML form, excluding empty and bare-root values.
        foreach (string raw in new[] { location, target, query })
        {
            foreach (string part in new[] { raw, Uri.UnescapeDataString(raw) })
            {
                if (part.Length == 0 || part == "/") continue;
                _secrets.Add(Encoding.UTF8.GetBytes(part));
                string html = part.Replace("&", "&amp;", StringComparison.Ordinal)
                    .Replace("<", "&lt;", StringComparison.Ordinal)
                    .Replace(">", "&gt;", StringComparison.Ordinal)
                    .Replace("\"", "&quot;", StringComparison.Ordinal)
                    .Replace("'", "&#x27;", StringComparison.Ordinal);
                _secrets.Add(Encoding.UTF8.GetBytes(html));
            }
        }
    }

    internal void Save(string name, ReadOnlySpan<byte> content)
    {
        Check(content);
        using var stream = new FileStream(
            Path.Combine(_request.EvidencePath, name),
            FileMode.CreateNew
        );
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage message,
        CancellationToken cancellationToken
    )
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _deadline.Token
        );
        cancellationToken = linked.Token;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ConsumerRequest.Require(
                !_stopped
                    && Requests < _request.MaximumRequests
                    && ResponseBytes < _request.MaximumResponseBytes,
                "Consumer HTTP allowance exhausted or stopped."
            );
            cancellationToken.ThrowIfCancellationRequested();
            string url = message.RequestUri?.AbsoluteUri ?? string.Empty;
            Check(url);
            ConsumerRequest.Require(
                message.Method == HttpMethod.Get
                    && message.Content is null
                    && (
                        url == ConsumerRequest.ServiceIndex
                        || url == _request.PackageUrl
                        || url
                            == _request.PackageBaseAddress
                                + ConsumerRequest.NormalizedId
                                + "/index.json"
                    ),
                "Consumer HTTP request is outside the selected resource scope."
            );
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basic);
            message.Headers.AcceptEncoding.Clear();
            ReadResult result = await ReadOnceAsync(
                message, url, null, null, null, cancellationToken
            ).ConfigureAwait(false);
            if (result.Location is not null)
            {
                using var storage = new HttpRequestMessage(
                    HttpMethod.Get,
                    new Uri(result.Location, new UriCreationOptions
                    {
                        DangerousDisablePathAndQueryCanonicalization = true,
                    })
                );
                result = await ReadOnceAsync(
                    storage, null, result.Origin, result.Index,
                    ConsumerRequest.Sha256(Encoding.UTF8.GetBytes(result.Location)),
                    cancellationToken
                ).ConfigureAwait(false);
            }
            byte[] body = result.Body;
            if (url == ConsumerRequest.ServiceIndex)
            {
                ConsumerRequest.Require(
                    ConsumerRequest.Sha256(body) == _request.ServiceIndexSha256,
                    "Consumer service index changed."
                );
            }
            if (url == _request.PackageUrl)
            {
                ConsumerRequest.Require(
                    ConsumerRequest.Sha256(body) == _request.PackageSha256,
                    "Consumer did not receive the admitted original package."
                );
                PackageReturned = true;
                PackageResponseIndex = result.Index;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
                RequestMessage = message,
            };
        }
        catch
        {
            _stopped = true;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record ReadResult(byte[] Body, int Index, string? Location, string? Origin);

    private async Task<ReadResult> ReadOnceAsync(
        HttpRequestMessage message,
        string? selectedUrl,
        string? origin,
        int? redirectedFrom,
        string? locationSha256,
        CancellationToken cancellationToken
    )
    {
        ConsumerRequest.Require(
            Requests < _request.MaximumRequests
                && ResponseBytes < _request.MaximumResponseBytes,
            "Consumer HTTP allowance exhausted before request."
        );
        cancellationToken.ThrowIfCancellationRequested();
        int index = ++Requests;
        Save(
            $"{index:D3}-reserved.json",
            Encoding.UTF8.GetBytes(
                new JsonObject
                {
                    ["schema"] = ConsumerRequest.HttpSchema,
                    ["method"] = "GET",
                    ["url"] = selectedUrl,
                    ["origin"] = origin ?? "https://nuget.pkg.github.com",
                    ["redirectedFrom"] = redirectedFrom,
                    ["locationSha256"] = locationSha256,
                    ["startedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["maximumRemainingResponseBytes"] =
                        _request.MaximumResponseBytes - ResponseBytes,
                }.ToJsonString()
            )
        );
        using HttpResponseMessage response = await base.SendAsync(message, cancellationToken)
            .ConfigureAwait(false);
        string[] locations = response.Headers.NonValidated.TryGetValues("Location", out var values)
            ? values.ToArray()
            : [];
        // Rejected metadata, unsupported package responses and second hops may
        // introduce a different capability. Protect it before checking fields.
        foreach (string encountered in locations) RememberLocation(encountered);
        bool redirect = selectedUrl == _request.PackageUrl
            && response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found;
        string? location = null;
        string? redirectOrigin = null;
        if (redirect)
        {
            ConsumerRequest.Require(
                locations.Length == 1, "Missing or ambiguous package Location."
            );
            location = locations[0];
            redirectOrigin = StorageOrigin(location);
        }
        foreach (var header in response.Headers.NonValidated.Concat(
            response.Content.Headers.NonValidated
        ))
        {
            if (header.Key.Equals("Location", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Check(header.Key);
            foreach (string value in header.Value)
            {
                Check(value);
            }
        }
        Check(response.ReasonPhrase ?? string.Empty);
        ConsumerRequest.Require(
            response.Content.Headers.ContentEncoding.Count == 0,
            "Encoded consumer response is unsupported by the byte allowance."
        );
        bool retain = response.StatusCode == HttpStatusCode.OK && locations.Length == 0;
        long before = ResponseBytes;
        using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        while (true)
        {
            int allowance = (int)Math.Min(
                chunk.Length, _request.MaximumResponseBytes - ResponseBytes
            );
            int count = await content.ReadAsync(chunk.AsMemory(0, allowance), cancellationToken)
                .ConfigureAwait(false);
            ResponseBytes += count;
            // The last allowance byte is an overflow sentinel. Establish EOF
            // inside the original total; never read again after exhausting it.
            ConsumerRequest.Require(
                ResponseBytes < _request.MaximumResponseBytes,
                "Consumer response allowance exhausted before complete response."
            );
            if (count == 0)
            {
                break;
            }
            if (retain)
            {
                buffer.Write(chunk, 0, count);
            }
        }
        byte[] body = buffer.ToArray();
        Check(body);
        cancellationToken.ThrowIfCancellationRequested();
        if (retain)
        {
            Save($"{index:D3}-body.bin", body);
        }
        var headers = new JsonObject();
        foreach (string key in new[] {
            "Date", "ETag", "Content-Type", "Content-Length", "X-GitHub-Request-Id"
        })
        {
            if (response.Headers.TryGetValues(key, out var selected)
                || response.Content.Headers.TryGetValues(key, out selected))
            {
                headers[key.ToLowerInvariant()] = new JsonArray(
                    selected.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()
                );
            }
        }
        Save(
            $"{index:D3}-response.json",
            Encoding.UTF8.GetBytes(
                new JsonObject
                {
                    ["schema"] = ConsumerRequest.HttpSchema,
                    ["status"] = (int)response.StatusCode,
                    ["headers"] = headers,
                    ["sha256"] = retain ? ConsumerRequest.Sha256(body) : null,
                    ["bytes"] = ResponseBytes - before,
                    ["bodyRetention"] = retain ? "original" : "omitted",
                    ["redirectOrigin"] = redirectOrigin,
                    ["locationSha256"] = location is null ? null
                        : ConsumerRequest.Sha256(Encoding.UTF8.GetBytes(location)),
                    ["completedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                }.ToJsonString()
            )
        );
        ConsumerRequest.Require(
            retain || redirect,
            "Consumer read did not return a complete successful response."
        );
        return new ReadResult(body, index, location, redirectOrigin);
    }

    private static string StorageOrigin(string location)
    {
        ConsumerRequest.Require(
            location.StartsWith("https://", StringComparison.Ordinal)
                && location.All(value => value is >= (char)33 and <= (char)126)
                && !location.Contains('\\') && !location.Contains('#'),
            "Invalid storage Location."
        );
        string authority = location[8..].Split('/', '?')[0].ToLowerInvariant();
        string[] labels = authority.Split('.');
        ConsumerRequest.Require(
            authority.Length is > 0 and <= 253
                && labels.All(label => label.Length is > 0 and <= 63
                    && char.IsAsciiLetterOrDigit(label[0])
                    && char.IsAsciiLetterOrDigit(label[^1])
                    && label.All(value => char.IsAsciiLetterOrDigit(value) || value == '-'))
                && !labels.All(label => label.All(char.IsAsciiDigit)
                    || (label.StartsWith("0x", StringComparison.Ordinal)
                        && label.Length > 2 && label[2..].All(char.IsAsciiHexDigit)))
                && authority != "api.github.com",
            "Invalid storage DNS authority."
        );
        return "https://" + authority;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deadline.Dispose();
            _gate.Dispose();
        }
        base.Dispose(disposing);
    }
}
