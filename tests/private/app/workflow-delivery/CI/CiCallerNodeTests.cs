using WorkflowDelivery.CI;
using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class CiCallerNodeTests
{
    private static readonly string Candidate = new('a', 40);
    private static readonly string[] Runtimes = ["node22", "node24"];
    private static readonly string[] Operations = ["node/project-build-v1", "node/project-test-v1"];

    [TestMethod]
    public void JoinRequiresIndependentCompleteRuntimeResults()
    {
        NodeGroupReadback group = Group();
        NodeRunResult first = Result(group, "node22", CheckStatus.Passed);
        NodeRunResult second = Result(group, "node24", CheckStatus.Passed);
        var complete = NodeCiCaller.Join(group, [first, second]);
        Assert.IsTrue(complete.Outcome.Satisfied);
        Assert.HasCount(4, complete.Results);
        var missing = NodeCiCaller.Join(group, [first]);
        Assert.IsFalse(missing.Outcome.Satisfied);
        Assert.HasCount(2, missing.Outcome.Checks.Where(c => c.Status == "missing"));
        Assert.IsTrue(missing.Outcome.Checks.Where(c => c.Key.Variant == "node22").All(c =>
            c.Status == "passed"));
        var absent = NodeCiCaller.Join(group, []);
        Assert.IsFalse(absent.Outcome.Satisfied);
        Assert.IsTrue(absent.Outcome.Checks.All(c => c.Status == "missing"));
    }

    [TestMethod]
    [DataRow((int)CheckStatus.Failed)]
    [DataRow((int)CheckStatus.Cancelled)]
    [DataRow((int)CheckStatus.Skipped)]
    [DataRow((int)CheckStatus.TimedOut)]
    public void JoinOneAbnormalRuntimeCannotBorrowAnotherRuntimeSuccess(int value)
    {
        var status = (CheckStatus)value;
        NodeGroupReadback group = Group();
        var joined = NodeCiCaller.Join(group, [Result(group, "node22", CheckStatus.Passed),
            Result(group, "node24", status)]);
        Assert.IsFalse(joined.Outcome.Satisfied);
        Assert.AreEqual(status.ToString().ToLowerInvariant(), joined.Outcome.Checks
            .First(c => c.Key.Variant == "node24").Status);
    }

    [TestMethod]
    public void JoinRejectsSubstitutionUnknownRuntimeAndRepeatedResultSets()
    {
        NodeGroupReadback group = Group();
        NodeRunResult first = Result(group, "node22", CheckStatus.Passed);
        foreach (NodeRunResult bad in new[] { first with { Candidate = new('b', 40) },
                     first with { Runtime = "node24" }, first with { Runtime = "node99" },
                     first with { Results = [first.Results[0] with { Status = (CheckStatus)999
                         }] } })
            Assert.ThrowsExactly<InvalidDataException>(() => NodeCiCaller.Join(group, [bad]));
        Assert.ThrowsExactly<InvalidDataException>(() => NodeCiCaller.Join(group, [first, first]));
        Assert.ThrowsExactly<InvalidDataException>(() => NodeCiCaller.Join(group with
        { Runtime = group.Runtime with { Revision = new('b', 40) } }, [first]));
    }

    [TestMethod]
    public void JoinConflictingResultsCannotBecomeSuccess()
    {
        NodeGroupReadback group = Group();
        NodeRunResult first = Result(group, "node22", CheckStatus.Passed);
        first = first with
        {
            Results = [.. first.Results, first.Results[0] with { Status =
            CheckStatus.Failed }]
        };
        var joined = NodeCiCaller.Join(group, [first, Result(group, "node24", CheckStatus.Passed)]);
        Assert.IsFalse(joined.Outcome.Satisfied);
        Assert.Contains("conflicting", joined.Outcome.Checks.Select(c => c.Status));
    }

    [TestMethod]
    public void JoinEmptyPlanNeedsNoRuntimeResults()
    {
        NodeGroupReadback group = Group();
        group = group with { Adopted = group.Adopted with { Checks = [] } };
        Assert.IsTrue(NodeCiCaller.Join(group, []).Outcome.Satisfied);
    }

    private static NodeGroupReadback Group()
    {
        PlannedCheck[] checks = Runtimes.SelectMany(runtime =>
            Operations.Select(operation =>
                new PlannedCheck(new(new("src/node", operation, runtime),
                    new() { ["node"] = runtime[4..] + ".x", ["pnpm"] = "10.0.0" },
                    "ubuntu-latest", true, []), ["node/hcoona-release-smoke-npm-v1"], [],
                    [CheckOrigin.Preset]))).ToArray();
        return new(new(Candidate, Candidate, NodeFactsAssembler.Scope, checks), [], "src/node",
            new(Candidate, [new("node22", "22.x", "22.1.0"), new("node24", "24.x", null)],
                "10.0.0", ["mise.toml", ".github/workflows/ci.yml"], "node22"));
    }

    private static NodeRunResult Result(NodeGroupReadback group, string runtime, CheckStatus
        status) =>
        new(Candidate, NodeExecution.SelectRuntime(group.Adopted, runtime).Checks
            .Select(c => new CheckResult(Candidate, c.Work.Key, status)).ToArray(), [], [], [],
                runtime, null);
}
