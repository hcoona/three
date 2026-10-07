using System.Text.Json;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record PythonMetadataEdge(string Id, string? Marker);
internal sealed record PythonMetadataEntry(string Name, string Id);
internal sealed record PythonMetadataSource(string Kind, JsonElement Value, string? LocalPath);
internal sealed record PythonMetadataNode(string Id, string Kind, string? Dimension,
    string? Name, PythonMetadataSource? Source, PythonMetadataEdge[] Dependencies,
    PythonMetadataEntry[] Groups, PythonMetadataEntry[] Extras);
internal sealed record PythonMetadataMember(string Name, string Directory, string Id);
internal sealed record PythonMetadata(string Root, string WorkspaceId, string RequiresPython,
    PythonMetadataMember[] Members, IReadOnlyDictionary<string, PythonMetadataNode> Nodes);

// UV owns resolution and marker semantics. IDs are keys, never coordinates to decode.
internal sealed class PythonMetadataReader
{
    private readonly Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute;

    internal PythonMetadataReader() : this(NativeProcess.ExecuteAsync) { }

    internal PythonMetadataReader(
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute) =>
        this.execute = execute;

    internal async Task<PythonMetadata> ReadAsync(string root, string uv, string interpreter,
        IReadOnlyDictionary<string, string?> environment, CancellationToken token)
    {
        Absolute(root);
        Absolute(interpreter);
        NativeCommandResult result = await execute(new(uv, root,
            ["workspace", "metadata", "--frozen", "--python", interpreter], 30, environment),
            token);
        if (!result.Succeeded || result.Error is not null)
            throw new InvalidDataException("Python metadata extraction failed.");
        return Parse(result.Stdout, root);
    }

    internal static PythonMetadata Parse(string text, string root)
    {
        Absolute(root);
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement value = document.RootElement;
            UniqueFields(value);
            if (Text(Field(Field(value, "schema"), "version")) != "preview" ||
                NativePath(Text(Field(value, "workspace_root"))) != root ||
                value.TryGetProperty("environment", out _) ||
                value.TryGetProperty("script", out _) ||
                Array(Field(Field(value, "conflicts"), "sets")).Any())
                throw new InvalidDataException("Unsupported Python metadata request context.");
            JsonElement workspace = Field(value, "workspace");
            if (NativePath(Text(Field(workspace, "path"))) != root)
                throw new InvalidDataException("Python metadata workspace identity differs.");
            string workspaceId = Text(Field(workspace, "id"));
            string requiresPython = Text(Field(value, "requires_python"));
            var nodes = new Dictionary<string, PythonMetadataNode>(StringComparer.Ordinal);
            foreach (JsonProperty property in Field(value, "resolution").EnumerateObject())
            {
                string id = property.Name;
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidDataException("Empty Python metadata node identity.");
                JsonElement node = property.Value;
                JsonElement kind = Field(node, "kind");
                string label;
                string? dimension = null;
                if (kind.ValueKind == JsonValueKind.String)
                {
                    label = Text(kind);
                    if (label is not ("package" or "workspace"))
                        throw new InvalidDataException("Unsupported Python metadata node kind.");
                }
                else
                {
                    JsonProperty[] tags = kind.EnumerateObject().ToArray();
                    if (tags.Length != 1 || tags[0].Name is not ("group" or "extra"))
                        throw new InvalidDataException("Unsupported Python metadata dimension.");
                    label = tags[0].Name;
                    dimension = Text(tags[0].Value);
                }
                string? name = null;
                PythonMetadataSource? source = null;
                if (label == "workspace")
                {
                    if (id != workspaceId || NativePath(Text(Field(node, "path"))) != root)
                        throw new InvalidDataException("Python metadata workspace node differs.");
                }
                else if (label == "group" && node.TryGetProperty("path", out JsonElement path))
                {
                    if (NativePath(Text(path)) != root)
                        throw new InvalidDataException("Python workspace group identity differs.");
                }
                else
                {
                    name = Text(Field(node, "name"));
                    source = Source(Field(node, "source"));
                }
                PythonMetadataEdge[] edges = Array(Field(node, "dependencies"))
                    .Select(edge => new PythonMetadataEdge(Text(Field(edge, "id")),
                        edge.TryGetProperty("marker", out JsonElement marker)
                            ? Text(marker) : null)).ToArray();
                nodes.Add(id, new(id, label, dimension, name, source, edges,
                    Entries(node, "dependency_groups"), Entries(node, "optional_dependencies")));
            }
            if (!nodes.TryGetValue(workspaceId, out PythonMetadataNode? workspaceNode) ||
                workspaceNode.Kind != "workspace")
                throw new InvalidDataException("Python metadata omitted its workspace node.");
            foreach (PythonMetadataNode node in nodes.Values)
            {
                foreach (PythonMetadataEdge edge in node.Dependencies)
                    if (!nodes.ContainsKey(edge.Id))
                        throw new InvalidDataException("Python metadata has a dangling edge.");
                ValidateEntries(node.Groups, "group", node);
                ValidateEntries(node.Extras, "extra", node);
            }
            var directories = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            PythonMetadataMember[] members = Array(Field(value, "members")).Select(member =>
            {
                string name = Text(Field(member, "name"));
                string path = NativePath(Text(Field(member, "path")));
                string directory = Relative(root, path);
                string id = Text(Field(member, "id"));
                if (!directories.Add(directory) || !names.Add(name) || !ids.Add(id) ||
                    !nodes.TryGetValue(id, out PythonMetadataNode? node) ||
                    node.Kind != "package" || node.Name != name ||
                    node.Source?.Kind is not ("directory" or "editable" or "virtual") ||
                    node.Source.LocalPath != path)
                    throw new InvalidDataException("Ambiguous Python metadata member identity.");
                return new PythonMetadataMember(name, directory, id);
            }).ToArray();
            return new(root, workspaceId, requiresPython, members, nodes);

            void ValidateEntries(PythonMetadataEntry[] entries, string kind,
                PythonMetadataNode parent)
            {
                foreach (PythonMetadataEntry entry in entries)
                    if (!nodes.TryGetValue(entry.Id, out PythonMetadataNode? target) ||
                        target.Kind != kind || target.Dimension != entry.Name ||
                        target.Name != parent.Name || !SameSource(target.Source, parent.Source))
                        throw new InvalidDataException(
                            "Python metadata dimension identity differs.");
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("Invalid Python metadata response.", exception);
        }
    }

