using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NativeMessageSchemasTests
{
    [TestMethod]
    public void RetainedSchemasMatchActualSerializationContracts()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,
            "AGENTS.md"))) root = root.Parent;
        Assert.IsNotNull(root);
        string directory = Path.Combine(root.FullName,
            "src/private/app/workflow-delivery/Native/contracts");
        foreach ((string name, JsonNode schema) in NativeMessageSchemas.Export())
        {
            Assert.IsTrue(JsonNode.DeepEquals(schema, JsonNode.Parse(File.ReadAllText(
                Path.Combine(directory, name + ".schema.json")))),
                    "Regenerate changed wire schema: " + name);
            Assert.AreEqual("object", schema["type"]!.GetValue<string>());
        }
        JsonNode ruby = NativeMessageSchemas.Export()["ruby-source"];
        Assert.IsFalse(ruby["additionalProperties"]!.GetValue<bool>());
        Assert.Contains("gemspec", ruby["required"]!.AsArray().Select(
            value => value!.GetValue<string>()));
        Assert.IsNotNull(ruby["properties"]!["require_paths"]);
        Assert.IsNull(ruby["properties"]!["gem"]);

    }

    [TestMethod]
    public void RubyWireReaderRejectsMalformedShapeAndRetainsSemanticBinding()
    {
        string path = Path.Combine(Path.GetTempPath(), "native.gemspec");
        var message = new RubySourceMessage { Gemspec = path, Name = "native", Version = "1.0",
            Platform = "ruby", RequirePaths = ["lib"], Files = ["lib/native.rb"],
                RuntimeDependencies = [] };
        string json = JsonSerializer.Serialize(message, RubyFactJson.Default.RubySourceMessage);
        Assert.AreEqual("native", RubyNativeFactsReader.ParseSource(json, path).Gem.Name);
        JsonObject wrong = JsonNode.Parse(json)!.AsObject();
        wrong.Remove("files");
        Assert.ThrowsExactly<InvalidDataException>(() => RubyNativeFactsReader.ParseSource(
            wrong.ToJsonString(), path));
        wrong = JsonNode.Parse(json)!.AsObject();
        wrong["invented"] = true;
        Assert.ThrowsExactly<InvalidDataException>(() => RubyNativeFactsReader.ParseSource(
            wrong.ToJsonString(), path));
        Assert.ThrowsExactly<InvalidDataException>(() => RubyNativeFactsReader.ParseSource(json,
            path + ".other"));
    }
}
