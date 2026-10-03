using System.Text.Json;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

// Native workspace relations are one input to repository analysis, not complete CI facts.
internal sealed record PnpmProject(string Directory, string? PublishDirectory,
    string[] Dependencies);
internal sealed record PnpmLocalInput(string Path, bool IsDirectory, string[] Consumers);
internal sealed record PnpmGraph(PnpmProject[] Projects, PnpmLocalInput[] LocalInputs);

internal sealed class PnpmGraphReader
{
    private static readonly string[] DependencyGroups =
        ["dependencies", "devDependencies", "optionalDependencies", "unsavedDependencies"];
    private readonly string root;
    private readonly Func<string[], CancellationToken, Task<string>> query;

    internal PnpmGraphReader(string root, string executable = "pnpm")
        : this(root, (arguments, token) => NativeProcess.RunAsync(executable,
            Path.GetFullPath(root), arguments, token))
    { }

    internal PnpmGraphReader(string root,
        Func<string[], CancellationToken, Task<string>> query)
    {
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        this.query = query;
    }

    internal async Task<PnpmGraph> ReadAsync(CancellationToken cancellationToken)
    {
        // Read only the layout settings needed by this adapter, never the full config/auth view.
        using JsonDocument shared = await ReadJsonAsync(
            ["config", "get", "shared-workspace-lockfile", "--json"], cancellationToken);
        if (shared.RootElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.True))
            throw new InvalidDataException("PNPM analysis requires a shared workspace lockfile.");
        using JsonDocument lockDirectory = await ReadJsonAsync(
            ["config", "get", "lockfile-dir", "--json"], cancellationToken);
        if (lockDirectory.RootElement.ValueKind != JsonValueKind.Null &&
            Absolute(RequiredString(lockDirectory.RootElement), root) != root)
            throw new InvalidDataException(
                "PNPM analysis requires the lockfile at its workspace root.");

        using JsonDocument membership = await ReadJsonAsync(ListArguments("-1"), cancellationToken);
        Dictionary<string, string> roots = ReadProjects(membership.RootElement);
        if (!roots.ContainsKey(root))
            throw new InvalidDataException("PNPM did not report the requested workspace root.");
        var directories = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var outputs = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach ((string directory, string identity) in roots)
        {
            AddOwner(directory, identity);
            string metadata = await query(["--dir", directory, "pkg", "get",
                "publishConfig.directory", "--json"], cancellationToken);
            string? output = null;
            if (!string.IsNullOrWhiteSpace(metadata))
            {
                using JsonDocument value = JsonDocument.Parse(metadata);
                if (value.RootElement.ValueKind != JsonValueKind.Null)
                {
                    string path = Absolute(RequiredString(value.RootElement), directory);
                    output = Relative(path);
                    AddOwner(path, identity);
                }
            }
            outputs.Add(identity, output);
        }

