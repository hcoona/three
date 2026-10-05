using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NodeGroupPlanningTests
{
    private const string Adopted = "src/adopted";
    private const string Retained = "src/retained";

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
    [DataRow("missing")]
    [DataRow("unsupported")]
    [DataRow("script")]
    public async Task SelectedAdoptedContractCannotTurnIntoRetainedExecution(string defect)
    {
        NodeRevisionInputs candidate = Inputs(NodeScenario.Candidate);
        if (defect != "script")
            candidate = candidate with
            {
                Quality = new Dictionary<string, QualitySelection?>(candidate.Quality)
                {
                    [Adopted] = defect == "missing" ? null
                        : new("node/unsupported-v1", Adopted + "/workflow-delivery.quality.yml"),
                },
            };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Plan(
            Inputs(NodeScenario.Basis), candidate, [Adopted + "/index.js"],
            scripts: (project, _) => Task.FromResult(defect == "script"
                ? new PnpmScripts(project.Directory, new Dictionary<string, string>())
                : NodeScenario.Scripts(project))));
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
    [DataRow("unknown")]
    [DataRow("revision")]
    [DataRow("native")]
    [DataRow("adopted")]
    public async Task IncompleteInputsCannotYieldSuccessfulRetainedApplicability(string defect)
    {
        NodeRevisionInputs basis = Inputs(NodeScenario.Basis);
        NodeRevisionInputs candidate = Inputs(NodeScenario.Candidate);
        string[] paths = defect == "unknown" ? ["unknown/file.txt"] : [];
        NodeScopeInputs scope = Scope(basis, candidate, paths);
        if (defect == "revision")
            scope = scope with { Comparison = scope.Comparison with { Candidate = "other" } };
        if (defect == "native")
            candidate = candidate with
            {
                Graph = candidate.Graph with { Projects = [NodeScenario.Project(".")] },
            };
        if (defect == "unknown")
            scope = scope with { Candidate = [new(paths[0], true, "100644", [])] };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => NodeGroupPlanning.PlanAsync(
            basis, candidate, scope, defect == "adopted" ? "src/missing" : Adopted,
            (project, _) => Task.FromResult(NodeScenario.Scripts(project)),
            (_, _) => Task.FromResult("1.2.3"), CancellationToken.None));
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

    private static NodeScopeInputs Scope(NodeRevisionInputs basis,
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
