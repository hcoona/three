using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

// Chrome Web Store API v2. The caller supplies a short-lived OAuth access token of the
// registered service account: chromewebstore.readonly for observation, chromewebstore for
// the publisher. The store reports one published and at most one submitted item revision.
internal sealed class ChromeWebStore(HttpClient client, ReleaseSubject subject,
    BrowserStoreTarget target, string version, string token,
    Func<TimeSpan, CancellationToken, Task>? delay = null, TimeProvider? time = null,
    int attempts = 30) : IBrowserStore
{
    internal static readonly Uri Origin = new("https://chromewebstore.googleapis.com/");
    private readonly StoreHttp http = new(client, [], delay, time);

    private string Item => $"publishers/{Uri.EscapeDataString(target.Publisher!)}/items/" +
        Uri.EscapeDataString(target.Item);

    public Task<ReleaseObservationResult> ObserveAsync(CancellationToken cancel) =>
        StoreHttp.ObserveAsync(subject, target, version, http.Reads, async () =>
        {
            StoreResponse response = await http.SendAsync("fetch-status",
                Request(HttpMethod.Get, $"v2/{Item}:fetchStatus"), cancel);
            if (response.Status is 404)
                return (StoreState.Conflict, "Chrome Web Store item is not an existing listing.");
            if (response.Status != 200)
                return (StoreState.Unknown, "Chrome Web Store status is unavailable.");
            using JsonDocument document = StoreHttp.Json(response);
            return Classify(document.RootElement, version);
        });

    public StoreWriter Bind(StoreFile[] files) => (operation, cancel) => operation switch
    {
        "upload" => UploadAsync(files.Single(), cancel),
        "publish" => PublishItemAsync(cancel),
        _ => throw new InvalidDataException("Unsupported Chrome Web Store operation."),
    };

    private async Task<DestinationInvocation> UploadAsync(StoreFile file, CancellationToken cancel)
    {
        var upload = Request(HttpMethod.Post, $"upload/v2/{Item}:upload");
        upload.Content = new StreamContent(File.OpenRead(file.Path));
        upload.Content.Headers.ContentType = new("application/zip");
        StoreResponse uploaded = await http.SendAsync("upload", upload, cancel);
        string? state = null;
        if (uploaded.Status == 200)
        {
            using JsonDocument document = StoreHttp.Json(uploaded);
            state = StoreHttp.Text(document.RootElement, "uploadState");
            string? crx = StoreHttp.Text(document.RootElement, "crxVersion");
            if (crx is not null && crx != version)
                return DestinationInvocation.Store(
                    "upload", false, "Uploaded package reports another version.");
        }
        for (int attempt = 0; state == "IN_PROGRESS" && attempt < attempts; attempt++)
        {
            await http.DelayAsync(TimeSpan.FromSeconds(10), cancel);
            StoreResponse status = await http.SendAsync("upload-status",
                Request(HttpMethod.Get, $"v2/{Item}:fetchStatus"), cancel);
            if (status.Status != 200) continue;
            using JsonDocument document = StoreHttp.Json(status);
            state = StoreHttp.Text(document.RootElement, "lastAsyncUploadState");
        }
        if (state == "SUCCEEDED")
            return DestinationInvocation.Store("upload", true, "Package upload succeeded.");
        string reported = state switch
        {
            "FAILED" or "IN_PROGRESS" or "NOT_STARTED" => state,
            null => "unavailable",
            _ => "unsupported",
        };
        return DestinationInvocation.Store("upload", false, $"Upload state: {reported}.");
    }

    private async Task<DestinationInvocation> PublishItemAsync(CancellationToken cancel)
    {
        var publish = Request(HttpMethod.Post, $"v2/{Item}:publish");
        publish.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        StoreResponse published = await http.SendAsync("publish", publish, cancel);
        string? itemState = null;
        if (published.Status == 200)
        {
            using JsonDocument document = StoreHttp.Json(published);
            itemState = StoreHttp.Text(document.RootElement, "state");
        }
        bool accepted =
            itemState is "PENDING_REVIEW" or "STAGED" or "PUBLISHED" or "PUBLISHED_TO_TESTERS";
        string? reported = accepted || itemState is "REJECTED" or "CANCELLED" ? itemState
            : itemState is null ? "unavailable" : "unsupported";
        return DestinationInvocation.Store("publish", published.Status == 200 && accepted,
            $"Publish status {published.Status}, item state {reported}.");
    }

    internal static (StoreState, string) Classify(JsonElement status, string version)
    {
        if (status.TryGetProperty("takenDown", out JsonElement down) &&
            down.ValueKind == JsonValueKind.True)
            return (StoreState.Conflict, "Chrome Web Store item is taken down.");
        const string unreadable = "Chrome Web Store revision lacks readable distribution channels.";
        StoreState? exact = null;
        string? conflict = null;
        foreach (string field in new[] { "submittedItemRevisionStatus",
                     "publishedItemRevisionStatus" })
        {
            if (!status.TryGetProperty(field, out JsonElement revision) ||
                revision.ValueKind == JsonValueKind.Null)
                continue;
            if (revision.ValueKind != JsonValueKind.Object ||
                !revision.TryGetProperty("distributionChannels", out JsonElement channels) ||
                channels.ValueKind != JsonValueKind.Array || channels.GetArrayLength() == 0)
                return (StoreState.Unknown, unreadable);

            bool later = false, matches = false;
            foreach (JsonElement channel in channels.EnumerateArray())
            {
                string? value = StoreHttp.Text(channel, "crxVersion");
                if (value is null || !StoreHttp.ManifestVersion(value))
                    return (StoreState.Unknown, unreadable);
                later |= Compare(value, version) > 0;
                matches |= value == version;
            }

            string? state = StoreHttp.Text(revision, "state");
            if (later)
                conflict ??= "Chrome Web Store holds a later version.";
            else if (matches)
                exact ??= state switch
                {
                    "PENDING_REVIEW" => StoreState.InReview,
                    "STAGED" => StoreState.Received,
                    "PUBLISHED" or "PUBLISHED_TO_TESTERS" => StoreState.Published,
                    "REJECTED" or "CANCELLED" => StoreState.Conflict,
                    _ => StoreState.Unknown,
                };
            else if (field == "submittedItemRevisionStatus")
            {
                if (state is "PENDING_REVIEW" or "STAGED")
                    conflict ??= "Chrome Web Store holds another submitted revision.";
                else if (state is not ("PUBLISHED" or "PUBLISHED_TO_TESTERS" or "REJECTED"
                             or "CANCELLED"))
                    return (StoreState.Unknown,
                        "Unsupported Chrome Web Store revision state.");
            }
        }
        if (conflict is not null) return (StoreState.Conflict, conflict);
        return exact switch
        {
            null => (StoreState.Absent, ""),
            StoreState.Conflict => (StoreState.Conflict,
                "Chrome Web Store rejected or cancelled this version."),
            StoreState.Unknown => (StoreState.Unknown,
                "Unsupported Chrome Web Store revision state."),
            StoreState value => (value, ""),
        };
    }

    // Chrome extension versions are one to four dot-separated integers.
    internal static int Compare(string left, string right)
    {
        int[] a = Parts(left), b = Parts(right);
        for (int index = 0; index < 4; index++)
        {
            int compared = (index < a.Length ? a[index] : 0).CompareTo(
                index < b.Length ? b[index] : 0);
            if (compared != 0) return compared;
        }
        return 0;

        static int[] Parts(string value) => value.Split('.').Select(part =>
            int.Parse(part, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(Origin, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
