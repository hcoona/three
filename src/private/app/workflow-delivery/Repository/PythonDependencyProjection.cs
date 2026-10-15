namespace WorkflowDelivery.Repository;

// Group selections follow UV's documented defaults, not an additional declaration.
internal sealed record PythonGroupOperation(string Id, string Directory,
    string[] Packages, bool NoDev);
internal sealed record PythonOperationGroups(string Directory, string[] Groups);
internal sealed record PythonBuildAssociation(string Consumer, string Producer, string[] Extras);
internal sealed record PythonProjectDependencies(string Directory, string Name,
    string[] Ordinary, string[] Build);

// Native metadata owns the resolved graph. Workflow treats every marker as active, so the
// projection is the finite union over platforms and interpreters, and joins guarded build
// requirement names to native workspace members.
internal static class PythonDependencyProjection
{
    private const string DefaultGroup = "dev";

    internal static PythonBuildAssociation[] ReadBuildAssociations(PythonMetadata metadata,
        PythonPassiveProject[] projects)
    {
        var members = metadata.Members.ToDictionary(member => member.Directory,
            StringComparer.Ordinal);
        var names = metadata.Members.ToDictionary(member => member.Name, StringComparer.Ordinal);
        var passive = new Dictionary<string, PythonPassiveProject>(StringComparer.Ordinal);
        foreach (PythonPassiveProject project in projects)
            if (!passive.TryAdd(PythonMetadataReader.Relative(metadata.Root, project.Directory),
                project))
                throw new InvalidDataException("Ambiguous Python passive context.");
        if (!passive.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(members.Keys.Append(".")))
            throw new InvalidDataException("Python passive contexts omit native ownership.");
        IReadOnlyDictionary<string, string> workspace = passive["."].Sources ??
            new Dictionary<string, string>();
        var associations = new List<PythonBuildAssociation>();
        foreach ((string consumer, PythonPassiveProject project) in passive.OrderBy(pair =>
            pair.Key, StringComparer.Ordinal))
        {
            if (project.BuildRequirements is not { } requirements) continue;
            if (!members.TryGetValue(consumer, out PythonMetadataMember? owner))
                throw new InvalidDataException("Unresolved Python build context.");
            foreach (PythonBuildRequirement requirement in requirements)
            {
                // Admit the same finite source shapes as UV build lowering: no source, or an
                // exact workspace reference in the project or workspace root table. A
                // project entry takes precedence, so a member producer needs one there or
                // in the root.
                string? local = project.Sources?.GetValueOrDefault(requirement.Name);
                workspace.TryGetValue(requirement.Name, out string? shared);
                if (local is "unsupported" || shared is "unsupported")
                    throw new InvalidDataException("Unsupported Python build requirement source.");
                // UV exempts a self-reference from workspace lowering; it adds no input.
                if (requirement.Name == owner.Name) continue;
                bool member = names.TryGetValue(requirement.Name,
                    out PythonMetadataMember? producer);
                bool workspaceSource = (local ?? shared) == "workspace";
                if (member != workspaceSource)
                    throw new InvalidDataException(
                        "Python build requirement has no unique workspace owner.");
                if (!member) continue;
                if (requirement.Extras.Distinct(StringComparer.Ordinal).Count() !=
                    requirement.Extras.Length)
                    throw new InvalidDataException("Duplicate Python build requirement extra.");
                associations.Add(new(consumer, producer!.Directory, requirement.Extras));
            }
        }
        return associations.ToArray();
    }

    // UV selects the default dev group unless no-dev is requested. Settings that change that
    // default are rejected by the passive guard. An undeclared or empty group has no node.
    internal static PythonOperationGroups[] SelectGroups(PythonMetadata metadata,
        PythonGroupOperation[] operations)
    {
        var contexts = metadata.Members.Select(member => member.Directory)
            .Append(".").ToHashSet(StringComparer.Ordinal);
        string[] directories = operations.Select(operation =>
            PythonMetadataReader.Relative(metadata.Root, operation.Directory)).ToArray();
        if (operations.Select(operation => operation.Id).Distinct(StringComparer.Ordinal)
            .Count() != operations.Length || !contexts.SetEquals(directories))
            throw new InvalidDataException("Python group operations omit native contexts.");
        var groups = new List<PythonOperationGroups>();
        for (int index = 0; index < operations.Length; index++)
        {
            PythonGroupOperation operation = operations[index];
            // Directory/base contexts remain even when a singleton package owns the groups.
            groups.Add(new(directories[index], []));
            string owner = operation.Packages switch
            {
                [] => directories[index],
                [var package] => metadata.Members.SingleOrDefault(member =>
                    member.Name == package)?.Directory ?? throw new InvalidDataException(
                        "Python group operation has no unique native owner."),
                _ => throw new InvalidDataException(
                    "Unsupported Python group operation package scope."),
            };
            string entry = metadata.Members.SingleOrDefault(member =>
                member.Directory == owner)?.Id ?? metadata.WorkspaceId;
            bool declared = metadata.Nodes[entry].Groups.Concat(owner == "." ?
                metadata.Nodes[metadata.WorkspaceId].Groups : []).Any(group =>
                    group.Name == DefaultGroup);
            groups.Add(new(owner, !operation.NoDev && declared ? [DefaultGroup] : []));
        }
        return groups.ToArray();
    }

    internal static PythonProjectDependencies[] Project(PythonMetadata metadata,
        PythonOperationGroups[] operations, PythonBuildAssociation[] builds)
    {
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
                foreach (PythonMetadataEdge edge in node.Dependencies)
                    pending.Enqueue(edge.Id);
            }
        }
    }
}
