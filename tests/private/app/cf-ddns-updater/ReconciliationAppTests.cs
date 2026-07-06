using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hcoona.CfDdnsUpdater.Tests;

[Collection(TestCollectionDefinition.Name)]
public sealed class ReconciliationAppTests
{
    [Fact]
    public async Task RunAsyncCreatesMissingRecord()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-txt",
            "name": "host.example.com",
            "type": "TXT",
            "content": "hello",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-1",
        "name": "host.example.com",
        "type": "A",
        "content": "8.8.8.8",
        "proxied": false,
        "ttl": 1
    },
    "errors": [],
    "messages": []
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: true).RunAsync(
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(4, apiHandler.Requests.Count);
        Assert.Equal(HttpMethod.Post, apiHandler.Requests[^1].Method);
        Assert.Contains("\"content\":\"8.8.8.8\"", apiHandler.Requests[^1].Body);
        Assert.Contains("\"proxied\":false", apiHandler.Requests[^1].Body);
        Assert.Contains("\"ttl\":1", apiHandler.Requests[^1].Body);
    }

    [Fact]
    public async Task RunAsyncEmitsTargetSpanWithOutcomeTags()
    {
        using ActivityRecorder recorder = ActivityRecorder.Start(
            CloudflareTelemetry.ActivitySourceName);

        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-1",
        "name": "host.example.com",
        "type": "A",
        "content": "8.8.8.8",
        "proxied": false,
        "ttl": 1
    },
    "errors": [],
    "messages": []
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: true).RunAsync(
            CancellationToken.None);

        Assert.Equal(0, exitCode);

        Activity targetActivity = Assert.Single(
            recorder.StoppedActivities,
            activity =>
                activity.OperationName == CloudflareTelemetry.ReconciliationTargetActivityName);
        Assert.Equal(
            "host.example.com",
            targetActivity.GetTagItem(CloudflareTelemetry.DomainTagName));
        Assert.Equal("example.com", targetActivity.GetTagItem(CloudflareTelemetry.ZoneNameTagName));
        Assert.Equal("zone-1", targetActivity.GetTagItem(CloudflareTelemetry.ZoneIdTagName));
        Assert.Equal("A", targetActivity.GetTagItem(CloudflareTelemetry.RecordTypeTagName));
        Assert.Equal(
            "InterNetwork",
            targetActivity.GetTagItem(CloudflareTelemetry.TargetFamilyTagName));
        Assert.Equal("8.8.8.8", targetActivity.GetTagItem(CloudflareTelemetry.AddressTagName));
        Assert.Equal("created", targetActivity.GetTagItem(CloudflareTelemetry.OutcomeTagName));
    }

    [Fact]
    public async Task RunAsyncUpdatesExistingRecordWithoutChangingTtlOrProxied()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.4.4"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-1",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.8",
            "comment": "keep me",
            "tags": [
                "prod",
                "managed"
            ],
            "settings": {
                "flatten_cname": true,
                "ipv4_only": false,
                "ipv6_only": true
            },
            "proxied": false,
            "ttl": 120
        },
        {
            "id": "record-txt",
            "name": "host.example.com",
            "type": "TXT",
            "content": "hello",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 2,
        "total_count": 2,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-1",
        "name": "host.example.com",
        "type": "A",
        "content": "8.8.4.4",
        "comment": "keep me",
        "tags": [
            "prod",
            "managed"
        ],
        "settings": {
            "flatten_cname": true,
            "ipv4_only": false,
            "ipv6_only": true
        },
        "proxied": false,
        "ttl": 120
    },
    "errors": [],
    "messages": []
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: true).RunAsync(
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(HttpMethod.Put, apiHandler.Requests[^1].Method);
        Assert.Contains("\"content\":\"8.8.4.4\"", apiHandler.Requests[^1].Body);
        Assert.Contains("\"comment\":\"keep me\"", apiHandler.Requests[^1].Body);
        Assert.Contains("\"tags\":[\"prod\",\"managed\"]", apiHandler.Requests[^1].Body);
        Assert.Contains(
            "\"settings\":{\"flatten_cname\":true,\"ipv4_only\":false,\"ipv6_only\":true}",
            apiHandler.Requests[^1].Body);
        Assert.Contains("\"proxied\":false", apiHandler.Requests[^1].Body);
        Assert.Contains("\"ttl\":120", apiHandler.Requests[^1].Body);
    }

    [Fact]
    public async Task RunAsyncNoOpsWhenRecordAlreadyMatches()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-1",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.8",
            "proxied": false,
            "ttl": 120
        },
        {
            "id": "record-2",
            "name": "host.example.com",
            "type": "TXT",
            "content": "hello",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 2,
        "total_count": 2,
        "total_pages": 1
    }
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: true).RunAsync(
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(3, apiHandler.Requests.Count);
        Assert.DoesNotContain(
            apiHandler.Requests,
            request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task RunAsyncNoOpsWhenAaaaRecordUsesEquivalentIpv6Formatting()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-a",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.8",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-aaaa",
            "name": "host.example.com",
            "type": "AAAA",
            "content": "2001:4860:4860:0:0:0:0:8888",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: false).RunAsync(
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, traceHandler.Requests.Count);
        Assert.Equal(
            new Uri("https://1.1.1.1/cdn-cgi/trace"),
            traceHandler.Requests[0].RequestUri);
        Assert.Equal(
            new Uri("https://[2606:4700:4700::1111]/cdn-cgi/trace"),
            traceHandler.Requests[1].RequestUri);
        Assert.Equal(4, apiHandler.Requests.Count);
        Assert.Equal(
            "/client/v4/zones/zone-1/dns_records",
            apiHandler.Requests[2].RequestUri!.AbsolutePath);
        Assert.Equal(
            "/client/v4/zones/zone-1/dns_records",
            apiHandler.Requests[3].RequestUri!.AbsolutePath);
        Assert.DoesNotContain(
            apiHandler.Requests,
            request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task RunAsyncFailsClosedOnCnameConflict()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-1",
            "name": "host.example.com",
            "type": "CNAME",
            "content": "alias.example.net",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: true).RunAsync(
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(3, apiHandler.Requests.Count);
        Assert.DoesNotContain(
            apiHandler.Requests,
            request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task RunAsyncFailsClosedOnCnameConflictWithUnrelatedRecords()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-1",
            "name": "host.example.com",
            "type": "CNAME",
            "content": "alias.example.net",
            "proxied": false,
            "ttl": 1
        },
        {
            "id": "record-2",
            "name": "host.example.com",
            "type": "TXT",
            "content": "hello",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 2,
        "total_count": 2,
        "total_pages": 1
    }
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: true).RunAsync(
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(3, apiHandler.Requests.Count);
        Assert.DoesNotContain(
            apiHandler.Requests,
            request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task RunAsyncFailsClosedOnCnameConflictForAaaa()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-1",
            "name": "host.example.com",
            "type": "CNAME",
            "content": "alias.example.net",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-2",
            "name": "host.example.com",
            "type": "CNAME",
            "content": "alias.example.net",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: false).RunAsync(
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(4, apiHandler.Requests.Count);
        Assert.DoesNotContain(
            apiHandler.Requests,
            request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task RunAsyncContinuesAfterOneTargetFails()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-1",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.7",
            "proxied": false,
            "ttl": 1
        },
        {
            "id": "record-2",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.6",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 2,
        "total_count": 2,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-2",
        "name": "host.example.com",
        "type": "AAAA",
        "content": "2001:4860:4860::8888",
        "proxied": false,
        "ttl": 1
    },
    "errors": [],
    "messages": []
}
"""),
        ]);
        RecordingLogger<ReconciliationApp> logger = new();

        int exitCode = await CreateApp(
            apiHandler,
            traceHandler,
            disableIpv6: false,
            logger: logger).RunAsync(
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                "finished: 1 created, 0 updated, 0 no-op, 1 failed.",
                StringComparison.Ordinal));
        Assert.Equal(5, apiHandler.Requests.Count);
        Assert.Contains(apiHandler.Requests, request => request.Method == HttpMethod.Post);
        Assert.Equal(HttpMethod.Post, apiHandler.Requests[^1].Method);
        Assert.Contains("\"type\":\"AAAA\"", apiHandler.Requests[^1].Body);
        Assert.Contains("\"content\":\"2001:4860:4860::8888\"", apiHandler.Requests[^1].Body);
    }

    [Fact]
    public async Task RunAsyncContinuesWhenOneDomainFailsAndAnotherSucceeds()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-1",
            "name": "failed.example.com",
            "type": "CNAME",
            "content": "alias.example.net",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-2",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-2",
        "name": "succeed.example.com",
        "type": "A",
        "content": "8.8.8.8",
        "proxied": false,
        "ttl": 1
    },
    "errors": [],
    "messages": []
}
"""),
        ]);
        RecordingLogger<ReconciliationApp> logger = new();

        int exitCode = await CreateApp(
                apiHandler,
                traceHandler,
                disableIpv6: true,
                domains: ["failed.example.com", "succeed.example.com"],
                logger: logger)
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                "finished: 1 created, 0 updated, 0 no-op, 1 failed.",
                StringComparison.Ordinal));
        Assert.Equal(7, apiHandler.Requests.Count);
        Assert.Contains(apiHandler.Requests, request => request.Method == HttpMethod.Post);
        Assert.DoesNotContain(apiHandler.Requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task RunAsyncContinuesWhenOneDiscoveryFamilyFails()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
            _ => TraceResponse(HttpStatusCode.OK, "colo=sin"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-1",
        "name": "host.example.com",
        "type": "A",
        "content": "8.8.8.8",
        "proxied": false,
        "ttl": 1
    },
    "errors": [],
    "messages": []
}
"""),
        ]);
        RecordingLogger<ReconciliationApp> logger = new();

        int exitCode = await CreateApp(
            apiHandler,
            traceHandler,
            disableIpv6: false,
            logger: logger).RunAsync(
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                "finished: 1 created, 0 updated, 0 no-op, 1 failed.",
                StringComparison.Ordinal));
        Assert.Equal(2, traceHandler.Requests.Count);
        Assert.Equal(4, apiHandler.Requests.Count);
        Assert.Equal(HttpMethod.Post, apiHandler.Requests[^1].Method);
        Assert.Contains("\"type\":\"A\"", apiHandler.Requests[^1].Body);
    }

    [Fact]
    public async Task RunAsyncContinuesWhenFirstDiscoveryFamilyFails()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "colo=sin"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-1",
        "name": "host.example.com",
        "type": "AAAA",
        "content": "2001:4860:4860::8888",
        "proxied": false,
        "ttl": 1
    },
    "errors": [],
    "messages": []
}
"""),
        ]);
        RecordingLogger<ReconciliationApp> logger = new();

        int exitCode = await CreateApp(
            apiHandler,
            traceHandler,
            disableIpv6: false,
            logger: logger).RunAsync(
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                "finished: 1 created, 0 updated, 0 no-op, 1 failed.",
                StringComparison.Ordinal));
        Assert.Equal(2, traceHandler.Requests.Count);
        Assert.Equal(4, apiHandler.Requests.Count);
        Assert.Equal(HttpMethod.Post, apiHandler.Requests[^1].Method);
        Assert.Contains("\"type\":\"AAAA\"", apiHandler.Requests[^1].Body);
        Assert.Contains("\"content\":\"2001:4860:4860::8888\"", apiHandler.Requests[^1].Body);
    }

    [Fact]
    public async Task RunAsyncAggregatesFailuresAcrossDomains()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-1",
        "name": "host-a.example.com",
        "type": "A",
        "content": "8.8.8.8",
        "proxied": false,
        "ttl": 1
    },
    "errors": [],
    "messages": []
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
        ]);
        RecordingLogger<ReconciliationApp> logger = new();

        int exitCode = await CreateApp(
                apiHandler,
                traceHandler,
                disableIpv6: true,
                domains: ["host-a.example.com", "host-b.example.com"],
                logger: logger)
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                "finished: 1 created, 0 updated, 0 no-op, 1 failed.",
                StringComparison.Ordinal));
        Assert.Equal(7, apiHandler.Requests.Count);
        Assert.Contains(apiHandler.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task RunAsyncAggregatesFailuresAcrossDomainsAndFamilies()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-a",
            "name": "host-a.example.com",
            "type": "A",
            "content": "8.8.8.8",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-aaaa",
            "name": "host-a.example.com",
            "type": "AAAA",
            "content": "2001:4860:4860::8844",
            "comment": "keep me",
            "tags": [
                "prod",
                "managed"
            ],
            "settings": {
                "flatten_cname": true,
                "ipv4_only": false,
                "ipv6_only": true
            },
            "proxied": false,
            "ttl": 120
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-aaaa",
        "name": "host-a.example.com",
        "type": "AAAA",
        "content": "2001:4860:4860::8888",
        "comment": "keep me",
        "tags": [
            "prod",
            "managed"
        ],
        "settings": {
            "flatten_cname": true,
            "ipv4_only": false,
            "ipv6_only": true
        },
        "proxied": false,
        "ttl": 120
    },
    "errors": [],
    "messages": []
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
        ]);
        RecordingLogger<ReconciliationApp> logger = new();

        int exitCode = await CreateApp(
                apiHandler,
                traceHandler,
                disableIpv6: false,
                domains: ["host-a.example.com", "host-b.example.com"],
                logger: logger)
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                "finished: 0 created, 1 updated, 1 no-op, 2 failed.",
                StringComparison.Ordinal));
        CapturedHttpRequest updateRequest = Assert.Single(
            apiHandler.Requests,
            request => request.Method == HttpMethod.Put);
        Assert.Contains("\"content\":\"2001:4860:4860::8888\"", updateRequest.Body);
        Assert.Contains("\"comment\":\"keep me\"", updateRequest.Body);
        Assert.Contains("\"tags\":[\"prod\",\"managed\"]", updateRequest.Body);
        Assert.Contains(
            "\"settings\":{\"flatten_cname\":true,\"ipv4_only\":false,\"ipv6_only\":true}",
            updateRequest.Body);
        Assert.Contains("\"proxied\":false", updateRequest.Body);
        Assert.Contains("\"ttl\":120", updateRequest.Body);
    }

    [Fact]
    public async Task RunAsyncFailsClosedOnDuplicateMatchingRecords()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-1",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.7",
            "proxied": false,
            "ttl": 1
        },
        {
            "id": "record-2",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.6",
            "proxied": false,
            "ttl": 1
        },
        {
            "id": "record-3",
            "name": "host.example.com",
            "type": "TXT",
            "content": "keep me",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 3,
        "total_count": 3,
        "total_pages": 1
    }
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: true).RunAsync(
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain(
            apiHandler.Requests,
            request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task RunAsyncFailsClosedOnDuplicateMatchingAaaaRecords()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-a",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.8",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-aaaa-1",
            "name": "host.example.com",
            "type": "AAAA",
            "content": "2001:4860:4860::8844",
            "proxied": false,
            "ttl": 1
        },
        {
            "id": "record-aaaa-2",
            "name": "host.example.com",
            "type": "AAAA",
            "content": "2001:4860:4860::8888",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 2,
        "total_count": 2,
        "total_pages": 1
    }
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: false).RunAsync(
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(4, apiHandler.Requests.Count);
        Assert.DoesNotContain(
            apiHandler.Requests,
            request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task RunAsyncFailsClosedOnProxiedMatchingRecord()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-1",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.8",
            "proxied": true,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: true).RunAsync(
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain(
            apiHandler.Requests,
            request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task RunAsyncFailsClosedOnProxiedMatchingAaaaRecord()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-a",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.8",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-aaaa",
            "name": "host.example.com",
            "type": "AAAA",
            "content": "2001:4860:4860::8888",
            "proxied": true,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: false).RunAsync(
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(4, apiHandler.Requests.Count);
        Assert.DoesNotContain(
            apiHandler.Requests,
            request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task RunAsyncDoesNotContactCloudflareApiWhenNoAddressesAreDiscovered()
    {
        RecordingHttpMessageHandler apiHandler = new([]);
        RecordingLogger<ReconciliationApp> logger = new();
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.BadRequest, "failed"),
            _ => TraceResponse(HttpStatusCode.BadRequest, "failed"),
        ]);

        int exitCode = await CreateApp(
                apiHandler,
                traceHandler,
                disableIpv6: false,
                domains: ["example.com"],
                logger: logger)
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(2, traceHandler.Requests.Count);
        Assert.Empty(apiHandler.Requests);
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                "finished: 0 created, 0 updated, 0 no-op, 2 failed.",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsyncDoesNotContactCloudflareApiWhenIpv4DiscoveryFailsAndIpv6IsDisabled()
    {
        RecordingHttpMessageHandler apiHandler = new([]);
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.BadRequest, "failed"),
        ]);

        int exitCode = await CreateApp(
                apiHandler,
                traceHandler,
                disableIpv6: true)
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Single(traceHandler.Requests);
        Assert.Empty(apiHandler.Requests);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RunAsyncHandlesIpv6RecordsInDualStackMode(
        bool updateAaaaRecord,
        bool createAaaaRecord)
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-a",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.8",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse(createAaaaRecord
                ? """
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""
                : updateAaaaRecord
                    ? """
{
    "success": true,
    "result": [
        {
            "id": "record-aaaa",
            "name": "host.example.com",
            "type": "AAAA",
            "content": "2001:4860:4860::8844",
            "comment": "keep me",
            "tags": [
                "prod",
                "managed"
            ],
            "settings": {
                "flatten_cname": true,
                "ipv4_only": false,
                "ipv6_only": true
            },
            "proxied": false,
            "ttl": 120
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""
                    : """
{
    "success": true,
    "result": [
        {
            "id": "record-aaaa",
            "name": "host.example.com",
            "type": "AAAA",
            "content": "2001:4860:4860::8888",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse(updateAaaaRecord
                ? """
{
    "success": true,
    "result": {
        "id": "record-aaaa",
        "name": "host.example.com",
        "type": "AAAA",
        "content": "2001:4860:4860::8888",
        "comment": "keep me",
        "tags": [
            "prod",
            "managed"
        ],
        "settings": {
            "flatten_cname": true,
            "ipv4_only": false,
            "ipv6_only": true
        },
        "proxied": false,
        "ttl": 120
    },
    "errors": [],
    "messages": []
}
"""
                : """
{
    "success": true,
    "result": {
        "id": "record-aaaa",
        "name": "host.example.com",
        "type": "AAAA",
        "content": "2001:4860:4860::8888",
        "proxied": false,
        "ttl": 1
    },
    "errors": [],
    "messages": []
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: false).RunAsync(
            CancellationToken.None);

        Assert.Equal(0, exitCode);

        if (updateAaaaRecord)
        {
            Assert.Equal(5, apiHandler.Requests.Count);
            Assert.Equal(HttpMethod.Put, apiHandler.Requests[^1].Method);
            Assert.Contains("\"type\":\"AAAA\"", apiHandler.Requests[^1].Body);
            Assert.Contains("\"content\":\"2001:4860:4860::8888\"", apiHandler.Requests[^1].Body);
            Assert.Contains("\"comment\":\"keep me\"", apiHandler.Requests[^1].Body);
            Assert.Contains("\"tags\":[\"prod\",\"managed\"]", apiHandler.Requests[^1].Body);
            Assert.Contains(
            "\"settings\":{\"flatten_cname\":true,\"ipv4_only\":false,\"ipv6_only\":true}",
            apiHandler.Requests[^1].Body);
            Assert.Contains("\"proxied\":false", apiHandler.Requests[^1].Body);
            Assert.Contains("\"ttl\":120", apiHandler.Requests[^1].Body);
        }
        else if (createAaaaRecord)
        {
            Assert.Equal(5, apiHandler.Requests.Count);
            Assert.Equal(HttpMethod.Post, apiHandler.Requests[^1].Method);
            Assert.Contains("\"type\":\"AAAA\"", apiHandler.Requests[^1].Body);
            Assert.Contains("\"content\":\"2001:4860:4860::8888\"", apiHandler.Requests[^1].Body);
        }
        else
        {
            Assert.Equal(4, apiHandler.Requests.Count);
            Assert.DoesNotContain(
            apiHandler.Requests,
            request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Put);
        }
    }

    [Fact]
    public async Task RunAsyncCreatesMissingARecordInDualStackMode()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-a",
        "name": "host.example.com",
        "type": "A",
        "content": "8.8.8.8",
        "proxied": false,
        "ttl": 1
    },
    "errors": [],
    "messages": []
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-aaaa",
            "name": "host.example.com",
            "type": "AAAA",
            "content": "2001:4860:4860::8888",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: false).RunAsync(
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(5, apiHandler.Requests.Count);
        Assert.Single(apiHandler.Requests, request => request.Method == HttpMethod.Post);
        Assert.DoesNotContain(apiHandler.Requests, request => request.Method == HttpMethod.Put);
        Assert.Contains(
            "\"type\":\"A\"",
            apiHandler.Requests.Single(request => request.Method == HttpMethod.Post).Body);
        Assert.Contains(
            "\"content\":\"8.8.8.8\"",
            apiHandler.Requests.Single(request => request.Method == HttpMethod.Post).Body);
    }

    [Fact]
    public async Task RunAsyncUpdatesExistingARecordInDualStackMode()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.4.4"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-a",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.8",
            "comment": "keep me",
            "tags": [
                "prod",
                "managed"
            ],
            "settings": {
                "flatten_cname": true,
                "ipv4_only": false,
                "ipv6_only": true
            },
            "proxied": false,
            "ttl": 120
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-a",
        "name": "host.example.com",
        "type": "A",
        "content": "8.8.4.4",
        "comment": "keep me",
        "tags": [
            "prod",
            "managed"
        ],
        "settings": {
            "flatten_cname": true,
            "ipv4_only": false,
            "ipv6_only": true
        },
        "proxied": false,
        "ttl": 120
    },
    "errors": [],
    "messages": []
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-aaaa",
            "name": "host.example.com",
            "type": "AAAA",
            "content": "2001:4860:4860::8888",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: false).RunAsync(
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(5, apiHandler.Requests.Count);
        Assert.Equal(HttpMethod.Put, apiHandler.Requests[3].Method);
        Assert.Contains("\"type\":\"A\"", apiHandler.Requests[3].Body);
        Assert.Contains("\"content\":\"8.8.4.4\"", apiHandler.Requests[3].Body);
        Assert.Contains("\"comment\":\"keep me\"", apiHandler.Requests[3].Body);
        Assert.Contains("\"tags\":[\"prod\",\"managed\"]", apiHandler.Requests[3].Body);
        Assert.Contains(
            "\"settings\":{\"flatten_cname\":true,\"ipv4_only\":false,\"ipv6_only\":true}",
            apiHandler.Requests[3].Body);
        Assert.Contains("\"proxied\":false", apiHandler.Requests[3].Body);
        Assert.Contains("\"ttl\":120", apiHandler.Requests[3].Body);
    }

    [Fact]
    public async Task RunAsyncUpdatesExistingARecordAndCreatesMissingAaaaRecordInDualStackMode()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.4.4"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-a",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.8",
            "comment": "keep me",
            "tags": [
                "prod",
                "managed"
            ],
            "settings": {
                "flatten_cname": true,
                "ipv4_only": false,
                "ipv6_only": true
            },
            "proxied": false,
            "ttl": 120
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-a",
        "name": "host.example.com",
        "type": "A",
        "content": "8.8.4.4",
        "comment": "keep me",
        "tags": [
            "prod",
            "managed"
        ],
        "settings": {
            "flatten_cname": true,
            "ipv4_only": false,
            "ipv6_only": true
        },
        "proxied": false,
        "ttl": 120
    },
    "errors": [],
    "messages": []
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-aaaa",
        "name": "host.example.com",
        "type": "AAAA",
        "content": "2001:4860:4860::8888",
        "proxied": false,
        "ttl": 1
    },
    "errors": [],
    "messages": []
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: false).RunAsync(
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(6, apiHandler.Requests.Count);
        Assert.Equal(HttpMethod.Put, apiHandler.Requests[3].Method);
        Assert.Contains("\"type\":\"A\"", apiHandler.Requests[3].Body);
        Assert.Contains("\"content\":\"8.8.4.4\"", apiHandler.Requests[3].Body);
        Assert.Equal(HttpMethod.Post, apiHandler.Requests[5].Method);
        Assert.Contains("\"type\":\"AAAA\"", apiHandler.Requests[5].Body);
        Assert.Contains("\"content\":\"2001:4860:4860::8888\"", apiHandler.Requests[5].Body);
    }

    [Fact]
    public async Task RunAsyncCreatesMissingAaaaRecordWhenARecordAlreadyMatches()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-a",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.8",
            "proxied": false,
            "ttl": 1
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-aaaa",
        "name": "host.example.com",
        "type": "AAAA",
        "content": "2001:4860:4860::8888",
        "proxied": false,
        "ttl": 1
    },
    "errors": [],
    "messages": []
}
"""),
        ]);

        int exitCode = await CreateApp(apiHandler, traceHandler, disableIpv6: false).RunAsync(
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(5, apiHandler.Requests.Count);
        Assert.Equal(HttpMethod.Post, apiHandler.Requests[^1].Method);
        Assert.Contains("\"type\":\"AAAA\"", apiHandler.Requests[^1].Body);
        Assert.Contains("\"content\":\"2001:4860:4860::8888\"", apiHandler.Requests[^1].Body);
        Assert.DoesNotContain(apiHandler.Requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task RunAsyncIgnoresPreExistingAaaaRecordWhenIpv6IsDisabled()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
        ]);

        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "record-a",
            "name": "host.example.com",
            "type": "A",
            "content": "8.8.8.8",
            "proxied": false,
            "ttl": 120
        },
        {
            "id": "record-aaaa",
            "name": "host.example.com",
            "type": "AAAA",
            "content": "2001:4860:4860::8844",
            "proxied": false,
            "ttl": 120
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 2,
        "total_count": 2,
        "total_pages": 1
    }
}
"""),
        ]);
        RecordingLogger<ReconciliationApp> logger = new();

        int exitCode = await CreateApp(
                apiHandler,
                traceHandler,
                disableIpv6: true,
                logger: logger)
            .RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Single(traceHandler.Requests);
        Assert.Equal(3, apiHandler.Requests.Count);
        Assert.DoesNotContain(
            apiHandler.Requests,
            request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Put);
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                "finished: 0 created, 0 updated, 1 no-op, 0 failed.",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsyncStopsBeforeDnsReconciliationWhenZoneResolutionFails()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingLogger<ReconciliationApp> logger = new();
        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
        ]);

        int exitCode = await CreateApp(
                apiHandler,
                traceHandler,
                disableIpv6: false,
                logger: logger)
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(3, apiHandler.Requests.Count);
        Assert.All(
            apiHandler.Requests,
            request => Assert.DoesNotContain(
                "/dns_records",
                request.RequestUri!.AbsolutePath,
                StringComparison.Ordinal));
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                "finished: 0 created, 0 updated, 0 no-op, 2 failed.",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsyncTreatsTimeoutsAsTargetFailuresAndContinues()
    {
        RecordingHttpMessageHandler traceHandler = new([
            _ => TraceResponse(HttpStatusCode.OK, "ip=8.8.8.8"),
            _ => TraceResponse(HttpStatusCode.OK, "ip=2001:4860:4860::8888"),
        ]);

        RecordingLogger<ReconciliationApp> logger = new();
        RecordingHttpMessageHandler apiHandler = new([
            _ => JsonResponse("""
{
    "success": true,
    "result": [
        {
            "id": "zone-1",
            "name": "example.com"
        }
    ],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 1,
        "total_count": 1,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": [],
    "errors": [],
    "messages": [],
    "result_info": {
        "page": 1,
        "per_page": 100,
        "count": 0,
        "total_count": 0,
        "total_pages": 1
    }
}
"""),
            _ => JsonResponse("""
{
    "success": true,
    "result": {
        "id": "record-1",
        "name": "host.example.com",
        "type": "A",
        "content": "8.8.8.8",
        "proxied": false,
        "ttl": 1
    },
    "errors": [],
    "messages": []
}
"""),
            _ => throw new OperationCanceledException("The request timed out."),
        ]);

        int exitCode = await CreateApp(
            apiHandler,
            traceHandler,
            disableIpv6: false,
            logger: logger)
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(4, apiHandler.Requests.Count);
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                "Failed to reconcile host.example.com in zone example.com for InterNetworkV6",
                StringComparison.Ordinal));
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                "finished: 1 created, 0 updated, 0 no-op, 1 failed.",
                StringComparison.Ordinal));
    }

    private static ReconciliationApp CreateApp(
        RecordingHttpMessageHandler apiHandler,
        RecordingHttpMessageHandler traceHandler,
        bool disableIpv6,
        string[]? domains = null,
        ILogger<ReconciliationApp>? logger = null)
    {
        string[] configuredDomains = domains ?? ["host.example.com"];
        CloudflareConfiguration configuration = new(
            "token",
            ImmutableArray.Create(configuredDomains),
            disableIpv6);

        HttpClient apiHttpClient = new(apiHandler)
        {
            BaseAddress = new Uri("https://api.cloudflare.com/client/v4/", UriKind.Absolute),
        };

        CloudflareApiClient apiClient = new(apiHttpClient, configuration);
        CloudflareZoneResolver zoneResolver = new(
            apiClient,
            NullLogger<CloudflareZoneResolver>.Instance);
        CloudflareDnsRecordClient dnsRecordClient = new(apiClient);

        HttpClient traceHttpClient = new(new CloudflareTraceRetryHandler
        {
            InnerHandler = traceHandler,
        });

        TraceIpDiscoveryService traceIpDiscoveryService =
            new(traceHttpClient, NullLogger<TraceIpDiscoveryService>.Instance);

        return new ReconciliationApp(
            logger ?? NullLogger<ReconciliationApp>.Instance,
            configuration,
            traceIpDiscoveryService,
            zoneResolver,
            dnsRecordClient);
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

    private static HttpResponseMessage TraceResponse(HttpStatusCode statusCode, string body)
        => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain"),
        };
}

internal sealed class RecordingHttpMessageHandler(
    IEnumerable<Func<HttpRequestMessage, HttpResponseMessage>> responses)
    : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses =
        new(responses);

    public List<CapturedHttpRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string body = string.Empty;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        Requests.Add(new CapturedHttpRequest(request.Method, request.RequestUri, body));

        if (responses.Count == 0)
        {
            throw new InvalidOperationException("No more scripted responses are available.");
        }

        return responses.Dequeue()(request);
    }
}

internal sealed record CapturedHttpRequest(HttpMethod Method, Uri? RequestUri, string Body);

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    IDisposable ILogger.BeginScope<TState>(TState state)
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Messages.Add(formatter(state, exception));
}

internal sealed class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new();

    public void Dispose()
    {
    }
}
