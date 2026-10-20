using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record GithubReleaseRead(string Stage, int? Status, string Detail);
internal sealed record GithubReleaseObservationResult(ReleaseObservation Observation,
    long? ReleaseId, GithubReleaseRead[] Reads);

// The repository's GitHub Release for one tag. GitHub owns tags, releases and asset
// storage; the application reads the tag's commit, the release's channel and every served
// asset, and compares each with the qualified original byte for byte.
internal sealed class GithubReleaseDestination(HttpClient client, string credential,
    TimeProvider? clock = null)
{
    internal const string Repository = "hcoona/three";
    internal const string Check = "release-identity";
    private const string Api = "https://api.github.com/repos/" + Repository;
    private const string Uploads = "https://uploads.github.com/repos/" + Repository;
    private const int MetadataLimit = 2 * 1024 * 1024;

    internal async Task<GithubReleaseObservationResult> ObserveAsync(PreparedRelease prepared,
        CancellationToken token)
    {
        (string tag, bool buddy) = Identity(prepared);
        var reads = new List<GithubReleaseRead>();
        long? releaseId = null;
        int sends = 0;
        using var timer = new CancellationTokenSource(TimeSpan.FromMinutes(20),
            clock ?? TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, timer.Token);
        try
        {
            // A tag without a release at the selected commit still admits creation; any
            // other existing tag is never moved. A missing tag admits only an absent release,
            // which creation then binds to the selected commit.
            (int status, byte[] body) = await ReadAsync("tag",
                new(Api + "/git/ref/tags/" + Escape(tag)));
            bool tagged = status == 200;
            if (tagged)
            {
                JsonObject reference = Object(body);
                JsonObject target = RequiredObject(reference["object"]);
                if (Text(reference, "ref") != "refs/tags/" + tag ||
                    Text(target, "type") != "commit" ||
                    Text(target, "sha") != prepared.Subject.Revision)
                    return Result(ReleaseFileState.Conflict, ReleaseCheckStatus.Failed);
            }
            else if (status != 404) throw new IOException("Unexpected tag response.");

            (status, body) = await ReadAsync("release",
                new(Api + "/releases/tags/" + Escape(tag)));
            if (status == 404) return Result(ReleaseFileState.Absent, ReleaseCheckStatus.Passed);
            RequireOk(status);
            JsonObject release = Object(body);
            long id = Number(release, "id");
            // A release whose tag is gone is no longer bound to the prepared commit.
            if (!tagged || Text(release, "tag_name") != tag || Flag(release, "draft") ||
                Flag(release, "prerelease") != buddy)
                return Result(ReleaseFileState.Conflict, ReleaseCheckStatus.Failed);
            releaseId = id;

            var assets = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            for (int page = 1; ; page++)
            {
                (status, body) = await ReadAsync("assets",
                    new(Api + "/releases/" + id + "/assets?per_page=100&page=" + page));
                RequireOk(status);
                JsonArray items = Parse(body) as JsonArray ?? throw new InvalidDataException();
                if (items.Count > 100) throw new InvalidDataException();
                foreach (JsonNode? item in items)
                {
                    JsonObject asset = RequiredObject(item);
                    if (!assets.TryAdd(Text(asset, "name"), asset))
                        throw new InvalidDataException("Repeated release asset.");
                }
                if (items.Count < 100) break;
            }
            // A release carrying any other asset is not this request's release.
            if (assets.Keys.Any(name => !prepared.Files.Any(file => file.Name == name)))
                return Result(ReleaseFileState.Conflict, ReleaseCheckStatus.Failed);

            var files = new List<ReleaseFileObservation>();
            foreach (ReleaseFile file in prepared.Files)
            {
                if (!assets.TryGetValue(file.Name, out JsonObject? asset))
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
                (status, RegistryOriginalBytes served) = await ReadBodyAsync("asset",
                    new(Api + "/releases/assets/" + Number(asset, "id")),
                    (stream, length, read) => RegistryBody.OriginalAsync(stream, length,
                        file.Size, retain: false, read), binary: true);
                RequireOk(status);
                files.Add(served.Size == file.Size && served.Sha256 == file.Sha256
                    ? Observed(file, ReleaseFileState.Exact, served.Size, served.Sha256)
                    : Observed(file, ReleaseFileState.Conflict, served.Size, served.Sha256));
            }
            return new(new(prepared.Subject, [.. files], [Check],
                [new(Check, ReleaseCheckStatus.Passed)]), releaseId, [.. reads]);
        }
        catch (OperationCanceledException)
        {
            reads.Add(new("operation", null, token.IsCancellationRequested
                ? "Cancelled." : "Read deadline exceeded."));
            return Result(ReleaseFileState.Unknown, ReleaseCheckStatus.Failed);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or
            InvalidDataException or JsonException or ArgumentException or
            InvalidOperationException or FormatException or OverflowException)
        {
            reads.Add(new("operation", null, "Required native response unavailable or invalid."));
            return Result(ReleaseFileState.Unknown, ReleaseCheckStatus.Failed);
        }

        GithubReleaseObservationResult Result(ReleaseFileState state, ReleaseCheckStatus check) =>
            new(new(prepared.Subject, [.. prepared.Files.Select(file => Observed(file, state))],
                [Check], [new(Check, check)]), releaseId, [.. reads]);

        Task<(int, byte[])> ReadAsync(string stage, Uri uri) => ReadBodyAsync(stage, uri,
            (stream, length, read) => RegistryBody.LimitedAsync(stream, length, MetadataLimit,
                read));

        // The platform handler follows the asset storage redirect and clears Authorization.
        async Task<(int, T)> ReadBodyAsync<T>(string stage, Uri uri,
            Func<Stream, long?, CancellationToken, Task<T>> read, bool binary = false)
        {
            if (++sends > 40) throw new IOException("Observation read allowance exhausted.");
            using var requestTimer = new CancellationTokenSource(binary
                ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(30),
                clock ?? TimeProvider.System);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,
                requestTimer.Token);
            using HttpRequestMessage request = Request(HttpMethod.Get, uri, binary
                ? "application/octet-stream" : "application/vnd.github+json");
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
            using HttpRequestMessage request = Request(HttpMethod.Post, uri,
                "application/vnd.github+json");
            request.Content = content;
            using HttpResponseMessage response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            byte[] body = await RegistryBody.LimitedAsync(await response.Content
                .ReadAsStreamAsync(deadline.Token), response.Content.Headers.ContentLength,
                MetadataLimit, deadline.Token);
            int status = (int)response.StatusCode;
            long? id = null;
            if (status == 201)
                try { id = Number(Object(body), "id"); }
                catch (Exception error) when (error is InvalidDataException or JsonException or
                    FormatException or OverflowException or InvalidOperationException)
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

    private HttpRequestMessage Request(HttpMethod method, Uri uri, string accept)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Host is not
                ("api.github.com" or "uploads.github.com") || uri.UserInfo.Length != 0)
            throw new InvalidDataException("Unsupported native origin.");
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        request.Headers.UserAgent.ParseAdd("WorkflowDelivery");
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.AcceptEncoding.ParseAdd("identity");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
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

    private static void RequireOk(int status)
    {
        if (status != 200) throw new IOException("Required native response not successful.");
    }

    private static JsonNode? Parse(byte[] bytes) => JsonNode.Parse(
        new UTF8Encoding(false, true).GetString(bytes),
        documentOptions: new() { AllowDuplicateProperties = false });

    private static JsonObject Object(byte[] bytes) => RequiredObject(Parse(bytes));

    private static JsonObject RequiredObject(JsonNode? node) =>
        node as JsonObject ?? throw new InvalidDataException("Expected a JSON object.");

    private static string Text(JsonObject value, string name) =>
        value[name] is JsonValue text && text.TryGetValue(out string? result) && result.Length > 0
            ? result : throw new InvalidDataException("Missing native text: " + name);

    private static long Number(JsonObject value, string name) =>
        value[name] is JsonValue number && number.TryGetValue(out long result) && result > 0
            ? result : throw new InvalidDataException("Missing native number: " + name);

    private static bool Flag(JsonObject value, string name) =>
        value[name] is JsonValue flag && flag.TryGetValue(out bool result) ? result
            : throw new InvalidDataException("Missing native flag: " + name);
}
