using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record RubySourceLocation(string Directory, string Gemspec);
internal sealed record RubyBundleLocation(string Directory, string Gemfile);
internal sealed record RubyRepositoryInventory(RubySourceLocation[] Sources,
    RubyBundleLocation[] Bundles)
{
    // Git discovers committed entry points; native libraries still interpret their contents.
    internal static RubyRepositoryInventory Discover(GitRevision revision)
    {
        var sources = new Dictionary<string, RubySourceLocation>(StringComparer.Ordinal);
        var bundles = new Dictionary<string, RubyBundleLocation>(StringComparer.Ordinal);
        foreach (GitEntry entry in revision.Entries)
        {
            bool source = entry.Path.EndsWith(".gemspec", StringComparison.Ordinal);
            string name = entry.Path[(entry.Path.LastIndexOf('/') + 1)..];
            bool bundle = name is "Gemfile" or "gems.rb";
            if (!source && !bundle) continue;
            RequireFile(entry);
            int separator = entry.Path.LastIndexOf('/');
            string directory = separator < 0 ? "." : entry.Path[..separator];
            if (source && !sources.TryAdd(directory, new(directory, entry.Path)))
                throw new InvalidDataException("Ambiguous source gemspec directory.");
            if (bundle && !bundles.TryAdd(directory, new(directory, entry.Path)))
                throw new InvalidDataException("Ambiguous bundle directory.");
        }
        return new(sources.Values.OrderBy(source => source.Directory,
            StringComparer.Ordinal).ToArray(), bundles.Values.OrderBy(bundle => bundle.Directory,
            StringComparer.Ordinal).ToArray());
    }

    internal RubyDependencyGraph Bind(string root, RubySourceGemFacts[] sources,
        RubyBundleFacts[] bundles)
    {
        if (!Path.IsPathFullyQualified(root))
            throw new InvalidDataException("Ruby input binding requires its absolute endpoint.");
        RequireSame(Sources.Select(source => source.Gemspec),
            sources.Select(source => Relative(root, source.Gemspec)));
        RequireSame(Bundles.Select(bundle => bundle.Gemfile),
            bundles.Select(bundle => Relative(root, bundle.Gemfile)));
        return RubyDependencyProjection.Project(root, sources, bundles);
    }

    internal static void RequireFile(GitEntry entry)
    {
        ImpactPlanner.ValidatePath(entry.Path);
        if (entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755"))
            throw new InvalidDataException("Ruby inputs require committed regular files.");
    }

    private static string Relative(string root, string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Ruby facts require absolute native source paths.");
        string relative = Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        ImpactPlanner.ValidatePath(relative);
        return relative;
    }

    private static void RequireSame(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        string[] values = actual.ToArray();
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length ||
            !expected.Order(StringComparer.Ordinal).SequenceEqual(
                values.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException(
                "Native Ruby answers omit or substitute an entry point.");
    }
}
