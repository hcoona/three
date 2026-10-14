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
    public void OutcomeCollectsOriginalAbnormalRequiredStates(int value, bool satisfied)
    {
        var status = (CheckStatus)value;
        CiPlan plan = Plan();
        CheckResult[] results = [new(Candidate, plan.Checks[0].Work.Key, status)];
        CiOutcome reported = ResultCollector.Collect(plan, results);
        CiOutcome actual = CiCallerTransfer.Outcome(plan, results, reported);
        Assert.AreEqual(satisfied, actual.Satisfied);
        if (status == (CheckStatus)999) Assert.Contains("Unknown result status",
            actual.Errors.Single());
        else Assert.AreEqual(status.ToString().ToLowerInvariant(), actual.Checks.Single().Status);
        if (!satisfied) Assert.ThrowsExactly<InvalidDataException>(() => CiCallerTransfer.Outcome(
            plan, results, reported with { Satisfied = true }));
    }

    [TestMethod]
    public void OutcomeRejectsSubstitutedBindingAndFalseEmptySuccess()
    {
        CiPlan plan = Plan();
        CiOutcome missing = ResultCollector.Collect(plan, []);
        Assert.IsFalse(CiCallerTransfer.Outcome(plan, [], missing).Satisfied);
        Assert.AreEqual("missing", missing.Checks.Single().Status);
        foreach (CiOutcome substituted in new[] { missing with { Candidate = new('b', 40) },
                     missing with { Comparison = new('c', 40) }, missing with { Scope = "other" },
                     missing with { Checks = [] }, missing with { Errors = ["hidden"] } })
            Assert.ThrowsExactly<InvalidDataException>(() => CiCallerTransfer.Outcome(plan, [],
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
            commit, commit, context.CancellationToken, (original, request, _) =>
            {
                Assert.AreEqual(plan.Candidate, original.Candidate);
                Assert.AreEqual(plan.Checks[0].Work.Key, original.Checks[0].Work.Key);
                Assert.AreEqual(repo.Directory, request.Checkout);
                Assert.IsTrue(Directory.Exists(request.Scratch));
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(request.Scratch));
                return Task.FromResult(new DotNetGroupRunResult(commit, [], new(commit, [], [],
                    [], []),
                    new(commit, [], [], [], [], []), ResultCollector.Collect(original, [])));
            });
        Assert.IsFalse(outcome.Satisfied);
        Assert.AreEqual("missing", outcome.Checks.Single().Status);
        Assert.IsTrue(File.Exists(Path.Combine(directory, "result.json")));
        Assert.IsTrue(File.Exists(Path.Combine(directory, "request.json")));
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
        [new(new(new("src/project.csproj", "dotnet/build-v1", "Debug"), [], "windows-latest",
            true, []),
            [], [], [CheckOrigin.NativeRetained])]);
}
