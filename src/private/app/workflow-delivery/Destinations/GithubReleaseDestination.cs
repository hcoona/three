using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record GithubReleaseObservationResult(ReleaseObservation Observation,
    long? ReleaseId, DestinationRead[] Reads) : IReleaseObservation;

// The repository's GitHub Release for one tag. GitHub owns tags, releases and asset
// storage; the application reads the tag's commit, the release's channel and every served
// asset, and compares each with the qualified original byte for byte.
internal sealed class GithubReleaseDestination(HttpClient client, string credential,
    TimeProvider? clock = null, HttpClient? writeClient = null)
{
    internal const string Repository = GithubApi.Repository;
    internal const string Check = "release-identity";
    private const string Api = "https://" + GithubApi.Host + "/repos/" + Repository;
    private const string UploadHost = "uploads.github.com";
    private const string Uploads = "https://" + UploadHost + "/repos/" + Repository;
    private const int MetadataLimit = 2 * 1024 * 1024;

    internal async Task<GithubReleaseObservationResult> ObserveAsync(PreparedRelease prepared,
        CancellationToken token)
    {
        (string tag, bool buddy) = Identity(prepared);
        long? releaseId = null;
        using var session = new DestinationHttpSession(client, clock,
            TimeSpan.FromMinutes(20), TimeSpan.FromSeconds(30), token, maxRequests: 40);
        var github = new GithubApi(session, credential, MetadataLimit);
        try
        {
            // A tag without a release at the selected commit still admits creation; any
            // other existing tag is never moved. A missing tag admits only an absent release,
            // which creation then binds to the selected commit.
            (int status, byte[] body) = await github.MetadataAsync("tag",
                new(Api + "/git/ref/tags/" + Escape(tag)));
            bool tagged = status == 200;
            if (tagged)
            {
                using JsonDocument document = StrictJson.Parse(body);
                JsonElement reference = document.RootElement;
                JsonElement target = reference.GetProperty("object");
                if (Text(reference, "ref") != "refs/tags/" + tag ||
                    Text(target, "type") != "commit" ||
                    Text(target, "sha") != prepared.Subject.Revision)
                    return Result(ReleaseFileState.Conflict, ReleaseCheckStatus.Failed);
            }
            else if (status != 404) throw new IOException("Unexpected tag response.");

            (status, body) = await github.MetadataAsync("release",
                new(Api + "/releases/tags/" + Escape(tag)));
            if (status == 404) return Result(ReleaseFileState.Absent, ReleaseCheckStatus.Passed);
            DestinationHttpSession.RequireOk(status);
            using JsonDocument releaseDocument = StrictJson.Parse(body);
            JsonElement release = releaseDocument.RootElement;
            long id = Number(release, "id");
            // A release whose tag is gone is no longer bound to the prepared commit.
            if (!tagged || Text(release, "tag_name") != tag || Flag(release, "draft") ||
                Flag(release, "prerelease") != buddy)
                return Result(ReleaseFileState.Conflict, ReleaseCheckStatus.Failed);
            releaseId = id;

            var assets = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            await github.ReadArrayAsync("assets", new(Api + "/releases/" + id + "/assets"),
                asset =>
                {
                    if (!assets.TryAdd(Text(asset, "name"), asset.Clone()))
                        throw new InvalidDataException("Repeated release asset.");
                });
            // A release carrying any other asset is not this request's release.
            if (assets.Keys.Any(name => !prepared.Files.Any(file => file.Name == name)))
                return Result(ReleaseFileState.Conflict, ReleaseCheckStatus.Failed);

            var files = new List<ReleaseFileObservation>();
            foreach (ReleaseFile file in prepared.Files)
            {
                if (!assets.TryGetValue(file.Name, out JsonElement asset))
                {
                    files.Add(Observed(file, ReleaseFileState.Absent));
                    continue;
                }
                // An interrupted upload leaves a non-uploaded asset GitHub alone resolves.
                if (Text(asset, "state") != "uploaded")
                {
                    files.Add(Observed(file, ReleaseFileState.Unknown));
                    continue;
                }
                if (Number(asset, "size") != file.Size)
                {
                    files.Add(Observed(file, ReleaseFileState.Conflict));
                    continue;
                }
                (status, RegistryOriginalBytes served) = await github.OriginalAsync("asset",
                    new(Api + "/releases/assets/" + Number(asset, "id")), file.Size,
                    TimeSpan.FromMinutes(10));
                DestinationHttpSession.RequireOk(status);
                files.Add(served.Size == file.Size && served.Sha256 == file.Sha256
                    ? Observed(file, ReleaseFileState.Exact, served.Size, served.Sha256)
                    : Observed(file, ReleaseFileState.Conflict, served.Size, served.Sha256));
            }
            return new(new(prepared.Subject, [.. files], [Check],
                [new(Check, ReleaseCheckStatus.Passed)]), releaseId, [.. session.Reads]);
        }
        catch (OperationCanceledException)
        {
            session.Reads.Add(new("operation", null, token.IsCancellationRequested
                ? "Cancelled." : "Read deadline exceeded."));
            return Result(ReleaseFileState.Unknown, ReleaseCheckStatus.Failed);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or
            InvalidDataException or JsonException or ArgumentException or
            InvalidOperationException or FormatException or OverflowException or
            KeyNotFoundException)
        {
            session.Reads.Add(new("operation", null,
                "Required native response unavailable or invalid."));
            return Result(ReleaseFileState.Unknown, ReleaseCheckStatus.Failed);
        }

        GithubReleaseObservationResult Result(ReleaseFileState state, ReleaseCheckStatus check) =>
            new(new(prepared.Subject, [.. prepared.Files.Select(file => Observed(file, state))],
                [Check], [new(Check, check)]), releaseId, [.. session.Reads]);
    }

    // One native create of a published release at the selected commit; GitHub creates a
    // missing lightweight tag there and rejects a second release for the tag.
    internal async Task<(NativeCommandResult Result, long? Id)> CreateAsync(
        PreparedRelease prepared, CancellationToken token)
    {
        (string tag, bool buddy) = Identity(prepared);
        string version = prepared.Files[0].Version;
        var body = new JsonObject
        {
            ["tag_name"] = tag,
            ["target_commitish"] = prepared.Subject.Revision,
            ["name"] = GithubReleaseUnits.Title(prepared.Subject.Unit, version),
            ["body"] = "Workflow Delivery " + prepared.Subject.Channel + " release of " +
                prepared.Subject.Unit + " " + version + " from " + prepared.Subject.Revision + ".",
            ["draft"] = false,
            ["prerelease"] = buddy,
        };
        if (buddy) body["make_latest"] = "false";
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8,
            "application/json");
        return await WriteAsync(new(Api + "/releases"), content, TimeSpan.FromSeconds(60), token);
    }

    // One native upload of one original asset to the observed release.
    internal async Task<NativeCommandResult> UploadAsync(long release, ReleaseFile file,
        string path, CancellationToken token)
    {
        await using FileStream stream = File.OpenRead(path);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new("application/octet-stream");
        content.Headers.ContentLength = file.Size;
        (NativeCommandResult result, _) = await WriteAsync(new(Uploads + "/releases/" + release +
            "/assets?name=" + Uri.EscapeDataString(file.Name)), content,
            TimeSpan.FromMinutes(20), token);
        return result;
    }

    private async Task<(NativeCommandResult, long?)> WriteAsync(Uri uri, HttpContent content,
        TimeSpan timeout, CancellationToken token)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        using var timer = new CancellationTokenSource(timeout, clock ?? TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timer.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            DestinationHttpSession.RequireHttpsOrigin(uri, uri.Host == UploadHost
                ? UploadHost : GithubApi.Host);
            GithubApi.ConfigureJsonWrite(request, credential);
            request.Content = content;
            using HttpResponseMessage response = await (writeClient ?? client).SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            byte[] body = await RegistryBody.LimitedAsync(await response.Content
                .ReadAsStreamAsync(deadline.Token), response.Content.Headers.ContentLength,
                MetadataLimit, deadline.Token);
            int status = (int)response.StatusCode;
            long? id = null;
            if (status == 201)
                try
                {
                    using JsonDocument document = StrictJson.Parse(body);
                    id = Number(document.RootElement, "id");
                }
                catch (Exception error) when (error is InvalidDataException or JsonException or
                    FormatException or OverflowException or InvalidOperationException or
                    KeyNotFoundException or DecoderFallbackException)
                {
                    return (new(NativeTermination.InvalidOutput, status, "", "",
                        started.Elapsed.TotalSeconds, "Unreadable created resource."), null);
                }
            return (new(NativeTermination.Exited, status == 201 ? 0 : status, "HTTP " + status,
                "", started.Elapsed.TotalSeconds, null), id);
        }
        catch (OperationCanceledException)
        {
            return (new(token.IsCancellationRequested ? NativeTermination.Cancelled
                : NativeTermination.TimedOut, null, "", "", started.Elapsed.TotalSeconds,
                "Write outcome unknown."), null);
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            return (new(NativeTermination.InvalidOutput, null, "", "",
                started.Elapsed.TotalSeconds, "Write outcome unknown."), null);
        }
    }

    private (string Tag, bool Buddy) Identity(PreparedRelease prepared)
    {
        bool buddy = prepared.Subject.Channel == "buddy";
        if (prepared.Files.Length == 0 ||
            prepared.Files.Any(file => file.Version != prepared.Files[0].Version) ||
            string.IsNullOrEmpty(credential) || credential.Any(c => c is '\r' or '\n' or '\0'))
            throw new InvalidDataException("Unsupported GitHub Release request.");
        return (GithubReleaseUnits.Tag(prepared.Subject, prepared.Files[0].Version), buddy);
    }

    private static ReleaseFileObservation Observed(ReleaseFile file, ReleaseFileState state,
        long? size = null, string? digest = null) =>
        new(file.Output, file.Name, file.Version, state, size, digest);

    // Each tag path segment is escaped; the separators of release/<unit>/v<version> stay.
    private static string Escape(string tag) =>
        string.Join('/', tag.Split('/').Select(Uri.EscapeDataString));

    private static string Text(JsonElement value, string name) =>
        value.GetProperty(name).GetString() is { Length: > 0 } text ? text :
            throw new InvalidDataException("Missing native text: " + name);

    private static long Number(JsonElement value, string name) =>
        value.GetProperty(name).GetInt64() is > 0 and var number ? number :
            throw new InvalidDataException("Missing native number: " + name);

    private static bool Flag(JsonElement value, string name) =>
        value.GetProperty(name).GetBoolean();
}
