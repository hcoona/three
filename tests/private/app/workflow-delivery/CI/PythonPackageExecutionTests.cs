using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class PythonPackageExecutionTests(TestContext context)
{
    private static readonly string[] DistributionKinds = ["python-wheel", "python-sdist"];
    private static readonly string[] ToolGroups = ["dev", "workflow-delivery-python"];

    [TestMethod]
    public async Task CompleteRecipeRetainsOriginalsAndBothIndependentConsumers()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        PythonPackageRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied,
            string.Join("; ", result.Failures.Select(failure => failure.Error)));
        Assert.HasCount(4, result.Results);
        Assert.HasCount(2, result.Distributions);
        CollectionAssert.AreEquivalent(DistributionKinds,
            result.Distributions.Select(item => item.Output.Kind).ToArray());
        foreach (PythonOriginalDistribution original in result.Distributions)
        {
            Assert.AreEqual("product", original.Unit);
            Assert.AreEqual("distributions", original.Build);
            Assert.AreEqual(8L, original.Length);
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData("original"u8)),
                original.Sha256);
            Assert.AreEqual("original", await File.ReadAllTextAsync(original.Path,
                context.CancellationToken));
        }
        NativeCommand[] builds = result.Commands.Select(item => item.Command)
            .Where(command => command.Arguments[0] == "build").ToArray();
        Assert.HasCount(2, builds);
        NativeCommand gitFree = builds.Single(command => command.Arguments.Contains("--wheel"));
        Assert.Contains(result.Distributions.Single(
            item => item.Output.Kind == "python-sdist").Path,
            gitFree.Arguments);
        Assert.Contains("--offline", gitFree.Arguments);
        Assert.Contains("build-dependency-check", gitFree.Arguments);
        Assert.IsNull(gitFree.Environment!["GIT_DIR"]);
        Assert.IsFalse(gitFree.Directory.StartsWith(fixture.Repo.Directory,
            StringComparison.Ordinal));
        NativeCommand[] installs = result.Commands.Select(item => item.Command)
            .Where(command => command.Arguments[0] == "pip").ToArray();
        Assert.HasCount(2, installs);
        Assert.Contains(result.Distributions.Single(
            item => item.Output.Kind == "python-wheel").Path,
            installs[0].Arguments);
        Assert.AreNotEqual(installs[0].Arguments[3], installs[1].Arguments[3]);
        Assert.IsTrue(installs.All(command => command.Arguments.Contains("--offline") &&
            command.Arguments.Contains("--no-cache")));
        Assert.HasCount(2, result.Commands.Where(item => item.Command.Arguments.Contains(
            "consume")));
        NativeCommand[] syncs = result.Commands.Select(item => item.Command)
            .Where(command => command.Arguments[0] == "sync").ToArray();
        Assert.Contains("--no-install-workspace", syncs[0].Arguments);
        CollectionAssert.AreEquivalent(ToolGroups,
            syncs[1].Arguments.Zip(syncs[1].Arguments.Skip(1))
                .Where(pair => pair.First == "--only-group")
                .Select(pair => pair.Second).ToArray());
        Assert.Contains("--no-editable", syncs[1].Arguments);
        Assert.Contains("--no-build-isolation", syncs[1].Arguments);
        Assert.AreEqual(fixture.Plan.Candidate, result.Candidate);
    }

    [TestMethod]
    [DataRow("prepare", "Failed")]
    [DataRow("build", "Failed")]
    [DataRow("build", "Cancelled")]
    [DataRow("build", "TimedOut")]
    [DataRow("version", "Failed")]
    [DataRow("missing-output", "Failed")]
    public async Task FailedNativeBuildPreventsConsumers(string failure, string expectedStatus)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        CheckStatus expected = Enum.Parse<CheckStatus>(expectedStatus);
        fixture.Failure = failure;
        fixture.Terminal = expected;
        PythonPackageRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(expected, result.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Build).Status);
        Assert.IsTrue(result.Results.Where(item => item.Key.Check != PythonPackageChecks.Build)
            .All(item => item.Status == CheckStatus.Skipped));
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.IsFalse(result.Commands.Any(item => item.Command.Arguments.Contains("consume")));
        Assert.HasCount(1, result.Failures);
    }

    [TestMethod]
    public async Task WheelFailureDoesNotEraseIndependentSdistResult()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Failure = "wheel-consumer";
        PythonPackageRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Wheel).Status);
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Sdist).Status);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("contents")]
    [DataRow("original-changed")]
    public async Task InvalidOriginalContentBlocksBothConsumers(string defect)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Failure = defect;
        PythonPackageRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Build).Status);
        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Contents).Status);
        Assert.AreEqual(CheckStatus.Skipped, result.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Wheel).Status);
        Assert.AreEqual(CheckStatus.Skipped, result.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Sdist).Status);
        Assert.IsFalse(result.Commands.Any(item => item.Command.Arguments.Contains("consume")));
    }

    [TestMethod]
    [DataRow("Passed", 0)]
    [DataRow("Failed", 1)]
    [DataRow("Skipped", 1)]
    public async Task CliTransfersOriginalResultsAndCannotSatisfyMissingObligations(
        string statusName, int expectedExit)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        CheckStatus status = Enum.Parse<CheckStatus>(statusName);
        string planFile = Path.Combine(fixture.Scratch, "plan.json");
        string requestFile = Path.Combine(fixture.Scratch, "request.json");
        await File.WriteAllTextAsync(planFile, JsonSerializer.Serialize(fixture.Plan,
            TransferJson.Default.CiPlan), context.CancellationToken);
        await File.WriteAllTextAsync(requestFile, JsonSerializer.Serialize(
            new PythonPackageRunRequest(
            fixture.Repo.Directory, fixture.Scratch, "controlled-uv", "controlled-python",
            "controlled-helper", PythonPackageChecksTests.Members, 30,
            new Dictionary<string, string?>()), TransferJson.Default.PythonPackageRunRequest),
            context.CancellationToken);
        var original = new PythonOriginalDistribution("product", "distributions",
            fixture.Plan.Checks[0].Work.Package!.Outputs[0], "original.whl", 8, "original-digest");
        var result = new PythonPackageRunResult(fixture.Plan.Candidate,
            fixture.Plan.Checks.Select(item => new CheckResult(fixture.Plan.Candidate,
                item.Work.Key, status)).ToArray(), [], [original], []);
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.AreEqual(expectedExit, Program.Run(["ci", "run-python-package", planFile,
            requestFile],
            output, error, runPythonPackage: (plan, request, token) => Task.FromResult(result)));
        Assert.AreEqual("", error.ToString());
        PythonPackageRunResult transferred = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.PythonPackageRunResult)!;
        Assert.AreEqual(original, Assert.ContainsSingle(transferred.Distributions));
        CollectionAssert.AreEqual(result.Results, transferred.Results);
        Assert.AreEqual(result.Candidate, transferred.Candidate);
    }

    [TestMethod]
    [DataRow("missing-check")]
    [DataRow("preset")]
    [DataRow("dirty")]
    [DataRow("prerequisite")]
    public async Task InvalidSelectedContractStopsBeforeNativeEffects(string defect)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        CiPlan plan = fixture.Plan;
        if (defect == "missing-check") plan = plan with { Checks = plan.Checks[..3] };
        if (defect == "preset") plan = plan with { Checks = [plan.Checks[0] with
            { QualityPresets = ["other"] }, .. plan.Checks[1..]] };
        if (defect == "prerequisite") plan = plan with { Checks = [plan.Checks[0],
            plan.Checks[1] with { Work = plan.Checks[1].Work with { Prerequisites = [] } },
            .. plan.Checks[2..]] };
        if (defect == "dirty") await File.WriteAllTextAsync(Path.Combine(fixture.Repo.Directory,
            "product/pyproject.toml"), "dirty", context.CancellationToken);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken, plan));
        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    [DataRow("HEAD", false)]
    [DataRow("branch", false)]
    [DataRow("short", false)]
    [DataRow("HEAD", true)]
    [DataRow("branch", true)]
    [DataRow("short", true)]
    public async Task MutableOrAbbreviatedCandidateStopsBeforeNativeEffects(
        string reference, bool emptyPartition)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        await fixture.Repo.GitAsync("branch", "package-candidate", fixture.Plan.Candidate);
        string candidate = reference switch
        {
            "branch" => "package-candidate",
            "short" => fixture.Plan.Candidate[..8],
            _ => "HEAD",
        };
        CiPlan plan = fixture.Plan with { Candidate = candidate,
            Checks = emptyPartition ? [] : fixture.Plan.Checks };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken, plan));
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    [DataRow("scratch", false)]
    [DataRow("scratch", true)]
    [DataRow("checkout", false)]
    [DataRow("checkout", true)]
    public async Task LinkedScratchOrCheckoutStopsBeforeNativeEffects(
        string boundary, bool linkedAncestor)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        string target = boundary == "scratch"
            ? Directory.CreateDirectory(Path.Combine(fixture.Repo.Directory,
                "neutral")).FullName : fixture.Repo.Directory;
        if (boundary == "scratch" && linkedAncestor)
            Directory.CreateDirectory(Path.Combine(target, "fresh"));
        string linkTarget = boundary == "checkout" && linkedAncestor
            ? Path.GetDirectoryName(target)! : target;
        using var link = await DirectoryLinkFixture.CreateAsync(linkTarget,
            context.CancellationToken);
        string alias = linkedAncestor
            ? Path.Combine(link.Link, boundary == "scratch" ? "fresh" : Path.GetFileName(target))
            : link.Link;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken,
                checkout: boundary == "checkout" ? alias : null,
                scratch: boundary == "scratch" ? alias : null));
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
        if (boundary == "scratch")
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(linkedAncestor
                ? Path.Combine(target, "fresh") : target));
        Assert.AreEqual(fixture.Plan.Candidate,
            (await fixture.Repo.GitAsync("rev-parse", "HEAD")).Trim());
        Assert.AreEqual("", (await fixture.Repo.GitAsync("status", "--porcelain")).Trim());
    }

    private sealed class DirectoryLinkFixture : IDisposable
    {
        private string Root { get; } = Directory.CreateTempSubdirectory(
            "workflow-python-package-alias-").FullName;
        internal string Link => Path.Combine(Root, "alias");

        internal static async Task<DirectoryLinkFixture> CreateAsync(string target,
            CancellationToken token)
        {
            var fixture = new DirectoryLinkFixture();
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    NativeCommandResult result = await NativeProcess.ExecuteAsync(new(
                        "cmd.exe", fixture.Root,
                        ["/d", "/c", "mklink", "/J", fixture.Link, target], 10), token);
                    Assert.IsTrue(result.Succeeded, result.Stderr + result.Error);
                }
                else
                    Directory.CreateSymbolicLink(fixture.Link, target);
                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Link)) Directory.Delete(Link);
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class Fixture(GitFixture repo, CiPlan plan) : IDisposable
    {
        internal GitFixture Repo { get; } = repo;
        internal CiPlan Plan { get; } = plan;
        internal string Scratch { get; } = Directory.CreateTempSubdirectory(
            "python-package-").FullName;
        internal List<NativeCommand> Commands { get; } = [];
        internal string? Failure { get; set; }
        internal CheckStatus Terminal { get; set; } = CheckStatus.Failed;

        internal static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            GitFixture repo = await GitFixture.CreateAsync(token);
            try
            {
                await repo.SetAsync("product/pyproject.toml", "fixture native source");
                await repo.SetAsync(PythonPackageChecksTests.Unit.SourcePath, """
                    schema: workflow-delivery/v3/release-unit
                    release-unit: product
                    builds:
                      - id: distributions
                        definition: python/distribution-set-v1
                        entry-point: pyproject.toml
                        outputs:
                          - id: wheel
                            role: primary-package
                            kind: python-wheel
                          - id: source
                            role: source-package
                            kind: python-sdist
                    """);
                string candidate = await repo.CommitAsync();
                await repo.GitAsync("reset", "--hard", candidate);
                CheckSpec[] checks = PythonPackageChecks.Expand(PythonPackageChecksTests.Unit,
                    PythonPackageChecksTests.Members,
                    new Dictionary<string, string> { ["product"] = "1.0.0" });
                return new(repo, new(candidate, candidate, PythonFactsAssembler.Scope,
                    checks.Select(check => new PlannedCheck(check, [PythonPackageChecks.Preset],
                        [], [CheckOrigin.Preset])).ToArray()));
            }
            catch { repo.Dispose(); throw; }
        }

        internal Task<PythonPackageRunResult> RunAsync(CancellationToken token,
            CiPlan? plan = null, string? checkout = null, string? scratch = null) =>
            PythonPackageExecution.RunAsync(plan ?? Plan,
                new(checkout ?? Repo.Directory, scratch ?? Scratch, "controlled-uv",
                    Path.Combine(Scratch, "python"),
                    Path.Combine(Scratch, "helper.py"), PythonPackageChecksTests.Members, 30,
                    new Dictionary<string, string?> { ["GIT_DIR"] = "excluded-from-consumer" }),
                ExecuteAsync, token);

        private async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            Commands.Add(command);
            string[] arguments = command.Arguments;
            bool build = arguments[0] == "build";
            bool fail = (Failure == "prepare" && arguments[0] == "sync") ||
                (Failure == "build" && build) || (Failure == "wheel-consumer" &&
                    arguments.Contains("consume") && command.Directory.Contains(
                        "python-wheel-consumer",
                        StringComparison.Ordinal)) ||
                (Failure == "contents" && arguments.Contains("archive"));
            if (fail) return new(Terminal switch
            {
                CheckStatus.Cancelled => NativeTermination.Cancelled,
                CheckStatus.TimedOut => NativeTermination.TimedOut,
                _ => NativeTermination.Exited,
            }, 1, "", "controlled failure", 0.1, null);
            if (build)
            {
                string destination = arguments[Array.IndexOf(arguments, "--out-dir") + 1];
                if (arguments.Contains("--wheel"))
                    await File.WriteAllTextAsync(Path.Combine(destination, "rebuilt.whl"),
                        "rebuilt", token);
                else
                {
                    await File.WriteAllTextAsync(Path.Combine(destination, "original.whl"),
                        "original", token);
                    if (Failure != "missing-output")
                        await File.WriteAllTextAsync(Path.Combine(destination,
                            "original.tar.gz"), "original", token);
                }
            }
            string stdout = "{}";
            if (Failure == "original-changed" && arguments.Contains("archive"))
                await File.WriteAllTextAsync(arguments[3], "changed!", token);
            if (arguments.Contains("identity")) stdout = JsonSerializer.Serialize(new
            { name = "hcoona-release-smoke-python",
                version = Failure == "version" ? "2.0" : "1.0.0" });
            if (arguments.Contains("outputs"))
            {
                string directory = arguments[3];
                stdout = JsonSerializer.Serialize(Directory.GetFiles(directory).Select(path => new
                {
                    kind = path.EndsWith(".whl",
                        StringComparison.Ordinal) ? "python-wheel" : "python-sdist",
                    path, length = new FileInfo(path).Length,
                    sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
                }));
            }
            return new(NativeTermination.Exited, 0, stdout, "", 0.1, null);
        }

        public void Dispose()
        {
            Repo.Dispose();
            Directory.Delete(Scratch, recursive: true);
        }
    }
}
