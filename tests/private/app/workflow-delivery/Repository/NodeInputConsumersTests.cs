using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NodeInputConsumersTests
{
    private const string Source = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Next = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Declaration = "workflow-delivery.quality.yml";

    [TestMethod]
    public void MapKeepsNativeVersionInputsAndAbsentConfigurationCandidates()
    {
        NbgvInputs[] projects = [new(Source, "src/a",
            ["version.json", "src/version.json", "src/a/version.json"],
            ["src/a/version.json", "shared/Version Ω.json"]),
            new(Source, "src/b", ["version.json", "src/version.json"],
                ["shared/Version Ω.json"])];
        GitRevision revision = Revision(Source, "src/a/version.json", "shared/Version Ω.json",
            "src/a/" + Declaration, "src/b/" + Declaration);

        SharedInput[] inputs = NodeInputConsumers.Map(revision,
            projects.Select(project => project.Directory).ToArray(), projects,
            LeafSelections("src/a", "src/b"), []);

        AssertConsumers(inputs, "version.json", "src/a", "src/b");
        AssertConsumers(inputs, "src/version.json", "src/a", "src/b");
        AssertConsumers(inputs, "src/a/version.json", "src/a");
        AssertConsumers(inputs, "shared/Version Ω.json", "src/a", "src/b");
        string[] expected = ["version.json", "src/version.json", "src/a/version.json",
            "shared/Version Ω.json", "src/a/" + Declaration, "src/b/" + Declaration];
        CollectionAssert.AreEquivalent(expected, inputs.Select(input => input.Path).ToArray());
    }

    [TestMethod]
    [DataRow("src/group/" + Declaration, 1)]
    [DataRow("src/" + Declaration, 2)]
    [DataRow("", 3)]
    public void MapKeepsQualitySearchThroughWinningAncestor(string sourcePath, int expectedCount)
    {
        GitRevision revision = sourcePath.Length == 0
            ? Revision(Source, "src/group/index.js")
            : Revision(Source, "src/group/index.js", sourcePath);
        QualitySelection? selection = sourcePath.Length == 0 ? null : new("preset", sourcePath);
        var quality = new Dictionary<string, QualitySelection?> { ["src/group"] = selection };

        SharedInput[] inputs = NodeInputConsumers.Map(revision, ["src/group"],
            [Project("src/group")], quality, []);

        string[] expected = expectedCount switch
        {
            1 => ["src/group/" + Declaration],
            2 => ["src/group/" + Declaration, "src/" + Declaration],
            _ => ["src/group/" + Declaration, "src/" + Declaration, Declaration],
        };
        CollectionAssert.AreEquivalent(expected, inputs.Select(input => input.Path).ToArray());
        string[] expectedConsumers = ["src/group"];
        foreach (SharedInput input in inputs)
            CollectionAssert.AreEqual(expectedConsumers, input.Consumers);
    }

    [TestMethod]
    public void MapExpandsCommittedLocalInputs()
    {
        GitRevision revision = Revision(Source, "src/a/" + Declaration,
            "src/b/" + Declaration, "src/c/" + Declaration,
            "vendor/data.txt", "vendor/nested/data.txt", "vendor-neighbor/data.txt",
            "archives/input.tgz", "archives/unrelated.tgz");
        PnpmLocalInput[] local = [new("vendor", true, ["src/a"]),
            new("archives/input.tgz", false, ["src/a", "src/b"]),
            new("vendor/nested/data.txt", false, ["src/b"])];

        SharedInput[] inputs = NodeInputConsumers.Map(revision, ["src/a", "src/b", "src/c"],
            [Project("src/a"), Project("src/b"), Project("src/c")],
            LeafSelections("src/a", "src/b", "src/c"), local);

        AssertConsumers(inputs, "vendor/data.txt", "src/a");
        AssertConsumers(inputs, "vendor/nested/data.txt", "src/a", "src/b");
        AssertConsumers(inputs, "archives/input.tgz", "src/a", "src/b");
        Assert.DoesNotContain("vendor-neighbor/data.txt", inputs.Select(input => input.Path));
        Assert.DoesNotContain("archives/unrelated.tgz", inputs.Select(input => input.Path));
        string[] expected = ["src/a/" + Declaration, "src/b/" + Declaration,
            "src/c/" + Declaration, "vendor/data.txt", "vendor/nested/data.txt",
            "archives/input.tgz"];
        CollectionAssert.AreEquivalent(expected, inputs.Select(input => input.Path).ToArray());
    }

    [TestMethod]
    public async Task VersionAndLocalInputsSelectEachConsumerOnceWithoutRequiringVersionFacts()
    {
        const string path = "archives/input.tgz";
        NodeRevisionInputs Endpoint(string revision)
        {
            NodeRevisionInputs inputs = NodeScenario.Inputs(revision,
                NodeScenario.Project("src/a"), NodeScenario.Project("src/b"),
                NodeScenario.Project("src/unrelated"));
            return inputs with
            {
                Revision = inputs.Revision with
                {
                    Entries = [.. inputs.Revision.Entries,
                    NodeScenario.File(path)]
                },
                Versions = [inputs.Versions[0] with { Paths = [path] }],
                Graph = inputs.Graph with { LocalInputs = [new(path, false, ["src/a", "src/b"])] }
            };
        }
        CiPlan plan = await NodeScenario.Plan(Endpoint(Source), Endpoint(Next), path);

        CheckKey[] expected = [new("src/a", "node/project-build-v1", "default"),
            new("src/a", "node/project-test-v1", "default"),
            new("src/b", "node/project-build-v1", "default"),
            new("src/b", "node/project-test-v1", "default")];
        CollectionAssert.AreEquivalent(expected, plan.Checks.Select(check => check.Work.Key)
            .ToArray());
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("duplicate")]
    [DataRow("missing-quality")]
    [DataRow("unknown-quality")]
    [DataRow("non-ancestor-quality")]
    [DataRow("uncommitted-quality")]
    [DataRow("unknown-local-consumer")]
    [DataRow("root-local-consumer")]
    [DataRow("uncommitted-version-input")]
    public void MapRejectsInvalidConsumers(string scenario)
    {
        GitRevision revision = Revision(Source, "src/a/" + Declaration,
            "src/b/" + Declaration, "shared/data.txt");
        NbgvInputs[] projects = [Project("src/a")];
        Dictionary<string, QualitySelection?> quality = LeafSelections("src/a");
        PnpmLocalInput[] local = [];
        switch (scenario)
        {
            case "root": projects = [Project(".")]; break;
            case "duplicate": projects = [Project("src/a"), Project("src/a")]; break;
            case "missing-quality": quality.Clear(); break;
            case "unknown-quality": quality = LeafSelections("src/b"); break;
            case "non-ancestor-quality":
                quality["src/a"] = new("preset", "src/b/" + Declaration);
                break;
            case "uncommitted-quality":
                quality["src/a"] = new("preset", "src/" + Declaration);
                break;
            case "unknown-local-consumer":
                local = [new("shared/data.txt", false, ["src/b"])];
                break;
            case "root-local-consumer": local = [new("shared/data.txt", false, ["."])]; break;
            case "uncommitted-version-input":
                projects = [new(Source, "src/a", [], ["missing.txt"])]; break;
        }

        Assert.ThrowsExactly<InvalidDataException>(() =>
            NodeInputConsumers.Map(revision,
                projects.Select(project => project.Directory).ToArray(), projects, quality, local));
    }

    [TestMethod]
    [DataRow("missing.txt", false, "100644", "blob")]
    [DataRow("missing", true, "100644", "blob")]
    [DataRow("src:input", false, "100644", "blob")]
    [DataRow("vendor/data.txt", false, "120000", "blob")]
    [DataRow("vendor", true, "120000", "blob")]
    [DataRow("vendor", true, "160000", "commit")]
    public void MapRejectsUnavailableLocalInputs(string path, bool directory, string mode,
        string objectType)
    {
        GitRevision revision = Revision(Source, "src/a/" + Declaration) with
        {
            Entries = [File("src/a/" + Declaration),
                new("vendor/data.txt", mode, objectType)],
        };

        Assert.ThrowsExactly<InvalidDataException>(() => NodeInputConsumers.Map(revision, ["src/a"],
            [Project("src/a")], LeafSelections("src/a"), [new(path, directory, ["src/a"])]));
    }

    [TestMethod]
    public async Task ChangedVersionFilesKeepOnlyTheirActualEndpointConsumers()
    {
        string[] directories = ["src/a", "src/b", "src/unrelated"];
        NodeRevisionInputs Endpoint(string revision, string path, string consumer)
        {
            NodeRevisionInputs inputs = NodeScenario.Inputs(revision,
                directories.Select(directory => NodeScenario.Project(directory)).ToArray());
            return inputs with
            {
                Revision = inputs.Revision with
                {
                    Entries = [.. inputs.Revision.Entries,
                    NodeScenario.File(path)]
                },
                Versions = [new(revision, consumer, [], [path])]
            };
        }
        CiPlan plan = await NodeScenario.Plan(Endpoint(Source, "shared/old.txt", "src/a"),
            Endpoint(Next, "shared/new.txt", "src/b"), "shared/old.txt", "shared/new.txt");

        CollectionAssert.AreEquivalent(ExpectedVersionConsumers,
            plan.Checks.Select(check => check.Work.Key.Target).Distinct().ToArray());
        foreach (PlannedCheck check in plan.Checks)
        {
            SelectionReason reason = check.Work.Key.Target == "src/a"
                ? new("shared/old.txt", Source, "src/a") : new("shared/new.txt", Next, "src/b");
            Assert.AreEqual(reason, Assert.ContainsSingle(check.Reasons));
        }
    }

    private static readonly string[] ExpectedVersionConsumers = ["src/a", "src/b"];

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("duplicate")]
    [DataRow("revision")]
    public void MapRejectsVersionAnswerForUnknownDuplicateOrWrongRevisionConsumer(string defect)
    {
        NbgvInputs[] versions = defect switch
        {
            "unknown" => [Project("src/unknown")],
            "duplicate" => [Project("src/a"), Project("src/a")],
            _ => [new(Next, "src/a", [], [])],
        };

        Assert.ThrowsExactly<InvalidDataException>(() => NodeInputConsumers.Map(
            Revision(Source, "src/a/" + Declaration), ["src/a"], versions,
            LeafSelections("src/a"), []));
    }

    private static NbgvInputs Project(string directory) => new(Source, directory, [], []);

    private static GitEntry File(string path) => new(path, "100644", "blob");

    private static GitRevision Revision(string commit, params string[] files) =>
        new(commit, files.Select(File).ToArray());

    private static Dictionary<string, QualitySelection?> LeafSelections(params string[] projects) =>
        projects.ToDictionary(directory => directory,
            directory => (QualitySelection?)new("preset", directory + "/" + Declaration));

    private static void AssertConsumers(SharedInput[] inputs, string path,
        params string[] consumers)
    {
        SharedInput input = Assert.ContainsSingle(inputs.Where(input => input.Path == path));
        CollectionAssert.AreEqual(consumers, input.Consumers);
    }
}
