using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class DestinationHttpTests(TestContext context)
{
    private const string Host = "registry.example";
    private static readonly Uri Resource = new($"https://{Host}/packages");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    [DataRow("headers")]
    [DataRow("body")]
    [DataRow("caller")]
    public async Task ReadDeadlineCoversHeadersAndBodyAndHonorsCancellation(string boundary)
    {
        var clock = new FakeTimeProvider();
        using var caller =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        using var handler = new ScriptedHttpHandler((_, _, token) =>
        {
            if (boundary == "headers")
            {
                clock.Advance(RequestTimeout);
                token.ThrowIfCancellationRequested();
                throw new AssertFailedException("Request escaped its deadline.");
            }

            clock.Advance(TimeSpan.FromSeconds(3));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new CancellingBody(() =>
                {
                    if (boundary == "caller") caller.Cancel();
                    else clock.Advance(TimeSpan.FromSeconds(2));
                })),
            });
        });
        using var client = new HttpClient(handler);
        using var session = new DestinationHttpSession(client, clock, TimeSpan.FromMinutes(1),
            RequestTimeout, caller.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => ReadAsync(session));
    }

    [TestMethod]
    public async Task MultipleReadsShareAnOperationDeadline()
    {
        var clock = new FakeTimeProvider();
        using var handler = new ScriptedHttpHandler((_, _) =>
        {
            clock.Advance(TimeSpan.FromSeconds(3));
            return Response("{}");
        });
        using var client = new HttpClient(handler);
        using var session = new DestinationHttpSession(client, clock, TimeSpan.FromSeconds(6),
            RequestTimeout, context.CancellationToken);

        Assert.AreEqual(200, (await ReadAsync(session)).Status);
        await Assert.ThrowsAsync<OperationCanceledException>(() => ReadAsync(session));
    }

    [TestMethod]
    [DataRow("iDeNtItY", true)]
    [DataRow("gzip", false)]
    public async Task OnlyIdentityEncodingCanBeConsumed(string encoding, bool supported)
    {
        using var handler = new ScriptedHttpHandler((_, _) =>
        {
            var response = Response("{}");
            response.Content.Headers.ContentEncoding.Add(encoding);
            return response;
        });
        using var client = new HttpClient(handler);
        using var session = Session(client);

        if (supported)
            Assert.AreEqual("{}", Encoding.UTF8.GetString((await ReadAsync(session)).Body));
        else
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReadAsync(session));
        var request = Assert.ContainsSingle(handler.Requests);
        Assert.AreEqual(HttpMethod.Get, request.Method);
        Assert.AreEqual("identity", Assert.ContainsSingle(request.Headers.AcceptEncoding).Value);
    }

    [TestMethod]
    [DataRow(null, 5)]
    [DataRow(5L, 3)]
    [DataRow(4L, 3)]
    [DataRow(2L, 3)]
    public async Task IncompleteOrOversizedMetadataCannotBeReturned(long? declared, int size)
    {
        using var handler = new ScriptedHttpHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new ByteArrayContent(new byte[size]),
            };
            response.Content.Headers.ContentLength = declared;
            return response;
        });
        using var client = new HttpClient(handler);
        using var session = Session(client);

        await Assert.ThrowsExactlyAsync<IOException>(() => ReadAsync(session, limit: 4));
    }

    [TestMethod]
    [DataRow("http://registry.example/packages")]
    [DataRow("https://other.example/packages")]
    [DataRow("https://person@registry.example/packages")]
    [DataRow("https://registry.example:444/packages")]
    [DataRow("https://registry.example/packages#fragment")]
    [DataRow("https://registry.example/packages?query")]
    public async Task RefusedOriginIsNeverContacted(string uri)
    {
        using var handler = new ScriptedHttpHandler((_, _) =>
            throw new AssertFailedException("Unexpected outbound request."));
        using var client = new HttpClient(handler);
        using var session = Session(client);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => session.MetadataAsync("read",
            new Uri(uri), Host, "application/json", 100, allowQuery: false));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public void StrictJsonRejectsDuplicatePropertiesInBothEntryPoints()
    {
        const string ambiguous = """{"versions":{"1.0":{},"1.0":{}}}""";
        Assert.Throws<JsonException>(() => StrictJson.Parse(ambiguous));
        Assert.Throws<JsonException>(() => StrictJson.Parse(Encoding.UTF8.GetBytes(ambiguous)));
    }

    [TestMethod]
    [DataRow("", "hcoona/three", true)]
    [DataRow("\"owner\":null,", "hcoona/three", true)]
    [DataRow("\"owner\":{\"login\":\"hcoona\"},", "hcoona/three", true)]
    [DataRow("\"owner\":{\"login\":\"other\"},", "hcoona/three", false)]
    [DataRow("", "hcoona/other", false)]
    public async Task UserPackageMustBelongToTheSelectedRepositoryAndOwner(string owner,
        string repository, bool matches)
    {
        using var handler = new ScriptedHttpHandler((_, _) => Response($$$"""
            {"name":"example","package_type":"npm",{{{owner}}}
             "repository":{"full_name":"{{{repository}}}"}}
            """));
        using var client = new HttpClient(handler);
        using var session = Session(client);

        ReleaseFileState state = await new GithubApi(session, "controlled", 1_000)
            .CheckPackageAsync("package", "npm", "example", name => name == "example",
                allowAbsent: false);

        Assert.AreEqual(matches ? ReleaseFileState.Exact : ReleaseFileState.Conflict, state);
        var request = Assert.ContainsSingle(handler.Requests);
        Assert.AreEqual("https://api.github.com/users/hcoona/packages/npm/example",
            request.Uri.AbsoluteUri);
        Assert.AreEqual("Bearer controlled", request.Headers.Authorization?.ToString());
    }

    [TestMethod]
    [DataRow(99)]
    [DataRow(100)]
    public async Task GitHubArrayReadPaginatesUntilAShortPage(int firstPageCount)
    {
        using var handler = new ScriptedHttpHandler((request, page) =>
            request.RequestUri!.Query switch
            {
                "?state=active&per_page=100&page=1" when page == 0 =>
                    Response(JsonSerializer.Serialize(Enumerable.Range(0, firstPageCount))),
                "?state=active&per_page=100&page=2" when page == 1 => Response("[1000]"),
                _ => throw new AssertFailedException($"Unexpected request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        using var session = Session(client);
        var items = new List<int>();

        await new GithubApi(session, "controlled", 1_000).ReadArrayAsync("versions",
            new Uri("https://api.github.com/packages/versions?state=active"),
            item => items.Add(item.GetInt32()));

        IEnumerable<int> expected = Enumerable.Range(0, firstPageCount);
        if (firstPageCount == 100) expected = expected.Append(1000);
        CollectionAssert.AreEqual(expected.ToArray(), items.ToArray());
    }

    [TestMethod]
    public async Task GitHubArrayReadRejectsOversizedPageBeforeConsumingAnyItems()
    {
        using var handler = new ScriptedHttpHandler((_, _) =>
            Response(JsonSerializer.Serialize(Enumerable.Range(0, 101))));
        using var client = new HttpClient(handler);
        using var session = Session(client);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new GithubApi(session, "controlled", 1_000).ReadArrayAsync("versions",
                new Uri("https://api.github.com/packages/versions"),
                _ => Assert.Fail("Unexpected item from oversized page.")));
        Assert.HasCount(1, handler.Requests);
    }

    private DestinationHttpSession Session(HttpClient client) => new(client, null,
        TimeSpan.FromMinutes(1), RequestTimeout, context.CancellationToken);

    private static Task<(int Status, byte[] Body)> ReadAsync(DestinationHttpSession session,
        int limit = 100) =>
        session.MetadataAsync("read", Resource, Host, "application/json", limit);

    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class CancellingBody(Action cancel) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new AssertFailedException("Body escaped cancellation.");
        }
    }
}
