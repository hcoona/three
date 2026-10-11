using System.Net;
using System.Text;
using WorkflowDelivery.Ecosystems;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetConsumerReadHandlerTests(TestContext context)
{
    private const string Archive = "https://nuget.pkg.github.com/hcoona/flat/p/1.0.0/p.1.0.0.nupkg";
    private const string Credential = "controlled-sensitive-read-value";

    [TestMethod]
    [DataRow("https://storage.example.test/p?token=controlled-sensitive-read-value")]
    [DataRow("https://storage.example.test/p?token=%63ontrolled-sensitive-read-value")]
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
            response.Headers.Location = new Uri(location);
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
