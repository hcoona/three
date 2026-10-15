using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace WorkflowDelivery.Repository;

// Static shape descriptions for the existing independent helper producers/consumers.
internal static class NativeMessageSchemas
{
    internal static IReadOnlyDictionary<string, JsonNode> Export() => new Dictionary<string,
        JsonNode>
    {
        ["ruby-fact-request"] = Schema(TransferJson.Default.RubyFactRequest),
        ["ruby-source-request"] = Schema(RubyFactJson.Default.RubySourceRequest),
        ["ruby-source-answer"] = Schema(RubyFactJson.Default.RubySourceAnswer),
        ["ruby-source"] = Schema(RubyFactJson.Default.RubySourceMessage),
        ["ruby-archive"] = Schema(RubyFactJson.Default.RubyArchiveMessage),
        ["ruby-context"] = Schema(RubyFactJson.Default.RubyContextMessage),
        ["ruby-version"] = Schema(RubyFactJson.Default.RubyVersionMessage),
        ["ruby-bundle"] = Schema(RubyFactJson.Default.RubyBundleMessage),
    };

    internal static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach ((string name, JsonNode schema) in Export())
            File.WriteAllText(Path.Combine(directory, name + ".schema.json"),
                schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
    private static JsonNode Schema(JsonTypeInfo type) => JsonSchemaExporter.GetJsonSchemaAsNode(
        type,
        new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
}
