using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class PythonPytestExecutionTests(TestContext context)
{
    private static readonly string[] RootPackages = ["native-root"];
    private static readonly string[] QualityPackages = ["native-root", "native-a", "native-b"];
    private static readonly string[] QualityTargets = ["tests/root", "pkg-a/tests", "pkg-b/tests"];
    private static readonly string[] RootInvocation = ["run", "--no-sync", "python", "-m", "pytest",
        "-c", "pyproject.toml", "tests/root"];

    [TestMethod]
    public async Task RootTargetUsesNativeRootPreparationAndOriginalConfiguration()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        CiPlan selected = fixture.Select("tests/root");
        PythonPytestRunResult result = await fixture.RunAsync(selected, context.CancellationToken);
        Assert.IsTrue(ResultCollector.Collect(selected, result.Results).Satisfied);
        Assert.AreEqual(selected.Candidate, result.Candidate);
        Assert.AreEqual(selected.Checks[0].Work.Key, result.Results[0].Key);
        Assert.HasCount(2, result.Commands);
        PythonPytestCommand preparation = result.Commands[0];
        CollectionAssert.AreEqual(RootPackages, Packages(preparation.Command));
        Assert.Contains("--frozen", preparation.Command.Arguments);
        Assert.DoesNotContain("--only-group", preparation.Command.Arguments);
        Assert.DoesNotContain("--no-install-workspace", preparation.Command.Arguments);
        Assert.DoesNotContain("--no-editable", preparation.Command.Arguments);
        Assert.Contains(fixture.Request.Interpreter, preparation.Command.Arguments);
        Assert.AreEqual(Path.Combine(fixture.Scratch, "test-environment"),
            preparation.Command.Environment!["UV_PROJECT_ENVIRONMENT"]);
        Assert.IsNull(preparation.Command.Environment["PYTHONPATH"]);
        Assert.IsNull(preparation.Command.Environment["PYTEST_ADDOPTS"]);
        Assert.IsNull(preparation.Command.Environment["PYTEST_DISABLE_PLUGIN_AUTOLOAD"]);
        CollectionAssert.AreEqual(RootInvocation, result.Commands[1].Command.Arguments);
        CollectionAssert.AreEqual(new[] { selected.Checks[0].Work.Key },
            result.Commands[1].Checks);
        Assert.IsEmpty(result.Failures);
        NativeCommand query = Assert.ContainsSingle(fixture.Queries);
        CollectionAssert.AreEqual(new[] { "workspace", "metadata", "--frozen", "--python",
            fixture.Request.Interpreter }, query.Arguments);
        Assert.AreEqual("1", query.Environment!["UV_OFFLINE"]);
        Assert.IsNull(query.Environment.GetValueOrDefault("PYTEST_ADDOPTS"));
        Assert.AreEqual(Path.Combine(fixture.Scratch, "metadata-project-environment"),
            query.Environment["UV_PROJECT_ENVIRONMENT"]);
        Assert.IsFalse(Directory.Exists(query.Environment["UV_PROJECT_ENVIRONMENT"]));
    }

    [TestMethod]
    public async Task MixedTargetsPrepareOnlyRootAndSelectedNativeMembersOnce()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        CiPlan selected = fixture.Select("tests/root", "pkg-a/tests", "pkg-b/tests");
        PythonPytestRunResult result = await fixture.RunAsync(selected, context.CancellationToken);
        Assert.IsTrue(ResultCollector.Collect(selected, result.Results).Satisfied);
        PythonPytestCommand preparation = Assert.ContainsSingle(result.Commands.Where(command =>
            command.Command.Arguments[0] == "sync"));
        CollectionAssert.AreEquivalent(QualityPackages,
            Packages(preparation.Command));
        Assert.DoesNotContain("input-only", preparation.Command.Arguments);
        Assert.HasCount(3, preparation.Checks);
        CollectionAssert.AreEqual(selected.Checks.Select(item => item.Work.Key).ToArray(),
            result.Results.Select(item => item.Key).ToArray());
        CollectionAssert.AreEqual(QualityTargets,
            result.Commands.Where(item => item.Command.Arguments[0] == "run")
                .Select(item => item.Command.Arguments[^1]).ToArray());
    }

    [TestMethod]
    public async Task EmptyPartitionPerformsNoProductPreparation()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        CiPlan selected = fixture.Select();
        PythonPytestRunResult result = await fixture.RunAsync(selected, context.CancellationToken);
        Assert.IsTrue(ResultCollector.Collect(selected, result.Results).Satisfied);
        Assert.IsEmpty(result.Results);
        Assert.IsEmpty(result.Commands);
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    [DataRow("Exited", "Failed")]
    [DataRow("TimedOut", "TimedOut")]
    [DataRow("Cancelled", "Cancelled")]
    public async Task SharedPreparationFailureUnsatisfiesEveryOriginalTarget(
        string termination, string expected)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Failure = "sync";
        fixture.Termination = Enum.Parse<NativeTermination>(termination);
        PythonPytestRunResult result = await fixture.RunAsync(fixture.Plan,
            context.CancellationToken);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.HasCount(3, result.Results);
        Assert.IsTrue(result.Results.All(item => item.Status == Enum.Parse<CheckStatus>(expected) &&
            item.Candidate == fixture.Plan.Candidate));
        Assert.HasCount(3, result.Failures);
        Assert.HasCount(1, result.Commands);
        Assert.HasCount(1, fixture.Commands);
        Assert.HasCount(3, result.Commands[0].Checks);
    }

    [TestMethod]
    [DataRow("Exited", "Failed")]
    [DataRow("TimedOut", "TimedOut")]
    public async Task OneTargetFailurePreservesOtherRequiredOutcomes(
        string termination, string expected)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Failure = "tests/root";
        fixture.Termination = Enum.Parse<NativeTermination>(termination);
        PythonPytestRunResult result = await fixture.RunAsync(fixture.Plan,
            context.CancellationToken);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        CollectionAssert.AreEqual(new[] { Enum.Parse<CheckStatus>(expected),
                CheckStatus.Passed, CheckStatus.Passed },
            result.Results.Select(item => item.Status).ToArray());
        Assert.HasCount(4, fixture.Commands);
        Assert.AreEqual(fixture.Plan.Checks[0].Work.Key,
            Assert.ContainsSingle(result.Failures).Check);
    }

    [TestMethod]
    [DataRow("sync")]
    [DataRow("tests/root")]
    public async Task CancellationRetainsCompleteResultsAndStopsFurtherTargets(string cancelAt)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        fixture.After = command =>
        {
            if (command.Arguments[0] == cancelAt || command.Arguments[^1] == cancelAt)
                cancellation.Cancel();
        };
        PythonPytestRunResult result = await fixture.RunAsync(fixture.Plan, cancellation.Token);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.HasCount(3, result.Results);
        Assert.IsTrue(result.Results.All(item => item.Status == CheckStatus.Cancelled));
        Assert.HasCount(cancelAt == "sync" ? 1 : 2, fixture.Commands);
    }

    [TestMethod]
    [DataRow("candidate")]
    [DataRow("binding")]
    [DataRow("member")]
    [DataRow("root")]
    [DataRow("duplicate-name")]
    [DataRow("check")]
    [DataRow("variant")]
    [DataRow("runner")]
    [DataRow("dimension")]
    [DataRow("origin")]
    [DataRow("options")]
    [DataRow("dirty")]
    public async Task InvalidTransferStopsBeforeNativeProductEffects(string defect)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        PythonPytestRunRequest request = fixture.Request;
        CiPlan plan = fixture.Plan;
        if (defect == "candidate") request = request with
        { Targets = request.Targets with { Revision = "foreign" } };
        if (defect == "binding") request = request with { Targets = request.Targets with
        { Targets = [request.Targets.Targets[0] with { Inputs = [] },
            .. request.Targets.Targets[1..]] } };
        if (defect == "member") request = request with { Targets = request.Targets with
        { Targets = [request.Targets.Targets[0],
            request.Targets.Targets[1] with { Member = "pkg-b" },
            request.Targets.Targets[2]] } };
        if (defect == "root") request = request with { Members = request.Members[1..] };
        if (defect == "duplicate-name") request = request with { Members =
            [request.Members[0], request.Members[1] with { Name = request.Members[0].Name },
                .. request.Members[2..]] };
        if (defect is "check" or "variant" or "runner" or "dimension" or "origin")
        {
            PlannedCheck first = plan.Checks[0];
            if (defect == "check") first = first with
            { Work = first.Work with { Key = first.Work.Key with { Check = "unsupported" } } };
            if (defect == "variant") first = first with
            { Work = first.Work with { Key = first.Work.Key with { Variant = "foreign" } } };
            if (defect == "runner")
                first = first with { Work = first.Work with { Runner = "foreign" } };
            if (defect == "dimension") first = first with { Work = first.Work with
            { Dimensions = new(first.Work.Dimensions) { ["testPath"] = "pkg-a/tests" } } };
            if (defect == "origin") first = first with
            { Origins = [CheckOrigin.Preset], QualityPresets = ["foreign"] };
            plan = plan with { Checks = [first, .. plan.Checks[1..]] };
        }
        if (defect == "options") request = request with
        { Configuration = request.Configuration with { Options = ["--collect-only"] } };
        if (defect == "dirty") await fixture.Repo.SetAsync("pyproject.toml", "changed");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(plan, context.CancellationToken, request));
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MutableCandidateIsRejectedEvenForEmptyPartition(bool empty)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        CiPlan plan = (empty ? fixture.Select() : fixture.Plan) with { Candidate = "HEAD" };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(plan, context.CancellationToken));
        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    [DataRow("unique-name")]
    [DataRow("swap")]
    [DataRow("coordinated")]
    public async Task CandidateMembershipMismatchStopsBeforeProductPreparation(string defect)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        PythonPytestRunRequest request = fixture.Request;
        PythonMetadataMember[] members = request.Members.ToArray();
        if (defect == "unique-name") members[1] = members[1] with { Name = "other-unique" };
        if (defect == "swap")
        {
            members[1] = members[1] with { Name = request.Members[3].Name };
            members[3] = members[3] with { Name = request.Members[1].Name };
        }
        if (defect == "coordinated")
            request = request with
            {
                Members = [members[0], .. members[2..]],
                Targets = request.Targets with { Targets = [request.Targets.Targets[0],
                    request.Targets.Targets[1] with { Member = null,
                        Inputs = ["pkg-a/tests/test_a.py"] }, request.Targets.Targets[2]] },
            };
        else request = request with { Members = members };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(fixture.Plan, context.CancellationToken, request));
        Assert.ContainsSingle(fixture.Queries);
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    public async Task ReorderedMembershipUsesIdsFromReceivingNativeContext()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        PythonPytestRunRequest request = fixture.Request with
        { Members = fixture.Request.Members.Reverse().Select(member => member with
            { Id = "previous-context:" + member.Id }).ToArray() };
        PythonPytestRunResult result = await fixture.RunAsync(fixture.Plan,
            context.CancellationToken, request);
        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.ContainsSingle(fixture.Queries);
        CollectionAssert.AreEquivalent(QualityPackages, Packages(result.Commands[0].Command));
    }

    private static string[] Packages(NativeCommand command) => command.Arguments
        .Zip(command.Arguments.Skip(1)).Where(pair => pair.First == "--package")
        .Select(pair => pair.Second).ToArray();

    private sealed class Fixture(GitFixture repo, CiPlan plan, PythonPytestRunRequest request)
        : IDisposable
    {
        internal GitFixture Repo { get; } = repo;
        internal CiPlan Plan { get; } = plan;
        internal PythonPytestRunRequest Request { get; } = request;
        internal string Scratch => Request.Scratch;
        internal List<NativeCommand> Commands { get; } = [];
        internal List<NativeCommand> Queries { get; } = [];
        internal string? Failure { get; set; }
        internal NativeTermination Termination { get; set; } = NativeTermination.Exited;
        internal Action<NativeCommand>? After { get; set; }

        internal static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            GitFixture repo = await GitFixture.CreateAsync(token);
            string scratch = Directory.CreateTempSubdirectory("workflow-pytest-").FullName;
            try
            {
                await repo.SetAsync("pyproject.toml", """
                    [project]
                    name = "native-root"
                    [tool.pytest.ini_options]
                    testpaths = ["tests/root", "pkg-a/tests", "pkg-b/tests"]
                    addopts = ["--import-mode=importlib"]
                    """);
                foreach (string path in new[] { "tests/root/test_root.py", "pkg-a/tests/test_a.py",
                             "pkg-b/tests/test_b.py", "pkg-a/pyproject.toml",
                             "pkg-b/pyproject.toml",
                             "input/pyproject.toml" })
                    await repo.SetAsync(path, "controlled native input");
                string candidate = await repo.CommitAsync();
                await repo.GitAsync("reset", "--hard", candidate);
                GitRevision revision = await new GitReader(repo.Directory).ReadAsync(candidate,
                    token);
                PythonMetadataMember[] members = [new("native-root", ".", "root"),
                    new("native-a", "pkg-a", "a"), new("native-b", "pkg-b", "b"),
                    new("input-only", "input", "input")];
                var configuration = new PythonPytestConfiguration("pyproject.toml",
                    ["tests/root", "pkg-a/tests", "pkg-b/tests"], ["--import-mode=importlib"]);
                PythonPytestTargetSet targets = PythonPytestTargets.Bind(configuration, revision,
                    members.Select(member => new PythonProjectDependencies(member.Directory,
                        member.Name, [], [])).ToArray());
                var operation = new PythonPytestOperation("ubuntu-latest", "native", new());
                CheckSpec[] checks = PythonPytestChecks.Expand(configuration, targets, operation);
                var plan = new CiPlan(candidate, candidate, PythonFactsAssembler.Scope,
                    checks.Select(check => new PlannedCheck(check, [], [],
                        [CheckOrigin.NativeRetained])).ToArray());
                return new(repo, plan, new(repo.Directory, scratch, "controlled-uv",
                    Path.Combine(scratch, "python"), members, configuration, targets, operation, 30,
                    new Dictionary<string, string?> { ["PYTEST_ADDOPTS"] = "--collect-only" }));
            }
            catch { repo.Dispose(); Directory.Delete(scratch, true); throw; }
        }

        internal CiPlan Select(params string[] targets) => Plan with
        { Checks = Plan.Checks.Where(item => targets.Contains(item.Work.Dimensions["testPath"],
            StringComparer.Ordinal)).ToArray() };

        internal Task<PythonPytestRunResult> RunAsync(CiPlan plan, CancellationToken token,
            PythonPytestRunRequest? request = null) => PythonPytestExecution.RunAsync(plan,
                request ?? Request, ExecuteAsync, token);

        private Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (command.Arguments[0] == "workspace")
            {
                Queries.Add(command);
                return Task.FromResult(PythonExecutionMetadata.Result(Repo.Directory,
                    Request.Members));
            }
            Commands.Add(command);
            After?.Invoke(command);
            bool fail = command.Arguments[0] == Failure || command.Arguments[^1] == Failure;
            return Task.FromResult(new NativeCommandResult(
                fail ? Termination : NativeTermination.Exited,
                fail ? 1 : 0, "", fail ? "controlled failure" : "", 0.1, null));
        }

        public void Dispose()
        { Repo.Dispose(); Directory.Delete(Scratch, true); }
    }
}
