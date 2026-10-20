using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Versioning;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

// nuget.org is public and its trusted-publishing policy owns package control, so its
// observation reads only anonymous native state. nuget.org repository-signs every package and
// its versions are immutable, so completion is the flat-container version list reporting the
// exact normalized version (WD-REL-009); the served bytes are not compared.
internal sealed class NuGetOrgObservation(HttpClient client, TimeProvider? clock = null)
{
    internal const string ServiceIndex = "https://api.nuget.org/v3/index.json";
    internal const string PackageBaseAddress = "https://api.nuget.org/v3-flatcontainer/";
    internal static readonly string[] RequiredChecks = ["native-metadata-visibility"];
    private const int MetadataLimit = 2 * 1024 * 1024;

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
            reads.Add(new("native-versions", null, "Read the published version list."));
            return Result(versions.Contains(identity.Version) ? ReleaseFileState.Available
                : ReleaseFileState.Absent);
        }
        catch (OperationCanceledException)
        {
            reads.Add(new("operation", null, token.IsCancellationRequested
                ? "Cancelled." : "Read deadline exceeded."));
            return Result(ReleaseFileState.Unknown);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or
            InvalidDataException or JsonException or ArgumentException or InvalidOperationException
            or FormatException or OverflowException)
        {
            reads.Add(new("operation", null, "Required native response unavailable or invalid."));
            return Result(ReleaseFileState.Unknown);
        }

        NuGetObservationResult Result(ReleaseFileState state) => new(new(prepared.Subject,
                [new(file.Output, file.Name, file.Version, state, null, null)],
                RequiredChecks,
                [new("native-metadata-visibility",
                    state is ReleaseFileState.Available or ReleaseFileState.Absent
                        ? ReleaseCheckStatus.Passed : ReleaseCheckStatus.Failed)]), resources,
                [.. reads]);

        async Task<(int, byte[])> ReadAsync(string stage, Uri uri, int limit)
        {
            ValidateOrigin(uri);
            if (++sends > 2) throw new IOException("Observation read allowance exhausted.");
            using var requestTimer = new CancellationTokenSource(TimeSpan.FromSeconds(30),
                clock ?? TimeProvider.System);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,
                requestTimer.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("WorkflowDelivery");
            request.Headers.Accept.ParseAdd("application/json");
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
