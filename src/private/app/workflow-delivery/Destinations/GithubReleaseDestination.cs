using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record GithubReleaseTarget(ReleaseSubject Subject, ReleaseFile[] Files,
    string Tag, string Title, string? ReleaseNotes,
    IReadOnlyCollection<string>? AllowedSiblingAssets);

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

    internal async Task<ReleaseObservationResult> ObserveAsync(GithubReleaseTarget target,
        CancellationToken token) => (await ReadAsync(target, token)).Observation;

    private async Task<(ReleaseObservationResult Observation, long? Id)> ReadAsync(
        GithubReleaseTarget prepared, CancellationToken token)
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
            if (status == 404)
                return Result(ReleaseFileState.Absent, ReleaseCheckStatus.Passed,
                    ContainerPresence.Missing);
            DestinationHttpSession.RequireOk(status);
            using JsonDocument releaseDocument = StrictJson.Parse(body);
            JsonElement release = releaseDocument.RootElement;
            long id = Number(release, "id");
            // A release whose tag is gone is no longer bound to the prepared commit.
            if (!tagged || Text(release, "tag_name") != tag || Flag(release, "draft") ||
                Flag(release, "prerelease") != buddy ||
                (!buddy && prepared.ReleaseNotes is not null &&
                    Body(release) != prepared.ReleaseNotes))
                return Result(ReleaseFileState.Conflict, ReleaseCheckStatus.Failed);
            releaseId = id;

            var assets = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            await github.ReadArrayAsync("assets", new(Api + "/releases/" + id + "/assets"),
                asset =>
                {
                    if (!assets.TryAdd(Text(asset, "name"), asset.Clone()))
                        throw new InvalidDataException("Repeated release asset.");
                });
            // A release carrying any unexpected asset is not this request's release.
            if (assets.Keys.Any(name => !prepared.Files.Any(file => file.Name == name) &&
                    (prepared.AllowedSiblingAssets is null ||
                        !prepared.AllowedSiblingAssets.Contains(name, StringComparer.Ordinal))))
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
            return (new(new(prepared.Subject, [.. files], [Check],
                [new(Check, ReleaseCheckStatus.Passed)]), [.. session.Reads],
                Container: ContainerPresence.Present), releaseId);
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

        (ReleaseObservationResult, long?) Result(ReleaseFileState state, ReleaseCheckStatus check,
            ContainerPresence presence = ContainerPresence.Unknown) =>
            (new(new(prepared.Subject, [.. prepared.Files.Select(file => Observed(file, state))],
                [Check], [new(Check, check)]), [.. session.Reads],
                Container: releaseId.HasValue ? ContainerPresence.Present : presence), null);
    }

    internal Publisher OpenPublisher(GithubReleaseTarget target, string originals)
    {
        if (target.Subject.DryRun)
            throw new InvalidDataException("A dry run cannot open a GitHub publisher.");
        LiveReleaseRef.RequireEligible(target.Subject);
        return new(this, target, originals);
    }

    // A publisher starts with no native handle. Only its own fresh read or acknowledged
    // creation can bind the release to which its asset uploads are addressed.
    internal sealed class Publisher(
        GithubReleaseDestination destination,
        GithubReleaseTarget target,
        string originals)
    {
        private long? releaseId;
        private bool canCreate;

        internal async Task<ReleaseObservationResult> RefreshAsync(CancellationToken token)
        {
            releaseId = null;
            canCreate = false;
            (ReleaseObservationResult observed, long? id) =
                await destination.ReadAsync(target, token);
            if (observed.Observation.Checks.All(check => check.Status == ReleaseCheckStatus.Passed)
                && observed.Observation.Files.All(file => file.State is
                    ReleaseFileState.Absent or ReleaseFileState.Exact))
            {
                releaseId = id;
                canCreate = observed.Container == ContainerPresence.Missing;
            }
            return observed;
        }

        internal async Task<ReleaseWriteStep> CreateAsync(CancellationToken token)
        {
            if (!canCreate)
                throw new InvalidDataException("The publisher has not observed a missing release.");
            canCreate = false;
            token.ThrowIfCancellationRequested();
            (NativeCommandResult result, long? created) = await destination.CreateAsync(target,
                token);
            ReleaseWriteStep step = ReleaseWriteStep.FromContainer(result, created.HasValue);
            if (step.Acknowledged) releaseId = created;
            return step;
        }

        internal async Task<ReleaseWriteStep> UploadAsync(ReleaseFile file, CancellationToken token)
        {
            if (releaseId is not long id || !target.Files.Contains(file))
                throw new InvalidDataException("Missing publisher-bound GitHub release or asset.");
            string path = OwnedPath.ResolveWithin(originals, file.Path,
                "Original GitHub Release directory must be absolute.",
                "Original GitHub Release path leaves its root.");
            OwnedFile.Verify(path, file.Size, file.Sha256, "Transferred GitHub original differs.");
            token.ThrowIfCancellationRequested();
            return ReleaseWriteStep.FromUpload(new(file.Output,
                await destination.UploadAsync(id, file, path, token)));
        }
    }

    // One native create of a published release at the selected commit; GitHub creates a
    // missing lightweight tag there and rejects a second release for the tag.
    private async Task<(NativeCommandResult Result, long? Id)> CreateAsync(
        GithubReleaseTarget prepared, CancellationToken token)
    {
        (string tag, bool buddy) = Identity(prepared);
        string version = prepared.Files[0].Version;
        var body = new JsonObject
        {
            ["tag_name"] = tag,
            ["target_commitish"] = prepared.Subject.Revision,
            ["name"] = prepared.Title,
            ["body"] = prepared.ReleaseNotes ??
                "Workflow Delivery " + prepared.Subject.Channel + " release of " +
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
    private async Task<NativeCommandResult> UploadAsync(long release, ReleaseFile file,
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

    private (string Tag, bool Buddy) Identity(GithubReleaseTarget prepared)
    {
        bool buddy = prepared.Subject.Channel == "buddy";
        if (prepared.Files.Length == 0 ||
            prepared.Files.Any(file => file.Version != prepared.Files[0].Version) ||
            string.IsNullOrEmpty(credential) || credential.Any(c => c is '\r' or '\n' or '\0'))
            throw new InvalidDataException("Unsupported GitHub Release request.");
        return (prepared.Tag, buddy);
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

    private static string Body(JsonElement value) =>
        value.GetProperty("body") switch
        {
            { ValueKind: JsonValueKind.Null } => "",
            { ValueKind: JsonValueKind.String } text => text.GetString()!,
            _ => throw new InvalidDataException("Invalid native text: body"),
        };

    private static long Number(JsonElement value, string name) =>
        value.GetProperty(name).GetInt64() is > 0 and var number ? number :
            throw new InvalidDataException("Missing native number: " + name);

    private static bool Flag(JsonElement value, string name) =>
        value.GetProperty(name).GetBoolean();
}
