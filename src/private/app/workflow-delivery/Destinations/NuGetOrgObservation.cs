using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Versioning;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

// nuget.org is public and its trusted-publishing policy owns package control, so its
// observation reads only anonymous native state. NuGet owns service-index and archive
// interpretation; the gallery adds only its repository signature to the uploaded original.
internal sealed class NuGetOrgObservation(HttpClient client, TimeProvider? clock = null)
{
    internal const string ServiceIndex = "https://api.nuget.org/v3/index.json";
    internal const string PackageBaseAddress = "https://api.nuget.org/v3-flatcontainer/";
    internal static readonly string[] RequiredChecks = ["native-metadata-visibility"];
    private const int MetadataLimit = 2 * 1024 * 1024;
    // Bounds the repository signature that the gallery adds to the qualified original.
    private const int SignatureAllowance = 1024 * 1024;

    internal async Task<NuGetObservationResult> ObserveAsync(PreparedRelease prepared,
        string credential, CancellationToken token)
    {
        if (prepared.Subject.Channel != "official" || prepared.Subject.Destination != "nugetorg" ||
            prepared.Files is not [{ Kind: "nuget-package" }] ||
            prepared.Files[0].Name != NuGetReleaseUnits.Admit(prepared.Subject).PackageId ||
            credential.Length != 0)
            throw new InvalidDataException("Unsupported nuget.org NuGet request.");
        ReleaseFile file = prepared.Files[0];
        var identity = new PackageIdentity(file.Name, NuGetVersion.Parse(file.Version));
        string packageId = identity.Id.ToLowerInvariant();
        string version = identity.Version.ToNormalizedString().ToLowerInvariant();
        var reads = new List<NuGetRead>();
        NuGetResources? resources = null;
        int sends = 0;
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(90),
            clock ?? TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, timer.Token);
        try
        {
            (int status, byte[] body) = await ReadAsync("service-index", new(ServiceIndex),
                MetadataLimit);
            RequireOk(status);
            var index = new ServiceIndexResourceV3(Object(body), DateTime.UtcNow);
            Uri packageBase = Endpoint(index, "PackageBaseAddress/3.0.0");
            Uri publish = Endpoint(index, "PackagePublish/2.0.0");
            if (packageBase.AbsoluteUri.TrimEnd('/') + "/" != PackageBaseAddress ||
                publish.AbsoluteUri.TrimEnd('/') + "/" != NuGetPublisher.NuGetOrgEndpoint)
                throw new InvalidDataException("Different nuget.org native endpoint.");
            resources = new(PackageBaseAddress, NuGetPublisher.NuGetOrgEndpoint);

            string root = PackageBaseAddress + Uri.EscapeDataString(packageId) + "/";
            (status, body) = await ReadAsync("native-versions", new(root + "index.json"),
                MetadataLimit);
            if (status == 404) return Result(ReleaseFileState.Absent);
            RequireOk(status);
            var versions = new HashSet<NuGetVersion>(VersionComparer.VersionRelease);
            foreach (JToken item in RequiredArray(Object(body)["versions"]))
                if (item.Type != JTokenType.String ||
                    !versions.Add(NuGetVersion.Parse(item.Value<string>()!)))
                    throw new InvalidDataException();
            if (!versions.Contains(identity.Version)) return Result(ReleaseFileState.Absent);

            string archiveUrl = root + Uri.EscapeDataString(version) + "/" +
                Uri.EscapeDataString(packageId + "." + version + ".nupkg");
            (status, body) = await ReadAsync("original-archive", new(archiveUrl),
                (int)Math.Min(file.Size + SignatureAllowance, Array.MaxLength), archive: true);
            RequireOk(status);
            using (var served = new MemoryStream(body, writable: false))
            using (var archive = new PackageArchiveReader(served))
                if (!PackageIdentityComparer.Default.Equals(archive.GetIdentity(), identity))
                    return Result(ReleaseFileState.Conflict);
            (long size, string digest) = await OriginalAsync(body, lifetime.Token);
            reads.Add(new("original-archive", null, size == body.LongLength
                ? "Served archive is the uploaded original."
                : "Served archive is the uploaded original plus its repository signature."));
            return Result(size == file.Size && digest == file.Sha256 ? ReleaseFileState.Exact
                : ReleaseFileState.Conflict, size, digest);
        }
        catch (OperationCanceledException)
        {
            reads.Add(new("operation", null, token.IsCancellationRequested
                ? "Cancelled." : "Read deadline exceeded."));
            return Result(ReleaseFileState.Unknown);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or
            InvalidDataException or JsonException or ArgumentException or InvalidOperationException
            or PackagingException or FormatException or OverflowException or
            SignatureException or CryptographicException)
        {
            reads.Add(new("operation", null, "Required native response unavailable or invalid."));
            return Result(ReleaseFileState.Unknown);
        }

