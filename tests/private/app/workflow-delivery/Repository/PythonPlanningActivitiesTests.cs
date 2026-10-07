using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonPlanningActivitiesTests
{
    private static readonly string Root = Path.GetFullPath("python activity endpoint");
    private const string Expression = "extra == 'feature'";
    private static readonly string[] Feature = ["feature"];
    private static readonly string[] PluginClosure = ["src/dep", "src/plugin"];
    private static readonly string[] Plugin = ["src/plugin"];
    private static readonly string[] Consumer = ["consumer"];
    private static readonly string[] Quality = ["quality"];

    private static PythonPlanningActivities Create(bool build = true,
        PythonGroupOperation[]? operations = null)
    {
        var nodes = new Dictionary<string, PythonMetadataNode>(StringComparer.Ordinal)
        {
            ["root"] = new("root", "workspace", null, null, null, [], [], []),
            ["consumer"] = Package("consumer", [], [new("quality", "quality")]),
            ["plugin"] = Package("plugin", [new("dep", Expression)], [],
                [new("feature", "feature")]),
            ["dep"] = Package("dep", [], []),
            ["quality"] = new("quality", "group", "quality", "consumer", null,
                [new("plugin", null)], [], []),
            ["feature"] = new("feature", "extra", "feature", "plugin", null,
                [new("dep", Expression)], [], [])
        };
        PythonMetadataMember[] members = [new("consumer", "src/consumer", "consumer"),
            new("plugin", "src/plugin", "plugin"), new("dep", "src/dep", "dep")];
        var metadata = new PythonMetadata(Root, "root", ">=3.14", members, nodes);
        return new(metadata, build ?
            [new("src/consumer", "src/plugin", Feature, "python_version >= '3.14'")] : [],
            Path.Combine(Root, "python"), Path.Combine(Root, "cache"), [],
            operations ?? Operations());
    }

    private static PythonMetadataNode Package(string name, PythonMetadataEdge[] edges,
        PythonMetadataEntry[] groups, PythonMetadataEntry[]? extras = null) =>
        new(name, "package", null, name,
            new("editable", default, Path.Combine(Root, "src", name)), edges, groups,
            extras ?? []);

    private static PythonGroupOperation[] Operations() =>
    [
        new("root-quality", Root, Consumer, false),
        new("consumer-quality", Path.Combine(Root, "src", "consumer"), [], false),
        new("plugin-no-dev", Path.Combine(Root, "src", "plugin"), [], true),
        new("dep-no-dev", Path.Combine(Root, "src", "dep"), [], true)
    ];

    // Controlled response; answer semantics are supplied, never evaluated in Workflow.
    private static PythonSupplementResult Answer(PythonPlanningActivities planning,
        bool build = true, bool supported = true, bool quality = false, bool omit = false)
    {
        PythonSupplementRequest request = planning.Request;
        var facts = new
        {
            workspace_root = request.WorkspaceRoot,
            interpreter = request.Interpreter,
            group_operations = request.GroupOperations.Reverse().Select(operation => new
            {
                id = operation.Id,
                groups = quality && operation.Id == "consumer-quality"
                    ? Quality : []
            }),
            markers = request.Markers.Skip(omit ? 1 : 0).Reverse().Select(marker => new
            {
                id = marker.Id,
                active = marker.Expression == Expression ? marker.Extras.Length == 1 : build
            }),
            python_constraints = new[] { new { id = "workspace-python", active = supported } }
        };
        return new(JsonSerializer.SerializeToElement(facts), []);
    }

    [TestMethod]
    public void MarkerScopesRemainDistinctAndNativeFalseExcludesOnlyItsLeg()
    {
        PythonPlanningActivities planning = Create();
        PythonMarkerInput[] markers = planning.Request.Markers
            .Where(marker => marker.Expression == Expression).ToArray();
        Assert.HasCount(2, markers);
        Assert.AreNotEqual(markers[0].Id, markers[1].Id);
        Assert.IsEmpty(markers.Single(marker => marker.Extras.Length == 0).Extras);
        CollectionAssert.AreEqual(Feature,
            markers.Single(marker => marker.Extras.Length != 0).Extras);
        Assert.AreEqual(">=3.14", planning.Request.PythonConstraints.Single().Specifier);

        PythonProjectDependencies[] result = planning.Project(Answer(planning));
        PythonProjectDependencies consumer = result.Single(item => item.Directory ==
            "src/consumer");
        Assert.IsEmpty(consumer.Ordinary);
        CollectionAssert.AreEqual(PluginClosure, consumer.Build);
        Assert.IsEmpty(result.Single(item => item.Directory == "src/plugin").Ordinary);
        Assert.IsEmpty(planning.Project(Answer(planning, build: false))
            .Single(item => item.Directory == "src/consumer").Build);
    }

    [TestMethod]
    public void NativeOperationAnswersUnionWithoutReconstructingDefaults()
    {
        PythonGroupOperation[] operations = [.. Operations(),
            new("consumer-no-dev", Path.Combine(Root, "src", "consumer"), [], true)];
        PythonPlanningActivities planning = Create(build: false, operations);
        PythonProjectDependencies consumer = planning.Project(Answer(planning, quality: true))
            .Single(item => item.Directory == "src/consumer");
        CollectionAssert.AreEqual(Plugin, consumer.Ordinary);
        Assert.IsEmpty(consumer.Build);
        Assert.IsEmpty(planning.Project(Answer(planning))
            .Single(item => item.Directory == "src/consumer").Ordinary);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public void UnsupportedInterpreterAndMissingAnswersFail(bool supported, bool omit)
    {
        PythonPlanningActivities planning = Create();
        Assert.ThrowsExactly<InvalidDataException>(() =>
            planning.Project(Answer(planning, supported: supported, omit: omit)));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("outside")]
    public void ChangedOperationContextFails(string fault)
    {
        PythonGroupOperation[] operations = Operations();
        if (fault == "missing") operations = operations[1..];
        else operations[0] = operations[0] with
        {
            Directory = Path.GetDirectoryName(Root)!
        };
        Assert.ThrowsExactly<InvalidDataException>(() => Create(operations: operations));
    }
}
