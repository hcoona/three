using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace WorkflowDelivery.Repository;

internal sealed record ReleaseOutput(string Id, string Role, string Kind);
internal sealed record ReleaseBuild(string Id, string Definition, string EntryPoint,
    ReleaseOutput[] Outputs);
internal sealed record ReleaseUnitDeclaration(string SourcePath, string Id, ReleaseBuild[] Builds);

// The caller obtains exact committed content; parsing supplied text cannot establish its revision.
internal static class ReleaseUnitDeclarationReader
{
    internal static ReleaseUnitDeclaration Read(string content, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(content);
        string path = RelativePath(sourcePath, allowDirectoryRoot: false);
        object? document;
        try
        {
            document = new DeserializerBuilder().WithDuplicateKeyChecking().Build()
                .Deserialize<object?>(content);
        }
        catch (YamlException exception)
        {
            throw new InvalidDataException($"Invalid release-unit YAML at {path}.", exception);
        }
        Dictionary<string, object?> unit = Mapping(document, "schema", "release-unit", "builds");
        if (Text(unit["schema"]) != "workflow-delivery/v3/release-unit")
            throw new InvalidDataException("Invalid release-unit schema.");

        string id = Identity(unit["release-unit"]);
        var builds = new List<ReleaseBuild>();
        var buildIds = new HashSet<string>(StringComparer.Ordinal);
        var outputIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (object? value in Sequence(unit["builds"]))
        {
            Dictionary<string, object?> build = Mapping(value, "id", "definition", "entry-point",
                "outputs");
            string buildId = Identity(build["id"]);
            if (!buildIds.Add(buildId))
                throw new InvalidDataException("Duplicate release build identity.");
            string definition = Text(build["definition"]);
            string entryPoint = RelativePath(build["entry-point"], allowDirectoryRoot: true);
            var outputs = new List<ReleaseOutput>();
            foreach (object? outputValue in Sequence(build["outputs"]))
            {
                Dictionary<string, object?> output = Mapping(outputValue, "id", "role", "kind");
                string outputId = Identity(output["id"]);
                if (!outputIds.Add(outputId))
                    throw new InvalidDataException("Duplicate release output identity.");
                outputs.Add(new(outputId, Identity(output["role"]), Identity(output["kind"])));
            }
            builds.Add(new(buildId, definition, entryPoint, [.. outputs]));
        }
        return new(path, id, [.. builds]);
    }

    private static Dictionary<string, object?> Mapping(object? node, params string[] fields)
    {
        if (node is not IDictionary<object, object?> mapping)
            throw new InvalidDataException("Expected a release-unit mapping.");
        Dictionary<string, object?> result = mapping.ToDictionary(pair => Text(pair.Key),
            pair => pair.Value, StringComparer.Ordinal);
        if (result.Count != fields.Length || fields.Any(field => !result.ContainsKey(field)))
            throw new InvalidDataException("Invalid release-unit declaration fields.");
        return result;
    }

    private static List<object?> Sequence(object? node) =>
        node is List<object?> { Count: > 0 } sequence
            ? sequence
            : throw new InvalidDataException("Expected a nonempty release-unit sequence.");

    private static string Text(object? node) =>
        node is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidDataException("Expected nonempty release-unit text.");

    private static string Identity(object? node)
    {
        string value = Text(node);
        if (value[0] == '-' || value[^1] == '-' ||
            value.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new InvalidDataException("Expected a canonical release-unit identity.");
        return value;
    }

    private static string RelativePath(object? node, bool allowDirectoryRoot)
    {
        string value = Text(node);
        if (allowDirectoryRoot && value == ".")
            return value;
        if (value.Contains('\\') || value.Contains(':') ||
            value.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Expected a canonical relative release-unit path.");
        return value;
    }
}
