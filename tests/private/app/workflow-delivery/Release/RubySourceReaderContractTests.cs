using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.Release;

// Runs the production RubySourceReader and the real source.rb with the mise-locked Ruby and
// RubyGems against a loopback HTTPS gem source that serves real index, specification and gem
// files: gems built by the locked gem command, indexes written by RubyGems' own Marshal and
// gzip. The only seam (ruby-source/loopback_source.rb) moves the selected host to loopback;
// every RubyGems method runs unchanged. Every proxy points at a closed port.
[TestClass]
[TestCategory("RubyNativeContract")]
public sealed class RubySourceReaderContractTests(TestContext context)
{
    private const string Name = "hcoona-release-smoke-ruby";
    private const string Official = "https://rubygems.org";
    private const string Buddy = "https://rubygems.pkg.github.com/hcoona";
    private static readonly RubyReadCredential Credential = new("read user:@/+?%&=",
        "synthetic token");
    private static string Seams => Path.Combine(RepositoryToolSelection.SourceRoot,
        "tests/private/app/workflow-delivery/Release/ruby-source");

    // Both inventories, the quick specification and the original are read in that order; the
    // answer is the downloaded original with its size and digest. Buddy sends the URI
    // credential as Basic authentication on every request; Official sends none.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow("official", "1.2.3")]
    [DataRow("official", "1.2.4.pre.beta.7")]
    [DataRow("buddy", "1.2.3")]
    public async Task SourceReadReturnsTheServedOriginal(string channel, string version)
    {
        using var source = await Source.CreateAsync(context.CancellationToken);
        string host = channel == "buddy" ? Buddy : Official;
        string prefix = channel == "buddy" ? "/hcoona" : "";

        (RubySourceReadResult result, string download) = await source.ReadAsync(host, version,
            channel == "buddy" ? Credential : null, null, context.CancellationToken);

        Assert.AreEqual(RubySourceState.Present, result.State, string.Join("; ", result.Errors));
        Assert.AreEqual(NativeTermination.Exited, result.Command!.Termination);
        Assert.AreEqual("", result.Command.Stdout + result.Command.Stderr);
        byte[] original = source.Original(version);
        Assert.AreEqual(new RubySourceOriginal(Path.Combine(download, "cache",
            $"{Name}-{version}.gem"), Name, version, "ruby", original.Length,
            Convert.ToHexStringLower(SHA256.HashData(original))), result.Original);
        CollectionAssert.AreEqual(new[] { prefix + "/specs.4.8.gz",
            prefix + "/prerelease_specs.4.8.gz",
            prefix + $"/quick/Marshal.4.8/{Name}-{version}.gemspec.rz",
            $"/gems/{Name}-{version}.gem" }, source.Paths);
        string? expected = channel == "buddy" ? "Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes(Credential.User + ":" + Credential.Token)) : null;
        Assert.IsTrue(source.Requests.All(request => request.Authorization == expected),
            string.Join(", ", source.Requests.Select(request => request.Authorization)));
    }

    // A version in neither inventory is absent after exactly the two inventory reads.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task SourceReadReportsAnUnlistedVersionAbsent()
    {
        using var source = await Source.CreateAsync(context.CancellationToken);

        (RubySourceReadResult result, _) = await source.ReadAsync(Official, "9.9.9", null,
            null, context.CancellationToken);

        Assert.AreEqual(RubySourceState.Absent, result.State);
        Assert.AreEqual(NativeTermination.Exited, result.Command!.Termination);
        Assert.IsNull(result.Original);
        CollectionAssert.AreEqual(Inventories, source.Paths);
    }

    // Any failed read is unknown, never absent or present: either inventory missing or
    // failing, the specification failing, or the original missing.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow("/specs.4.8.gz", 404, 1)]
    [DataRow("/specs.4.8.gz", 500, 1)]
    [DataRow("/prerelease_specs.4.8.gz", 404, 2)]
    [DataRow("/prerelease_specs.4.8.gz", 500, 2)]
    [DataRow("/quick/Marshal.4.8/hcoona-release-smoke-ruby-1.2.3.gemspec.rz", 500, 3)]
    [DataRow("/gems/hcoona-release-smoke-ruby-1.2.3.gem", 404, 4)]
    public async Task SourceReadFailureIsUnknown(string path, int status, int reads)
    {
        using var source = await Source.CreateAsync(context.CancellationToken);
        source.Failures[path] = status;

        (RubySourceReadResult result, _) = await source.ReadAsync(Official, "1.2.3", null,
            null, context.CancellationToken);

        Assert.AreEqual(RubySourceState.Unknown, result.State);
        Assert.AreEqual(NativeTermination.Exited, result.Command!.Termination);
        Assert.AreNotEqual(0, result.Command.ExitCode);
        Assert.IsNull(result.Original);
        // The read stops at the failed request.
        Assert.HasCount(reads, source.Paths);
        Assert.AreEqual(path, source.Paths.Last());
    }

