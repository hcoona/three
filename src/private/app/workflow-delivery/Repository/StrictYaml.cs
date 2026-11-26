using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace WorkflowDelivery.Repository;

internal static class StrictYaml
{
    internal static object? Parse(string content, string declaration, string path)
    {
        try
        {
            return new DeserializerBuilder().WithDuplicateKeyChecking().Build()
                .Deserialize<object?>(content);
        }
        catch (YamlException exception)
        {
            throw new InvalidDataException($"Invalid {declaration} YAML at {path}.", exception);
        }
    }

    internal static Dictionary<string, object?> Mapping(object? node, string declaration,
        params string[] fields)
    {
        if (node is not IDictionary<object, object?> mapping)
            throw new InvalidDataException($"Expected a {declaration} mapping.");
        Dictionary<string, object?> result = mapping.ToDictionary(pair => Text(pair.Key),
            pair => pair.Value, StringComparer.Ordinal);
        if (fields.Length != 0 && (result.Count != fields.Length ||
            fields.Any(field => !result.ContainsKey(field))))
            throw new InvalidDataException($"Invalid {declaration} declaration fields.");
        return result;
    }

    internal static List<object?> Sequence(object? node) =>
        node is List<object?> { Count: > 0 } sequence
            ? sequence : throw new InvalidDataException("Expected a nonempty YAML sequence.");

    internal static string Text(object? node) =>
        node is string text && !string.IsNullOrWhiteSpace(text)
            ? text : throw new InvalidDataException("Expected nonempty YAML text.");

    internal static string RelativePath(object? node, bool allowDirectoryRoot)
    {
        string path = Text(node);
        if (allowDirectoryRoot && path == ".") return path;
        if (Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Expected a canonical repository-relative path.");
        return path;
    }
}
