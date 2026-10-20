using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

// Microsoft Edge Add-ons API v1.1. The API exposes no read of listing or version state, so
// the store is unobserved before submission; the submission operation status is the only
// store report of the version having been received.
internal sealed class EdgeAddons(HttpClient client, ReleaseSubject subject,
    BrowserStoreTarget target, string version, string apiKey, string clientId,
    Func<TimeSpan, CancellationToken, Task>? delay = null, TimeProvider? time = null,
    int attempts = 60) : IBrowserStore
{
    internal static readonly Uri Origin = new("https://api.addons.microsoftedge.microsoft.com/");
    private readonly StoreHttp http = new(client, [], delay, time);

    private string Product => "v1/products/" + Uri.EscapeDataString(target.Item);

    public Task<StoreObservation> ObserveAsync(CancellationToken cancel) =>
        Task.FromResult(Unobserved(subject, target, version));

    internal static StoreObservation Unobserved(ReleaseSubject subject, BrowserStoreTarget
        target, string version) => new(subject, target, version, StoreState.Unobserved,
        ["The store exposes no version read before submission."], []);

    public async Task<StoreObservation?> PublishAsync(StoreFile[] files,
        List<StoreOperation> operations, CancellationToken cancel)
    {
        var upload = Request(HttpMethod.Post, Product + "/submissions/draft/package");
        upload.Content = new StreamContent(File.OpenRead(files.Single().Path));
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        (bool uploaded, string uploadDetail) = await OperationAsync("upload", upload,
            Product + "/submissions/draft/package/operations/", cancel);
        operations.Add(new("upload", uploaded, uploadDetail));
        if (!uploaded)
            return Final(StoreState.Unknown, "Package upload did not succeed.");
        var submit = Request(HttpMethod.Post, Product + "/submissions");
        // The prepared version is dot-separated digits, so it needs no JSON escaping.
        submit.Content = new StringContent(
            "{\"notes\":\"Workflow Delivery release " + version + "\"}", Encoding.UTF8,
            "application/json");
        (bool submitted, string submitDetail) = await OperationAsync("submit", submit,
            Product + "/submissions/operations/", cancel);
        operations.Add(new("submit", submitted, submitDetail));
        return submitted ? Final(StoreState.Received, "")
            : Final(submitDetail.StartsWith("Failed", StringComparison.Ordinal)
                ? StoreState.Conflict : StoreState.Unknown, "Submission did not succeed.");

        StoreObservation Final(StoreState state, string reason) => new(subject, target,
            version, state, reason.Length == 0 ? [] : [reason], [.. http.Reads]);
    }

    // Starts an asynchronous operation (202 with its id in Location) and polls its status.
    private async Task<(bool, string)> OperationAsync(string stage, HttpRequestMessage start,
        string statusPath, CancellationToken cancel)
    {
        StoreResponse started;
        try { started = await http.SendAsync(stage, start, cancel); }
        catch (Exception failure) when (failure is HttpRequestException or IOException or
            OperationCanceledException)
        { return (false, "Unavailable: request did not complete."); }
        string? operation = started.Location?.TrimEnd('/').Split('/')[^1];
        if (started.Status != 202 || string.IsNullOrWhiteSpace(operation))
            return (false, started.Status is >= 400 and < 500
                ? $"Failed: status {started.Status}." : $"Unavailable: status {started.Status}.");
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            await http.DelayAsync(TimeSpan.FromSeconds(5), cancel);
            StoreResponse status;
            try
            {
                status = await http.SendAsync(stage + "-status", Request(HttpMethod.Get,
                    statusPath + Uri.EscapeDataString(operation)), cancel);
            }
            catch (Exception failure) when (failure is HttpRequestException or IOException)
            { continue; }
            if (status.Status != 200) continue;
            using JsonDocument document = StoreHttp.Json(status);
            string? state = StoreHttp.Text(document.RootElement, "status");
            if (state == "Succeeded") return (true, "Succeeded.");
            if (state == "Failed")
                return (false, "Failed: " + (StoreHttp.Text(document.RootElement,
                    "errorCode") ?? "no error code") + ".");
        }
        return (false, "Unavailable: operation did not finish.");
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(Origin, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", apiKey);
        request.Headers.Add("X-ClientID", clientId);
        return request;
    }
}
