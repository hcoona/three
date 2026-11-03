using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NodeGroupReaderTests
{
    private const string Adopted = "src/adopted";
    private const string Retained = "src/retained";
    private static readonly string[] RuntimeSources = [NodeRuntimeReader.Schedule,
        "package.json", Adopted + "/package.json", Retained + "/package.json"];
    private static readonly string[] PlannedSelectors = ["20.x", "22.x", "24.x", "26.x"];

    [TestMethod]
    public async Task ControlInputsSelectAdoptedAndRetainedSurvivingOwners()
    {
        const string removed = "control/removed.cs";
        NodeRevisionInputs basis = Inputs(NodeScenario.Basis) with
        {
            Revision = Inputs(NodeScenario.Basis).Revision with
            {
                Entries = [.. Inputs(NodeScenario.Basis).Revision.Entries,
                NodeScenario.File(removed)]
            },
        };
        NodeRevisionInputs candidate = Inputs(NodeScenario.Candidate);
        basis = NodeGroupReader.WithControlInputs(basis,
            Control(NodeScenario.Basis, new ControlSourceInput(removed, "compiler Sources", true)),
            false);
        candidate = NodeGroupReader.WithControlInputs(candidate,
            Control(NodeScenario.Candidate), true);
        var scope = new ScopeInputs(new(NodeScenario.Basis, NodeScenario.Candidate, false),
            [removed], [new(removed, true, "100644", [])], [new(removed, false, null, [])]);

        NodeGroupPlan group = await NodeGroupPlanning.PlanAsync(basis, candidate, scope, Adopted,
            (project, _) => Task.FromResult(NodeScenario.Scripts(project)),
            (_, _) => Task.FromResult("1.2.3"), CancellationToken.None);

        Assert.HasCount(2, group.Adopted.Checks);
        RetainedNodeSelection retained = Assert.ContainsSingle(group.Retained);
        Assert.AreEqual(Retained, retained.Directory);
        Assert.AreEqual(new(removed, NodeScenario.Basis, Retained),
            Assert.ContainsSingle(retained.Reasons));
        foreach (PlannedCheck check in group.Adopted.Checks)
            Assert.AreEqual(new(removed, NodeScenario.Basis, Adopted),
                Assert.ContainsSingle(check.Reasons));
    }

    [TestMethod]
    [DataRow("eng/scripts/Bootstrap-CiControl.ps1")]
    [DataRow(".config/dotnet-tools.json")]
    public async Task CallerImportsAndDependencyPreparationSelectConcreteGroupOwners(string path)
    {
        NodeRevisionInputs basis = NodeGroupReader.WithControlInputs(Inputs(NodeScenario.Basis),
            Control(NodeScenario.Basis), false);
        NodeRevisionInputs candidate = Inputs(NodeScenario.Candidate);
        candidate = candidate with
        {
            Revision = candidate.Revision with
            { Entries = [.. candidate.Revision.Entries, NodeScenario.File(path)] },
        };
        candidate = NodeGroupReader.WithControlInputs(candidate,
            Control(NodeScenario.Candidate), true);
        var scope = new ScopeInputs(new(NodeScenario.Basis, NodeScenario.Candidate, false),
            [path], [new(path, false, null, [])], [new(path, true, "100644", [])]);

        NodeGroupPlan group = await NodeGroupPlanning.PlanAsync(basis, candidate, scope, Adopted,
            (project, _) => Task.FromResult(NodeScenario.Scripts(project)),
            (_, _) => Task.FromResult("1.2.3"), CancellationToken.None);

        Assert.HasCount(2, group.Adopted.Checks);
        RetainedNodeSelection retained = Assert.ContainsSingle(group.Retained);
        Assert.AreEqual(Retained, retained.Directory);
        foreach (PlannedCheck check in group.Adopted.Checks)
            Assert.Contains(new SelectionReason(path, NodeScenario.Candidate, Adopted),
                check.Reasons);
        Assert.Contains(new SelectionReason(path, NodeScenario.Candidate, Retained),
            retained.Reasons);
    }

    [TestMethod]
    public void NativeSourcesAndAbsentCandidatesPreserveExistingConsumerRelations()
    {
        NodeRevisionInputs inputs = Inputs(NodeScenario.Candidate) with
        { OperationInputs = [new("control/shared.props", [Retained])] };
        NodeRevisionInputs joined = NodeGroupReader.WithControlInputs(inputs,
            Control(NodeScenario.Candidate,
                new("control/shared.props", "import", true),
                new("control/optional.editorconfig", "native configuration candidate", false)),
            true);

        foreach (string path in (string[])["control/shared.props", "control/optional.editorconfig",
            .. NodeGroupReader.CallerSources, .. RuntimeSources])
        {
            SharedInput input = joined.OperationInputs.Single(input => input.Path == path);
            CollectionAssert.AreEquivalent(new[] { Adopted, Retained }, input.Consumers);
        }
        Assert.HasCount(NodeGroupReader.CallerSources.Length + RuntimeSources.Length + 2,
            joined.OperationInputs);
        Assert.AreSame(inputs.Graph, joined.Graph);
        Assert.AreSame(inputs.Revision, joined.Revision);
    }

    // Under the lowest-minimum rule one member's engines range plans the matrix for every
    // member, so changing it selects all members on the resulting matrix.
    [TestMethod]
    public async Task OneMemberEnginesChangeSelectsEveryMemberOnTheNewMatrix()
    {
        const string path = Retained + "/package.json";
        NodeRevisionInputs basis = NodeGroupReader.WithControlInputs(Inputs(NodeScenario.Basis),
            Control(NodeScenario.Basis), false);
        NodeRevisionInputs candidate = NodeGroupReader.WithControlInputs(
            Inputs(NodeScenario.Candidate), Control(NodeScenario.Candidate), true);
        var scope = new ScopeInputs(new(NodeScenario.Basis, NodeScenario.Candidate, false),
            [path], [new(path, true, "100644", [])], [new(path, true, "100644", [])]);
        string root = Path.Combine(Path.GetTempPath(), "node-runtime-checkout");
        NodeRuntimeInputs runtime = NodeRuntimeReader.Read(new(NodeScenario.Candidate, []), root,
            NodeRuntimeInputsTests.Workflow, NodeRuntimeInputsTests.Schedule(),
            "[tools]\nnode='24'\n", "version=3\n",
            [new("package.json", NodeRuntimeInputsTests.Manifest(">=22")),
                new(Adopted + "/package.json", NodeRuntimeInputsTests.Manifest(">=24.0.0")),
                new(path, NodeRuntimeInputsTests.Manifest(">=20"))],
            NodeRuntimeInputsTests.Tool(root, "node"), NodeRuntimeInputsTests.Tool(root, "pnpm"));

        NodeGroupPlan group = await NodeGroupPlanning.PlanAsync(basis, candidate, scope, Adopted,
            (project, _) => Task.FromResult(NodeScenario.Scripts(project)),
            (_, _) => Task.FromResult("1.2.3"), CancellationToken.None);
        CiPlan adopted = runtime.Expand(group.Adopted);

        CollectionAssert.AreEqual(PlannedSelectors,
            runtime.Variants.Select(variant => variant.Selector).ToArray());
        Assert.Contains(new SelectionReason(path, NodeScenario.Candidate, Retained),
            Assert.ContainsSingle(group.Retained).Reasons);
        foreach (NodeRuntimeVariant variant in runtime.Variants)
        {
            PlannedCheck[] checks = adopted.Checks.Where(check =>
                check.Work.Key.Variant == variant.Key && check.Reasons.Contains(
                    new SelectionReason(path, NodeScenario.Candidate, Adopted))).ToArray();
            Assert.AreEqual(variant.Key is "node24" or "node26", checks.Length != 0,
                variant.Key);
        }
    }

    [TestMethod]
    public void ConfirmedBasisOwnerAbsenceDoesNotFabricateControlFacts()
    {
        NodeRevisionInputs inputs = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project(Adopted));

        NodeRevisionInputs joined = NodeGroupReader.WithControlInputs(inputs, null, false);

        CollectionAssert.AreEquivalent((string[])[.. NodeGroupReader.CallerSources,
            NodeRuntimeReader.Schedule, "package.json", Adopted + "/package.json"],
            joined.OperationInputs.Select(input => input.Path).ToArray());
        foreach (SharedInput input in joined.OperationInputs)
            Assert.AreEqual(Adopted, Assert.ContainsSingle(input.Consumers));
        Assert.AreEqual(NodeScenario.Basis, joined.Revision.Commit);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void MissingOrSubstitutedBuildCannotBecomeOwnerAbsence(bool owner, bool build)
    {
        NodeRevisionInputs inputs = owner ? Inputs(NodeScenario.Candidate)
            : NodeScenario.Inputs(NodeScenario.Candidate, NodeScenario.Project(Adopted));

        Assert.ThrowsExactly<InvalidDataException>(() => NodeGroupReader.WithControlInputs(inputs,
            build ? Control(NodeScenario.Candidate) : null, true));
    }

    [TestMethod]
    public void ControlInputsRequireTheExactEndpointRevision()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => NodeGroupReader.WithControlInputs(
            Inputs(NodeScenario.Candidate), Control(NodeScenario.Basis), true));
    }

    [TestMethod]
    [DataRow("owner", "120000", "blob")]
    [DataRow("owner", "040000", "tree")]
    [DataRow("caller", "120000", "blob")]
    [DataRow("caller", "160000", "commit")]
    [DataRow("schedule", "120000", "blob")]
    public void UnsupportedOwnerOrCallerEntryCannotEstablishConsumption(string role,
        string mode, string type)
    {
        NodeRevisionInputs inputs = Inputs(NodeScenario.Candidate);
        string path = role switch
        {
            "owner" => NodeGroupReader.ControlProject,
            "schedule" => NodeRuntimeReader.Schedule,
            _ => NodeGroupReader.CallerSources[0],
        };
        inputs = inputs with
        {
            Revision = inputs.Revision with
            {
                Entries = [.. inputs.Revision.Entries.Where(entry => entry.Path != path),
                new(path, mode, type)]
            },
        };

        Assert.ThrowsExactly<InvalidDataException>(() => NodeGroupReader.WithControlInputs(inputs,
            Control(NodeScenario.Candidate), true));
    }

    private static NodeRevisionInputs Inputs(string revision)
    {
        NodeRevisionInputs inputs = NodeScenario.Inputs(revision,
            NodeScenario.Project(Adopted), NodeScenario.Project(Retained));
        return inputs with
        {
            Revision = inputs.Revision with
            {
                Entries = [.. inputs.Revision.Entries,
                NodeScenario.File(NodeGroupReader.ControlProject)]
            },
            Quality = new Dictionary<string, QualitySelection?>(inputs.Quality)
            { [Retained] = null },
        };
    }

    private static ControlInputFacts Control(string revision,
        params ControlSourceInput[] sources) => new(revision, NodeGroupReader.ControlProject,
            sources);
}
