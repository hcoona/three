using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Versioning;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Release;

using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Destinations;

internal sealed record NuGetResources(string PackageBaseAddress, string PackagePublish);

// NuGet owns service-index, version and archive interpretation. The application
// constrains its request and compares qualified originals with selected native state.
internal sealed class GithubNuGetObservation(HttpClient client, TimeProvider? clock = null)
{
    internal const string ServiceIndex = "https://nuget.pkg.github.com/hcoona/index.json";
    internal static readonly string[] RequiredChecks =
        ["native-package-control", "native-metadata-visibility"];
    private const string Host = "nuget.pkg.github.com";

    internal async Task<ReleaseObservationResult> ObserveAsync(PreparedRelease prepared,
        string credential, CancellationToken token)
    {
        if (prepared.Subject.Channel != "buddy" ||
            prepared.Subject.Destination != "github-packages-nuget" || prepared.Files is not
                [{ Kind: "nuget-package" }] ||
            prepared.Files[0].Name != NuGetReleaseUnits.Admit(prepared.Subject).PackageId ||
            string.IsNullOrEmpty(credential) || credential.Any(c => c is '\r' or '\n' or '\0'))
            throw new InvalidDataException("Unsupported GitHub NuGet request.");
        ReleaseFile file = prepared.Files[0];
        var identity = new PackageIdentity(file.Name, NuGetVersion.Parse(file.Version));
        string packageId = identity.Id.ToLowerInvariant();
        string version = identity.Version.ToNormalizedString().ToLowerInvariant();
        Uri api = GithubApi.UserPackage("nuget", packageId);
        NuGetResources? resources = null;
        ReleaseCheckStatus control = ReleaseCheckStatus.Missing;
        using var session = new DestinationHttpSession(client, clock,
            TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(30), token, maxRequests: 80);
        var github = new GithubApi(session, credential, NuGetMetadata.Limit);
        var authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("hcoona:" + credential)));
        try
        {
            (int status, byte[] body) = await ReadAsync("service-index", new(ServiceIndex));
            DestinationHttpSession.RequireOk(status);
            var index = NuGetMetadata.ServiceIndex(body);
            Uri packageBase = Endpoint(index, "PackageBaseAddress/3.0.0");
            Uri publish = Endpoint(index, "PackagePublish/2.0.0");
            // The live index lists the push resource without a trailing slash; the client
            // pushes to it with one, which is the endpoint the publisher admits.
            resources = new(packageBase.AbsoluteUri.TrimEnd('/') + "/",
                publish.AbsoluteUri.TrimEnd('/') + "/");

            string root = resources.PackageBaseAddress + Uri.EscapeDataString(packageId) + "/";
            ReleaseFileState ownership = await github.CheckPackageAsync("package-control", "nuget",
                packageId, name => StringComparer.OrdinalIgnoreCase.Equals(name, identity.Id),
                allowAbsent: true);
            // An owner package that does not exist yet is created by the first publication,
            // which links it to the packed repository; the feed must agree it is absent.
            if (ownership == ReleaseFileState.Absent)
            {
                (status, _) = await ReadAsync("native-versions", new(root + "index.json"));
                if (status != 404) return Result(ReleaseFileState.Unknown);
                control = ReleaseCheckStatus.Passed;
                return Result(ReleaseFileState.Absent);
            }
            if (ownership != ReleaseFileState.Exact) return Result(ownership);
            control = ReleaseCheckStatus.Passed;

            var githubVersions = new HashSet<NuGetVersion>(VersionComparer.VersionRelease);
            var ids = new HashSet<long>();
            await github.ReadArrayAsync("github-versions",
                new(api.AbsoluteUri + "/versions?state=active"), entry =>
                {
                    long id = entry.GetProperty("id").GetInt64();
                    if (id <= 0 || !ids.Add(id) ||
                        !githubVersions.Add(NuGetVersion.Parse(
                            StrictJson.Text(entry.GetProperty("name")))))
                        throw new InvalidDataException();
                });

            (status, body) = await ReadAsync("native-versions", new(root + "index.json"));
            DestinationHttpSession.RequireOk(status);
            HashSet<NuGetVersion> nativeVersions = NuGetMetadata.Versions(body);
            if (!nativeVersions.SetEquals(githubVersions))
                return Result(ReleaseFileState.Unknown);
            if (!nativeVersions.Contains(identity.Version)) return Result(ReleaseFileState.Absent);

            string archiveUrl = root + Uri.EscapeDataString(version) + "/" +
                Uri.EscapeDataString(packageId + "." + version + ".nupkg");
            (status, RegistryOriginalBytes served) = await session.OriginalAsync(
                "original-archive", new(archiveUrl), Host, file.Size, retain: true, authorization);
            DestinationHttpSession.RequireOk(status);
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
            session.Reads.Add(new("operation", null, token.IsCancellationRequested
                ? "Cancelled." : "Read deadline exceeded."));
            return Result(ReleaseFileState.Unknown);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or
            InvalidDataException or JsonException or ArgumentException or InvalidOperationException
            or PackagingException or FormatException or OverflowException or KeyNotFoundException)
        {
            session.Reads.Add(new("operation", null,
                "Required native response unavailable or invalid."));
            return Result(ReleaseFileState.Unknown);
        }

        ReleaseObservationResult Result(ReleaseFileState state, long? size = null,
            string? digest = null) => new(new(prepared.Subject,
                [new(file.Output, file.Name, file.Version, state, size, digest)],
                RequiredChecks,
                [new("native-package-control", control), new("native-metadata-visibility",
                    state is ReleaseFileState.Exact or ReleaseFileState.Absent
                        ? ReleaseCheckStatus.Passed : ReleaseCheckStatus.Failed)]),
                [.. session.Reads], Resources: resources);

        Task<(int, byte[])> ReadAsync(string stage, Uri uri) => session.MetadataAsync(stage, uri,
            Host, "application/json", NuGetMetadata.Limit, authorization);
    }

    private static Uri Endpoint(ServiceIndexResourceV3 index, string type)
    {
        Uri[] endpoints = index.GetServiceEntryUris(type).Distinct().ToArray();
        if (endpoints is not [Uri endpoint]) throw new InvalidDataException();
        DestinationHttpSession.RequireHttpsOrigin(endpoint, Host, allowQuery: false);
        if (!(endpoint.AbsolutePath == "/hcoona" ||
                endpoint.AbsolutePath.StartsWith("/hcoona/", StringComparison.Ordinal)))
            throw new InvalidDataException("Different native owner endpoint.");
        return endpoint;
    }
}

internal static class NuGetMetadata
{
    internal const int Limit = 2 * 1024 * 1024;

    internal static ServiceIndexResourceV3 ServiceIndex(byte[] bytes)
    {
        try
        {
            JToken parsed = JToken.Parse(StrictJson.Utf8.GetString(bytes),
                new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                });
            return new(parsed as JObject ??
                throw new InvalidDataException("Expected native service index object."),
                DateTime.UtcNow);
        }
        catch (Newtonsoft.Json.JsonException error)
        {
            throw new InvalidDataException("Invalid native service index JSON.", error);
        }
    }

    internal static HashSet<NuGetVersion> Versions(byte[] bytes)
    {
        using JsonDocument document = StrictJson.Parse(bytes);
        var versions = new HashSet<NuGetVersion>(VersionComparer.VersionRelease);
        foreach (JsonElement entry in document.RootElement.GetProperty("versions").EnumerateArray())
            if (!versions.Add(NuGetVersion.Parse(entry.GetString()!)))
                throw new InvalidDataException("Repeated native version.");
        return versions;
    }
}
