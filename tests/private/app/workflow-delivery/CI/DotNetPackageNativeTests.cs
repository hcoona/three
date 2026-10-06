using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class DotNetPackageNativeTests(TestContext context)
{
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
        // This deliberately defective test archive is never a product Pack output.
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
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
        string candidateCommitText) : IDisposable
    {
        internal string Checkout { get; } = checkout;
        internal string Scratch { get; } = scratch;
        internal CiPlan Plan { get; } = plan;
        internal Dictionary<string, string> Native { get; } = native;
        internal string SourceRevision { get; } = sourceRevision;
        internal string Candidate => Plan.Candidate;
        internal string ProjectSha256 { get; } = projectSha256;
        internal string CandidateCommitText { get; } = candidateCommitText;
        private int run;
        internal const string Project = "src/public/lib/hcoona-release-smoke-github-packages/" +
            "hcoona-release-smoke-github-packages.csproj";

        internal static async Task<ProductFixture> CreateAsync(CancellationToken token,
            bool wrongMarker = false)
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
                NativeCommandResult checkoutResult = await NativeProcess.ExecuteAsync(
                    new("git", checkout, checkoutArguments, 30), token);
                DotNetNativeFixture.RequireSuccess(checkoutResult,
                    "Fixture git " + string.Join(' ', checkoutArguments) + " in " + checkout);
                string sourceRevision = revision.Commit;
                byte[] projectBytes = await File.ReadAllBytesAsync(
                    Path.Combine(source, Project), token);
                string projectSha256 = Convert.ToHexStringLower(SHA256.HashData(projectBytes));
                await File.WriteAllBytesAsync(Path.Combine(checkout, Project), projectBytes, token);
                var paths = new List<string> { Project };
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
                    checkout, ["restore", Project, "--locked-mode"], 60), token);
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
                    candidateCommitText);
            }
            catch { Cleanup(scratch); throw; }
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
