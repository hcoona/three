using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class ResultCollectorTests
{
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
    [DataRow("failed")]
    [DataRow("missing")]
    [DataRow("conflicting")]
    public void CollectAdvisoryFailureRemainsVisibleWithoutBlocking(string state)
    {
        CheckSpec required = Scenario.Check("library");
        CheckSpec advisory = Scenario.Check("library", "lint", required: false);
        var results = new List<CheckResult> { new("candidate", required.Key, CheckStatus.Passed) };
        if (state != "missing")
            results.Add(new("candidate", advisory.Key, CheckStatus.Failed));
        if (state == "conflicting")
            results.Add(new("candidate", advisory.Key, CheckStatus.Passed));

        CiOutcome outcome = ResultCollector.Collect(Scenario.Plan(required, advisory), [..
            results]);

        Assert.IsTrue(outcome.Satisfied);
        Assert.Contains(new CheckOutcome(advisory.Key, false, state), outcome.Checks);
        Assert.Contains(new CheckOutcome(required.Key, true, "passed"), outcome.Checks);
        Assert.IsEmpty(outcome.Errors);
    }

    [TestMethod]
    [DataRow("revision", "Unexpected result subject")]
    [DataRow("target", "Unexpected result subject")]
    [DataRow("operation", "Unexpected result subject")]
    [DataRow("variant", "Unexpected result subject")]
    [DataRow("status", "Unknown result status")]
    public void CollectInvalidResultSubjectOrStatusPreventsSuccess(string defect, string message)
    {
        CheckSpec check = Scenario.Check("library");
        CheckResult invalid = new("candidate", check.Key, CheckStatus.Passed);
        invalid = defect switch
        {
            "revision" => invalid with { Candidate = "another-candidate" },
            "target" => invalid with { Key = check.Key with { Target = "other" } },
            "operation" => invalid with { Key = check.Key with { Check = "build" } },
            "variant" => invalid with { Key = check.Key with { Variant = "windows" } },
            "status" => invalid with { Status = (CheckStatus)999 },
            _ => throw new InvalidOperationException(defect),
        };

        CiOutcome outcome = ResultCollector.Collect(Scenario.Plan(check),
            [new("candidate", check.Key, CheckStatus.Passed), invalid]);

        Assert.IsFalse(outcome.Satisfied);
        Assert.Contains(message, Assert.ContainsSingle(outcome.Errors));
        Assert.AreEqual(new(check.Key, true, "passed"), Assert.ContainsSingle(outcome.Checks));
    }

    [TestMethod]
    [DataRow("missing", "Missing prerequisite")]
    [DataRow("advisory", "advisory prerequisite")]
    [DataRow("cycle", "Cyclic check prerequisites")]
    [DataRow("duplicate", "Duplicate planned check")]
    public void CollectMalformedPlanRejectsInvalidPrerequisiteContracts(string defect, string
        message)
    {
        CheckSpec build = Scenario.Check("library", "build");
        CheckSpec test = Scenario.Check("library", prerequisites: [build.Key]);
        CiPlan plan = defect switch
        {
            "missing" => Scenario.Plan(test),
            "advisory" => Scenario.Plan(test, build with { Required = false }),
            "cycle" => Scenario.Plan(test, build with { Prerequisites = [test.Key] }),
            "duplicate" => Scenario.Plan(test, test, build),
            _ => throw new InvalidOperationException(defect),
        };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ResultCollector.Collect(plan, []));

        Assert.Contains(message, error.Message);
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
