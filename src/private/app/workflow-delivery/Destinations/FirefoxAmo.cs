using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

// addons.mozilla.org API v5 with the publisher's JWT API credential. Only the publish job
// holds the credential, so it observes the exact listed version before and after its
// submission. The add-on must already exist; a version create never creates a listing.
internal sealed class FirefoxAmo(HttpClient client, ReleaseSubject subject,
    BrowserStoreTarget target, string version, string issuer, string secret,
    Func<TimeSpan, CancellationToken, Task>? delay = null, TimeProvider? time = null,
    int attempts = 60) : IBrowserStore
{
    internal static readonly Uri Origin = new("https://addons.mozilla.org/api/v5/");
    private readonly StoreHttp http = new(client, [], delay, time);
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    private string Addon => $"addons/addon/{Uri.EscapeDataString(target.Item)}/";

    public Task<StoreObservation> ObserveAsync(CancellationToken cancel) =>
        StoreHttp.ObserveAsync(subject, target, version, http.Reads, async () =>
        {
            StoreResponse response = await http.SendAsync("version",
                Request(HttpMethod.Get, Addon + "versions/v" + Uri.EscapeDataString(version) +
                    "/"), cancel);
            if (response.Status == 404)
            {
                StoreResponse addon = await http.SendAsync("addon",
                    Request(HttpMethod.Get, Addon), cancel);
                return addon.Status == 200 ? (StoreState.Absent, "")
                    : addon.Status == 404
                        ? (StoreState.Conflict, "AMO add-on is not an existing listing.")
                        : (StoreState.Unknown, "AMO add-on is unavailable.");
            }
            if (response.Status != 200)
                return (StoreState.Unknown, "AMO version is unavailable.");
            using JsonDocument document = StoreHttp.Json(response);
            return Classify(document.RootElement, version);
        });

    public async Task<StoreObservation?> PublishAsync(StoreFile[] files,
        List<StoreOperation> operations, CancellationToken cancel)
    {
        string package = files.Single(file => file.Output == "firefox-zip").Path;
        string source = files.Single(file => file.Output == "firefox-sources").Path;
        var upload = Request(HttpMethod.Post, "addons/upload/");
        upload.Content = new MultipartFormDataContent
        {
            { File(package), "upload", Path.GetFileName(package) },
            { new StringContent("listed"), "channel" },
        };
        StoreResponse uploaded = await http.SendAsync("upload", upload, cancel);
        string? uuid = null;
        if (uploaded.Status is 200 or 201)
        {
            using JsonDocument document = StoreHttp.Json(uploaded);
            uuid = StoreHttp.Text(document.RootElement, "uuid");
        }
        if (string.IsNullOrWhiteSpace(uuid))
            return await Final("upload", false, $"Upload status {uploaded.Status}.");
        bool? valid = null;
        for (int attempt = 0; valid is null && attempt < attempts; attempt++)
        {
            if (attempt != 0) await http.DelayAsync(TimeSpan.FromSeconds(5), cancel);
            StoreResponse detail = await http.SendAsync("upload-status", Request(
                HttpMethod.Get, "addons/upload/" + Uri.EscapeDataString(uuid) + "/"), cancel);
            if (detail.Status != 200) continue;
            using JsonDocument document = StoreHttp.Json(detail);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("processed", out JsonElement processed) ||
                processed.ValueKind != JsonValueKind.True)
                continue;
            valid = root.TryGetProperty("valid", out JsonElement value) &&
                value.ValueKind == JsonValueKind.True &&
                StoreHttp.Text(root, "version") == version;
        }
        if (valid != true)
            return await Final("upload", false, valid is null
                ? "Upload validation did not finish." : "Upload is invalid or another version.");
        operations.Add(new("upload", true, "Package upload validated."));
        var create = Request(HttpMethod.Post, Addon + "versions/");
        create.Content = new MultipartFormDataContent
        {
            { new StringContent(uuid), "upload" },
            { File(source), "source", Path.GetFileName(source) },
        };
        StoreResponse created = await http.SendAsync("create-version", create, cancel);
        return await Final("create-version", created.Status == 201,
            $"Version create status {created.Status}.");

        async Task<StoreObservation?> Final(string operation, bool succeeded, string text)
        {
            operations.Add(new(operation, succeeded, text));
            return await ObserveAsync(cancel);
        }
    }

    internal static (StoreState, string) Classify(JsonElement detail, string version)
    {
        if (StoreHttp.Text(detail, "version") != version)
            return (StoreState.Unknown, "AMO returned another version.");
        if (StoreHttp.Text(detail, "channel") != "listed")
            return (StoreState.Conflict, "AMO version is not listed.");
        if (detail.TryGetProperty("is_disabled", out JsonElement disabled) &&
            disabled.ValueKind == JsonValueKind.True)
            return (StoreState.Conflict, "AMO version is disabled.");
        string? status = detail.TryGetProperty("file", out JsonElement file)
            ? StoreHttp.Text(file, "status") : null;
        return status switch
        {
            "public" => (StoreState.Published, ""),
            "unreviewed" => (StoreState.InReview, ""),
            "disabled" => (StoreState.Conflict, "AMO version file is disabled."),
            _ => (StoreState.Unknown, "Unsupported AMO version file status."),
        };
    }

    // HS256 JWT per request: iss is the API key, jti a fresh nonce, and exp at most five
    // minutes after iat, as the AMO authentication documentation requires.
    internal string Token()
    {
        long now = clock.GetUtcNow().ToUnixTimeSeconds();
        string header = Encode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"u8.ToArray());
        using var claims = new MemoryStream();
        using (var writer = new Utf8JsonWriter(claims))
        {
            writer.WriteStartObject();
            writer.WriteString("iss", issuer);
            writer.WriteString("jti", Guid.NewGuid().ToString("N"));
            writer.WriteNumber("iat", now);
            writer.WriteNumber("exp", now + 60);
            writer.WriteEndObject();
        }
        string payload = Encode(claims.ToArray());
        string unsigned = header + "." + payload;
        byte[] signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.ASCII.GetBytes(unsigned));
        return unsigned + "." + Encode(signature);

        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static StreamContent File(string path)
    {
        var content = new StreamContent(System.IO.File.OpenRead(path));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return content;
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(Origin, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("JWT", Token());
        return request;
    }
}
