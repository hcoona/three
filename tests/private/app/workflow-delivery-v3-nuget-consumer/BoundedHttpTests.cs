using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkflowDeliveryV3NuGetConsumer.Tests;

[TestClass]
public sealed class BoundedHttpTests
{
    private const string Token = "synthetic-bounded-consumer-token-abcdef";
    private const string BaseAddress = "https://nuget.pkg.github.com/hcoona/download/";
    private const string VersionsUrl = BaseAddress + ConsumerRequest.NormalizedId + "/index.json";

    [TestMethod]
    public async Task HttpReadStopsAtCumulativeRequestAllowance()
    {
        ConsumerRequest request = Request() with { MaximumRequests = 2, MaximumResponseBytes = 14 };
        var transport = new ResponseHandler(_ => Response("content"));
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);

        for (int index = 0; index < 2; index++)
        {
            using HttpResponseMessage response = await client.GetAsync(VersionsUrl);
            Assert.AreEqual("content", await response.Content.ReadAsStringAsync());
        }
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(VersionsUrl));

        Assert.AreEqual(2, transport.Calls);
        Assert.AreEqual(2, bounded.Requests);
        Assert.AreEqual(14, bounded.ResponseBytes);
        Assert.HasCount(2, Directory.GetFiles(request.EvidencePath, "*-reserved.json"));
        Assert.HasCount(2, Directory.GetFiles(request.EvidencePath, "*-body.bin"));
    }

    [TestMethod]
    public async Task HttpReadStopsBeforeReturningAnOversizedBody()
    {
        ConsumerRequest request = Request() with { MaximumResponseBytes = 4 };
        var transport = new ResponseHandler(_ => Response("abcde"));
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(VersionsUrl));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(VersionsUrl));

        Assert.AreEqual(1, transport.Calls);
        Assert.AreEqual(5, bounded.ResponseBytes);
        Assert.HasCount(1, Directory.GetFiles(request.EvidencePath, "*-reserved.json"));
        Assert.IsEmpty(Directory.GetFiles(request.EvidencePath, "*-body.bin"));
    }

    [TestMethod]
    public async Task HttpReadStopsAtCumulativeResponseAllowance()
    {
        ConsumerRequest request = Request() with { MaximumResponseBytes = 4 };
        int calls = 0;
        var transport = new ResponseHandler(_ => Response(++calls == 1 ? "abc" : "de"));
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);

        using HttpResponseMessage first = await client.GetAsync(VersionsUrl);
        Assert.AreEqual("abc", await first.Content.ReadAsStringAsync());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(VersionsUrl));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(VersionsUrl));

        Assert.AreEqual(2, transport.Calls);
        Assert.AreEqual(5, bounded.ResponseBytes);
        Assert.HasCount(2, Directory.GetFiles(request.EvidencePath, "*-reserved.json"));
        Assert.HasCount(1, Directory.GetFiles(request.EvidencePath, "*-body.bin"));
        Assert.AreEqual(
            "abc",
            await File.ReadAllTextAsync(Path.Combine(request.EvidencePath, "001-body.bin"))
        );
    }

    [TestMethod]
    [DataRow("https://example.invalid/package", "GET")]
    [DataRow("http://nuget.pkg.github.com/hcoona/index.json", "GET")]
    [DataRow(VersionsUrl + "?extra=1", "GET")]
    [DataRow(VersionsUrl, "POST")]
    public async Task HttpReadRejectsOutOfScopeRequests(string url, string method)
    {
        ConsumerRequest request = Request();
        var transport = new ResponseHandler(_ => Response("unexpected"));
        using var client = new HttpClient(new BoundedHttpHandler(request, Token, transport));
        using var message = new HttpRequestMessage(new HttpMethod(method), url);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.SendAsync(message));

        Assert.AreEqual(0, transport.Calls);
        Assert.IsEmpty(Directory.GetFiles(request.EvidencePath));
    }

    [TestMethod]
    [DataRow("body-token")]
    [DataRow("body-basic")]
    [DataRow("body-encoded-token")]
    [DataRow("body-encoded-basic")]
    [DataRow("header-token")]
    [DataRow("header-basic")]
    [DataRow("header-encoded")]
    public async Task HttpReadRejectsCredentialReflectionBeforePersistence(string reflection)
    {
        ConsumerRequest request = Request();
        string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("hcoona:" + Token));
        var transport = new ResponseHandler(_ =>
        {
            HttpResponseMessage response = Response(
                reflection switch
                {
                    "body-token" => Token,
                    "body-basic" => basic,
                    "body-encoded-token" => string.Concat(
                        Encoding.UTF8.GetBytes(Token).Select(value => "%" + value.ToString("X2"))
                    ),
                    "body-encoded-basic" => string.Concat(
                        Encoding.UTF8.GetBytes(basic).Select(value => "%" + value.ToString("X2"))
                    ),
                    _ => "safe body",
                }
            );
            if (reflection.StartsWith("header", StringComparison.Ordinal))
            {
                response.Headers.TryAddWithoutValidation(
                    "X-Unretained-Header",
                    reflection switch
                    {
                        "header-basic" => basic,
                        "header-encoded" => string.Concat(
                            Encoding
                                .UTF8.GetBytes(Token)
                                .Select(value => "%" + value.ToString("X2"))
                        ),
                        _ => Token,
                    }
                );
            }
            return response;
        });
        using var client = new HttpClient(new BoundedHttpHandler(request, Token, transport));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(VersionsUrl));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(VersionsUrl));

        Assert.AreEqual(1, transport.Calls);
        Assert.IsEmpty(Directory.GetFiles(request.EvidencePath, "*-body.bin"));
        Assert.HasCount(1, Directory.GetFiles(request.EvidencePath));
        string reservation = await File.ReadAllTextAsync(
            Directory.GetFiles(request.EvidencePath).Single()
        );
        Assert.DoesNotContain(Token, reservation);
        Assert.DoesNotContain(basic, reservation);
    }

    [TestMethod]
    public async Task HttpReadRejectsRedirectWithoutAnotherSend()
    {
        ConsumerRequest request = Request();
        var transport = new ResponseHandler(_ =>
        {
            HttpResponseMessage response = Response("redirect");
            response.StatusCode = HttpStatusCode.Found;
            response.Headers.Location = new Uri("https://example.invalid/redirect-target");
            return response;
        });
        using var client = new HttpClient(new BoundedHttpHandler(request, Token, transport));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(VersionsUrl));

        Assert.AreEqual(1, transport.Calls);
        Assert.HasCount(1, Directory.GetFiles(request.EvidencePath, "*-response.json"));
        Assert.AreEqual(
            "redirect",
            await File.ReadAllTextAsync(
                Directory.GetFiles(request.EvidencePath, "*-body.bin").Single()
            )
        );
    }

    [TestMethod]
    public async Task HttpReadDeadlineCancelsPendingTransportWithoutRetry()
    {
        ConsumerRequest request = Request() with { TimeoutSeconds = 1 };
        var transport = new ResponseHandler(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable completion.");
        });
        using var client = new HttpClient(new BoundedHttpHandler(request, Token, transport));

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetAsync(VersionsUrl));
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetAsync(VersionsUrl));

        Assert.AreEqual(1, transport.Calls);
        Assert.HasCount(1, Directory.GetFiles(request.EvidencePath, "*-reserved.json"));
        Assert.IsEmpty(Directory.GetFiles(request.EvidencePath, "*-body.bin"));
    }

    private static ConsumerRequest Request()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "wdv3-consumer-http-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path.Combine(directory, "restore-evidence"));
        return new ConsumerRequest(
            directory,
            "1.2.3",
            new string('a', 64),
            new string('b', 64),
            new string('c', 64),
            new string('d', 64),
            BaseAddress,
            3,
            4096,
            30
        );
    }

    private static HttpResponseMessage Response(string body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };

    private sealed class ResponseHandler : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _response;
        internal int Calls { get; private set; }

        internal ResponseHandler(Func<CancellationToken, HttpResponseMessage> response)
        {
            _response = cancellationToken => Task.FromResult(response(cancellationToken));
        }

        internal ResponseHandler(Func<CancellationToken, Task<HttpResponseMessage>> response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage message,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return _response(cancellationToken);
        }
    }
}
