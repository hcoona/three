using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

// Join native bundle consumption to its actual PNPM workspace directory.
internal static class RubyNodeInputConsumers
{
    internal static NodeRevisionInputs Join(NodeRevisionInputs node, RubyFactsEndpoint ruby)
    {
        if (node.Revision.Commit != ruby.Revision.Commit)
            throw new InvalidDataException(
                "Ruby and Node inputs have different endpoint revisions.");
        _ = RubyFactsAssembler.Assemble(ruby, ruby, [], false);
        var nodeProjects = node.Graph.Projects.Select(project => project.Directory)
            .ToHashSet(StringComparer.Ordinal);
        var producers = ruby.Native.Graph.Projects.Select(project => project.Directory)
            .ToHashSet(StringComparer.Ordinal);
        var inputs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (SharedInput input in node.OperationInputs) Add(input.Path, input.Consumers);
        foreach (RubyBundleConsumption bundle in ruby.Native.Graph.Bundles)
        {
            if (!nodeProjects.Contains(bundle.Directory))
            {
                if (producers.Contains(bundle.Directory)) continue;
                throw new InvalidDataException(
                    "A bundle-only Ruby consumer has no native Node workspace identity.");
            }
            if (bundle.Directory == ".")
                throw new InvalidDataException("Root Node bundle consumption is unsupported.");
            string[] consumer = [bundle.Directory];
            foreach (string path in bundle.Inputs) Add(path, consumer);
            foreach (SharedInput input in ruby.Native.Inputs)
                if (input.Consumers.Intersect(bundle.Producers, StringComparer.Ordinal).Any())
                    Add(input.Path, consumer);
        }
        return node with
        {
            OperationInputs = inputs.OrderBy(input => input.Key, StringComparer.Ordinal)
                .Select(input => new SharedInput(input.Key,
                    input.Value.Order(StringComparer.Ordinal).ToArray())).ToArray(),
        };

        void Add(string path, string[] consumers)
        {
            ImpactPlanner.ValidatePath(path);
            if (!inputs.TryGetValue(path, out HashSet<string>? owners))
                inputs.Add(path, owners = new(StringComparer.Ordinal));
            owners.UnionWith(consumers);
        }
    }
}
