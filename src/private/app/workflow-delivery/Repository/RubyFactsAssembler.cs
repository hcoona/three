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
        var before = Validate(basis);
        var after = Validate(candidate);
        var endpoints = new[] { (Endpoint: basis, Index: before),
            (Endpoint: candidate, Index: after) };
        // Only the candidate must claim each bundle, so a basis bundle can still be removed.
        if (candidate.Native.Graph.Bundles.Any(bundle =>
                !after.NativeDirectories.Contains(bundle.Directory, StringComparer.Ordinal) &&
                !candidate.Owners.Any(owner => Governs(owner.Directory, bundle.Directory,
                    after.NativeDirectories))))
            throw new InvalidDataException(
                "A Ruby bundle has no Ruby producer or quality owner.");
        if (changedPaths.Distinct(StringComparer.Ordinal).Count() != changedPaths.Length)
            throw new InvalidDataException("Duplicate Ruby changed coordinate.");
        var downstream = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var owners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (endpoint, index) in endpoints)
        {
            foreach (RubyProjectDependencies producer in endpoint.Native.Graph.Projects)
                foreach (string dependency in producer.Ordinary)
                    Add(downstream, dependency, producer.Directory);
            foreach (ProjectFacts owner in endpoint.Owners)
            {
                Add(owners, owner.Directory, owner.Id);
                foreach (RubyBundleConsumption bundle in endpoint.Native.Graph.Bundles.Where(
                             bundle => Governs(owner.Directory, bundle.Directory,
                                 index.NativeDirectories)))
                    foreach (string producer in bundle.Producers)
                        Add(downstream, producer, owner.Directory);
            }
        }
        var reasons = new HashSet<SelectionReason>();
        var unaffected = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in changedPaths)
        {
            ImpactPlanner.ValidatePath(path);
            bool present = false, affected = false;
            foreach (var (endpoint, index) in endpoints)
            {
                if (!index.Entries.TryGetValue(path, out GitEntry? entry)) continue;
                present = true;
                RubyRepositoryInventory.RequireFile(entry);
                var producers = new HashSet<string>(StringComparer.Ordinal);
                var targets = new HashSet<string>(StringComparer.Ordinal);
                string? nearest = GitPathOwnership.NearestDirectory(path, index.NativeDirectories);
                if (nearest is not null) producers.Add(nearest);
                foreach (SharedInput input in index.Inputs[path])
                    producers.UnionWith(input.Consumers);
                foreach (SharedInput input in index.Operations[path])
                    targets.UnionWith(input.Consumers);
                foreach (ProjectFacts owner in endpoint.Owners.Where(owner =>
                             owner.OwnedPaths?.Contains(path, StringComparer.Ordinal) == true))
                    targets.Add(owner.Id);
                if (nearest is null && !index.Inputs.Contains(path) &&
                    !index.Operations.Contains(path) && targets.Count == 0 &&
                    !index.BundleInputs.Contains(path))
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
        return new(new(before.Facts with { UnaffectedPaths = Present(before.Entries, unaffected) },
            after.Facts with { UnaffectedPaths = Present(after.Entries, unaffected) },
            changedPaths, full),
            reasons.OrderBy(reason => reason.Path, StringComparer.Ordinal)
                .ThenBy(reason => reason.Revision, StringComparer.Ordinal)
                .ThenBy(reason => reason.Project, StringComparer.Ordinal).ToArray());
    }

    private static (RepositoryFacts Facts, Dictionary<string, GitEntry> Entries,
        ILookup<string, SharedInput> Inputs, ILookup<string, SharedInput> Operations,
        HashSet<string> BundleInputs, string[] NativeDirectories) Validate(
            RubyFactsEndpoint endpoint)
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
        var bundleInputs = new HashSet<string>(StringComparer.Ordinal);
        foreach (RubyBundleConsumption bundle in endpoint.Native.Graph.Bundles)
        {
            if (bundle.Producers.Any(producer => !producers.Contains(producer)))
                throw new InvalidDataException("Unresolved Ruby bundle producer.");
            foreach (string path in bundle.Inputs)
            {
                Require(path);
                bundleInputs.Add(path);
            }
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var owned = new HashSet<string>(StringComparer.Ordinal);
        var quality = new List<ProjectFacts>();
        foreach (ProjectFacts owner in endpoint.Owners)
        {
            bool bundleOnly = !producers.Contains(owner.Directory) &&
                owner.QualityPreset == RubyProjectChecks.Preset &&
                endpoint.Native.Graph.Bundles.Any(bundle => bundle.Directory == owner.Directory);
            if (!ids.Add(owner.Id) || !owned.Add(owner.Directory) ||
                (!producers.Contains(owner.Directory) && !bundleOnly))
                throw new InvalidDataException("Unresolved actual Ruby quality owner.");
            ImpactPlanner.ValidateQualityOwner(owner);
            foreach (string path in owner.OwnedPaths ?? []) Require(path);
            string[] paths = endpoint.Revision.Entries.Select(entry => entry.Path)
                .Where(path => bundleOnly ? path != owner.Directory &&
                    Governs(owner.Directory, path, directories) :
                    GitPathOwnership.NearestDirectory(path, directories) == owner.Directory)
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
        _ = CiPlanner.Plan(new(facts, facts, [], true));
        return (facts, entries,
            endpoint.Native.Inputs.ToLookup(input => input.Path, StringComparer.Ordinal),
            endpoint.OperationInputs.ToLookup(input => input.Path, StringComparer.Ordinal),
            bundleInputs, directories);

        void Require(string path)
        {
            ImpactPlanner.ValidatePath(path);
            if (!entries.TryGetValue(path, out GitEntry? entry))
                throw new InvalidDataException("Missing committed Ruby input.");
            RubyRepositoryInventory.RequireFile(entry);
        }
    }

    // A directory owner governs its subtree up to, but excluding, any nested native gem.
    internal static bool Governs(string owner, string path, IEnumerable<string> native) =>
        (path == owner || path.StartsWith(owner + "/", StringComparison.Ordinal)) &&
        !native.Any(directory => directory != owner &&
            directory.StartsWith(owner + "/", StringComparison.Ordinal) &&
            (path == directory || path.StartsWith(directory + "/", StringComparison.Ordinal)));

    private static string[] Present(Dictionary<string, GitEntry> entries,
        IEnumerable<string> paths) => paths.Where(entries.ContainsKey)
            .Order(StringComparer.Ordinal).ToArray();

    private static void Add(Dictionary<string, HashSet<string>> graph, string from, string to)
    {
        if (!graph.TryGetValue(from, out HashSet<string>? values))
            graph.Add(from, values = new(StringComparer.Ordinal));
        values.Add(to);
    }
}
