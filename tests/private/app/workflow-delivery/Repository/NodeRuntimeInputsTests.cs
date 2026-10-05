using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NodeRuntimeInputsTests(TestContext context)
{
    private const string Source = "packages/adopted";
    private const string Workflow = "jobs:\n  node-tests:\n    strategy:\n      matrix:\n"
        + "        node-version: [24.x, 22.x]\n";
    private static readonly string[] SourcePaths =
        [NodeRuntimeReader.Workflow, "mise.toml", "mise.lock"];
    private static readonly string[] NativeCommands =
        ["ls --json --current --locked node", "ls --json --current --locked pnpm"];

    [TestMethod]
    public async Task ReadUsesCommittedMatrixAndNativeMiseToolIdentities()
    {
        using var repo = await RuntimeCheckoutAsync();
        GitMaterialization checkout = await BindAsync(repo);
        var commands = new List<string>();
        NodeRuntimeInputs inputs = await new NodeRuntimeReader(checkout, (args, _) =>
        {
            commands.Add(string.Join(' ', args));
            return Task.FromResult(Tool(repo.Directory, args[^1]));
        }).ReadAsync(context.CancellationToken);

        Assert.AreEqual(checkout.Revision.Commit, inputs.Revision);
        CollectionAssert.AreEqual(new[] { new NodeRuntimeVariant("node24", "24.x", "24.21.0"),
            new NodeRuntimeVariant("node22", "22.x", null) }, inputs.Variants);
        Assert.AreEqual("12.8.2", inputs.PnpmVersion);
        CollectionAssert.AreEqual(SourcePaths, inputs.Sources);
        CollectionAssert.AreEqual(NativeCommands, commands);
    }

    [TestMethod]
    public async Task RuntimeProjectionPreservesSevenObligationsAndOriginalSubject()
    {
        CiPlan original = await PackagePlanAsync();
        CiPlan plan = Inputs(original.Candidate).Expand(original);

        Assert.AreEqual(original.Comparison, plan.Comparison);
        Assert.AreEqual(original.Candidate, plan.Candidate);
        Assert.AreEqual(original.Scope, plan.Scope);
        Assert.HasCount(7, plan.Checks);
        Assert.HasCount(2, plan.Checks.Where(c => c.Work.Key.Variant == "node22"));
        Assert.HasCount(5, plan.Checks.Where(c => c.Work.Key.Variant == "node24"));
        foreach (PlannedCheck check in plan.Checks)
        {
            PlannedCheck source = original.Checks.Single(c =>
                c.Work.Key.Target == check.Work.Key.Target &&
                c.Work.Key.Check == check.Work.Key.Check);
            CollectionAssert.AreEqual(source.Reasons, check.Reasons);
            CollectionAssert.AreEqual(source.QualityPresets, check.QualityPresets);
            Assert.AreSame(source.Work.Package, check.Work.Package);
            Assert.AreEqual("12.8.2", check.Work.Dimensions["pnpm"]);
            Assert.AreEqual(check.Work.Key.Variant == "node22" ? "22.x" : "24.x",
                check.Work.Dimensions["node"]);
            Assert.AreEqual(check.Work.Key.Variant == "node24",
                check.Work.Dimensions.ContainsKey("node-version"));
            if (check.Work.Key.Variant == "node24")
                Assert.AreEqual("24.21.0", check.Work.Dimensions["node-version"]);
            CollectionAssert.AreEqual(source.Work.Prerequisites.Select(p =>
                p with { Variant = check.Work.Key.Variant }).ToArray(), check.Work.Prerequisites);
        }
        foreach (PlannedCheck test in plan.Checks.Where(c =>
            c.Work.Key.Check == "node/project-test-v1"))
            Assert.IsEmpty(test.Work.Prerequisites);
        PlannedCheck pack = plan.Checks.Single(c => c.Work.Key.Check == "node/npm-artifact-v1");
        Assert.AreEqual(new CheckKey(Source, "node/project-build-v1", "node24"),
            Assert.ContainsSingle(pack.Work.Prerequisites));
        foreach (PlannedCheck check in plan.Checks.Where(c =>
            c.Work.Key.Check is "node/npm-artifact-contents-v1" or "node/npm-install-import-v1"))
            Assert.AreEqual(pack.Work.Key, Assert.ContainsSingle(check.Work.Prerequisites));
        CiOutcome outcome = ResultCollector.Collect(plan, plan.Checks.Select(c =>
            new CheckResult(plan.Candidate, c.Work.Key, CheckStatus.Passed)).ToArray());
        Assert.IsTrue(outcome.Satisfied);
        Assert.HasCount(7, outcome.Checks);
        Assert.IsEmpty(outcome.Errors);
    }

    [TestMethod]
    public void EmptyProjectionKeepsTheComparisonAndCandidate()
    {
        var original = new CiPlan(NodeScenario.Basis, NodeScenario.Candidate,
            NodeFactsAssembler.Scope, []);

        CiPlan plan = Inputs(original.Candidate).Expand(original);

        Assert.AreEqual(original, plan with { Checks = original.Checks });
        Assert.IsEmpty(plan.Checks);
        Assert.IsTrue(ResultCollector.Collect(plan, []).Satisfied);
    }

    [TestMethod]
    [DataRow("missing-runtime")]
    [DataRow("duplicate-runtime")]
    [DataRow("unsupported-runtime")]
    [DataRow("matrix-include")]
    [DataRow("duplicate-yaml-key")]
    [DataRow("missing-workflow")]
    [DataRow("missing-mise")]
    [DataRow("missing-lock")]
    [DataRow("tool-source")]
    [DataRow("tool-inactive")]
    [DataRow("node-request")]
    [DataRow("node-version")]
    [DataRow("pnpm-floating")]
    [DataRow("duplicate-tool")]
    public async Task UnsupportedRuntimeFactsCannotBecomeAPlan(string defect)
    {
        string workflow = defect switch
        {
            "missing-runtime" => Workflow.Replace("[24.x, 22.x]", "[24.x]"),
            "duplicate-runtime" => Workflow.Replace("[24.x, 22.x]", "[24.x, 24.x]"),
            "unsupported-runtime" => Workflow.Replace("22.x", "26.x"),
            "matrix-include" => Workflow + "        include: []\n",
            "duplicate-yaml-key" => Workflow + "        node-version: [24.x, 22.x]\n",
            _ => Workflow,
        };
        using var repo = await RuntimeCheckoutAsync(workflow, defect);
        GitMaterialization checkout = await BindAsync(repo);
        var reader = new NodeRuntimeReader(checkout, (args, _) =>
            Task.FromResult(Tool(repo.Directory, args[^1], defect)));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadAsync(context.CancellationToken));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("failed")]
    [DataRow("cancelled")]
    [DataRow("timedout")]
    [DataRow("skipped")]
    [DataRow("conflicting")]
    public async Task CollectorRequiresEveryOriginalRuntimeResult(string state)
    {
        CiPlan original = await PackagePlanAsync();
        CiPlan plan = Inputs(original.Candidate).Expand(original);
        CheckKey key = new(Source, "node/project-test-v1", "node22");
        var results = plan.Checks.Select(c =>
            new CheckResult(plan.Candidate, c.Work.Key, CheckStatus.Passed)).ToList();
        CheckResult passed = results.Single(r => r.Key == key);
        if (state != "conflicting") results.Remove(passed);
        if (state != "missing")
        {
            CheckStatus status = state switch
            {
                "cancelled" => CheckStatus.Cancelled,
                "timedout" => CheckStatus.TimedOut,
                "skipped" => CheckStatus.Skipped,
                _ => CheckStatus.Failed,
            };
            results.Add(passed with { Status = status });
        }

        CiOutcome outcome = ResultCollector.Collect(plan, [.. results]);

        Assert.IsFalse(outcome.Satisfied);
        Assert.HasCount(7, outcome.Checks);
        Assert.AreEqual(state, outcome.Checks.Single(c => c.Key == key).Status);
        Assert.HasCount(6, outcome.Checks.Where(c => c.Status == "passed"));
    }

    [TestMethod]
    [DataRow("candidate")]
    [DataRow("scope")]
    [DataRow("variant")]
    [DataRow("check")]
    public async Task ProjectionRejectsAnUnsupportedParentPlan(string defect)
    {
        CiPlan plan = await PackagePlanAsync();
        plan = defect switch
        {
            "candidate" => plan with { Candidate = NodeScenario.Basis },
            "scope" => plan with { Scope = "unknown" },
            _ => plan with
            {
                Checks = plan.Checks.Select(c => c with
                {
                    Work = c.Work with
                    {
                        Key = c.Work.Key with
                        {
                            Variant = defect == "variant" ? "other" : c.Work.Key.Variant,
                            Check = defect == "check" ? "unknown" : c.Work.Key.Check,
                        }
                    },
                }).ToArray()
            },
        };

        Assert.ThrowsExactly<InvalidDataException>(() =>
            Inputs(NodeScenario.Candidate).Expand(plan));
    }

    private static NodeRuntimeInputs Inputs(string revision) => new(revision,
        [new("node22", "22.x", null), new("node24", "24.x", "24.21.0")], "12.8.2",
        [NodeRuntimeReader.Workflow, "mise.toml", "mise.lock"]);

    private static Task<CiPlan> PackagePlanAsync()
    {
        NodeRevisionInputs At(string revision) => NodeScenario.Units(NodeScenario.Inputs(revision,
            NodeScenario.Project(Source)), NodeScenario.Unit("fixture", ("npm", Source)));
        return NodeScenario.Plan(At(NodeScenario.Basis), At(NodeScenario.Candidate),
            Source + "/index.js");
    }

    private async Task<GitFixture> RuntimeCheckoutAsync(string workflow = Workflow,
        string defect = "")
    {
        GitFixture repo = await GitFixture.CreateAsync(context.CancellationToken);
        if (defect != "missing-workflow") await repo.SetAsync(NodeRuntimeReader.Workflow, workflow);
        if (defect != "missing-mise") await repo.SetAsync("mise.toml", "[tools]\nnode='24'\n");
        if (defect != "missing-lock") await repo.SetAsync("mise.lock", "version=3\n");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        return repo;
    }

    private async Task<GitMaterialization> BindAsync(GitFixture repo) =>
        await GitMaterialization.BindAsync(repo.Directory,
            await new GitReader(repo.Directory).ReadAsync("HEAD", context.CancellationToken),
            context.CancellationToken);

    private static string Tool(string root, string name, string defect = "")
    {
        string version = name == "node" ? "24.21.0" : "12.8.2";
        string requested = name == "node" ? "24" : version;
        if (name == "node" && defect == "node-request") requested = "22";
        if (name == "node" && defect == "node-version") version = "22.20.0";
        if (name == "pnpm" && defect == "pnpm-floating") requested = "latest";
        object tool = new
        {
            version,
            requested_version = requested,
            active = defect != "tool-inactive",
            source = new
            {
                type = "mise.toml",
                path = Path.Combine(root,
                defect == "tool-source" ? "other.toml" : "mise.toml")
            },
        };
        return JsonSerializer.Serialize(defect == "duplicate-tool" ? new[] { tool, tool } : [tool]);
    }
}
