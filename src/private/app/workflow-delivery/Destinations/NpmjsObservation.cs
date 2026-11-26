using System.Text;
using System.Text.Json;
using WorkflowDelivery.Release;

using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Destinations;

internal sealed record NpmjsObservationResult(ReleaseObservation Observation,
    DestinationRead[] Reads) : IReleaseObservation;

// This concrete destination exposes only GET. Native registry state and original
// bytes are compared without a package witness or an application approval chain.
internal sealed class NpmjsObservation(HttpClient client, TimeProvider? timeProvider = null)
{
    private const string Host = "registry.npmjs.org";

    internal async Task<NpmjsObservationResult> ObserveAsync(PreparedRelease prepared,
        CancellationToken token)
    {
        if (prepared.Subject.Destination != "npmjs" || prepared.Files is not
            [{ Kind: "npm-tarball" }])
            throw new InvalidDataException("Unsupported complete public npmjs request.");
        ReleaseFile file = prepared.Files[0];
        using var session = new DestinationHttpSession(client, timeProvider,
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(20), token, noCache: true,
            recordUrl: true);
        try
        {
            string name = Uri.EscapeDataString(file.Name).Replace("%40", "@",
                StringComparison.Ordinal);
            var uri = new Uri($"https://{Host}/{name}/"
                + Uri.EscapeDataString(file.Version));
            (int status, byte[] body) = await session.MetadataAsync("metadata", uri, Host,
                "application/vnd.npm.install-v1+json, application/json", 1_000_000);
            if (status == 404) return Result(ReleaseFileState.Absent);
            if (status != 200) return Result(ReleaseFileState.Unknown);
            using JsonDocument document = StrictJson.Parse(body);
            JsonElement metadata = document.RootElement;
            // Responses that do not describe the requested coordinate establish no state.
            if (metadata.GetProperty("name").GetString() != file.Name ||
                metadata.GetProperty("version").GetString() != file.Version ||
                !Uri.TryCreate(metadata.GetProperty("dist").GetProperty("tarball").GetString(),
                    UriKind.Absolute, out Uri? tarball))
                return Result(ReleaseFileState.Unknown);
            (status, RegistryOriginalBytes served) = await session.OriginalAsync("tarball",
                tarball, Host, file.Size, retain: false);
            if (status != 200) return Result(ReleaseFileState.Unknown);
            return Result(served.Size == file.Size && served.Sha256 == file.Sha256
                ? ReleaseFileState.Exact : ReleaseFileState.Conflict, served.Size, served.Sha256);
        }
        catch (OperationCanceledException)
        {
            session.Reads.Add(new("operation", null, token.IsCancellationRequested
                ? "Cancelled." : "Read deadline exceeded.", ""));
            return Result(ReleaseFileState.Unknown);
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException)
        {
            session.Reads.Add(new("operation", null, "Complete response unavailable.", ""));
            return Result(ReleaseFileState.Unknown);
        }
        catch (Exception failure) when (failure is InvalidDataException or JsonException or
            KeyNotFoundException or InvalidOperationException or UriFormatException
            or DecoderFallbackException)
        {
            session.Reads.Add(new("operation", null,
                "Unsupported native metadata or response.", ""));
            return Result(ReleaseFileState.Unknown);
        }

        NpmjsObservationResult Result(ReleaseFileState state, long? size = null,
            string? sha256 = null) => new(new(prepared.Subject,
                [new(file.Output, file.Name, file.Version, state, size, sha256)],
                ["native-metadata-visibility"],
                [new("native-metadata-visibility", state is ReleaseFileState.Exact or
                    ReleaseFileState.Absent ? ReleaseCheckStatus.Passed :
                        ReleaseCheckStatus.Failed)]), [.. session.Reads]);
    }
}
