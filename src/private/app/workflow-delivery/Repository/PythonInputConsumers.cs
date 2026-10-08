using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

// Bind supplied native and policy inputs to producers; actual check owners stay separate.
internal static class PythonInputConsumers
{
    internal static PythonInputBinding[] Map(GitRevision revision, string endpointRoot,
        PythonProjectDependencies[] projects, PythonPassiveInputs[] passive,
        NbgvInputs[] versions, IReadOnlyDictionary<string, QualitySelection?> quality,
        string[] configurationInputs)
    {
        if (!Path.IsPathFullyQualified(endpointRoot) ||
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(endpointRoot)) != endpointRoot)
            throw new InvalidDataException("Python input mapping requires a canonical endpoint.");
        var entries = new Dictionary<string, GitEntry>(StringComparer.Ordinal);
        foreach (GitEntry entry in revision.Entries)
        {
            ImpactPlanner.ValidatePath(entry.Path);
            if (!entries.TryAdd(entry.Path, entry))
                throw new InvalidDataException("Duplicate committed Python input.");
        }
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (PythonProjectDependencies project in projects)
        {
            DirectoryCoordinate(project.Directory);
            if (!members.Add(project.Directory))
                throw new InvalidDataException("Duplicate native Python input producer.");
            Require(Manifest(project.Directory));
        }
        if (!members.Contains(".") || quality.Count != members.Count ||
            quality.Keys.Any(key => !members.Contains(key)))
            throw new InvalidDataException("Incomplete Python root or quality input contexts.");
        var inputs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var configurations = new HashSet<string>(StringComparer.Ordinal);
        foreach (string absolute in configurationInputs)
            configurations.Add(Coordinate(absolute));
        if (!configurations.SetEquals(members.Select(Manifest)))
            throw new InvalidDataException(
                "Incomplete or unsupported native configuration inputs.");
        foreach (string member in members)
        {
            Add(Manifest(member), member);
            Add("pyproject.toml", member);
        }
        var nativeVersions = new Dictionary<string, NbgvInputs>(StringComparer.Ordinal);
        foreach (NbgvInputs version in versions)
        {
            DirectoryCoordinate(version.Directory);
            if (version.Commit != revision.Commit ||
                !nativeVersions.TryAdd(version.Directory, version))
                throw new InvalidDataException("Duplicate or foreign Python version subject.");
        }
        var passiveMembers = new HashSet<string>(StringComparer.Ordinal);
        var usedVersions = new HashSet<string>(StringComparer.Ordinal);
        foreach (PythonPassiveInputs input in passive)
        {
            string member = Coordinate(input.Project.Directory, directory: true);
            if (!members.Contains(member) || !passiveMembers.Add(member))
                throw new InvalidDataException("Unresolved Python passive producer.");
            if (input.Nbgv is not { } plugin) continue;
            string subject = Coordinate(plugin.WorkingDirectory, directory: true);
            if (!nativeVersions.TryGetValue(subject, out NbgvInputs? version))
                throw new InvalidDataException("Missing supplied native Python version subject.");
            usedVersions.Add(subject);
            foreach (string path in version.Paths)
            {
                Require(path);
                Add(path, member);
            }
            foreach (string path in version.ConfigurationCandidates) Add(path, member);
            if (plugin.GeneratedFile is { } generated) Add(Coordinate(generated), member);
        }
        if (!passiveMembers.SetEquals(members) || !usedVersions.SetEquals(nativeVersions.Keys))
            throw new InvalidDataException("Incomplete passive or unused native version subjects.");
        foreach (string member in members)
        {
            QualitySelection? selection = quality[member];
            if (selection is not null) Require(selection.SourcePath);
            string directory = member == "." ? "" : member;
            while (true)
            {
                string path = (directory.Length == 0 ? "" : directory + "/") +
                    "workflow-delivery.quality.yml";
                Add(path, member);
                if (selection?.SourcePath == path) break;
                if (directory.Length == 0)
                {
                    if (selection is not null)
                        throw new InvalidDataException(
                            "Quality source is outside its ancestor search.");
                    break;
                }
                int separator = directory.LastIndexOf('/');
                directory = separator < 0 ? "" : directory[..separator];
            }
        }
        return inputs.OrderBy(input => input.Key, StringComparer.Ordinal)
            .Select(input => new PythonInputBinding(input.Key,
                input.Value.Order(StringComparer.Ordinal).ToArray(), [])).ToArray();

        string Coordinate(string absolute, bool directory = false)
        {
            if (!Path.IsPathFullyQualified(absolute))
                throw new InvalidDataException("Python plugin inputs require absolute paths.");
            string canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(absolute));
            string path = Path.GetRelativePath(endpointRoot, canonical).Replace('\\', '/');
            if (directory) DirectoryCoordinate(path);
            else ImpactPlanner.ValidatePath(path);
            return path;
        }

        void Require(string path)
        {
            ImpactPlanner.ValidatePath(path);
            if (!entries.TryGetValue(path, out GitEntry? entry))
                throw new InvalidDataException("Required native Python input is not committed.");
            Regular(entry);
        }

        void Add(string path, string member)
        {
            ImpactPlanner.ValidatePath(path);
            if (entries.TryGetValue(path, out GitEntry? entry)) Regular(entry);
            if (!inputs.TryGetValue(path, out HashSet<string>? consumers))
                inputs.Add(path, consumers = new(StringComparer.Ordinal));
            consumers.Add(member);
        }
    }

    private static string Manifest(string member) =>
        member == "." ? "pyproject.toml" : member + "/pyproject.toml";

    private static void DirectoryCoordinate(string directory)
    {
        if (directory != ".") ImpactPlanner.ValidatePath(directory);
    }

    private static void Regular(GitEntry entry)
    {
        if (entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755"))
            throw new InvalidDataException("Python inputs require regular committed files.");
    }
}
