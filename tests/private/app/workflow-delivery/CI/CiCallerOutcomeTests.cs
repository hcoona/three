using WorkflowDelivery.CI;
using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class CiCallerOutcomeTests(TestContext context)
{
    private static readonly string Candidate = new('a', 40);

    [TestMethod]
    [DataRow((int)CheckStatus.Passed, true)]
    [DataRow((int)CheckStatus.Failed, false)]
    [DataRow((int)CheckStatus.Cancelled, false)]
    [DataRow((int)CheckStatus.TimedOut, false)]
    [DataRow((int)CheckStatus.Skipped, false)]
    [DataRow(999, false)]
    public void OutcomeForwardsExecutionOwnedRequiredStates(int value, bool satisfied)
    {
        var status = (CheckStatus)value;
        CiPlan plan = Plan();
        CheckResult[] results = [new(Candidate, plan.Checks[0].Work.Key, status)];
        CiOutcome reported = ResultCollector.Collect(plan, results);
        CiOutcome actual = CiCallerTransfer.Outcome(plan, reported);
        Assert.AreSame(reported, actual);
        Assert.AreEqual(satisfied, actual.Satisfied);
        if (status == (CheckStatus)999) Assert.Contains("Unknown result status",
            actual.Errors.Single());
        else Assert.AreEqual(status.ToString().ToLowerInvariant(), actual.Checks.Single().Status);
    }

    [TestMethod]
    public void OutcomeRejectsMissingShapeAndSubstitutedBinding()
    {
        CiPlan plan = Plan();
        CiOutcome missing = ResultCollector.Collect(plan, []);
        Assert.IsFalse(CiCallerTransfer.Outcome(plan, missing).Satisfied);
        Assert.AreEqual("missing", missing.Checks.Single().Status);
        foreach (CiOutcome substituted in new[] { missing with { Candidate = new('b', 40) },
                     missing with { Comparison = new('c', 40) }, missing with { Scope = "other" },
                     missing with { Checks = null! }, missing with { Errors = null! }, null! })
            Assert.ThrowsExactly<InvalidDataException>(() => CiCallerTransfer.Outcome(plan,
                substituted));
        Assert.ThrowsExactly<InvalidDataException>(() => CiCallerTransfer.Plan(plan,
            Candidate, Candidate, "other"));
    }

    [TestMethod]
    public async Task DotNetExecuteTransfersOriginalPlanAndRetainsMissingResultAsFailure()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("source.txt");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        using var files = new CiCallerFiles();
        string transfer = files.Transfer();
        CiPlan plan = Plan() with { Comparison = commit, Candidate = commit };
        CiCallerTransfer.Write(Path.Combine(transfer, "dotnet-plan.json"), plan,
            TransferJson.Default.CiPlan);
        string directory = Path.Combine(files.Root, "execution-state");
        CiOutcome outcome = await DotNetCiCaller.ExecuteAsync(repo.Directory, transfer, directory,
            commit, commit, DotNetTools.None, context.CancellationToken,
            (original, request, _) =>
            {
                Assert.AreEqual(plan.Candidate, original.Candidate);
                Assert.AreEqual(plan.Checks[0].Work.Key, original.Checks[0].Work.Key);
                Assert.AreEqual(repo.Directory, request.Checkout);
                Assert.IsTrue(Directory.Exists(request.Scratch));
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(request.Scratch));
                return Task.FromResult(new DotNetGroupRunResult(commit, [], new(commit, [], [], []),
                    new(commit, [], [], [], [], []), ResultCollector.Collect(original, [])));
            });
        Assert.IsFalse(outcome.Satisfied);
        Assert.AreEqual("missing", outcome.Checks.Single().Status);
        Assert.IsTrue(File.Exists(Path.Combine(directory, "result.json")));
        Assert.IsTrue(File.Exists(Path.Combine(directory, "request.json")));
        Assert.AreEqual(DotNetTools.None, CiCallerTransfer.Read(Path.Combine(directory,
            DotNetCiCaller.GroupResults), TransferJson.Default.DotNetGroupResultSet).Group);
        Assert.AreEqual(commit, CiCallerTransfer.Read(Path.Combine(directory, "outcome.json"),
            TransferJson.Default.CiOutcome).Candidate);
    }

    [TestMethod]
    public void DotNetPreparePreservesFullEndpointReuseAndNativeAssemblyIdentity()
    {
        using var source = new CiCallerFiles();
        using var state = new CiCallerFiles();
        string transfer = state.Transfer();
        string directory = Path.Combine(state.Root, "dotnet");
        DotNetCiCaller.Bootstrap(source.Root, directory, transfer);
        var endpoint = new MaterializedEndpoint(source.Root, Candidate);
        string endpoints = Path.Combine(state.Root, "endpoints.json");
        CiCallerTransfer.Write(endpoints, new CiCallerEndpoints(endpoint, endpoint, true),
            TransferJson.Default.CiCallerEndpoints);
        string path = DotNetCiCaller.Prepare(source.Root, directory, endpoints);
        DotNetGroupRequest request = CiCallerTransfer.Read(path,
            TransferJson.Default.DotNetGroupRequest);
        Assert.AreEqual(request.CandidateFiles, request.BasisFiles);
        Assert.AreEqual(CiCallerTransfer.Application(transfer), request.CandidateFiles.Assembly);
        Assert.AreEqual(endpoint, request.Candidate);
        Assert.AreEqual(source.Root, request.Completion.Checkout);
        Assert.IsTrue(request.Full);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(request.Completion.Scratch));
    }

    private static CiPlan Plan() => new(Candidate, Candidate, DotNetCiCaller.Scope,
        [Check("src/project.csproj", "{}")]);

    private static PlannedCheck Check(string project, string tools) =>
        new(new(new(project, "dotnet/build-v1", "Debug"),
            new() { [DotNetTools.Dimension] = tools }, "windows-latest", true, []),
            [], [], [CheckOrigin.NativeRetained]);

    private static CiPlan Grouped() => new(Candidate, Candidate, DotNetCiCaller.Scope,
        [Check("src/plain.csproj", "{}"), Check("src/node.csproj", "{\"node\":\"24.1.0\"}")]);

    private static DotNetGroupResultSet Set(string group, params (int Check,
        CheckStatus Status)[] results) => new(group, Candidate, [.. results.Select(result =>
            new CheckResult(Candidate, Grouped().Checks[result.Check].Work.Key,
                result.Status))]);

    [TestMethod]
    public void DotNetJoinCollectsEveryPlannedGroupAndKeepsMissingGroupsMissing()
    {
        CiPlan plan = Grouped();
        Assert.AreEqual("node+none", string.Join('+',
            DotNetTools.Groups(plan).Select(group => group.Key)));
        Assert.AreEqual("src/node.csproj",
            DotNetTools.Select(plan, "node").Checks.Single().Work.Key.Target);
        Assert.IsTrue(DotNetCiCaller.Join(plan, new Dictionary<string, DotNetGroupResultSet>
        {
            ["none"] = Set("none", (0, CheckStatus.Passed)),
            ["node"] = Set("node", (1, CheckStatus.Passed)),
        }).Outcome.Satisfied);
        CiOutcome missing = DotNetCiCaller.Join(plan,
            new Dictionary<string, DotNetGroupResultSet>
            { ["none"] = Set("none", (0, CheckStatus.Passed)) }).Outcome;
        Assert.IsFalse(missing.Satisfied);
        Assert.AreEqual("missing,passed", string.Join(',',
            missing.Checks.Select(check => check.Status).Order(StringComparer.Ordinal)));
        foreach (CheckStatus status in new[] { CheckStatus.Failed, CheckStatus.Cancelled })
            Assert.IsFalse(DotNetCiCaller.Join(plan,
                new Dictionary<string, DotNetGroupResultSet>
                {
                    ["none"] = Set("none", (0, CheckStatus.Passed)),
                    ["node"] = Set("node", (1, status)),
                }).Outcome.Satisfied);
        CiPlan toolOnly = new(Candidate, Candidate, DotNetCiCaller.Scope, [plan.Checks[1]]);
        CiOutcome silent = DotNetCiCaller.Join(toolOnly,
            new Dictionary<string, DotNetGroupResultSet>
            { ["node"] = Set("node", (1, CheckStatus.Passed)) }).Outcome;
        Assert.IsFalse(silent.Satisfied);
        Assert.Contains(".NET group reported no result set: none", silent.Errors);
        Assert.IsFalse(DotNetCiCaller.Join(new(Candidate, Candidate, DotNetCiCaller.Scope, []),
            new Dictionary<string, DotNetGroupResultSet>()).Outcome.Satisfied);
    }

    [TestMethod]
    public void DotNetJoinRejectsExtraDuplicatedMisplacedAndUnplannedResults()
    {
        CiPlan plan = Grouped();
        foreach (Dictionary<string, DotNetGroupResultSet> sets in new[]
                 {
                     new Dictionary<string, DotNetGroupResultSet>
                     { ["none"] = Set("none", (0, CheckStatus.Passed), (1, CheckStatus.Passed)) },
                     new Dictionary<string, DotNetGroupResultSet>
                     { ["none"] = Set("none", (0, CheckStatus.Passed), (0, CheckStatus.Passed)) },
                     new Dictionary<string, DotNetGroupResultSet>
                     { ["none"] = Set("none", (1, CheckStatus.Passed)) },
                     new Dictionary<string, DotNetGroupResultSet>
                     { ["node"] = Set("none", (1, CheckStatus.Passed)) },
                     new Dictionary<string, DotNetGroupResultSet>
                     { ["python"] = Set("python") },
                 })
            Assert.ThrowsExactly<InvalidDataException>(() => DotNetCiCaller.Join(plan, sets));
    }

    [TestMethod]
    public void DotNetJoinReadsGroupArtifactsAndRejectsStrayEntries()
    {
        using var files = new CiCallerFiles();
        string transfer = files.Transfer();
        CiCallerTransfer.Write(Path.Combine(transfer, "dotnet-plan.json"), Grouped(),
            TransferJson.Default.CiPlan);
        string results = Path.Combine(files.Root, "group-results");
        string group = Directory.CreateDirectory(Path.Combine(results, "none")).FullName;
        CiCallerTransfer.Write(Path.Combine(group, DotNetCiCaller.GroupResults),
            Set("none", (0, CheckStatus.Passed)), TransferJson.Default.DotNetGroupResultSet);
        Directory.CreateDirectory(Path.Combine(results, "node"));
        string directory = Path.Combine(files.Root, "join");
        string root = Directory.CreateDirectory(Path.Combine(files.Root, "checkout")).FullName;
        CiOutcome outcome = DotNetCiCaller.Join(root, transfer, results, directory,
            Candidate, Candidate);
        Assert.IsFalse(outcome.Satisfied);
        Assert.AreEqual(1, outcome.Checks.Count(check => check.Status == "missing"));
        Assert.IsTrue(File.Exists(Path.Combine(directory, "outcome.json")));
        File.WriteAllText(Path.Combine(results, "stray.json"), "{}");
        Assert.ThrowsExactly<InvalidDataException>(() => DotNetCiCaller.Join(root,
            transfer, results, Path.Combine(files.Root, "join-again"), Candidate, Candidate));
    }
}
