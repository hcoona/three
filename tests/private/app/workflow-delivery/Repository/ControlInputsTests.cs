using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class ControlInputsTests
{
    [TestMethod]
    public void ControlFactsRetainRealSourcesReferencesHelpersAndAbsentConfiguration()
    {
        DotNetRevisionInputs inputs = Inputs();
        ControlInputFacts facts = ControlInputs.Read(inputs, true)!;
        CollectionAssert.AreEquivalent(new[] { NodeGroupReader.ControlProject, "control/Program.cs",
            "shared/runtime.dll", "control/Native/facts.rb", "Directory.Build.targets" },
            facts.Sources.Select(source => source.Path).Distinct().ToArray());
        Assert.AreEqual("ReferenceHintPath", facts.Sources.Single(
            source => source.Path == "shared/runtime.dll").Role);
        Assert.IsFalse(facts.Sources.Single(
            source => source.Path == "Directory.Build.targets").Present);
        Assert.AreEqual(inputs.Revision.Commit, facts.Revision);
    }

    [TestMethod]
    public void UnsupportedControlRelationCannotBecomeSilentIncompleteFacts()
    {
        DotNetRevisionInputs inputs = Inputs();
        var edge = new DotNetGraphEdge(inputs.Nodes[0].Native.Identity, new(
            "/checkout/other.csproj", new()));
        Assert.ThrowsExactly<InvalidDataException>(() => ControlInputs.Read(inputs with
        { Graph = inputs.Graph with { Edges = [edge] } }, true));
        Assert.IsNull(ControlInputs.Read(inputs with { Nodes = [] }, false));
        Assert.ThrowsExactly<InvalidDataException>(() => ControlInputs.Read(
            inputs with { Nodes = [] }, true));
        Assert.ThrowsExactly<InvalidDataException>(() => ControlInputs.Read(inputs with
        { Nodes = [inputs.Nodes[0], inputs.Nodes[0]] }, true));
    }

    private static DotNetRevisionInputs Inputs()
    {
        string project = NodeGroupReader.ControlProject;
        var identity = new DotNetNodeIdentity("/checkout/" + project, new());
        var node = new DotNetGraphNode(identity, false, new("Debug", "net10.0"), "", "", [],
            "None");
        string[] paths = [project, "control/Program.cs", "shared/runtime.dll",
            "control/Native/facts.rb"];
        var revision = new GitRevision(NodeScenario.Candidate, paths.Select(
            NodeScenario.File).ToArray());
        return new(revision, new(revision.Commit, "/checkout", "/checkout/dirs.proj", [], [], []),
            [new(project, node, [project, "control/Program.cs", "control/Native/facts.rb"])],
            [new("shared/runtime.dll", new("/checkout/shared/runtime.dll", "ReferenceHintPath",
                "Evaluation", identity))],
            [], new Dictionary<string, QualitySelection?>(), [new SharedInput(
                "Directory.Build.targets", [project])], []);
    }
}
