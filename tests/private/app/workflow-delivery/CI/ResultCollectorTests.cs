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
    public void CollectRepeatedRequiredPassCannotSatisfyTheOriginalPlan()
    {
        CheckSpec build = Scenario.Check("library", "build");
        CheckSpec test = Scenario.Check("library", prerequisites: [build.Key]);
        CheckResult passed = new("candidate", test.Key, CheckStatus.Passed);

        CiOutcome outcome = ResultCollector.Collect(Scenario.Plan(build, test),
            [new("candidate", build.Key, CheckStatus.Passed), passed, passed]);

        Assert.IsFalse(outcome.Satisfied);
        Assert.Contains(test.Key.ToString(), Assert.ContainsSingle(outcome.Errors));
        CollectionAssert.AreEquivalent(new[]
        {
            new CheckOutcome(build.Key, true, "passed"),
            new CheckOutcome(test.Key, true, "duplicate"),
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
    [DataRow("other-tree", (int)CheckStatus.Passed, "unbound")]
    [DataRow("candidate", 999, "unknown")]
    public void JoinedAdvisoryReceiptCannotClaimAPassOrBlockMandatoryWork(string candidate,
        int status, string advisoryStatus)
    {
        CheckSpec test = Scenario.Check("library");
        CheckSpec lint = Scenario.Check("library", "lint", required: false);
        CiPlan plan = Scenario.Plan(test, lint);
        CiGroupReceipt receipt = new("group", plan.Candidate,
            [new(plan.Candidate, test.Key, CheckStatus.Passed), new(candidate, lint.Key,
                (CheckStatus)status)], plan);

        var (_, outcome) = ResultCollector.Join(plan, ["group"], _ => "group",
            [new("group", receipt)], "group", requireCompleteSets: true);

        Assert.IsTrue(outcome.Satisfied);
        Assert.Contains(new CheckOutcome(lint.Key, false, advisoryStatus), outcome.Checks);
    }

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
    public void ValidateRejectsMissingPlanIdentity()
    {
        CiPlan plan = Scenario.Plan();
        AssertRejected(plan with { Comparison = "" }, "comparison");
        AssertRejected(plan with { Candidate = " " }, "candidate");
        AssertRejected(plan with { Scope = "" }, "coverage scope");
    }

    [TestMethod]
    public void ValidateRejectsInvalidPrerequisiteContracts()
    {
        CheckSpec build = Scenario.Check("library", "build");
        CheckSpec test = Scenario.Check("library", prerequisites: [build.Key]);
        AssertRejected(Scenario.Plan(test), "Missing prerequisite");
        AssertRejected(Scenario.Plan(test, build with { Required = false }),
            "advisory prerequisite");
        AssertRejected(Scenario.Plan(test, build with { Prerequisites = [test.Key] }),
            "Cyclic check prerequisites");
        AssertRejected(Scenario.Plan(test, test, build), "Duplicate planned check");
    }

    [TestMethod]
    public void PartitionRequiresBothSidesToKeepTheirPrerequisites()
    {
        CheckSpec build = Scenario.Check("library", "build");
        CheckSpec test = Scenario.Check("library", prerequisites: [build.Key]);
        CheckSpec unrelated = Scenario.Check("app");
        CiPlan plan = Scenario.Plan(test, unrelated, build);
        plan.Validate();

        var (library, app) = plan.Partition(item => item.Work.Key.Target == "library");
        CollectionAssert.AreEqual(new[] { test.Key, build.Key },
            library.Checks.Select(item => item.Work.Key).ToArray());
        Assert.AreEqual(unrelated.Key, Assert.ContainsSingle(app.Checks).Work.Key);
        Assert.Contains("prerequisite crosses CI execution partitions",
            Assert.ThrowsExactly<InvalidDataException>(() =>
                plan.Partition(item => item.Work.Key == build.Key)).Message);
    }

    [TestMethod]
    public async Task RunnerGatesDependentsWithoutSuppressingIndependentWork()
    {
        CheckSpec build = Scenario.Check("app", "build");
        CheckSpec contents = Scenario.Check("app", "contents", prerequisites: [build.Key]);
        CheckSpec consumer = Scenario.Check("app", "consumer", prerequisites: [contents.Key]);
        CheckSpec test = Scenario.Check("app");
        CiPlan plan = Scenario.Plan(consumer, test, contents, build);
        var runner = new CheckExecutionRunner(plan, CancellationToken.None);

        await runner.RunAsync(build, () => Task.FromResult(CheckStatus.Passed));
        await runner.RunAsync(contents, () => Task.FromResult(CheckStatus.Failed));
        await runner.RunAsync(consumer, UnexpectedStep);
        await runner.RunAsync(test, () => Task.FromResult(CheckStatus.Passed));

        CollectionAssert.AreEqual(new CheckResult[]
        {
            new(plan.Candidate, consumer.Key, CheckStatus.Skipped),
            new(plan.Candidate, test.Key, CheckStatus.Passed),
            new(plan.Candidate, contents.Key, CheckStatus.Failed),
            new(plan.Candidate, build.Key, CheckStatus.Passed),
        }, runner.InPlanOrder());
    }

    [TestMethod]
    public async Task RunnerCancellationTakesPrecedenceOverFailedPrerequisite()
    {
        CheckSpec build = Scenario.Check("app", "build");
        CheckSpec test = Scenario.Check("app", prerequisites: [build.Key]);
        CiPlan plan = Scenario.Plan(build, test);
        using var cancellation = new CancellationTokenSource();
        var runner = new CheckExecutionRunner(plan, cancellation.Token);

        await runner.RunAsync(build, () => Task.FromResult(CheckStatus.TimedOut));
        await cancellation.CancelAsync();
        await runner.RunAsync(test, UnexpectedStep);

        CollectionAssert.AreEqual(new CheckResult[]
        {
            new(plan.Candidate, build.Key, CheckStatus.TimedOut),
            new(plan.Candidate, test.Key, CheckStatus.Cancelled),
        }, runner.InPlanOrder());
    }

    private static Task<CheckStatus> UnexpectedStep() =>
        throw new AssertFailedException("An ineligible check must not execute.");

    private static void AssertRejected(CiPlan plan, string diagnostic) =>
        Assert.Contains(diagnostic, Assert.ThrowsExactly<InvalidDataException>(
            plan.Validate).Message);

    [TestMethod]
    public void CollectEmptyPlanIsSatisfiedWithoutInventingWork()
    {
        CiOutcome outcome = ResultCollector.Collect(Scenario.Plan(), []);

        Assert.IsTrue(outcome.Satisfied);
        Assert.IsEmpty(outcome.Checks);
        Assert.IsEmpty(outcome.Errors);
    }
}
