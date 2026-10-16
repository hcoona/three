using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Versioning;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record GithubNuGetRead(string Stage, int? Status, string Detail);
internal sealed record GithubNuGetResources(string PackageBaseAddress, string PackagePublish);
internal sealed record GithubNuGetObservationResult(ReleaseObservation Observation,
    GithubNuGetResources? Resources, GithubNuGetRead[] Reads);

// NuGet owns service-index, version and archive interpretation. The application
// constrains its request and compares qualified originals with selected native state.
internal sealed class GithubNuGetObservation(HttpClient client, TimeProvider? clock = null)
{
    internal const string Package = "Hcoona.ReleaseSmoke.GithubPackages";
    internal const string ServiceIndex = "https://nuget.pkg.github.com/hcoona/index.json";
    private const string Api = "https://api.github.com/users/hcoona/packages/nuget/"
        + "hcoona.releasesmoke.githubpackages";
    private const int MetadataLimit = 2 * 1024 * 1024;

    internal async Task<GithubNuGetObservationResult> ObserveAsync(PreparedRelease prepared,
        string credential, CancellationToken token)
    {
        if (prepared.Subject.Channel != "buddy" ||
            prepared.Subject.Destination != "github-packages-nuget" || prepared.Files is not
                [{ Kind: "nuget-package" }] ||
            !StringComparer.OrdinalIgnoreCase.Equals(prepared.Files[0].Name, Package) ||
            string.IsNullOrEmpty(credential) || credential.Any(c => c is '\r' or '\n' or '\0'))
            throw new InvalidDataException("Unsupported GitHub NuGet request.");
        ReleaseFile file = prepared.Files[0];
        var identity = new PackageIdentity(file.Name, NuGetVersion.Parse(file.Version));
        string packageId = identity.Id.ToLowerInvariant();
        string version = identity.Version.ToNormalizedString().ToLowerInvariant();
        var reads = new List<GithubNuGetRead>();
        GithubNuGetResources? resources = null;
        ReleaseCheckStatus control = ReleaseCheckStatus.Missing;
        int sends = 0;
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(90),
            clock ?? TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, timer.Token);
        try
        {
            (int status, byte[] body) = await ReadAsync("service-index", new(ServiceIndex));
            RequireOk(status);
            var index = new ServiceIndexResourceV3(Object(body), DateTime.UtcNow);
            Uri packageBase = Endpoint(index, "PackageBaseAddress/3.0.0");
            Uri publish = Endpoint(index, "PackagePublish/2.0.0");
            resources = new(packageBase.AbsoluteUri.TrimEnd('/') + "/", publish.AbsoluteUri);

            (status, body) = await ReadAsync("package-control", new(Api));
            RequireOk(status);
            JObject package = Object(body);
            if (!StringComparer.OrdinalIgnoreCase.Equals(Text(package, "name"), Package) ||
                Text(package, "package_type") != "nuget" || Text(package, "visibility") !=
                    "public" || Text(RequiredObject(package["repository"]), "full_name") !=
                    "hcoona/three" || package["owner"] is { Type: not JTokenType.Null } owner &&
                    Text(RequiredObject(owner), "login") != "hcoona")
                return Result(ReleaseFileState.Conflict);
            control = ReleaseCheckStatus.Passed;

            var githubVersions = new HashSet<NuGetVersion>(VersionComparer.VersionRelease);
            var ids = new HashSet<long>();
            for (int page = 1; ; page++)
            {
                (status, body) = await ReadAsync("github-versions",
                    new(Api + "/versions?state=active&per_page=100&page=" + page));
                RequireOk(status);
                JArray items = RequiredArray(Parse(body));
                if (items.Count > 100) throw new InvalidDataException();
                foreach (JToken item in items)
                {
                    JObject entry = RequiredObject(item);
                    if (entry["id"]?.Type != JTokenType.Integer ||
                        entry["id"]!.Value<long>() <= 0 || !ids.Add(entry["id"]!.Value<long>()) ||
                        !githubVersions.Add(NuGetVersion.Parse(Text(entry, "name"))))
                        throw new InvalidDataException();
                }
                if (items.Count < 100) break;
            }

            string root = resources.PackageBaseAddress + Uri.EscapeDataString(packageId) + "/";
            (status, body) = await ReadAsync("native-versions", new(root + "index.json"));
            RequireOk(status);
            var nativeVersions = new HashSet<NuGetVersion>(VersionComparer.VersionRelease);
            foreach (JToken item in RequiredArray(Object(body)["versions"]))
                if (item.Type != JTokenType.String ||
                    !nativeVersions.Add(NuGetVersion.Parse(item.Value<string>()!)))
                    throw new InvalidDataException();
            if (!nativeVersions.SetEquals(githubVersions))
                return Result(ReleaseFileState.Unknown);
            if (!nativeVersions.Contains(identity.Version)) return Result(ReleaseFileState.Absent);

            string archiveUrl = root + Uri.EscapeDataString(version) + "/" +
                Uri.EscapeDataString(packageId + "." + version + ".nupkg");
            (status, RegistryOriginalBytes served) = await ReadBodyAsync("original-archive",
                new(archiveUrl), (stream, length, read) => RegistryBody.OriginalAsync(stream,
                    length, file.Size, retain: true, read), archive: true);
            RequireOk(status);
            if (served.Size != file.Size || served.Sha256 != file.Sha256)
                return Result(ReleaseFileState.Conflict, served.Size, served.Sha256);
            using var stream = new MemoryStream(served.Bytes!, writable: false);
            using var archive = new PackageArchiveReader(stream);
            if (!PackageIdentityComparer.Default.Equals(archive.GetIdentity(), identity))
                return Result(ReleaseFileState.Conflict);
            return Result(ReleaseFileState.Exact, served.Size, served.Sha256);
        }
        catch (OperationCanceledException)
        {
            reads.Add(new("operation", null, token.IsCancellationRequested
                ? "Cancelled." : "Read deadline exceeded."));
            return Result(ReleaseFileState.Unknown);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or
            InvalidDataException or JsonException or ArgumentException or InvalidOperationException
            or PackagingException or FormatException or OverflowException)
        {
            reads.Add(new("operation", null, "Required native response unavailable or invalid."));
            return Result(ReleaseFileState.Unknown);
        }

