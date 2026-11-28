using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record StoreFile(string Output, string Path);
internal delegate Task<DestinationInvocation> StoreWriter(
    string operation,
    CancellationToken token);

internal static class StoreArtifacts
{
    internal const string Chrome = "chrome-web-store";
    internal const string Firefox = "firefox-amo";
    internal const string Edge = "edge-addons";

    internal static string[] For(string store) => store switch
    {
        Chrome => ["chrome-zip"],
        Firefox => ["firefox-zip", "firefox-sources"],
        Edge => ["edge-zip"],
        _ => throw new InvalidDataException("Unsupported browser store."),
    };
}

// One store adapter. The publisher context supplies its credential; observation reads are
// credential-free records.
internal interface IBrowserStore
{
    Task<ReleaseObservationResult> ObserveAsync(CancellationToken token);

    // Bind one publisher session; each call performs only the selected native operation.
    StoreWriter Bind(StoreFile[] files);
}

internal sealed record StoreResponse(int Status, byte[] Body, string? Location);

// Bounded store HTTP. Credentials stay in request headers; reads record only stage, URL
// and status. Store responses are small JSON documents.
internal sealed class StoreHttp(HttpClient client, List<DestinationRead> reads,
    Func<TimeSpan, CancellationToken, Task>? delay = null, TimeProvider? time = null)
{
    private const int BodyLimit = 4 * 1024 * 1024;
    internal const string Check = "store-status";

    internal List<DestinationRead> Reads => reads;

    internal Task DelayAsync(TimeSpan interval, CancellationToken token) =>
        (delay ?? ((span, cancel) => Task.Delay(span, time ?? TimeProvider.System, cancel)))(
            interval, token);

    internal async Task<StoreResponse> SendAsync(string stage, HttpRequestMessage request,
        CancellationToken token, bool sessionLocation = false)
    {
        using HttpRequestMessage owned = request;
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(120),
            time ?? TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timer.Token);
        string? url = sessionLocation ? null : owned.RequestUri!.GetLeftPart(UriPartial.Path);
        owned.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using HttpResponseMessage response = await client.SendAsync(owned,
            HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        int status = (int)response.StatusCode;
        using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        byte[] body = await RegistryBody.LimitedAsync(stream,
            response.Content.Headers.ContentLength, BodyLimit, deadline.Token);
        reads.Add(new(stage, status, "Received response.", url));
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

    internal static bool ManifestVersion(string version)
    {
        string[] parts = version.Split('.');
        return parts.Length is >= 1 and <= 4 && parts.All(part => part.Length is >= 1 and
            <= 5 && part.All(char.IsAsciiDigit) && (part == "0" || part[0] != '0') &&
            int.Parse(part, CultureInfo.InvariantCulture) <= 65535);
    }

    internal static ReleaseObservationResult Observation(ReleaseSubject subject,
        BrowserStoreTarget target, string version, StoreState state, string[] reasons,
        DestinationRead[] reads)
    {
        ReleaseFileState fileState = state switch
        {
            StoreState.Absent => ReleaseFileState.Absent,
            StoreState.Received or StoreState.InReview or StoreState.Published =>
                ReleaseFileState.Accepted,
            StoreState.Unobserved => ReleaseFileState.Unobserved,
            StoreState.Conflict => ReleaseFileState.Conflict,
            _ => ReleaseFileState.Unknown,
        };
        ReleaseCheckStatus checkStatus = state is StoreState.Conflict or StoreState.Unknown
            ? ReleaseCheckStatus.Failed : ReleaseCheckStatus.Passed;
        ReleaseFileObservation[] files = [.. StoreArtifacts.For(target.Store)
            .Select(output => new ReleaseFileObservation(output, subject.Unit, version,
                fileState, null, null))];
        return new(new(subject, files, [Check], [new(Check, checkStatus)],
                reasons.Length == 0 ? null : reasons), reads,
            StoreState: state, StoreTarget: target);
    }

    // Unavailable or unsupported responses establish no store state.
    internal static async Task<ReleaseObservationResult> ObserveAsync(ReleaseSubject subject,
        BrowserStoreTarget target, string version, List<DestinationRead> reads,
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
        return Observation(subject, target, version, state, reason.Length == 0 ? [] : [reason],
            [.. reads]);
    }
}
