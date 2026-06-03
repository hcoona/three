using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hcoona.CfDdnsUpdater.Tests;

[Collection(TestCollectionDefinition.Name)]
public sealed class TraceIpDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsyncUsesFamilySpecificIpv4TraceUri()
    {
        RecordingTraceHttpMessageHandler handler = new([
            () => Response(HttpStatusCode.OK, "ip=8.8.8.8\ncolo=sin"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        IPAddress address = await service.DiscoverAsync(
            AddressFamily.InterNetwork,
            CancellationToken.None);

        Assert.Equal(IPAddress.Parse("8.8.8.8"), address);
        Assert.Single(handler.Requests);
        Assert.Equal(new Uri("https://1.1.1.1/cdn-cgi/trace"), handler.Requests[0].RequestUri);
    }

    [Fact]
    public async Task DiscoverAsyncUsesFamilySpecificIpv6TraceUri()
    {
        RecordingTraceHttpMessageHandler handler = new([
            () => Response(HttpStatusCode.OK, "ip=2001:4860:4860::8888\ncolo=sin"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        IPAddress address = await service.DiscoverAsync(
            AddressFamily.InterNetworkV6,
            CancellationToken.None);

        Assert.Equal(IPAddress.Parse("2001:4860:4860::8888"), address);
        Assert.Single(handler.Requests);
        Assert.Equal(
            new Uri("https://[2606:4700:4700::1111]/cdn-cgi/trace"),
            handler.Requests[0].RequestUri);
    }

    [Fact]
    public async Task DiscoverAsyncRejectsWrongFamilyTraceResult()
    {
        RecordingTraceHttpMessageHandler handler = new([
            () => Response(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        await Assert.ThrowsAsync<TraceIpDiscoveryException>(() => service.DiscoverAsync(
            AddressFamily.InterNetwork,
            CancellationToken.None).AsTask());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DiscoverAsyncRejectsNonPublicTraceResult()
    {
        RecordingTraceHttpMessageHandler handler = new([
            () => Response(HttpStatusCode.OK, "ip=10.0.0.1"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        await Assert.ThrowsAsync<TraceIpDiscoveryException>(() => service.DiscoverAsync(
            AddressFamily.InterNetwork,
            CancellationToken.None).AsTask());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void GetDelayHonorsLargeRetryAfterDelta()
    {
        RetryConditionHeaderValue retryAfter = new(TimeSpan.FromMinutes(5));

        TimeSpan delay = CloudflareTraceRetryHandler.GetDelay(retryAfter, 1);

        Assert.Equal(TimeSpan.FromMinutes(5), delay);
    }

    [Theory]
    [InlineData("192.0.0.9")]
    [InlineData("192.0.0.10")]
    public async Task DiscoverAsyncAcceptsPublicIetfIpv4AnycastAddresses(string address)
    {
        RecordingTraceHttpMessageHandler handler = new([
            () => Response(HttpStatusCode.OK, $"ip={address}"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        IPAddress discoveredAddress = await service.DiscoverAsync(
            AddressFamily.InterNetwork,
            CancellationToken.None);

        Assert.Equal(IPAddress.Parse(address), discoveredAddress);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.0.0.8")]
    [InlineData("192.0.0.170")]
    [InlineData("192.0.0.171")]
    [InlineData("192.31.196.1")]
    [InlineData("192.52.193.1")]
    [InlineData("192.88.99.2")]
    [InlineData("192.175.48.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.0.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    public async Task DiscoverAsyncRejectsSpecialUseIpv4Ranges(string address)
    {
        RecordingTraceHttpMessageHandler handler = new([
            () => Response(HttpStatusCode.OK, $"ip={address}"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        await Assert.ThrowsAsync<TraceIpDiscoveryException>(() => service.DiscoverAsync(
            AddressFamily.InterNetwork,
            CancellationToken.None).AsTask());
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("192.88.99.0")]
    [InlineData("192.88.99.255")]
    public async Task DiscoverAsyncRejectsDeprecatedIpv4AnycastRange(string address)
    {
        RecordingTraceHttpMessageHandler handler = new([
            () => Response(HttpStatusCode.OK, $"ip={address}"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        await Assert.ThrowsAsync<TraceIpDiscoveryException>(() => service.DiscoverAsync(
            AddressFamily.InterNetwork,
            CancellationToken.None).AsTask());
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("64:ff9b::1")]
    [InlineData("64:ff9b:1::1")]
    [InlineData("100:0:0:1::1")]
    [InlineData("2001:1::1")]
    [InlineData("2001:1::2")]
    [InlineData("2001:1::3")]
    [InlineData("2001:2::1")]
    [InlineData("2001:3::1")]
    [InlineData("2001:4:112::1")]
    [InlineData("2001:5::1")]
    [InlineData("2001:10::1")]
    [InlineData("2001:20::1")]
    [InlineData("2001:30::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002::1")]
    [InlineData("fc00::1")]
    [InlineData("fd00::1")]
    [InlineData("2620:4f:8000::1")]
    [InlineData("3ffe::1")]
    [InlineData("3fff::1")]
    [InlineData("5f00::1")]
    public async Task DiscoverAsyncRejectsAdditionalSpecialUseIpv6Ranges(string address)
    {
        RecordingTraceHttpMessageHandler handler = new([
            () => Response(HttpStatusCode.OK, $"ip={address}"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        await Assert.ThrowsAsync<TraceIpDiscoveryException>(() => service.DiscoverAsync(
            AddressFamily.InterNetworkV6,
            CancellationToken.None).AsTask());
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("100::1")]
    [InlineData("100::ffff:ffff:ffff:ffff")]
    [InlineData("fec0::1")]
    [InlineData("feff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    public async Task DiscoverAsyncRejectsReservedIpv6Ranges(string address)
    {
        RecordingTraceHttpMessageHandler handler = new([
            () => Response(HttpStatusCode.OK, $"ip={address}"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        await Assert.ThrowsAsync<TraceIpDiscoveryException>(() => service.DiscoverAsync(
            AddressFamily.InterNetworkV6,
            CancellationToken.None).AsTask());
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("::8.8.8.8")]
    [InlineData("::192.0.2.1")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::8.8.8.8")]
    public async Task DiscoverAsyncRejectsIpv4EmbeddedIpv6TraceResult(string address)
    {
        RecordingTraceHttpMessageHandler handler = new([
            () => Response(HttpStatusCode.OK, $"ip={address}"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        await Assert.ThrowsAsync<TraceIpDiscoveryException>(() => service.DiscoverAsync(
            AddressFamily.InterNetworkV6,
            CancellationToken.None).AsTask());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DiscoverAsyncDoesNotRetryParseFailures()
    {
        RecordingTraceHttpMessageHandler handler = new([
            () => Response(HttpStatusCode.OK, "colo=sin"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        await Assert.ThrowsAsync<TraceIpDiscoveryException>(() => service.DiscoverAsync(
            AddressFamily.InterNetwork,
            CancellationToken.None).AsTask());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DiscoverAsyncRetriesTransientFailuresWithJitter()
    {
        RecordingTraceHttpMessageHandler handler = new([
            () => throw new HttpRequestException("Transient network failure."),
            () => Response(HttpStatusCode.ServiceUnavailable, string.Empty),
            () => Response(HttpStatusCode.OK, "ip=8.8.8.8"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        IPAddress address = await service.DiscoverAsync(
            AddressFamily.InterNetwork,
            CancellationToken.None);

        Assert.Equal(IPAddress.Parse("8.8.8.8"), address);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task DiscoverAsyncRetriesTransientBodyReadFailures()
    {
        TrackingThrowingContent failingContent = new();
        RecordingTraceHttpMessageHandler handler = new([
            () => Response(HttpStatusCode.OK, failingContent),
            () => Response(HttpStatusCode.OK, "ip=8.8.8.8"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        IPAddress address = await service.DiscoverAsync(
            AddressFamily.InterNetwork,
            CancellationToken.None);

        Assert.Equal(IPAddress.Parse("8.8.8.8"), address);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(1, failingContent.DisposeCount);
    }

    [Fact]
    public async Task DiscoverAsyncDisposesFinalAttemptResponseWhenBodyReadFails()
    {
        TrackingThrowingContent failingContent = new();
        RecordingTraceHttpMessageHandler handler = new([
            () => throw new HttpRequestException("Transient network failure."),
            () => throw new HttpRequestException("Transient network failure."),
            () => Response(HttpStatusCode.OK, failingContent),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.DiscoverAsync(
            AddressFamily.InterNetwork,
            CancellationToken.None).AsTask());

        Assert.IsType<IOException>(exception.InnerException);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(1, failingContent.DisposeCount);
    }

    [Fact]
    public async Task DiscoverAsyncHonorsRetryAfterHeader()
    {
        RecordingTraceHttpMessageHandler handler = new([
            () => Response(HttpStatusCode.TooManyRequests, string.Empty, TimeSpan.Zero),
            () => Response(HttpStatusCode.OK, "ip=8.8.4.4"),
        ]);

        TraceIpDiscoveryService service = CreateService(handler);

        IPAddress address = await service.DiscoverAsync(
            AddressFamily.InterNetwork,
            CancellationToken.None);

        Assert.Equal(IPAddress.Parse("8.8.4.4"), address);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static TraceIpDiscoveryService CreateService(RecordingTraceHttpMessageHandler handler)
    {
        HttpClient client = new(new CloudflareTraceRetryHandler
        {
            InnerHandler = handler,
        });

        return new TraceIpDiscoveryService(client, NullLogger<TraceIpDiscoveryService>.Instance);
    }

    private static HttpResponseMessage Response(
        HttpStatusCode statusCode,
        string body,
        TimeSpan? retryAfter = null)
    {
        HttpResponseMessage response = new(statusCode)
        {
            Content = new StringContent(body),
        };

        if (retryAfter is not null)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(
                DateTimeOffset.UtcNow.Add(retryAfter.Value));
        }

        return response;
    }

    private static HttpResponseMessage Response(
        HttpStatusCode statusCode,
        HttpContent content,
        TimeSpan? retryAfter = null)
    {
        HttpResponseMessage response = new(statusCode)
        {
            Content = content,
        };

        if (retryAfter is not null)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(
                DateTimeOffset.UtcNow.Add(retryAfter.Value));
        }

        return response;
    }

}

internal sealed class RecordingTraceHttpMessageHandler(IEnumerable<Func<HttpResponseMessage>> responses)
    : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> responses = new(responses);

    public List<CapturedTraceRequest> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(new CapturedTraceRequest(request.Method, request.RequestUri));

        if (responses.Count == 0)
        {
            throw new InvalidOperationException("No more responses configured.");
        }

        Func<HttpResponseMessage> responseFactory = responses.Dequeue();
        return Task.FromResult(responseFactory());
    }
}

internal sealed record CapturedTraceRequest(HttpMethod Method, Uri? RequestUri);

internal sealed class TrackingThrowingContent : HttpContent
{
    public int DisposeCount { get; private set; }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => throw new IOException("Transient response body failure.");

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCount++;
        }

        base.Dispose(disposing);
    }
}
