using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonDependencyProjectionTests
{
    private static readonly string Root = Path.GetFullPath("python projection endpoint");
    private static readonly PythonOperationGroups[] NoDev =
        [new(".", []), new("src/consumer", []), new("src/plugin", [])];
    private static readonly string[] Plugin = ["src/plugin"];
    private static readonly string[] Producer = ["src/producer"];
    private static readonly string[] BuildClosure = ["src/plugin", "src/producer"];
    private static readonly string[] PluginBuildClosure = ["src/consumer", "src/producer"];
    private static readonly string[] Feature = ["feature"];
    private static readonly string[] NativeBuildConsumers =
    [
        "src/lab/azure-document-intelligence-lab", "src/private/app/azureauth-credprovider/python",
        "src/public/lib/hcoona-release-smoke-python", "src/sample/nbgv-hatch-demo",
    ];

    [TestMethod]
    public void RecordedNativeBuildSourcesJoinMembersWithoutNameOrIdHeuristics()
    {
        JsonNode metadataValue = Recorded("python-metadata.json");
        JsonNode buildValue = Recorded("python-build-associations.json");
        PythonMetadataReaderTests.Reroot(metadataValue, Root);
        PythonMetadataReaderTests.Reroot(buildValue, Root);
        PythonMetadata metadata = PythonMetadataReader.Parse(metadataValue.ToJsonString(), Root);
        using JsonDocument document = JsonDocument.Parse(buildValue.ToJsonString());

        PythonBuildAssociation[] associations = PythonDependencyProjection.ReadBuildAssociations(
            metadata, new(document.RootElement.Clone(), []), PassiveContexts());

        Assert.HasCount(4, associations);
        CollectionAssert.AreEquivalent(NativeBuildConsumers,
            associations.Select(association => association.Consumer).ToArray());
        Assert.IsTrue(associations.All(association =>
            association.Producer == "src/public/lib/nbgv-python"));
        Assert.IsTrue(associations.All(association => association.Extras.Length == 0 &&
            association.Marker is null));
    }

    [TestMethod]
    public void LoweredBuildExtraAndMarkerRemainNativeStrings()
    {
        JsonNode metadataValue = Recorded("python-metadata.json");
        JsonNode value = Recorded("python-build-associations.json");
        PythonMetadataReaderTests.Reroot(metadataValue, Root);
        PythonMetadataReaderTests.Reroot(value, Root);
        // Controlled transfer modification; this does not claim another native observation.
        JsonNode requirement = value["build_requirements"]![0]!["requirements"]![1]!;
        requirement["extras"] = new JsonArray("native-normalized-feature");
        requirement["marker"] = "python_full_version >= '3.14'";
        using JsonDocument document = JsonDocument.Parse(value.ToJsonString());
        PythonMetadata metadata = PythonMetadataReader.Parse(metadataValue.ToJsonString(), Root);

        PythonBuildAssociation[] associations = PythonDependencyProjection.ReadBuildAssociations(
            metadata, new(document.RootElement.Clone(), []), PassiveContexts());

        PythonBuildAssociation association = associations.Single(item => item.Consumer ==
            "src/lab/azure-document-intelligence-lab");
        Assert.ContainsSingle(association.Extras);
        Assert.AreEqual("native-normalized-feature", association.Extras.Single());
        Assert.AreEqual("python_full_version >= '3.14'", association.Marker);
    }

    [TestMethod]
    [DataRow("endpoint")]
    [DataRow("missing-member")]
    [DataRow("wrong-name")]
    [DataRow("unowned-path")]
    [DataRow("url")]
    [DataRow("unknown-field")]
    [DataRow("duplicate-context")]
    [DataRow("missing-build-context")]
    [DataRow("missing-requirement")]
    public void NativeBuildJoinRejectsUnresolvedSourcesAndInventory(string failure)
    {
        JsonNode value = Recorded("python-build-associations.json");
        JsonNode metadataValue = Recorded("python-metadata.json");
        PythonMetadataReaderTests.Reroot(value, Root);
        PythonMetadataReaderTests.Reroot(metadataValue, Root);
        JsonNode requirement = value["build_requirements"]![0]!["requirements"]![1]!;
        switch (failure)
        {
            case "endpoint": value["workspace_root"] = Path.GetDirectoryName(Root); break;
            case "missing-member": value["members"]!.AsArray().RemoveAt(0); break;
            case "wrong-name": requirement["name"] = "wrong"; break;
            case "unowned-path": requirement["editable"] = Path.Combine(Root, "missing"); break;
            case "url": requirement["url"] = "https://example.invalid/plugin.whl"; break;
            case "unknown-field": requirement["future"] = true; break;
            case "duplicate-context":
                value["build_requirements"]!.AsArray().Add(
                value["build_requirements"]![0]!.DeepClone()); break;
            case "missing-build-context": value["build_requirements"]!.AsArray().RemoveAt(0); break;
            case "missing-requirement":
                value["build_requirements"]![0]!["requirements"]!.AsArray().RemoveAt(1); break;
        }
        using JsonDocument document = JsonDocument.Parse(value.ToJsonString());
        PythonMetadata metadata = PythonMetadataReader.Parse(metadataValue.ToJsonString(), Root);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonDependencyProjection.ReadBuildAssociations(metadata,
                new(document.RootElement.Clone(), []), PassiveContexts()));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void NativeActivityControlsOrdinaryExtraGraphWithoutParsingMarkers(bool enabled)
    {
        PythonMetadata metadata = Metadata();
        PythonProjectDependencies[] facts = PythonDependencyProjection.Project(metadata, NoDev,
            [], (expression, extras) =>
            {
                Assert.AreEqual("python_version >= '3.14'", expression);
                Assert.IsEmpty(extras);
                return enabled;
            }, true);

        CollectionAssert.AreEqual(enabled ? Plugin : [],
            facts.Single(project => project.Directory == "src/consumer").Ordinary);
        Assert.IsEmpty(facts.Single(project => project.Directory == "src/plugin").Ordinary);
        Assert.IsEmpty(facts.Single(project => project.Directory == ".").Build);
    }

    [TestMethod]
    public void BuildAssociationUsesNativeExtraEntryAndRetainsPluginOrdinaryLeg()
    {
        JsonNode value = PythonMetadataReaderTests.Fixture(Root);
        value["resolution"]!["consumer opaque"]!["dependencies"] = new JsonArray();
        // Controlled third member: this is graph projection input, not an observed UV edge.
        AddProducer(value);
        value["resolution"]!["native extra id"]!["dependencies"]!.AsArray()
            .Add(new JsonObject { ["id"] = "third native id", ["marker"] = "extra == 'feature'" });
        PythonMetadata metadata = PythonMetadataReader.Parse(value.ToJsonString(), Root);
        PythonProjectDependencies[] facts = PythonDependencyProjection.Project(metadata,
            [.. NoDev, new("src/producer", [])],
            [new("src/consumer", "src/plugin", ["feature"], "native build marker"),
                new("src/plugin", "src/producer", [], null)], (expression, extras) =>
            {
                if (expression == "extra == 'feature'")
                    CollectionAssert.AreEqual(Feature, extras);
                else
                {
                    Assert.AreEqual("native build marker", expression);
                    Assert.IsEmpty(extras);
                }
                return true;
            }, true);

        PythonProjectDependencies consumer = facts.Single(project =>
            project.Directory == "src/consumer");
        Assert.IsEmpty(consumer.Ordinary);
        CollectionAssert.AreEqual(BuildClosure, consumer.Build);
        CollectionAssert.AreEqual(Producer,
            facts.Single(project => project.Directory == "src/plugin").Build);
        Assert.IsEmpty(facts.Single(project => project.Directory == "src/producer").Build);
    }

    [TestMethod]
    public void GroupUnionSelectsQualityDependenciesButBuildLegsDoNotEnableDev()
    {
        JsonNode value = PythonMetadataReaderTests.Fixture(Root);
        value["resolution"]!["consumer opaque"]!["dependencies"] = new JsonArray();
        value["resolution"]!["native group id"]!["dependencies"] = new JsonArray(
            new JsonObject { ["id"] = "not-a-path:registry+misleading" });
        PythonMetadata metadata = PythonMetadataReader.Parse(value.ToJsonString(), Root);
        PythonProjectDependencies[] withDev = PythonDependencyProjection.Project(metadata,
            [.. NoDev, new("src/consumer", ["dev"])], [], NeverEvaluate, true);
        PythonProjectDependencies[] withoutDev = PythonDependencyProjection.Project(metadata,
            NoDev, [], NeverEvaluate, true);

        CollectionAssert.AreEqual(Plugin,
            withDev.Single(project => project.Directory == "src/consumer").Ordinary);
        Assert.IsEmpty(withoutDev.Single(project => project.Directory == "src/consumer").Ordinary);
        Assert.IsEmpty(withDev.Single(project => project.Directory == "src/plugin").Ordinary);
    }

    [TestMethod]
    public void InactiveBuildMarkerCannotIntroducePluginInputs()
    {
        JsonNode value = PythonMetadataReaderTests.Fixture(Root);
        value["resolution"]!["consumer opaque"]!["dependencies"] = new JsonArray();
        PythonProjectDependencies[] facts = PythonDependencyProjection.Project(
            PythonMetadataReader.Parse(value.ToJsonString(), Root), NoDev,
            [new("src/consumer", "src/plugin", [], "native false")], (expression, extras) =>
            {
                Assert.AreEqual("native false", expression);
                Assert.IsEmpty(extras);
                return false;
            }, true);

        Assert.IsEmpty(facts.Single(project => project.Directory == "src/consumer").Build);
    }

    [TestMethod]
    public void CyclicNativeAndBuildRelationsTerminateWithCompleteLocalClosure()
    {
        JsonNode value = PythonMetadataReaderTests.Fixture(Root);
        AddProducer(value);
        value["resolution"]!["not-a-path:registry+misleading"]!["dependencies"] = new JsonArray(
            new JsonObject { ["id"] = "consumer opaque" });
        PythonMetadata metadata = PythonMetadataReader.Parse(value.ToJsonString(), Root);

        PythonProjectDependencies[] facts = PythonDependencyProjection.Project(metadata,
            [.. NoDev, new("src/producer", [])],
            [new("src/consumer", "src/plugin", [], null),
                new("src/plugin", "src/producer", [], null),
                new("src/producer", "src/plugin", [], null)], (_, _) => true, true);

        PythonProjectDependencies consumer = facts.Single(project =>
            project.Directory == "src/consumer");
        CollectionAssert.AreEqual(Plugin, consumer.Ordinary);
        CollectionAssert.AreEqual(BuildClosure, consumer.Build);
        Assert.DoesNotContain("src/consumer", consumer.Build);
        CollectionAssert.AreEqual(PluginBuildClosure,
            facts.Single(project => project.Directory == "src/plugin").Build);
    }

    [TestMethod]
    public void PluginDevelopmentGroupDoesNotBecomeConsumerBuildInput()
    {
        JsonNode value = PythonMetadataReaderTests.Fixture(Root);
        AddProducer(value);
        value["resolution"]!["consumer opaque"]!["dependencies"] = new JsonArray();
        value["resolution"]!["not-a-path:registry+misleading"]!["dependency_groups"] =
            new JsonArray(
            new JsonObject { ["name"] = "dev", ["id"] = "plugin dev" });
        value["resolution"]!["plugin dev"] = new JsonObject
        {
            ["kind"] = new JsonObject { ["group"] = "dev" },
            ["name"] = "producer",
            ["source"] = new JsonObject { ["editable"] = Path.Combine(Root, "src", "plugin") },
            ["dependencies"] = new JsonArray(new JsonObject { ["id"] = "third native id" }),
        };
        PythonMetadata metadata = PythonMetadataReader.Parse(value.ToJsonString(), Root);

        PythonProjectDependencies[] facts = PythonDependencyProjection.Project(metadata,
            [.. NoDev, new("src/plugin", ["dev"]), new("src/producer", [])],
            [new("src/consumer", "src/plugin", [], null)], NeverEvaluate, true);

        CollectionAssert.AreEqual(Plugin,
            facts.Single(project => project.Directory == "src/consumer").Build);
        CollectionAssert.AreEqual(Producer,
            facts.Single(project => project.Directory == "src/plugin").Ordinary);
        Assert.DoesNotContain("src/producer",
            facts.Single(project => project.Directory == "src/consumer").Build);
    }

    [TestMethod]
    [DataRow("constraint")]
    [DataRow("missing-context")]
    [DataRow("unknown-group")]
    [DataRow("unknown-producer")]
    [DataRow("unknown-extra")]
    [DataRow("missing-activity")]
    [DataRow("unowned-local")]
    public void NecessaryUnresolvedActivityOrOwnershipCannotCertifyExclusion(string failure)
    {
        JsonNode value = PythonMetadataReaderTests.Fixture(Root);
        PythonOperationGroups[] operations = NoDev;
        PythonBuildAssociation[] builds = [];
        bool supported = true;
        switch (failure)
        {
            case "constraint": supported = false; break;
            case "missing-context": operations = NoDev[1..]; break;
            case "unknown-group": operations = [.. NoDev, new("src/plugin", ["unknown"])]; break;
            case "unknown-producer": builds = [new("src/consumer", "src/missing", [], null)]; break;
            case "unknown-extra":
                builds = [new("src/consumer", "src/plugin", ["unknown"], null)]; break;
            case "unowned-local":
                value["members"]!.AsArray().RemoveAt(1); operations = NoDev[..2]; break;
        }
        PythonMetadata metadata = PythonMetadataReader.Parse(value.ToJsonString(), Root);
        Assert.ThrowsExactly<InvalidDataException>(() => PythonDependencyProjection.Project(
            metadata, operations, builds, (expression, _) => failure == "missing-activity"
                ? throw new InvalidDataException("Missing native marker result.") : true,
            supported));
    }

    private static PythonMetadata Metadata() =>
        PythonMetadataReader.Parse(PythonMetadataReaderTests.Fixture(Root).ToJsonString(), Root);

    private static JsonNode Recorded(string name)
    {
        using Stream stream = typeof(PythonDependencyProjectionTests).Assembly
            .GetManifestResourceStream("WorkflowDelivery.Tests.Repository.fixtures." + name)!;
        return JsonNode.Parse(stream)!;
    }

    private static PythonPassiveProject[] PassiveContexts()
    {
        JsonNode value = Recorded("python-passive-contexts.json");
        PythonMetadataReaderTests.Reroot(value, Root);
        return value.AsArray().Select(project => new PythonPassiveProject(
            project!["directory"]!.GetValue<string>(), project["build_requirements"]?.AsArray()
                .Select(requirement => requirement!.GetValue<string>()).ToArray())).ToArray();
    }

    private static bool NeverEvaluate(string expression, string[] extras) =>
        throw new InvalidOperationException("No native marker was requested.");

    private static void AddProducer(JsonNode value)
    {
        string path = Path.Combine(Root, "src", "producer");
        value["members"]!.AsArray().Add(new JsonObject
        { ["name"] = "third", ["path"] = path, ["id"] = "third native id" });
        value["resolution"]!["third native id"] = new JsonObject
        {
            ["kind"] = "package",
            ["name"] = "third",
            ["source"] = new JsonObject { ["editable"] = path },
            ["dependencies"] = new JsonArray(),
        };
    }
}
