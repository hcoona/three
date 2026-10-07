using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class ImpactPlannerPairedReasonsTests
{
    private const string Input = "shared/removed.py";
    private static readonly string[] InputPaths = [Input];
    private static readonly string[] Consumer = ["consumer"];
    private static readonly string[] FullTargets = ["c", "other"];
    private static readonly string[] SurvivingTargets = ["consumer", "new"];

    [TestMethod]
    public void PairedReasonSelectsCandidateOnlyOwnerAndCompleteSharedClosure()
    {
        CheckSpec prepare = Scenario.Check("tooling", "restore", required: false);
        CheckSpec linux = Scenario.Check("c", variant: "linux", prerequisites: [prepare.Key]) with
        {
            Dimensions = new() { ["rid"] = "linux-x64" },
        };
        CheckSpec windows = Scenario.Check("c", variant: "windows", prerequisites: [prepare.Key])
            with
        { Runner = "windows-latest", Dimensions = new() { ["rid"] = "win-x64" } };
        ProjectFacts owner = Scenario.Project("c", qualityConsumers: ["quality"],
            releaseUnit: "product", checks: [linux, windows]) with
        {
            QualityPreset = null,
            Origin = CheckOrigin.NativeRetained,
        };
        ProjectFacts sibling = Scenario.Project("sibling", releaseUnit: "product");
        ProjectFacts consumer = Scenario.Project("consumer", dependencies: [owner.Id]);
        ProjectFacts quality = Scenario.Project("quality");
        var request = new PlanRequest(Scenario.Facts("base"), Scenario.Facts("candidate",
            owner, sibling, consumer, quality,
            Scenario.Project("tooling", checks: [prepare]), Scenario.Project("unrelated")),
            InputPaths, false);
        var reason = new SelectionReason(Input, "base", owner.Id);

        CiPlan plan = ImpactPlanner.Plan(request, [reason]);

        CollectionAssert.AreEquivalent(new[] { linux.Key, windows.Key, prepare.Key,
            sibling.Checks[0].Key, consumer.Checks[0].Key, quality.Checks[0].Key },
            plan.Checks.Select(check => check.Work.Key).ToArray());
        Assert.AreEqual("base", plan.Comparison);
        Assert.AreEqual("candidate", plan.Candidate);
        foreach (PlannedCheck check in plan.Checks)
            Assert.AreEqual(reason, Assert.ContainsSingle(check.Reasons));
        PlannedCheck prerequisite = plan.Checks.Single(check => check.Work.Key == prepare.Key);
        Assert.IsTrue(prerequisite.Work.Required);
        Assert.AreEqual(CheckOrigin.NativeRetained, Assert.ContainsSingle(prerequisite.Origins));
        Assert.IsEmpty(prerequisite.QualityPresets);
        PlannedCheck selectedWindows = plan.Checks.Single(check => check.Work.Key == windows.Key);
        Assert.AreEqual("windows-latest", selectedWindows.Work.Runner);
        Assert.AreEqual("win-x64", selectedWindows.Work.Dimensions["rid"]);
        PlannedCheck selectedSibling = plan.Checks.Single(check =>
            check.Work.Key == sibling.Checks[0].Key);
        Assert.AreEqual(CheckOrigin.Preset, Assert.ContainsSingle(selectedSibling.Origins));
        Assert.AreEqual("standard", Assert.ContainsSingle(selectedSibling.QualityPresets));
    }

    [TestMethod]
    public void DeletedPairedOwnerKeepsSurvivingMixedEndpointConsumers()
    {
        RepositoryFacts basis = Scenario.Facts("base",
            Scenario.Project("removed", qualityConsumers: Consumer),
            Scenario.Project("consumer"));
        RepositoryFacts candidate = Scenario.Facts("candidate", Scenario.Project("consumer"),
            Scenario.Project("new", dependencies: Consumer), Scenario.Project("unrelated"));
        var reason = new SelectionReason(Input, "base", "removed");

        CiPlan plan = ImpactPlanner.Plan(new(basis, candidate, InputPaths, false), [reason]);

        CollectionAssert.AreEquivalent(SurvivingTargets,
            plan.Checks.Select(check => check.Work.Key.Target).ToArray());
        foreach (PlannedCheck check in plan.Checks)
            Assert.AreEqual(reason, Assert.ContainsSingle(check.Reasons));
    }

    [TestMethod]
    public void PairedAndNativeReasonsRetainBothEndpointProvenance()
    {
        const string native = "src/c/code.py";
        PlanRequest request = Scenario.Request([Scenario.Project("c")], Input, native);
        var basis = new SelectionReason(Input, "base", "c");
        var candidate = new SelectionReason(Input, "candidate", "c");

        PlannedCheck selected = Assert.ContainsSingle(ImpactPlanner.Plan(request,
            [basis, candidate, basis]).Checks);

        CollectionAssert.AreEquivalent(new[] { basis, candidate,
            new SelectionReason(native, "base", "c"),
            new SelectionReason(native, "candidate", "c") }, selected.Reasons);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ValidPairedReasonDoesNotHideOtherUnknownChanges(bool full)
    {
        PlanRequest request = Scenario.Request([Scenario.Project("c")], Input,
            "unknown/file.py") with
        { Full = full };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(request, [new(Input, "base", "c")]));

        Assert.Contains("Unresolved changed path: unknown/file.py", error.Message);
    }

    [TestMethod]
    [DataRow("owner", false)]
    [DataRow("owner", true)]
    [DataRow("revision", false)]
    [DataRow("revision", true)]
    [DataRow("changed-path", false)]
    [DataRow("changed-path", true)]
    [DataRow("normalized-path", false)]
    [DataRow("normalized-path", true)]
    public void InvalidPairedReasonRejectsWithoutFullFallback(string defect, bool full)
    {
        const string changed = "src/c/code.py";
        PlanRequest request = Scenario.Request([Scenario.Project("c")], changed) with
        { Full = full };
        var reason = new SelectionReason(changed, "base", "c");
        reason = defect switch
        {
            "owner" => reason with { Project = "foreign" },
            "revision" => reason with { Revision = "foreign" },
            "changed-path" => reason with { Path = "src/c/unchanged.py" },
            "normalized-path" => reason with { Path = "../outside.py" },
            _ => throw new ArgumentException("Unknown fixture defect.", nameof(defect)),
        };
        // An ordinary valid owner must not make an invalid supplied reason ignorable.
        Assert.ThrowsExactly<InvalidDataException>(() => ImpactPlanner.Plan(request, [reason]));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void PairedReasonsDoNotBypassEndpointFactValidation(bool invalidBasis, bool full)
    {
        PlanRequest request = Scenario.Request([Scenario.Project("c")], Input) with
        { Full = full };
        request = invalidBasis
            ? request with { Basis = request.Basis with { Errors = ["incomplete native scope"] } }
            : request with
            {
                Candidate = request.Candidate with
                { Errors = ["incomplete native scope"] }
            };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(request, [new(Input, "base", "c")]));

        Assert.Contains("incomplete native scope", error.Message);
    }

    [TestMethod]
    [DataRow("checks")]
    [DataRow("preset")]
    [DataRow("native-preset")]
    public void PairedReasonsDoNotBypassSelectedQualityContract(string defect)
    {
        ProjectFacts owner = Scenario.Project("c");
        owner = defect switch
        {
            "checks" => owner with { Checks = [] },
            "preset" => owner with { QualityPreset = null },
            "native-preset" => owner with { Origin = CheckOrigin.NativeRetained },
            _ => throw new ArgumentException("Unknown fixture defect.", nameof(defect)),
        };
        var request = new PlanRequest(Scenario.Facts("base"),
            Scenario.Facts("candidate", owner), InputPaths, false);

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(request, [new(Input, "base", owner.Id)]));

        Assert.Contains("Unresolved quality contract for c", error.Message);
    }

    [TestMethod]
    [DataRow("checks", false)]
    [DataRow("checks", true)]
    [DataRow("preset", false)]
    [DataRow("preset", true)]
    [DataRow("native-preset", false)]
    [DataRow("native-preset", true)]
    public void PairedReasonsRejectMalformedDeletedOwnersBeforeClosure(string defect, bool full)
    {
        ProjectFacts owner = Scenario.Project("removed", qualityConsumers: Consumer);
        owner = defect switch
        {
            "checks" => owner with { Checks = [] },
            "preset" => owner with { QualityPreset = null },
            "native-preset" => owner with { Origin = CheckOrigin.NativeRetained },
            _ => throw new ArgumentException("Unknown fixture defect.", nameof(defect)),
        };
        var request = new PlanRequest(Scenario.Facts("base", owner,
            Scenario.Project("consumer")), Scenario.Facts("candidate",
            Scenario.Project("consumer")), InputPaths, full);

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.SelectProjects(request, [new(Input, "base", owner.Id)]));

        Assert.Contains("Unresolved quality contract for removed", error.Message);
    }

    [TestMethod]
    public void PairedReasonPreservesExplicitFullMode()
    {
        PlanRequest request = Scenario.Request([Scenario.Project("c"),
            Scenario.Project("other")], Input) with
        { Full = true };
        var reason = new SelectionReason(Input, "base", "c");

        CiPlan plan = ImpactPlanner.Plan(request, [reason]);

        CollectionAssert.AreEquivalent(FullTargets,
            plan.Checks.Select(check => check.Work.Key.Target).ToArray());
        CollectionAssert.AreEquivalent(new[] { reason,
            new SelectionReason("<full>", "candidate", "c") },
            plan.Checks.Single(check => check.Work.Key.Target == "c").Reasons);
        Assert.AreEqual(new SelectionReason("<full>", "candidate", "other"),
            Assert.ContainsSingle(plan.Checks.Single(check =>
                check.Work.Key.Target == "other").Reasons));
    }
}
