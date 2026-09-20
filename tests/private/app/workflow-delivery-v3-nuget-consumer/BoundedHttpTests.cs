using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkflowDeliveryV3NuGetConsumer.Tests;

[TestClass]
public sealed class BoundedHttpTests
{
    private const string Token = "synthetic-bounded-consumer-token-abcdef";
    private const string BaseAddress = "https://nuget.pkg.github.com/hcoona/download/";
    private const string VersionsUrl = BaseAddress + ConsumerRequest.NormalizedId + "/index.json";
    private const string StorageUrl =
        "https://storage.example.invalid/a/../package%2Fn.nupkg?sig=signed%2Fvalue&x=1";

    [TestMethod]
    [DataRow("schema")]
    [DataRow("policy")]
    [DataRow("missing-policy")]
    public void ConsumerRequestRejectsOldOrChangedRedirectContract(string change)
    {
        JsonObject document = Request().ToDocument();
        if (change == "schema")
            document["schema"] = "workflow-delivery/v3/nuget-consumer-restore-request";
        if (change == "policy") document["packageRedirectPolicy"] = "automatic";
        if (change == "missing-policy") document.Remove("packageRedirectPolicy");

        Assert.ThrowsExactly<InvalidDataException>(() =>
            ConsumerRequest.Read(Encoding.UTF8.GetBytes(document.ToJsonString()))
        );
    }

