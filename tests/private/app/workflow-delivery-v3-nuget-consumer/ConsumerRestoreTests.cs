using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkflowDeliveryV3NuGetConsumer.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ConsumerRestoreTests
{
    private const string Token = "synthetic-consumer-read-token-12345";
    private const string BaseAddress = "https://nuget.pkg.github.com/hcoona/download/";
    private static readonly byte[] ServiceIndex = Encoding.UTF8.GetBytes(
        "{\"version\":\"3.0.0\",\"resources\":[{\"@id\":\""
            + BaseAddress
            + "\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}"
    );
    private static readonly Lazy<Task<byte[]>> Package = new(CreatePackageAsync);

    [TestMethod]
    public async Task RestoreUsesBoundedHttpSourceAndWritesExactPackageAssets()
    {
        byte[] original = await Package.Value;
        ConsumerRequest request = await CreateConsumerAsync(original);
        request = ConsumerRequest.Read(Encoding.UTF8.GetBytes(request.ToDocument().ToJsonString()));
        using var environment = new CacheEnvironment(request.Workspace);
        var transport = new FeedHandler(request, original);

        JsonObject result = await NativeRestore.RunAsync(request, Token, transport);

        Assert.IsTrue(result["completed"]!.GetValue<bool>());
        Assert.AreEqual(
            ConsumerRequest.Sha256(original),
            result["packageSha256"]!.GetValue<string>()
        );
        int restoreRequests = transport.Urls.Count;
        Assert.IsInRange(minValue: 3, maxValue: request.MaximumRequests, value: restoreRequests);
        CollectionAssert.AreEquivalent(
            new[]
            {
                ConsumerRequest.ServiceIndex,
                BaseAddress + ConsumerRequest.NormalizedId + "/index.json",
                request.PackageUrl,
            },
            transport.Urls.Distinct(StringComparer.Ordinal).ToArray()
        );
        Assert.IsTrue(
            transport.Authorizations.All(value =>
                value
                == "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("hcoona:" + Token))
            )
        );
        string installed = Path.Combine(
            request.PackagesPath,
            ConsumerRequest.NormalizedId,
            request.Version,
            ConsumerRequest.NormalizedId + "." + request.Version + ".nupkg"
        );
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(installed));
        Assert.AreEqual(restoreRequests, result["requests"]!.GetValue<int>());
        Assert.AreEqual(transport.ReturnedBytes, result["responseBytes"]!.GetValue<long>());
        byte[] effectiveRequest = await File.ReadAllBytesAsync(
            Path.Combine(request.EvidencePath, "request.json")
        );
        Assert.AreEqual(
            ConsumerRequest.Sha256(effectiveRequest),
            result["requestSha256"]!.GetValue<string>()
        );
        Assert.IsTrue(JsonNode.DeepEquals(request.ToDocument(), JsonNode.Parse(effectiveRequest)));

        await RunSdkAsync(
            request.Workspace,
            "build",
            request.ProjectPath,
            "--no-restore",
            "-property:UseSharedCompilation=false",
            "-nodeReuse:false",
            "-bl:" + Path.Combine(request.Workspace, "consumer-build.binlog")
        );
        string output = await RunSdkAsync(
            request.Workspace,
            Path.Combine(request.Workspace, "bin", "Debug", "net10.0", "consumer.dll")
        );
        Assert.AreEqual("hcoona-release-smoke-github-packages", output.Trim());
        Assert.HasCount(restoreRequests, transport.Urls);
        Assert.IsFalse(
            Directory
                .EnumerateFiles(request.EvidencePath)
                .Any(file => File.ReadAllText(file).Contains(Token, StringComparison.Ordinal))
        );
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("version")]
    [DataRow("dependency")]
    [DataRow("reference")]
    [DataRow("audit")]
    [DataRow("cache")]
    [DataRow("mapping")]
    [DataRow("graph-bytes")]
    public async Task RestoreRejectsUnexpectedGraphBeforeTransport(string change)
    {
        byte[] original = await Package.Value;
        ConsumerRequest request = await CreateConsumerAsync(original);
        JsonObject graph = JsonNode
            .Parse(await File.ReadAllBytesAsync(request.GraphPath))!
            .AsObject();
        JsonObject project = graph["projects"]![request.ProjectPath]!.AsObject();
        switch (change)
        {
            case "source":
                project["restore"]!["sources"]!["https://example.invalid/index.json"] =
                    new JsonObject();
                break;
            case "version":
                project["frameworks"]!["net10.0"]!["dependencies"]![ConsumerRequest.PackageId]![
                    "version"
                ] = "[1.2.3,)";
                break;
            case "dependency":
                project["frameworks"]!["net10.0"]!["dependencies"]!["Another.Package"] =
                    new JsonObject { ["version"] = "[2.0.0]", ["target"] = "Package" };
                break;
            case "reference":
                project["restore"]!["frameworks"]!["net10.0"]!["projectReferences"]![
                    "extra.csproj"
                ] = new JsonObject();
                break;
            case "audit":
                project["restore"]!["restoreAuditProperties"]!["enableAudit"] = "true";
                break;
            case "cache":
                Directory.CreateDirectory(request.PackagesPath);
                await File.WriteAllTextAsync(
                    Path.Combine(request.PackagesPath, "old-package"),
                    "preexisting"
                );
                break;
            case "mapping":
                await File.WriteAllTextAsync(
                    request.ConfigPath,
                    ConsumerRequest.Configuration.Replace(
                        "pattern=\"" + ConsumerRequest.PackageId + "\"",
                        "pattern=\"*\"",
                        StringComparison.Ordinal
                    )
                );
                break;
        }
        byte[] changed = Encoding.UTF8.GetBytes(graph.ToJsonString());
        await File.WriteAllBytesAsync(request.GraphPath, changed);
        if (change != "graph-bytes")
        {
            request = request with { GraphSha256 = ConsumerRequest.Sha256(changed) };
        }
        using var environment = new CacheEnvironment(request.Workspace);
        var transport = new FeedHandler(request, original);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NativeRestore.RunAsync(request, Token, transport)
        );

        Assert.IsEmpty(transport.Urls);
        Assert.IsFalse(Directory.Exists(request.EvidencePath));
    }

    [TestMethod]
    public async Task RestoreFailedReadRetainsEvidenceAndCannotRestart()
    {
        ConsumerRequest request = await CreateConsumerAsync(await Package.Value);
        using var environment = new CacheEnvironment(request.Workspace);
        var transport = new FeedHandler(request, [])
        {
            FailureStatus = HttpStatusCode.Unauthorized,
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NativeRestore.RunAsync(request, Token, transport)
        );

        Assert.HasCount(1, transport.Urls);
        Assert.IsTrue(File.Exists(Path.Combine(request.EvidencePath, "failure.json")));
        Assert.IsFalse(File.Exists(Path.Combine(request.EvidencePath, "result.json")));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NativeRestore.RunAsync(request, Token, transport)
        );
        Assert.HasCount(1, transport.Urls);
    }

    [TestMethod]
    [DataRow("service-index")]
    [DataRow("package")]
    [DataRow("witness")]
    public async Task RestoreRejectsSubstitutedOriginalBindings(string changed)
    {
        byte[] original = await Package.Value;
        ConsumerRequest request = await CreateConsumerAsync(original);
        string wrong = new('f', 64);
        request = changed switch
        {
            "service-index" => request with { ServiceIndexSha256 = wrong },
            "package" => request with { PackageSha256 = wrong },
            _ => request with { WitnessSha256 = wrong },
        };
        using var environment = new CacheEnvironment(request.Workspace);
        var transport = new FeedHandler(request, original);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NativeRestore.RunAsync(request, Token, transport)
        );

        Assert.IsFalse(File.Exists(Path.Combine(request.EvidencePath, "result.json")));
        JsonObject failure = JsonNode
            .Parse(
                await File.ReadAllBytesAsync(Path.Combine(request.EvidencePath, "failure.json"))
            )!
            .AsObject();
        Assert.IsTrue(failure["consumerSpent"]!.GetValue<bool>());
        Assert.IsInRange(
            minValue: 1,
            maxValue: request.MaximumRequests,
            value: transport.Urls.Count
        );
        if (changed == "service-index")
        {
            CollectionAssert.AreEqual(new[] { ConsumerRequest.ServiceIndex }, transport.Urls);
        }
        else
        {
            CollectionAssert.Contains(transport.Urls, request.PackageUrl);
        }
    }

    private static async Task<ConsumerRequest> CreateConsumerAsync(byte[] package)
    {
        string root = CreateWorkspace();
        await File.WriteAllTextAsync(
            Path.Combine(root, "nuget.config"),
            ConsumerRequest.Configuration
        );
        await File.WriteAllTextAsync(
            Path.Combine(root, "consumer.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType>"
                + "<TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit>"
                + "<RestoreFallbackFolders></RestoreFallbackFolders></PropertyGroup><ItemGroup>"
                + "<PackageReference Include=\""
                + ConsumerRequest.PackageId
                + "\" Version=\"[1.2.3]\"/>"
                + "</ItemGroup></Project>"
        );
        await File.WriteAllTextAsync(
            Path.Combine(root, "Program.cs"),
            "System.Console.Write(HcoonaReleaseSmokeGithubPackages.Smoke.ProjectId);"
        );
        await RunSdkAsync(
            root,
            "msbuild",
            Path.Combine(root, "consumer.csproj"),
            "-target:GenerateRestoreGraphFile",
            "-property:RestoreGraphOutputPath=" + Path.Combine(root, "graph.json"),
            "-property:RestoreConfigFile=" + Path.Combine(root, "nuget.config"),
            "-nodeReuse:false",
            "-bl:" + Path.Combine(root, "graph.binlog")
        );
        return new ConsumerRequest(
            root,
            "1.2.3",
            ConsumerRequest.Sha256(package),
            ConsumerRequest.Sha256("{\"test\":\"native consumer\"}"u8),
            ConsumerRequest.Sha256(await File.ReadAllBytesAsync(Path.Combine(root, "graph.json"))),
            ConsumerRequest.Sha256(ServiceIndex),
            BaseAddress,
            6,
            1024 * 1024,
            30
        );
    }

    private static async Task<byte[]> CreatePackageAsync()
    {
        string root = CreateWorkspace();
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (
            repository is not null && !File.Exists(Path.Combine(repository.FullName, "dirs.proj"))
        )
        {
            repository = repository.Parent;
        }
        Assert.IsNotNull(repository);
        File.Copy(
            Path.Combine(
                repository.FullName,
                "src/public/lib/hcoona-release-smoke-github-packages/Smoke.cs"
            ),
            Path.Combine(root, "Smoke.cs")
        );
        await File.WriteAllTextAsync(
            Path.Combine(root, "nuget.config"),
            "<configuration><packageSources><clear/></packageSources></configuration>"
        );
        await File.WriteAllTextAsync(
            Path.Combine(root, "witness.json"),
            "{\"test\":\"native consumer\"}"
        );
        await File.WriteAllTextAsync(
            Path.Combine(root, "fixture.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"
                + "<TargetFramework>net10.0</TargetFramework>"
                + "<PackageId>"
                + ConsumerRequest.PackageId
                + "</PackageId><Version>1.2.3</Version>"
                + "<NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>"
                + "<None Include=\"witness.json\" Pack=\"true\" "
                + "PackagePath=\"workflow-delivery/provenance.json\"/>"
                + "</ItemGroup></Project>"
        );
        await RunSdkAsync(
            root,
            "pack",
            Path.Combine(root, "fixture.csproj"),
            "--output",
            Path.Combine(root, "output"),
            "-property:UseSharedCompilation=false",
            "-nodeReuse:false",
            "-bl:" + Path.Combine(root, "fixture-pack.binlog")
        );
        return await File.ReadAllBytesAsync(
            Path.Combine(root, "output", ConsumerRequest.PackageId + ".1.2.3.nupkg")
        );
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "wdv3-consumer-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "home"));
        File.WriteAllText(
            Path.Combine(root, "global.json"),
            "{\"sdk\":{\"version\":\"10.0.300\",\"rollForward\":\"disable\"}}"
        );
        return root;
    }

    private static async Task<string> RunSdkAsync(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment.Clear();
        foreach (
            string key in new[]
            {
                "PATH",
                "DOTNET_ROOT",
                "SystemRoot",
                "SYSTEMDRIVE",
                "WINDIR",
                "TMP",
                "TEMP",
                "TMPDIR",
                "ProgramFiles",
                "ProgramFiles(x86)",
                "LD_LIBRARY_PATH",
            }
        )
        {
            if (Environment.GetEnvironmentVariable(key) is string value)
            {
                start.Environment[key] = value;
            }
        }
        foreach (string key in new[] { "HOME", "USERPROFILE", "DOTNET_CLI_HOME" })
        {
            start.Environment[key] = Path.Combine(root, "home");
        }
        start.Environment["NUGET_PACKAGES"] = Path.Combine(root, "packages");
        start.Environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(root, "http-cache");
        start.Environment["NUGET_PLUGINS_CACHE_PATH"] = Path.Combine(root, "plugin-cache");
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using Process process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Missing test SDK process.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        string output = await stdout;
        string errors = await stderr;
        Assert.AreEqual(0, process.ExitCode, output + errors);
        return output;
    }

    private sealed class CacheEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _original = [];

        internal CacheEnvironment(string root)
        {
            foreach (
                (string key, string relative) in new[]
                {
                    ("NUGET_PACKAGES", "packages"),
                    ("NUGET_HTTP_CACHE_PATH", "http-cache"),
                    ("NUGET_PLUGINS_CACHE_PATH", "plugin-cache"),
                    ("DOTNET_CLI_HOME", "home"),
                }
            )
            {
                _original[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, Path.Combine(root, relative));
            }
        }

        public void Dispose()
        {
            foreach ((string key, string? value) in _original)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private sealed class FeedHandler(ConsumerRequest request, byte[] package) : HttpMessageHandler
    {
        internal List<string> Urls { get; } = [];
        internal List<string> Authorizations { get; } = [];
        internal long ReturnedBytes { get; private set; }
        internal HttpStatusCode? FailureStatus { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage message,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = message.RequestUri!.AbsoluteUri;
            Urls.Add(url);
            Authorizations.Add(message.Headers.Authorization!.ToString());
            byte[] bytes =
                FailureStatus is not null ? "read denied"u8.ToArray()
                : url == ConsumerRequest.ServiceIndex ? ServiceIndex
                : url == request.PackageUrl ? package
                : "{\"versions\":[\"1.2.3\"]}"u8.ToArray();
            ReturnedBytes += bytes.Length;
            return Task.FromResult(
                new HttpResponseMessage(FailureStatus ?? HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes),
                }
            );
        }
    }
}
