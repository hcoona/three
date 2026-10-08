using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record PythonQualityBinding(string Owner, string? Member, string[] Paths);
internal sealed record PythonInputBinding(string Path, string[] Members, string[] Owners);
internal sealed record PythonOwnerEndpoint(GitRevision Revision,
    PythonProjectDependencies[] Projects, PythonQualityBinding[] Quality,
    PythonInputBinding[] Inputs);
internal sealed record PythonOwnerImpact(string Path, string Revision, string[] Owners);

// This projection consumes resolved native relations and actual quality bindings.
// It keeps all producers until both endpoint graphs have been joined. It does not
// discover targets, expand checks, associate renamed identities or qualify execution.
internal static class PythonOwnerProjection
{
    internal static PythonOwnerImpact[] Project(PythonOwnerEndpoint basis,
        PythonOwnerEndpoint candidate, string[] changedPaths,
        SelectionReason[]? pairedReasons = null)
    {
        Validate(basis);
        Validate(candidate);
        if (changedPaths.Distinct(StringComparer.Ordinal).Count() != changedPaths.Length)
            throw new InvalidDataException("Duplicate Python changed coordinate.");
        var direct = new Dictionary<(string Path, string Revision), HashSet<string>>();
        var unionOwners = basis.Quality.Concat(candidate.Quality).Select(binding => binding.Owner)
            .ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<SelectionReason>();
        foreach (SelectionReason reason in pairedReasons ?? [])
        {
            ImpactPlanner.ValidatePath(reason.Path);
            ImpactPlanner.RequireText(reason.Project, "paired Python quality owner");
            PythonOwnerEndpoint[] sources = new[] { basis, candidate }.Where(endpoint =>
                endpoint.Revision.Commit == reason.Revision && endpoint.Revision.Entries.Any(
                    entry => entry.Path == reason.Path)).ToArray();
            if (!seen.Add(reason) || !changedPaths.Contains(reason.Path, StringComparer.Ordinal) ||
                !unionOwners.Contains(reason.Project) || sources.Length == 0)
                throw new InvalidDataException("Unresolved paired Python input reason.");
            foreach (PythonOwnerEndpoint source in sources)
                Regular(source.Revision.Entries.Single(entry => entry.Path == reason.Path));
            var coordinate = (reason.Path, reason.Revision);
            if (!direct.TryGetValue(coordinate, out HashSet<string>? targets))
                direct.Add(coordinate, targets = new(StringComparer.Ordinal));
            targets.Add(reason.Project);
        }
        // Repository coordinates associate producers across endpoints. Opaque UV IDs
        // and package names remain endpoint-local; moves retain their two coordinates.
        var consumers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var quality = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (PythonOwnerEndpoint endpoint in new[] { basis, candidate })
        {
            foreach (PythonProjectDependencies project in endpoint.Projects)
                foreach (string producer in project.Ordinary.Concat(project.Build))
                    Add(consumers, producer, project.Directory);
            foreach (PythonQualityBinding binding in endpoint.Quality)
                if (binding.Member is { } member)
                    Add(quality, member, binding.Owner);
        }

        var impacts = new List<PythonOwnerImpact>();
        foreach (string path in changedPaths.Order(StringComparer.Ordinal))
        {
            ImpactPlanner.ValidatePath(path);
            bool present = false;
            foreach (PythonOwnerEndpoint endpoint in new[] { basis, candidate })
            {
                GitEntry? entry = endpoint.Revision.Entries.SingleOrDefault(item =>
                    item.Path == path);
                if (entry is null) continue;
                present = true;
                Regular(entry);
                var members = new HashSet<string>(StringComparer.Ordinal);
                var owners = new HashSet<string>(StringComparer.Ordinal);
                if (direct.TryGetValue((path, endpoint.Revision.Commit),
                        out HashSet<string>? pairedOwners)) owners.UnionWith(pairedOwners);
                string? nearest = endpoint.Projects.Select(project => project.Directory)
                    .Where(directory => directory != "." && Under(path, directory))
                    .OrderByDescending(directory => directory.Length).FirstOrDefault();
                if (nearest is not null) members.Add(nearest);
                PythonInputBinding[] inputs = endpoint.Inputs.Where(input => input.Path == path)
                    .ToArray();
                foreach (PythonInputBinding input in inputs)
                {
                    members.UnionWith(input.Members);
                    owners.UnionWith(input.Owners);
                }
                foreach (PythonQualityBinding binding in endpoint.Quality.Where(binding =>
                    binding.Paths.Contains(path, StringComparer.Ordinal)))
                    owners.Add(binding.Owner);
                if (nearest is null && inputs.Length == 0 && owners.Count == 0)
                    throw new InvalidDataException($"Unresolved Python input: {path}");
                var pending = new Queue<string>(members);
                while (pending.TryDequeue(out string? member))
                {
                    if (quality.TryGetValue(member, out HashSet<string>? targets))
                        owners.UnionWith(targets);
                    if (consumers.TryGetValue(member, out HashSet<string>? downstream))
                        foreach (string consumer in downstream)
                            if (members.Add(consumer)) pending.Enqueue(consumer);
                }
                impacts.Add(new(path, endpoint.Revision.Commit,
                    owners.Order(StringComparer.Ordinal).ToArray()));
            }
            if (!present)
                throw new InvalidDataException($"Python input absent at both endpoints: {path}");
        }
        return impacts.ToArray();
    }

