using System.Text.Json;
using WorkflowDelivery.Platform;
using static WorkflowDelivery.Platform.StrictJson;

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
            ["workspace", "metadata", "--frozen", "--python", interpreter], 30, environment,
            InheritEnvironment: false), token);
        if (!result.Succeeded || result.Error is not null)
            throw new InvalidDataException("Python metadata extraction failed: " +
                NativeDiagnostic.Describe(result));
        return Parse(result.Stdout, root);
    }

    internal static PythonMetadata Parse(string text, string root)
    {
        Absolute(root);
        try
        {
            using JsonDocument document = StrictJson.Parse(text);
            JsonElement value = document.RootElement;
            if (Text(value.GetProperty("schema").GetProperty("version")) != "preview" ||
                NativePath(Text(value.GetProperty("workspace_root"))) != root ||
                value.TryGetProperty("environment", out _) ||
                value.TryGetProperty("script", out _) ||
                value.GetProperty("conflicts").GetProperty("sets").EnumerateArray().Any())
                throw new InvalidDataException("Unsupported Python metadata request context.");
            JsonElement workspace = value.GetProperty("workspace");
            if (NativePath(Text(workspace.GetProperty("path"))) != root)
                throw new InvalidDataException("Python metadata workspace identity differs.");
            string workspaceId = Text(workspace.GetProperty("id"));
            string requiresPython = Text(value.GetProperty("requires_python"));
            var nodes = new Dictionary<string, PythonMetadataNode>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.GetProperty("resolution").EnumerateObject())
            {
                string id = property.Name;
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidDataException("Empty Python metadata node identity.");
                JsonElement node = property.Value;
                JsonElement kind = node.GetProperty("kind");
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
                    if (id != workspaceId || NativePath(Text(node.GetProperty("path"))) != root)
                        throw new InvalidDataException("Python metadata workspace node differs.");
                }
                else if (label == "group" && node.TryGetProperty("path", out JsonElement path))
                {
                    if (NativePath(Text(path)) != root)
                        throw new InvalidDataException("Python workspace group identity differs.");
                }
                else
                {
                    name = Text(node.GetProperty("name"));
                    source = Source(node.GetProperty("source"));
                }
                PythonMetadataEdge[] edges = node.GetProperty("dependencies").EnumerateArray()
                    .Select(edge => new PythonMetadataEdge(Text(edge.GetProperty("id")),
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
            var members = new List<PythonMetadataMember>();
            foreach (JsonElement member in value.GetProperty("members").EnumerateArray())
            {
                string name = Text(member.GetProperty("name"));
                string path = NativePath(Text(member.GetProperty("path")));
                string directory = Relative(root, path);
                string id = Text(member.GetProperty("id"));
                if (!directories.Add(directory) || !names.Add(name) || !ids.Add(id) ||
                    !nodes.TryGetValue(id, out PythonMetadataNode? node) ||
                    node.Kind != "package" || node.Name != name ||
                    node.Source?.Kind is not ("directory" or "editable" or "virtual") ||
                    node.Source.LocalPath != path)
                    throw new InvalidDataException("Ambiguous Python metadata member identity.");
                members.Add(new(name, directory, id));
            }
            return new(root, workspaceId, requiresPython, [.. members], nodes);

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
        PythonMetadataEntry[] entries = value.EnumerateArray().Select(entry =>
            new PythonMetadataEntry(Text(entry.GetProperty("name")), Text(entry.GetProperty("id"))))
            .ToArray();
        if (entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() !=
            entries.Length || entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal)
                .Count() != entries.Length)
            throw new InvalidDataException("Duplicate Python metadata dimension.");
        return entries;
    }

    internal static string Relative(string root, string path) =>
        OwnedPath.RelativeWithin(root, NativePath(path),
            "Python coordinate is outside its endpoint.");

    // UV's PortablePathBuf serializes forward slashes on Windows. Adapt that
    // supported path format without touching the opaque graph IDs.
    private static string NativePath(string path)
    {
        string native = OperatingSystem.IsWindows() ? path.Replace('/', '\\') : path;
        Absolute(native);
        return native;
    }

    private static void Absolute(string path) => OwnedPath.RequireCanonical(path,
        "Python coordinates require canonical absolute paths.");
}
