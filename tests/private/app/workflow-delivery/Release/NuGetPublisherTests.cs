using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetPublisherTests(TestContext context)
{
    private const string Credential = "controlled-publish-secret";

    [TestMethod]
    public async Task OfficialPublisherSendsOriginalMultipartWithOriginCredentials()
    {
        using var fixture = new Fixture();
        byte[]? body = null;
        using var handler = new Handler(async (request, token) =>
        {
            Assert.AreEqual(HttpMethod.Put, request.Method);
            Assert.AreEqual(NuGetPublisher.GithubEndpoint, request.RequestUri!.AbsoluteUri);
            Assert.AreEqual("Basic", request.Headers.Authorization!.Scheme);
            Assert.AreEqual(Convert.ToBase64String(Encoding.UTF8.GetBytes("hcoona:" + Credential)),
                request.Headers.Authorization.Parameter);
            Assert.AreEqual(Credential, Assert.ContainsSingle(request.Headers.GetValues(
                "X-NuGet-ApiKey")));
            Assert.AreEqual("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
            body = await request.Content.ReadAsByteArrayAsync(token);
            return new(HttpStatusCode.Created) { Content = new ByteArrayContent([]) };
        });
        NuGetPushResult result = await NuGetPublisher.PushAsync(fixture.Prepared,
            fixture.Root, NuGetPublisher.GithubEndpoint, Credential, context.CancellationToken,
            handler);

        Assert.IsTrue(result.ReturnedSuccessfully);
        Assert.IsFalse(result.Cancelled);
        Assert.IsNull(result.FailureType);
        Assert.AreEqual(1, handler.Sends);
        Assert.IsNotNull(body);
        Assert.IsTrue(body.AsSpan().IndexOf(fixture.Original) >= 0);
        Assert.DoesNotContain(Credential, JsonSerializer.Serialize(result));
        CollectionAssert.AreEqual(fixture.Original, File.ReadAllBytes(fixture.Path));
    }

    [TestMethod]
    public async Task NuGetOrgPublisherSendsOnlyTheExchangedApiKey()
    {
        using var fixture = new Fixture("circular-list",
            "IO.Github.Hcoona.Collections.CircularList", "official", "nugetorg",
            "refs/heads/main");
        byte[]? body = null;
        using var handler = new Handler(async (request, token) =>
        {
            Assert.AreEqual(HttpMethod.Put, request.Method);
            Assert.AreEqual(NuGetPublisher.NuGetOrgEndpoint, request.RequestUri!.AbsoluteUri);
            Assert.IsNull(request.Headers.Authorization);
            Assert.AreEqual(Credential, Assert.ContainsSingle(request.Headers.GetValues(
                "X-NuGet-ApiKey")));
            body = await request.Content!.ReadAsByteArrayAsync(token);
            return new(HttpStatusCode.Created) { Content = new ByteArrayContent([]) };
        });
        NuGetPushResult result = await NuGetPublisher.PushAsync(fixture.Prepared,
            fixture.Root, NuGetPublisher.NuGetOrgEndpoint, Credential,
            context.CancellationToken, handler);

        Assert.IsTrue(result.ReturnedSuccessfully);
        Assert.AreEqual(1, handler.Sends);
        Assert.IsTrue(body!.AsSpan().IndexOf(fixture.Original) >= 0);
    }

    [TestMethod]
    [DataRow("circular-list", "IO.Github.Hcoona.Collections.CircularList", "official",
        "nugetorg", "refs/heads/development", NuGetPublisher.NuGetOrgEndpoint)]
    [DataRow("circular-list", "IO.Github.Hcoona.Collections.CircularList", "official",
        "nugetorg", "refs/heads/main", NuGetPublisher.GithubEndpoint)]
    [DataRow("circular-list", "IO.Github.Hcoona.Collections.CircularList", "buddy",
        "nugetorg", "refs/heads/main", NuGetPublisher.NuGetOrgEndpoint)]
    [DataRow("hcoona-release-smoke-github-packages", "Hcoona.ReleaseSmoke.GithubPackages",
        "official", "nugetorg", "refs/heads/main", NuGetPublisher.NuGetOrgEndpoint)]
    [DataRow("circular-list", "IO.Github.Hcoona.Pngcs", "buddy", "github-packages-nuget",
        "refs/heads/main", NuGetPublisher.GithubEndpoint)]
    public async Task PublisherRejectsMismatchedChannelRefEndpointOrIdentity(string unit,
        string package, string channel, string destination, string reference, string endpoint)
    {
        using var fixture = new Fixture(unit, package, channel, destination, reference);
        await AssertNoNativeSendAsync(fixture, fixture.Prepared, endpoint);
    }

    [TestMethod]
    public async Task NativeDuplicateRejectionPreservesFailureWithoutApplicationResend()
    {
        using var fixture = new Fixture();
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.Conflict)
        { Content = new ByteArrayContent([]) }));
        NuGetPushResult result = await NuGetPublisher.PushAsync(fixture.Prepared,
            fixture.Root, NuGetPublisher.GithubEndpoint, Credential, context.CancellationToken,
            handler);

        Assert.IsFalse(result.ReturnedSuccessfully);
        Assert.IsNotNull(result.FailureType);
        Assert.AreEqual(1, handler.Sends);
        Assert.DoesNotContain(Credential, JsonSerializer.Serialize(result));
    }

    // Client errors other than the throttling statuses end the invocation after one send.
    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest)]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.Forbidden)]
    [DataRow(HttpStatusCode.NotFound)]
    public async Task OfficialClientErrorsAreNotResent(HttpStatusCode status)
    {
        using var fixture = new Fixture();
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(
            status)
        { Content = new ByteArrayContent([]) }));
        NuGetPushResult result = await NuGetPublisher.PushAsync(fixture.Prepared,
            fixture.Root, NuGetPublisher.GithubEndpoint, Credential, context.CancellationToken,
            handler);

        Assert.IsFalse(result.ReturnedSuccessfully);
        Assert.IsNotNull(result.FailureType);
        Assert.AreEqual(1, handler.Sends);
    }

    [TestMethod]
    public async Task ParentCancellationReachesActualNativeSendAndIsAwaited()
    {
        using var fixture = new Fixture();
        using var parent = new CancellationTokenSource();
        bool finished = false;
        using var handler = new Handler(async (_, token) =>
        {
            parent.Cancel();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { finished = true; }
            return new(HttpStatusCode.Created);
        });
        NuGetPushResult result = await NuGetPublisher.PushAsync(fixture.Prepared,
            fixture.Root, NuGetPublisher.GithubEndpoint, Credential, parent.Token, handler);

        Assert.IsFalse(result.ReturnedSuccessfully);
        Assert.IsTrue(result.Cancelled);
        Assert.IsTrue(finished);
        Assert.AreEqual(1, handler.Sends);
    }

    // Every status the pinned client retries (5xx, 408 and, by default, 429) stays within
    // the same three-send bound.
    [TestMethod]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    [DataRow(HttpStatusCode.InternalServerError)]
    [DataRow(HttpStatusCode.RequestTimeout)]
    [DataRow(HttpStatusCode.TooManyRequests)]
    public async Task OfficialBoundedRetriesRemainInsideOneInvocation(HttpStatusCode status)
    {
        using var fixture = new Fixture();
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(
            status)
        { Content = new ByteArrayContent([]) }));
        NuGetPushResult result = await NuGetPublisher.PushAsync(fixture.Prepared,
            fixture.Root, NuGetPublisher.GithubEndpoint, Credential, context.CancellationToken,
            handler);

        Assert.IsFalse(result.ReturnedSuccessfully);
        Assert.AreEqual(3, handler.Sends);
        Assert.IsNotNull(result.FailureType);
        CollectionAssert.AreEqual(fixture.Original, File.ReadAllBytes(fixture.Path));
    }

    // The pinned client also retries transport failures and its own request timeout inside
    // the same bound, and the publisher returns the terminal failure as handled data. A real
    // SocketsHttpHandler wraps socket errors in HttpRequestException.
    [TestMethod]
    public Task OfficialHttpFailureRetriesInsideTheSameBound() =>
        AssertBoundedTransportFailureAsync(() => new HttpRequestException("HTTP failure"),
            nameof(HttpRequestException));

    [TestMethod]
    public Task OfficialIoFailureRetriesInsideTheSameBound() =>
        AssertBoundedTransportFailureAsync(() => new IOException("I/O failure"),
            nameof(IOException));

    [TestMethod]
    public Task OfficialRequestTimeoutRetriesInsideTheSameBound() =>
        AssertBoundedTransportFailureAsync(() => new TimeoutException("Request timed out"),
            nameof(TimeoutException));

    private async Task AssertBoundedTransportFailureAsync(Func<Exception> createException,
        string expectedType)
    {
        using var fixture = new Fixture();
        using var handler = new Handler((_, _) => throw createException());
        NuGetPushResult result = await NuGetPublisher.PushAsync(fixture.Prepared,
            fixture.Root, NuGetPublisher.GithubEndpoint, Credential, context.CancellationToken,
            handler);

        Assert.IsFalse(result.ReturnedSuccessfully);
        Assert.IsFalse(result.Cancelled);
        Assert.AreEqual(3, handler.Sends);
        Assert.AreEqual(expectedType, result.FailureType);
        CollectionAssert.AreEqual(fixture.Original, File.ReadAllBytes(fixture.Path));
    }

    [TestMethod]
    public async Task ParentCancellationReachesNativeResponseStreamAcquisition()
    {
        using var fixture = new Fixture();
        using var parent = new CancellationTokenSource();
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.Created)
        { Content = new CancellingContent(parent) }));
        NuGetPushResult result = await NuGetPublisher.PushAsync(fixture.Prepared,
            fixture.Root, NuGetPublisher.GithubEndpoint, Credential, parent.Token, handler);

        Assert.IsFalse(result.ReturnedSuccessfully);
        Assert.IsTrue(result.Cancelled);
        Assert.AreEqual(1, handler.Sends);
    }

    [TestMethod]
    public async Task RedirectCannotForwardPublisherCredentials()
    {
        using var fixture = new Fixture();
        using var handler = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect)
            { Content = new ByteArrayContent([]) };
            response.Headers.Location = new("https://objects.githubusercontent.com/package");
            return Task.FromResult(response);
        });
        NuGetPushResult result = await NuGetPublisher.PushAsync(fixture.Prepared,
            fixture.Root, NuGetPublisher.GithubEndpoint, Credential, context.CancellationToken,
            handler);

        Assert.IsFalse(result.ReturnedSuccessfully);
        Assert.AreEqual(1, handler.Sends);
    }

    [TestMethod]
    public async Task DryRunMakesNoNativeSend()
    {
        using var fixture = new Fixture();
        PreparedRelease prepared = fixture.Prepared;
        await AssertNoNativeSendAsync(fixture,
            prepared with { Subject = prepared.Subject with { DryRun = true } });
    }

    [TestMethod]
    public async Task UntrustedEndpointMakesNoNativeSend()
    {
        using var fixture = new Fixture();
        await AssertNoNativeSendAsync(fixture, fixture.Prepared, "https://example.com/");
    }

    [TestMethod]
    public async Task ChangedOriginalMakesNoNativeSend()
    {
        using var fixture = new Fixture();
        File.AppendAllText(fixture.Path, "changed");
        await AssertNoNativeSendAsync(fixture, fixture.Prepared);
    }

    private async Task AssertNoNativeSendAsync(Fixture fixture, PreparedRelease prepared,
        string endpoint = NuGetPublisher.GithubEndpoint)
    {
        using var handler = new Handler((_, _) =>
            throw new AssertFailedException("Rejected uploads must not send."));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NuGetPublisher.PushAsync(prepared, fixture.Root, endpoint, Credential,
                context.CancellationToken, handler));
        Assert.AreEqual(0, handler.Sends);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("nuget-publisher-")
            .FullName;
        internal string Path => System.IO.Path.Combine(Root, "original.nupkg");
        internal byte[] Original { get; }
        internal PreparedRelease Prepared { get; }
        internal Fixture() : this("hcoona-release-smoke-github-packages",
            "Hcoona.ReleaseSmoke.GithubPackages", "buddy", "github-packages-nuget",
            "refs/heads/development")
        {
        }
        internal Fixture(string unit, string package, string channel, string destination,
            string reference)
        {
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(zip.CreateEntry("package.nuspec").Open()))
                writer.Write($"<package><metadata><id>{package}</id>"
                    + "<version>1.0.0</version><authors>hcoona</authors>"
                    + "<description>Controlled native archive</description></metadata></package>");
            Original = bytes.ToArray();
            File.WriteAllBytes(Path, Original);
            Prepared = new(new(new string('a', 40), reference, unit,
                WorkflowDelivery.Ecosystems.ReleaseUnitCatalog.NuGet(unit).Declaration, channel,
                destination, false),
                [new("build", "package", "nuget-package", package,
                    "1.0.0", "original.nupkg", Original.Length,
                    Convert.ToHexStringLower(SHA256.HashData(Original)))], ["contents"],
                [new("contents", ReleaseCheckStatus.Passed)]);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class CancellingContent(CancellationTokenSource parent) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new InvalidOperationException("Expected native stream acquisition.");
        protected override Task<Stream> CreateContentReadStreamAsync()
            => CreateContentReadStreamAsync(CancellationToken.None);
        protected override async Task<Stream> CreateContentReadStreamAsync(
            CancellationToken cancellation)
        {
            parent.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            return Stream.Null;
        }
    }

    private sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> operation)
        : HttpMessageHandler
    {
        internal int Sends { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellation)
        {
            Sends++;
            return operation(request, cancellation);
        }
    }
}