    private static void Validate(PythonOwnerEndpoint endpoint)
    {
        ImpactPlanner.RequireText(endpoint.Revision.Commit, "Python revision");
        var entries = new Dictionary<string, GitEntry>(StringComparer.Ordinal);
        foreach (GitEntry entry in endpoint.Revision.Entries)
        {
            ImpactPlanner.ValidatePath(entry.Path);
            if (!entries.TryAdd(entry.Path, entry))
                throw new InvalidDataException("Duplicate Python committed coordinate.");
        }
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (PythonProjectDependencies project in endpoint.Projects)
        {
            if (project.Directory != ".") ImpactPlanner.ValidatePath(project.Directory);
            if (!members.Add(project.Directory))
                throw new InvalidDataException("Duplicate Python native member.");
            Require(project.Directory == "." ? "pyproject.toml" :
                project.Directory + "/pyproject.toml");
        }
        if (!members.Contains("."))
            throw new InvalidDataException("Python native graph omits its root context.");
        foreach (PythonProjectDependencies project in endpoint.Projects)
            foreach (string dependency in project.Ordinary.Concat(project.Build))
                if (!members.Contains(dependency))
                    throw new InvalidDataException("Unresolved Python producer relation.");
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (PythonQualityBinding binding in endpoint.Quality)
        {
            ImpactPlanner.RequireText(binding.Owner, "Python quality owner");
            if (!owners.Add(binding.Owner) ||
                (binding.Member is { } member && !members.Contains(member)) ||
                binding.Paths.Distinct(StringComparer.Ordinal).Count() != binding.Paths.Length)
                throw new InvalidDataException("Unresolved Python quality binding.");
            foreach (string path in binding.Paths) Require(path);
        }
        foreach (PythonInputBinding input in endpoint.Inputs)
        {
            ImpactPlanner.ValidatePath(input.Path);
            if (entries.TryGetValue(input.Path, out GitEntry? entry)) Regular(entry);
            if (input.Members.Any(member => !members.Contains(member)) ||
                input.Owners.Any(owner => !owners.Contains(owner)))
                throw new InvalidDataException("Unresolved Python input binding.");
        }

        void Require(string path)
        {
            ImpactPlanner.ValidatePath(path);
            if (!entries.TryGetValue(path, out GitEntry? entry))
                throw new InvalidDataException($"Missing committed Python input: {path}");
            Regular(entry);
        }
    }

    private static bool Under(string path, string directory) =>
        path.StartsWith(directory + "/", StringComparison.Ordinal);

    private static void Regular(GitEntry entry)
    {
        if (entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755"))
            throw new InvalidDataException("Unsupported Python source entry kind.");
    }

    private static void Add(Dictionary<string, HashSet<string>> graph, string from, string to)
    {
        if (!graph.TryGetValue(from, out HashSet<string>? targets))
            graph.Add(from, targets = new(StringComparer.Ordinal));
        targets.Add(to);
    }
}