    [TestMethod]
    public async Task HttpReadStopsAtCumulativeRequestAllowance()
    {
        ConsumerRequest request = Request() with { MaximumRequests = 2, MaximumResponseBytes = 15 };
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
    [DataRow("abcd")]
    [DataRow("abcde")]
    public async Task HttpReadStopsBeforeReturningAnOversizedBody(string body)
    {
        ConsumerRequest request = Request() with { MaximumResponseBytes = 4 };
        using var stream = new TrackingStream(body);
        var transport = new ResponseHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }
        );
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(VersionsUrl));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(VersionsUrl));

        Assert.AreEqual(1, transport.Calls);
        Assert.AreEqual(4, stream.BytesRead);
        Assert.AreEqual(stream.BytesRead, bounded.ResponseBytes);
        Assert.HasCount(1, Directory.GetFiles(request.EvidencePath, "*-reserved.json"));
        Assert.IsEmpty(Directory.GetFiles(request.EvidencePath, "*-body.bin"));
    }

    [TestMethod]
    public async Task HttpReadStopsAtCumulativeResponseAllowance()
    {
        ConsumerRequest request = Request() with { MaximumResponseBytes = 4 };
        int calls = 0;
        using var firstStream = new TrackingStream("abc");
        using var secondStream = new TrackingStream("de");
        var transport = new ResponseHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(++calls == 1 ? firstStream : secondStream),
            }
        );
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);

        using HttpResponseMessage first = await client.GetAsync(VersionsUrl);
        Assert.AreEqual("abc", await first.Content.ReadAsStringAsync());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(VersionsUrl));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(VersionsUrl));

        Assert.AreEqual(2, transport.Calls);
        Assert.AreEqual(3, firstStream.BytesRead);
        Assert.AreEqual(1, secondStream.BytesRead);
        Assert.AreEqual(firstStream.BytesRead + secondStream.BytesRead, bounded.ResponseBytes);
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
    [DataRow(VersionsUrl)]
    [DataRow(ConsumerRequest.ServiceIndex)]
    public async Task HttpReadRejectsRedirectWithoutAnotherSend(string url)
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

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(url));

        Assert.AreEqual(1, transport.Calls);
        Assert.HasCount(1, Directory.GetFiles(request.EvidencePath, "*-response.json"));
        Assert.IsEmpty(Directory.GetFiles(request.EvidencePath, "*-body.bin"));
        JsonObject evidence = ReadEvidence(request, "001-response.json");
        Assert.AreEqual("omitted", evidence["bodyRetention"]!.GetValue<string>());
        Assert.AreEqual(8L, evidence["bytes"]!.GetValue<long>());
    }

    [TestMethod]
    [DataRow(200)]
    [DataRow(301)]
    [DataRow(302)]
    public async Task PackageReadReturnsOriginalBytesWithBoundedRedirectEvidence(int status)
    {
        byte[] package = "original package"u8.ToArray();
        ConsumerRequest request = Request() with
        {
            PackageSha256 = ConsumerRequest.Sha256(package)
        };
        var transport = new InspectingHandler((message, _) =>
        {
            if (message.RequestUri!.OriginalString == request.PackageUrl)
            {
                Assert.AreEqual("Basic", message.Headers.Authorization!.Scheme);
                return Task.FromResult(
                    status == 200 ? Response("original package") : Redirect(status, StorageUrl)
                );
            }
            Assert.AreEqual(StorageUrl, message.RequestUri.OriginalString);
            Assert.AreEqual(StorageUrl, message.RequestUri.AbsoluteUri);
            Assert.AreEqual(HttpMethod.Get, message.Method);
            Assert.IsNull(message.Content);
            Assert.IsEmpty(message.Headers);
            return Task.FromResult(Response("original package"));
        });
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);
        using var message = new HttpRequestMessage(HttpMethod.Get, request.PackageUrl);
        message.Headers.TryAddWithoutValidation("Cookie", "credential cookie");
        message.Headers.TryAddWithoutValidation("X-NuGet-ApiKey", "credential key");
        message.Headers.TryAddWithoutValidation("Referer", request.PackageUrl);
        message.Headers.TryAddWithoutValidation("X-Custom", "must not be forwarded");

        using HttpResponseMessage result = await client.SendAsync(message);

        CollectionAssert.AreEqual(package, await result.Content.ReadAsByteArrayAsync());
        Assert.AreSame(message, result.RequestMessage);
        Assert.AreEqual(status == 200 ? 1 : 2, transport.Calls);
        Assert.AreEqual(transport.Calls, bounded.Requests);
        Assert.AreEqual(package.Length + (status == 200 ? 0 : 8), bounded.ResponseBytes);
        Assert.IsTrue(bounded.PackageReturned);
        Assert.AreEqual(transport.Calls, bounded.PackageResponseIndex);
        Assert.HasCount(1, Directory.GetFiles(request.EvidencePath, "*-body.bin"));
        if (status != 200)
        {
            JsonObject redirect = ReadEvidence(request, "001-response.json");
            JsonObject storage = ReadEvidence(request, "002-reserved.json");
            Assert.AreEqual("omitted", redirect["bodyRetention"]!.GetValue<string>());
            Assert.IsNull(redirect["sha256"]);
            Assert.IsNull(storage["url"]);
            Assert.AreEqual(1, storage["redirectedFrom"]!.GetValue<int>());
            Assert.AreEqual(
                "https://storage.example.invalid", storage["origin"]!.GetValue<string>()
            );
            Assert.AreEqual(
                ConsumerRequest.Sha256(Encoding.UTF8.GetBytes(StorageUrl)),
                storage["locationSha256"]!.GetValue<string>()
            );
            Assert.IsTrue(
                JsonNode.DeepEquals(redirect["locationSha256"], storage["locationSha256"])
            );
            Assert.AreEqual(
                request.MaximumResponseBytes - 8,
                storage["maximumRemainingResponseBytes"]!.GetValue<long>()
            );
        }
        AssertEvidenceOmitsCapabilities(request);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("/relative")]
    [DataRow("http://storage.example.invalid/p")]
    [DataRow(" https://storage.example.invalid/p")]
    [DataRow("https://storage.example.invalid/p\t")]
    [DataRow("https://storage.example.invalid/p\n")]
    [DataRow("https://storage.example.invalid/p\\x")]
    [DataRow("https://storage.example.invalid/p#fragment")]
    [DataRow("https://user@storage.example.invalid/p")]
    [DataRow("https://storage.example.invalid:443/p")]
    [DataRow("https://127.0.0.1/p")]
    [DataRow("https://0x7f.1/p")]
    [DataRow("https://2130706433/p")]
    [DataRow("https://[::1]/p")]
    [DataRow("https://api.github.com/p")]
    [DataRow("https://storage..invalid/p")]
    [DataRow("https://-storage.invalid/p")]
    [DataRow("https://storage.invalid./p")]
    [DataRow("https://storage.invalid/p\u00e9")]
    [DataRow("https://storage.invalid/" + Token)]
    public async Task PackageRedirectRejectsUnsafeOriginalLocationWithoutSecondSend(string location)
    {
        ConsumerRequest request = Request();
        var transport = new ResponseHandler(_ => Redirect(302, location));
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );

        Assert.AreEqual(1, transport.Calls);
        Assert.AreEqual(1, bounded.Requests);
        Assert.IsFalse(bounded.PackageReturned);
        Assert.IsEmpty(Directory.GetFiles(request.EvidencePath, "*-body.bin"));
        AssertEvidenceOmitsCapabilities(request);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("multiple")]
    [DataRow("307")]
    [DataRow("308")]
    [DataRow("200-location")]
    public async Task PackageReadRejectsUnadmittedRedirectShape(string shape)
    {
        ConsumerRequest request = Request();
        var transport = new ResponseHandler(_ =>
        {
            var response = Redirect(
                shape switch { "307" => 307, "308" => 308, "200-location" => 200, _ => 302 },
                StorageUrl
            );
            if (shape == "missing") response.Headers.Remove("Location");
            if (shape == "multiple")
                response.Headers.TryAddWithoutValidation("Location", "https://other.invalid/p");
            return response;
        });
        using var client = new HttpClient(new BoundedHttpHandler(request, Token, transport));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );

        Assert.AreEqual(1, transport.Calls);
        Assert.IsEmpty(Directory.GetFiles(request.EvidencePath, "*-body.bin"));
        AssertEvidenceOmitsCapabilities(request);
    }

    [TestMethod]
    [DataRow(301, true)]
    [DataRow(302, true)]
    [DataRow(403, true)]
    [DataRow(429, true)]
    [DataRow(500, true)]
    [DataRow(200, true)]
    [DataRow(403, false)]
    [DataRow(429, false)]
    [DataRow(500, false)]
    public async Task StorageFailureOmitsBodyAndNeverRetries(int status, bool location)
    {
        ConsumerRequest request = Request();
        int calls = 0;
        var transport = new ResponseHandler(_ =>
        {
            var response = ++calls == 1 ? Redirect(302, StorageUrl) : Redirect(status, StorageUrl);
            if (calls > 1 && !location) response.Headers.Remove("Location");
            return response;
        });
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );

        Assert.AreEqual(2, transport.Calls);
        Assert.AreEqual(16, bounded.ResponseBytes);
        Assert.IsFalse(bounded.PackageReturned);
        Assert.IsEmpty(Directory.GetFiles(request.EvidencePath, "*-body.bin"));
        Assert.AreEqual(
            "omitted",
            ReadEvidence(request, "002-response.json")["bodyRetention"]!.GetValue<string>()
        );
        AssertEvidenceOmitsCapabilities(request);
    }

    [TestMethod]
    public async Task StoragePackageMustMatchAdmittedOriginal()
    {
        ConsumerRequest request = Request();
        int calls = 0;
        var transport = new ResponseHandler(_ =>
            ++calls == 1 ? Redirect(302, StorageUrl) : Response("different package")
        );
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );

        Assert.AreEqual(2, transport.Calls);
        Assert.IsFalse(bounded.PackageReturned);
        Assert.AreEqual(0, bounded.PackageResponseIndex);
        Assert.AreEqual(
            "different package",
            File.ReadAllText(Path.Combine(request.EvidencePath, "002-body.bin"))
        );
    }

    [TestMethod]
    [DataRow("original-header", false)]
    [DataRow("original-header", true)]
    [DataRow("storage-header", false)]
    [DataRow("storage-header", true)]
    [DataRow("storage-body", false)]
    [DataRow("storage-body", true)]
    public async Task PackageRedirectRejectsRawAndDecodedCapabilityReflection(
        string place, bool decoded
    )
    {
        ConsumerRequest request = Request();
        string reflected = decoded ? Uri.UnescapeDataString(StorageUrl) : StorageUrl;
        int calls = 0;
        var transport = new ResponseHandler(_ =>
        {
            bool first = ++calls == 1;
            var response = first ? Redirect(302, StorageUrl)
                : Response(place == "storage-body" ? reflected : "package");
            if ((first && place == "original-header") || (!first && place == "storage-header"))
                response.Headers.TryAddWithoutValidation("X-Unretained", reflected);
            return response;
        });
        using var client = new HttpClient(new BoundedHttpHandler(request, Token, transport));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );

        Assert.AreEqual(place == "original-header" ? 1 : 2, transport.Calls);
        Assert.IsEmpty(Directory.GetFiles(request.EvidencePath, "*-body.bin"));
        AssertEvidenceOmitsCapabilities(request);
    }

    private const string ReflectionLocation =
        "https://storage.example.invalid/opaque%2Fpart?sig=a%27b%22c%3Cd%3E&x=1";

    private static readonly string[] ReflectionForms =
    [
        ReflectionLocation,
        "https://storage.example.invalid/opaque%2Fpart?sig=a%27b%22c%3Cd%3E&amp;x=1",
        "https://storage.example.invalid/opaque/part?sig=a'b\"c<d>&x=1",
        "https://storage.example.invalid/opaque/part?sig=a&#x27;b&quot;c&lt;d&gt;&amp;x=1",
        "/opaque%2Fpart?sig=a%27b%22c%3Cd%3E&x=1",
        "/opaque%2Fpart?sig=a%27b%22c%3Cd%3E&amp;x=1",
        "/opaque/part?sig=a'b\"c<d>&x=1",
        "/opaque/part?sig=a&#x27;b&quot;c&lt;d&gt;&amp;x=1",
        "sig=a%27b%22c%3Cd%3E&x=1",
        "sig=a%27b%22c%3Cd%3E&amp;x=1",
        "sig=a'b\"c<d>&x=1",
        "sig=a&#x27;b&quot;c&lt;d&gt;&amp;x=1",
    ];

    public static IEnumerable<(string place, string reflected)> AdmittedReflectionCases()
    {
        foreach (string place in new[] { "original-header", "storage-header", "storage-body" })
            foreach (string reflected in ReflectionForms)
                yield return (place, reflected);
    }

    [TestMethod]
    [DynamicData(nameof(AdmittedReflectionCases))]
    public async Task AdmittedLocationFormsStopBeforeEvidence(string place, string reflected)
    {
        ConsumerRequest request = Request();
        int calls = 0;
        var transport = new ResponseHandler(_ =>
        {
            bool first = ++calls == 1;
            var response = first ? Redirect(302, ReflectionLocation)
                : Response(place == "storage-body" ? reflected : "package");
            if ((first && place == "original-header") || (!first && place == "storage-header"))
                response.Headers.TryAddWithoutValidation("ETag", "\"" + reflected + "\"");
            return response;
        });
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );

        int expectedCalls = place == "original-header" ? 1 : 2;
        Assert.AreEqual(expectedCalls, transport.Calls);
        Assert.AreEqual(expectedCalls, bounded.Requests);
        Assert.IsFalse(bounded.PackageReturned);
        Assert.AreEqual(0, bounded.PackageResponseIndex);
        Assert.HasCount(
            expectedCalls - 1, Directory.GetFiles(request.EvidencePath, "*-response.json")
        );
        Assert.IsEmpty(Directory.GetFiles(request.EvidencePath, "*-body.bin"));
        AssertEvidenceOmitsReflectionForms(request);
    }

    public static IEnumerable<(string resource, int status, string reflected)>
        UnexpectedReflectionCases()
    {
        foreach (var shape in new[] {
            ("service", 302), ("versions", 200), ("package", 307), ("package", 308),
            ("package", 200), ("storage", 301), ("storage", 302), ("storage", 200),
            ("storage", 403), ("package-multiple", 307),
        })
            foreach (string reflected in ReflectionForms)
                yield return (shape.Item1, shape.Item2, reflected);
    }

    [TestMethod]
    [DynamicData(nameof(UnexpectedReflectionCases))]
    public async Task UnexpectedLocationFormsStopBeforeEvidence(
        string resource, int status, string reflected
    )
    {
        ConsumerRequest request = Request();
        int calls = 0;
        var transport = new ResponseHandler(_ =>
        {
            if (++calls == 1 && resource == "storage") return Redirect(302, StorageUrl);
            var response = Redirect(
                status, resource == "package-multiple" ? StorageUrl : ReflectionLocation
            );
            if (resource == "package-multiple")
                response.Headers.TryAddWithoutValidation("Location", ReflectionLocation);
            response.Headers.TryAddWithoutValidation("X-GitHub-Request-Id", reflected);
            return response;
        });
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);
        string url = resource switch
        {
            "service" => ConsumerRequest.ServiceIndex,
            "versions" => VersionsUrl,
            _ => request.PackageUrl,
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(url));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(url));

        int expectedCalls = resource == "storage" ? 2 : 1;
        Assert.AreEqual(expectedCalls, transport.Calls);
        Assert.AreEqual(expectedCalls, bounded.Requests);
        Assert.AreEqual(resource == "storage" ? 8 : 0, bounded.ResponseBytes);
        Assert.IsFalse(bounded.PackageReturned);
        Assert.HasCount(
            expectedCalls - 1, Directory.GetFiles(request.EvidencePath, "*-response.json")
        );
        Assert.IsEmpty(Directory.GetFiles(request.EvidencePath, "*-body.bin"));
        AssertEvidenceOmitsCapabilities(request);
        AssertEvidenceOmitsReflectionForms(request);
    }

    [TestMethod]
    [DataRow("https://storage.example.invalid/")]
    [DataRow("https://storage.example.invalid/?")]
    [DataRow("https://storage.example.invalid/?/")]
    [DataRow("https://storage.example.invalid/?%2F")]
    [DataRow(StorageUrl)]
    public async Task BenignLocationExclusionsPreserveOpaqueTarget(string location)
    {
        byte[] original = "package / normal body"u8.ToArray();
        ConsumerRequest request = Request() with
        {
            PackageSha256 = ConsumerRequest.Sha256(original)
        };
        int calls = 0;
        var transport = new InspectingHandler((message, _) =>
        {
            if (++calls == 1) return Task.FromResult(Redirect(302, location));
            Assert.AreEqual(location, message.RequestUri!.OriginalString);
            Assert.AreEqual(location, message.RequestUri.AbsoluteUri);
            Assert.IsEmpty(message.Headers);
            var response = Response("package / normal body");
            response.Headers.TryAddWithoutValidation("ETag", "\"/\"");
            return Task.FromResult(response);
        });
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);

        using HttpResponseMessage response = await client.GetAsync(request.PackageUrl);

        CollectionAssert.AreEqual(original, await response.Content.ReadAsByteArrayAsync());
        Assert.AreEqual(2, transport.Calls);
        Assert.AreEqual(8 + original.Length, bounded.ResponseBytes);
        Assert.IsTrue(bounded.PackageReturned);
        Assert.AreEqual(2, bounded.PackageResponseIndex);
        Assert.HasCount(2, Directory.GetFiles(request.EvidencePath, "*-response.json"));
        Assert.HasCount(1, Directory.GetFiles(request.EvidencePath, "*-body.bin"));
    }

    private static void AssertEvidenceOmitsReflectionForms(ConsumerRequest request)
    {
        foreach (string path in Directory.GetFiles(request.EvidencePath))
        {
            string text = File.ReadAllText(path);
            foreach (string forbidden in ReflectionForms)
                Assert.DoesNotContain(forbidden, text);
            if (path.EndsWith("-response.json", StringComparison.Ordinal))
            {
                JsonObject headers = JsonNode.Parse(text)!["headers"]!.AsObject();
                foreach (var header in headers)
                    foreach (JsonNode? value in header.Value!.AsArray())
                        foreach (string forbidden in ReflectionForms)
                            Assert.DoesNotContain(forbidden, value!.GetValue<string>());
            }
        }
    }

    [TestMethod]
    public async Task PackageRedirectChargesRequestAllowanceBeforeStorageSend()
    {
        ConsumerRequest request = Request() with { MaximumRequests = 1 };
        var transport = new ResponseHandler(_ => Redirect(302, StorageUrl));
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );

        Assert.AreEqual(1, transport.Calls);
        Assert.AreEqual(8, bounded.ResponseBytes);
        Assert.HasCount(1, Directory.GetFiles(request.EvidencePath, "*-reserved.json"));
        Assert.HasCount(1, Directory.GetFiles(request.EvidencePath, "*-response.json"));
    }

    [TestMethod]
    [DataRow(8, 1, 8)]
    [DataRow(10, 2, 2)]
    public async Task PackageRedirectSharesOriginalByteAllowanceIncludingSentinel(
        int allowance, int expectedCalls, int finalRead
    )
    {
        ConsumerRequest request = Request() with { MaximumResponseBytes = allowance };
        using var first = new TrackingStream("redirect");
        using var second = new TrackingStream("package");
        int calls = 0;
        var transport = new ResponseHandler(_ =>
        {
            var response = ++calls == 1 ? Redirect(302, StorageUrl) : Response("ignored");
            response.Content = new StreamContent(calls == 1 ? first : second);
            return response;
        });
        var bounded = new BoundedHttpHandler(request, Token, transport);
        using var client = new HttpClient(bounded);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.GetAsync(request.PackageUrl)
        );

        Assert.AreEqual(expectedCalls, transport.Calls);
        Assert.AreEqual(finalRead, expectedCalls == 1 ? first.BytesRead : second.BytesRead);
        Assert.AreEqual(allowance, bounded.ResponseBytes);
        Assert.IsEmpty(Directory.GetFiles(request.EvidencePath, "*-body.bin"));
        Assert.IsFalse(bounded.PackageReturned);
    }

    [TestMethod]
    public async Task StorageTransportUsesOriginalDeadlineAndStopsWithoutRetry()
    {
        ConsumerRequest request = Request() with { TimeoutSeconds = 1 };
        var time = new DeadlineTimeProvider();
        var storageEntered = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var transportLifetime = new CancellationTokenSource();
        TimeSpan watchdog = TimeSpan.FromSeconds(10);
        int calls = 0;
        var transport = new InspectingHandler(async (_, cancellationToken) =>
        {
            if (++calls == 1)
            {
                time.Advance(TimeSpan.FromMilliseconds(700));
                return Redirect(302, StorageUrl);
            }
            storageEntered.TrySetResult(cancellationToken);
            using var pending = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, transportLifetime.Token
            );
            await Task.Delay(Timeout.InfiniteTimeSpan, pending.Token);
            throw new InvalidOperationException("Unreachable completion.");
        });
        var bounded = new BoundedHttpHandler(request, Token, transport, time);
        using var client = new HttpClient(bounded) { Timeout = Timeout.InfiniteTimeSpan };
        Task<HttpResponseMessage> operation = client.GetAsync(request.PackageUrl);

        try
        {
            CancellationToken storageToken = await storageEntered.Task.WaitAsync(watchdog);
            time.Advance(TimeSpan.FromMilliseconds(299));
            Assert.IsFalse(storageToken.IsCancellationRequested);

            time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.IsTrue(storageToken.IsCancellationRequested);
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => operation.WaitAsync(watchdog)
            );
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => client.GetAsync(request.PackageUrl).WaitAsync(watchdog)
            );
        }
        finally
        {
            transportLifetime.Cancel();
            client.CancelPendingRequests();
            try
            {
                using HttpResponseMessage response = await operation.WaitAsync(watchdog);
            }
            catch (OperationCanceledException)
            {
                // Observe the canceled test-owned operation before disposing its handler.
            }
        }

        Assert.AreEqual(2, transport.Calls);
        Assert.HasCount(2, Directory.GetFiles(request.EvidencePath, "*-reserved.json"));
        Assert.HasCount(1, Directory.GetFiles(request.EvidencePath, "*-response.json"));
        Assert.IsFalse(bounded.PackageReturned);
    }

    private static HttpResponseMessage Redirect(int status, string location)
    {
        var response = Response("redirect");
        response.StatusCode = (HttpStatusCode)status;
        response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    }

    private static JsonObject ReadEvidence(ConsumerRequest request, string name) =>
        JsonNode.Parse(File.ReadAllBytes(Path.Combine(request.EvidencePath, name)))!.AsObject();

    private static void AssertEvidenceOmitsCapabilities(ConsumerRequest request)
    {
        foreach (string path in Directory.GetFiles(request.EvidencePath))
        {
            string text = File.ReadAllText(path);
            Assert.DoesNotContain(Token, text);
            Assert.DoesNotContain(StorageUrl, text);
            Assert.DoesNotContain(Uri.UnescapeDataString(StorageUrl), text);
        }
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

    private sealed class TrackingStream(string body) : MemoryStream(Encoding.UTF8.GetBytes(body))
    {
        internal long BytesRead { get; private set; }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            int count = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += count;
            return count;
        }
    }

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

    private sealed class InspectingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response
    ) : HttpMessageHandler
    {
        internal int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage message, CancellationToken cancellationToken
        )
        {
            Calls++;
            return response(message, cancellationToken);
        }
    }
}

file sealed class DeadlineTimeProvider : TimeProvider
{
    private readonly List<DeadlineTimer> _timers = [];
    private TimeSpan _elapsed;

    public override ITimer CreateTimer(
        TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period
    )
    {
        var timer = new DeadlineTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    internal void Advance(TimeSpan elapsed)
    {
        _elapsed += elapsed;
        foreach (DeadlineTimer timer in _timers.ToArray()) timer.FireIfDue();
    }

    private sealed class DeadlineTimer(
        DeadlineTimeProvider owner, TimerCallback callback, object? state
    ) : ITimer
    {
        private TimeSpan? _dueAt;
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan)
                throw new NotSupportedException("This deadline test uses only one-shot timers.");
            if (_disposed) return false;
            _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._elapsed + dueTime;
            return true;
        }

        internal void FireIfDue()
        {
            if (_dueAt is not { } dueAt || dueAt > owner._elapsed) return;
            _dueAt = null;
            callback(state);
        }

        public void Dispose()
        {
            _disposed = true;
            _dueAt = null;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
