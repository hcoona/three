using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hcoona.CfDdnsUpdater.Tests;

[Collection(TestCollectionDefinition.Name)]
public sealed class CloudflareZoneResolverTests
{
    [Fact]
    public async Task ResolveAsyncWalksSuffixesUntilItFindsAnExactMatch()
    {
        ScriptedHttpMessageHandler handler = new([
            () => JsonResponse("""
                {
                  "success": true,
                  "result": [],
                  "errors": [],
                  "messages": [],
                  "result_info": {
                    "page": 1,
                    "per_page": 20,
                    "count": 0,
                    "total_count": 0,
                    "total_pages": 1
                  }
                }
                """),
            () => JsonResponse("""
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
                    "per_page": 20,
                    "count": 1,
                    "total_count": 1,
                    "total_pages": 1
                  }
                }
                """),
        ]);

        CloudflareZoneResolver resolver = CreateResolver(handler);

        CloudflareZone zone =
            await resolver.ResolveAsync("API.Example.Com.", CancellationToken.None);

        Assert.Equal(new CloudflareZone("zone-1", "example.com"), zone);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(
            "name=api.example.com",
            handler.Requests[0].RequestUri!.Query);
        Assert.Contains(
            "name=example.com",
            handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task ResolveAsyncFailsClosedWhenMultipleExactMatchesAreVisible()
    {
        ScriptedHttpMessageHandler handler = new([
            () => JsonResponse("""
                {
                  "success": true,
                  "result": [
                    {
                      "id": "zone-1",
                      "name": "example.com"
                    },
                    {
                      "id": "zone-2",
                      "name": "example.com"
                    }
                  ],
                  "errors": [],
                  "messages": [],
                  "result_info": {
                    "page": 1,
                    "per_page": 20,
                    "count": 2,
                    "total_count": 2,
                    "total_pages": 1
                  }
                }
                """),
        ]);

        CloudflareZoneResolver resolver = CreateResolver(handler);

        await Assert.ThrowsAsync<CloudflareZoneResolutionException>(() =>
            resolver.ResolveAsync("example.com", CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ResolveAsyncFailsClosedWhenNoZoneIsVisible()
    {
        ScriptedHttpMessageHandler handler = new([
            () => JsonResponse("""
                {
                  "success": true,
                  "result": [],
                  "errors": [],
                  "messages": [],
                  "result_info": {
                    "page": 1,
                    "per_page": 20,
                    "count": 0,
                    "total_count": 0,
                    "total_pages": 1
                  }
                }
                """),
            () => JsonResponse("""
                {
                  "success": true,
                  "result": [],
                  "errors": [],
                  "messages": [],
                  "result_info": {
                    "page": 1,
                    "per_page": 20,
                    "count": 0,
                    "total_count": 0,
                    "total_pages": 1
                  }
                }
                """),
            () => JsonResponse("""
                {
                  "success": true,
                  "result": [],
                  "errors": [],
                  "messages": [],
                  "result_info": {
                    "page": 1,
                    "per_page": 20,
                    "count": 0,
                    "total_count": 0,
                    "total_pages": 1
                  }
                }
                """),
        ]);

        CloudflareZoneResolver resolver = CreateResolver(handler);

        await Assert.ThrowsAsync<CloudflareZoneResolutionException>(() =>
            resolver.ResolveAsync("host.example.com", CancellationToken.None));

        Assert.Equal(3, handler.Requests.Count);
    }

    private static CloudflareZoneResolver CreateResolver(ScriptedHttpMessageHandler handler)
    {
        HttpClient client = new(handler)
        {
            BaseAddress = new Uri("https://api.cloudflare.com/client/v4/", UriKind.Absolute),
        };

        CloudflareApiClient apiClient =
            new(client, new CloudflareConfiguration("token", [], false));
        return new CloudflareZoneResolver(apiClient, NullLogger<CloudflareZoneResolver>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}