        GithubNuGetObservationResult Result(ReleaseFileState state, long? size = null,
            string? digest = null) => new(new(prepared.Subject,
                [new(file.Output, file.Name, file.Version, state, size, digest)],
                ["native-package-control", "native-metadata-visibility"],
                [new("native-package-control", control), new("native-metadata-visibility",
                    state is ReleaseFileState.Exact or ReleaseFileState.Absent
                        ? ReleaseCheckStatus.Passed : ReleaseCheckStatus.Failed)]), resources,
                [.. reads]);

        // The platform handler follows the archive's storage redirect and clears
        // Authorization on each hop; the archive bytes are then compared below.
        Task<(int, byte[])> ReadAsync(string stage, Uri uri) => ReadBodyAsync(stage, uri,
            (stream, length, read) => RegistryBody.LimitedAsync(stream, length, MetadataLimit,
                read));

        async Task<(int, T)> ReadBodyAsync<T>(string stage, Uri uri,
            Func<Stream, long?, CancellationToken, Task<T>> read, bool archive = false)
        {
            ValidateOrigin(uri, stage.StartsWith("github", StringComparison.Ordinal) ||
                stage == "package-control" ? "api.github.com" : "nuget.pkg.github.com");
            if (++sends > 80) throw new IOException("Observation read allowance exhausted.");
            using var requestTimer = new CancellationTokenSource(TimeSpan.FromSeconds(30),
                clock ?? TimeProvider.System);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,
                requestTimer.Token);
            bool api = uri.Host == "api.github.com";
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = api ? new("Bearer", credential)
                : new("Basic", Convert.ToBase64String(
                    Encoding.UTF8.GetBytes("hcoona:" + credential)));
            request.Headers.UserAgent.ParseAdd("WorkflowDelivery");
            request.Headers.Accept.ParseAdd(archive ? "application/octet-stream"
                : api ? "application/vnd.github+json" : "application/json");
            request.Headers.AcceptEncoding.ParseAdd("identity");
            if (api) request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using HttpResponseMessage response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            int status = (int)response.StatusCode;
            reads.Add(new(stage, status, "Received response headers."));
            if (response.Content.Headers.ContentEncoding.Any(value =>
                !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unsupported response encoding.");
            using Stream content = await response.Content.ReadAsStreamAsync(deadline.Token);
            T body = await read(content, response.Content.Headers.ContentLength, deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            return (status, body);
        }
    }

    private static void RequireOk(int status)
    {
        if (status != 200) throw new IOException("Required native response not successful.");
    }

    private static Uri Endpoint(ServiceIndexResourceV3 index, string type)
    {
        Uri[] endpoints = index.GetServiceEntryUris(type).Distinct().ToArray();
        if (endpoints is not [Uri endpoint]) throw new InvalidDataException();
        ValidateOrigin(endpoint, "nuget.pkg.github.com");
        if (endpoint.Query.Length != 0 || !(endpoint.AbsolutePath == "/hcoona" ||
                endpoint.AbsolutePath.StartsWith("/hcoona/", StringComparison.Ordinal)))
            throw new InvalidDataException("Different native owner endpoint.");
        return endpoint;
    }

    private static void ValidateOrigin(Uri uri, string host)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Host != host ||
            uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Unsupported native origin.");
    }

    private static JToken Parse(byte[] bytes) => JToken.Parse(new UTF8Encoding(false, true)
        .GetString(bytes),
        new JsonLoadSettings
            { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
    private static JObject Object(byte[] bytes) => RequiredObject(Parse(bytes));
    private static JObject RequiredObject(JToken? token) => token as JObject ??
        throw new InvalidDataException("Expected native metadata object.");
    private static JArray RequiredArray(JToken? token) => token as JArray ??
        throw new InvalidDataException("Expected complete native version array.");
    private static string Text(JObject value, string name) =>
        value[name] is { Type: JTokenType.String } token ? token.Value<string>()! :
            throw new InvalidDataException("Missing native identity field.");
}