    // RubyGems reuses an original already in its download cache without fetching it, so a
    // stale file there would be answered as present. The reader therefore requires fresh
    // scratch: it refuses a used one, and the native fact that makes this necessary holds.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task CachedOriginalIsReusedSoTheReaderRequiresFreshScratch()
    {
        using var source = await Source.CreateAsync(context.CancellationToken);
        byte[] stale = Encoding.UTF8.GetBytes("stale cached original");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => source.ReadAsync(Official,
            "1.2.3", null, stale, context.CancellationToken));
        Assert.IsEmpty(source.Paths);

        (string answer, string download) = await source.ReadNativeAsync(Official, "1.2.3",
            stale, context.CancellationToken);
        Assert.Contains("\"sha256\":\"" + Convert.ToHexStringLower(SHA256.HashData(stale)) +
            "\"", answer);
        Assert.DoesNotContain("/gems/" + Name + "-1.2.3.gem", source.Paths);
        Assert.IsTrue(File.Exists(Path.Combine(download, "cache", Name + "-1.2.3.gem")));
    }

    private static readonly string[] Inventories = ["/specs.4.8.gz",
        "/prerelease_specs.4.8.gz"];

    // A loopback gem source with two built versions and real indexes.
    private sealed class Source : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("ruby source contract ")
            .FullName;
        private readonly LoopbackAuthority authority;
        private readonly FileHost host;
        private string ruby = "";
        internal Dictionary<string, int> Failures => host.Failures;
        internal FileHost.Request[] Requests => host.Requests;
        internal string[] Paths => host.Requests.Select(request => request.Path).ToArray();

        private Source()
        {
            authority = new LoopbackAuthority(root);
            host = new FileHost(authority.Leaf, Path.Combine(root, "served"));
        }

        internal static async Task<Source> CreateAsync(CancellationToken token)
        {
            var source = new Source();
            try { await source.PrepareAsync(token); }
            catch { source.Dispose(); throw; }
            return source;
        }

        internal byte[] Original(string version) => File.ReadAllBytes(Path.Combine(root,
            "served", "gems", $"{Name}-{version}.gem"));

        private async Task PrepareAsync(CancellationToken token)
        {
            ruby = await WhichAsync("ruby", token);
            string gem = await WhichAsync("gem", token);
            string served = Directory.CreateDirectory(Path.Combine(root, "served", "gems"))
                .FullName;
            foreach (string version in new[] { "1.2.3", "1.2.4.pre.beta.7" })
            {
                string source = Directory.CreateDirectory(Path.Combine(root, "source-" +
                    version, "lib")).Parent!.FullName;
                File.WriteAllText(Path.Combine(source, "lib", "probe.rb"), "PROBE = true\n");
                File.WriteAllText(Path.Combine(source, "probe.gemspec"), $$"""
                    Gem::Specification.new do |spec|
                      spec.name = "{{Name}}"
                      spec.version = "{{version}}"
                      spec.summary = "Loopback source contract"
                      spec.authors = ["Fixture"]
                      spec.license = "MIT"
                      spec.files = ["lib/probe.rb"]
                    end
                    """);
                Exited(await NativeProcess.ExecuteAsync(new(ruby, source, [gem, "build",
                    "--norc", "probe.gemspec", "--output", Path.Combine(served,
                        $"{Name}-{version}.gem")], 120, Isolated("build-" + version),
                    InheritEnvironment: false, CloseStandardInput: true), token));
            }
            Exited(await NativeProcess.ExecuteAsync(new(ruby, root, [Path.Combine(Seams,
                "index.rb"), Path.Combine(root, "served")], 120, Isolated("index"),
                InheritEnvironment: false, CloseStandardInput: true), token));
        }

        // The production reader with one seam: the loopback host route and trust settings.
        internal async Task<(RubySourceReadResult Result, string Download)> ReadAsync(
            string selected, string version, RubyReadCredential? credential, byte[]? cached,
            CancellationToken token)
        {
            string scratch = Directory.CreateDirectory(Path.Combine(root, "read-" +
                Guid.NewGuid().ToString("N"))).FullName;
            if (cached is not null)
            {
                string cache = Directory.CreateDirectory(Path.Combine(scratch, "download",
                    "cache")).FullName;
                File.WriteAllBytes(Path.Combine(cache, $"{Name}-{version}.gem"), cached);
            }
            RubySourceReadResult result = await RubySourceReader.ReadAsync(selected, version,
                new(ruby, Path.Combine(RepositoryToolSelection.SourceRoot,
                    "src/private/app/workflow-delivery/Native/Ruby/source.rb"), scratch),
                credential, (command, cancellation) => NativeProcess.ExecuteAsync(command with
                {
                    Arguments = ["-r", Path.Combine(Seams, "loopback_source.rb"),
                        .. command.Arguments],
                    Environment = Loopback(command.Environment!),
                    DeadlineSeconds = 60,
                }, cancellation), token);
            return (result, Path.Combine(scratch, "download"));
        }

        // The native helper alone on a pre-populated download directory.
        internal async Task<(string Answer, string Download)> ReadNativeAsync(string selected,
            string version, byte[] cached, CancellationToken token)
        {
            string scratch = Directory.CreateDirectory(Path.Combine(root, "native-" +
                Guid.NewGuid().ToString("N"))).FullName;
            string download = Directory.CreateDirectory(Path.Combine(scratch, "download"))
                .FullName;
            File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(download,
                "cache")).FullName, $"{Name}-{version}.gem"), cached);
            string request = Path.Combine(scratch, "request.json");
            File.WriteAllText(request, System.Text.Json.JsonSerializer.Serialize(new Dictionary<
                string, string> { ["host"] = selected, ["name"] = Name, ["version"] = version,
                    ["directory"] = download }));
            var environment = Isolated("native-" + Path.GetFileName(scratch));
            NativeCommandResult result = await NativeProcess.ExecuteAsync(new(ruby, scratch,
                ["-r", Path.Combine(Seams, "loopback_source.rb"), Path.Combine(
                    RepositoryToolSelection.SourceRoot,
                    "src/private/app/workflow-delivery/Native/Ruby/source.rb"), request], 60,
                Loopback(environment), InheritEnvironment: false, CloseStandardInput: true),
                token);
            Exited(result);
            return (result.Stdout, download);
        }

        private Dictionary<string, string?> Isolated(string name)
        {
            string home = Directory.CreateDirectory(Path.Combine(root, "home-" + name)).FullName;
            return new(StringComparer.Ordinal)
            {
                ["PATH"] = Path.GetDirectoryName(ruby) + ":/usr/bin:/bin",
                ["HOME"] = home,
                ["GEM_HOME"] = home,
                ["GEM_PATH"] = home,
                ["GEM_SPEC_CACHE"] = Path.Combine(home, "specs"),
                ["LANG"] = "C.UTF-8",
            };
        }

        private Dictionary<string, string?> Loopback(IReadOnlyDictionary<string, string?> values)
            => new(values, StringComparer.Ordinal)
            {
                ["LOOPBACK_GEM_SOURCE_PORT"] = host.Port.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ["SSL_CERT_FILE"] = authority.Bundle,
                ["HTTPS_PROXY"] = "http://127.0.0.1:9",
                ["HTTP_PROXY"] = "http://127.0.0.1:9",
                ["https_proxy"] = "http://127.0.0.1:9",
                ["http_proxy"] = "http://127.0.0.1:9",
                ["NO_PROXY"] = "127.0.0.1,localhost",
                ["no_proxy"] = "127.0.0.1,localhost",
            };

        private static void Exited(NativeCommandResult result)
        {
            Assert.AreEqual(NativeTermination.Exited, result.Termination, result.Stderr);
            Assert.AreEqual(0, result.ExitCode, result.Stderr);
        }

        private static async Task<string> WhichAsync(string tool, CancellationToken token)
        {
            string path = (await NativeProcess.RunAsync("mise",
                RepositoryToolSelection.SourceRoot, ["which", tool], token)).Trim();
            Assert.IsTrue(Path.IsPathFullyQualified(path) && File.Exists(path), tool);
            return path;
        }

        public void Dispose()
        {
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            authority.Dispose();
            ScratchRoots.TryDelete(root);
        }
    }

    // A minimal HTTPS/1.1 static file host. A path under an owner prefix (/hcoona/...) serves
    // the same files as the root; configured failures answer their status instead.
    private sealed class FileHost : IAsyncDisposable
    {
        internal sealed record Request(string Method, string Path, string? Authorization);

        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly List<Request> requests = [];
        private readonly List<Task> connections = [];
        private readonly Task loop;
        private readonly string directory;

        internal FileHost(X509Certificate2 certificate, string directory)
        {
            this.directory = directory;
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
        internal Dictionary<string, int> Failures { get; } = new(StringComparer.Ordinal);

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
                        string relative = request.Path.StartsWith("/hcoona/",
                            StringComparison.Ordinal) ? request.Path["/hcoona".Length..]
                            : request.Path;
                        string file = Path.GetFullPath(Path.Combine(directory,
                            relative.TrimStart('/')));
                        (int status, byte[] body) = Failures.TryGetValue(relative, out int code)
                            ? (code, Encoding.UTF8.GetBytes("controlled failure"))
                            : file.StartsWith(directory + Path.DirectorySeparatorChar,
                                StringComparison.Ordinal) && File.Exists(file)
                                ? (200, await File.ReadAllBytesAsync(file, stop.Token))
                                : (404, Encoding.UTF8.GetBytes("not found"));
                        string reason = status switch { 200 => "OK", 404 => "Not Found",
                            _ => "Internal Server Error" };
                        await secure.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 " + status +
                            " " + reason + "\r\nContent-Length: " + body.Length + "\r\n\r\n"),
                            stop.Token);
                        await secure.WriteAsync(body, stop.Token);
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
            string? authorization = lines.Skip(1).Where(line => line.StartsWith(
                    "Authorization:", StringComparison.OrdinalIgnoreCase))
                .Select(line => line["Authorization:".Length..].Trim()).SingleOrDefault();
            string[] start = lines[0].Split(' ');
            return new(start[0], start[1], authorization);
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
