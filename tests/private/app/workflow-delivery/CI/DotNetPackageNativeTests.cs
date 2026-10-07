using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using NuGet.Packaging;
using NuGet.ProjectModel;
using NuGet.Versioning;
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
        using JsonDocument planning = await ProductFixture.NbgvReceiptAsync(fixture.Checkout,
            globals, context.CancellationToken);
        AssertNativeContext(planning, fixture.Checkout);
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
        using JsonDocument received = await ProductFixture.NbgvReceiptAsync(receiver, globals,
            context.CancellationToken);
        AssertNativeContext(received, receiver);
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
        Assert.IsEmpty(result.Retained.Commands);
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
        Assert.AreEqual(DotNetPackageConsumer.Marker,
            result.Packages.Commands[^1].Result.Stdout.Trim());

        void AssertNativeContext(JsonDocument receipt, string root)
        {
            context.WriteLine(receipt.RootElement.GetRawText());
            JsonElement properties = receipt.RootElement.GetProperty("Properties");
            Assert.AreEqual("MSBuildTargetCaching", properties.GetProperty("NBGV_CacheMode")
                .GetString());
            string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                properties.GetProperty("GitVersionBaseDirectory").GetString()!));
            string expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                Path.Combine(root, Path.GetRelativePath(fixture.Checkout,
                    discovered.VersionBaseDirectory))));
            Assert.AreEqual(expected, directory, OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            JsonElement reference = Assert.ContainsSingle(receipt.RootElement.GetProperty("Items")
                .GetProperty("ProjectReference").EnumerateArray().Where(item =>
                    item.TryGetProperty("NBGV_InnerProject", out JsonElement marker) &&
                    marker.GetString() == "true"));
            string helper = reference.GetProperty("FullPath").GetString()!;
            Assert.AreEqual(Path.GetFullPath(properties.GetProperty("NBGV_CachingProjectReference")
                .GetString()!), Path.GetFullPath(helper));
            Assert.AreEqual("PrivateP2PCaching.proj", Path.GetFileName(helper));
            LockFile assets = new LockFileFormat().Read(properties.GetProperty("ProjectAssetsFile")
                .GetString()!);
            LockFileLibrary package = Assert.ContainsSingle(assets.Libraries.Where(library =>
                library.Name == "Nerdbank.GitVersioning"));
            Assert.AreEqual("3.10.94", package.Version.ToNormalizedString());
            Assert.IsTrue(assets.PackageFolders.Any(folder => Path.GetFullPath(Path.Combine(
                folder.Path, package.Path, "build", "PrivateP2PCaching.proj")) ==
                Path.GetFullPath(helper)));
        }
    }

    [TestMethod]
    public async Task NativeProductPackAndCleanConsumerAgreeWithNbgv()
    {
        using var fixture = await ProductFixture.CreateAsync(context.CancellationToken);
        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);
        Retain(fixture, result, "first");
        context.WriteLine(JsonSerializer.Serialize(
            result, TransferJson.Default.DotNetPackageRunResult));
        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied,
            string.Join("; ", result.Failures.Select(item => item.Error)));
        Assert.HasCount(1, result.Packages);
        Assert.HasCount(1, result.ConsumerOutputs);
        Assert.HasCount(4, result.Commands);
        Assert.AreEqual(DotNetPackageConsumer.Marker,
            result.Commands[^1].Result.Stdout.Trim());
        Assert.IsTrue(result.Commands.Skip(1).All(
            item => item.Command.Environment!["NUGET_PACKAGES"]!
            .StartsWith(fixture.Scratch, StringComparison.Ordinal)));
        DotNetPackageRunResult repeated = await fixture.RunAsync(context.CancellationToken);
        Retain(fixture, repeated, "repeated");
        context.WriteLine(JsonSerializer.Serialize(
            repeated, TransferJson.Default.DotNetPackageRunResult));
        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, repeated.Results).Satisfied,
            string.Join("; ", repeated.Failures.Select(item => item.Error)));
        Assert.AreEqual(result.Packages[0].Sha256, repeated.Packages[0].Sha256,
            "Bounded same-input native package byte reproducibility.");
        Assert.AreNotEqual(result.Packages[0].Path, repeated.Packages[0].Path);
    }

    [TestMethod]
    [DataRow("", "", "true", "315532800")]
    [DataRow("315532804", "315532802", "true", "315532804")]
    [DataRow("", "315532802", "true", "")]
    [DataRow("", "", "false", "")]
    public async Task NativeTimestampDefaultPreservesExplicitInputs(string property,
        string epoch, string deterministic, string expected)
    {
        using var fixture = await ProductFixture.CreateAsync(context.CancellationToken);
        var arguments = new List<string> { "msbuild", ProductFixture.Project, "-nologo",
            "-getProperty:Deterministic,DeterministicTimestamp,SOURCE_DATE_EPOCH",
            "-property:Deterministic=" + deterministic };
        if (property.Length != 0) arguments.Add("-property:DeterministicTimestamp=" + property);
        NativeCommandResult result = await NativeProcess.ExecuteAsync(new("dotnet",
            fixture.Checkout, arguments.ToArray(), 60,
            new Dictionary<string, string?>
            { ["SOURCE_DATE_EPOCH"] = epoch.Length == 0 ? null : epoch }),
            context.CancellationToken);
        DotNetNativeFixture.RequireSuccess(result, "Native timestamp precedence");
        context.WriteLine(result.Stdout);
        using JsonDocument response = JsonDocument.Parse(result.Stdout);
        JsonElement values = response.RootElement.GetProperty("Properties");
        Assert.AreEqual(expected, values.GetProperty("DeterministicTimestamp").GetString());
        Assert.AreEqual(epoch, values.GetProperty("SOURCE_DATE_EPOCH").GetString());
        Assert.AreEqual(deterministic, values.GetProperty("Deterministic").GetString());
    }

    [TestMethod]
    public async Task NativeInstalledMarkerFailureRetainsPackagePrerequisites()
    {
        using var fixture = await ProductFixture.CreateAsync(context.CancellationToken,
            wrongMarker: true);
        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);
        Retain(fixture, result, "wrong-marker");
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item =>
            item.Key.Check == DotNetPackageChecks.Pack).Status);
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item =>
            item.Key.Check == DotNetPackageChecks.Contents).Status);
        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item =>
            item.Key.Check == DotNetPackageChecks.Consumer).Status);
        Assert.Contains("marker API", Assert.ContainsSingle(result.Failures).Error);
        Assert.AreEqual("wrong-native-marker", result.Commands[^1].Result.Stdout.Trim());
        Assert.HasCount(4, result.Commands);
        Assert.IsEmpty(result.ConsumerOutputs);
    }

    [TestMethod]
    public async Task NativeConsumerMissingDependencyFailsWithOriginalArchive()
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        string path = Path.Combine(fixture.Scratch, "WorkflowDelivery.Tests.1.0.0.nupkg");
        fixture.CreateArchive(path);
        const string missing = "WorkflowDelivery.MissingNativeDependency";
        AddMissingDependency(path, missing);
        var original = new DotNetOriginalPackage("product", "package", "package",
            "primary-package", "nuget-package", path, new FileInfo(path).Length,
            DotNetPackageArchive.Digest(path));
        string fresh =
            Directory.CreateDirectory(Path.Combine(fixture.Scratch, "consumer-probe")).FullName;
        var observations = new List<(NativeCommand Command, NativeCommandResult Result)>();
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            DotNetPackageConsumer.RunAsync(original, fixture.Values, fresh, 60,
                async (command, token) =>
                {
                    NativeCommandResult native = await NativeProcess.ExecuteAsync(command, token);
                    observations.Add((command, native));
                    context.WriteLine(native.Stdout + native.Stderr);
                    if (!native.Succeeded)
                        throw new InvalidDataException("Native missing-dependency restore failed.");
                    return native;
                }, context.CancellationToken));
        Assert.Contains("missing-dependency restore failed", failure.Message);
        var restore = Assert.ContainsSingle(observations);
        Assert.Contains("restore", restore.Command.Arguments);
        Assert.IsFalse(restore.Result.Succeeded);
        Assert.Contains("NU1101", restore.Result.Stdout + restore.Result.Stderr);
        Assert.Contains(missing, restore.Result.Stdout + restore.Result.Stderr);
        Assert.AreEqual(original.Sha256, DotNetPackageArchive.Digest(original.Path));
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

        foreach (bool enableSource in new[] { false, true })
        {
            string fresh = Directory.CreateDirectory(Path.Combine(fixture.Scratch,
                enableSource ? "source-enabled" : "source-isolated")).FullName;
            var observations = new List<NativeCommandResult>();
            InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () =>
                DotNetPackageConsumer.RunAsync(original, fixture.Values, fresh, 60,
                    async (command, token) =>
                    {
                        var environment = new Dictionary<string, string?>(command.Environment!)
                        { ["_WorkloadLibraryPacksFolder"] = sdkFeed };
                        string[] arguments = enableSource
                            ? [.. command.Arguments,
                                "-property:DisableImplicitLibraryPacksFolder=false"]
                            : command.Arguments;
                        NativeCommandResult native = await NativeProcess.ExecuteAsync(
                            command with { Arguments = arguments, Environment = environment },
                            token);
                        observations.Add(native);
                        context.WriteLine(native.Stdout + native.Stderr);
                        if (!native.Succeeded)
                            throw new InvalidDataException(
                                "Native isolated dependency restore failed.");
                        return native;
                    }, context.CancellationToken));
            NativeCommandResult restore = Assert.ContainsSingle(observations);
            if (enableSource)
            {
                Assert.IsTrue(restore.Succeeded, "The native sensitivity restore must succeed.");
                Assert.Contains("source count", failure.Message);
                LockFile assets = new LockFileFormat().Read(
                    Path.Combine(fresh, "consumer", "obj", "project.assets.json"));
                Assert.Contains(missing, assets.Libraries.Select(item => item.Name).ToArray());
                string installed = new VersionFolderPathResolver(Path.Combine(fresh, "packages"))
                    .GetPackageFilePath(missing, dependency.Version);
                Assert.IsTrue(File.Exists(installed),
                    "The control dependency was really restored.");
            }
            else
            {
                Assert.IsFalse(restore.Succeeded);
                Assert.Contains("isolated dependency restore failed", failure.Message);
                Assert.Contains("NU1101", restore.Stdout + restore.Stderr);
                Assert.Contains(missing, restore.Stdout + restore.Stderr);
            }
            Assert.AreEqual(original.Sha256, DotNetPackageArchive.Digest(original.Path));
        }
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

    [TestMethod]
    public async Task FreshConsumerCannotBorrowCachedMissingPackage()
    {
        using var fixture = await ProductFixture.CreateAsync(context.CancellationToken);
        DotNetPackageRunResult first = await fixture.RunAsync(context.CancellationToken);
        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, first.Results).Satisfied,
            string.Join("; ", first.Failures.Select(item => item.Error)));
        DotNetOriginalPackage original = first.Packages[0];
        string fresh =
            Directory.CreateDirectory(Path.Combine(fixture.Scratch, "missing-probe")).FullName;
        var observations = new List<NativeCommand>();
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            DotNetPackageConsumer.RunAsync(original, fixture.Native, fresh, 60,
                async (command, token) =>
                {
                    observations.Add(command);
                    foreach (string path in Directory.EnumerateFiles(Path.Combine(fresh, "feed")))
                        File.Delete(path);
                    NativeCommandResult native = await NativeProcess.ExecuteAsync(command, token);
                    context.WriteLine(native.Stdout + native.Stderr);
                    if (!native.Succeeded) throw new InvalidDataException(
                        "Native empty-feed restore failed.");
                    return native;
                }, context.CancellationToken));
        Assert.Contains("empty-feed restore failed", failure.Message);
        NativeCommand restore = Assert.ContainsSingle(observations);
        Assert.Contains("restore", restore.Arguments);
        Assert.AreNotEqual(first.Commands[1].Command.Environment!["NUGET_PACKAGES"],
            restore.Environment!["NUGET_PACKAGES"]);
        Assert.IsTrue(File.Exists(first.ConsumerOutputs[0].Path),
            "The prior successful installed consumer remains available " +
                "but cannot mask this failure.");
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
        private int run;
        internal const string Project = "src/public/lib/hcoona-release-smoke-github-packages/" +
            "hcoona-release-smoke-github-packages.csproj";

        internal static async Task<ProductFixture> CreateAsync(CancellationToken token,
            bool wrongMarker = false, bool discoverGraph = false)
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
                    new("git", checkout, checkoutArguments, 30,
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
                if (wrongMarker)
                {
                    string marker = DotNetRepositoryReader.Parent(Project) + "/Smoke.cs";
                    string content = await File.ReadAllTextAsync(
                        Path.Combine(checkout, marker), token);
                    await File.WriteAllTextAsync(Path.Combine(checkout, marker), content.Replace(
                        DotNetPackageConsumer.Marker, "wrong-native-marker",
                            StringComparison.Ordinal), token);
                    paths.Add(marker);
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
                string declaration = "src/public/lib/hcoona-release-smoke-github-packages/" +
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
                    "-getItem:ProjectReference",
                    "-getProperty:NBGV_CacheMode,NBGV_CachingProjectReference," +
                    "GitVersionBaseDirectory,ProjectAssetsFile"],
                60), token);
            token.ThrowIfCancellationRequested();
            DotNetNativeFixture.RequireSuccess(result, "Native receiving NBGV context");
            return JsonDocument.Parse(result.Stdout);
        }

        internal Task<DotNetPackageRunResult> RunAsync(CancellationToken token) =>
            DotNetPackageExecution.RunAsync(Plan, new(Checkout,
                Directory.CreateDirectory(
                    Path.Combine(Scratch, "run-" + run++)).FullName, 60), token);

        public void Dispose() => Cleanup(Scratch);

        private static void Cleanup(string root)
        {
            foreach (string path in Directory.EnumerateFiles(
                root, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
