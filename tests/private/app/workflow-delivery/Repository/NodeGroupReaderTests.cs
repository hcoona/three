using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NodeGroupReaderTests
{
    private const string Adopted = "src/adopted";
    private const string Retained = "src/retained";

    [TestMethod]
    public async Task ControlInputsSelectAdoptedAndRetainedSurvivingOwners()
    {
        const string removed = "control/removed.cs";
        NodeRevisionInputs basis = Inputs(NodeScenario.Basis) with
        {
            Revision = Inputs(NodeScenario.Basis).Revision with
            { Entries = [.. Inputs(NodeScenario.Basis).Revision.Entries,
                NodeScenario.File(removed)] },
        };
        NodeRevisionInputs candidate = Inputs(NodeScenario.Candidate);
        basis = NodeGroupReader.WithControlInputs(basis,
            Control(NodeScenario.Basis, new ControlSourceInput(removed, "compiler Sources", true)),
            false);
        candidate = NodeGroupReader.WithControlInputs(candidate,
            Control(NodeScenario.Candidate), true);
        var scope = new NodeScopeInputs(new(NodeScenario.Basis, NodeScenario.Candidate, false),
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
    [DataRow("eng/scripts/repository_path_patterns.py")]
    [DataRow("uv.lock")]
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
        var scope = new NodeScopeInputs(new(NodeScenario.Basis, NodeScenario.Candidate, false),
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
            .. NodeGroupReader.CallerSources])
        {
            SharedInput input = joined.OperationInputs.Single(input => input.Path == path);
            CollectionAssert.AreEquivalent(new[] { Adopted, Retained }, input.Consumers);
        }
        Assert.HasCount(NodeGroupReader.CallerSources.Length + 2, joined.OperationInputs);
        Assert.AreSame(inputs.Graph, joined.Graph);
        Assert.AreSame(inputs.Revision, joined.Revision);
    }

    [TestMethod]
    public void ConfirmedBasisOwnerAbsenceDoesNotFabricateControlFacts()
    {
        NodeRevisionInputs inputs = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project(Adopted));

        NodeRevisionInputs joined = NodeGroupReader.WithControlInputs(inputs, null, false);

        CollectionAssert.AreEquivalent(NodeGroupReader.CallerSources,
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
    public void UnsupportedOwnerOrCallerEntryCannotEstablishConsumption(string role,
        string mode, string type)
    {
        NodeRevisionInputs inputs = Inputs(NodeScenario.Candidate);
        string path = role == "owner" ? NodeGroupReader.ControlProject
            : NodeGroupReader.CallerSources[0];
        inputs = inputs with
        {
            Revision = inputs.Revision with
            { Entries = [.. inputs.Revision.Entries.Where(entry => entry.Path != path),
                new(path, mode, type)] },
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
            { Entries = [.. inputs.Revision.Entries,
                NodeScenario.File(NodeGroupReader.ControlProject)] },
            Quality = new Dictionary<string, QualitySelection?>(inputs.Quality)
            { [Retained] = null },
        };
    }

    private static ControlBuildConsumption Control(string revision,
        params ControlSourceInput[] sources) => new(new(revision, NodeGroupReader.ControlProject,
            new("Debug", "net10.0"), "10.0.401", "/sdk", "/dotnet", "/packages", "/output",
            "/intermediate", "/intermediate", "/assets", "/output/app.dll", [], [],
            new Dictionary<string, string>()), sources, [], []);
}
