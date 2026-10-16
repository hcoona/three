using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record GithubNpmRead(string Stage, int? Status, string Detail);
internal sealed record GithubNpmObservationResult(ReleaseObservation Observation,
    GithubNpmRead[] Reads, string? TarballUrlSha256 = null);

// Native package control and exact originals are the destination contract.
// No approval mirror, version reservation, package witness or tag certificate.
internal sealed class GithubNpmObservation(HttpClient client, TimeProvider? clock = null)
{
    internal const string Package = "@hcoona/hcoona-release-smoke-npm";
    internal const string Registry = "https://npm.pkg.github.com";
    private const string Api = "https://api.github.com/users/hcoona/packages/npm/"
        + "hcoona-release-smoke-npm";

    internal async Task<GithubNpmObservationResult> ObserveAsync(PreparedRelease prepared,
        string credential, CancellationToken token)
    {
        if (prepared.Subject.Channel != "buddy" ||
            prepared.Subject.Destination != "github-packages-npm" || prepared.Files is not
                [{ Kind: "npm-tarball", Name: Package, Size: <= 25_000_000 }] ||
            credential.Length == 0 || credential.Any(c => c is '\r' or '\n' or '\0'))
            throw new InvalidDataException("Unsupported GitHub npm request or read credential.");
        ReleaseFile file = prepared.Files[0];
        var reads = new List<GithubNpmRead>();
        ReleaseCheckStatus control = ReleaseCheckStatus.Missing;
        string? tarballUrlSha256 = null;
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(60),
            clock ?? TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, timer.Token);
        try
        {
            (int status, byte[] body) = await ReadAsync("package", new(Api), 1_000_000);
            // An unavailable or unreadable package-control response cannot establish state.
            if (status != 200) return Result(ReleaseFileState.Unknown);
            using (JsonDocument document = JsonDocument.Parse(body))
            {
                JsonElement package = document.RootElement;
                Unique(package);
                if (Text(package, "name") is not ("hcoona-release-smoke-npm" or Package) ||
                    Text(package, "package_type") != "npm" ||
                    Text(package, "visibility") != "public" ||
                    Text(package.GetProperty("repository"), "full_name") != "hcoona/three")
                    return Result(ReleaseFileState.Conflict);
                // GitHub's user route establishes ownership when owner is null.
                if (package.TryGetProperty("owner", out JsonElement owner) &&
                    owner.ValueKind != JsonValueKind.Null && Text(owner, "login") != "hcoona")
                    return Result(ReleaseFileState.Conflict);
                control = ReleaseCheckStatus.Passed;
            }
            (status, body) = await ReadAsync("metadata", new(Registry + "/@hcoona%2F"
                + "hcoona-release-smoke-npm"), 1_000_000);
            // A missing provisioned package response cannot authorize creation.
            if (status != 200) return Result(ReleaseFileState.Unknown);
            using JsonDocument metadata = JsonDocument.Parse(body);
            JsonElement root = metadata.RootElement;
            Unique(root);
            // Responses that do not describe the requested coordinate establish no state.
            if (Text(root, "name") != file.Name ||
                root.GetProperty("versions").ValueKind != JsonValueKind.Object)
                return Result(ReleaseFileState.Unknown);
            if (!root.GetProperty("versions").TryGetProperty(file.Version, out JsonElement version))
                return Result(ReleaseFileState.Absent);
            if (Text(version, "name") != file.Name || Text(version, "version") != file.Version ||
                !Uri.TryCreate(Text(version.GetProperty("dist"), "tarball"),
                    UriKind.Absolute, out Uri? archive))
                return Result(ReleaseFileState.Unknown);
            ValidateUri(archive, "tarball");
            tarballUrlSha256 = Convert.ToHexStringLower(SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(archive.AbsoluteUri)));
            (status, body) = await ReadAsync("tarball", archive, 25_000_000);
            if (status != 200) return Result(ReleaseFileState.Unknown);
            string digest = Convert.ToHexStringLower(SHA256.HashData(body));
            return Result(body.LongLength == file.Size && digest == file.Sha256
                ? ReleaseFileState.Exact : ReleaseFileState.Conflict, body.LongLength, digest);
        }
        catch (OperationCanceledException)
        {
            reads.Add(new("operation", null, token.IsCancellationRequested
                ? "Cancelled." : "Read deadline exceeded."));
            return Result(ReleaseFileState.Unknown);
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException)
        {
            reads.Add(new("operation", null, "Complete response unavailable."));
            return Result(ReleaseFileState.Unknown);
        }
        catch (Exception failure) when (failure is InvalidDataException or JsonException or
            KeyNotFoundException or InvalidOperationException or UriFormatException)
        {
            reads.Add(new("operation", null, "Unsupported native identity or response."));
            return Result(ReleaseFileState.Unknown);
        }

        GithubNpmObservationResult Result(ReleaseFileState state, long? size = null,
            string? digest = null) => new(new(prepared.Subject,
                [new(file.Output, file.Name, file.Version, state, size, digest)],
                ["native-package-control", "native-metadata-visibility"],
                [new("native-package-control", control), new("native-metadata-visibility",
                    state is ReleaseFileState.Absent or ReleaseFileState.Exact
                        ? ReleaseCheckStatus.Passed : ReleaseCheckStatus.Failed)]), [.. reads],
                tarballUrlSha256);

        // The platform handler follows storage redirects and clears Authorization on
        // each hop; tarball bytes are compared with the original above.
        async Task<(int, byte[])> ReadAsync(string stage, Uri uri, int limit)
        {
            ValidateUri(uri, stage);
            using var requestTimer = new CancellationTokenSource(TimeSpan.FromSeconds(20),
                clock ?? TimeProvider.System);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
                lifetime.Token, requestTimer.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (uri.Host is "api.github.com" or "npm.pkg.github.com")
                request.Headers.Authorization = new("Bearer", credential);
            request.Headers.Accept.ParseAdd(stage == "package"
                ? "application/vnd.github+json" : stage == "metadata"
                    ? "application/vnd.npm.install-v1+json, application/json"
                    : "application/octet-stream");
            request.Headers.UserAgent.ParseAdd("WorkflowDelivery");
            if (stage == "package") request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.AcceptEncoding.ParseAdd("identity");
            request.Headers.CacheControl = new() { NoCache = true };
            using HttpResponseMessage response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            int status = (int)response.StatusCode;
            // URLs can contain signed storage credentials; never retain them.
            reads.Add(new(stage, status, "Received response headers."));
            if (response.Content.Headers.ContentEncoding.Any(value =>
                    !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unsupported response encoding.");
            long? length = response.Content.Headers.ContentLength;
            if (length > limit) throw new IOException("Response exceeds its body limit.");
            using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var body = new MemoryStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, deadline.Token)) != 0)
            {
                if (body.Length + count > limit)
                    throw new IOException("Response exceeds its body limit.");
                await body.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
            }
            if (length is not null && body.Length != length)
                throw new IOException("Incomplete response body.");
            deadline.Token.ThrowIfCancellationRequested();
            return (status, body.ToArray());
        }
    }

    private static string? Text(JsonElement value, string name) =>
        value.GetProperty(name).GetString();

    private static void ValidateUri(Uri uri, string stage)
    {
        bool host = stage == "package" ? uri.Host == "api.github.com"
            : stage == "metadata" ? uri.Host == "npm.pkg.github.com"
            : uri.Host is "npm.pkg.github.com" or "pkg-npm.githubusercontent.com"
                or "objects.githubusercontent.com" or "github-registry-files.githubusercontent.com";
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || !host || uri.Port != 443 ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Unsupported native endpoint.");
    }

    private static void Unique(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected native metadata object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty field in value.EnumerateObject())
        {
            if (!names.Add(field.Name))
                throw new InvalidDataException("Duplicate native metadata.");
            if (field.Value.ValueKind == JsonValueKind.Object) Unique(field.Value);
        }
    }
}
