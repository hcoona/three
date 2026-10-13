using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class DotNetGroupResourceTests
{
    private const string Revision = "1111111111111111111111111111111111111111";
    private const string Owner = DotNetGroupReader.TestProject;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "resource-projection");
    private static readonly string[] ResourceUnion =
        ["shared/common.json", "shared/net10.json", "shared/net9.json"];

    [TestMethod]
    [DataRow("missing", "blob")]
    [DataRow("120000", "blob")]
    [DataRow("160000", "commit")]
    [DataRow("100644", "tree")]
    public void CandidateRequiresCommittedRegularOwner(string mode, string type)
    {
        DotNetRevisionInputs inputs = Inputs([], [], mode == "missing" ? [] :
            [new(Owner, mode, type)]);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            DotNetGroupReader.Resources(inputs, true));

        StringAssert.Contains(error.Message, "committed regular project");
    }

    [TestMethod]
    public void BasisAbsenceComesFromCommittedInventory()
    {
        DotNetRevisionInputs inputs = Inputs([], [], []);

        DotNetResourceEndpoint result = DotNetGroupReader.Resources(inputs, false);

        Assert.AreEqual(Revision, result.Revision);
        Assert.AreEqual(Owner, result.Project);
        Assert.IsFalse(result.Present);
        Assert.IsEmpty(result.Inputs);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void CommittedOwnerMissingNativeNodeIsUnavailable(bool candidate)
    {
        DotNetRevisionInputs inputs = Inputs([], [], [new(Owner, "100644", "blob")]);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            DotNetGroupReader.Resources(inputs, candidate));

        StringAssert.Contains(error.Message, "missing from the native graph");
    }

    [TestMethod]
    [DataRow("100644")]
    [DataRow("100755")]
    public void ResourceUnionUsesCompleteNativeIdentities(string mode)
    {
        DotNetBoundNode net10 = Node(Owner, "net10.0");
        DotNetBoundNode net9 = Node(Owner, "net9.0");
        DotNetBoundNode other = Node("tests/other/Other.Tests.csproj", "net10.0");
        DotNetNodeIdentity differentGlobals = net10.Native.Identity with
        {
            Globals = new(net10.Native.Identity.Globals) { ["BusinessSentinel"] = "different" },
        };
        DotNetRevisionInputs inputs = Inputs([net10, net9, other],
            [Input("shared/common.json", net10.Native.Identity),
                Input("shared/common.json", net9.Native.Identity),
                Input("shared/net9.json", net9.Native.Identity),
                Input("shared/net10.json", net10.Native.Identity),
                Input("shared/other.json", other.Native.Identity),
                Input(null, other.Native.Identity),
                Input("shared/wrong-variant.json", differentGlobals),
                Input("shared/content.json", net10.Native.Identity, role: "Content"),
                Input("shared/build.json", net10.Native.Identity, stage: "Build")],
            [new(Owner, mode, "blob")]);

        DotNetResourceEndpoint result = DotNetGroupReader.Resources(inputs, true);

        Assert.AreEqual(Revision, result.Revision);
        Assert.AreEqual(Owner, result.Project);
        Assert.IsTrue(result.Present);
        CollectionAssert.AreEqual(ResourceUnion, result.Inputs);
    }

    [TestMethod]
    public void ProviderOnlyResourceCannotDisappearFromProjection()
    {
        DotNetBoundNode node = Node(Owner, "net10.0");
        DotNetBoundInput provider = Input(null, node.Native.Identity) with
        {
            Native = new(Path.Combine(Root, "provider", "resource.json"), "EmbeddedResource",
                "Evaluation", node.Native.Identity, new("NuGet", Root, "fixture", "1.0")),
        };
        DotNetRevisionInputs inputs = Inputs([node], [provider],
            [new(Owner, "100644", "blob")]);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            DotNetGroupReader.Resources(inputs, true));

        StringAssert.Contains(error.Message, "committed source coordinates");
    }

    private static DotNetBoundNode Node(string project, string framework)
    {
        var identity = new DotNetNodeIdentity(Path.Combine(Root, project), new()
        {
            ["Configuration"] = "Debug", ["ContinuousIntegrationBuild"] = "true",
            ["RestoreLockedMode"] = "true", ["TargetFramework"] = framework,
        });
        return new(project, new(identity, false, new("Debug", framework), "", "", [], ""), []);
    }

    private static DotNetBoundInput Input(string? path, DotNetNodeIdentity consumer,
        string role = "EmbeddedResource", string stage = "Evaluation") =>
        new(path, new(Path.Combine(Root, path ?? "uncommitted.json"), role, stage, consumer));

    private static DotNetRevisionInputs Inputs(DotNetBoundNode[] nodes,
        DotNetBoundInput[] inputs, GitEntry[] entries) => new(new(Revision, entries),
        new(Revision, Root, Path.Combine(Root, "dirs.proj"),
            nodes.Select(node => node.Native).ToArray(), [],
            inputs.Select(input => input.Native).ToArray()), nodes, inputs, [],
        new Dictionary<string, QualitySelection?>(), [], []);
}
