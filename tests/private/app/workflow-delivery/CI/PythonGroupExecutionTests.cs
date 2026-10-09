using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class PythonGroupExecutionTests(TestContext context)
{
    [TestMethod]
    public async Task OriginalMixedPlanAndRequiredResultsSurviveBothPartitions()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        PythonGroupRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsTrue(result.Outcome.Satisfied);
        Assert.AreEqual(fixture.Plan.Candidate, result.Candidate);
        Assert.HasCount(5, result.Results);
        Assert.HasCount(2, fixture.Partitions);
        Assert.HasCount(1, fixture.Partitions[0].Checks);
        Assert.HasCount(4, fixture.Partitions[1].Checks);
        foreach (CiPlan partition in fixture.Partitions)
        {
            Assert.AreEqual(fixture.Plan.Comparison, partition.Comparison);
            Assert.AreEqual(fixture.Plan.Candidate, partition.Candidate);
            Assert.AreEqual(fixture.Plan.Scope, partition.Scope);
            foreach (PlannedCheck check in partition.Checks)
                Assert.AreSame(fixture.Plan.Checks.Single(item => item.Work.Key == check.Work.Key),
                    check);
        }
        Assert.AreNotEqual(fixture.Scratches[0], fixture.Scratches[1]);
        CollectionAssert.AreEquivalent(fixture.Plan.Checks.Select(item => item.Work.Key).ToArray(),
            result.Results.Select(item => item.Key).ToArray());
    }

    [TestMethod]
    public async Task PytestFailureLeavesIndependentPackageObligationsVisible()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.PytestStatus = CheckStatus.Failed;
        PythonGroupRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsFalse(result.Outcome.Satisfied);
        Assert.AreEqual(CheckStatus.Failed, Assert.ContainsSingle(result.Pytest.Results).Status);
        Assert.HasCount(4, result.Packages.Results);
        Assert.IsTrue(result.Packages.Results.All(item => item.Status == CheckStatus.Passed));
        Assert.HasCount(2, fixture.Partitions);
    }

    [TestMethod]
    [DataRow("package")]
    [DataRow("pytest")]
    [DataRow("helper")]
    public async Task BothContractsAreValidatedBeforeEitherPartition(string defect)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        CiPlan plan = fixture.Plan;
        PythonGroupRunRequest request = fixture.Request;
        if (defect == "package") plan = plan with { Checks = plan.Checks[..^1] };
        if (defect == "pytest") plan = plan with { Checks = [plan.Checks[0] with
        { Work = plan.Checks[0].Work with
            { Dimensions = new(plan.Checks[0].Work.Dimensions) { ["testPath"] = "foreign" } } },
            .. plan.Checks[1..]] };
        if (defect == "helper") request = request with { PackageHelper = "relative.py" };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken, plan, request));
        Assert.IsEmpty(fixture.Partitions);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    public async Task CancellationRetainsEveryOriginalRequiredResult()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        fixture.Cancel = cancellation;
        PythonGroupRunResult result = await fixture.RunAsync(cancellation.Token);
        Assert.HasCount(5, result.Results);
        Assert.IsTrue(result.Results.All(item => item.Status == CheckStatus.Cancelled));
        Assert.IsFalse(result.Outcome.Satisfied);
        Assert.HasCount(1, fixture.Partitions);
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Scratch, "packages")));
    }

    [TestMethod]
    public async Task MissingRequiredPackageResultCannotBecomeGroupSuccess()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.DropPackageResult = true;
        PythonGroupRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsFalse(result.Outcome.Satisfied);
        Assert.AreEqual(fixture.Plan.Checks[^1].Work.Key,
            Assert.ContainsSingle(result.Outcome.Checks.Where(item =>
                item.Status == "missing")).Key);
    }

    [TestMethod]
    public async Task EmptyPlanCreatesNoProductPartition()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        PythonGroupRunResult result = await fixture.RunAsync(context.CancellationToken,
            fixture.Plan with { Checks = [] });
        Assert.IsTrue(result.Outcome.Satisfied);
        Assert.IsEmpty(result.Results);
        Assert.IsEmpty(fixture.Partitions);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    public async Task CliTransfersOriginalRequestAndCollectsAgainstOriginalPlan()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        using var files = new DriverFiles();
        string plan = Path.Combine(files.Root, "plan.json"), request = Path.Combine(files.Root,
            "request.json");
        await File.WriteAllTextAsync(plan, JsonSerializer.Serialize(fixture.Plan,
            TransferJson.Default.CiPlan), context.CancellationToken);
        await File.WriteAllTextAsync(request, JsonSerializer.Serialize(fixture.Request,
            TransferJson.Default.PythonGroupRunRequest), context.CancellationToken);
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = Program.Run(["ci", "run-python-group", plan, request], output, error,
            runPythonGroup: async (receivedPlan, receivedRequest, token) =>
            {
                Assert.AreEqual(fixture.Plan.Candidate, receivedPlan.Candidate);
                Assert.HasCount(5, receivedPlan.Checks);
                Assert.AreEqual(fixture.Request.Native.Configuration.ConfigurationFile,
                    receivedRequest.Native.Configuration.ConfigurationFile);
                Assert.AreEqual(fixture.Request.PackageHelper, receivedRequest.PackageHelper);
                return await fixture.RunAsync(token, receivedPlan, receivedRequest);
            });
        Assert.AreEqual(0, exit, error.ToString());
        Assert.AreEqual("", error.ToString());
        PythonGroupRunResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.PythonGroupRunResult)!;
        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.HasCount(5, result.Results);
    }

    [TestMethod]
    [DataRow("member", false)]
    [DataRow("member", true)]
    [DataRow("target", false)]
    [DataRow("target", true)]
    public async Task NullNativeElementsReturnInputErrorBeforeDispatch(string defect, bool empty)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        using var files = new DriverFiles();
        CiPlan original = empty ? fixture.Plan with { Checks = [] } : fixture.Plan;
        string plan = Path.Combine(files.Root, "plan.json"), request = Path.Combine(files.Root,
            "request.json");
        await File.WriteAllTextAsync(plan, JsonSerializer.Serialize(original,
            TransferJson.Default.CiPlan), context.CancellationToken);
        JsonNode document = JsonNode.Parse(JsonSerializer.Serialize(fixture.Request,
            TransferJson.Default.PythonGroupRunRequest))!;
        JsonNode native = document["native"]!;
        JsonArray elements = (JsonArray)(defect == "member" ? native["members"]! :
            native["targets"]!["targets"]!);
        elements[0] = null;
        await File.WriteAllTextAsync(request, document.ToJsonString(), context.CancellationToken);
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = Program.Run(["ci", "run-python-group", plan, request], output, error,
            runPythonGroup: (receivedPlan, receivedRequest, token) =>
                fixture.RunAsync(token, receivedPlan, receivedRequest));
        Assert.AreEqual(2, exit);
        Assert.AreEqual("", output.ToString());
        Assert.IsNotEmpty(error.ToString());
        Assert.IsEmpty(fixture.Partitions);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    private sealed class DriverFiles : IDisposable
    {
        internal string Root { get; } =
            Directory.CreateTempSubdirectory("python-group-cli-").FullName;
        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class Fixture(GitFixture repo, CiPlan plan, PythonGroupRunRequest request)
        : IDisposable
    {
        internal GitFixture Repo { get; } = repo;
        internal CiPlan Plan { get; } = plan;
        internal PythonGroupRunRequest Request { get; } = request;
        internal string Scratch => Request.Native.Scratch;
        internal List<CiPlan> Partitions { get; } = [];
        internal List<string> Scratches { get; } = [];
        internal CheckStatus PytestStatus { get; set; } = CheckStatus.Passed;
        internal bool DropPackageResult { get; set; }
        internal CancellationTokenSource? Cancel { get; set; }

        internal static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            GitFixture repo = await GitFixture.CreateAsync(token);
            string scratch = Directory.CreateTempSubdirectory("workflow-python-group-").FullName;
            try
            {
                await repo.SetAsync("pyproject.toml", "controlled root pytest configuration");
                await repo.SetAsync("tests/root/test_one.py");
                await repo.SetAsync("product/pyproject.toml", "controlled native product");
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
                GitRevision revision = await new GitReader(repo.Directory).ReadAsync(candidate,
                    token);
                PythonMetadataMember[] members = [new("native-root", ".", "root"),
                    .. PythonPackageChecksTests.Members];
                var configuration = new PythonPytestConfiguration("pyproject.toml",
                    ["tests/root"], []);
                PythonPytestTargetSet targets = PythonPytestTargets.Bind(configuration, revision,
                    members.Select(member => new PythonProjectDependencies(member.Directory,
                        member.Name, [], [])).ToArray());
                var operation = new PythonPytestOperation("ubuntu-latest", "native", new());
                CheckSpec[] native = PythonPytestChecks.Expand(configuration, targets, operation);
                CheckSpec[] packages = PythonPackageChecks.Expand(PythonPackageChecksTests.Unit,
                    members, new Dictionary<string, string> { ["product"] = "1.0.0" });
                var plan = new CiPlan(candidate, candidate, PythonFactsAssembler.Scope,
                    [.. native.Select(check =>
                        new PlannedCheck(check, [], [], [CheckOrigin.NativeRetained])),
                        .. packages.Select(check =>
                            new PlannedCheck(check, [PythonPackageChecks.Preset],
                            [], [CheckOrigin.Preset]))]);
                return new(repo, plan, new(new(repo.Directory, scratch, "controlled-uv",
                    Path.Combine(scratch, "python"), members, configuration, targets, operation,
                    30, new Dictionary<string, string?>()), Path.Combine(scratch, "packages.py")));
            }
            catch { repo.Dispose(); Directory.Delete(scratch, true); throw; }
        }

        internal Task<PythonGroupRunResult> RunAsync(CancellationToken token, CiPlan? plan = null,
            PythonGroupRunRequest? request = null) => PythonGroupExecution.RunAsync(plan ?? Plan,
                request ?? Request, RunPytestAsync, RunPackagesAsync, token);

        private Task<PythonPytestRunResult> RunPytestAsync(CiPlan plan,
            PythonPytestRunRequest request, CancellationToken token)
        {
            Partitions.Add(plan); Scratches.Add(request.Scratch);
            Cancel?.Cancel(); token.ThrowIfCancellationRequested();
            return Task.FromResult(new PythonPytestRunResult(plan.Candidate,
                plan.Checks.Select(item =>
                new CheckResult(plan.Candidate, item.Work.Key, PytestStatus)).ToArray(), [], []));
        }
        private Task<PythonPackageRunResult> RunPackagesAsync(CiPlan plan,
            PythonPackageRunRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Partitions.Add(plan); Scratches.Add(request.Scratch);
            CheckResult[] results = plan.Checks.Select(item => new CheckResult(plan.Candidate,
                item.Work.Key, CheckStatus.Passed)).ToArray();
            return Task.FromResult(new PythonPackageRunResult(plan.Candidate,
                DropPackageResult ? results[..^1] : results, [], [], []));
        }
        public void Dispose() { Repo.Dispose(); Directory.Delete(Scratch, true); }
    }
}
