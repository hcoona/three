using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record PythonPytestTarget(string Target, string? Member, string[] Inputs);
internal sealed record PythonPytestTargetSet(string Revision, string ConfigurationFile,
    PythonPytestTarget[] Targets);

// Native pytest supplies targets; Git and native UV membership supply their coordinates.
// This binds inputs, not successful collection, execution or package quality contracts.
internal static class PythonPytestTargets
{
    internal static PythonPytestTargetSet Bind(PythonPytestConfiguration configuration,
        GitRevision revision, PythonProjectDependencies[] projects)
    {
        ImpactPlanner.RequireText(revision.Commit, "Python target revision");
        var entries = new Dictionary<string, GitEntry>(StringComparer.Ordinal);
        foreach (GitEntry entry in revision.Entries)
        {
            ImpactPlanner.ValidatePath(entry.Path);
            if (!entries.TryAdd(entry.Path, entry))
                throw new InvalidDataException("Duplicate committed Python target coordinate.");
        }
        Require(configuration.ConfigurationFile);
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (PythonProjectDependencies project in projects)
        {
            if (project.Directory != ".") ImpactPlanner.ValidatePath(project.Directory);
            if (!members.Add(project.Directory))
                throw new InvalidDataException("Duplicate native Python member coordinate.");
            Require(project.Directory == "." ? "pyproject.toml" :
                project.Directory + "/pyproject.toml");
        }
        if (!members.Contains("."))
            throw new InvalidDataException("Native Python targets omit the root context.");
        if (configuration.TestPaths.Length == 0 || configuration.TestPaths.Distinct(
                StringComparer.Ordinal).Count() != configuration.TestPaths.Length)
            throw new InvalidDataException("Native Python targets must be explicit and unique.");
        var targets = new List<PythonPytestTarget>();
        foreach (string target in configuration.TestPaths)
        {
            ImpactPlanner.ValidatePath(target);
            string[] inputs = entries.Keys.Where(path => path == target || Under(path, target))
                .Order(StringComparer.Ordinal).ToArray();
            if (inputs.Length == 0)
                throw new InvalidDataException("Native pytest target has no committed inputs.");
            foreach (string input in inputs) Require(input);
            string? member = members.Where(directory => directory != "." &&
                (target == directory || Under(target, directory)))
                .OrderByDescending(directory => directory.Length)
                .FirstOrDefault();
            // A single runnable target must not hide additional native producer contexts.
            if (members.Any(directory => directory != "." && Under(directory, target)))
                throw new InvalidDataException("Unsupported multi-member pytest target scope.");
            targets.Add(new(target, member, inputs));
        }
        return new(revision.Commit, configuration.ConfigurationFile, targets.ToArray());

        void Require(string path)
        {
            ImpactPlanner.ValidatePath(path);
            if (!entries.TryGetValue(path, out GitEntry? entry) || entry.ObjectType != "blob" ||
                entry.Mode is not ("100644" or "100755"))
                throw new InvalidDataException("Required pytest input is not a committed file.");
        }
    }

    private static bool Under(string path, string directory) =>
        path.StartsWith(directory + "/", StringComparison.Ordinal);
}
