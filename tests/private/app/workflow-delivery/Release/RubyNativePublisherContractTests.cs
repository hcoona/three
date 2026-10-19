using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

// Runs the production RubyNativePublisher with the mise-locked Ruby and RubyGems against a
// loopback HTTPS gem host with fake keys. Only --host is retargeted to loopback; every
// proxy points at a closed port, so a non-loopback request fails instead of leaving the host.
// The Linux native-preparation job runs this class under mise exec --locked.
[TestClass]
[TestCategory("RubyNativeContract")]
public sealed class RubyNativePublisherContractTests(TestContext context)
{
    private const string Key = "fake-gem-host-key";

    // Buddy sends the GitHub token as a Bearer key to the owner path; Official sends the raw
    // key to the root. Each is exactly one POST of the original bytes, and the command exits 0.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow("buddy")]
    [DataRow("official")]
    public async Task PushSendsTheOriginalOnceWithTheSelectedKey(string channel)
    {
        using var push = await Push.RunAsync(channel, "accept", context.CancellationToken);

        AssertExited(push.Result, true);
        GemHost.Request posted = Assert.ContainsSingle(push.Requests);
        Assert.AreEqual("POST", posted.Method);
        Assert.AreEqual(channel == "buddy" ? "/hcoona/api/v1/gems" : "/api/v1/gems",
            posted.Path);
        Assert.AreEqual(channel == "buddy" ? "Bearer " + Key : Key, posted.Authorization);
        Assert.AreEqual("application/octet-stream", posted.ContentType);
        CollectionAssert.AreEqual(push.Original, posted.Body);
        Assert.StartsWith("127.0.0.1:", posted.Host);
    }

    // A returned failure status, the registry's existing-version rejection, a TLS connection
    // dropped without close_notify (an unclean EOF) and any redirect (same or other realm)
    // each end the command as a failure after exactly one POST. A TCP reset (RST) and a clean
    // TLS shutdown without a response are resent once by RubyGems' request layer, so they end
    // after two POSTs of the same original. No request reaches another realm.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow("unavailable", 1)]
    [DataRow("existing-version", 1)]
    [DataRow("unclean-close", 1)]
    [DataRow("clean-close", 2)]
    [DataRow("reset", 2)]
    [DataRow("same-realm-redirect", 1)]
    [DataRow("other-realm-redirect", 1)]
    public async Task PushFailuresStayWithinTheNativeResendBound(string answer, int posts)
    {
        using var push = await Push.RunAsync("official", answer, context.CancellationToken);

        AssertExited(push.Result, false);
        Assert.HasCount(posts, push.Requests);
        foreach (GemHost.Request posted in push.Requests)
        {
            Assert.AreEqual("/api/v1/gems", posted.Path);
            Assert.StartsWith("127.0.0.1:", posted.Host);
            CollectionAssert.AreEqual(push.Original, posted.Body);
        }
    }

    private static void AssertExited(NativeCommandResult result, bool succeeded)
    {
        Assert.AreEqual(NativeTermination.Exited, result.Termination, result.Error);
        Assert.AreEqual(succeeded, result.ExitCode == 0, result.Error);
    }

    // One publisher invocation with a freshly built original gem.
    private sealed class Push : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("ruby publish contract ")
            .FullName;
        internal NativeCommandResult Result { get; private set; } = null!;
        internal GemHost.Request[] Requests { get; private set; } = [];
        internal byte[] Original { get; private set; } = [];

        internal static async Task<Push> RunAsync(string channel, string answer,
            CancellationToken token)
        {
            var push = new Push();
            try { await push.PublishAsync(channel, answer, token); }
            catch { push.Dispose(); throw; }
            return push;
        }

