using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record NpmjsRead(string Stage, string Url, int? Status, string Detail);
internal sealed record NpmjsObservationResult(ReleaseObservation Observation, NpmjsRead[] Reads);

// This concrete destination exposes only GET. Native registry state and original
// bytes are compared without a package witness or an application approval chain.
internal sealed class NpmjsObservation(HttpClient client, TimeProvider? timeProvider = null)
{
    internal static HttpClient CreateClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        UseDefaultCredentials = false,
        AutomaticDecompression = DecompressionMethods.None,
    }) { Timeout = Timeout.InfiniteTimeSpan };

    internal async Task<NpmjsObservationResult> ObserveAsync(PreparedRelease prepared,
        CancellationToken token)
    {
        ReleaseCompletion.Validate(prepared);
        if (prepared.Subject.Destination != "npmjs" || prepared.Files is not
            [{ Kind: "npm-tarball", Size: <= 25_000_000 }])
            throw new InvalidDataException("Unsupported complete public npmjs request.");
        if (prepared.RequiredChecks.Any(id => !prepared.Checks.Any(check => check.Id == id &&
            check.Status == ReleaseCheckStatus.Passed)))
            throw new InvalidDataException("npmjs reads require complete local qualification.");
        ReleaseFile file = prepared.Files[0];
        var reads = new List<NpmjsRead>();
        using var operationTimer = new CancellationTokenSource(TimeSpan.FromSeconds(60),
            timeProvider ?? TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token,
            operationTimer.Token);
        try
        {
            string name = Uri.EscapeDataString(file.Name).Replace("%40", "@",
                StringComparison.Ordinal);
            var uri = new Uri($"https://registry.npmjs.org/{name}/"
                + Uri.EscapeDataString(file.Version));
            (int status, byte[] body) = await ReadAsync("metadata", uri, 1_000_000);
            if (status == 404) return Result(ReleaseFileState.Absent);
            if (status == 429 || status >= 500) return Result(ReleaseFileState.Unknown);
            if (status != 200) return Result(ReleaseFileState.Unprovable);
            using JsonDocument document = JsonDocument.Parse(body);
            RequireUnique(document.RootElement);
            JsonElement metadata = document.RootElement;
            if (metadata.GetProperty("name").GetString() != file.Name ||
                metadata.GetProperty("version").GetString() != file.Version ||
                !Uri.TryCreate(metadata.GetProperty("dist").GetProperty("tarball").GetString(),
                    UriKind.Absolute, out Uri? tarball))
                return Result(ReleaseFileState.Unprovable);
            ValidateUri(tarball);
            (status, body) = await ReadAsync("tarball", tarball, 25_000_000);
            if (status == 404 || status == 429 || status >= 500)
                return Result(ReleaseFileState.Unknown);
            if (status != 200) return Result(ReleaseFileState.Unprovable);
            string sha256 = Convert.ToHexStringLower(SHA256.HashData(body));
            return Result(body.LongLength == file.Size && sha256 == file.Sha256
                ? ReleaseFileState.Exact : ReleaseFileState.Conflict, body.LongLength, sha256);
        }
        catch (OperationCanceledException)
        {
            reads.Add(new("operation", "", null, token.IsCancellationRequested
                ? "Cancelled." : "Read deadline exceeded."));
            return Result(ReleaseFileState.Unknown);
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException)
        {
            reads.Add(new("operation", "", null, "Complete response unavailable."));
            return Result(ReleaseFileState.Unknown);
        }
        catch (Exception failure) when (failure is InvalidDataException or JsonException or
            KeyNotFoundException or InvalidOperationException or UriFormatException)
        {
            reads.Add(new("operation", "", null, "Unsupported native metadata or response."));
            return Result(ReleaseFileState.Unprovable);
        }

        NpmjsObservationResult Result(ReleaseFileState state, long? size = null,
            string? sha256 = null) => new(new(prepared.Subject,
                [new(file.Output, file.Name, file.Version, state, size, sha256)],
                ["native-metadata-visibility"],
                [new("native-metadata-visibility", state is ReleaseFileState.Exact or
                    ReleaseFileState.Absent ? ReleaseCheckStatus.Passed :
                        ReleaseCheckStatus.Failed)]), [.. reads]);

        async Task<(int, byte[])> ReadAsync(string stage, Uri initial, int limit)
        {
            Uri current = initial;
            for (int redirects = 0; ; redirects++)
            {
                ValidateUri(current);
                using var requestTimer = new CancellationTokenSource(TimeSpan.FromSeconds(20),
                    timeProvider ?? TimeProvider.System);
                using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(
                    deadline.Token, requestTimer.Token);
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Accept.ParseAdd(stage == "metadata"
                    ? "application/vnd.npm.install-v1+json, application/json"
                    : "application/octet-stream");
                request.Headers.AcceptEncoding.ParseAdd("identity");
                request.Headers.CacheControl = new() { NoCache = true };
                using HttpResponseMessage response = await client.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, requestDeadline.Token);
                ValidateUri(response.RequestMessage?.RequestUri ?? current);
                int status = (int)response.StatusCode;
                reads.Add(new(stage, current.AbsoluteUri, status, "Received response headers."));
                if (response.Content.Headers.ContentEncoding.Any(value =>
                    !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Unsupported response encoding.");
                if (status is >= 300 and < 400)
                {
                    if (redirects >= 5 || response.Headers.Location is not { } location)
                        throw new InvalidDataException("Unsupported redirect response.");
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    ValidateUri(current);
                    continue;
                }
                long? length = response.Content.Headers.ContentLength;
                if (length > limit) throw new IOException("Response exceeds its body limit.");
                using Stream stream = await response.Content.ReadAsStreamAsync(
                    requestDeadline.Token);
                using var body = new MemoryStream();
                byte[] buffer = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(buffer, requestDeadline.Token)) != 0)
                {
                    if (body.Length + count > limit)
                        throw new IOException("Response exceeds its body limit.");
                    await body.WriteAsync(buffer.AsMemory(0, count), requestDeadline.Token);
                }
                if (length is not null && body.Length != length)
                    throw new IOException("Incomplete response body.");
                deadline.Token.ThrowIfCancellationRequested();
                requestDeadline.Token.ThrowIfCancellationRequested();
                return (status, body.ToArray());
            }
        }
    }

    private static void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" ||
            uri.Host != "registry.npmjs.org" || uri.Port != 443 ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("URL differs from the public registry origin.");
    }

    private static void RequireUnique(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected native metadata object.");
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!fields.Add(property.Name))
                throw new InvalidDataException("Duplicate native metadata property.");
            if (property.Value.ValueKind == JsonValueKind.Object) RequireUnique(property.Value);
        }
    }
}
