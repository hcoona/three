using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class ImpactPlannerTests
{
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
    [DataRow("duplicate")]
    [DataRow("invalid")]
    public void PlanMalformedExactOwnershipFailsBeforeSelection(string defect)
    {
        ProjectFacts project = Scenario.Project("native") with
        {
            OwnedPaths = defect == "duplicate" ? ["src/native/code.cs", "src/native/code.cs"]
                : ["../src/native/code.cs"],
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
    [DataRow("empty")]
    [DataRow("preset")]
    [DataRow("unknown")]
    public void PlanNativeRetainedDoesNotAcceptAnUnresolvedOrContradictoryContract(string defect)
    {
        ProjectFacts native = Scenario.Project("native") with
        {
            QualityPreset = defect == "preset" ? "invented-adoption" : null,
            Origin = defect == "unknown" ? (CheckOrigin)42 : CheckOrigin.NativeRetained,
            Checks = defect == "empty" ? [] : [Scenario.Check("native")],
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
    [DataRow("dependency", "Unresolved relation")]
    [DataRow("quality-consumer", "Unresolved relation")]
    [DataRow("shared-input", "Unresolved shared input")]
    [DataRow("duplicate", "Duplicate project")]
    [DataRow("quality-preset", "Unresolved quality contract")]
    [DataRow("quality-checks", "Unresolved quality contract")]
    [DataRow("scope", "coverage scopes differ")]
    public void PlanInvalidFactsRejectsUnresolvedContracts(string defect, string message)
    {
        ProjectFacts project = Scenario.Project("library");
        PlanRequest request = Scenario.Request([project], "src/library/code.cs");
        RepositoryFacts candidate = request.Candidate;
        candidate = defect switch
        {
            "dependency" => candidate with
            {
                Projects = [project with { Dependencies =
                ["missing"] }]
            },
            "quality-consumer" => candidate with
            {
                Projects = [project with { QualityConsumers
                = ["missing"] }]
            },
            "shared-input" => candidate with { SharedInputs = [new("build.props", ["missing"])] },
            "duplicate" => candidate with { Projects = [project, project] },
            "quality-preset" => candidate with
            {
                Projects = [project with { QualityPreset =
                null }]
            },
            "quality-checks" => candidate with { Projects = [project with { Checks = [] }] },
            "scope" => candidate with { Scope = "unsupported-subset" },
            _ => throw new InvalidOperationException(defect),
        };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(
            () => ImpactPlanner.Plan(request with { Candidate = candidate }));

        Assert.Contains(message, error.Message);
    }

    [TestMethod]
    [DataRow("dimensions")]
    [DataRow("dimension-value")]
    [DataRow("runner")]
    [DataRow("prerequisites")]
    public void PlanConflictingCheckDefinitionRejectsAmbiguousWork(string field)
    {
        CheckSpec check = Scenario.Check("shared") with
        {
            Dimensions = new()
            {
                ["tfm"] =
            "net10.0"
            }
        };
        CheckSpec conflicting = field switch
        {
            "dimensions" => check with { Dimensions = [] },
            "dimension-value" => check with { Dimensions = new() { ["tfm"] = "net9.0" } },
            "runner" => check with { Runner = "windows-latest" },
            "prerequisites" => check with { Prerequisites = [Scenario.Check("missing").Key] },
            _ => throw new InvalidOperationException(field),
        };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(
            Scenario.Request([Scenario.Project("a", checks: [check]), Scenario.Project("b",
                checks: [conflicting])],
                "src/a/code.cs")));

        Assert.Contains("Conflicting check definition", error.Message);
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
    [DataRow("directory")]
    [DataRow("version")]
    [DataRow("output")]
    public void PlanRejectsConflictingPackageSubjectForIdenticalCheckKey(string field)
    {
        PackageTarget subject = PackageSubject();
        PackageTarget other = field switch
        {
            "directory" => subject with { Directory = "src/other" },
            "version" => subject with { ExpectedVersion = "2.0.0" },
            _ => subject with
            {
                Outputs = [new("different", "primary-package", "npm-tarball")],
            },
        };
        CheckSpec first = Scenario.Check("release/product/build", "artifact") with
        {
            Package = subject
        };
        CheckSpec second = first with { Package = other };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ImpactPlanner.Plan(Scenario.Request(
                [Scenario.Project("a", checks: [first]), Scenario.Project("b", checks: [second])],
                "src/a/code.cs", "src/b/code.cs")));

        Assert.Contains("Conflicting check definition", error.Message);
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

    private static PackageTarget PackageSubject() => new("product",
        "workflow-delivery.release-unit.yml", "build", "node/npm-package-v1",
        "src/a", "src/a/package.json", null, "1.2.3",
        [new("package", "primary-package", "npm-tarball")]);


}
