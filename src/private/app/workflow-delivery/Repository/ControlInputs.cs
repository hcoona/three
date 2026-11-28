using WorkflowDelivery.CI;
using static WorkflowDelivery.Repository.DotNetRepositoryReader;

namespace WorkflowDelivery.Repository;

internal sealed record ControlSourceInput(string Path, string Role, bool Present);
internal sealed record ControlInputFacts(string Revision, string Project,
    ControlSourceInput[] Sources);

// Bind maintained native evaluation and explicit control consumers, without interpreting build
// tasks.
internal static class ControlInputs
{
    private const string Root = "src/private/app/workflow-delivery";
    private static readonly StringComparer PhysicalPaths = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // The control project's DefaultItemExcludes remove its docs subtree and README from the
    // native item region; they are records, not control-run inputs.
    internal static bool InertDocument(string path) => path == Root + "/README.md" ||
        path.StartsWith(Root + "/docs/", StringComparison.Ordinal);

    internal static HashSet<(string Project, string Globals)> Reachable(string root,
        IEnumerable<DotNetGraphNode> nodes, DotNetGraphEdge[] edges, bool required)
    {
        var comparer = new NativeIdentityComparer();
        var closure = new HashSet<(string Project, string Globals)>(comparer);
        string application = Path.GetFullPath(NodeGroupReader.ControlProject, root);
        DotNetGraphNode[] control = nodes.Where(node => !node.OuterBuild &&
            PhysicalPaths.Equals(node.Identity.Project, application)).ToArray();
        if (control.Length == 0 && !required) return closure;
        if (control.Length != 1)
            throw new InvalidDataException("The control requires one complete native dimension.");
        var references = edges.ToLookup(edge => Key(edge.Consumer), edge => Key(edge.Dependency),
            comparer);
        var cli = Key(control[0].Identity);
        closure.Add(cli);
        var pending = new Queue<(string Project, string Globals)>();
        pending.Enqueue(cli);
        while (pending.TryDequeue(out var project))
            foreach (var dependency in references[project])
            {
                string path = Path.GetRelativePath(root, dependency.Project).Replace('\\', '/');
                if (!path.StartsWith(Root + "/", StringComparison.Ordinal) ||
                    !path.EndsWith(".csproj", StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "Control project dependencies require a concrete adapter.");
                if (closure.Add(dependency)) pending.Enqueue(dependency);
            }
        return closure;
    }

    internal static ControlInputFacts? Read(DotNetRevisionInputs inputs, bool required)
    {
        var closure = Reachable(inputs.Graph.Root, inputs.Nodes.Select(node => node.Native),
            inputs.Graph.Edges, required);
        if (closure.Count == 0) return null;
        DotNetBoundNode[] consumed = inputs.Nodes.Where(node =>
            closure.Contains(Key(node.Native.Identity))).ToArray();
        var projects = consumed.Select(node => node.Project).ToHashSet(StringComparer.Ordinal);
        var paths = consumed
            .SelectMany(node => node.OwnedPaths.Select(path =>
                new ControlSourceInput(path, "ControlSource", true))).ToList();
        foreach (DotNetBoundInput input in inputs.Inputs.Where(input => input.Path is not null &&
                     closure.Contains(Key(input.Native.Consumer))))
            paths.Add(new(input.Path!, input.Native.Role,
                inputs.Revision.Entries.Any(entry => entry.Path == input.Path)));
        // Native NBGV distribution is a tool exception. Its configuration is consumed;
        // the control application's unused self-version does not add product source consumers.
        var versionInputs = inputs.Versions.SelectMany(version =>
            version.Paths.Concat(version.ConfigurationCandidates))
            .ToHashSet(StringComparer.Ordinal);
        var versionBases = consumed.Select(node => node.Native.VersionBaseDirectory.Length > 0
            ? node.Native.VersionBaseDirectory
            : Path.GetDirectoryName(node.Native.Identity.Project)!)
            .ToHashSet(PhysicalPaths);
        var versionConfiguration = inputs.Versions.Where(version =>
                versionBases.Contains(Path.GetFullPath(version.Directory, inputs.Graph.Root)))
            .SelectMany(version => version.ConfigurationCandidates)
            .ToHashSet(StringComparer.Ordinal);
        foreach (SharedInput input in inputs.SelectionInputs.Where(input =>
                     input.Consumers.Any(projects.Contains) &&
                     (!versionInputs.Contains(input.Path) || versionConfiguration.Contains(
                         input.Path))))
            paths.Add(new(input.Path, "ControlConfiguration",
                inputs.Revision.Entries.Any(entry => entry.Path == input.Path)));
        return new(inputs.Revision.Commit, NodeGroupReader.ControlProject,
            paths.Distinct().OrderBy(input => input.Path, StringComparer.Ordinal)
                .ThenBy(input => input.Role, StringComparer.Ordinal).ToArray());
    }
}
