using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class ResultCollectorTests
{
    [TestMethod]
    public void CollectMalformedOriginContractRejectsPassedResults()
    {
        CheckSpec work = Scenario.Check("library");
        CiPlan plan = Scenario.Plan(work);
        PlannedCheck check = Assert.ContainsSingle(plan.Checks);
        PlannedCheck[] malformed =
        [
            check with { Origins = [] },
            check with { Origins = [(CheckOrigin)999] },
            check with { QualityPresets = [] },
            check with { Origins = [CheckOrigin.NativeRetained] },
            check with
            {
                Origins = [CheckOrigin.Preset, CheckOrigin.NativeRetained],
                QualityPresets = [],
            },
            check with { QualityPresets = [" "] },
        ];
        foreach (PlannedCheck invalid in malformed)
            Assert.Contains("origin/preset relationship",
                Assert.ThrowsExactly<InvalidDataException>(() =>
                    ResultCollector.Collect(plan with { Checks = [invalid] },
                        [new(plan.Candidate, work.Key, CheckStatus.Passed)])).Message);
    }

    [TestMethod]
    public void CollectPresetNativeAndMixedOriginsPreserveSatisfiedResult()
    {
        CheckSpec work = Scenario.Check("library");
        CiPlan plan = Scenario.Plan(work);
        PlannedCheck preset = Assert.ContainsSingle(plan.Checks);
        PlannedCheck[] sources =
        [
            preset,
            preset with { Origins = [CheckOrigin.NativeRetained], QualityPresets = [] },
            preset with { Origins = [CheckOrigin.Preset, CheckOrigin.NativeRetained] },
        ];
        foreach (PlannedCheck source in sources)
        {
            CiOutcome outcome = ResultCollector.Collect(plan with { Checks = [source] },
                [new(plan.Candidate, work.Key, CheckStatus.Passed)]);
            Assert.IsTrue(outcome.Satisfied);
            Assert.IsEmpty(outcome.Errors);
            Assert.AreEqual(new(work.Key, true, "passed"), Assert.ContainsSingle(outcome.Checks));
        }
    }

    [TestMethod]
    public void CollectAllRequiredPassedWithDuplicateResultIsSatisfied()
    {
        CheckSpec build = Scenario.Check("library", "build");
        CheckSpec test = Scenario.Check("library", prerequisites: [build.Key]);
        CheckResult passed = new("candidate", test.Key, CheckStatus.Passed);

        CiOutcome outcome = ResultCollector.Collect(Scenario.Plan(build, test),
            [new("candidate", build.Key, CheckStatus.Passed), passed, passed]);

        Assert.IsTrue(outcome.Satisfied);
        Assert.IsEmpty(outcome.Errors);
        CollectionAssert.AreEquivalent(new[]
        {
            new CheckOutcome(build.Key, true, "passed"), new CheckOutcome(test.Key, true, "passed"),
        }, outcome.Checks);
    }

    [TestMethod]
    [DataRow((int)CheckStatus.Failed, "failed")]
    [DataRow((int)CheckStatus.Cancelled, "cancelled")]
    [DataRow((int)CheckStatus.TimedOut, "timedout")]
    [DataRow((int)CheckStatus.Skipped, "skipped")]
    public void CollectUnsuccessfulRequiredResultPreventsSuccess(int status, string expected)
    {
        CheckSpec check = Scenario.Check("library");

        CiOutcome outcome = ResultCollector.Collect(Scenario.Plan(check), [new("candidate",
            check.Key, (CheckStatus)status)]);

        Assert.IsFalse(outcome.Satisfied);
        Assert.AreEqual(new(check.Key, true, expected), Assert.ContainsSingle(outcome.Checks));
        Assert.IsEmpty(outcome.Errors);
    }

    [TestMethod]
    public void CollectMissingRequiredVariantPreventsSuccessDespiteOtherPass()
    {
        CheckSpec linux = Scenario.Check("library", variant: "linux");
        CheckSpec windows = Scenario.Check("library", variant: "windows");

        CiOutcome outcome = ResultCollector.Collect(Scenario.Plan(linux, windows),
            [new("candidate", linux.Key, CheckStatus.Passed)]);

        Assert.IsFalse(outcome.Satisfied);
        CollectionAssert.AreEquivalent(new[]
        {
            new CheckOutcome(linux.Key, true, "passed"), new CheckOutcome(windows.Key, true,
                "missing"),
        }, outcome.Checks);
    }

    [TestMethod]
    public void CollectConflictingRequiredResultsPreventsSuccess()
    {
        CheckSpec check = Scenario.Check("library");

        CiOutcome outcome = ResultCollector.Collect(Scenario.Plan(check),
            [new("candidate", check.Key, CheckStatus.Passed), new("candidate", check.Key,
                CheckStatus.Failed)]);

        Assert.IsFalse(outcome.Satisfied);
        Assert.AreEqual(new(check.Key, true, "conflicting"), Assert.ContainsSingle(outcome.Checks));
    }

    [TestMethod]
    [DataRow("failed", true, false)]
    [DataRow("missing", false, false)]
    [DataRow("conflicting", true, true)]
    public void CollectAdvisoryFailureRemainsVisibleWithoutBlocking(string expected,
        bool failed, bool passed)
    {
        CheckSpec required = Scenario.Check("library");
        CheckSpec advisory = Scenario.Check("library", "lint", required: false);
        var results = new List<CheckResult> { new("candidate", required.Key, CheckStatus.Passed) };
        if (failed)
            results.Add(new("candidate", advisory.Key, CheckStatus.Failed));
        if (passed)
            results.Add(new("candidate", advisory.Key, CheckStatus.Passed));

        CiOutcome outcome = ResultCollector.Collect(Scenario.Plan(required, advisory), [..
            results]);

        Assert.IsTrue(outcome.Satisfied);
        Assert.Contains(new CheckOutcome(advisory.Key, false, expected), outcome.Checks);
        Assert.Contains(new CheckOutcome(required.Key, true, "passed"), outcome.Checks);
        Assert.IsEmpty(outcome.Errors);
    }

    [TestMethod]
    [DataRow("another-candidate", "library", "test", "default")]
    [DataRow("candidate", "other", "test", "default")]
    [DataRow("candidate", "library", "build", "default")]
    [DataRow("candidate", "library", "test", "windows")]
    public void CollectDifferentResultSubjectPreventsSuccess(string candidate, string target,
        string operation, string variant) =>
        AssertInvalidResult(new(candidate, new(target, operation, variant), CheckStatus.Passed),
            "Unexpected result subject");

    [TestMethod]
    public void CollectUnknownResultStatusPreventsSuccess() =>
        AssertInvalidResult(new("candidate", Scenario.Check("library").Key, (CheckStatus)999),
            "Unknown result status");

    private static void AssertInvalidResult(CheckResult invalid, string diagnostic)
    {
        CheckSpec check = Scenario.Check("library");
        CiOutcome outcome = ResultCollector.Collect(Scenario.Plan(check),
            [new("candidate", check.Key, CheckStatus.Passed), invalid]);

        Assert.IsFalse(outcome.Satisfied);
        Assert.Contains(diagnostic, Assert.ContainsSingle(outcome.Errors));
        Assert.AreEqual(new(check.Key, true, "passed"), Assert.ContainsSingle(outcome.Checks));
    }

    [TestMethod]
    public void CollectMalformedPlanRejectsInvalidPrerequisiteContracts()
    {
        CheckSpec build = Scenario.Check("library", "build");
        CheckSpec test = Scenario.Check("library", prerequisites: [build.Key]);
        AssertRejected("Missing prerequisite", test);
        AssertRejected("advisory prerequisite", test, build with { Required = false });
        AssertRejected("Cyclic check prerequisites", test,
            build with { Prerequisites = [test.Key] });
        AssertRejected("Duplicate planned check", test, test, build);

        static void AssertRejected(string diagnostic, params CheckSpec[] checks) =>
            Assert.Contains(diagnostic, Assert.ThrowsExactly<InvalidDataException>(() =>
                ResultCollector.Collect(Scenario.Plan(checks), [])).Message);
    }

    [TestMethod]
    public void CollectEmptyPlanIsSatisfiedWithoutInventingWork()
    {
        CiOutcome outcome = ResultCollector.Collect(Scenario.Plan(), []);

        Assert.IsTrue(outcome.Satisfied);
        Assert.IsEmpty(outcome.Checks);
        Assert.IsEmpty(outcome.Errors);
    }
}
