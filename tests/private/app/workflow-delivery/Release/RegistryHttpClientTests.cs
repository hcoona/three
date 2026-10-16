using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;

namespace WorkflowDelivery.Tests.Release;

// Exercises the configured platform handler over loopback sockets; the registry
// adapters rely on these redirect properties instead of their own redirect code.
[TestClass]
public sealed class RegistryHttpClientTests(TestContext context)
{
    [TestMethod]
    public async Task CrossHostRedirectDropsAuthorizationAndReturnsTerminalBytes()
    {
        await using var server = new LoopbackServer(tls: null, request =>
            request.Path == "/archive"
                ? Redirect($"http://localhost:{request.Port}/storage?signature=private")
                : Ok("original"));
        using HttpClient client = RegistryHttpClient.Create();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"http://127.0.0.1:{server.Port}/archive");
        request.Headers.Authorization = new("Bearer", "controlled-secret");

        using HttpResponseMessage response = await client.SendAsync(request,
            context.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("original", await response.Content.ReadAsStringAsync(
            context.CancellationToken));
        Assert.HasCount(2, server.Requests);
        Assert.AreEqual("Bearer controlled-secret", server.Requests[0].Authorization);
        Assert.StartsWith("localhost:", server.Requests[1].Host);
        Assert.IsNull(server.Requests[1].Authorization);
    }

    [TestMethod]
    public async Task RedirectBeyondTheLimitReturnsTheUnfollowedResponse()
    {
        await using var server = new LoopbackServer(tls: null,
            request => Redirect("/next" + request.Path.Length));
        using HttpClient client = RegistryHttpClient.Create();

        using HttpResponseMessage response = await client.GetAsync(
            $"http://127.0.0.1:{server.Port}/start", context.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.HasCount(RegistryHttpClient.MaxRedirects + 1, server.Requests);
    }

    [TestMethod]
    public async Task HttpsToHttpRedirectIsNotFollowed()
    {
        using X509Certificate2 certificate = Certificate();
        await using var plain = new LoopbackServer(tls: null, _ => Ok("downgraded"));
        await using var secure = new LoopbackServer(certificate,
            _ => Redirect($"http://127.0.0.1:{plain.Port}/storage"));
        using HttpClientHandler handler = RegistryHttpClient.CreateHandler();
        handler.ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
            presented?.Thumbprint == certificate.Thumbprint;
        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://localhost:{secure.Port}/archive");
        request.Headers.Authorization = new("Bearer", "controlled-secret");

        using HttpResponseMessage response = await client.SendAsync(request,
            context.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.HasCount(1, secure.Requests);
        Assert.IsEmpty(plain.Requests);
    }

    private static string Redirect(string location) =>
        "HTTP/1.1 302 Found\r\nLocation: " + location + "\r\nContent-Length: 0\r\n" +
        "Connection: close\r\n\r\n";

    private static string Ok(string body) => "HTTP/1.1 200 OK\r\nContent-Length: " +
        Encoding.ASCII.GetByteCount(body) + "\r\nConnection: close\r\n\r\n" + body;

    private static X509Certificate2 Certificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        using X509Certificate2 created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        // Windows SChannel needs a persisted-key form of an ephemeral certificate.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), null);
    }

    internal sealed record Request(string Path, string Host, string? Authorization, int Port);

    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Task loop;
        private readonly List<Request> requests = [];

        internal LoopbackServer(X509Certificate2? tls, Func<Request, string> respond)
        {
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            loop = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    using TcpClient socket = await listener.AcceptTcpClientAsync(stop.Token);
                    Stream stream = socket.GetStream();
                    if (tls is not null)
                    {
                        var secure = new SslStream(stream);
                        await secure.AuthenticateAsServerAsync(tls);
                        stream = secure;
                    }
                    await using (stream)
                    {
                        Request request = await ReadAsync(stream);
                        lock (requests) requests.Add(request);
                        byte[] reply = Encoding.ASCII.GetBytes(respond(request));
                        await stream.WriteAsync(reply, stop.Token);
                    }
                }
            });
        }

        internal int Port { get; }

        internal Request[] Requests
        {
            get { lock (requests) return [.. requests]; }
        }

        private async Task<Request> ReadAsync(Stream stream)
        {
            var header = new StringBuilder();
            byte[] one = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) &&
                await stream.ReadAsync(one, stop.Token) == 1)
                header.Append((char)one[0]);
            string[] lines = header.ToString().Split("\r\n");
            string? Field(string name) => lines.Skip(1).Where(line => line.StartsWith(
                    name + ":", StringComparison.OrdinalIgnoreCase))
                .Select(line => line[(name.Length + 1)..].Trim()).SingleOrDefault();
            return new(lines[0].Split(' ')[1], Field("Host") ?? "", Field("Authorization"),
                Port);
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            listener.Stop();
            try { await loop; }
            catch (Exception error) when (error is OperationCanceledException or
                SocketException or ObjectDisposedException) { }
            stop.Dispose();
        }
    }
}
