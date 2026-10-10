using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record RubyRepositoryInputs(RubyDependencyGraph Graph, SharedInput[] Inputs);

// Bind supplied native inputs to producers; bundle-only consumers keep their actual identity.
internal static class RubyInputConsumers
{
    internal static RubyRepositoryInputs Map(string root, GitRevision revision,
        RubySourceGemFacts[] sources, RubyBundleFacts[] bundles, NbgvInputs[] versions,
        IReadOnlyDictionary<string, QualitySelection?> quality)
    {
        RubyRepositoryInventory inventory = RubyRepositoryInventory.Discover(revision);
        RubyDependencyGraph graph = inventory.Bind(root, sources, bundles);
        var projects = graph.Projects.Select(project => project.Directory)
            .ToHashSet(StringComparer.Ordinal);
        if (projects.Contains("."))
            throw new InvalidDataException("Root Ruby source input ownership is unsupported.");
        if (quality.Count != projects.Count || quality.Keys.Any(key => !projects.Contains(key)))
            throw new InvalidDataException("Ruby quality owners do not match source producers.");
        var entries = revision.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var inputs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (GitEntry entry in revision.Entries)
        {
            string? owner = projects.Where(directory => entry.Path.StartsWith(directory + "/",
                StringComparison.Ordinal)).OrderByDescending(directory => directory.Length)
                .FirstOrDefault();
            if (owner is null) continue;
            RubyRepositoryInventory.RequireFile(entry);
            Add(entry.Path, owner);
        }
        var versioned = new HashSet<string>(StringComparer.Ordinal);
        foreach (NbgvInputs version in versions)
        {
            if (version.Commit != revision.Commit || !projects.Contains(version.Directory) ||
                !versioned.Add(version.Directory))
                throw new InvalidDataException("Unresolved Ruby version input owner or revision.");
            foreach (string path in version.Paths)
            {
                Require(path);
                Add(path, version.Directory);
            }
            foreach (string path in version.ConfigurationCandidates)
                Add(path, version.Directory);
        }
        foreach (string project in projects)
        {
            QualitySelection? selection = quality[project];
            if (selection is not null) Require(selection.SourcePath);
            string directory = project;
            while (true)
            {
                string path = (directory.Length == 0 ? "" : directory + "/") +
                    "workflow-delivery.quality.yml";
                Add(path, project);
                if (path == selection?.SourcePath) break;
                if (directory.Length == 0)
                {
                    if (selection is not null)
                        throw new InvalidDataException(
                            "Ruby quality is outside its ancestor search.");
                    break;
                }
                int separator = directory.LastIndexOf('/');
                directory = separator < 0 ? "" : directory[..separator];
            }
        }
        foreach (RubyBundleConsumption bundle in graph.Bundles)
            foreach (string path in bundle.Inputs)
            {
                Require(path);
                if (projects.Contains(bundle.Directory)) Add(path, bundle.Directory);
            }
        return new(graph, inputs.OrderBy(input => input.Key, StringComparer.Ordinal)
            .Select(input => new SharedInput(input.Key,
                input.Value.Order(StringComparer.Ordinal).ToArray())).ToArray());

        void Require(string path)
        {
            if (!entries.TryGetValue(path, out GitEntry? entry))
                throw new InvalidDataException("A required Ruby input is not committed.");
            RubyRepositoryInventory.RequireFile(entry);
        }

        void Add(string path, string consumer)
        {
            ImpactPlanner.ValidatePath(path);
            if (entries.TryGetValue(path, out GitEntry? entry))
                RubyRepositoryInventory.RequireFile(entry);
            if (!inputs.TryGetValue(path, out HashSet<string>? owners))
                inputs.Add(path, owners = new(StringComparer.Ordinal));
            owners.Add(consumer);
        }
    }
}
