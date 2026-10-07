using System.Text.Json;

namespace WorkflowDelivery.Repository;

// These are native operation answers, not an additional package/group declaration.
internal sealed record PythonOperationGroups(string Directory, string[] Groups);
internal sealed record PythonBuildAssociation(string Consumer, string Producer,
    string[] Extras, string? Marker);
internal sealed record PythonProjectDependencies(string Directory, string Name,
    string[] Ordinary, string[] Build);

// Marker evaluation and operation group selection stay native. Workflow joins resolved
// coordinates, follows graph edges and computes a finite union for impact selection.
internal static class PythonDependencyProjection
{
    internal static PythonBuildAssociation[] ReadBuildAssociations(PythonMetadata metadata,
        PythonSupplementResult supplement, PythonPassiveProject[] projects)
    {
        try
        {
            JsonElement facts = supplement.Facts;
            if (facts.GetProperty("workspace_root").GetString() != metadata.Root)
                throw new InvalidDataException(
                    "Python native supplement has a different endpoint.");
            var members = metadata.Members.ToDictionary(member => member.Directory,
                StringComparer.Ordinal);
            var passive = new Dictionary<string, PythonPassiveProject>(StringComparer.Ordinal);
            foreach (PythonPassiveProject project in projects)
                if (!passive.TryAdd(PythonMetadataReader.Relative(metadata.Root, project.Directory),
                    project))
                    throw new InvalidDataException("Ambiguous Python passive context.");
            if (!passive.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(members.Keys.Append(".")))
                throw new InvalidDataException("Python passive contexts omit native ownership.");
            var observed = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement member in facts.GetProperty("members").EnumerateArray())
            {
                string directory = PythonMetadataReader.Relative(metadata.Root,
                    Text(member.GetProperty("directory")));
                if (!observed.Add(directory) || !members.TryGetValue(directory,
                    out PythonMetadataMember? native) ||
                    Text(member.GetProperty("name")) != native.Name)
                    throw new InvalidDataException("Python native membership results disagree.");
            }
            if (!observed.SetEquals(members.Keys))
                throw new InvalidDataException("Python native supplement omits a member.");
            var associations = new List<PythonBuildAssociation>();
            var contexts = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement build in facts.GetProperty("build_requirements").EnumerateArray())
            {
                string consumer = PythonMetadataReader.Relative(metadata.Root,
                    Text(build.GetProperty("directory")));
                if (!contexts.Add(consumer) || !members.ContainsKey(consumer) ||
                    passive[consumer].BuildRequirements is not { } strings)
                    throw new InvalidDataException("Unresolved Python native build context.");
                JsonElement[] requirements = build.GetProperty("requirements")
                    .EnumerateArray().ToArray();
                if (requirements.Length != strings.Length)
                    throw new InvalidDataException(
                        "Python native build requirements are incomplete.");
                foreach (JsonElement requirement in requirements)
                {
                    _ = Text(requirement.GetProperty("name"));
                    if (requirement.EnumerateObject().Any(field => field.Name is not
                        ("name" or "extras" or "marker" or "specifier" or "index" or "conflict" or
                            "editable" or "directory")) ||
                        (requirement.TryGetProperty("index", out JsonElement index) &&
                            index.ValueKind != JsonValueKind.Null) ||
                        (requirement.TryGetProperty("conflict", out JsonElement conflict) &&
                            conflict.ValueKind != JsonValueKind.Null))
                        throw new InvalidDataException("Unsupported Python native build source.");
                    bool editable = requirement.TryGetProperty("editable", out JsonElement source);
                    bool directory = requirement.TryGetProperty("directory", out JsonElement other);
                    if (editable && directory)
                        throw new InvalidDataException("Ambiguous Python native build source.");
                    if (!editable && !directory) continue;
                    string producer = PythonMetadataReader.Relative(metadata.Root,
                        Text(editable ? source : other));
                    if (!members.TryGetValue(producer, out PythonMetadataMember? native) ||
                        Text(requirement.GetProperty("name")) != native.Name)
                        throw new InvalidDataException(
                            "Python native build source has no member owner.");
                    string[] extras = requirement.TryGetProperty("extras", out JsonElement selected)
                        ? selected.EnumerateArray().Select(Text).ToArray() : [];
                    string? marker = requirement.TryGetProperty("marker",
                        out JsonElement expression)
                        ? Text(expression) : null;
                    associations.Add(new(consumer, producer, extras, marker));
                }
            }
            if (!contexts.SetEquals(passive.Where(pair => pair.Value.BuildRequirements is not null)
                .Select(pair => pair.Key)))
                throw new InvalidDataException(
                    "Python native build output omits a passive context.");
            return associations.ToArray();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException(
                "Invalid Python native build association response.", exception);
        }
    }

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!
            : throw new InvalidDataException("Invalid Python native association coordinate.");

    internal static PythonProjectDependencies[] Project(PythonMetadata metadata,
        PythonOperationGroups[] operations, PythonBuildAssociation[] builds,
        Func<string, string[], bool> active, bool supportsPython)
    {
        if (!supportsPython)
            throw new InvalidDataException(
                "Python operation interpreter is outside native constraints.");
        var members = metadata.Members.ToDictionary(member => member.Directory,
            StringComparer.Ordinal);
        var local = members.Values.ToDictionary(member =>
            metadata.Nodes[member.Id].Source!.LocalPath!, member => member.Directory,
            StringComparer.Ordinal);
        var contexts = members.Keys.ToHashSet(StringComparer.Ordinal);
        contexts.Add(".");
        foreach (PythonOperationGroups operation in operations)
            if (!contexts.Contains(operation.Directory) ||
                operation.Groups.Distinct(StringComparer.Ordinal).Count() !=
                    operation.Groups.Length)
                throw new InvalidDataException("Unresolved Python native group operation.");
        if (!contexts.SetEquals(operations.Select(operation => operation.Directory)))
            throw new InvalidDataException("Python group operations omit a native context.");
        foreach (PythonBuildAssociation association in builds)
            if (!members.ContainsKey(association.Consumer) ||
                !members.ContainsKey(association.Producer) ||
                association.Extras.Distinct(StringComparer.Ordinal).Count() !=
                    association.Extras.Length)
                throw new InvalidDataException("Unresolved Python native build association.");

        var results = new List<PythonProjectDependencies>();
        foreach (string directory in contexts.Order(StringComparer.Ordinal))
        {
            var ordinary = new HashSet<string>(StringComparer.Ordinal);
            var build = new HashSet<string>(StringComparer.Ordinal);
            string entry = members.TryGetValue(directory, out PythonMetadataMember? member)
                ? member.Id : metadata.WorkspaceId;
            Walk([entry], ordinary);
            foreach (PythonOperationGroups operation in operations.Where(operation =>
                operation.Directory == directory))
            {
                foreach (string group in operation.Groups)
                {
                    PythonMetadataEntry[] groups = metadata.Nodes[entry].Groups
                        .Concat(directory == "." ? metadata.Nodes[metadata.WorkspaceId].Groups : [])
                        .Where(item => item.Name == group).DistinctBy(item => item.Id).ToArray();
                    if (groups.Length != 1)
                        throw new InvalidDataException(
                            "Python native group has no unique graph entry.");
                    Walk([groups[0].Id], ordinary);
                }
            }
            // Ordinary local inputs may themselves require local build plugins. Build legs
            // always follow ordinary/explicit extra entries, never development groups.
            var pending = new Queue<string>(ordinary.Append(directory));
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (pending.TryDequeue(out string? consumer))
            {
                if (!visited.Add(consumer)) continue;
                foreach (PythonBuildAssociation association in builds.Where(association =>
                    association.Consumer == consumer))
                {
                    if (association.Marker is not null && !active(association.Marker, [])) continue;
                    PythonMetadataMember producer = members[association.Producer];
                    var entries = new List<string> { producer.Id };
                    foreach (string extra in association.Extras)
                    {
                        PythonMetadataEntry[] extras = metadata.Nodes[producer.Id].Extras
                            .Where(item => item.Name == extra).ToArray();
                        if (extras.Length != 1)
                            throw new InvalidDataException(
                                "Python build extra has no unique graph entry.");
                        entries.Add(extras[0].Id);
                    }
                    var reached = new HashSet<string>(StringComparer.Ordinal);
                    Walk(entries, reached);
                    reached.Add(association.Producer);
                    foreach (string dependency in reached)
                    {
                        build.Add(dependency);
                        pending.Enqueue(dependency);
                    }
                }
            }
            ordinary.Remove(directory);
            build.Remove(directory);
            results.Add(new(directory, member?.Name ?? "workspace",
                ordinary.Order(StringComparer.Ordinal).ToArray(),
                build.Order(StringComparer.Ordinal).ToArray()));
        }
        return results.ToArray();

        void Walk(IEnumerable<string> entryIds, HashSet<string> dependencies)
        {
            var pending = new Queue<string>(entryIds);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (pending.TryDequeue(out string? id))
            {
                if (!visited.Add(id)) continue;
                if (!metadata.Nodes.TryGetValue(id, out PythonMetadataNode? node))
                    throw new InvalidDataException("Python native entry is unresolved.");
                if (node.Source?.LocalPath is { } path)
                {
                    if (!local.TryGetValue(path, out string? owner))
                        throw new InvalidDataException(
                            "Python local source has no native member owner.");
                    dependencies.Add(owner);
                }
                string[] extras = node.Kind == "extra" ? [node.Dimension!] : [];
                foreach (PythonMetadataEdge edge in node.Dependencies)
                    if (edge.Marker is null || active(edge.Marker, extras))
                        pending.Enqueue(edge.Id);
            }
        }
    }
}
