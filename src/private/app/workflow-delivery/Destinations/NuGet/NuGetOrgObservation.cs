using System.Text.Json;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Versioning;
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
    private const string Host = "api.nuget.org";

    internal async Task<ReleaseObservationResult> ObserveAsync(PackageDestination prepared,
        string credential, CancellationToken token)
    {
        if (prepared.Subject.Channel != "official" || prepared.Subject.Destination != "nugetorg" ||
            prepared.Files is not [{ Kind: "nuget-package" }] ||
            prepared.Files[0].Name != prepared.Package ||
            credential.Length != 0)
            throw new InvalidDataException("Unsupported nuget.org NuGet request.");
        ReleaseFile file = prepared.Files[0];
        var identity = new PackageIdentity(file.Name, NuGetVersion.Parse(file.Version));
        string packageId = identity.Id.ToLowerInvariant();
        using var session = new DestinationHttpSession(client, clock,
            TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(30), token, maxRequests: 2);
        try
        {
            (int status, byte[] body) = await ReadAsync("service-index", new(ServiceIndex));
            DestinationHttpSession.RequireOk(status);
            var index = NuGetMetadata.ServiceIndex(body);
            Uri packageBase = Endpoint(index, "PackageBaseAddress/3.0.0");
            Uri publish = Endpoint(index, "PackagePublish/2.0.0");
            if (packageBase.AbsoluteUri.TrimEnd('/') + "/" != PackageBaseAddress ||
                publish.AbsoluteUri.TrimEnd('/') + "/" != NuGetPublisher.NuGetOrgEndpoint)
                throw new InvalidDataException("Different nuget.org native endpoint.");
            string root = PackageBaseAddress + Uri.EscapeDataString(packageId) + "/";
            (status, body) = await ReadAsync("native-versions", new(root + "index.json"));
            if (status == 404) return Result(ReleaseFileState.Absent);
            DestinationHttpSession.RequireOk(status);
            HashSet<NuGetVersion> versions = NuGetMetadata.Versions(body);
            session.Reads.Add(new("native-versions", null, "Read the published version list."));
            return Result(versions.Contains(identity.Version) ? ReleaseFileState.Available
                : ReleaseFileState.Absent);
        }
        catch (OperationCanceledException)
        {
            session.Reads.Add(new("operation", null, token.IsCancellationRequested
                ? "Cancelled." : "Read deadline exceeded."));
            return Result(ReleaseFileState.Unknown);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or
            InvalidDataException or JsonException or ArgumentException or InvalidOperationException
            or FormatException or OverflowException or KeyNotFoundException)
        {
            session.Reads.Add(new("operation", null,
                "Required native response unavailable or invalid."));
            return Result(ReleaseFileState.Unknown);
        }

        ReleaseObservationResult Result(ReleaseFileState state) => new(new(prepared.Subject,
                [new(file.Output, file.Name, file.Version, state, null, null)],
                RequiredChecks,
                [new("native-metadata-visibility",
                    state is ReleaseFileState.Available or ReleaseFileState.Absent
                        ? ReleaseCheckStatus.Passed : ReleaseCheckStatus.Failed)]),
                [.. session.Reads]);

        Task<(int, byte[])> ReadAsync(string stage, Uri uri) => session.MetadataAsync(stage, uri,
            Host, "application/json", NuGetMetadata.Limit, allowQuery: false);
    }

    private static Uri Endpoint(ServiceIndexResourceV3 index, string type) =>
        index.GetServiceEntryUris(type).Distinct().ToArray() is [Uri endpoint]
            ? endpoint : throw new InvalidDataException();
}
