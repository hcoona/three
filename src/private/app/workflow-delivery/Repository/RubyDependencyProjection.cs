using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record RubyProjectDependencies(string Directory, string Name, string[] Ordinary);
internal sealed record RubyBundleConsumption(string Gemfile, string Directory,
    string[] Inputs, string[] Producers);
internal sealed record RubyDependencyGraph(RubyProjectDependencies[] Projects,
    RubyBundleConsumption[] Bundles);

// Native libraries select versions/groups/platforms. This joins their resolved local coordinates.
internal static class RubyDependencyProjection
{
    internal static RubyDependencyGraph Project(string root, RubySourceGemFacts[] sources,
        RubyBundleFacts[] bundles)
    {
        if (!Path.IsPathFullyQualified(root))
            throw new InvalidDataException("Ruby projection requires its absolute endpoint.");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var gems = new Dictionary<string, RubySourceGemFacts>(Paths);
        var owners = new Dictionary<string, RubySourceGemFacts>(StringComparer.Ordinal);
        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (RubySourceGemFacts source in sources)
        {
            string directory = DirectoryOf(root, source.Gemspec);
            if (!gems.TryAdd(Path.GetFullPath(source.Gemspec), source) ||
                !owners.TryAdd(directory, source) ||
                !names.Add(source.Gem.Name))
                throw new InvalidDataException("Ambiguous Ruby source producer.");
            dependencies.Add(directory, new(StringComparer.Ordinal));
        }
        var results = new List<RubyBundleConsumption>();
        var gemfiles = new HashSet<string>(Paths);
        foreach (RubyBundleFacts bundle in bundles)
        {
            string directory = DirectoryOf(root, bundle.Gemfile);
            if (!gemfiles.Add(Path.GetFullPath(bundle.Gemfile)))
                throw new InvalidDataException("Duplicate Ruby bundle context.");
            var native = new Dictionary<string, RubyBundleSpecification>(StringComparer.Ordinal);
            foreach (RubyBundleSpecification specification in bundle.Specifications)
                if (!native.TryAdd(specification.Gem.Name, specification))
                    throw new InvalidDataException("Ambiguous selected native Ruby specification.");
            foreach (string name in bundle.DirectDependencies.Concat(native.Values.SelectMany(
                specification => specification.Gem.RuntimeDependencies.Select(edge => edge.Name))))
                if (!native.ContainsKey(name))
                    throw new InvalidDataException("Incomplete selected native Ruby dependencies.");
            var local = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (RubyBundleSpecification specification in native.Values)
            {
                RubyNativeSource source = specification.Source;
                if (source.Kind is not ("path" or "local_git")) continue;
                string path = source.Path ?? throw new InvalidDataException(
                    "Missing original native Ruby source.");
                string gemspec = source.Gemspec ?? throw new InvalidDataException(
                    "Missing original native Ruby gemspec.");
                _ = Relative(root, path);
                _ = Relative(root, gemspec);
                string within = Path.GetRelativePath(path, gemspec);
                if (Path.IsPathRooted(within) || within == ".." ||
                    within.StartsWith(".." + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal) ||
                    !gems.TryGetValue(Path.GetFullPath(gemspec),
                        out RubySourceGemFacts? producer) ||
                    producer.Gem.Name != specification.Gem.Name)
                    throw new InvalidDataException(
                        "Native local Ruby source has no exact producer.");
                local.Add(specification.Gem.Name, DirectoryOf(root, producer.Gemspec));
            }
            foreach ((string name, string owner) in local)
                dependencies[owner].UnionWith(Reach(native[name].Gem.RuntimeDependencies
                    .Select(edge => edge.Name)).Where(producer => producer != owner));
            string[] producers = Reach(bundle.DirectDependencies)
                .Where(producer => producer != directory).Order(StringComparer.Ordinal).ToArray();
            if (dependencies.TryGetValue(directory, out HashSet<string>? own))
                own.UnionWith(producers);
            results.Add(new(Relative(root, bundle.Gemfile), directory,
                bundle.Gemfiles.Append(bundle.Lockfile)
                .Select(path => Relative(root, path)).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray(), producers));

            HashSet<string> Reach(IEnumerable<string> entries)
            {
                var found = new HashSet<string>(StringComparer.Ordinal);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var pending = new Queue<string>(entries);
                while (pending.TryDequeue(out string? name))
                {
                    if (!visited.Add(name)) continue;
                    if (local.TryGetValue(name, out string? owner)) found.Add(owner);
                    foreach (RubyDependency edge in native[name].Gem.RuntimeDependencies)
                        pending.Enqueue(edge.Name);
                }
                return found;
            }
        }
        return new(owners.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            new RubyProjectDependencies(pair.Key, pair.Value.Gem.Name,
                dependencies[pair.Key].Order(StringComparer.Ordinal).ToArray())).ToArray(),
            results.OrderBy(bundle => bundle.Gemfile, StringComparer.Ordinal).ToArray());
    }

    private static string DirectoryOf(string root, string path)
    {
        _ = Relative(root, path);
        return Relative(root, Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    private static string Relative(string root, string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Ruby coordinates require absolute native paths.");
        string relative = Path.GetRelativePath(root, Path.GetFullPath(path))
            .Replace(Path.DirectorySeparatorChar, '/');
        if (relative != ".") ImpactPlanner.ValidatePath(relative);
        return relative;
    }

    private static StringComparer Paths => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
