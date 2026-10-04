using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.CI;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NodeQualityChecksTests(TestContext context)
{
    private const string Preset = "node/hcoona-release-smoke-npm-v1";

    [TestMethod]
    [DataRow("src/public/lib/hcoona-release-smoke-npm")]
    [DataRow("src/component")]
    public void ExpandKnownPresetForDiscoveredProject(string directory)
    {
        CheckSpec[] checks = NodeQualityChecks.Expand(Selection(directory), Scripts(directory));

        CheckKey[] expected = [new(directory, "node/project-build-v1", "default"),
            new(directory, "node/project-test-v1", "default")];
        CollectionAssert.AreEquivalent(expected, checks.Select(check => check.Key).ToArray());
        foreach (CheckSpec check in checks)
        {
            Assert.AreEqual("ubuntu-latest", check.Runner);
            Assert.IsTrue(check.Required);
            Assert.IsEmpty(check.Dimensions);
            Assert.IsEmpty(check.Prerequisites);
        }
    }

    [TestMethod]
    [DataRow("node script.js")]
    [DataRow("")]
    [DataRow("echo 'opaque'; exit 5 && $(unknown)")]
    [DataRow("echo λ\nnode --test")]
    public void ExpandKeepsNativeScriptBodiesOpaque(string body)
    {
        CheckSpec[] checks = NodeQualityChecks.Expand(Selection("src/component"),
            Scripts("src/component", body));

        CheckKey[] expected = [new("src/component", "node/project-build-v1", "default"),
            new("src/component", "node/project-test-v1", "default")];
        CollectionAssert.AreEquivalent(expected,
            checks.Select(check => check.Key).ToArray());
        foreach (CheckSpec check in checks)
        {
            Assert.AreEqual("ubuntu-latest", check.Runner);
            Assert.IsTrue(check.Required);
            Assert.IsEmpty(check.Dimensions);
            Assert.IsEmpty(check.Prerequisites);
        }
    }

    [TestMethod]
    [DataRow("build", "", "test")]
    [DataRow("", "test", "build")]
    [DataRow("", "", "build")]
    [DataRow("Build", "test", "build")]
    [DataRow("build", "Test", "test")]
    public void ExpandRejectsMissingRequiredScript(string build, string test, string missing)
    {
        var scripts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (build.Length != 0)
            scripts.Add(build, "opaque");
        if (test.Length != 0)
            scripts.Add(test, "opaque");

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            NodeQualityChecks.Expand(Selection("src/component"), new("src/component", scripts)));

        Assert.Contains($"'{missing}'", error.Message);
        Assert.Contains("src/component", error.Message);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("node/unknown-v1")]
    [DataRow("python/hcoona-release-smoke-python-v1")]
    public void ExpandRejectsUnknownSelection(string preset)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => NodeQualityChecks.Expand(
            new(preset, "src/component/workflow-delivery.quality.yml"), Scripts("src/component")));
    }

    [TestMethod]
    public void ExpandRejectsAbsentSelection()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
            NodeQualityChecks.Expand(null, Scripts("src/component")));
    }

    [TestMethod]
    public void ExpandRejectsRootInputConsumer()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
            NodeQualityChecks.Expand(Selection("."), Scripts(".")));
    }

    [TestMethod]
    [DataRow("node/project-build-v1")]
    [DataRow("node/project-test-v1")]
    public async Task ExpandedNativeScriptsParticipateInSelectivePlanAndRequiredResult(
        string missingCheck)
    {
        var reader = new PnpmScriptReader(Path.GetTempPath(), (_, _) => Task.FromResult(
            """{"build":"node build.js","test":"node --test","extra":"opaque"}"""));
        PnpmScripts scripts = await reader.ReadAsync(new("src/component", null, []),
            context.CancellationToken);
        ProjectFacts selected = Scenario.Project("component", directory: scripts.Directory,
            preset: Preset,
            checks: NodeQualityChecks.Expand(Selection(scripts.Directory), scripts));
        ProjectFacts unrelated = Scenario.Project("unrelated", preset: Preset,
            checks: NodeQualityChecks.Expand(Selection("src/unrelated"),
                Scripts("src/unrelated")));

        CiPlan plan = ImpactPlanner.Plan(Scenario.Request([selected, unrelated],
            "src/component/index.js"));

        CheckKey[] expected = [new("src/component", "node/project-build-v1", "default"),
            new("src/component", "node/project-test-v1", "default")];
        CollectionAssert.AreEquivalent(expected,
            plan.Checks.Select(check => check.Work.Key).ToArray());
        Assert.DoesNotContain("src/unrelated", plan.Checks.Select(check => check.Work.Key.Target));
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.AreEqual(Preset, Assert.ContainsSingle(check.QualityPresets));
            SelectionReason[] reasons = [new("src/component/index.js", "base", "component"),
                new("src/component/index.js", "candidate", "component")];
            CollectionAssert.AreEquivalent(reasons, check.Reasons);
        }

        CheckResult[] partial = plan.Checks.Where(check => check.Work.Key.Check != missingCheck)
            .Select(check => new CheckResult(plan.Candidate, check.Work.Key, CheckStatus.Passed))
            .ToArray();
        CiOutcome missing = ResultCollector.Collect(plan, partial);
        Assert.IsFalse(missing.Satisfied);
        var missingOutcome = new CheckOutcome(new("src/component", missingCheck, "default"),
            true, "missing");
        Assert.Contains(missingOutcome, missing.Checks);

        CiOutcome complete = ResultCollector.Collect(plan, plan.Checks.Select(check =>
            new CheckResult(plan.Candidate, check.Work.Key, CheckStatus.Passed)).ToArray());
        Assert.IsTrue(complete.Satisfied);
        Assert.IsEmpty(complete.Errors);
        CheckOutcome[] outcomes = expected.Select(key => new CheckOutcome(key, true, "passed"))
            .ToArray();
        CollectionAssert.AreEquivalent(outcomes, complete.Checks);
    }

    private static QualitySelection Selection(string directory) =>
        new(Preset, directory + "/workflow-delivery.quality.yml");

    private static PnpmScripts Scripts(string directory, string body = "opaque") =>
        new(directory, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build"] = body,
            ["test"] = body,
        });
}