    private static bool SameSource(PythonMetadataSource? left, PythonMetadataSource? right) =>
        left is null ? right is null : right is not null && left.Kind == right.Kind &&
            JsonElement.DeepEquals(left.Value, right.Value);

    private static PythonMetadataSource Source(JsonElement source)
    {
        JsonProperty[] fields = source.EnumerateObject().ToArray();
        JsonProperty[] tags = fields.Where(field => field.Name != "subdirectory").ToArray();
        if (tags.Length != 1 || tags[0].Name is not
            ("registry" or "git" or "url" or "path" or "directory" or "editable" or "virtual") ||
            (fields.Length != 1 && (tags[0].Name != "url" || fields.Length != 2)))
            throw new InvalidDataException("Unsupported Python metadata source.");
        JsonProperty tag = tags[0];
        string? local = null;
        if (tag.Name == "registry")
        {
            JsonProperty[] locations = tag.Value.EnumerateObject().ToArray();
            if (locations.Length != 1 || locations[0].Name is not ("url" or "path"))
                throw new InvalidDataException("Unsupported Python registry coordinate.");
            _ = Text(locations[0].Value);
        }
        else
        {
            string coordinate = Text(tag.Value);
            if (tag.Name is "path" or "directory" or "editable" or "virtual")
            {
                local = NativePath(coordinate);
            }
        }
        return new(tag.Name, source.Clone(), local);
    }

    private static PythonMetadataEntry[] Entries(JsonElement node, string field)
    {
        if (!node.TryGetProperty(field, out JsonElement value)) return [];
        PythonMetadataEntry[] entries = Array(value).Select(entry =>
            new PythonMetadataEntry(Text(Field(entry, "name")), Text(Field(entry, "id"))))
            .ToArray();
        if (entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() !=
            entries.Length || entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal)
                .Count() != entries.Length)
            throw new InvalidDataException("Duplicate Python metadata dimension.");
        return entries;
    }

    internal static string Relative(string root, string path)
    {
        path = NativePath(path);
        string relative = Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith("../", StringComparison.Ordinal))
            throw new InvalidDataException("Python coordinate is outside its endpoint.");
        return relative;
    }

    // UV's PortablePathBuf serializes forward slashes on Windows. Adapt that
    // supported path format without touching the opaque graph IDs.
    private static string NativePath(string path)
    {
        string native = OperatingSystem.IsWindows() ? path.Replace('/', '\\') : path;
        Absolute(native);
        return native;
    }

    private static void Absolute(string path)
    {
        if (!Path.IsPathFullyQualified(path) ||
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) != path)
            throw new InvalidDataException("Python coordinates require canonical absolute paths.");
    }

    private static void UniqueFields(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty field in value.EnumerateObject())
            {
                if (!names.Add(field.Name))
                    throw new InvalidDataException("Duplicate Python metadata field.");
                UniqueFields(field.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in value.EnumerateArray()) UniqueFields(item);
    }

    private static JsonElement Field(JsonElement value, string name) => value.GetProperty(name);
    private static JsonElement.ArrayEnumerator Array(JsonElement value) => value.EnumerateArray();
    private static string Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()! : throw new InvalidDataException("Invalid Python metadata text.");
}