        NuGetObservationResult Result(ReleaseFileState state, long? size = null,
            string? digest = null) => new(new(prepared.Subject,
                [new(file.Output, file.Name, file.Version, state, size, digest)],
                RequiredChecks,
                [new("native-metadata-visibility",
                    state is ReleaseFileState.Exact or ReleaseFileState.Absent
                        ? ReleaseCheckStatus.Passed : ReleaseCheckStatus.Failed)]), resources,
                [.. reads]);

        // The platform handler follows the archive's storage redirect; the archive bytes are
        // then compared with the qualified original.
        async Task<(int, byte[])> ReadAsync(string stage, Uri uri, int limit,
            bool archive = false)
        {
            ValidateOrigin(uri);
            if (++sends > 10) throw new IOException("Observation read allowance exhausted.");
            using var requestTimer = new CancellationTokenSource(TimeSpan.FromSeconds(30),
                clock ?? TimeProvider.System);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,
                requestTimer.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("WorkflowDelivery");
            request.Headers.Accept.ParseAdd(archive ? "application/octet-stream"
                : "application/json");
            request.Headers.AcceptEncoding.ParseAdd("identity");
            using HttpResponseMessage response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            int status = (int)response.StatusCode;
            reads.Add(new(stage, status, "Received response headers."));
            if (response.Content.Headers.ContentEncoding.Any(value =>
                !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unsupported response encoding.");
            using Stream content = await response.Content.ReadAsStreamAsync(deadline.Token);
            byte[] body = await RegistryBody.LimitedAsync(content,
                response.Content.Headers.ContentLength, limit, deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            return (status, body);
        }
    }

    // The size and digest of the uploaded original that the served archive carries: the served
    // bytes themselves when unsigned, or with only the repository signature removed by NuGet.
    internal static async Task<(long Size, string Sha256)> OriginalAsync(byte[] served,
        CancellationToken token)
    {
        using var input = new MemoryStream(served, writable: false);
        bool signed;
        using (var reader = new PackageArchiveReader(input, leaveStreamOpen: true))
            signed = await reader.IsSignedAsync(token);
        if (!signed) return (served.LongLength, Convert.ToHexStringLower(SHA256.HashData(served)));
        input.Position = 0;
        using var output = new MemoryStream();
        if (!await SignedPackageArchiveUtility.RemoveRepositorySignaturesAsync(input, output,
                token))
            throw new InvalidDataException("Served archive carries a non-repository signature.");
        byte[] original = output.ToArray();
        using (var stripped = new MemoryStream(original, writable: false))
        using (var reader = new PackageArchiveReader(stripped))
            if (await reader.IsSignedAsync(token))
                throw new InvalidDataException("Served archive carries an author signature.");
        return (original.LongLength, Convert.ToHexStringLower(SHA256.HashData(original)));
    }

    private static void RequireOk(int status)
    {
        if (status != 200) throw new IOException("Required native response not successful.");
    }

    private static Uri Endpoint(ServiceIndexResourceV3 index, string type) =>
        index.GetServiceEntryUris(type).Distinct().ToArray() is [Uri endpoint]
            ? endpoint : throw new InvalidDataException();

    private static void ValidateOrigin(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Host != "api.nuget.org" ||
            uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 ||
            uri.Fragment.Length != 0)
            throw new InvalidDataException("Unsupported native origin.");
    }

    private static JToken Parse(byte[] bytes) => JToken.Parse(new UTF8Encoding(false, true)
        .GetString(bytes),
        new JsonLoadSettings
            { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
    private static JObject Object(byte[] bytes) => Parse(bytes) as JObject ??
        throw new InvalidDataException("Expected native metadata object.");
    private static JArray RequiredArray(JToken? token) => token as JArray ??
        throw new InvalidDataException("Expected complete native version array.");
}
