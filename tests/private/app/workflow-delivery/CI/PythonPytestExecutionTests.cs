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

    // Membership and candidate-reference rows read one class-owned committed checkout;
    // each row owns fresh scratch and runs the complete execution entry point.
    private static GitFixture? shared;
    private static GitRevision? sharedRevision;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            sharedRevision = await Fixture.CommitAsync(repo, classContext.CancellationToken);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    public async Task SharedCheckoutQualifiesEveryTarget()
    {
        // Defect-free control: the shared checkout runs every selected target.
        using var fixture = Fixture.Shared(shared!, sharedRevision!);
        PythonPytestRunResult result = await fixture.RunAsync(fixture.Plan,
            context.CancellationToken);
        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.ContainsSingle(fixture.Queries);
        Assert.HasCount(4, fixture.Commands);
    }

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
        Assert.IsNull(preparation.Command.Environment["PYTHONSAFEPATH"]);
        Assert.IsNull(preparation.Command.Environment["PYTEST_ADDOPTS"]);
        Assert.IsNull(preparation.Command.Environment["PYTEST_DISABLE_PLUGIN_AUTOLOAD"]);
        NativeCommand invocation = result.Commands[1].Command;
        string junit = JunitOutput(invocation);
        Assert.IsTrue(Path.IsPathFullyQualified(junit));
        Assert.AreEqual(fixture.Scratch, Path.GetDirectoryName(junit));
        Assert.EndsWith(".xml", junit);
        Assert.IsNull(invocation.Environment!["UV_PROJECT_ENVIRONMENT"]);
        Assert.IsNull(invocation.Environment["PYTHONSAFEPATH"]);
        Assert.AreEqual("false", invocation.Environment["MISE_TASK_RUN_AUTO_INSTALL"]);
        Assert.AreEqual("0", invocation.Environment["MISE_EXEC_AUTO_INSTALL"]);
        string[] expectedInvocation = ["run", "--no-project", "--python",
            Path.Combine(fixture.Scratch, "test-environment"), "python", "-m", "pytest",
            "-c", "pyproject.toml", "--junitxml=" + junit, "tests/root"];
        CollectionAssert.AreEqual(expectedInvocation, invocation.Arguments);
        CollectionAssert.AreEqual(new[] { selected.Checks[0].Work.Key },
            result.Commands[1].Checks);
        Assert.IsEmpty(result.Failures);
        NativeCommand query = Assert.ContainsSingle(fixture.Queries);
        CollectionAssert.AreEqual(new[] { "workspace", "metadata", "--frozen", "--python",
            fixture.Request.Interpreter }, query.Arguments);
        Assert.AreEqual("1", query.Environment!["UV_OFFLINE"]);
        Assert.AreEqual("1", query.Environment["PYTHONSAFEPATH"]);
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
        string[] reports = result.Commands.Where(item => item.Command.Arguments[0] == "run")
            .Select(item => JunitOutput(item.Command)).ToArray();
        Assert.HasCount(QualityTargets.Length, reports.Distinct(StringComparer.Ordinal));
        foreach (string report in reports)
        {
            Assert.IsTrue(Path.IsPathFullyQualified(report));
            Assert.AreEqual(fixture.Scratch, Path.GetDirectoryName(report));
            Assert.EndsWith(".xml", report);
        }
        foreach (PythonPytestCommand command in result.Commands.Where(item =>
                     item.Command.Arguments[0] == "run"))
        {
            Assert.IsNull(command.Command.Environment!["UV_PROJECT_ENVIRONMENT"]);
            Assert.IsNull(command.Command.Environment["PYTHONSAFEPATH"]);
            Assert.AreEqual("false", command.Command.Environment["MISE_TASK_RUN_AUTO_INSTALL"]);
            Assert.AreEqual("0", command.Command.Environment["MISE_EXEC_AUTO_INSTALL"]);
            Assert.Contains(Path.Combine(fixture.Scratch, "test-environment"),
                command.Command.Arguments);
        }
    }

    [TestMethod]
    public async Task EmptyPartitionPerformsNoProductPreparation()
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision!);
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
        using var fixture = Fixture.Shared(shared!, sharedRevision!);
        fixture.Failure = "sync";
        fixture.Termination = Enum.Parse<NativeTermination>(termination);
        PythonPytestRunResult result = await fixture.RunAsync(fixture.Plan,
            context.CancellationToken);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.HasCount(3, result.Results);
        Assert.IsTrue(result.Results.All(item => item.Status == Enum.Parse<CheckStatus>(expected) &&
            item.Candidate == fixture.Plan.Candidate));
        Assert.HasCount(3, result.Failures);
        // The stage, native termination and controlled stderr reach every failure text.
        foreach (PythonPytestFailure failure in result.Failures)
            Assert.AreEqual($"Shared uv sync preparation failed: {termination}, exit 1. " +
                "stderr: controlled failure", failure.Error);
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
        using var fixture = Fixture.Shared(shared!, sharedRevision!);
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
        using var fixture = Fixture.Shared(shared!, sharedRevision!);
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
    [DataRow("binding")]
    [DataRow("")]
    [DataRow("member")]
    [DataRow("root")]
    [DataRow("duplicate-name")]
    [DataRow("check")]
    [DataRow("variant")]
    [DataRow("runner")]
    [DataRow("dimension")]
    [DataRow("origin")]
    [DataRow("options")]
    public void InvalidTransferContractIsRejected(string defect)
    {
        // The transfer contract is a pure check over the plan, request and revision; the
        // end-to-end rows below establish that execution applies it before native effects.
        (CiPlan plan, PythonPytestRunRequest request, GitRevision revision) =
            Fixture.Contract(Fixture.Revision("0123456789abcdef0123456789abcdef01234567"),
                "/checkout", "/scratch");
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
        if (defect.Length == 0)
            PythonPytestExecution.ReadChecks(plan, request, revision);
        else
            Assert.ThrowsExactly<InvalidDataException>(() =>
                PythonPytestExecution.ReadChecks(plan, request, revision));
    }

    [TestMethod]
    [DataRow("candidate")]
    [DataRow("check")]
    [DataRow("dirty")]
    public async Task InvalidTransferStopsBeforeNativeProductEffects(string defect)
    {
        // Only the dirty row changes the checkout, so only it keeps a private repository.
        using var fixture = defect == "dirty"
            ? await Fixture.CreateAsync(context.CancellationToken)
            : Fixture.Shared(shared!, sharedRevision!);
        PythonPytestRunRequest request = fixture.Request;
        CiPlan plan = fixture.Plan;
        if (defect == "candidate") request = request with
        { Targets = request.Targets with { Revision = "foreign" } };
        if (defect == "check") plan = plan with { Checks = [plan.Checks[0] with { Work =
            plan.Checks[0].Work with { Key = plan.Checks[0].Work.Key with
            { Check = "unsupported" } } }, .. plan.Checks[1..]] };
        if (defect == "dirty")
        {
            await fixture.Repo.SetAsync("pyproject.toml", "changed");
            await fixture.Repo.FlushAsync();
        }
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
        using var fixture = Fixture.Shared(shared!, sharedRevision!);
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
        using var fixture = Fixture.Shared(shared!, sharedRevision!);
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

    private static string JunitOutput(NativeCommand command)
    {
        const string prefix = "--junitxml=";
        return Assert.ContainsSingle(command.Arguments.Where(argument =>
            argument.StartsWith(prefix, StringComparison.Ordinal)))[prefix.Length..];
    }

    private sealed class Fixture(GitFixture repo, CiPlan plan, PythonPytestRunRequest request,
        bool ownsRepository = true) : IDisposable
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
                GitRevision revision = await CommitAsync(repo, token);
                (CiPlan plan, PythonPytestRunRequest request, _) =
                    Contract(revision, repo.Directory, scratch);
                return new(repo, plan, request);
            }
            catch { repo.Dispose(); Directory.Delete(scratch, true); throw; }
        }

        // Reads a class-owned committed checkout; the row owns only its scratch.
        internal static Fixture Shared(GitFixture repo, GitRevision revision)
        {
            string scratch = Directory.CreateTempSubdirectory("workflow-pytest-").FullName;
            (CiPlan plan, PythonPytestRunRequest request, _) =
                Contract(revision, repo.Directory, scratch);
            return new(repo, plan, request, ownsRepository: false);
        }

        internal static async Task<GitRevision> CommitAsync(GitFixture repo,
            CancellationToken token)
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
            return await new GitReader(repo.Directory).ReadAsync(candidate, token);
        }

        private static readonly string[] Layout = ["pyproject.toml", "tests/root/test_root.py",
            "pkg-a/tests/test_a.py", "pkg-b/tests/test_b.py", "pkg-a/pyproject.toml",
            "pkg-b/pyproject.toml", "input/pyproject.toml"];

        // Same committed layout as CreateAsync, for contract checks that need no checkout.
        internal static GitRevision Revision(string commit) => new(commit,
            Layout.Select(path => new GitEntry(path, "100644", "blob")).ToArray());

        internal static (CiPlan Plan, PythonPytestRunRequest Request, GitRevision Revision)
            Contract(GitRevision revision, string checkout, string scratch)
        {
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
            var plan = new CiPlan(revision.Commit, revision.Commit, PythonFactsAssembler.Scope,
                checks.Select(check => new PlannedCheck(check, [], [],
                    [CheckOrigin.NativeRetained])).ToArray());
            return (plan, new(checkout, scratch, "controlled-uv",
                Path.Combine(scratch, "python"), members, configuration, targets, operation, 30,
                new Dictionary<string, string?> { ["PYTEST_ADDOPTS"] = "--collect-only",
                    ["PYTHONSAFEPATH"] = "1",
                    ["MISE_TASK_RUN_AUTO_INSTALL"] = "false",
                    ["MISE_EXEC_AUTO_INSTALL"] = "0",
                    ["UV_PROJECT_ENVIRONMENT"] = "query-environment" }), revision);
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
        {
            if (ownsRepository) Repo.Dispose();
            Directory.Delete(Scratch, true);
        }
    }
}
