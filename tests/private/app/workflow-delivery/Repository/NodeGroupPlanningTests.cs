using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NodeGroupPlanningTests
{
    private const string Adopted = "src/adopted";
    private const string Retained = "src/retained";
    private static readonly string[] AdoptedChecks =
        ["node/project-build-v1", "node/project-test-v1"];

    [TestMethod]
    public async Task SharedChangesKeepAdoptedChecksAndRetainedMemberReasons()
    {
        NodeRevisionInputs basis = Inputs(NodeScenario.Basis);
        NodeRevisionInputs candidate = Inputs(NodeScenario.Candidate);
        NodeGroupPlan group = await Plan(basis, candidate, ["pnpm-lock.yaml"]);

        Assert.HasCount(2, group.Adopted.Checks);
        foreach (PlannedCheck check in group.Adopted.Checks)
        {
            Assert.AreEqual(Adopted, check.Work.Key.Target);
            CollectionAssert.AreEquivalent(new[]
            {
                new SelectionReason("pnpm-lock.yaml", NodeScenario.Basis, Adopted),
                new SelectionReason("pnpm-lock.yaml", NodeScenario.Candidate, Adopted),
            }, check.Reasons);
        }
        RetainedNodeSelection member = Assert.ContainsSingle(group.Retained);
        Assert.AreEqual(Retained, member.Directory);
        CollectionAssert.AreEquivalent(new[]
        {
            new SelectionReason("pnpm-lock.yaml", NodeScenario.Basis, Retained),
            new SelectionReason("pnpm-lock.yaml", NodeScenario.Candidate, Retained),
        }, member.Reasons);
    }

    [TestMethod]
    public async Task RetainedOnlyChangesNeedNoReplacementPresetOrHydration()
    {
        NodeGroupPlan group = await Plan(Inputs(NodeScenario.Basis),
            Inputs(NodeScenario.Candidate), [Retained + "/index.js"],
            scripts: (_, _) => throw new InvalidOperationException("Retained script hydration."),
            version: (_, _) => throw new InvalidOperationException("Retained version hydration."));

        Assert.IsEmpty(group.Adopted.Checks);
        RetainedNodeSelection member = Assert.ContainsSingle(group.Retained);
        Assert.AreEqual(Retained, member.Directory);
        Assert.HasCount(2, member.Reasons);
        Assert.AreEqual(NodeScenario.Basis, group.Adopted.Comparison);
        Assert.AreEqual(NodeScenario.Candidate, group.Adopted.Candidate);
    }

    [TestMethod]
    public async Task NativeReverseConsumersRemainSelectedAcrossExecutionPartition()
    {
        NodeRevisionInputs basis = Inputs(NodeScenario.Basis, dependent: true);
        NodeRevisionInputs candidate = Inputs(NodeScenario.Candidate, dependent: true);

        NodeGroupPlan group = await Plan(basis, candidate, [Retained + "/index.js"]);

        Assert.HasCount(2, group.Adopted.Checks);
        Assert.AreEqual(Retained, Assert.ContainsSingle(group.Retained).Directory);
        foreach (PlannedCheck check in group.Adopted.Checks)
            CollectionAssert.AreEquivalent(new[]
            {
                new SelectionReason(Retained + "/index.js", NodeScenario.Basis, Retained),
                new SelectionReason(Retained + "/index.js", NodeScenario.Candidate, Retained),
            }, check.Reasons);
    }

    [TestMethod]
    public async Task RemovedRelationsRetainTheirBasisSelectionReasons()
    {
        NodeRevisionInputs basis = Inputs(NodeScenario.Basis, dependent: true);
        NodeRevisionInputs candidate = Inputs(NodeScenario.Candidate);
        basis = basis with
        {
            Revision = basis.Revision with
            {
                Entries = [.. basis.Revision.Entries,
                NodeScenario.File("external/deleted.txt")]
            },
            OperationInputs = [.. basis.OperationInputs,
                new("external/deleted.txt", [Retained])],
        };

        NodeGroupPlan group = await Plan(basis, candidate, ["external/deleted.txt"]);

        RetainedNodeSelection member = Assert.ContainsSingle(group.Retained);
        var reason = new SelectionReason("external/deleted.txt", NodeScenario.Basis, Retained);
        Assert.AreEqual(reason, Assert.ContainsSingle(member.Reasons));
        foreach (PlannedCheck check in group.Adopted.Checks)
            Assert.AreEqual(reason, Assert.ContainsSingle(check.Reasons));
        Assert.HasCount(2, group.Adopted.Checks);
    }

    [TestMethod]
    public async Task NamedMovesKeepBothOwnersAndBasisReverseConsumerReasons()
    {
        const string oldAdopted = "src/old-adopted";
        const string oldRetained = "src/old-retained";
        const string newAdopted = "tests/new-adopted";
        const string newRetained = "tests/new-retained";
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project(oldAdopted, oldRetained) with { Name = "@example/adopted" },
            NodeScenario.Project(oldRetained) with { Name = "@example/retained" });
        NodeRevisionInputs candidate = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project(newAdopted) with { Name = "@example/adopted" },
            NodeScenario.Project(newRetained) with { Name = "@example/retained" });
        string changed = oldRetained + "/index.js";
        var reason = new SelectionReason(changed, NodeScenario.Basis, "npm:@example/retained");

        NodeGroupPlan group = await NodeGroupPlanning.PlanAsync(basis, candidate,
            Scope(basis, candidate, [changed]), newAdopted,
            (project, _) => project.Directory == newAdopted
                ? Task.FromResult(NodeScenario.Scripts(project))
                : throw new InvalidOperationException("Retained or basis script hydration."),
            (_, _) => throw new InvalidOperationException("Unneeded version hydration."),
            CancellationToken.None);

        CollectionAssert.AreEquivalent(AdoptedChecks,
            group.Adopted.Checks.Select(check => check.Work.Key.Check).ToArray());
        foreach (PlannedCheck check in group.Adopted.Checks)
        {
            Assert.AreEqual(newAdopted, check.Work.Key.Target);
            Assert.AreEqual(reason, Assert.ContainsSingle(check.Reasons));
        }
        RetainedNodeSelection member = Assert.ContainsSingle(group.Retained);
        Assert.AreEqual(newRetained, member.Directory);
        Assert.AreEqual(reason, Assert.ContainsSingle(member.Reasons));
    }

    [TestMethod]
    public async Task FullSelectsBothOwnersWithoutRequiringRetainedAdoption()
    {
        NodeRevisionInputs basis = Inputs(NodeScenario.Candidate);
        NodeRevisionInputs candidate = Inputs(NodeScenario.Candidate);

        NodeGroupPlan group = await Plan(basis, candidate, [], full: true);

        Assert.HasCount(2, group.Adopted.Checks);
        RetainedNodeSelection member = Assert.ContainsSingle(group.Retained);
        Assert.AreEqual(new("<full>", NodeScenario.Candidate, Retained),
            Assert.ContainsSingle(member.Reasons));
        foreach (PlannedCheck check in group.Adopted.Checks)
            Assert.AreEqual(new("<full>", NodeScenario.Candidate, Adopted),
                Assert.ContainsSingle(check.Reasons));
    }

    [TestMethod]
    public async Task OwnedUnrelatedAndEmptyPlansPreserveTheExactSubject()
    {
        NodeRevisionInputs basis = WithRecord(Inputs(NodeScenario.Basis));
        NodeRevisionInputs candidate = WithRecord(Inputs(NodeScenario.Candidate));
        NodeGroupPlan unrelated = await Plan(basis, candidate, ["docs/guide.md"]);
        NodeGroupPlan empty = await Plan(basis, candidate, []);

        foreach (NodeGroupPlan group in new[] { unrelated, empty })
        {
            Assert.IsEmpty(group.Adopted.Checks);
            Assert.IsEmpty(group.Retained);
            Assert.AreEqual(NodeScenario.Basis, group.Adopted.Comparison);
            Assert.AreEqual(NodeScenario.Candidate, group.Adopted.Candidate);
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("node/unsupported-v1")]
    [DataRow("python/hcoona-release-smoke-python-v1")]
    public async Task MissingOrUnsupportedAdoptionCannotFallBackToRetainedWork(string? preset)
    {
        NodeRevisionInputs candidate = Inputs(NodeScenario.Candidate);
        candidate = candidate with
        {
            Quality = new Dictionary<string, QualitySelection?>(candidate.Quality)
            {
                [Adopted] = preset is null ? null :
                    new(preset, Adopted + "/workflow-delivery.quality.yml"),
            },
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            Plan(Inputs(NodeScenario.Basis), candidate, [Adopted + "/index.js"]));
    }

    [TestMethod]
    [DataRow("Build", "test", "build")]
    [DataRow("build", "Test", "test")]
    public async Task AdoptedScriptsMustHaveTheirExactNames(
        string buildName, string testName, string missingScript)
    {
        var scripts = new Dictionary<string, string>
        {
            [buildName] = "opaque build",
            [testName] = "opaque test",
        };
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => Plan(Inputs(NodeScenario.Basis), Inputs(NodeScenario.Candidate),
                [Adopted + "/index.js"],
                scripts: (project, _) => Task.FromResult(new PnpmScripts(project.Directory,
                    scripts))));
        Assert.Contains("'" + missingScript + "'", failure.Message);
    }

    [TestMethod]
    [DataRow("", "node/project-test-v1")]
    [DataRow("echo 'opaque'; exit 5 && $(unknown)\necho λ", "node/project-build-v1")]
    public async Task AdoptedScriptsSelectRequiredWorkWithoutInterpretingBodies(string body,
        string missing)
    {
        const string changed = Adopted + "/index.js";
        NodeGroupPlan group = await Plan(Inputs(NodeScenario.Basis),
            Inputs(NodeScenario.Candidate), [changed],
            scripts: (project, _) => Task.FromResult(new PnpmScripts(project.Directory,
                new Dictionary<string, string>(StringComparer.Ordinal)
                { ["build"] = body, ["test"] = body })));

        CiPlan plan = group.Adopted;
        Assert.IsEmpty(group.Retained);
        CheckKey[] expected = AdoptedChecks.Select(check => new CheckKey(Adopted, check,
            "default")).ToArray();
        CollectionAssert.AreEquivalent(expected, plan.Checks.Select(check => check.Work.Key)
            .ToArray());
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.AreEqual(NodeScenario.Preset, Assert.ContainsSingle(check.QualityPresets));
            Assert.AreEqual("ubuntu-latest", check.Work.Runner);
            Assert.IsTrue(check.Work.Required);
            Assert.IsEmpty(check.Work.Dimensions);
            Assert.IsEmpty(check.Work.Prerequisites);
            Assert.Contains(new SelectionReason(changed, NodeScenario.Candidate, Adopted),
                check.Reasons);
        }

        CheckResult[] passed = plan.Checks.Select(check =>
            new CheckResult(plan.Candidate, check.Work.Key, CheckStatus.Passed)).ToArray();
        CiOutcome incomplete = ResultCollector.Collect(plan,
            passed.Where(result => result.Key.Check != missing).ToArray());
        Assert.IsFalse(incomplete.Satisfied);
        Assert.Contains(new CheckOutcome(new(Adopted, missing, "default"), true, "missing"),
            incomplete.Checks);
        Assert.IsTrue(ResultCollector.Collect(plan, passed).Satisfied);
    }

    [TestMethod]
    public async Task SelectedMixedUnitCannotOmitItsRetainedMember()
    {
        ReleaseUnitDeclaration unit = NodeScenario.Unit("mixed",
            ("adopted", Adopted), ("retained", Retained));
        NodeRevisionInputs basis = NodeScenario.Units(Inputs(NodeScenario.Basis), unit);
        NodeRevisionInputs candidate = NodeScenario.Units(Inputs(NodeScenario.Candidate), unit);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Plan(basis, candidate,
            [Adopted + "/index.js"]));
    }

    [TestMethod]
    public async Task SelectedAdoptedPackageHydratesOnlyItsNativeVersion()
    {
        ReleaseUnitDeclaration unit = NodeScenario.Unit("product", ("build", Adopted));
        NodeRevisionInputs basis = NodeScenario.Units(Inputs(NodeScenario.Basis), unit);
        NodeRevisionInputs candidate = NodeScenario.Units(Inputs(NodeScenario.Candidate), unit);
        NodeGroupPlan group = await Plan(basis, candidate, ["pnpm-lock.yaml"],
            version: (project, _) => project.Directory == Adopted
                ? Task.FromResult("1.2.3")
                : throw new InvalidOperationException("Retained version hydration."));

        Assert.HasCount(5, group.Adopted.Checks);
        Assert.AreEqual(Retained, Assert.ContainsSingle(group.Retained).Directory);
        Assert.HasCount(3, group.Adopted.Checks.Where(check => check.Work.Package is not null));
        foreach (PlannedCheck check in group.Adopted.Checks.Where(check =>
            check.Work.Package is not null))
            Assert.AreEqual("1.2.3", check.Work.Package!.ExpectedVersion);
    }

    [TestMethod]
    public async Task UnknownPresentChangeCannotReturnEmptyRetainedWork()
    {
        const string path = "unknown/file.txt";
        NodeRevisionInputs basis = Inputs(NodeScenario.Basis);
        NodeRevisionInputs candidate = Inputs(NodeScenario.Candidate);
        candidate = candidate with
        {
            Revision = candidate.Revision with
            { Entries = [.. candidate.Revision.Entries, NodeScenario.File(path)] },
        };

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => Plan(basis, candidate, [path]));
        Assert.Contains(path, failure.Message);
    }

    private static NodeRevisionInputs Inputs(string revision, bool dependent = false)
    {
        NodeRevisionInputs inputs = NodeScenario.Inputs(revision,
            NodeScenario.Project(Adopted, dependent ? [Retained] : []),
            NodeScenario.Project(Retained));
        return inputs with
        {
            Quality = new Dictionary<string, QualitySelection?>(inputs.Quality)
            { [Retained] = null },
            OperationInputs = [new("pnpm-lock.yaml", [Adopted, Retained])],
        };
    }

    private static NodeRevisionInputs WithRecord(NodeRevisionInputs inputs) => inputs with
    {
        Revision = inputs.Revision with
        {
            Entries = [.. inputs.Revision.Entries,
            NodeScenario.File("docs/guide.md")]
        },
    };

    private static ScopeInputs Scope(NodeRevisionInputs basis,
        NodeRevisionInputs candidate, string[] paths, bool full = false)
    {
        ScopeCoordinate[] Rows(GitRevision revision) => paths.Select(path =>
        {
            GitEntry? entry = revision.Entries.SingleOrDefault(entry => entry.Path == path);
            ScopeResponsibility[] reasons = path == "docs/guide.md"
                ? [new("record-system", path, "fixture-record-binding", ["package.json"])] : [];
            return new ScopeCoordinate(path, entry is not null, entry?.Mode,
                entry is null ? [] : reasons);
        }).ToArray();
        return new(new(basis.Revision.Commit, candidate.Revision.Commit, full), paths,
            Rows(basis.Revision), Rows(candidate.Revision));
    }

    private static Task<NodeGroupPlan> Plan(NodeRevisionInputs basis,
        NodeRevisionInputs candidate, string[] paths, bool full = false,
        Func<PnpmProject, CancellationToken, Task<PnpmScripts>>? scripts = null,
        Func<PnpmProject, CancellationToken, Task<string>>? version = null) =>
        NodeGroupPlanning.PlanAsync(basis, candidate, Scope(basis, candidate, paths, full),
            Adopted, scripts ?? ((project, _) => Task.FromResult(NodeScenario.Scripts(project))),
            version ?? ((_, _) => Task.FromResult("1.2.3")), CancellationToken.None);
}
