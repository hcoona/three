using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using NuGet.Packaging;
using NuGet.ProjectModel;
using NuGet.Versioning;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class DotNetPackageNativeTests(TestContext context)
{
    [TestMethod]
    [Timeout(600000, CooperativeCancellation = true)]
    public async Task NativeOriginalPlanExecutesInDifferentCheckout()
    {
        using var fixture = await ProductFixture.CreateAsync(context.CancellationToken,
            discoverGraph: true);
        DotNetGraphNode? discovered = fixture.DiscoveredNode;
        Assert.IsNotNull(discovered);
        string original = JsonSerializer.Serialize(fixture.Plan, TransferJson.Default.CiPlan);
        CiPlan transferred = JsonSerializer.Deserialize(original, TransferJson.Default.CiPlan)!;
        Dictionary<string, string> globals = JsonSerializer.Deserialize(
            transferred.Checks[0].Work.Dimensions["globals"],
            TransferJson.Default.DictionaryStringString)!;
        CollectionAssert.AreEquivalent(discovered.Identity.Globals.ToArray(), globals.ToArray());
        Assert.IsFalse(globals.ContainsKey("GitVersionBaseDirectory"));
        Assert.IsTrue(globals.Values.All(value => !value.Contains(fixture.Checkout,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                StringComparison.Ordinal)));
        string receiver = Path.Combine(fixture.Scratch, "receiver");
        NativeCommandResult checkout = await NativeProcess.ExecuteAsync(new("git", fixture.Checkout,
            ["worktree", "add", "--detach", receiver, transferred.Candidate], 30,
            new Dictionary<string, string?> { ["GIT_LFS_SKIP_SMUDGE"] = "1" }),
            context.CancellationToken);
        DotNetNativeFixture.RequireSuccess(checkout, "Transferred exact candidate checkout");
        NativeCommandResult restore = await NativeProcess.ExecuteAsync(new("dotnet", receiver,
            ["restore", ProductFixture.Project, "--locked-mode"], 60),
            context.CancellationToken);
        DotNetNativeFixture.RequireSuccess(restore, "Transferred locked package preparation");
        // The transfer omits checkout paths; NBGV must recompute them in the receiver.
        using (JsonDocument received = await ProductFixture.NbgvReceiptAsync(receiver, globals,
            context.CancellationToken))
        {
            context.WriteLine(received.RootElement.GetRawText());
            JsonElement properties = received.RootElement.GetProperty("Properties");
            Assert.AreEqual("MSBuildTargetCaching", properties.GetProperty("NBGV_CacheMode")
                .GetString());
            string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                properties.GetProperty("GitVersionBaseDirectory").GetString()!));
            string expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                Path.Combine(receiver, Path.GetRelativePath(fixture.Checkout,
                    discovered.VersionBaseDirectory))));
            Assert.AreEqual(expected, directory, OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }
        string execution = Directory.CreateDirectory(
            Path.Combine(fixture.Scratch, "received-run")).FullName;
        DotNetGroupRunResult result = await DotNetGroupExecution.RunAsync(transferred,
            new(receiver, execution, 60), context.CancellationToken);
        Retain(fixture, result.Packages, "cross-root");
        context.WriteLine(JsonSerializer.Serialize(result,
            TransferJson.Default.DotNetGroupRunResult));
        context.WriteLine(JsonSerializer.Serialize(fixture.Native,
            TransferJson.Default.DictionaryStringString));
        string receipt = result.Packages.Commands[0].Command.Arguments.Single(argument =>
            argument.StartsWith("-getResultOutputFile:", StringComparison.Ordinal))
            ["-getResultOutputFile:".Length..];
        context.WriteLine(await File.ReadAllTextAsync(receipt, context.CancellationToken));
        Assert.AreNotEqual(fixture.Checkout, receiver);
        Assert.IsTrue(result.Outcome.Satisfied,
            string.Join("; ", result.Packages.Failures.Select(item => item.Error)));
        Assert.HasCount(3, result.Outcome.Checks);
        Assert.IsNull(result.Retained.TestBatch);
        CollectionAssert.AreEqual(transferred.Checks.Select(item => item.Work.Key).ToArray(),
            result.Results.Select(item => item.Key).ToArray());
        Assert.AreEqual(original, JsonSerializer.Serialize(transferred,
            TransferJson.Default.CiPlan));
        NativeCommand pack = result.Packages.Commands[0].Command;
        Assert.AreEqual(receiver, pack.Directory);
        Assert.Contains(Path.GetFullPath(Path.Combine(receiver, ProductFixture.Project)),
            pack.Arguments);
        Assert.IsFalse(pack.Arguments.Any(argument => argument.Contains("NBGV_CacheMode",
            StringComparison.OrdinalIgnoreCase)));
        CollectionAssert.IsSubsetOf(DotNetChecks.Properties(globals), pack.Arguments);
        Assert.HasCount(1, result.Packages.Packages);
        Assert.HasCount(1, result.Packages.ConsumerOutputs);
        Assert.HasCount(4, result.Packages.Commands);
        Assert.AreEqual(DotNetPackageConsumer.Marker,
            result.Packages.Commands[^1].Result.Stdout.Trim());
        // The consumer restores only into this run's fresh package folder; NuGet owns the rest.
        string packages = result.Packages.Commands[1].Command.Environment!["NUGET_PACKAGES"]!;
        Assert.StartsWith(execution + Path.DirectorySeparatorChar, packages);
        Assert.IsTrue(result.Packages.Commands.Skip(1).All(
            item => item.Command.Environment!["NUGET_PACKAGES"] == packages));
        CollectionAssert.IsSubsetOf(new[] { "--packages", packages },
            result.Packages.Commands[1].Command.Arguments);
    }

    // MSBuild owns property precedence; this guards only the product's default Condition.
    [TestMethod]
    public async Task NativeTimestampDefaultAppliesWithoutExplicitInputs()
    {
        string source = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(source, "global.json")) ||
               !File.Exists(Path.Combine(source, ProductFixture.Project)))
            source = Path.GetDirectoryName(source) ??
                throw new InvalidDataException("Repository source root is unavailable.");
        string scratch = Directory.CreateTempSubdirectory("workflow-native-timestamp-").FullName;
        try
        {
            string project = Path.Combine(scratch, Path.GetFileName(ProductFixture.Project));
            File.Copy(Path.Combine(source, ProductFixture.Project), project);
            File.Copy(Path.Combine(source, "global.json"), Path.Combine(scratch, "global.json"));
            var arguments = new List<string> { "msbuild", project, "-nologo", "-noAutoResponse",
                "-property:ImportDirectoryBuildProps=false",
                "-property:ImportDirectoryBuildTargets=false",
                "-getProperty:Deterministic,DeterministicTimestamp,SOURCE_DATE_EPOCH",
                "-property:Deterministic=true" };
            NativeCommandResult result = await NativeProcess.ExecuteAsync(new("dotnet",
                scratch, arguments.ToArray(), 60,
                new Dictionary<string, string?> { ["SOURCE_DATE_EPOCH"] = null }),
                context.CancellationToken);
            DotNetNativeFixture.RequireSuccess(result, "Native timestamp precedence");
            context.WriteLine(result.Stdout);
            using JsonDocument response = JsonDocument.Parse(result.Stdout);
            JsonElement values = response.RootElement.GetProperty("Properties");
            Assert.AreEqual("315532800", values.GetProperty("DeterministicTimestamp").GetString());
            Assert.AreEqual("", values.GetProperty("SOURCE_DATE_EPOCH").GetString());
            Assert.AreEqual("true", values.GetProperty("Deterministic").GetString());
        }
        finally { Directory.Delete(scratch, true); }
    }

    [TestMethod]
    public async Task NativeConsumerCannotBorrowDependencyFromImplicitSdkFeed()
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        string path = Path.Combine(fixture.Scratch, "WorkflowDelivery.Tests.1.0.0.nupkg");
        fixture.CreateArchive(path);
        const string missing = "WorkflowDelivery.ImplicitSdkDependency";
        AddMissingDependency(path, missing);
        var original = new DotNetOriginalPackage("product", "package", "package",
            "primary-package", "nuget-package", path, new FileInfo(path).Length,
            DotNetPackageArchive.Digest(path));
        string sdkFeed = Directory.CreateDirectory(Path.Combine(fixture.Scratch, "sdk-feed"))
            .FullName;
        string readme = Path.Combine(fixture.Scratch, "dependency-readme.md");
        await File.WriteAllTextAsync(readme, "Owned native source sensitivity package.",
            context.CancellationToken);
        var dependency = new PackageBuilder
        {
            Id = missing,
            Version = new NuGetVersion(1, 0, 0),
            Description = "Dependency available only in the owned implicit SDK feed."
        };
        dependency.Authors.Add("WorkflowDelivery.Tests");
        dependency.Files.Add(new PhysicalPackageFile
        { SourcePath = readme, TargetPath = "README.md" });
        using (Stream output = File.Create(Path.Combine(sdkFeed, missing + ".1.0.0.nupkg")))
            dependency.Save(output);

        // NuGet owns the isolated default; this observes the app's guard on real enabled assets.
        string fresh = Directory.CreateDirectory(Path.Combine(fixture.Scratch, "source-enabled"))
            .FullName;
        var observations = new List<NativeCommandResult>();
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            DotNetPackageConsumer.RunAsync(original, fixture.Values, fresh, 60,
                async (command, token) =>
                {
                    var environment = new Dictionary<string, string?>(command.Environment!)
                    { ["_WorkloadLibraryPacksFolder"] = sdkFeed };
                    NativeCommandResult native = await NativeProcess.ExecuteAsync(command with
                    {
                        Arguments = [.. command.Arguments,
                            "-property:DisableImplicitLibraryPacksFolder=false"],
                        Environment = environment,
                    }, token);
                    observations.Add(native);
                    context.WriteLine(native.Stdout + native.Stderr);
                    if (!native.Succeeded)
                        throw new InvalidDataException("Native enabled dependency restore failed.");
                    return native;
                }, context.CancellationToken));
        NativeCommandResult restore = Assert.ContainsSingle(observations);
        Assert.IsTrue(restore.Succeeded, "The native sensitivity restore must succeed.");
        Assert.Contains("source count", failure.Message);
        LockFile assets = new LockFileFormat().Read(
            Path.Combine(fresh, "consumer", "obj", "project.assets.json"));
        Assert.Contains(missing, assets.Libraries.Select(item => item.Name).ToArray());
        string installed = new VersionFolderPathResolver(Path.Combine(fresh, "packages"))
            .GetPackageFilePath(missing, dependency.Version);
        Assert.IsTrue(File.Exists(installed), "The control dependency was really restored.");
        Assert.AreEqual(original.Sha256, DotNetPackageArchive.Digest(original.Path));
    }

    private static void AddMissingDependency(string path, string missing)
    {
        // This deliberately defective test archive is never a product Pack output.
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Update);
        ZipArchiveEntry entry = zip.GetEntry("product.nuspec")!;
        XDocument document;
        using (Stream input = entry.Open()) document = XDocument.Load(input);
        document.Root!.Element("metadata")!.Add(new XElement("dependencies",
            new XElement("dependency", new XAttribute("id", missing),
                new XAttribute("version", "[1.0.0]"))));
        entry.Delete();
        using Stream output = zip.CreateEntry("product.nuspec").Open();
        document.Save(output);
    }

    private void Retain(ProductFixture fixture, DotNetPackageRunResult result, string label)
    {
        string directory = Directory.CreateDirectory(Path.Combine(
            context.TestResultsDirectory ??
                throw new InvalidDataException("Missing test results directory."),
            "native-archives", label + "-" + Guid.NewGuid().ToString("N"))).FullName;
        var archives = new List<DotNetOriginalPackage>();
        foreach (DotNetOriginalPackage original in result.Packages)
        {
            string path = Path.Combine(directory, Path.GetFileName(original.Path));
            File.Copy(original.Path, path);
            Assert.AreEqual(original.Sha256, DotNetPackageArchive.Digest(path));
            archives.Add(original with { Path = path });
            context.AddResultFile(path);
        }
        string binding = Path.Combine(directory, "binding.json");
        string project = Path.Combine(directory, "product.csproj");
        string marker = Path.Combine(directory, "Smoke.cs");
        File.Copy(Path.Combine(fixture.Checkout, ProductFixture.Project), project);
        File.Copy(
            Path.Combine(fixture.Checkout, DotNetRepositoryReader.Parent(ProductFixture.Project),
            "Smoke.cs"), marker);
        context.AddResultFile(project);
        context.AddResultFile(marker);
        File.WriteAllText(binding, JsonSerializer.Serialize(new
        {
            fixture.SourceRevision,
            fixture.Candidate,
            fixture.ProjectSha256,
            fixture.CandidateCommitText,
            Native = fixture.Native,
            Label = label,
            OriginalArchives = archives,
            Scope = "Current product project bytes in an isolated clean fixture commit; " +
                "no source-worktree commit."
        }));
        context.AddResultFile(binding);
        context.WriteLine(File.ReadAllText(binding));
    }

    private sealed class ProductFixture(string checkout, string scratch, CiPlan plan,
        Dictionary<string, string> native, string sourceRevision, string projectSha256,
        string candidateCommitText, DotNetGraphNode? discoveredNode = null) : IDisposable
    {
        internal string Checkout { get; } = checkout;
        internal string Scratch { get; } = scratch;
        internal CiPlan Plan { get; } = plan;
        internal Dictionary<string, string> Native { get; } = native;
        internal string SourceRevision { get; } = sourceRevision;
        internal string Candidate => Plan.Candidate;
        internal string ProjectSha256 { get; } = projectSha256;
        internal string CandidateCommitText { get; } = candidateCommitText;
        internal DotNetGraphNode? DiscoveredNode { get; } = discoveredNode;
        private static readonly string[] GraphRestoreProperties = ["-property:Configuration=Debug",
            "-property:ContinuousIntegrationBuild=true"];
        internal const string Project = "tests/private/app/workflow-delivery/" +
            "fixtures/products/hcoona-release-smoke-github-packages/" +
            "hcoona-release-smoke-github-packages.csproj";

        internal static async Task<ProductFixture> CreateAsync(CancellationToken token,
            bool discoverGraph = false)
        {
            string source = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(source, "global.json")) ||
                   !Directory.Exists(Path.Combine(source, "src/private/app/workflow-delivery")))
                source = Path.GetDirectoryName(source) ??
                    throw new InvalidDataException("Repository source root is unavailable.");
            string scratch = Directory.CreateTempSubdirectory("workflow-native-nuget-").FullName;
            string checkout = Path.Combine(scratch, "source");
            try
            {
                GitRevision revision = await new GitReader(source).ReadAsync("HEAD", token);
                await NativeProcess.RunAsync("git", source,
                    ["clone", "--shared", "--no-checkout", "--", source, checkout], token);
                string[] checkoutArguments = ["checkout", "--detach", revision.Commit];
                // This package fixture needs Git history, not unrelated products' LFS payloads.
                NativeCommandResult checkoutResult = await NativeProcess.ExecuteAsync(
                    new("git", checkout, checkoutArguments, 120,
                        new Dictionary<string, string?> { ["GIT_LFS_SKIP_SMUDGE"] = "1" }), token);
                DotNetNativeFixture.RequireSuccess(checkoutResult,
                    "Fixture git " + string.Join(' ', checkoutArguments) + " in " + checkout);
                string sourceRevision = revision.Commit;
                byte[] projectBytes = await File.ReadAllBytesAsync(
                    Path.Combine(source, Project), token);
                string projectSha256 = Convert.ToHexStringLower(SHA256.HashData(projectBytes));
                await File.WriteAllBytesAsync(Path.Combine(checkout, Project), projectBytes, token);
                var paths = new List<string> { Project };
                if (discoverGraph)
                {
                    await File.WriteAllTextAsync(Path.Combine(checkout, "dirs.proj"), $"""
                        <Project><ItemGroup><ProjectReference Include="{Project}" />
                        </ItemGroup></Project>
                        """, token);
                    paths.Add("dirs.proj");
                }
                // Like the existing GitFixture, only this disposable clone creates fixture objects.
                foreach (string path in paths)
                {
                    string blob = (await NativeProcess.RunAsync("git", checkout,
                        ["hash-object", "--no-filters", "-w", "--", path], token)).Trim();
                    await NativeProcess.RunAsync("git", checkout,
                        ["update-index", "--cacheinfo", "100644", blob, path], token);
                }
                string tree =
                    (await NativeProcess.RunAsync("git", checkout, ["write-tree"], token)).Trim();
                string candidate = (await NativeProcess.RunAsync("git", checkout,
                    ["-c", "user.name=Workflow fixture", "-c", "user.email=fixture@example.invalid",
                        "-c", "commit.gpgSign=false",
                        "commit-tree", tree, "-p", sourceRevision,
                            "-m", "Native NuGet fixture snapshot"], token)).Trim();
                await NativeProcess.RunAsync(
                    "git", checkout, ["update-ref", "HEAD", candidate], token);
                revision = await new GitReader(checkout).ReadAsync(candidate, token);
                string candidateCommitText = await NativeProcess.RunAsync("git", checkout,
                    ["cat-file", "commit", candidate], token);
                NativeCommandResult preparation = await NativeProcess.ExecuteAsync(new("dotnet",
                    checkout, ["restore", Project, "--locked-mode", .. (discoverGraph
                        ? GraphRestoreProperties : [])], 60), token);
                DotNetNativeFixture.RequireSuccess(preparation, "Exact product locked preparation");
                string declaration = "tests/private/app/workflow-delivery/" +
                    "fixtures/products/hcoona-release-smoke-github-packages/" +
                    "workflow-delivery.release-unit.yml";
                ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(
                    await File.ReadAllTextAsync(Path.Combine(checkout, declaration),
                        token), declaration);
                var node = new DotNetGraphNode(new(
                    Path.GetFullPath(Path.Combine(checkout, Project)),
                    DotNetNativeFixture.Globals(appHost: false)), false,
                    new("Debug", "net10.0", ""), "", "", [], "None");
                if (discoverGraph)
                {
                    string graph = Directory.CreateDirectory(Path.Combine(scratch, "graph"))
                        .FullName;
                    GitMaterialization materialization = await GitMaterialization.BindAsync(
                        checkout, revision, token);
                    var reader = new DotNetRepositoryReader(materialization, new(
                        typeof(DotNetGraphTask).Assembly.Location, Path.Combine(graph, "read.proj"),
                        Path.Combine(graph, "request.json"),
                        Path.Combine(graph, "response.json"), 60));
                    DotNetRevisionInputs inputs = await reader.ReadAsync(token);
                    node = Assert.ContainsSingle(inputs.Nodes.Where(bound =>
                        bound.Project == Project && !bound.Native.OuterBuild)).Native;
                    Assert.IsTrue(inputs.Nodes.All(bound =>
                        bound.Project is "dirs.proj" or Project));
                    Assert.IsFalse(inputs.Graph.Edges.Any(edge =>
                        Path.GetFileName(edge.Dependency.Project) == "PrivateP2PCaching.proj"));
                }
                var project = new ProjectFacts(Project, DotNetRepositoryReader.Parent(Project),
                    [], [], unit.Id, DotNetPackageChecks.Preset, [], [Project]);
                var facts = new RepositoryFacts(revision.Commit, DotNetFactsAssembler.Scope,
                    [project], [new(declaration, [Project])], [], []);
                var selection = new DotNetSelection(new(facts, facts, [Project], false),
                    new Dictionary<string, HashSet<SelectionReason>>
                    { [Project] = [new(Project, revision.Commit, Project)] },
                    new Dictionary<string, DotNetGraphNode[]> { [Project] = [node] }, [unit]);
                string projection =
                    Directory.CreateDirectory(Path.Combine(scratch, "projection")).FullName;
                CiPlan plan = await DotNetFactsAssembler.CompleteAsync(selection,
                    new(checkout, projection, 60), token);
                Dictionary<string, string> native = DotNetPackageChecks.Read(plan.Checks[0].Work,
                    checkout, unit).Values;
                return new(checkout, scratch, plan, native, sourceRevision, projectSha256,
                    candidateCommitText, discoverGraph ? node : null);
            }
            catch { Cleanup(scratch); throw; }
        }

        internal static async Task<JsonDocument> NbgvReceiptAsync(string checkout,
            Dictionary<string, string> globals, CancellationToken token)
        {
            NativeCommandResult result = await NativeProcess.ExecuteAsync(new("dotnet", checkout,
                ["msbuild", Path.Combine(checkout, Project), "-nologo", "-noAutoResponse",
                    .. DotNetChecks.Properties(globals), "-property:IsGraphBuild=true",
                    "-getProperty:NBGV_CacheMode,GitVersionBaseDirectory"],
                60), token);
            token.ThrowIfCancellationRequested();
            DotNetNativeFixture.RequireSuccess(result, "Native receiving NBGV context");
            return JsonDocument.Parse(result.Stdout);
        }

        public void Dispose() => Cleanup(Scratch);

        private static void Cleanup(string root)
        {
            if (OperatingSystem.IsWindows())
            {
                // NBGV 3.10.94 leaves objects/info/alternates open until finalization.
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            foreach (string path in Directory.EnumerateFiles(
                root, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
