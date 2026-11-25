using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class ImpactPlannerTests
{
    private const string PairedInput = "shared/removed.py";
    private static readonly string[] PairedInputPaths = [PairedInput];
    private static readonly string[] PairedConsumer = ["consumer"];
    private static readonly string[] PairedFullTargets = ["c", "other"];
    private static readonly string[] PairedSurvivingTargets = ["consumer", "new"];

    [TestMethod]
    public void PlanExactOwnershipExcludesFixtureSourceButPreservesEmbeddedConsumer()
    {
        const string root = "tests/controller";
        const string fixture = root + "/fixtures/node";
        const string source = fixture + "/index.js";
        const string embedded = fixture + "/version.json";
        ProjectFacts native = Scenario.Project("controller", directory: root) with
        {
            OwnedPaths = [root + "/Tests.cs"],
            QualityPreset = null,
            Origin = CheckOrigin.NativeRetained,
        };
        ProjectFacts node = Scenario.Project("node", directory: fixture);
        RepositoryFacts basis = Scenario.Facts("base", native, node) with
        {
            SharedInputs = [new(embedded, [native.Id])],
        };
        RepositoryFacts candidate = basis with { Revision = "candidate" };

        CiPlan ordinary = ImpactPlanner.Plan(new(basis, candidate, [source], false));
        Assert.AreEqual(node.Id, Assert.ContainsSingle(ordinary.Checks).Work.Key.Target);

        CiPlan resource = ImpactPlanner.Plan(new(basis, candidate, [embedded], false));
        CollectionAssert.AreEquivalent(new[] { native.Id, node.Id },
            resource.Checks.Select(check => check.Work.Key.Target).ToArray());
        PlannedCheck consumer = resource.Checks.Single(check => check.Work.Key.Target == native.Id);
        Assert.IsEmpty(consumer.QualityPresets);
        CollectionAssert.AreEqual(new[] { CheckOrigin.NativeRetained }, consumer.Origins);
        CollectionAssert.AreEquivalent(new[]
        {
            new SelectionReason(embedded, "base", native.Id),
            new SelectionReason(embedded, "candidate", native.Id),
        }, consumer.Reasons);
    }

    [TestMethod]
    public void PlanExactDeletedInputUsesFormerEdgesAndAllSurvivingCandidateVariants()
    {
        const string deleted = "src/library/removed.cs";
        ProjectFacts library = Scenario.Project("library") with { OwnedPaths = [deleted] };
        ProjectFacts consumer = Scenario.Project("consumer", dependencies: [library.Id]);
        CheckSpec[] variants =
        [Scenario.Check(library.Id, variant: "net8"), Scenario.Check(library.Id, variant: "net10")];
        RepositoryFacts basis = Scenario.Facts("base", library, consumer);
        RepositoryFacts candidate = Scenario.Facts("candidate",
            library with { OwnedPaths = [], Checks = variants },
            consumer with { Dependencies = [] });

        CiPlan plan = ImpactPlanner.Plan(new(basis, candidate, [deleted], false));

        CollectionAssert.AreEquivalent(variants.Select(check => check.Key)
            .Append(consumer.Checks[0].Key).ToArray(), plan.Checks.Select(check => check.Work.Key)
            .ToArray());
        foreach (PlannedCheck check in plan.Checks)
            CollectionAssert.AreEqual(new[] { new SelectionReason(deleted, "base", library.Id) },
                check.Reasons);
    }

    [TestMethod]
    public void PlanExactOwnershipDoesNotRestoreAnExcludedParentDirectory()
    {
        const string path = "src/parent/native/excluded.txt";
        ProjectFacts parent = Scenario.Project("parent");
        ProjectFacts child = Scenario.Project("child", directory: "src/parent/native") with
        {
            OwnedPaths = [],
        };
        PlanRequest request = Scenario.Request([parent, child], path);

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(request));
        Assert.Contains("Unresolved changed path", error.Message);

        request = request with
        {
            Basis = request.Basis with { SharedInputs = [new(path, [parent.Id])] },
        };
        PlannedCheck selected = Assert.ContainsSingle(ImpactPlanner.Plan(request).Checks);
        Assert.AreEqual(parent.Id, selected.Work.Key.Target);
        CollectionAssert.AreEqual(new[] { new SelectionReason(path, "base", parent.Id) },
            selected.Reasons);
    }

    [TestMethod]
    public void PlanExactOwnershipRetainsEveryProjectSharingThePath()
    {
        const string path = "src/shared/code.cs";
        ProjectFacts first = Scenario.Project("first", directory: "src/shared") with
        {
            OwnedPaths = [path],
        };
        ProjectFacts second = Scenario.Project("second", directory: "src/shared") with
        {
            OwnedPaths = [path],
        };

        CiPlan plan = ImpactPlanner.Plan(Scenario.Request([first, second], path));

        CollectionAssert.AreEquivalent(new[] { first.Id, second.Id },
            plan.Checks.Select(check => check.Work.Key.Target).ToArray());
        foreach (PlannedCheck check in plan.Checks)
            Assert.HasCount(2, check.Reasons);
    }

    [TestMethod]
    [DataRow("src/native/code.cs", "src/native/code.cs")]
    [DataRow("../src/native/code.cs", "src/native/other.cs")]
    public void PlanMalformedExactOwnershipFailsBeforeSelection(string first, string second)
    {
        ProjectFacts project = Scenario.Project("native") with
        {
            OwnedPaths = [first, second],
        };

        Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(Scenario.Request([project])));
    }

    [TestMethod]
    public void PlanNativeRetainedChecksNeedNoPresetAndKeepRequiredPrerequisites()
    {
        CheckSpec build = Scenario.Check("native", "build", required: false);
        CheckSpec test = Scenario.Check("native", prerequisites: [build.Key]);
        ProjectFacts project = Scenario.Project("native", checks: [test, build]) with
        {
            QualityPreset = null,
            Origin = CheckOrigin.NativeRetained,
        };

        CiPlan plan = ImpactPlanner.Plan(Scenario.Request([project], "src/native/code.cs"));

        CollectionAssert.AreEquivalent(new[] { build.Key, test.Key },
            plan.Checks.Select(check => check.Work.Key).ToArray());
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.IsTrue(check.Work.Required);
            Assert.IsEmpty(check.QualityPresets);
            CollectionAssert.AreEqual(new[] { CheckOrigin.NativeRetained }, check.Origins);
            Assert.HasCount(2, check.Reasons);
        }
    }

    [TestMethod]
    [DataRow((int)CheckOrigin.NativeRetained, null, false)]
    [DataRow((int)CheckOrigin.NativeRetained, "invented-adoption", true)]
    [DataRow(42, null, true)]
    public void PlanRejectsUnresolvedOrContradictoryNativeRetainedContract(
        int origin, string? preset, bool hasCheck)
    {
        ProjectFacts native = Scenario.Project("native") with
        {
            QualityPreset = preset,
            Origin = (CheckOrigin)origin,
            Checks = hasCheck ? [Scenario.Check("native")] : [],
        };

        Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(Scenario.Request([native], "src/native/code.cs")));
    }

    [TestMethod]
    public void PlanNativeAndPresetSourcesMergeWithoutAdoptingUnselectedPolicies()
    {
        CheckSpec shared = Scenario.Check("shared", required: false);
        ProjectFacts native = Scenario.Project("native", checks: [shared]) with
        {
            QualityPreset = null,
            Origin = CheckOrigin.NativeRetained,
        };
        ProjectFacts adopted = Scenario.Project("adopted", preset: "selected",
            checks: [shared with { Required = true }]);
        ProjectFacts unrelated = Scenario.Project("unrelated", preset: "unselected",
            checks: [shared with { Required = true }]);

        PlannedCheck onlyNative = Assert.ContainsSingle(ImpactPlanner.Plan(
            Scenario.Request([native, adopted, unrelated], "src/native/code.cs")).Checks);
        Assert.IsFalse(onlyNative.Work.Required);
        Assert.IsEmpty(onlyNative.QualityPresets);
        CollectionAssert.AreEqual(new[] { CheckOrigin.NativeRetained }, onlyNative.Origins);

        PlannedCheck merged = Assert.ContainsSingle(ImpactPlanner.Plan(Scenario.Request(
            [native, adopted, unrelated], "src/native/code.cs", "src/adopted/code.cs")).Checks);
        Assert.IsTrue(merged.Work.Required);
        Assert.AreEqual("selected", Assert.ContainsSingle(merged.QualityPresets));
        CollectionAssert.AreEqual(new[] { CheckOrigin.Preset, CheckOrigin.NativeRetained },
            merged.Origins);
        Assert.HasCount(4, merged.Reasons);
    }

    [TestMethod]
    public void PlanLateNativeOriginPropagatesThroughSharedPrerequisiteClosure()
    {
        CheckSpec restore = Scenario.Check("dependency", "restore", required: false);
        CheckSpec build = Scenario.Check("dependency", "build", required: false,
            prerequisites: [restore.Key]);
        ProjectFacts adopted = Scenario.Project("adopted", checks:
            [Scenario.Check("adopted", required: false, prerequisites: [build.Key])]);
        ProjectFacts native = Scenario.Project("native", checks:
            [Scenario.Check("native", prerequisites: [build.Key])]) with
        {
            QualityPreset = null,
            Origin = CheckOrigin.NativeRetained,
        };
        ProjectFacts dependency = Scenario.Project("dependency", checks: [build, restore]);

        CiPlan plan = ImpactPlanner.Plan(Scenario.Request([adopted, native, dependency],
            "src/adopted/code.cs", "src/native/code.cs"));

        foreach (CheckKey key in new[] { build.Key, restore.Key })
        {
            PlannedCheck check = plan.Checks.Single(check => check.Work.Key == key);
            Assert.IsTrue(check.Work.Required);
            Assert.AreEqual("standard", Assert.ContainsSingle(check.QualityPresets));
            CollectionAssert.AreEqual(new[] { CheckOrigin.Preset, CheckOrigin.NativeRetained },
                check.Origins);
            Assert.HasCount(4, check.Reasons);
        }
    }

    [TestMethod]
    public void PlanSourceChangesSelectTransitiveAndQualityConsumersWithAllReasons()
    {
        ProjectFacts[] projects =
        [
            Scenario.Project("library", qualityConsumers: ["quality"]),
            Scenario.Project("consumer", dependencies: ["library"]),
            Scenario.Project("application", dependencies: ["consumer"]),
            Scenario.Project("quality"),
            Scenario.Project("unrelated"),
        ];

        CiPlan plan = ImpactPlanner.Plan(Scenario.Request(projects,
            "src/library/one.cs", "src/library/two.cs"));

        string[] expected = ["library", "consumer", "application", "quality"];

        CollectionAssert.AreEquivalent(expected,
            plan.Checks.Select(x => x.Work.Key.Target).ToArray());
        Assert.DoesNotContain("unrelated", plan.Checks.Select(x => x.Work.Key.Target));
        foreach (PlannedCheck check in plan.Checks)
        {
            CollectionAssert.AreEquivalent(new[]
            {
                new SelectionReason("src/library/one.cs", "base", "library"),
                new SelectionReason("src/library/one.cs", "candidate", "library"),
                new SelectionReason("src/library/two.cs", "base", "library"),
                new SelectionReason("src/library/two.cs", "candidate", "library"),
            }, check.Reasons);
        }
    }

    [TestMethod]
    public void PlanDeletedProjectAndReferencePreserveCrossRevisionConsumerClosure()
    {
        RepositoryFacts basis = Scenario.Facts("base", Scenario.Project("removed"),
            Scenario.Project("consumer", dependencies: ["removed"]), Scenario.Project(
                "application"));
        RepositoryFacts candidate = Scenario.Facts("candidate", Scenario.Project("consumer"),
            Scenario.Project("application", dependencies: ["consumer"]), Scenario.Project(
                "unrelated"));

        CiPlan plan = ImpactPlanner.Plan(new(basis, candidate, ["src/removed/code.cs"], false));

        string[] expected = ["consumer", "application"];

        CollectionAssert.AreEquivalent(expected,
            plan.Checks.Select(x => x.Work.Key.Target).ToArray());
        foreach (PlannedCheck check in plan.Checks)
            Assert.AreEqual(new("src/removed/code.cs", "base", "removed"),
                Assert.ContainsSingle(check.Reasons));
    }

    [TestMethod]
    public void PlanRenameBetweenProjectsSelectsBothPathOwners()
    {
        CiPlan plan = ImpactPlanner.Plan(Scenario.Request(
            [Scenario.Project("old"), Scenario.Project("new"), Scenario.Project("unrelated")],
            "src/old/name.cs", "src/new/name.cs"));

        string[] expected = ["old", "new"];

        CollectionAssert.AreEquivalent(expected,
            plan.Checks.Select(x => x.Work.Key.Target).ToArray());
        Assert.Contains(new SelectionReason("src/old/name.cs", "base", "old"),
            plan.Checks.Single(x => x.Work.Key.Target == "old").Reasons);
        Assert.Contains(new SelectionReason("src/new/name.cs", "candidate", "new"),
            plan.Checks.Single(x => x.Work.Key.Target == "new").Reasons);
    }

    [TestMethod]
    public void PlanReleaseUnitChangeSelectsEveryMemberAndVariant()
    {
        CheckSpec linux = Scenario.Check("package", variant: "linux") with
        {
            Dimensions = new() { ["rid"] = "linux-x64" },
        };
        CheckSpec windows = Scenario.Check("package", variant: "windows") with
        {
            Runner = "windows-latest",
            Dimensions = new() { ["rid"] = "win-x64" },
        };
        CiPlan plan = ImpactPlanner.Plan(Scenario.Request(
        [
            Scenario.Project("entry", releaseUnit: "product"),
            Scenario.Project("package", releaseUnit: "product", checks: [linux, windows]),
            Scenario.Project("unrelated"),
        ], "src/entry/code.cs"));

        CollectionAssert.AreEquivalent(new[] { Scenario.Check("entry").Key, linux.Key,
            windows.Key },
            plan.Checks.Select(x => x.Work.Key).ToArray());
        PlannedCheck selected = plan.Checks.Single(x => x.Work.Key == windows.Key);
        Assert.AreEqual("windows-latest", selected.Work.Runner);
        Assert.AreEqual("win-x64", selected.Work.Dimensions["rid"]);
    }

    [TestMethod]
    public void PlanSharedInputSelectsActualConsumersAndTheirDependents()
    {
        RepositoryFacts basis = Scenario.Facts("base", Scenario.Project("library"),
            Scenario.Project("consumer", dependencies: ["library"]), Scenario.Project(
                "unrelated")) with
        {
            SharedInputs = [new("locks/native.lock", ["library"])],
        };
        CiPlan plan = ImpactPlanner.Plan(new(basis, basis with { Revision = "candidate" },
            ["locks/native.lock"], false));

        string[] expected = ["library", "consumer"];

        CollectionAssert.AreEquivalent(expected,
            plan.Checks.Select(x => x.Work.Key.Target).ToArray());
        Assert.Contains(new SelectionReason("locks/native.lock", "candidate", "library"),
            plan.Checks.Single(x => x.Work.Key.Target == "consumer").Reasons);
    }

    [TestMethod]
    public void PlanNestedProjectSelectsNearestOwnerOnly()
    {
        CiPlan plan = ImpactPlanner.Plan(Scenario.Request(
            [Scenario.Project("parent"), Scenario.Project("nested", directory:
                "src/parent/nested")],
            "src/parent/nested/code.cs"));

        Assert.AreEqual("nested", Assert.ContainsSingle(plan.Checks).Work.Key.Target);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void PlanRootWithoutOwnedPathsRejectsEitherRevisionIncludingFullMode(bool basis,
        bool full)
    {
        ProjectFacts root = Scenario.Project("root", directory: ".") with { OwnedPaths = [] };
        PlanRequest request = Scenario.Request([root]) with { Full = full };
        ProjectFacts invalid = root with { OwnedPaths = null };
        request = basis
            ? request with { Basis = request.Basis with { Projects = [invalid] } }
            : request with { Candidate = request.Candidate with { Projects = [invalid] } };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(request));

        Assert.Contains("Root project root requires explicit owned paths", error.Message);
    }

    [TestMethod]
    public void PlanEmptyRootOwnershipCanBeSelectedByExplicitInput()
    {
        ProjectFacts root = Scenario.Project("root", directory: ".") with { OwnedPaths = [] };
        RepositoryFacts basis = Scenario.Facts("base", root) with
        {
            SharedInputs = [new("shared/input.txt", ["root"])],
        };
        PlanRequest request = new(basis, basis with { Revision = "candidate" }, [], false);

        Assert.IsEmpty(ImpactPlanner.Plan(request).Checks);
        CiPlan plan = ImpactPlanner.Plan(request with { ChangedPaths = ["shared/input.txt"] });

        PlannedCheck selected = Assert.ContainsSingle(plan.Checks);
        Assert.AreEqual(Scenario.Check("root").Key, selected.Work.Key);
        CollectionAssert.AreEquivalent(new[]
        {
            new SelectionReason("shared/input.txt", "base", "root"),
            new SelectionReason("shared/input.txt", "candidate", "root"),
        }, selected.Reasons);
    }

    [TestMethod]
    public void PlanEmptyRootOwnershipDoesNotOwnRepositoryPaths()
    {
        ProjectFacts root = Scenario.Project("root", directory: ".") with { OwnedPaths = [] };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(Scenario.Request([root], "unknown/file.cs")));

        Assert.Contains("Unresolved changed path: unknown/file.cs", error.Message);
    }

    [TestMethod]
    public void PlanKnownUnaffectedPathProducesEmptySuccessfulPlan()
    {
        CiPlan plan = ImpactPlanner.Plan(Scenario.Request([Scenario.Project("library")],
            "docs/guide.md"));

        Assert.IsEmpty(plan.Checks);
        Assert.AreEqual("base", plan.Comparison);
        Assert.AreEqual("candidate", plan.Candidate);
        Assert.AreEqual("fixture-projects", plan.Scope);
    }

    [TestMethod]
    public void PlanExplicitFullModeSelectsAllCandidateProjects()
    {
        PlanRequest request = Scenario.Request([Scenario.Project("library"), Scenario.Project(
            "other")]);

        CiPlan plan = ImpactPlanner.Plan(request with { Full = true });

        string[] expected = ["library", "other"];

        CollectionAssert.AreEquivalent(expected,
            plan.Checks.Select(x => x.Work.Key.Target).ToArray());
        foreach (PlannedCheck check in plan.Checks)
            Assert.AreEqual(new("<full>", "candidate", check.Work.Key.Target),
                Assert.ContainsSingle(check.Reasons));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PlanUnresolvedPathRejectsWithoutFullFallback(bool full)
    {
        PlanRequest request = Scenario.Request([Scenario.Project("library")],
            "unknown/file.cs") with
        { Full = full };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(request));

        Assert.Contains("Unresolved changed path", error.Message);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void PlanIncompleteFactsRejectsEitherRevisionIncludingFullMode(bool basis, bool full)
    {
        PlanRequest request = Scenario.Request([Scenario.Project("library")],
            "src/library/code.cs") with
        { Full = full };
        request = basis
            ? request with { Basis = request.Basis with { Errors = ["native evaluation failed"] } }
            : request with
            {
                Candidate = request.Candidate with
                {
                    Errors =
                ["native evaluation failed"]
                }
            };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(request));

        Assert.Contains("native evaluation failed", error.Message);
    }

    [TestMethod]
    public void PlanRejectsMissingDependencyAndQualityConsumer()
    {
        ProjectFacts project = Scenario.Project("library");
        PlanRequest request = Scenario.Request([project], "src/library/code.cs");
        AssertCandidateRejected(request, request.Candidate with
        { Projects = [project with { Dependencies = ["missing"] }] }, "Unresolved relation");
        AssertCandidateRejected(request, request.Candidate with
        { Projects = [project with { QualityConsumers = ["missing"] }] }, "Unresolved relation");
    }

    [TestMethod]
    public void PlanRejectsUnresolvedSharedInputAndDuplicateProject()
    {
        ProjectFacts project = Scenario.Project("library");
        PlanRequest request = Scenario.Request([project], "src/library/code.cs");
        AssertCandidateRejected(request, request.Candidate with
        { SharedInputs = [new("build.props", ["missing"])] }, "Unresolved shared input");
        AssertCandidateRejected(request, request.Candidate with
        { Projects = [project, project] }, "Duplicate project");
    }

    [TestMethod]
    public void PlanRejectsSelectedOwnerWithoutPresetOrChecks()
    {
        ProjectFacts project = Scenario.Project("library");
        PlanRequest request = Scenario.Request([project], "src/library/code.cs");
        AssertCandidateRejected(request, request.Candidate with
        { Projects = [project with { QualityPreset = null }] }, "Unresolved quality contract");
        AssertCandidateRejected(request, request.Candidate with
        { Projects = [project with { Checks = [] }] }, "Unresolved quality contract");
    }

    [TestMethod]
    public void PlanRejectsMismatchedCoverageScopes()
    {
        PlanRequest request = Scenario.Request([Scenario.Project("library")],
            "src/library/code.cs");
        AssertCandidateRejected(request, request.Candidate with { Scope = "unsupported-subset" },
            "coverage scopes differ");
    }

    [TestMethod]
    public void PlanRejectsAmbiguousDimensionsRunnerOrPrerequisitesForSharedCheck()
    {
        CheckSpec check = Scenario.Check("shared") with
        { Dimensions = new() { ["tfm"] = "net10.0" } };
        CheckSpec[] conflicts =
        [
            check with { Dimensions = [] },
            check with { Dimensions = new() { ["tfm"] = "net9.0" } },
            check with { Runner = "windows-latest" },
            check with { Prerequisites = [Scenario.Check("missing").Key] },
        ];
        foreach (CheckSpec conflict in conflicts)
        {
            AssertCheckConflict(Scenario.Request(
                [Scenario.Project("a", checks: [check]), Scenario.Project("b", checks: [conflict])],
                "src/a/code.cs"));
        }
    }

    [TestMethod]
    [DataRow(false, "Missing prerequisite")]
    [DataRow(true, "Cyclic check prerequisites")]
    public void PlanBrokenPrerequisiteGraphRejectsBeforeExecution(bool cycle, string message)
    {
        CheckSpec build = Scenario.Check("library", "build");
        CheckSpec test = Scenario.Check("library", prerequisites: [build.Key]);
        CheckSpec[] checks = cycle ? [test, build with { Prerequisites = [test.Key] }] : [test];

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(
            Scenario.Request([Scenario.Project("library", checks: checks)],
                "src/library/code.cs")));

        Assert.Contains(message, error.Message);
    }

    [TestMethod]
    public void PlanDuplicateSelectedCheckMergesPresetsReasonsAndRequiredStatus()
    {
        CheckSpec advisory = Scenario.Check("shared", required: false);
        CiPlan plan = ImpactPlanner.Plan(Scenario.Request(
        [
            Scenario.Project("a", preset: "advisory", checks: [advisory]),
            Scenario.Project("b", preset: "required", checks: [advisory with { Required = true }]),
        ], "src/a/code.cs", "src/b/code.cs"));

        PlannedCheck selected = Assert.ContainsSingle(plan.Checks);
        Assert.IsTrue(selected.Work.Required);
        string[] expected = ["advisory", "required"];
        CollectionAssert.AreEquivalent(expected, selected.QualityPresets);
        Assert.HasCount(4, selected.Reasons);
        Assert.Contains(new SelectionReason("src/a/code.cs", "base", "a"), selected.Reasons);
        Assert.Contains(new SelectionReason("src/b/code.cs", "candidate", "b"), selected.Reasons);
    }

    [TestMethod]
    public void PlanUnselectedRequiredContractDoesNotPromoteSelectedAdvisoryCheck()
    {
        CheckSpec advisory = Scenario.Check("shared", required: false);
        CiPlan plan = ImpactPlanner.Plan(Scenario.Request(
        [
            Scenario.Project("selected", preset: "advisory", checks: [advisory]),
            Scenario.Project("unrelated", preset: "strict", checks: [advisory with { Required =
                true }]),
        ], "src/selected/code.cs"));

        PlannedCheck selected = Assert.ContainsSingle(plan.Checks);
        Assert.IsFalse(selected.Work.Required);
        string[] expected = ["advisory"];
        CollectionAssert.AreEqual(expected, selected.QualityPresets);
    }

    [TestMethod]
    public void PlanRequiredCheckPromotesEntirePrerequisiteClosure()
    {
        CheckSpec restore = Scenario.Check("native", "restore", required: false);
        CheckSpec build = Scenario.Check("native", "build", required: false, prerequisites:
            [restore.Key]);
        CheckSpec test = Scenario.Check("selected", prerequisites: [build.Key]);
        CiPlan plan = ImpactPlanner.Plan(Scenario.Request(
        [
            Scenario.Project("selected", preset: "required", checks: [test]),
            Scenario.Project("native", preset: "advisory", checks: [build, restore]),
        ], "src/selected/code.cs"));

        CollectionAssert.AreEquivalent(new[] { test.Key, build.Key, restore.Key },
            plan.Checks.Select(x => x.Work.Key).ToArray());
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.IsTrue(check.Work.Required);
            string[] expected = ["required"];
            CollectionAssert.AreEqual(expected, check.QualityPresets);
            Assert.Contains(new SelectionReason("src/selected/code.cs", "candidate",
                "selected"), check.Reasons);
        }
    }

    [TestMethod]
    public void PlanAdvisoryPrerequisiteDoesNotAdoptUnselectedRequiredPolicy()
    {
        CheckSpec build = Scenario.Check("native", "build", required: false);
        CheckSpec test = Scenario.Check("selected", required: false, prerequisites: [build.Key]);
        CiPlan plan = ImpactPlanner.Plan(Scenario.Request(
        [
            Scenario.Project("selected", preset: "advisory", checks: [test]),
            Scenario.Project("native", checks: [build]),
            Scenario.Project("unrelated", preset: "strict", checks: [build with { Required =
                true }]),
        ], "src/selected/code.cs"));

        Assert.HasCount(2, plan.Checks);
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.IsFalse(check.Work.Required);
            string[] expected = ["advisory"];
            CollectionAssert.AreEqual(expected, check.QualityPresets);
        }
    }

    [TestMethod]
    public void PlanSharedPrerequisitePropagatesLateRequiredPromotionAndAllReasons()
    {
        CheckSpec restore = Scenario.Check("native", "restore", required: false);
        CheckSpec build = Scenario.Check("native", "build", required: false, prerequisites:
            [restore.Key]);
        CheckSpec package = Scenario.Check("native", "pack", required: false, prerequisites:
            [build.Key]);
        CheckSpec advisory = Scenario.Check("advisory", required: false, prerequisites:
            [build.Key]);
        CheckSpec required = Scenario.Check("required", prerequisites: [package.Key]);

        CiPlan plan = ImpactPlanner.Plan(Scenario.Request(
        [
            Scenario.Project("advisory", preset: "optional", checks: [advisory]),
            Scenario.Project("required", preset: "strict", checks: [required]),
            Scenario.Project("native", checks: [restore, build, package]),
        ], "src/advisory/code.cs", "src/required/code.cs"));

        Assert.HasCount(5, plan.Checks);
        Assert.IsFalse(plan.Checks.Single(x => x.Work.Key == advisory.Key).Work.Required);
        foreach (CheckKey key in new[] { build.Key, restore.Key })
        {
            PlannedCheck selected = plan.Checks.Single(x => x.Work.Key == key);
            Assert.IsTrue(selected.Work.Required);
            string[] expectedPresets = ["optional", "strict"];
            CollectionAssert.AreEquivalent(expectedPresets, selected.QualityPresets);
            Assert.HasCount(4, selected.Reasons);
            Assert.Contains(new SelectionReason("src/advisory/code.cs", "base", "advisory"),
                selected.Reasons);
            Assert.Contains(new SelectionReason("src/required/code.cs", "candidate",
                "required"), selected.Reasons);
        }
    }

    [TestMethod]
    [DataRow("../src/library/code.cs")]
    [DataRow("/src/library/code.cs")]
    [DataRow("src//library/code.cs")]
    [DataRow("src/./library/code.cs")]
    [DataRow("src\\library\\code.cs")]
    [DataRow("C:/src/library/code.cs")]
    public void PlanUnnormalizedChangedPathRejectsAmbiguousOwnership(string path)
    {
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(
            Scenario.Request([Scenario.Project("library")], path)));

        Assert.Contains("normalized repository-relative path", error.Message);
    }
    [TestMethod]
    public void PlanRejectsConflictingPackageSubjectsForIdenticalCheckKey()
    {
        PackageTarget subject = PackageSubject();
        CheckSpec first = Scenario.Check("release/product/build", "artifact") with
        { Package = subject };
        PackageTarget[] conflicts =
        [
            subject with { Directory = "src/other" },
            subject with { ExpectedVersion = "2.0.0" },
            subject with { Outputs = [new("different", "primary-package", "npm-tarball")] },
        ];
        foreach (PackageTarget conflict in conflicts)
            AssertCheckConflict(Scenario.Request(
                [Scenario.Project("a", checks: [first]),
                    Scenario.Project("b", checks: [first with { Package = conflict }])],
                "src/a/code.cs", "src/b/code.cs"));
    }

    [TestMethod]
    public void PlanDeduplicatesEquivalentPackageSubjectsWithDifferentOutputArrays()
    {
        PackageTarget subject = PackageSubject();
        CheckSpec first = Scenario.Check("release/product/build", "artifact") with
        {
            Package = subject
        };
        CheckSpec second = first with { Package = subject with { Outputs = [.. subject.Outputs] } };

        CiPlan plan = ImpactPlanner.Plan(Scenario.Request(
            [Scenario.Project("a", checks: [first]), Scenario.Project("b", checks: [second])],
            "src/a/code.cs", "src/b/code.cs"));

        PlannedCheck selected = Assert.ContainsSingle(plan.Checks);
        Assert.AreEqual(first.Key, selected.Work.Key);
        Assert.AreEqual("1.2.3", selected.Work.Package!.ExpectedVersion);
        Assert.AreEqual(new PackageOutput("package", "primary-package", "npm-tarball"),
            Assert.ContainsSingle(selected.Work.Package.Outputs));
        Assert.HasCount(4, selected.Reasons);
    }

    [TestMethod]
    public void PairedReasonSelectsCandidateOnlyOwnerAndCompleteSharedClosure()
    {
        CheckSpec prepare = Scenario.Check("tooling", "restore", required: false);
        CheckSpec linux = Scenario.Check("c", variant: "linux", prerequisites: [prepare.Key])
            with
        { Dimensions = new() { ["rid"] = "linux-x64" } };
        CheckSpec windows = Scenario.Check("c", variant: "windows", prerequisites: [prepare.Key])
            with
        { Runner = "windows-latest", Dimensions = new() { ["rid"] = "win-x64" } };
        ProjectFacts owner = Scenario.Project("c", qualityConsumers: ["quality"],
            releaseUnit: "product", checks: [linux, windows]) with
        { QualityPreset = null, Origin = CheckOrigin.NativeRetained };
        ProjectFacts sibling = Scenario.Project("sibling", releaseUnit: "product");
        ProjectFacts consumer = Scenario.Project("consumer", dependencies: [owner.Id]);
        ProjectFacts quality = Scenario.Project("quality");
        var request = new PlanRequest(Scenario.Facts("base"), Scenario.Facts("candidate",
            owner, sibling, consumer, quality, Scenario.Project("tooling", checks: [prepare]),
            Scenario.Project("unrelated")), PairedInputPaths, false);
        var reason = new SelectionReason(PairedInput, "base", owner.Id);

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
            Scenario.Project("removed", qualityConsumers: PairedConsumer),
            Scenario.Project("consumer"));
        RepositoryFacts candidate = Scenario.Facts("candidate", Scenario.Project("consumer"),
            Scenario.Project("new", dependencies: PairedConsumer), Scenario.Project("unrelated"));
        var reason = new SelectionReason(PairedInput, "base", "removed");

        CiPlan plan = ImpactPlanner.Plan(new(basis, candidate, PairedInputPaths, false), [reason]);

        CollectionAssert.AreEquivalent(PairedSurvivingTargets,
            plan.Checks.Select(check => check.Work.Key.Target).ToArray());
        foreach (PlannedCheck check in plan.Checks)
            Assert.AreEqual(reason, Assert.ContainsSingle(check.Reasons));
    }

    [TestMethod]
    public void PairedAndNativeReasonsRetainBothEndpointProvenance()
    {
        const string native = "src/c/code.py";
        PlanRequest request = Scenario.Request([Scenario.Project("c")], PairedInput, native);
        var basis = new SelectionReason(PairedInput, "base", "c");
        var candidate = new SelectionReason(PairedInput, "candidate", "c");

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
        PlanRequest request = Scenario.Request([Scenario.Project("c")], PairedInput,
            "unknown/file.py") with
        { Full = full };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(request, [new(PairedInput, "base", "c")]));

        Assert.Contains("Unresolved changed path: unknown/file.py", error.Message);
    }

    [TestMethod]
    [DataRow("src/c/code.py", "base", "foreign", false)]
    [DataRow("src/c/code.py", "base", "foreign", true)]
    [DataRow("src/c/code.py", "foreign", "c", false)]
    [DataRow("src/c/code.py", "foreign", "c", true)]
    [DataRow("src/c/unchanged.py", "base", "c", false)]
    [DataRow("src/c/unchanged.py", "base", "c", true)]
    [DataRow("../outside.py", "base", "c", false)]
    [DataRow("../outside.py", "base", "c", true)]
    public void InvalidPairedReasonRejectsWithoutFullFallback(string path, string revision,
        string owner, bool full)
    {
        PlanRequest request = Scenario.Request([Scenario.Project("c")], "src/c/code.py") with
        { Full = full };
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(request, [new(path, revision, owner)]));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void PairedReasonsDoNotBypassEndpointFactValidation(bool invalidBasis, bool full)
    {
        PlanRequest request = Scenario.Request([Scenario.Project("c")], PairedInput) with
        { Full = full };
        string[] errors = ["incomplete native scope"];
        request = invalidBasis
            ? request with { Basis = request.Basis with { Errors = errors } }
            : request with { Candidate = request.Candidate with { Errors = errors } };

        Assert.Contains(errors[0], Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(request, [new(PairedInput, "base", "c")])).Message);
    }

    [TestMethod]
    public void PairedReasonsDoNotBypassSelectedQualityContract()
    {
        foreach (ProjectFacts owner in MalformedQualityContracts(Scenario.Project("c")))
        {
            var request = new PlanRequest(Scenario.Facts("base"),
                Scenario.Facts("candidate", owner), PairedInputPaths, false);

            Assert.Contains("Unresolved quality contract for c",
                Assert.ThrowsExactly<InvalidDataException>(() =>
                    ImpactPlanner.Plan(request, [new(PairedInput, "base", owner.Id)])).Message);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PairedReasonsRejectMalformedDeletedOwnersBeforeClosure(bool full)
    {
        ProjectFacts consumer = Scenario.Project("consumer");
        ProjectFacts removed = Scenario.Project("removed", qualityConsumers: PairedConsumer);
        foreach (ProjectFacts owner in MalformedQualityContracts(removed))
        {
            var request = new PlanRequest(Scenario.Facts("base", owner, consumer),
                Scenario.Facts("candidate", consumer), PairedInputPaths, full);

            Assert.Contains("Unresolved quality contract for removed",
                Assert.ThrowsExactly<InvalidDataException>(() => ImpactPlanner.SelectProjects(
                    request, [new(PairedInput, "base", owner.Id)])).Message);
        }
    }

    [TestMethod]
    public void PairedReasonPreservesExplicitFullMode()
    {
        PlanRequest request = Scenario.Request([Scenario.Project("c"),
            Scenario.Project("other")], PairedInput) with
        { Full = true };
        var reason = new SelectionReason(PairedInput, "base", "c");

        CiPlan plan = ImpactPlanner.Plan(request, [reason]);

        CollectionAssert.AreEquivalent(PairedFullTargets,
            plan.Checks.Select(check => check.Work.Key.Target).ToArray());
        CollectionAssert.AreEquivalent(new[] { reason,
            new SelectionReason("<full>", "candidate", "c") },
            plan.Checks.Single(check => check.Work.Key.Target == "c").Reasons);
        Assert.AreEqual(new SelectionReason("<full>", "candidate", "other"),
            Assert.ContainsSingle(plan.Checks.Single(check =>
                check.Work.Key.Target == "other").Reasons));
    }

    private static ProjectFacts[] MalformedQualityContracts(ProjectFacts owner) =>
    [
        owner with { Checks = [] },
        owner with { QualityPreset = null },
        owner with { Origin = CheckOrigin.NativeRetained },
    ];

    private static void AssertCandidateRejected(PlanRequest request, RepositoryFacts candidate,
        string diagnostic) => Assert.Contains(diagnostic,
            Assert.ThrowsExactly<InvalidDataException>(() =>
                ImpactPlanner.Plan(request with { Candidate = candidate })).Message);

    private static void AssertCheckConflict(PlanRequest request) =>
        Assert.Contains("Conflicting check definition",
            Assert.ThrowsExactly<InvalidDataException>(() => ImpactPlanner.Plan(request)).Message);

    private static PackageTarget PackageSubject() => new("product",
        "workflow-delivery.release-unit.yml", "build", "node/npm-package-v1",
        "src/a", "src/a/package.json", null, "1.2.3",
        [new("package", "primary-package", "npm-tarball")]);
}
