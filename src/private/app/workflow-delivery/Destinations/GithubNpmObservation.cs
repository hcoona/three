using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

// Native package control and exact originals are the destination contract.
// No approval mirror, version reservation, package witness or tag certificate.
internal sealed class GithubNpmObservation(HttpClient client, TimeProvider? clock = null)
{
    internal const string Scope = "@hcoona/";
    internal const string Registry = "https://npm.pkg.github.com";
    private const string Host = "npm.pkg.github.com";

    // The registered unit's Buddy package is the only coordinate this destination reads.
    internal static string Package(PackageDestination prepared)
    {
        string package = prepared.Package;
        if (prepared.Subject.Channel != "buddy" ||
            prepared.Subject.Destination != "github-packages-npm" ||
            !package.StartsWith(Scope, StringComparison.Ordinal) || prepared.Files is not
                [{ Kind: "npm-tarball" } file] || file.Name != package)
            throw new InvalidDataException("Unsupported GitHub npm request.");
        return package;
    }

    internal async Task<ReleaseObservationResult> ObserveAsync(PackageDestination prepared,
        string credential, CancellationToken token)
    {
        string bare = Package(prepared)[Scope.Length..];
        if (credential.Length == 0 || credential.Any(c => c is '\r' or '\n' or '\0'))
            throw new InvalidDataException("Unsupported GitHub npm request or read credential.");
        ReleaseFile file = prepared.Files[0];
        ReleaseCheckStatus control = ReleaseCheckStatus.Missing;
        using var session = new DestinationHttpSession(client, clock,
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(20), token, noCache: true);
        var github = new GithubApi(session, credential, 1_000_000);
        var authorization = new AuthenticationHeaderValue("Bearer", credential);
        try
        {
            // An unavailable or unreadable package-control response cannot establish state.
            ReleaseFileState ownership = await github.CheckPackageAsync("package", "npm",
                bare, name => name == bare || name == file.Name,
                allowAbsent: false);
            if (ownership != ReleaseFileState.Exact) return Result(ownership);
            control = ReleaseCheckStatus.Passed;
            (int status, byte[] body) = await session.MetadataAsync("metadata",
                new(Registry + "/@hcoona%2F" + Uri.EscapeDataString(bare)), Host,
                "application/vnd.npm.install-v1+json, application/json", 1_000_000,
                authorization);
            // A missing provisioned package response cannot authorize creation.
            if (status != 200) return Result(ReleaseFileState.Unknown);
            using JsonDocument metadata = StrictJson.Parse(body);
            JsonElement root = metadata.RootElement;
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
            string archiveHost = archive.Host;
            if (archiveHost is not (Host or "pkg-npm.githubusercontent.com" or
                "objects.githubusercontent.com" or "github-registry-files.githubusercontent.com"))
                throw new InvalidDataException("Unsupported native endpoint.");
            DestinationHttpSession.RequireHttpsOrigin(archive, archiveHost);
            session.Reads.Add(new("tarball-location", null, "Resolved native tarball URL.",
                UrlSha256: Convert.ToHexStringLower(SHA256.HashData(
                    Encoding.UTF8.GetBytes(archive.AbsoluteUri)))));
            (status, RegistryOriginalBytes served) = await session.OriginalAsync("tarball", archive,
                archiveHost, file.Size, retain: false, archiveHost == Host ? authorization : null);
            if (status != 200) return Result(ReleaseFileState.Unknown);
            return Result(served.Size == file.Size && served.Sha256 == file.Sha256
                ? ReleaseFileState.Exact : ReleaseFileState.Conflict, served.Size, served.Sha256);
        }
        catch (OperationCanceledException)
        {
            session.Reads.Add(new("operation", null, token.IsCancellationRequested
                ? "Cancelled." : "Read deadline exceeded."));
            return Result(ReleaseFileState.Unknown);
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException)
        {
            session.Reads.Add(new("operation", null, "Complete response unavailable."));
            return Result(ReleaseFileState.Unknown);
        }
        catch (Exception failure) when (failure is InvalidDataException or JsonException or
            KeyNotFoundException or InvalidOperationException or UriFormatException
            or DecoderFallbackException)
        {
            session.Reads.Add(new("operation", null, "Unsupported native identity or response."));
            return Result(ReleaseFileState.Unknown);
        }

        ReleaseObservationResult Result(ReleaseFileState state, long? size = null,
            string? digest = null) => new(new(prepared.Subject,
                [new(file.Output, file.Name, file.Version, state, size, digest)],
                ["native-package-control", "native-metadata-visibility"],
                [new("native-package-control", control), new("native-metadata-visibility",
                    state is ReleaseFileState.Absent or ReleaseFileState.Exact
                        ? ReleaseCheckStatus.Passed : ReleaseCheckStatus.Failed)]),
                [.. session.Reads]);
    }

    private static string? Text(JsonElement value, string name) =>
        value.GetProperty(name).GetString();
}
