using System.Net;
using System.Text;
using WorkflowDelivery.Ecosystems;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetConsumerReadHandlerTests(TestContext context)
{
    private const string Archive = "https://nuget.pkg.github.com/hcoona/flat/p/1.0.0/p.1.0.0.nupkg";
    private const string Credential = "controlled-sensitive-read-value";
    private static readonly byte[] Original = [1, 2, 3];

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task CompletionCancelsAndJoinsReadsBeforeResourceDisposal()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unwinding = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new NuGetConsumerReadHandler(Credential, new Controlled(async
            (_, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally
            {
                unwinding.TrySetResult();
                await release.Task.WaitAsync(context.CancellationToken);
                stopped.TrySetResult();
            }
            return new(HttpStatusCode.OK);
        }), context.CancellationToken);
        using var client = new HttpClient(handler);
        Task<HttpResponseMessage> read = client.GetAsync(Archive, context.CancellationToken);
        Task? completion = null;
        try
        {
            await entered.Task.WaitAsync(context.CancellationToken);
            completion = handler.CompleteAsync();
            await unwinding.Task.WaitAsync(context.CancellationToken);
            Assert.IsFalse(completion.IsCompleted);
            Assert.IsFalse(stopped.Task.IsCompleted);
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => client.GetAsync(
                Archive, context.CancellationToken));
            Assert.AreEqual(1, handler.Reads);
        }
        finally
        {
            release.TrySetResult();
            await Assert.ThrowsAsync<OperationCanceledException>(() => read);
            if (completion is not null) await completion.WaitAsync(context.CancellationToken);
        }
        Assert.IsTrue(stopped.Task.IsCompletedSuccessfully);
    }

    [TestMethod]
    [DataRow(200)]
    [DataRow(201)]
    [DataRow(206)]
    public async Task DirectArchiveRequires200BeforeBodyConsumption(int status)
    {
        int sends = 0;
        using var body = new Tracked(Original);
        using var handler = new NuGetConsumerReadHandler(Credential, new Controlled((request, _) =>
        {
            sends++;
            Assert.AreEqual(Archive, request.RequestUri!.AbsoluteUri);
            Assert.AreEqual("Basic", request.Headers.Authorization!.Scheme);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
                { Content = new StreamContent(body) });
        }), context.CancellationToken);
        using var client = new HttpClient(handler);
        if (status == 200)
        {
            using HttpResponseMessage result = await client.GetAsync(Archive,
                context.CancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
            CollectionAssert.AreEqual(Original,
                await result.Content.ReadAsByteArrayAsync(context.CancellationToken));
            Assert.IsGreaterThan(0, body.Reads);
        }
        else
        {
            InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => client.GetAsync(Archive, context.CancellationToken));
            Assert.Contains("direct archive did not return 200", failure.Message);
            Assert.AreEqual(0, body.Reads);
        }
        Assert.AreEqual(1, sends);
        Assert.AreEqual(1, handler.Reads);
        Assert.IsTrue(body.Disposed);
    }

    [TestMethod]
    [DataRow("https://storage.example.test/p?token=controlled-sensitive-read-value")]
    [DataRow("https://storage.example.test/p?token=%63ontrolled-sensitive-read-value")]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("https://storage.example.test\\other/p")]
    [DataRow("https://storage%2eexample.test/p")]
    [DataRow("https://2130706433/p")]
    [DataRow("https://0x7f.0.0.1/p")]
    [DataRow("https://storage.example.test:/p")]
    [DataRow("http://storage.example.test/p")]
    [DataRow("https://storage.example.test:444/p")]
    [DataRow("https://user@storage.example.test/p")]
    [DataRow("https://storage.example.test/p#fragment")]
    public async Task InvalidArchiveRedirectCannotSendReadCapability(string location)
    {
        int sends = 0;
        using var handler = new NuGetConsumerReadHandler(Credential, new Controlled((_, _) =>
        {
            sends++;
            var response = new HttpResponseMessage(HttpStatusCode.Found)
                { Content = new ByteArrayContent([]) };
            response.Headers.TryAddWithoutValidation("Location", location);
            return Task.FromResult(response);
        }), context.CancellationToken);
        using var client = new HttpClient(handler);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(Archive,
            context.CancellationToken));
        Assert.AreEqual(1, sends);
    }

    [TestMethod]
    [DataRow(200)]
    [DataRow(201)]
    [DataRow(302)]
    public async Task SignedRawRedirectPreservesRequestAndRequiresStorage200(int status)
    {
        const string location = "https://storage.example.test/a%2fb/../p?signature=a%2fb+%25";
        int sends = 0;
        using var handler = new NuGetConsumerReadHandler(Credential, new Controlled((request, _) =>
        {
            sends++;
            if (sends == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found)
                    { Content = new ByteArrayContent([]) };
                redirect.Headers.TryAddWithoutValidation("Location", location);
                return Task.FromResult(redirect);
            }
            Assert.IsNull(request.Headers.Authorization);
            Assert.AreEqual(location, request.RequestUri!.OriginalString);
            Assert.AreEqual(location[location.IndexOf('/', 8)..], request.RequestUri.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
                { Content = new ByteArrayContent([1, 2, 3]) });
        }), context.CancellationToken);
        using var client = new HttpClient(handler);
        if (status == 200)
        {
            using HttpResponseMessage result = await client.GetAsync(Archive,
                context.CancellationToken);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 },
                await result.Content.ReadAsByteArrayAsync(context.CancellationToken));
        }
        else await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(Archive,
            context.CancellationToken));
        Assert.AreEqual(2, sends);
    }

    [TestMethod]
    public async Task MultipleRawLocationsStopBeforeStorageSend()
    {
        int sends = 0;
        using var handler = new NuGetConsumerReadHandler(Credential, new Controlled((_, _) =>
        {
            sends++;
            var response = new HttpResponseMessage(HttpStatusCode.Found)
                { Content = new ByteArrayContent([]) };
            response.Headers.TryAddWithoutValidation("Location", "https://storage.example.test/a");
            response.Headers.TryAddWithoutValidation("Location", "https://storage.example.test/b");
            return Task.FromResult(response);
        }), context.CancellationToken);
        using var client = new HttpClient(handler);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(Archive,
            context.CancellationToken));
        Assert.AreEqual(1, sends);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("basic")]
    [DataRow("encoded-basic")]
    [DataRow("unexpected-final")]
    public async Task RawLocationPresenceAndBasicReflectionStopBeforeAnotherSend(string form)
    {
        int sends = 0;
        string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("hcoona:" + Credential));
        using var handler = new NuGetConsumerReadHandler(Credential, new Controlled((_, _) =>
        {
            sends++;
            var response = new HttpResponseMessage(form == "unexpected-final"
                ? HttpStatusCode.OK : HttpStatusCode.Found)
                { Content = new ByteArrayContent([]) };
            if (form != "missing")
            {
                string value = form == "encoded-basic"
                    ? "%" + ((int)basic[0]).ToString("X2") + basic[1..] : basic;
                response.Headers.TryAddWithoutValidation("Location",
                    "https://storage.example.test/p?capability=" + value);
            }
            return Task.FromResult(response);
        }), context.CancellationToken);
        using var client = new HttpClient(handler);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetAsync(Archive,
            context.CancellationToken));
        Assert.AreEqual(1, sends);
    }

    [TestMethod]
    public async Task ParentCancellationStopsAndAwaitsActualBodyConsumption()
    {
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new NuGetConsumerReadHandler(Credential,
            new Controlled((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new Blocking(entered, stopped)) })), parent.Token);
        using var client = new HttpClient(handler);
        Task<HttpResponseMessage> operation = client.GetAsync(Archive, context.CancellationToken);
        await entered.Task.WaitAsync(context.CancellationToken);
        await parent.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
        Assert.IsTrue(stopped.Task.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task MetadataBodyLimitBlocksNativeConsumption()
    {
        using var handler = new NuGetConsumerReadHandler(Credential,
            new Controlled((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[2 * 1024 * 1024 + 1]) })),
            context.CancellationToken);
        using var client = new HttpClient(handler);
        await Assert.ThrowsExactlyAsync<IOException>(() => client.GetAsync(
            DotNetRemoteConsumer.ServiceIndex, context.CancellationToken));
    }

    private sealed class Tracked(byte[] bytes) : MemoryStream(bytes)
    {
        internal int Reads { get; private set; }
        internal bool Disposed { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken token = default)
        {
            Reads++;
            return base.ReadAsync(buffer, token);
        }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class Controlled(Func<HttpRequestMessage, CancellationToken,
        Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken token) => send(request, token);
    }

    private sealed class Blocking(TaskCompletionSource entered, TaskCompletionSource stopped)
        : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken token = default)
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); return 0; }
            finally { stopped.TrySetResult(); }
        }
    }
}
