using WorkflowDelivery.CI;
using WorkflowDelivery.CI.Callers;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class GroupJoinConformanceTests
{
    [TestMethod]
    [TestCategory("DeliveryTlcConformance")]
    public void NodeMatrixJoinAgreesWithComponentVerdict()
    {
        var group = CiCallerState.NodeGroup();
        foreach (string[] row in DeliveryModelOracle.Rows("CiJoinOracle"))
        {
            if (row is not [var fault, var verdict])
                throw new AssertFailedException("Incomplete CI join observation.");
            var first = CiCallerState.NodeResult(group, "node22", CheckStatus.Passed);
            var second = CiCallerState.NodeResult(group, "node24", CheckStatus.Passed);
            CheckResult focus = first.Results[0];
            CheckResult[] rest = first.Results[1..];
            var receipts = new List<KeyValuePair<string, NodeRunResult>>();
            switch (fault)
            {
                case "passed":
                case "missing-group":
                    break;
                case "other-tree":
                    first = first with
                    {
                        Results = [focus with { Candidate = CiCallerState.Foreign }, .. rest]
                    };
                    break;
                case "other-basis":
                    first = first with
                    {
                        OriginalPlan = first.OriginalPlan with
                        {
                            Comparison = CiCallerState.Foreign
                        }
                    };
                    break;
                case "other-plan":
                    first = first with { OriginalPlan = first.OriginalPlan with { Checks = [] } };
                    break;
                case "duplicate":
                case "conflicting":
                    first = first with
                    {
                        Results =
                        [
                            .. first.Results,
                            fault == "duplicate"
                                ? focus
                                : focus with { Status = CheckStatus.Failed }
                        ]
                    };
                    break;
                case "misplaced-group":
                    second = second with { Results = [.. second.Results, focus] };
                    goto case "missing";
                case "missing":
                    first = first with { Results = rest };
                    break;
                case "duplicate-group":
                    receipts.Add(new(first.Runtime, first));
                    break;
                case "extra-group":
                    receipts.Add(
                        new("unplanned", first with { Runtime = "unplanned", Results = [] }));
                    break;
                default:
                    CheckStatus status = fault switch
                    {
                        "failed" => CheckStatus.Failed,
                        "skipped" => CheckStatus.Skipped,
                        "cancelled" => CheckStatus.Cancelled,
                        "timed-out" => CheckStatus.TimedOut,
                        "unknown" => (CheckStatus)(-1),
                        _ => throw new AssertFailedException("Unknown model fault: " + fault),
                    };
                    first = first with { Results = [focus with { Status = status }, .. rest] };
                    break;
            }
            if (fault != "missing-group") receipts.Add(new(first.Runtime, first));
            receipts.Add(new(second.Runtime, second));
            bool passed;
            try { passed = NodeCiCaller.Join(group.Adopted, group, receipts).Outcome.Satisfied; }
            catch (InvalidDataException) { passed = false; }
            Assert.AreEqual(verdict, passed ? "pass" : "reject", fault);
        }
    }
}
