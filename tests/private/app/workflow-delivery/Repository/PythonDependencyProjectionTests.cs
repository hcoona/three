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
    private static readonly string[] Dev = ["dev"];
    private static readonly Dictionary<string, string> None = [];
    private static readonly Dictionary<string, string> Workspace = new()
        { ["producer"] = "workspace" };
    private static readonly string[] NativeBuildConsumers =
    [
        "src/lab/azure-document-intelligence-lab", "src/private/app/azureauth-credprovider/python",
        "src/public/lib/hcoona-release-smoke-python", "src/sample/nbgv-hatch-demo",
    ];

    [TestMethod]
    public void GuardedNameJoinMatchesRecordedNativeLowering()
    {
        JsonNode metadataValue = Recorded("python-metadata.json");
        JsonNode lowered = Recorded("python-build-associations.json");
        PythonMetadataReaderTests.Reroot(metadataValue, Root);
        PythonMetadataReaderTests.Reroot(lowered, Root);
        PythonMetadata metadata = PythonMetadataReader.Parse(metadataValue.ToJsonString(), Root);

        PythonBuildAssociation[] associations = PythonDependencyProjection.ReadBuildAssociations(
            metadata, PassiveProjects());

        // The retained UV lowering emitted a local install path only for these joins.
        string[] native = lowered["build_requirements"]!.AsArray().SelectMany(build =>
            build!["requirements"]!.AsArray().Where(requirement =>
                requirement!["editable"] is not null).Select(requirement =>
                PythonMetadataReader.Relative(Root, build["directory"]!.GetValue<string>()) +
                    "->" + PythonMetadataReader.Relative(Root,
                        requirement!["editable"]!.GetValue<string>()))).ToArray();
        CollectionAssert.AreEquivalent(native, associations.Select(association =>
            association.Consumer + "->" + association.Producer).ToArray());
        CollectionAssert.AreEquivalent(NativeBuildConsumers,
            associations.Select(association => association.Consumer).ToArray());
        Assert.IsTrue(associations.All(association => association.Extras.Length == 0));
    }

    [TestMethod]
    [DataRow("project")]
    [DataRow("root")]
    public void ExactWorkspaceSourceInEitherTableAdmitsTheMemberProducer(string table)
    {
        PythonMetadata metadata = Metadata();
        PythonPassiveProject[] projects = Contexts(
            [new("producer", ["feature"]), new("hatchling", [])],
            table == "project" ? Workspace : None, table == "root" ? Workspace : None);

        PythonBuildAssociation association = Assert.ContainsSingle(
            PythonDependencyProjection.ReadBuildAssociations(metadata, projects));

        Assert.AreEqual("src/consumer", association.Consumer);
        Assert.AreEqual("src/plugin", association.Producer);
        CollectionAssert.AreEqual(Feature, association.Extras);
    }

    [TestMethod]
    [DataRow("no-source")]
    [DataRow("project-unsupported")]
    [DataRow("root-unsupported")]
    [DataRow("registry-workspace-source")]
    [DataRow("duplicate-extra")]
    [DataRow("missing-context")]
    [DataRow("duplicate-context")]
    [DataRow("context-outside-members")]
    public void GuardedBuildJoinRejectsUnsupportedSourcesAndInventory(string failure)
    {
        PythonMetadata metadata = Metadata();
        PythonBuildRequirement[] requirements = failure switch
        {
            "registry-workspace-source" => [new("hatchling", [])],
            "duplicate-extra" => [new("producer", ["feature", "feature"])],
            _ => [new("producer", [])],
        };
        IReadOnlyDictionary<string, string> project = failure switch
        {
            "project-unsupported" => new Dictionary<string, string>
                { ["producer"] = "unsupported" },
            "registry-workspace-source" => new Dictionary<string, string>
                { ["hatchling"] = "workspace" },
            "no-source" => None,
            _ => Workspace,
        };
        IReadOnlyDictionary<string, string> root = failure == "root-unsupported"
            ? new Dictionary<string, string> { ["producer"] = "unsupported" } : None;
        PythonPassiveProject[] projects = Contexts(requirements, project, root);
        projects = failure switch
        {
            "missing-context" => projects[1..],
            "duplicate-context" => [.. projects, projects[1]],
            "context-outside-members" => [.. projects,
                new(Path.Combine(Root, "src", "other"), null)],
            _ => projects,
        };

        Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonDependencyProjection.ReadBuildAssociations(metadata, projects));
    }

    [TestMethod]
    public void UnsourcedSelfAndRegistryBuildRequirementsAddNoLocalInput()
    {
        PythonMetadata metadata = Metadata();
        PythonPassiveProject[] projects = Contexts(
            [new("consumer", []), new("hatchling", [])], None, None);

        Assert.IsEmpty(PythonDependencyProjection.ReadBuildAssociations(metadata, projects));
    }

    [TestMethod]
    public void SourcedSelfReferenceKeepsItsExtraBuildInputs()
    {
        JsonNode value = PythonMetadataReaderTests.Fixture(Root);
        string consumer = Path.Combine(Root, "src", "consumer");
        value["resolution"]!["consumer opaque"]!["dependencies"] = new JsonArray();
        value["resolution"]!["consumer opaque"]!["optional_dependencies"] = new JsonArray(
            new JsonObject { ["name"] = "plugins", ["id"] = "consumer plugins" });
        // Controlled self extra: graph projection input, not an observed UV edge.
        value["resolution"]!["consumer plugins"] = new JsonObject
        {
            ["kind"] = new JsonObject { ["extra"] = "plugins" },
            ["name"] = "consumer",
            ["source"] = new JsonObject { ["editable"] = consumer },
            ["dependencies"] = new JsonArray(
                new JsonObject { ["id"] = "not-a-path:registry+misleading" }),
        };
        PythonMetadata metadata = PythonMetadataReader.Parse(value.ToJsonString(), Root);
        PythonPassiveProject[] projects = Contexts([new("consumer", ["plugins"])],
            new Dictionary<string, string> { ["consumer"] = "workspace" }, None);

        PythonBuildAssociation association = Assert.ContainsSingle(
            PythonDependencyProjection.ReadBuildAssociations(metadata, projects));
        PythonProjectDependencies[] facts = PythonDependencyProjection.Project(metadata, NoDev,
            [association]);

        Assert.AreEqual("src/consumer", association.Producer);
        PythonProjectDependencies result = facts.Single(project =>
            project.Directory == "src/consumer");
        Assert.IsEmpty(result.Ordinary);
        CollectionAssert.AreEqual(Plugin, result.Build);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void DefaultGroupSelectionFollowsDeclaredDevAndNoDev(bool noDev, bool packageOwner)
    {
        PythonMetadata metadata = Metadata();
        PythonGroupOperation[] operations = [.. Operations(metadata), new("selected",
            Root, packageOwner ? ["consumer"] : [], noDev)];

        PythonOperationGroups[] groups = PythonDependencyProjection.SelectGroups(metadata,
            operations);

        string owner = packageOwner ? "src/consumer" : ".";
        PythonOperationGroups selected = groups[^1];
        Assert.AreEqual(owner, selected.Directory);
        CollectionAssert.AreEqual(noDev ? Array.Empty<string>() : Dev, selected.Groups);
        // A member without a declared dev group selects no graph entry.
        Assert.IsTrue(groups.Where(group => group.Directory == "src/plugin")
            .All(group => group.Groups.Length == 0));
    }

    [TestMethod]
    [DataRow("missing-context")]
    [DataRow("unknown-package")]
    [DataRow("multiple-packages")]
    [DataRow("duplicate-id")]
    public void GroupSelectionRejectsUnresolvedOperations(string failure)
    {
        PythonMetadata metadata = Metadata();
        PythonGroupOperation[] operations = Operations(metadata);
        operations = failure switch
        {
            "missing-context" => operations[1..],
            "unknown-package" => [.. operations, new("selected", Root, ["unknown"], false)],
            "multiple-packages" => [.. operations,
                new("selected", Root, ["consumer", "producer"], false)],
            _ => [.. operations, operations[0]],
        };

        Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonDependencyProjection.SelectGroups(metadata, operations));
    }

    [TestMethod]
    public void MarkedEdgesAreActiveForEveryPlatformAndInterpreter()
    {
        PythonMetadata metadata = Metadata();
        PythonProjectDependencies[] facts = PythonDependencyProjection.Project(metadata, NoDev,
            []);

        // The fixture edge is marked; the finite union keeps it without evaluation.
        CollectionAssert.AreEqual(Plugin,
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
            [new("src/consumer", "src/plugin", ["feature"]),
                new("src/plugin", "src/producer", [])]);

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
            [.. NoDev, new("src/consumer", ["dev"])], []);
        PythonProjectDependencies[] withoutDev = PythonDependencyProjection.Project(metadata,
            NoDev, []);

        CollectionAssert.AreEqual(Plugin,
            withDev.Single(project => project.Directory == "src/consumer").Ordinary);
        Assert.IsEmpty(withoutDev.Single(project => project.Directory == "src/consumer").Ordinary);
        Assert.IsEmpty(withDev.Single(project => project.Directory == "src/plugin").Ordinary);
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
            [new("src/consumer", "src/plugin", []),
                new("src/plugin", "src/producer", []),
                new("src/producer", "src/plugin", [])]);

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
            [new("src/consumer", "src/plugin", [])]);

        CollectionAssert.AreEqual(Plugin,
            facts.Single(project => project.Directory == "src/consumer").Build);
        CollectionAssert.AreEqual(Producer,
            facts.Single(project => project.Directory == "src/plugin").Ordinary);
        Assert.DoesNotContain("src/producer",
            facts.Single(project => project.Directory == "src/consumer").Build);
    }

    [TestMethod]
    [DataRow("missing-context")]
    [DataRow("unknown-group")]
    [DataRow("unknown-producer")]
    [DataRow("unknown-extra")]
    [DataRow("unowned-local")]
    public void NecessaryUnresolvedOwnershipCannotCertifyExclusion(string failure)
    {
        JsonNode value = PythonMetadataReaderTests.Fixture(Root);
        PythonOperationGroups[] operations = NoDev;
        PythonBuildAssociation[] builds = [];
        switch (failure)
        {
            case "missing-context": operations = NoDev[1..]; break;
            case "unknown-group": operations = [.. NoDev, new("src/plugin", ["unknown"])]; break;
            case "unknown-producer": builds = [new("src/consumer", "src/missing", [])]; break;
            case "unknown-extra":
                builds = [new("src/consumer", "src/plugin", ["unknown"])]; break;
            case "unowned-local":
                value["members"]!.AsArray().RemoveAt(1); operations = NoDev[..2]; break;
        }
        PythonMetadata metadata = PythonMetadataReader.Parse(value.ToJsonString(), Root);
        Assert.ThrowsExactly<InvalidDataException>(() => PythonDependencyProjection.Project(
            metadata, operations, builds));
    }

    private static PythonMetadata Metadata() =>
        PythonMetadataReader.Parse(PythonMetadataReaderTests.Fixture(Root).ToJsonString(), Root);

    private static JsonNode Recorded(string name)
    {
        using Stream stream = typeof(PythonDependencyProjectionTests).Assembly
            .GetManifestResourceStream("WorkflowDelivery.Tests.Repository.fixtures." + name)!;
        return JsonNode.Parse(stream)!;
    }

    private static PythonPassiveProject[] PassiveProjects()
    {
        JsonNode value = Recorded("python-passive-projects.json");
        PythonMetadataReaderTests.Reroot(value, Root);
        return value.AsArray().Select(project => new PythonPassiveProject(
            project!["directory"]!.GetValue<string>(), project["build_requirements"]?.AsArray()
                .Select(requirement => new PythonBuildRequirement(
                    requirement!["name"]!.GetValue<string>(), requirement["extras"]!.AsArray()
                        .Select(extra => extra!.GetValue<string>()).ToArray())).ToArray(),
            project["sources"]!.AsObject().ToDictionary(source => source.Key,
                source => source.Value!.GetValue<string>()))).ToArray();
    }

    // Root, consumer and plugin contexts over the controlled metadata fixture.
    private static PythonPassiveProject[] Contexts(PythonBuildRequirement[] consumer,
        IReadOnlyDictionary<string, string> consumerSources,
        IReadOnlyDictionary<string, string> rootSources) =>
    [
        new(Root, null, rootSources),
        new(Path.Combine(Root, "src", "consumer"), consumer, consumerSources),
        new(Path.Combine(Root, "src", "plugin"), [], None),
    ];

    private static PythonGroupOperation[] Operations(PythonMetadata metadata) =>
        metadata.Members.Select(member => member.Directory).Append(".").Select(directory =>
            new PythonGroupOperation(directory, Path.GetFullPath(Path.Combine(Root, directory)),
                [], false)).ToArray();

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
