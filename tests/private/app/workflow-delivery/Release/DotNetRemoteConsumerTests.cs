using System.Net;
using System.Text;
using System.Text.Json;
using NuGet.Packaging;
using NuGet.Versioning;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class DotNetRemoteConsumerTests(TestContext context)
{
    private static readonly string[] WindowsInstallationInputs = ["ProgramFiles",
        "ProgramFiles(x86)"];

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    [DataRow("Cancelled")]
    [DataRow("TimedOut")]
    [DataRow("CleanupFailed")]
    public async Task NativeCommandTerminationPreservesInterruptionAndCleanupFailure(
        string terminationName)
    {
        NativeTermination termination = Enum.Parse<NativeTermination>(terminationName);
        string root = Directory.CreateTempSubdirectory("nuget-command-termination-").FullName;
        try
        {
            string package = await PackageAsync(root);
            var original = new DotNetOriginalPackage("product", "package", "original",
                "primary-package", "nuget-package", package, new FileInfo(package).Length,
                DotNetPackageArchive.Digest(package));
            var native = new Dictionary<string, string>
            {
                ["PackageId"] = DotNetRemoteConsumer.Package,
                ["NuGetPackageVersion"] = "1.0.0", ["TargetFramework"] = "net10.0",
                ["NETCoreSdkVersion"] = await SdkAsync(root),
            };
            using var parent = CancellationTokenSource.CreateLinkedTokenSource(
                context.CancellationToken);
            using var transport = new Feed(await File.ReadAllBytesAsync(package,
                context.CancellationToken), false, null, new(), 200);
            var seen = new List<NativeCommand>();
            string scratch = Directory.CreateDirectory(Path.Combine(root, "remote")).FullName;
            Task<DotNetRemoteConsumerResult> operation = DotNetRemoteConsumer.RunAsync(original,
                native, scratch, "controlled-read-capability", parent.Token, transport,
                async (command, _) =>
                {
                    seen.Add(command);
                    if (termination == NativeTermination.CleanupFailed)
                        await parent.CancelAsync();
                    return new(termination, 17, "", "", 0, "Controlled command interruption.");
                });

            Exception failure = termination switch
            {
                NativeTermination.Cancelled => await Assert.ThrowsExactlyAsync<
                    OperationCanceledException>(() => operation),
                NativeTermination.TimedOut => await Assert.ThrowsExactlyAsync<TimeoutException>(
                    () => operation),
                _ => await Assert.ThrowsExactlyAsync<InvalidDataException>(() => operation),
            };

            Assert.Contains(termination.ToString(), failure.Message);
            Assert.Contains("exit 17", failure.Message);
            Assert.AreEqual("--version", Assert.ContainsSingle(
                Assert.ContainsSingle(seen).Arguments));
            Assert.IsEmpty(transport.Requests);
            Assert.AreEqual(termination == NativeTermination.Cancelled, operation.IsCanceled);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    [DataRow(false, false, false, false, 200)]
    [DataRow(true, false, false, false, 200)]
    [DataRow(true, false, false, false, 201)]
    [DataRow(false, true, false, false, 200)]
    [DataRow(false, false, true, false, 200)]
    [DataRow(false, false, false, true, 200)]
    public async Task NativeRemoteRestoreBuildAndMarkerUseSelectedOriginal(bool redirect,
        bool conflict, bool cancel, bool wrongSource, int storageStatus)
    {
        string root = Directory.CreateTempSubdirectory("nuget-remote-native-").FullName;
        try
        {
            string package = await PackageAsync(root);
            var original = new DotNetOriginalPackage("product", "package", "original",
                "primary-package", "nuget-package", package, new FileInfo(package).Length,
                DotNetPackageArchive.Digest(package));
            byte[] bytes = await File.ReadAllBytesAsync(package, context.CancellationToken);
            using var parent = CancellationTokenSource.CreateLinkedTokenSource(
                context.CancellationToken);
            var entered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var stopped = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new Feed(conflict ? [.. bytes, 0] : bytes, redirect,
                cancel ? entered : null, stopped, storageStatus);
            string scratch = Directory.CreateDirectory(Path.Combine(root, "remote")).FullName;
            var seen = new List<NativeCommand>();
            var native = new Dictionary<string, string>
            {
                ["PackageId"] = DotNetRemoteConsumer.Package,
                ["NuGetPackageVersion"] = "1.0.0", ["TargetFramework"] = "net10.0",
                ["NETCoreSdkVersion"] = await SdkAsync(root),
            };
            Task<DotNetRemoteConsumerResult> operation = DotNetRemoteConsumer.RunAsync(original,
                native, scratch, "controlled-read-capability", parent.Token, transport,
                async (command, token) =>
                {
                    seen.Add(command);
                    Assert.IsFalse(command.InheritEnvironment);
                    Assert.IsFalse(command.Environment!.ContainsKey("GITHUB_TOKEN"));
                    Assert.IsFalse(command.Environment.ContainsKey("GH_TOKEN"));
                    Assert.IsFalse(command.Environment.Values.Any(value =>
                        value?.Contains("controlled-read-capability", StringComparison.Ordinal)
                            == true));
                    if (OperatingSystem.IsWindows())
                        foreach (string name in WindowsInstallationInputs)
                        {
                            if (Environment.GetEnvironmentVariable(name) is { } expected)
                                Assert.AreEqual(expected, command.Environment[name], name);
                        }
                    NativeCommandResult actual = await NativeProcess.ExecuteAsync(command, token);
                    context.WriteLine(actual.Stdout + actual.Stderr);
                    Assert.IsTrue(actual.Succeeded, JsonSerializer.Serialize(new
                    {
                        command.Executable, command.Arguments, command.DeadlineSeconds,
                        actual.Termination, actual.ExitCode, actual.DurationSeconds,
                        actual.Error, actual.Stdout, actual.Stderr,
                    }));
                    if (wrongSource && command.Arguments.Contains(
                        "-target:GenerateRestoreGraphFile"))
                    {
                        string graph = Path.Combine(scratch, "consumer", "graph.json");
                        string content = await File.ReadAllTextAsync(graph, token);
                        await File.WriteAllTextAsync(graph, content.Replace(
                            DotNetRemoteConsumer.ServiceIndex,
                            "https://unselected.example.test/index.json", StringComparison.Ordinal),
                            token);
                    }
                    return actual;
                });
            if (wrongSource)
            {
                await Assert.ThrowsExactlyAsync<InvalidDataException>(() => operation);
                Assert.IsEmpty(transport.Requests);
                Assert.HasCount(2, seen);
                return;
            }
            if (cancel)
            {
                Task reached = await Task.WhenAny(entered.Task, operation)
                    .WaitAsync(context.CancellationToken);
                if (reached == operation)
                {
                    await operation;
                    Assert.Fail("Native consumer completed before transport entry.");
                }
                await parent.CancelAsync();
                await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
                Assert.IsTrue(stopped.Task.IsCompletedSuccessfully);
                Assert.HasCount(2, seen);
                return;
            }
            if (storageStatus != 200)
            {
                await Assert.ThrowsExactlyAsync<InvalidDataException>(() => operation);
                Assert.HasCount(2, seen);
                Assert.IsFalse(seen.Any(command => command.Arguments.Contains(
                    "-target:Build,GetTargetPath")));
                return;
            }
            if (conflict)
            {
                InvalidDataException failure = await
                    Assert.ThrowsExactlyAsync<InvalidDataException>(
                    () => operation);
                Assert.Contains("Installed remote original bytes differ", failure.Message);
                Assert.HasCount(2, seen);
                Assert.IsFalse(seen.Any(command => command.Arguments.Contains(
                    "-target:Build,GetTargetPath")));
                return;
            }
            DotNetRemoteConsumerResult result = await operation;
            Assert.AreEqual(DotNetPackageConsumer.Marker, result.Commands[^1].Stdout.Trim());
            Assert.IsTrue(result.Commands.All(command => command.Succeeded));
            Assert.IsTrue(File.Exists(result.Output));
            Assert.AreEqual(original.Sha256, DotNetPackageArchive.Digest(Path.Combine(scratch,
                "packages", DotNetRemoteConsumer.Package.ToLowerInvariant(), "1.0.0",
                DotNetRemoteConsumer.Package.ToLowerInvariant() + ".1.0.0.nupkg")));
            Assert.Contains("-target:GenerateRestoreGraphFile", seen[1].Arguments);
            Assert.HasCount(4, seen);
            Assert.IsTrue(transport.Requests.All(item => item.Method == HttpMethod.Get));
            Assert.Contains("/hcoona/flat/hcoona.releasesmoke.githubpackages/1.0.0/" +
                "hcoona.releasesmoke.githubpackages.1.0.0.nupkg",
                transport.Requests.Select(item => item.Uri.AbsolutePath).ToArray());
            Assert.IsTrue(transport.Requests.Where(item => item.Uri.Host ==
                "nuget.pkg.github.com").All(item => item.Authorization?.StartsWith("Basic ",
                    StringComparison.Ordinal) == true));
            if (redirect) Assert.IsGreaterThan(0, transport.Requests.Count(item =>
                item.Uri.Host == "storage.example.test"));
            else Assert.IsFalse(transport.Requests.Any(item =>
                item.Uri.Host == "storage.example.test"));
            Assert.IsTrue(transport.Requests.Where(item => item.Uri.Host ==
                "storage.example.test").All(item => item.Authorization is null));
            context.WriteLine(JsonSerializer.Serialize(result));
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => Path.GetFileName(file) is "graph.json" or "project.assets.json"))
            {
                string retained = Path.Combine(context.TestResultsDirectory!,
                    Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(file));
                File.Copy(file, retained);
                context.AddResultFile(retained);
                context.WriteLine(await File.ReadAllTextAsync(file, context.CancellationToken));
            }
            Directory.Delete(root, true);
        }
    }

    private async Task<string> SdkAsync(string root)
    {
        NativeCommandResult sdk = await NativeProcess.ExecuteAsync(new("dotnet", root,
            ["--version"], 30), context.CancellationToken);
        Assert.IsTrue(sdk.Succeeded, sdk.Stderr);
        return sdk.Stdout.Trim();
    }

    private async Task<string> PackageAsync(string root)
    {
        string producer = Directory.CreateDirectory(Path.Combine(root, "producer")).FullName;
        string project = Path.Combine(producer, "Product.csproj");
        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>Hcoona.ReleaseSmoke.GithubPackages</AssemblyName>
                <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
                <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                <NuGetAudit>false</NuGetAudit>
              </PropertyGroup>
            </Project>
            """, context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(producer, "Smoke.cs"), """
            namespace HcoonaReleaseSmokeGithubPackages;
            public static class Smoke
            {
                public const string ProjectId = "hcoona-release-smoke-github-packages";
            }
            """, context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(producer, "NuGet.Config"),
            "<configuration><packageSources><clear/></packageSources></configuration>",
            context.CancellationToken);
        NativeCommandResult build = await NativeProcess.ExecuteAsync(new("dotnet", producer,
            ["build", project, "-nologo", "-property:RestoreConfigFile=" +
                Path.Combine(producer, "NuGet.Config")], 60), context.CancellationToken);
        context.WriteLine(build.Stdout + build.Stderr);
        Assert.IsTrue(build.Succeeded, build.Stdout + build.Stderr);
        var builder = new PackageBuilder
        {
            Id = DotNetRemoteConsumer.Package, Version = new NuGetVersion(1, 0, 0),
            Description = "Controlled native consumer package.",
        };
        builder.Authors.Add("WorkflowDelivery.Tests");
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = Path.Combine(producer, "bin", "Debug", "net10.0",
                "Hcoona.ReleaseSmoke.GithubPackages.dll"),
            TargetPath = "lib/net10.0/Hcoona.ReleaseSmoke.GithubPackages.dll",
        });
        string package = Path.Combine(root, "original.nupkg");
        using Stream file = File.Create(package);
        builder.Save(file);
        return package;
    }

    private sealed class Feed(byte[] package, bool redirect, TaskCompletionSource? entered,
        TaskCompletionSource stopped, int storageStatus) : HttpMessageHandler
    {
        internal List<(HttpMethod Method, Uri Uri, string? Authorization)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (entered is not null)
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { stopped.TrySetResult(); }
            }
            Uri uri = request.RequestUri!;
            Requests.Add((request.Method, uri, request.Headers.Authorization?.ToString()));
            System.Console.WriteLine("Controlled native read " + uri.AbsolutePath);
            HttpResponseMessage response;
            if (uri.Host == "storage.example.test")
                response = new((HttpStatusCode)storageStatus)
                    { Content = new ByteArrayContent(package) };
            else if (uri.AbsolutePath == "/hcoona/index.json")
                response = Json("""
                    {"version":"3.0.0","resources":[
                      {"@id":"https://nuget.pkg.github.com/hcoona/flat/",
                       "@type":"PackageBaseAddress/3.0.0"}]}
                    """);
            else if (uri.AbsolutePath.EndsWith(".nupkg", StringComparison.Ordinal))
            {
                response = new(redirect ? HttpStatusCode.Found : HttpStatusCode.OK)
                    { Content = new ByteArrayContent(redirect ? [] : package) };
                if (redirect) response.Headers.TryAddWithoutValidation("Location",
                    "https://storage.example.test/original.nupkg?signature=controlled");
            }
            else if (uri.AbsolutePath.EndsWith("/index.json", StringComparison.Ordinal))
                response = Json("{\"versions\":[\"1.0.0\"]}");
            else response = new(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) };
            response.RequestMessage = request;
            return response;
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