        var projects = new List<PnpmProject>();
        var inputs = new Dictionary<(string Path, bool Directory), HashSet<string>>();
        foreach ((string directory, string identity) in roots)
        {
            // This is a native exact-directory query; legacy subtree filtering cannot broaden it.
            if (identity.IndexOfAny(['{', '}', '[', ']', '*', '?', '!', '\\']) >= 0)
                throw new InvalidDataException(
                    "PNPM directory selector cannot represent this project.");
            string selector = "{./" + identity + "}";
            using JsonDocument closure = await ReadJsonAsync(
                ["--config.legacy-dir-filtering=false", "--filter", selector + "...",
                    .. ListArguments("-1")], cancellationToken);
            Dictionary<string, string> selected = ReadProjects(closure.RootElement);
            if (!selected.ContainsKey(directory) ||
                selected.Keys.Any(path => !roots.ContainsKey(path)))
                throw new InvalidDataException(
                    "PNPM dependency closure has an unresolved project identity.");
            var related = selected.Values.ToHashSet(StringComparer.Ordinal);
            // PNPM shares its rendering cache across selected projects. Query one consumer
            // at a time so elided subtrees cannot hide another consumer's local inputs.
            using JsonDocument full = await ReadJsonAsync(
                ["--config.legacy-dir-filtering=false", "--filter", selector,
                    .. ListArguments("Infinity")], cancellationToken);
            Dictionary<string, JsonElement> trees = ReadTrees(full.RootElement);
            if (trees.Count != 1 || !trees.TryGetValue(directory, out JsonElement tree))
                throw new InvalidDataException(
                    "PNPM full query must return exactly its requested project.");
            foreach (JsonElement node in WalkDependencies(tree))
            {
                string nativePath = Absolute(RequiredString(Property(node, "path")), root);
                var owners = new HashSet<string>(StringComparer.Ordinal);
                if (directories.TryGetValue(nativePath, out HashSet<string>? pathOwners))
                    owners.UnionWith(pathOwners);

                string version = RequiredString(Property(node, "version"));
                if (!node.TryGetProperty("resolved", out JsonElement resolved))
                {
                    // Decode a resolved native directory coordinate, never a manifest specifier.
                    if (version.StartsWith("file:", StringComparison.Ordinal))
                    {
                        string source = Absolute(version[5..], root);
                        if (directories.TryGetValue(source, out HashSet<string>? sourceOwners))
                            owners.UnionWith(sourceOwners);
                        else
                            AddInput(Relative(source), true, identity);
                    }
                    else if (version.StartsWith("link:", StringComparison.Ordinal) &&
                        owners.Count == 0)
                        AddInput(Relative(nativePath), true, identity);
                }
                else if (RequiredString(resolved).StartsWith("file:", StringComparison.Ordinal))
                {
                    // A native tarball resolution is an archive input, including when names match.
                    AddInput(Relative(Absolute(RequiredString(resolved)[5..], root)),
                        false, identity);
                }
                if (owners.Count > 1)
                    throw new InvalidDataException(
                        "PNPM dependency has ambiguous source ownership.");
                related.UnionWith(owners);
            }
            related.Remove(identity);
            projects.Add(new(identity, outputs[identity],
                related.Order(StringComparer.Ordinal).ToArray()));
        }
        return new(projects.OrderBy(p => p.Directory, StringComparer.Ordinal).ToArray(),
            inputs.OrderBy(p => p.Key.Path, StringComparer.Ordinal)
                .ThenBy(p => p.Key.Directory).Select(p => new PnpmLocalInput(p.Key.Path,
                    p.Key.Directory, p.Value.Order(StringComparer.Ordinal).ToArray())).ToArray());

        void AddOwner(string path, string identity)
        {
            if (!directories.TryGetValue(path, out HashSet<string>? owners))
                directories.Add(path, owners = new(StringComparer.Ordinal));
            owners.Add(identity);
        }

        void AddInput(string path, bool directory, string consumer)
        {
            if (!inputs.TryGetValue((path, directory), out HashSet<string>? consumers))
                inputs.Add((path, directory), consumers = new(StringComparer.Ordinal));
            consumers.Add(consumer);
        }
    }

    private async Task<JsonDocument> ReadJsonAsync(string[] arguments, CancellationToken token) =>
        JsonDocument.Parse(await query(arguments, token));

    private static string[] ListArguments(string depth) =>
        ["list", "--recursive", "--include-workspace-root", "--lockfile-only",
            "--json", "--depth", depth];

    private Dictionary<string, string> ReadProjects(JsonElement value) =>
        ReadTrees(value).Keys.ToDictionary(path => path, Relative, StringComparer.Ordinal);

    private Dictionary<string, JsonElement> ReadTrees(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("PNPM must return a project array.");
        var projects = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement project in value.EnumerateArray())
        {
            string path = Absolute(RequiredString(Property(project, "path")), root);
            _ = Relative(path);
            if (!projects.TryAdd(path, project))
                throw new InvalidDataException("PNPM returned a duplicate project directory.");
        }
        return projects;
    }

    private static IEnumerable<JsonElement> WalkDependencies(JsonElement tree)
    {
        if (tree.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("PNPM dependency must be an object.");
        foreach (string group in DependencyGroups)
        {
            if (!tree.TryGetProperty(group, out JsonElement dependencies))
                continue;
            if (dependencies.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("PNPM dependency group must be an object.");
            foreach (JsonProperty dependency in dependencies.EnumerateObject())
            {
                yield return dependency.Value;
                foreach (JsonElement node in WalkDependencies(dependency.Value))
                    yield return node;
            }
        }
    }

    private string Relative(string path)
    {
        string relative = Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith("../", StringComparison.Ordinal))
            throw new InvalidDataException(
                "PNPM reported an input outside the supported repository root.");
        return relative;
    }

    private static string Absolute(string path, string basis) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path, basis));

    private static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty(name, out JsonElement property)
            ? property : throw new InvalidDataException(
                "PNPM omitted a required native identity field.");

    private static string RequiredString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException("PNPM returned an invalid native identity or path.");
}
