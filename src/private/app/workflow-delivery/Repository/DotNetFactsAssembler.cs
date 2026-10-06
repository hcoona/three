using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record DotNetSelection(PlanRequest Request,
    IReadOnlyDictionary<string, HashSet<SelectionReason>> Reasons,
    IReadOnlyDictionary<string, DotNetGraphNode[]> CandidateNodes);

// Aggregate native variants only for selection. Later completion still receives
// every candidate native identity; empty checks here are not execution obligations.
internal static class DotNetFactsAssembler
{
    internal const string Scope = "dotnet/native-endpoint-selection-v1";

    internal static DotNetSelection Select(DotNetRevisionInputs basis,
        DotNetRevisionInputs candidate, string[] changedPaths, bool full,
        string[] basisUnaffected, string[] candidateUnaffected)
    {
        var request = new PlanRequest(Assemble(basis, basisUnaffected),
            Assemble(candidate, candidateUnaffected), changedPaths, full);
        Dictionary<string, HashSet<SelectionReason>> selected =
            ImpactPlanner.SelectProjects(request);
        return new(request, selected, candidate.Nodes.Where(node => node.Project != "dirs.proj")
            .GroupBy(node => node.Project, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(node => node.Native).ToArray(),
                StringComparer.Ordinal));
    }

    internal static RepositoryFacts Assemble(DotNetRevisionInputs inputs,
        string[] unaffectedPaths)
    {
        var nodes = inputs.Nodes.ToDictionary(node => DotNetRepositoryReader.Key(
            node.Native.Identity), new DotNetRepositoryReader.NativeIdentityComparer());
        var business = inputs.Nodes.Where(node => node.Project != "dirs.proj")
            .GroupBy(node => node.Project, StringComparer.Ordinal).ToArray();
        var dependencies = business.ToDictionary(group => group.Key,
            _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var references = new Dictionary<(string Project, string Globals),
            HashSet<(string Project, string Globals)>>(
                new DotNetRepositoryReader.NativeIdentityComparer());
        foreach (DotNetGraphEdge edge in inputs.Graph.Edges)
        {
            var consumer = DotNetRepositoryReader.Key(edge.Consumer);
            var dependency = DotNetRepositoryReader.Key(edge.Dependency);
            DotNetBoundNode from = nodes[consumer];
            DotNetBoundNode to = nodes[dependency];
            if (!references.TryGetValue(consumer, out var targets))
                references.Add(consumer, targets = new(
                    new DotNetRepositoryReader.NativeIdentityComparer()));
            targets.Add(dependency);
            if (from.Project != "dirs.proj" && to.Project == "dirs.proj")
                throw new InvalidDataException("Unsupported business reference to discovery node.");
            if (from.Project != "dirs.proj" && from.Project != to.Project)
                dependencies[from.Project].Add(to.Project);
        }
        var shared = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (DotNetBoundInput input in inputs.Inputs.Where(input => input.Path is not null))
        {
            var consumer = DotNetRepositoryReader.Key(input.Native.Consumer);
            string project = nodes[consumer].Project;
            Add(input.Path!, project == "dirs.proj" ? Reachable(consumer) : [project]);
        }
        foreach (SharedInput input in inputs.SelectionInputs) Add(input.Path, input.Consumers);
        ProjectFacts[] projects = business.Select(group =>
        {
            QualitySelection? quality = inputs.Quality[group.Key];
            return new ProjectFacts(group.Key, DotNetRepositoryReader.Parent(group.Key),
                dependencies[group.Key].Order(StringComparer.Ordinal).ToArray(), [], null,
                quality?.Preset, [], group.SelectMany(node => node.OwnedPaths)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                quality is null ? CheckOrigin.NativeRetained : CheckOrigin.Preset);
        }).OrderBy(project => project.Id, StringComparer.Ordinal).ToArray();
        return new(inputs.Revision.Commit, Scope, projects, shared.OrderBy(pair => pair.Key,
            StringComparer.Ordinal).Select(pair => new SharedInput(pair.Key,
                pair.Value.Order(StringComparer.Ordinal).ToArray())).ToArray(),
            unaffectedPaths, []);

        string[] Reachable((string Project, string Globals) root)
        {
            var visited = new HashSet<(string Project, string Globals)>(
                new DotNetRepositoryReader.NativeIdentityComparer());
            var pending = new Queue<(string Project, string Globals)>();
            var consumers = new HashSet<string>(StringComparer.Ordinal);
            pending.Enqueue(root);
            while (pending.TryDequeue(out var identity))
            {
                if (!visited.Add(identity)) continue;
                string project = nodes[identity].Project;
                if (project != "dirs.proj") consumers.Add(project);
                if (references.TryGetValue(identity, out var targets))
                    foreach (var dependency in targets) pending.Enqueue(dependency);
            }
            return consumers.Order(StringComparer.Ordinal).ToArray();
        }

        void Add(string path, string[] consumers)
        {
            if (consumers.Length == 0) return;
            if (!shared.TryGetValue(path, out HashSet<string>? values))
                shared.Add(path, values = new(StringComparer.Ordinal));
            values.UnionWith(consumers);
        }
    }
}
