using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonMetadataReaderTests(TestContext context)
{
    private static readonly string Root = Path.GetFullPath("python endpoint");

    [TestMethod]
    public void OpaqueIdsAndStructuredSourcesRetainDimensionsAndMarkers()
    {
        PythonMetadata facts = PythonMetadataReader.Parse(Fixture().ToJsonString(), Root);

        Assert.AreEqual("opaque workspace", facts.WorkspaceId);
        Assert.AreEqual(">=3.14", facts.RequiresPython);
        Assert.HasCount(2, facts.Members);
        PythonMetadataMember producer = facts.Members.Single(member => member.Name == "producer");
        Assert.AreEqual("src/plugin", producer.Directory);
        Assert.AreEqual("not-a-path:registry+misleading", producer.Id);
        Assert.AreEqual(Path.Combine(Root, "src", "plugin"),
            facts.Nodes[producer.Id].Source!.LocalPath);
        PythonMetadataNode consumer = facts.Nodes["consumer opaque"];
        Assert.AreEqual("python_version >= '3.14'", consumer.Dependencies.Single().Marker);
        Assert.AreEqual("native extra id", consumer.Dependencies.Single().Id);
        Assert.AreEqual("feature", facts.Nodes["native extra id"].Dimension);
        Assert.AreEqual("dev", consumer.Groups.Single().Name);
        Assert.AreEqual("dev", facts.Nodes["native group id"].Dimension);
        Assert.AreEqual("dev", facts.Nodes["tableless root group"].Dimension);
        Assert.IsNull(facts.Nodes["tableless root group"].Source);
    }

    [TestMethod]
    public void RecordedNativePreviewReplaysWithIdsUnchangedAcrossCheckoutCoordinates()
    {
        using Stream stream = typeof(PythonMetadataReaderTests).Assembly
            .GetManifestResourceStream(
                "WorkflowDelivery.Tests.Repository.fixtures.python-metadata.json")!;
        JsonNode original = JsonNode.Parse(stream)!;
        string[] ids = original["resolution"]!.AsObject().Select(pair => pair.Key).ToArray();
        Reroot(original);
        PythonMetadata facts = PythonMetadataReader.Parse(original.ToJsonString(), Root);

        CollectionAssert.AreEquivalent(ids, facts.Nodes.Keys.ToArray());
        Assert.HasCount(262, facts.Nodes);
        Assert.HasCount(16, facts.Members);
        Assert.ContainsSingle(facts.Members.Where(member => member.Directory == "."));
        PythonMetadataMember smoke = facts.Members.Single(member =>
            member.Name == "hcoona-release-smoke-python");
        Assert.AreEqual("src/public/lib/hcoona-release-smoke-python", smoke.Directory);
        Assert.IsFalse(facts.Nodes[smoke.Id].Dependencies.Any(edge =>
            facts.Nodes[edge.Id].Name == "nbgv-python"));
        PythonMetadataMember sample = facts.Members.Single(member =>
            member.Name == "nbgv-hatch-demo");
        Assert.IsTrue(facts.Nodes[sample.Id].Dependencies.Any(edge =>
            facts.Nodes[edge.Id].Name == "nbgv-python"));
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("environment")]
    [DataRow("conflict")]
    [DataRow("workspace")]
    [DataRow("dangling")]
    [DataRow("member-source")]
    [DataRow("member-outside")]
    [DataRow("duplicate-member")]
    [DataRow("dimension-owner")]
    [DataRow("unknown-kind")]
    [DataRow("unknown-source")]
    public void NecessaryUnknownOrAmbiguousMetadataReturnsNoFacts(string failure)
    {
        JsonNode value = Fixture();
        JsonNode node = value["resolution"]!["consumer opaque"]!;
        switch (failure)
        {
            case "schema": value["schema"]!["version"] = "future"; break;
            case "environment": value["environment"] = new JsonObject(); break;
            case "conflict": value["conflicts"]!["sets"] = new JsonArray(new JsonObject()); break;
            case "workspace": value["workspace"]!["id"] = "missing"; break;
            case "dangling": node["dependencies"]![0]!["id"] = "missing"; break;
            case "member-source": node["source"]!["editable"] = Root; break;
            case "member-outside":
                value["members"]![0]!["path"] = Path.GetDirectoryName(Root); break;
            case "duplicate-member":
                value["members"]!.AsArray().Add(value["members"]![0]!.DeepClone()); break;
            case "dimension-owner":
                value["resolution"]!["native group id"]!["name"] = "producer"; break;
            case "unknown-kind": node["kind"] = "build"; break;
            case "unknown-source": node["source"] = new JsonObject { ["unknown"] = "value" }; break;
        }

        Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonMetadataReader.Parse(value.ToJsonString(), Root));
    }

    [TestMethod]
    public void DuplicateNodeIdentityAndFieldsAreRejected()
    {
        string json = Fixture().ToJsonString();
        string duplicate = json.Replace("\"requires_python\":",
            "\"requires_python\":\">=3.14\",\"requires_python\":",
            StringComparison.Ordinal);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonMetadataReader.Parse(duplicate, Root));
        duplicate = json.Replace("\"opaque workspace\":",
            "\"opaque workspace\":{},\"opaque workspace\":",
            StringComparison.Ordinal);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonMetadataReader.Parse(duplicate, Root));
    }

    [TestMethod]
    public void NativePortablePathSeparatorsDoNotChangeOpaqueIds()
    {
        JsonNode value = Fixture();
        MakePortable(value);
        PythonMetadata metadata = PythonMetadataReader.Parse(value.ToJsonString(), Root);

        Assert.AreEqual(Root, metadata.Root);
        Assert.AreEqual("src/plugin", metadata.Members.Single(member =>
            member.Name == "producer").Directory);
        Assert.AreEqual("not-a-path:registry+misleading",
            metadata.Members.Single(member => member.Name == "producer").Id);
        Assert.AreEqual(Path.Combine(Root, "src", "plugin"),
            metadata.Nodes["not-a-path:registry+misleading"].Source!.LocalPath);
    }

    [TestMethod]
    public async Task NativeRequestUsesFrozenLockAndExplicitInterpreterWithoutSync()
    {
        string interpreter = Path.Combine(Root, "tools", "python");
        var environment = new Dictionary<string, string?> { ["UV_PYTHON_DOWNLOADS"] = "never" };
        var reader = new PythonMetadataReader((command, token) =>
        {
            Assert.AreEqual(context.CancellationToken, token);
            Assert.AreEqual(Root, command.Directory);
            Assert.AreEqual("uv", command.Executable);
            CollectionAssert.AreEqual(new[]
                { "workspace", "metadata", "--frozen", "--python", interpreter },
                command.Arguments);
            Assert.AreSame(environment, command.Environment);
            return Task.FromResult(new NativeCommandResult(NativeTermination.Exited, 0,
                Fixture().ToJsonString(), "", 0, null));
        });

        PythonMetadata facts = await reader.ReadAsync(Root, "uv", interpreter, environment,
            context.CancellationToken);

        Assert.HasCount(2, facts.Members);
    }

    [TestMethod]
    [DataRow((int)NativeTermination.StartFailed, null, null)]
    [DataRow((int)NativeTermination.TimedOut, 0, null)]
    [DataRow((int)NativeTermination.CleanupFailed, 0, "cleanup")]
    [DataRow((int)NativeTermination.InvalidOutput, 0, "output")]
    [DataRow((int)NativeTermination.Exited, 1, null)]
    [DataRow((int)NativeTermination.Exited, 0, "transport")]
    public async Task NativeFailureCannotReturnOtherwiseValidPartialFacts(
        int termination, int? code, string? error)
    {
        var reader = new PythonMetadataReader((_, _) => Task.FromResult(new NativeCommandResult(
            (NativeTermination)termination, code, Fixture().ToJsonString(), "", 0, error)));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadAsync(Root,
            "uv", Path.Combine(Root, "python"), new Dictionary<string, string?>(),
            context.CancellationToken));
    }

    internal static JsonNode Fixture(string? root = null)
    {
        root ??= Root;
        string consumer = Path.Combine(root, "src", "consumer");
        string producer = Path.Combine(root, "src", "plugin");
        return JsonSerializer.SerializeToNode(new
        {
            schema = new { version = "preview" },
            workspace_root = root,
            workspace = new { path = root, id = "opaque workspace" },
            requires_python = ">=3.14",
            conflicts = new { sets = Array.Empty<object>() },
            members = new[]
            {
                new { name = "consumer", path = consumer, id = "consumer opaque" },
                new { name = "producer", path = producer, id = "not-a-path:registry+misleading" },
            },
            resolution = new Dictionary<string, object>
            {
                ["opaque workspace"] = new
                {
                    kind = "workspace",
                    path = root,
                    dependencies = Array.Empty<object>(),
                    dependency_groups = new[] { new { name = "dev", id = "tableless root group" } }
                },
                ["tableless root group"] = new
                {
                    kind = new { group = "dev" },
                    path = root,
                    dependencies = Array.Empty<object>()
                },
                ["consumer opaque"] = new
                {
                    kind = "package",
                    name = "consumer",
                    source = new { editable = consumer },
                    dependencies = new[]
                        { new { id = "native extra id", marker = "python_version >= '3.14'" } },
                    dependency_groups = new[] { new { name = "dev", id = "native group id" } }
                },
                ["native group id"] = new
                {
                    kind = new { group = "dev" },
                    name = "consumer",
                    source = new { editable = consumer },
                    dependencies = Array.Empty<object>()
                },
                ["not-a-path:registry+misleading"] = new
                {
                    kind = "package",
                    name = "producer",
                    source = new { editable = producer },
                    dependencies = Array.Empty<object>(),
                    optional_dependencies = new[]
                        { new { name = "feature", id = "native extra id" } }
                },
                ["native extra id"] = new
                {
                    kind = new { extra = "feature" },
                    name = "producer",
                    source = new { editable = producer },
                    dependencies = new[] { new { id = "not-a-path:registry+misleading" } }
                },
            },
        })!;
    }

    internal static void Reroot(JsonNode? value, string? root = null)
    {
        root ??= Root;
        if (value is JsonObject obj)
            foreach ((string key, JsonNode? child) in obj.ToArray())
            {
                if (key is "workspace_root" or "path" or "directory" or "editable" or "virtual" &&
                    child is JsonValue field && field.TryGetValue(out string? text) &&
                    (text == "/recorded/python-endpoint" || text.StartsWith(
                        "/recorded/python-endpoint/", StringComparison.Ordinal)))
                    obj[key] = root + text["/recorded/python-endpoint".Length..]
                        .Replace('/', Path.DirectorySeparatorChar);
                else Reroot(child, root);
            }
        else if (value is JsonArray array)
            foreach (JsonNode? child in array) Reroot(child, root);
    }

    private static void MakePortable(JsonNode? value)
    {
        if (value is JsonObject obj)
            foreach ((string key, JsonNode? child) in obj.ToArray())
            {
                if (key is "workspace_root" or "path" or "editable" &&
                    child is JsonValue field && field.TryGetValue(out string? text))
                    obj[key] = text.Replace('\\', '/');
                else MakePortable(child);
            }
        else if (value is JsonArray array)
            foreach (JsonNode? child in array) MakePortable(child);
    }
}
