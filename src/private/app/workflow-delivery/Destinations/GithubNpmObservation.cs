using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record GithubNpmRead(string Stage, int? Status, string Detail);
internal sealed record GithubNpmObservationResult(ReleaseObservation Observation,
    GithubNpmRead[] Reads, string? TarballUrlSha256 = null);

// Native package control and exact originals are the destination contract.
// No approval mirror, version reservation, package witness or tag certificate.
internal sealed class GithubNpmObservation(HttpClient client, TimeProvider? clock = null)
{
    internal const string Scope = "@hcoona/";
    internal const string Registry = "https://npm.pkg.github.com";
    private const string Api = "https://api.github.com/users/hcoona/packages/npm/";

    // The registered unit's Buddy package is the only coordinate this destination reads.
    internal static string Package(PreparedRelease prepared)
    {
        string package = NpmReleaseUnits.Admit(prepared.Subject).BuddyName;
        if (prepared.Subject.Channel != "buddy" ||
            !package.StartsWith(Scope, StringComparison.Ordinal) || prepared.Files is not
                [{ Kind: "npm-tarball" } file] || file.Name != package)
            throw new InvalidDataException("Unsupported GitHub npm request.");
        return package;
    }

    internal async Task<GithubNpmObservationResult> ObserveAsync(PreparedRelease prepared,
        string credential, CancellationToken token)
    {
        string bare = Package(prepared)[Scope.Length..];
        if (credential.Length == 0 || credential.Any(c => c is '\r' or '\n' or '\0'))
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
            (int status, byte[] body) = await ReadAsync("package", new(Api + bare), Metadata);
            // An unavailable or unreadable package-control response cannot establish state.
            if (status != 200) return Result(ReleaseFileState.Unknown);
            using (JsonDocument document = JsonDocument.Parse(body))
            {
                JsonElement package = document.RootElement;
                Unique(package);
                if (Text(package, "name") is not { } name ||
                    (name != bare && name != file.Name) ||
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
                + bare), Metadata);
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
            (status, RegistryOriginalBytes served) = await ReadAsync("tarball", archive,
                (stream, length, read) => RegistryBody.OriginalAsync(stream, length, file.Size,
                    retain: false, read));
            if (status != 200) return Result(ReleaseFileState.Unknown);
            return Result(served.Size == file.Size && served.Sha256 == file.Sha256
                ? ReleaseFileState.Exact : ReleaseFileState.Conflict, served.Size, served.Sha256);
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
        static Task<byte[]> Metadata(Stream stream, long? length, CancellationToken read) =>
            RegistryBody.LimitedAsync(stream, length, 1_000_000, read);

        async Task<(int, T)> ReadAsync<T>(string stage, Uri uri,
            Func<Stream, long?, CancellationToken, Task<T>> read)
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
            using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            T body = await read(stream, response.Content.Headers.ContentLength, deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            return (status, body);
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