        private async Task PublishAsync(string channel, string answer, CancellationToken token)
        {
            string ruby = await WhichAsync("ruby", token);
            string gem = await WhichAsync("gem", token);
            string source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            Directory.CreateDirectory(Path.Combine(source, "lib"));
            File.WriteAllText(Path.Combine(source, "lib", "probe.rb"), "PROBE = true\n");
            File.WriteAllText(Path.Combine(source, "probe.gemspec"), """
                Gem::Specification.new do |spec|
                  spec.name = "hcoona-release-smoke-ruby"
                  spec.version = "1.2.3"
                  spec.summary = "Loopback push contract"
                  spec.authors = ["Fixture"]
                  spec.license = "MIT"
                  spec.files = ["lib/probe.rb"]
                end
                """);
            string originals = Directory.CreateDirectory(Path.Combine(root, "originals"))
                .FullName;
            string home = Directory.CreateDirectory(Path.Combine(root, "build home")).FullName;
            NativeCommandResult built = await NativeProcess.ExecuteAsync(new(ruby, source,
                [gem, "build", "--norc", "probe.gemspec", "--output",
                    Path.Combine(originals, "original.gem")], 120,
                new Dictionary<string, string?> { ["HOME"] = home, ["GEM_HOME"] = home,
                    ["PATH"] = Path.GetDirectoryName(ruby) + ":/usr/bin:/bin" },
                InheritEnvironment: false, CloseStandardInput: true), token);
            AssertExited(built, true);
            string path = Path.Combine(originals, "original.gem");
            Original = File.ReadAllBytes(path);

            using var authority = new LoopbackAuthority(root);
            await using var host = new GemHost(authority.Leaf, answer);
            bool buddy = channel == "buddy";
            var prepared = new PreparedRelease(new(new string('a', 40), buddy
                ? "refs/heads/development" : ReleasePolicy.OfficialRef,
                "hcoona-release-smoke-ruby", "unit.yml", channel,
                buddy ? "github-packages-ruby" : "rubygems", false),
                [new("gem-build", "selected-gem", "ruby-gem", "hcoona-release-smoke-ruby",
                    "1.2.3", "original.gem", Original.Length,
                    Convert.ToHexStringLower(SHA256.HashData(Original)))],
                ["qualified"], [new("qualified", ReleaseCheckStatus.Passed)]);
            var preparation = new RubyPreparationResult(prepared, new(StringComparer.Ordinal),
                [], []);
            string scratch = Directory.CreateDirectory(Path.Combine(root, "publisher"))
                .FullName;
            var platform = new Dictionary<string, string?>
                { [buddy ? "GITHUB_TOKEN" : "GEM_HOST_API_KEY"] = Key };
            string production = buddy ? "https://rubygems.pkg.github.com/hcoona"
                : "https://rubygems.org";
            string loopback = "https://127.0.0.1:" + host.Port + (buddy ? "/hcoona" : "");

            RubyPublishResult result = await RubyNativePublisher.PublishAsync(preparation,
                originals, new(ruby, gem, scratch), platform, (command, cancellation) =>
                {
                    // The production command, retargeted to the loopback host only.
                    int index = Array.IndexOf(command.Arguments, "--host") + 1;
                    Assert.AreEqual(production, command.Arguments[index]);
                    string[] arguments = [.. command.Arguments];
                    arguments[index] = loopback;
                    var environment = new Dictionary<string, string?>(command.Environment!,
                        StringComparer.Ordinal)
                    {
                        ["SSL_CERT_FILE"] = authority.Bundle,
                        ["HTTPS_PROXY"] = "http://127.0.0.1:9",
                        ["HTTP_PROXY"] = "http://127.0.0.1:9",
                        ["https_proxy"] = "http://127.0.0.1:9",
                        ["http_proxy"] = "http://127.0.0.1:9",
                        ["NO_PROXY"] = "127.0.0.1,localhost",
                        ["no_proxy"] = "127.0.0.1,localhost",
                    };
                    return NativeProcess.ExecuteAsync(command with
                    {
                        Arguments = arguments,
                        Environment = environment,
                        DeadlineSeconds = 60,
                    }, cancellation);
                }, token);
            Result = result.Upload.Command;
            Requests = host.Requests;
        }

        private static async Task<string> WhichAsync(string tool, CancellationToken token)
        {
            string path = (await NativeProcess.RunAsync("mise",
                RepositoryToolSelection.SourceRoot, ["which", tool], token)).Trim();
            Assert.IsTrue(Path.IsPathFullyQualified(path) && File.Exists(path), tool);
            return path;
        }

