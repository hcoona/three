using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal static class GitPathOwnership
{
    internal static string? NearestDirectory(string path, IEnumerable<string> directories)
    {
        string? nearest = null;
        foreach (string directory in directories)
            if (directory != "." && (nearest is null || directory.Length > nearest.Length) &&
                path.StartsWith(directory + "/", StringComparison.Ordinal))
                nearest = directory;
        return nearest;
    }

    internal static void AddSharedInput(Dictionary<string, HashSet<string>> bindings,
        string path, IEnumerable<string> consumers)
    {
        ImpactPlanner.ValidatePath(path);
        if (!bindings.TryGetValue(path, out HashSet<string>? values))
            bindings.Add(path, values = new(StringComparer.Ordinal));
        values.UnionWith(consumers);
    }

    internal static SharedInput[] GroupSharedInputs(Dictionary<string, HashSet<string>> bindings) =>
        bindings.OrderBy(binding => binding.Key, StringComparer.Ordinal)
            .Select(binding => new SharedInput(binding.Key,
                binding.Value.Order(StringComparer.Ordinal).ToArray())).ToArray();
}
