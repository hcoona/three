using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class PythonGroupExecutionTests(TestContext context)
{
    private static readonly string[] Targets = ["tests/root", "pkg-a/tests", "pkg-b/tests"];
    private static readonly string[] SelectedOwners = ["native-root", "native-a", "native-b"];
    private static readonly string[] PytestInvocation =
        ["python", "-m", "pytest", "-c", "pyproject.toml"];
    private static GitFixture? shared;
    private static GitRevision? revision;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            await repo.SetAsync("pyproject.toml", "controlled root pytest configuration");
            foreach (string target in Targets)
                await repo.SetAsync(target + "/test_example.py");
            await repo.SetAsync("pkg-a/pyproject.toml");
            await repo.SetAsync("pkg-b/pyproject.toml");
            await repo.SetAsync("input/pyproject.toml");
            await PythonPackageExecutionTests.WriteProductAsync(repo);
            string candidate = await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", candidate);
            revision = await new GitReader(repo.Directory).ReadAsync(candidate,
                classContext.CancellationToken);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    public async Task MixedPlanRunsSelectedPytestTargetsAndQualifiesBothDistributions()
    {
        using var fixture = new Fixture();

        PythonGroupRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsTrue(result.Outcome.Satisfied,
            string.Join("; ", result.Packages.Failures.Select(item => item.Error)));
        Assert.AreEqual(fixture.Plan.Candidate, result.Candidate);
        CollectionAssert.AreEquivalent(fixture.Plan.Checks.Select(item => item.Work.Key).ToArray(),
            result.Results.Select(item => item.Key).ToArray());
        Assert.HasCount(2, result.Packages.Distributions);
        NativeCommand sync = Assert.ContainsSingle(result.Pytest.Commands.Where(item =>
            item.Command.Arguments[0] == "sync")).Command;
        string[] owners = sync.Arguments.Zip(sync.Arguments.Skip(1))
            .Where(pair => pair.First == "--package").Select(pair => pair.Second).ToArray();
        CollectionAssert.AreEquivalent(SelectedOwners, owners);
        Assert.Contains("--frozen", sync.Arguments);
        Assert.DoesNotContain("--no-editable", sync.Arguments);
        AssertScrubbed(sync);
        string? environment = sync.Environment!["UV_PROJECT_ENVIRONMENT"];
        Assert.IsNotNull(environment);
        Assert.StartsWith(fixture.Scratch, environment);
        Assert.AreNotEqual("inherited-environment", environment);

        NativeCommand[] invocations = result.Pytest.Commands.Select(item => item.Command)
            .Where(command => command.Arguments[0] == "run").ToArray();
        CollectionAssert.AreEquivalent(Targets,
            invocations.Select(command => command.Arguments[^1]).ToArray());
        var reports = new HashSet<string>(StringComparer.Ordinal);
        const string reportFlag = "--junitxml=";
        foreach (NativeCommand invocation in invocations)
        {
            Assert.Contains("--no-project", invocation.Arguments);
            Assert.Contains(environment, invocation.Arguments);
            CollectionAssert.AreEqual(PytestInvocation, invocation.Arguments[4..9]);
            AssertScrubbed(invocation);
            Assert.IsNull(invocation.Environment!["UV_PROJECT_ENVIRONMENT"]);
            string report = Assert.ContainsSingle(invocation.Arguments.Where(argument =>
                argument.StartsWith(reportFlag, StringComparison.Ordinal)))[reportFlag.Length..];
            Assert.AreEqual(Path.Combine(fixture.Scratch, "pytest"), Path.GetDirectoryName(report));
            Assert.EndsWith(".xml", report);
            Assert.IsTrue(reports.Add(report));
        }
        NativeCommand membership = Assert.ContainsSingle(fixture.GroupMembershipQueries);
        Assert.AreEqual("1", membership.Environment!["PYTHONSAFEPATH"]);
        Assert.AreNotEqual(environment, membership.Environment["UV_PROJECT_ENVIRONMENT"]);
    }

    private static void AssertScrubbed(NativeCommand command)
    {
        Assert.IsNull(command.Environment!["PYTHONSAFEPATH"]);
        Assert.IsNull(command.Environment["PYTEST_ADDOPTS"]);
        Assert.IsNull(command.Environment["PYTEST_DISABLE_PLUGIN_AUTOLOAD"]);
    }

    [TestMethod]
    public async Task PytestSyncAndRunDoNotInheritTheRunnerEnvironment()
    {
        using var fixture = new Fixture();

        PythonGroupRunResult result = await fixture.RunAsync(fixture.PytestPlan,
            context.CancellationToken);

        Assert.IsTrue(result.Outcome.Satisfied);
        Assert.HasCount(Targets.Length + 1, result.Pytest.Commands);
        foreach (PythonPytestCommand command in result.Pytest.Commands)
            Assert.IsFalse(command.Command.InheritEnvironment);
    }

    [TestMethod]
    [DataRow(nameof(NativeTermination.Exited), nameof(CheckStatus.Failed))]
    [DataRow(nameof(NativeTermination.TimedOut), nameof(CheckStatus.TimedOut))]
    [DataRow(nameof(NativeTermination.Cancelled), nameof(CheckStatus.Cancelled))]
    public async Task SharedPreparationFailureIsAttributedToEveryPytestTarget(
        string termination, string expectedStatus)
    {
        using var fixture = new Fixture();
        fixture.PytestResponse = command => command.Arguments[0] == "sync" ?
            new(Enum.Parse<NativeTermination>(termination), 1, "", "controlled sync failure",
                0.1, null) : null;

        PythonGroupRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsFalse(result.Outcome.Satisfied);
        CheckStatus status = Enum.Parse<CheckStatus>(expectedStatus);
        Assert.IsTrue(result.Pytest.Results.All(item => item.Status == status));
        CollectionAssert.AreEquivalent(fixture.PytestKeys,
            result.Pytest.Failures.Select(item => item.Check).ToArray());
        foreach (PythonPytestFailure failure in result.Pytest.Failures)
        {
            Assert.Contains("Shared uv sync preparation failed", failure.Error);
            Assert.Contains("controlled sync failure", failure.Error);
        }
        Assert.IsFalse(result.Pytest.Commands.Any(item => item.Command.Arguments[0] == "run"));
        Assert.HasCount(2, result.Packages.Distributions);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedTargetDoesNotEraseOtherSelectedWork(bool timedOut)
    {
        using var fixture = new Fixture();
        NativeTermination termination = timedOut ? NativeTermination.TimedOut :
            NativeTermination.Exited;
        fixture.PytestResponse = command => command.Arguments[^1] == "tests/root"
            ? new(termination, timedOut ? null : 1, "", "pytest failed", 0.1, null) : null;

        PythonGroupRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsFalse(result.Outcome.Satisfied);
        CheckResult failed = Assert.ContainsSingle(result.Results.Where(item =>
            item.Status != CheckStatus.Passed));
        Assert.AreEqual("pytest:tests/root", failed.Key.Target);
        Assert.AreEqual(timedOut ? CheckStatus.TimedOut : CheckStatus.Failed, failed.Status);
        Assert.HasCount(fixture.Plan.Checks.Length, result.Results);
        Assert.HasCount(Targets.Length - 1, result.Pytest.Results.Where(item =>
            item.Status == CheckStatus.Passed));
        Assert.AreEqual(failed.Key, Assert.ContainsSingle(result.Pytest.Failures).Check);
    }

    [TestMethod]
    public async Task CancellationInPytestPreservesOriginalKeysAndStopsPackagePartition()
    {
        using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        fixture.PytestResponse = command =>
        {
            if (command.Arguments[0] == "run") cancellation.Cancel();
            return null;
        };

        PythonGroupRunResult result = await fixture.RunAsync(cancellation.Token);

        Assert.IsFalse(result.Outcome.Satisfied);
        CollectionAssert.AreEquivalent(fixture.Plan.Checks.Select(item => item.Work.Key).ToArray(),
            result.Results.Select(item => item.Key).ToArray());
        Assert.IsTrue(result.Results.All(item => item.Status == CheckStatus.Cancelled));
        Assert.ContainsSingle(result.Pytest.Commands.Where(item =>
            item.Command.Arguments[0] == "run"));
        Assert.IsEmpty(result.Packages.Commands);
    }

    [TestMethod]
    public void AlteredPytestTargetsMembershipAndOptionsAreRejected()
    {
        using var fixture = new Fixture();
        PythonPytestRunRequest native = fixture.Native;
        PythonPytestTarget[] targets = native.Targets.Targets;
        Reject(native with
        {
            Targets = native.Targets with
            {
                Targets = [targets[0] with { Inputs = [] },
                .. targets[1..]]
            },
        }, "target transfer differs");
        Reject(native with
        {
            Targets = native.Targets with
            {
                Targets = [targets[0], targets[1] with { Member = "pkg-b" }, targets[2]],
            },
        }, "target transfer differs");
        Reject(native with { Members = native.Members[1..] }, "Ambiguous native pytest membership");
        Reject(native with
        {
            Configuration = native.Configuration with { Options = ["--collect-only"] },
        }, "Unsupported supplied native pytest option");

        void Reject(PythonPytestRunRequest request, string diagnostic) =>
            Assert.Contains(diagnostic, Assert.ThrowsExactly<InvalidDataException>(() =>
                PythonPytestExecution.ReadChecks(fixture.PytestPlan, request, revision!)).Message);
    }

    [TestMethod]
    public void AlteredPytestRunnerDimensionsAndOriginAreRejected()
    {
        using var fixture = new Fixture();
        CiPlan plan = fixture.PytestPlan;
        PlannedCheck first = plan.Checks[0];
        PlannedCheck[] altered =
        [
            first with { Work = first.Work with { Runner = "foreign" } },
            first with { Work = first.Work with
                { Dimensions = new(first.Work.Dimensions) { ["testPath"] = "foreign" } } },
            first with { Origins = [CheckOrigin.Preset], QualityPresets = ["foreign"] },
        ];
        foreach (PlannedCheck check in altered)
        {
            var invalid = plan with { Checks = [check, .. plan.Checks[1..]] };
            Assert.Contains("Conflicting native pytest check contract",
                Assert.ThrowsExactly<InvalidDataException>(() => PythonPytestExecution
                    .ReadChecks(invalid, fixture.Native, revision!)).Message);
        }
    }

    [TestMethod]
    public async Task IncompleteDistributionPlanStopsBeforeEitherPartition()
    {
        using var fixture = new Fixture();
        CiPlan incomplete = fixture.Plan with { Checks = fixture.Plan.Checks[..^1] };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(incomplete, context.CancellationToken));

        Assert.IsEmpty(fixture.GroupMembershipQueries);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    public async Task PackageConsumersRejectScratchUnderADifferentGitCheckout()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Scratch, ".git"));
        string scratch = Path.Combine(fixture.Scratch, "external");
        var request = new PythonGroupRunRequest(fixture.Native with { Scratch = scratch },
            Path.Combine(fixture.Scratch, "helper.py"));

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            PythonGroupExecution.RunAsync(fixture.Plan, request, null, null,
                (_, _) => throw new AssertFailedException("No native preparation should run."),
                context.CancellationToken));

        Assert.Contains("Git-free scratch", failure.Message);
        Assert.IsFalse(Directory.Exists(scratch));
    }

    [TestMethod]
    public async Task MembershipMismatchStopsBeforeEitherPartition()
    {
        using var fixture = new Fixture();
        fixture.NativeMembers = fixture.NativeMembers[..^1];

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken));

        Assert.Contains("membership transfer differs", failure.Message);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    public async Task EmptyPlanDoesNotPrepareNativeWork()
    {
        using var fixture = new Fixture();

        PythonGroupRunResult result = await fixture.RunAsync(fixture.Plan with { Checks = [] },
            context.CancellationToken);

        Assert.IsTrue(result.Outcome.Satisfied);
        Assert.IsEmpty(result.Results);
        Assert.IsEmpty(fixture.GroupMembershipQueries);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PythonGroupRunRequest request;
        private readonly PythonPackageExecutionTests.PackageNative packageNative = new();

        internal Fixture()
        {
            PythonMetadataMember[] members = [new("native-root", ".", "root-id"),
                new("native-a", "pkg-a", "a-id"), new("native-b", "pkg-b", "b-id"),
                new("input-only", "input", "input-id"), .. PythonPackageChecksTests.Members];
            NativeMembers = members;
            var configuration = new PythonPytestConfiguration("pyproject.toml", Targets,
                ["--import-mode=importlib"]);
            var operation = new PythonPytestOperation("ubuntu-latest", "native", new());
            PythonProjectDependencies[] projects = members.Select(member =>
                new PythonProjectDependencies(member.Directory, member.Name, [], [])).ToArray();
            GitRevision candidate = revision!;
            PythonPytestTargetSet targets = PythonPytestTargets.Bind(configuration, candidate,
                projects);
            PlannedCheck[] pytest = PythonPytestChecks.Expand(configuration, targets, operation)
                .Select(check => new PlannedCheck(check, [], [], [CheckOrigin.NativeRetained]))
                .ToArray();
            PytestKeys = pytest.Select(item => item.Work.Key).ToArray();
            Plan = new(candidate.Commit, candidate.Commit, PythonFactsAssembler.Scope,
                [.. pytest, .. PythonPackageExecutionTests.PackageChecks()]);
            request = new(new(shared!.Directory, Scratch, "controlled-uv",
                Path.Combine(Scratch, "python"), members, configuration, targets, operation, 30,
                new Dictionary<string, string?>
                {
                    ["PYTEST_ADDOPTS"] = "--collect-only",
                    ["PYTEST_DISABLE_PLUGIN_AUTOLOAD"] = "1",
                    ["PYTHONSAFEPATH"] = "1",
                    ["UV_PROJECT_ENVIRONMENT"] = "inherited-environment",
                }), Path.Combine(Scratch, "helper.py"));
        }

        internal string Scratch { get; } =
            Directory.CreateTempSubdirectory("python-group-").FullName;
        internal CiPlan Plan { get; }
        internal CiPlan PytestPlan => Plan with { Checks = Plan.Checks[..PytestKeys.Length] };
        internal PythonPytestRunRequest Native => request.Native;
        internal CheckKey[] PytestKeys { get; }
        internal PythonMetadataMember[] NativeMembers { get; set; }
        internal List<NativeCommand> GroupMembershipQueries { get; } = [];
        internal Func<NativeCommand, NativeCommandResult?>? PytestResponse { get; set; }

        internal Task<PythonGroupRunResult> RunAsync(CancellationToken token) =>
            RunAsync(Plan, token);

        internal Task<PythonGroupRunResult> RunAsync(CiPlan plan, CancellationToken token) =>
            PythonGroupExecution.RunAsync(plan, request,
                (partition, native, cancellation) => PythonPytestExecution.RunAsync(
                    partition, native, RunPytestAsync, cancellation),
                (partition, native, cancellation) => PythonPackageExecution.RunAsync(
                    partition, native, packageNative.ExecuteAsync, cancellation),
                (command, _) =>
                {
                    GroupMembershipQueries.Add(command);
                    return Task.FromResult(Membership());
                }, token);

        private NativeCommandResult Membership() =>
            PythonExecutionMetadata.Result(request.Native.Checkout, NativeMembers);

        private Task<NativeCommandResult> RunPytestAsync(NativeCommand command,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (command.Arguments[0] == "workspace") return Task.FromResult(Membership());
            return Task.FromResult(PytestResponse?.Invoke(command) ??
                PythonPackageExecutionTests.PackageNative.Success());
        }

        public void Dispose() => Directory.Delete(Scratch, recursive: true);
    }
}