        public void Dispose() => ScratchRoots.TryDelete(root);
    }

    // A minimal HTTPS/1.1 gem host that records every request and answers the row's behavior.
    private sealed class GemHost : IAsyncDisposable
    {
        internal sealed record Request(string Method, string Path, string? Authorization,
            string? ContentType, string Host, byte[] Body);

        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly List<Request> requests = [];
        private readonly List<Task> connections = [];
        private readonly Task loop;
        private readonly string answer;

        internal GemHost(X509Certificate2 certificate, string answer)
        {
            this.answer = answer;
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            loop = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    TcpClient socket = await listener.AcceptTcpClientAsync(stop.Token);
                    lock (connections) connections.Add(ServeAsync(socket, certificate));
                }
            });
        }

        internal int Port { get; }

        internal Request[] Requests
        {
            get { lock (requests) return [.. requests]; }
        }

        private async Task ServeAsync(TcpClient socket, X509Certificate2 certificate)
        {
            using (socket)
            await using (var secure = new SslStream(socket.GetStream()))
            {
                try
                {
                    await secure.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = certificate,
                        ApplicationProtocols = [SslApplicationProtocol.Http11],
                    }, stop.Token);
                    while (await ReadAsync(secure) is { } request)
                    {
                        lock (requests) requests.Add(request);
                        (string status, string body, string headers) = answer switch
                        {
                            "accept" => ("200 OK", "Successfully registered gem", ""),
                            "unavailable" => ("503 Service Unavailable", "unavailable", ""),
                            "existing-version" => ("409 Conflict",
                                "Repushing of gem versions is not allowed.", ""),
                            "reset" => ("reset", "", ""),
                            "unclean-close" => ("unclean-close", "", ""),
                            "clean-close" => ("clean-close", "", ""),
                            "same-realm-redirect" => ("307 Temporary Redirect", "",
                                "Location: https://127.0.0.1:" + Port + "/moved" +
                                request.Path + "\r\n"),
                            "other-realm-redirect" => ("307 Temporary Redirect", "",
                                "Location: https://localhost:" + Port + "/moved" +
                                request.Path + "\r\n"),
                            _ => throw new InvalidOperationException(answer),
                        };
                        if (status == "reset")
                        {
                            // An abortive close: the client receives a TCP reset (RST).
                            socket.Client.LingerState = new LingerOption(true, 0);
                            socket.Client.Close(0);
                            return;
                        }
                        // A clean TLS shutdown (close_notify) without a response.
                        if (status == "clean-close")
                        {
                            await secure.ShutdownAsync();
                            return;
                        }
                        // The socket closes without close_notify: an unclean TLS EOF.
                        if (status == "unclean-close") return;
                        byte[] content = Encoding.UTF8.GetBytes(body);
                        await secure.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 " + status +
                            "\r\n" + headers + "Content-Length: " + content.Length +
                            "\r\n\r\n"), stop.Token);
                        await secure.WriteAsync(content, stop.Token);
                    }
                }
                catch (Exception error) when (error is IOException or
                    OperationCanceledException or System.Security.Authentication
                        .AuthenticationException) { }
            }
        }

        private async Task<Request?> ReadAsync(Stream stream)
        {
            var header = new StringBuilder();
            byte[] one = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await stream.ReadAsync(one, stop.Token) != 1) return null;
                header.Append((char)one[0]);
            }
            string[] lines = header.ToString().Split("\r\n");
            string? Field(string name) => lines.Skip(1).Where(line => line.StartsWith(
                    name + ":", StringComparison.OrdinalIgnoreCase))
                .Select(line => line[(name.Length + 1)..].Trim()).SingleOrDefault();
            string[] start = lines[0].Split(' ');
            byte[] body = new byte[int.Parse(Field("Content-Length") ?? "0",
                System.Globalization.CultureInfo.InvariantCulture)];
            await stream.ReadExactlyAsync(body, stop.Token);
            return new(start[0], start[1], Field("Authorization"), Field("Content-Type"),
                Field("Host") ?? "", body);
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            listener.Stop();
            try { await loop; }
            catch (Exception error) when (error is OperationCanceledException or
                SocketException or ObjectDisposedException) { }
            Task[] pending;
            lock (connections) pending = [.. connections];
            await Task.WhenAll(pending);
            stop.Dispose();
        }
    }
}
