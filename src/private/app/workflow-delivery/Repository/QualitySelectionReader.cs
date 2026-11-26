using static WorkflowDelivery.Repository.StrictYaml;

namespace WorkflowDelivery.Repository;

internal sealed record QualitySelection(string Preset, string SourcePath);

// Only application-owned YAML is interpreted here. The caller supplies a materialized revision.
internal sealed class QualitySelectionReader
{
    private const string FileName = "workflow-delivery.quality.yml";
    private readonly string root;
    private readonly Func<string, CancellationToken, Task<string?>> read;

    internal QualitySelectionReader(string root)
        : this(root, async (path, token) =>
        {
            try
            {
                return await File.ReadAllTextAsync(Path.Combine(root, path), token);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        })
    { }

    internal QualitySelectionReader(string root,
        Func<string, CancellationToken, Task<string?>> read)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        this.read = read;
    }

    internal async Task<QualitySelection?> ReadAsync(string manifestDirectory, string ecosystem,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(ecosystem);
        RelativePath(manifestDirectory, allowDirectoryRoot: true);

        string directory = manifestDirectory == "." ? root : Path.Combine(root, manifestDirectory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("The manifest directory is not materialized.");

        while (true)
        {
            token.ThrowIfCancellationRequested();
            string path = Path.GetRelativePath(root, Path.Combine(directory, FileName))
                .Replace('\\', '/');
            // A bound committed reader distinguishes true absence from untracked files.
            string? content = await read(path, token);

            if (content is not null)
            {
                Dictionary<string, string> selections = ReadSelections(content, path);
                if (selections.TryGetValue(ecosystem, out string? preset))
                    return new(preset, path);
            }

            if (directory == root)
                return null;
            directory = Path.GetDirectoryName(directory)!;
        }
    }

    private static Dictionary<string, string> ReadSelections(string content, string sourcePath)
    {
        const string declaration = "quality-selection";
        Dictionary<string, object?> fields = Mapping(Parse(content, declaration, sourcePath),
            declaration, "schema", "ecosystems");
        if (Text(fields["schema"]) != "workflow-delivery/v3/quality-selection")
            throw new InvalidDataException("Invalid quality-selection schema.");
        Dictionary<string, object?> entries = Mapping(fields["ecosystems"], declaration);
        if (entries.Count == 0)
            throw new InvalidDataException("Quality selection requires ecosystem entries.");
        var selections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string ecosystem, object? selection) in entries)
        {
            selections.Add(ecosystem, Text(Mapping(selection, declaration, "preset")["preset"]));
        }
        return selections;
    }
}
