using System.Net.Http.Headers;
using System.Text.Json;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

// Store-reported state of one exact submitted version (the WD-REL-009 store exception).
// Received, InReview and Published complete a request; Unobserved means no read was possible.
internal enum StoreState { Absent, Received, InReview, Published, Conflict, Unknown, Unobserved }
internal sealed record StoreRead(string Stage, string Url, int? Status, string Detail);
internal sealed record StoreObservation(ReleaseSubject Subject, BrowserStoreTarget Target,
    string Version, StoreState State, string[] Reasons, StoreRead[] Reads);
internal sealed record StoreOperation(string Operation, bool Succeeded, string Detail);
internal sealed record StoreFile(string Output, string Path);

// One store adapter. The publisher context supplies its credential; observation reads are
// credential-free records.
internal interface IBrowserStore
{
    Task<StoreObservation> ObserveAsync(CancellationToken token);

    // Performs the store's native submission and returns the store's post-submission
    // observation when the publisher is the only reader, or null when another job reads.
    Task<StoreObservation?> PublishAsync(StoreFile[] files, List<StoreOperation> operations,
        CancellationToken token);
}

internal sealed record StoreResponse(int Status, byte[] Body, string? Location);

// Bounded store HTTP. Credentials stay in request headers; reads record only stage, URL
// and status. Store responses are small JSON documents.
internal sealed class StoreHttp(HttpClient client, List<StoreRead> reads,
    Func<TimeSpan, CancellationToken, Task>? delay = null, TimeProvider? time = null)
{
    private const int BodyLimit = 4 * 1024 * 1024;

    internal List<StoreRead> Reads => reads;

    internal Task DelayAsync(TimeSpan interval, CancellationToken token) =>
        (delay ?? ((span, cancel) => Task.Delay(span, time ?? TimeProvider.System, cancel)))(
            interval, token);

    internal async Task<StoreResponse> SendAsync(string stage, HttpRequestMessage request,
        CancellationToken token)
    {
        using HttpRequestMessage owned = request;
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(120),
            time ?? TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timer.Token);
        string url = request.RequestUri!.GetLeftPart(UriPartial.Path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using HttpResponseMessage response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        int status = (int)response.StatusCode;
        using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        byte[] body = await RegistryBody.LimitedAsync(stream,
            response.Content.Headers.ContentLength, BodyLimit, deadline.Token);
        reads.Add(new(stage, url, status, "Received response."));
        return new(status, body, response.Headers.Location?.OriginalString);
    }

    internal static JsonDocument Json(StoreResponse response)
    {
        JsonDocument document = JsonDocument.Parse(response.Body);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new InvalidDataException("Expected a store JSON object.");
        }
        return document;
    }

    internal static string? Text(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement
            text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;

    // Unavailable or unsupported responses establish no store state.
    internal static async Task<StoreObservation> ObserveAsync(ReleaseSubject subject,
        BrowserStoreTarget target, string version, List<StoreRead> reads,
        Func<Task<(StoreState State, string Reason)>> read)
    {
        StoreState state;
        string reason;
        try { (state, reason) = await read(); }
        catch (OperationCanceledException)
        { (state, reason) = (StoreState.Unknown, "Store read was cancelled or timed out."); }
        catch (Exception failure) when (failure is HttpRequestException or IOException or
            JsonException or InvalidDataException or InvalidOperationException or
            KeyNotFoundException or FormatException)
        { (state, reason) = (StoreState.Unknown, "Store response unavailable or unsupported."); }
        return new(subject, target, version, state, reason.Length == 0 ? [] : [reason],
            [.. reads]);
    }
}
