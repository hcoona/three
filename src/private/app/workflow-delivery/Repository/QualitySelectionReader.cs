using YamlDotNet.Serialization;

namespace WorkflowDelivery.Repository;

internal sealed record QualitySelection(string Preset, string SourcePath);

// Only application-owned YAML is interpreted here. The caller supplies a materialized revision.
internal sealed class QualitySelectionReader
{
    private const string FileName = "workflow-delivery.quality.yml";
    private readonly string root;

    internal QualitySelectionReader(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    internal async Task<QualitySelection?> ReadAsync(string manifestDirectory, string ecosystem,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(ecosystem);
        if (Path.IsPathRooted(manifestDirectory) || manifestDirectory.Contains('\\') ||
            manifestDirectory.Contains(':') || (manifestDirectory != "." &&
                manifestDirectory.Split('/').Any(part => part is "" or "." or "..")))
            throw new InvalidDataException(
                "Expected a canonical repository-relative manifest directory.");

        string directory = manifestDirectory == "." ? root : Path.Combine(root, manifestDirectory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("The manifest directory is not materialized.");

        while (true)
        {
            token.ThrowIfCancellationRequested();
            string path = Path.Combine(directory, FileName);
            string? content = null;
            try
            {
                content = await File.ReadAllTextAsync(path, token);
            }
            catch (FileNotFoundException)
            {
                // Only descriptor absence allows ancestor search; other I/O failures propagate.
            }

            if (content is not null)
            {
                Dictionary<string, string> selections = ReadSelections(content);
                if (selections.TryGetValue(ecosystem, out string? preset))
                    return new(preset, Path.GetRelativePath(root, path).Replace('\\', '/'));
            }

            if (directory == root)
                return null;
            directory = Path.GetDirectoryName(directory)!;
        }
    }

    private static Dictionary<string, string> ReadSelections(string content)
    {
        object? declaration = new DeserializerBuilder().WithDuplicateKeyChecking().Build()
            .Deserialize<object?>(content);
        Dictionary<string, object?> fields = Mapping(declaration);
        if (fields.Count != 2 || !fields.TryGetValue("schema", out object? schema) ||
            Scalar(schema) != "workflow-delivery/v3/quality-selection" ||
            !fields.TryGetValue("ecosystems", out object? ecosystems))
            throw new InvalidDataException(
                "Invalid quality-selection declaration fields or schema.");
        Dictionary<string, object?> entries = Mapping(ecosystems);
        if (entries.Count == 0)
            throw new InvalidDataException("Quality selection requires ecosystem entries.");
        var selections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string ecosystem, object? selection) in entries)
        {
            Dictionary<string, object?> entry = Mapping(selection);
            if (entry.Count != 1 || !entry.TryGetValue("preset", out object? preset))
                throw new InvalidDataException("An ecosystem selection requires only a preset.");
            selections.Add(ecosystem, Scalar(preset));
        }
        return selections;
    }

    private static Dictionary<string, object?> Mapping(object? node)
    {
        if (node is not IDictionary<object, object?> mapping)
            throw new InvalidDataException("Expected a quality-selection mapping.");
        return mapping.ToDictionary(pair => Scalar(pair.Key), pair => pair.Value,
            StringComparer.Ordinal);
    }

    private static string Scalar(object? node) =>
        node is string scalar && !string.IsNullOrWhiteSpace(scalar)
            ? scalar
            : throw new InvalidDataException("Expected a nonempty quality-selection identifier.");
}
