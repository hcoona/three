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

    internal async Task<NpmjsObservationResult> ObserveAsync(PreparedRelease prepared,
        CancellationToken token)
    {
        if (prepared.Subject.Destination != "npmjs" || prepared.Files is not
            [{ Kind: "npm-tarball" }])
            throw new InvalidDataException("Unsupported complete public npmjs request.");
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
            (int status, byte[] body) = await ReadAsync("metadata", uri, (stream, length, read) =>
                RegistryBody.LimitedAsync(stream, length, 1_000_000, read));
            if (status == 404) return Result(ReleaseFileState.Absent);
            if (status != 200) return Result(ReleaseFileState.Unknown);
            using JsonDocument document = JsonDocument.Parse(body);
            RequireUnique(document.RootElement);
            JsonElement metadata = document.RootElement;
            // Responses that do not describe the requested coordinate establish no state.
            if (metadata.GetProperty("name").GetString() != file.Name ||
                metadata.GetProperty("version").GetString() != file.Version ||
                !Uri.TryCreate(metadata.GetProperty("dist").GetProperty("tarball").GetString(),
                    UriKind.Absolute, out Uri? tarball))
                return Result(ReleaseFileState.Unknown);
            ValidateUri(tarball);
            (status, RegistryOriginalBytes served) = await ReadAsync("tarball", tarball,
                (stream, length, read) => RegistryBody.OriginalAsync(stream, length, file.Size,
                    retain: false, read));
            if (status != 200) return Result(ReleaseFileState.Unknown);
            return Result(served.Size == file.Size && served.Sha256 == file.Sha256
                ? ReleaseFileState.Exact : ReleaseFileState.Conflict, served.Size, served.Sha256);
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
            return Result(ReleaseFileState.Unknown);
        }

        NpmjsObservationResult Result(ReleaseFileState state, long? size = null,
            string? sha256 = null) => new(new(prepared.Subject,
                [new(file.Output, file.Name, file.Version, state, size, sha256)],
                ["native-metadata-visibility"],
                [new("native-metadata-visibility", state is ReleaseFileState.Exact or
                    ReleaseFileState.Absent ? ReleaseCheckStatus.Passed :
                        ReleaseCheckStatus.Failed)]), [.. reads]);

        // The platform handler follows any redirect; an unfollowed 3xx is not success.
        async Task<(int, T)> ReadAsync<T>(string stage, Uri uri,
            Func<Stream, long?, CancellationToken, Task<T>> read)
        {
            ValidateUri(uri);
            using var requestTimer = new CancellationTokenSource(TimeSpan.FromSeconds(20),
                timeProvider ?? TimeProvider.System);
            using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(
                deadline.Token, requestTimer.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd(stage == "metadata"
                ? "application/vnd.npm.install-v1+json, application/json"
                : "application/octet-stream");
            request.Headers.AcceptEncoding.ParseAdd("identity");
            request.Headers.CacheControl = new() { NoCache = true };
            using HttpResponseMessage response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, requestDeadline.Token);
            int status = (int)response.StatusCode;
            reads.Add(new(stage, uri.AbsoluteUri, status, "Received response headers."));
            if (response.Content.Headers.ContentEncoding.Any(value =>
                !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unsupported response encoding.");
            using Stream stream = await response.Content.ReadAsStreamAsync(
                requestDeadline.Token);
            T body = await read(stream, response.Content.Headers.ContentLength,
                requestDeadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            requestDeadline.Token.ThrowIfCancellationRequested();
            return (status, body);
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
