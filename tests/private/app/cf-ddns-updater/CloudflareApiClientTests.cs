using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hcoona.CfDdnsUpdater.Tests;

[Collection(TestCollectionDefinition.Name)]
public sealed class CloudflareApiClientTests
{
    [Fact]
    public async Task ResolveAsyncWalksSuffixesUntilAnExactZoneMatchIsVisible()
    {
        ScriptedHttpMessageHandler handler = new([
            () => JsonResponse("""{"success":true,"result":[],"errors":[],"messages":[],"result_info":{"page":1,"per_page":20,"count":0,"total_count":0,"total_pages":1}}"""),
            () => JsonResponse("""{"success":true,"result":[{"id":"zone-1","name":"example.com"}],"errors":[],"messages":[],"result_info":{"page":1,"per_page":20,"count":1,"total_count":1,"total_pages":1}}"""),
        ]);

        CloudflareApiClient apiClient = CreateApiClient(handler);
        CloudflareZoneResolver resolver = new(apiClient, NullLogger<CloudflareZoneResolver>.Instance);

        CloudflareZone zone = await resolver.ResolveAsync(
            "API.Example.Com.",
            CancellationToken.None);

        Assert.Equal(new CloudflareZone("zone-1", "example.com"), zone);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("name=api.example.com", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("name=example.com", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task ResolveAsyncKeepsInvalidDomainTagForCorrelation()
    {
        using ActivityRecorder recorder = ActivityRecorder.Start(CloudflareTelemetry.ActivitySourceName);

        ScriptedHttpMessageHandler handler = new([]);
        CloudflareApiClient apiClient = CreateApiClient(handler);
        CloudflareZoneResolver resolver = new(apiClient, NullLogger<CloudflareZoneResolver>.Instance);

        await Assert.ThrowsAsync<CloudflareZoneResolutionException>(() => resolver.ResolveAsync(
            "bad host name",
            CancellationToken.None));

        Activity activity = Assert.Single(
            recorder.StoppedActivities,
            stoppedActivity => stoppedActivity.OperationName == CloudflareTelemetry.ZoneResolutionActivityName);
        Assert.Equal("bad host name", activity.GetTagItem(CloudflareTelemetry.DomainTagName));
    }

    [Fact]
    public async Task ListZonesByExactNameAsyncReturnsAllPages()
    {
        using ActivityRecorder recorder = ActivityRecorder.Start(CloudflareTelemetry.ActivitySourceName);

        ScriptedHttpMessageHandler handler = new([
            () => JsonResponse("""{"success":true,"result":[],"errors":[],"messages":[],"result_info":{"page":1,"per_page":100,"count":0,"total_count":2,"total_pages":2}}"""),
            () => JsonResponse("""{"success":true,"result":[{"id":"zone-1","name":"example.com"}],"errors":[],"messages":[],"result_info":{"page":2,"per_page":100,"count":1,"total_count":2,"total_pages":2}}"""),
        ]);

        CloudflareApiClient apiClient = CreateApiClient(handler);

        IReadOnlyList<CloudflareZone> zones = await apiClient.ListZonesByExactNameAsync(
            "example.com",
            CancellationToken.None);

        Assert.Single(zones);
        Assert.Equal("zone-1", zones[0].Id);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("page=1", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("page=2", handler.Requests[1].RequestUri!.Query);

        List<Activity> pageActivities = recorder.StoppedActivities
            .Where(stoppedActivity =>
                stoppedActivity.OperationName == CloudflareTelemetry.CloudflareZoneListingPageActivityName)
            .ToList();

        Assert.Equal(2, pageActivities.Count);
        Assert.Equal("1", pageActivities[0].GetTagItem(CloudflareTelemetry.PageTagName)?.ToString());
        Assert.Equal("2", pageActivities[1].GetTagItem(CloudflareTelemetry.PageTagName)?.ToString());
        Assert.Equal("success", pageActivities[0].GetTagItem(CloudflareTelemetry.OutcomeTagName)?.ToString());
        Assert.Equal("success", pageActivities[1].GetTagItem(CloudflareTelemetry.OutcomeTagName)?.ToString());
    }

    [Fact]
    public async Task ListZonesByExactNameAsyncAppendsRootRequestIdsAcrossPages()
    {
        using ActivityRecorder recorder = ActivityRecorder.Start(CloudflareTelemetry.ActivitySourceName);

        ScriptedHttpMessageHandler handler = new([
            () => JsonResponse(
                """{"success":true,"result":[],"errors":[],"messages":[],"result_info":{"page":1,"per_page":100,"count":0,"total_count":2,"total_pages":2}}""",
                ("cf-ray", "ray-1"),
                ("cf-request-id", "req-1")),
            () => JsonResponse(
                """{"success":true,"result":[{"id":"zone-1","name":"example.com"}],"errors":[],"messages":[],"result_info":{"page":2,"per_page":100,"count":1,"total_count":2,"total_pages":2}}""",
                ("cf-ray", "ray-2"),
                ("cf-request-id", "req-2")),
        ]);

        CloudflareApiClient apiClient = CreateApiClient(handler);

        IReadOnlyList<CloudflareZone> zones = await apiClient.ListZonesByExactNameAsync(
            "example.com",
            CancellationToken.None);

        Assert.Single(zones);

        Activity rootActivity = Assert.Single(
            recorder.StoppedActivities,
            stoppedActivity => stoppedActivity.OperationName == CloudflareTelemetry.CloudflareZoneListingActivityName);
        Assert.Equal("ray-1, ray-2", rootActivity.GetTagItem(CloudflareTelemetry.CloudflareRayIdTagName)?.ToString());
        Assert.Equal("req-1, req-2", rootActivity.GetTagItem(CloudflareTelemetry.CloudflareRequestIdTagName)?.ToString());
    }

    [Fact]
    public async Task ListZonesByExactNameAsyncMarksFailedPageActivityForNonSuccessStatus()
    {
        using ActivityRecorder recorder = ActivityRecorder.Start(CloudflareTelemetry.ActivitySourceName);

        ScriptedHttpMessageHandler handler = new([
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("""{"success":false,"errors":[{"message":"boom"}],"messages":[]}""", Encoding.UTF8, "application/json"),
            },
        ]);

        CloudflareApiClient apiClient = CreateApiClient(handler);

        await Assert.ThrowsAsync<CloudflareApiException>(() => apiClient.ListZonesByExactNameAsync(
            "example.com",
            CancellationToken.None));

        Activity pageActivity = Assert.Single(
            recorder.StoppedActivities,
            stoppedActivity => stoppedActivity.OperationName == CloudflareTelemetry.CloudflareZoneListingPageActivityName);
        Assert.Equal(ActivityStatusCode.Error, pageActivity.Status);
        Assert.Equal("failure", pageActivity.GetTagItem(CloudflareTelemetry.OutcomeTagName));
    }

    [Fact]
    public async Task ListZonesByExactNameAsyncFailsOnSemanticApiError()
    {
        ScriptedHttpMessageHandler handler = new([
            () => JsonResponse("""{"success":false,"result":[],"errors":[{"code":123,"message":"zone lookup failed"}],"messages":[{"code":456,"message":"try again"}],"result_info":{"page":1,"per_page":100,"count":0,"total_count":0,"total_pages":1}}"""),
        ]);

        CloudflareApiClient apiClient = CreateApiClient(handler);

        CloudflareApiException exception = await Assert.ThrowsAsync<CloudflareApiException>(() =>
            apiClient.ListZonesByExactNameAsync("example.com", CancellationToken.None));

        Assert.Contains("zone lookup for \"example.com\"", exception.Message);
        Assert.Contains("zone lookup failed", exception.Message);
        Assert.Contains("try again", exception.Message);
    }

    [Fact]
    public async Task ListDnsRecordsByExactNameAsyncReturnsAllPages()
    {
        using ActivityRecorder recorder = ActivityRecorder.Start(CloudflareTelemetry.ActivitySourceName);

        ScriptedHttpMessageHandler handler = new([
            () => JsonResponse(
                """{"success":true,"result":[{"id":"record-1","name":"host.example.com","type":"A","content":"198.51.100.10","proxied":false,"ttl":1}],"errors":[],"messages":[],"result_info":{"page":1,"per_page":100,"count":1,"total_count":2,"total_pages":2}}""",
                ("cf-ray", "ray-1"),
                ("cf-request-id", "req-1")),
            () => JsonResponse(
                """{"success":true,"result":[{"id":"record-2","name":"host.example.com","type":"AAAA","content":"2001:db8::1","proxied":false,"ttl":1}],"errors":[],"messages":[],"result_info":{"page":2,"per_page":100,"count":1,"total_count":2,"total_pages":2}}""",
                ("cf-ray", "ray-2"),
                ("cf-request-id", "req-2")),
        ]);

        CloudflareApiClient apiClient = CreateApiClient(handler);

        IReadOnlyList<CloudflareDnsRecord> records = await apiClient.ListDnsRecordsByExactNameAsync(
            "zone-1",
            "host.example.com",
            CancellationToken.None);

        Assert.Equal(2, records.Count);
        Assert.Equal("record-1", records[0].Id);
        Assert.Equal("record-2", records[1].Id);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("page=1", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("page=2", handler.Requests[1].RequestUri!.Query);

        List<Activity> pageActivities = recorder.StoppedActivities
            .Where(stoppedActivity =>
                stoppedActivity.OperationName == CloudflareTelemetry.CloudflareDnsRecordListingPageActivityName)
            .ToList();

        Assert.Equal(2, pageActivities.Count);
        Assert.Equal("success", pageActivities[0].GetTagItem(CloudflareTelemetry.OutcomeTagName)?.ToString());
        Assert.Equal("success", pageActivities[1].GetTagItem(CloudflareTelemetry.OutcomeTagName)?.ToString());

        Activity rootActivity = Assert.Single(
            recorder.StoppedActivities,
            stoppedActivity => stoppedActivity.OperationName == CloudflareTelemetry.CloudflareDnsRecordListingActivityName);
        Assert.Equal("ray-1, ray-2", rootActivity.GetTagItem(CloudflareTelemetry.CloudflareRayIdTagName)?.ToString());
        Assert.Equal("req-1, req-2", rootActivity.GetTagItem(CloudflareTelemetry.CloudflareRequestIdTagName)?.ToString());
    }

    [Fact]
    public async Task ListDnsRecordsByExactNameAsyncMarksFailedPageActivityWhenSendAsyncThrows()
    {
        using ActivityRecorder recorder = ActivityRecorder.Start(CloudflareTelemetry.ActivitySourceName);

        CloudflareApiClient apiClient = CreateApiClient(new ThrowingHttpMessageHandler(
            new InvalidOperationException("transport failed")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => apiClient.ListDnsRecordsByExactNameAsync(
            "zone-1",
            "host.example.com",
            CancellationToken.None));

        Activity pageActivity = Assert.Single(
            recorder.StoppedActivities,
            stoppedActivity => stoppedActivity.OperationName == CloudflareTelemetry.CloudflareDnsRecordListingPageActivityName);
        Assert.Equal(ActivityStatusCode.Error, pageActivity.Status);
        Assert.Equal("failure", pageActivity.GetTagItem(CloudflareTelemetry.OutcomeTagName));
    }

    [Fact]
    public async Task ListDnsRecordsByExactNameAsyncCapturesResponseMetadataBeforeBodyReadFailure()
    {
        using ActivityRecorder recorder = ActivityRecorder.Start(CloudflareTelemetry.ActivitySourceName);

        ScriptedHttpMessageHandler handler = new([
            () => ResponseWithThrowingBody(
                HttpStatusCode.OK,
                ("cf-ray", "ray-1"),
                ("cf-request-id", "req-1")),
        ]);

        CloudflareApiClient apiClient = CreateApiClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => apiClient.ListDnsRecordsByExactNameAsync(
            "zone-1",
            "host.example.com",
            CancellationToken.None));

        Activity pageActivity = Assert.Single(
            recorder.StoppedActivities,
            stoppedActivity => stoppedActivity.OperationName == CloudflareTelemetry.CloudflareDnsRecordListingPageActivityName);
        Assert.Equal((int)HttpStatusCode.OK, pageActivity.GetTagItem(CloudflareTelemetry.HttpStatusCodeTagName));
        Assert.Equal("ray-1", pageActivity.GetTagItem(CloudflareTelemetry.CloudflareRayIdTagName));
        Assert.Equal("req-1", pageActivity.GetTagItem(CloudflareTelemetry.CloudflareRequestIdTagName));
        Assert.Equal(ActivityStatusCode.Error, pageActivity.Status);
        Assert.Equal("failure", pageActivity.GetTagItem(CloudflareTelemetry.OutcomeTagName));
    }

    [Fact]
    public async Task CreateDnsRecordAsyncFailsOnSemanticApiError()
    {
        ScriptedHttpMessageHandler handler = new([
            () => JsonResponse("""{"success":false,"result":{"id":"record-1","name":"host.example.com","type":"A","content":"8.8.8.8","proxied":false,"ttl":1},"errors":[{"code":123,"message":"mutation failed"}],"messages":[{"code":456,"message":"record rejected"}]}"""),
        ]);

        CloudflareApiClient apiClient = CreateApiClient(handler);

        CloudflareApiException exception = await Assert.ThrowsAsync<CloudflareApiException>(() =>
            apiClient.CreateDnsRecordAsync(
                "zone-1",
                new CloudflareDnsRecordMutationRequestDto
                {
                    Name = "host.example.com",
                    Type = "A",
                    Content = "8.8.8.8",
                    Proxied = false,
                    Ttl = 1,
                },
                CancellationToken.None));

        Assert.Contains("DNS record create in zone \"zone-1\"", exception.Message);
        Assert.Contains("mutation failed", exception.Message);
        Assert.Contains("record rejected", exception.Message);
    }

    private static CloudflareApiClient CreateApiClient(ScriptedHttpMessageHandler handler)
    {
        HttpClient client = new(handler)
        {
            BaseAddress = new Uri("https://api.cloudflare.com/client/v4/", UriKind.Absolute),
        };

        return new CloudflareApiClient(
            client,
            new CloudflareConfiguration("token", [], false));
    }

    private static CloudflareApiClient CreateApiClient(ThrowingHttpMessageHandler handler)
    {
        HttpClient client = new(handler)
        {
            BaseAddress = new Uri("https://api.cloudflare.com/client/v4/", UriKind.Absolute),
        };

        return new CloudflareApiClient(
            client,
            new CloudflareConfiguration("token", [], false));
    }

    private static HttpResponseMessage JsonResponse(string json)
        => JsonResponse(json, Array.Empty<(string Name, string Value)>());

    private static HttpResponseMessage JsonResponse(
        string json,
        params (string Name, string Value)[] headers)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        foreach ((string Name, string Value) header in headers)
        {
            response.Headers.Add(header.Name, header.Value);
        }

        return response;
    }

    private static HttpResponseMessage ResponseWithThrowingBody(
        HttpStatusCode statusCode,
        params (string Name, string Value)[] headers)
    {
        HttpResponseMessage response = new(statusCode)
        {
            Content = new StreamContent(new ThrowingReadStream()),
        };

        foreach ((string Name, string Value) header in headers)
        {
            response.Headers.Add(header.Name, header.Value);
        }

        return response;
    }
}

internal sealed class ScriptedHttpMessageHandler(IEnumerable<Func<HttpResponseMessage>> responses)
    : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> responses = new(responses);

    public List<CapturedRequest> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri));

        if (responses.Count == 0)
        {
            throw new InvalidOperationException("No more scripted responses are available.");
        }

        return Task.FromResult(responses.Dequeue()());
    }
}

internal sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => throw exception;
}

internal sealed class ThrowingReadStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
        => throw new InvalidOperationException("body read failed");

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("body read failed");

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("body read failed");
}

internal sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri);

internal sealed class ActivityRecorder : IDisposable
{
    private readonly ActivityListener listener = new();

    private ActivityRecorder()
    {
    }

    public List<Activity> StoppedActivities { get; } = [];

    public static ActivityRecorder Start(string sourceName)
    {
        ActivityRecorder recorder = new();
        recorder.listener.ShouldListenTo = source => source.Name == sourceName;
        recorder.listener.Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
            ActivitySamplingResult.AllData;
        recorder.listener.SampleUsingParentId =
            (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData;
        recorder.listener.ActivityStopped = activity => recorder.StoppedActivities.Add(activity);
        ActivitySource.AddActivityListener(recorder.listener);
        return recorder;
    }

    public void Dispose()
        => listener.Dispose();
}
