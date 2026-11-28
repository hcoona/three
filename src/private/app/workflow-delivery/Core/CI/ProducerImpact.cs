namespace WorkflowDelivery.CI;

// Native readers contribute relations at both revisions. Only quality owners
// become runnable; input-only producers remain in the graph until after closure.
internal sealed class ProducerImpact
{
    private readonly Dictionary<string, HashSet<string>> consumers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> owners = new(StringComparer.Ordinal);

    internal void AddConsumption(string producer, string consumer) =>
        Add(consumers, producer, consumer);

    internal void BindOwner(string member, string owner) => Add(owners, member, owner);

    internal void IncludeOwners(IEnumerable<string> inputs, HashSet<string> selected)
    {
        var reached = inputs.ToHashSet(StringComparer.Ordinal);
        var pending = new Queue<string>(reached);
        while (pending.TryDequeue(out string? member))
        {
            if (owners.TryGetValue(member, out var bound)) selected.UnionWith(bound);
            if (consumers.TryGetValue(member, out var next))
                foreach (string consumer in next)
                    if (reached.Add(consumer)) pending.Enqueue(consumer);
        }
    }

    private static void Add(Dictionary<string, HashSet<string>> graph, string from, string to)
    {
        if (!graph.TryGetValue(from, out var values))
            graph.Add(from, values = new(StringComparer.Ordinal));
        values.Add(to);
    }
}
