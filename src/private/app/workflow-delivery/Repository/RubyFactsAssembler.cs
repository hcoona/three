using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record RubyFactsEndpoint(GitRevision Revision, RubyRepositoryInputs Native,
    ProjectFacts[] Owners, SharedInput[] OperationInputs);
internal sealed record RubyFactAssembly(PlanRequest Request, SelectionReason[] PairedReasons);

// Preserve native producers across both endpoints; only actual quality owners become checks.
internal static class RubyFactsAssembler
{
    internal const string Scope = "ruby/native-paired-facts-v1";

    internal static RubyFactAssembly Assemble(RubyFactsEndpoint basis,
        RubyFactsEndpoint candidate, string[] changedPaths, bool full)
    {
        RepositoryFacts before = Validate(basis), after = Validate(candidate);
        if (changedPaths.Distinct(StringComparer.Ordinal).Count() != changedPaths.Length)
            throw new InvalidDataException("Duplicate Ruby changed coordinate.");
        var downstream = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var owners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (RubyFactsEndpoint endpoint in new[] { basis, candidate })
        {
            foreach (RubyProjectDependencies producer in endpoint.Native.Graph.Projects)
                foreach (string dependency in producer.Ordinary)
                    Add(downstream, dependency, producer.Directory);
            foreach (ProjectFacts owner in endpoint.Owners)
                Add(owners, owner.Directory, owner.Id);
        }
        var reasons = new HashSet<SelectionReason>();
        var unaffected = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in changedPaths)
        {
            ImpactPlanner.ValidatePath(path);
            bool present = false, affected = false;
            foreach (RubyFactsEndpoint endpoint in new[] { basis, candidate })
            {
                GitEntry? entry = endpoint.Revision.Entries
                    .SingleOrDefault(row => row.Path == path);
                if (entry is null) continue;
                present = true;
                RubyRepositoryInventory.RequireFile(entry);
                var producers = new HashSet<string>(StringComparer.Ordinal);
                var targets = new HashSet<string>(StringComparer.Ordinal);
                string? nearest = Nearest(endpoint, path);
                if (nearest is not null) producers.Add(nearest);
                SharedInput[] inputs = endpoint.Native.Inputs.Where(input => input.Path == path)
                    .ToArray();
                foreach (SharedInput input in inputs) producers.UnionWith(input.Consumers);
                SharedInput[] operations = endpoint.OperationInputs
                    .Where(input => input.Path == path)
                    .ToArray();
                foreach (SharedInput input in operations) targets.UnionWith(input.Consumers);
                foreach (ProjectFacts owner in endpoint.Owners.Where(owner =>
                             owner.OwnedPaths?.Contains(path, StringComparer.Ordinal) == true))
                    targets.Add(owner.Id);
                bool bundleInput = endpoint.Native.Graph.Bundles.Any(bundle =>
                    bundle.Inputs.Contains(path, StringComparer.Ordinal));
                if (nearest is null && inputs.Length == 0 && operations.Length == 0 &&
                    targets.Count == 0 && !bundleInput)
                    throw new InvalidDataException($"Unresolved Ruby input: {path}");
                var pending = new Queue<string>(producers);
                while (pending.TryDequeue(out string? producer))
                {
                    if (owners.TryGetValue(producer, out HashSet<string>? actual))
                        targets.UnionWith(actual);
                    if (downstream.TryGetValue(producer, out HashSet<string>? consumers))
                        foreach (string consumer in consumers)
                            if (producers.Add(consumer)) pending.Enqueue(consumer);
                }
                foreach (string owner in targets)
                    reasons.Add(new(path, endpoint.Revision.Commit, owner));
                affected |= targets.Count != 0;
            }
            if (!present)
                throw new InvalidDataException($"Ruby input absent at both endpoints: {path}");
            if (!affected) unaffected.Add(path);
        }
        return new(new(before with { UnaffectedPaths = Present(basis, unaffected) },
            after with { UnaffectedPaths = Present(candidate, unaffected) }, changedPaths, full),
            reasons.OrderBy(reason => reason.Path, StringComparer.Ordinal)
                .ThenBy(reason => reason.Revision, StringComparer.Ordinal)
                .ThenBy(reason => reason.Project, StringComparer.Ordinal).ToArray());
    }

    private static RepositoryFacts Validate(RubyFactsEndpoint endpoint)
    {
        ImpactPlanner.RequireText(endpoint.Revision.Commit, "Ruby revision");
        var entries = endpoint.Revision.Entries.ToDictionary(entry => entry.Path,
            StringComparer.Ordinal);
        RubyRepositoryInventory inventory = RubyRepositoryInventory.Discover(endpoint.Revision);
        string[] directories = endpoint.Native.Graph.Projects.Select(project => project.Directory)
            .ToArray();
        if (directories.Contains(".", StringComparer.Ordinal) ||
            directories.Distinct(StringComparer.Ordinal).Count() != directories.Length ||
            !directories.Order(StringComparer.Ordinal).SequenceEqual(inventory.Sources
                .Select(source => source.Directory).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Incomplete Ruby source producer graph.");
        var producers = directories.ToHashSet(StringComparer.Ordinal);
        foreach (RubyProjectDependencies project in endpoint.Native.Graph.Projects)
        {
            ImpactPlanner.RequireText(project.Name, "native Ruby gem name");
            if (project.Ordinary.Any(dependency => !producers.Contains(dependency)))
                throw new InvalidDataException("Unresolved Ruby producer relation.");
        }
        string[] bundles = endpoint.Native.Graph.Bundles.Select(bundle => bundle.Gemfile).ToArray();
        if (bundles.Distinct(StringComparer.Ordinal).Count() != bundles.Length ||
            !bundles.Order(StringComparer.Ordinal).SequenceEqual(inventory.Bundles
                .Select(bundle => bundle.Gemfile).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Incomplete Ruby bundle consumer graph.");
        foreach (RubyBundleConsumption bundle in endpoint.Native.Graph.Bundles)
        {
            if (bundle.Producers.Any(producer => !producers.Contains(producer)))
                throw new InvalidDataException("Unresolved Ruby bundle producer.");
            foreach (string path in bundle.Inputs) Require(path);
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var owned = new HashSet<string>(StringComparer.Ordinal);
        var quality = new List<ProjectFacts>();
        foreach (ProjectFacts owner in endpoint.Owners)
        {
            if (!ids.Add(owner.Id) || !owned.Add(owner.Directory) ||
                !producers.Contains(owner.Directory))
                throw new InvalidDataException("Unresolved actual Ruby quality owner.");
            ImpactPlanner.ValidateQualityOwner(owner);
            foreach (string path in owner.OwnedPaths ?? []) Require(path);
            string[] paths = endpoint.Revision.Entries.Select(entry => entry.Path)
                .Where(path => Nearest(endpoint, path) == owner.Directory)
                .Concat(owner.OwnedPaths ?? []).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            foreach (string path in paths) Require(path);
            quality.Add(owner with { OwnedPaths = paths });
        }
        foreach (SharedInput input in endpoint.Native.Inputs)
        {
            ImpactPlanner.ValidatePath(input.Path);
            if (entries.ContainsKey(input.Path)) Require(input.Path);
            if (input.Consumers.Any(producer => !producers.Contains(producer)))
                throw new InvalidDataException("Unresolved native Ruby input owner.");
        }
        foreach (SharedInput input in endpoint.OperationInputs)
        {
            ImpactPlanner.ValidatePath(input.Path);
            if (entries.ContainsKey(input.Path)) Require(input.Path);
            if (input.Consumers.Any(owner => !ids.Contains(owner)))
                throw new InvalidDataException("Unresolved Ruby operation input owner.");
        }
        var facts = new RepositoryFacts(endpoint.Revision.Commit, Scope,
            quality.ToArray(), [], [], []);
        // Validate complete available obligations, including unselected and basis-only owners.
        _ = ImpactPlanner.Plan(new(facts, facts, [], true));
        return facts;

        void Require(string path)
        {
            ImpactPlanner.ValidatePath(path);
            if (!entries.TryGetValue(path, out GitEntry? entry))
                throw new InvalidDataException("Missing committed Ruby input.");
            RubyRepositoryInventory.RequireFile(entry);
        }
    }

    private static string? Nearest(RubyFactsEndpoint endpoint, string path) =>
        endpoint.Native.Graph.Projects.Select(project => project.Directory)
            .Where(directory => path.StartsWith(directory + "/", StringComparison.Ordinal))
            .OrderByDescending(directory => directory.Length).FirstOrDefault();

    private static string[] Present(RubyFactsEndpoint endpoint, IEnumerable<string> paths) =>
        paths.Where(path => endpoint.Revision.Entries.Any(entry => entry.Path == path))
            .Order(StringComparer.Ordinal).ToArray();

    private static void Add(Dictionary<string, HashSet<string>> graph, string from, string to)
    {
        if (!graph.TryGetValue(from, out HashSet<string>? values))
            graph.Add(from, values = new(StringComparer.Ordinal));
        values.Add(to);
    }
}
