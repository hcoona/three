using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

// Map supplied native coordinates; the caller still owns complete fact assembly.
internal static class NodeInputConsumers
{
    internal static SharedInput[] Map(GitRevision revision, NbgvInputs[] projects,
        IReadOnlyDictionary<string, QualitySelection?> quality, PnpmLocalInput[] localInputs)
    {
        var entries = revision.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var consumers = new HashSet<string>(StringComparer.Ordinal);
        foreach (NbgvInputs project in projects)
        {
            ValidatePath(project.Directory);
            if (project.Commit != revision.Commit || !consumers.Add(project.Directory))
                throw new InvalidDataException(
                    "Unresolved native input consumer or source revision.");
        }
        if (quality.Count != consumers.Count || quality.Keys.Any(key => !consumers.Contains(key)))
            throw new InvalidDataException(
                "Quality input consumers do not match the source projects.");

        var inputs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (NbgvInputs project in projects)
        {
            foreach (string path in project.Paths)
            {
                if (!entries.ContainsKey(path))
                    throw new InvalidDataException("A native version input is not committed.");
                Add(path, project.Directory);
            }
            foreach (string path in project.ConfigurationCandidates)
                Add(path, project.Directory);

            QualitySelection? selection = quality[project.Directory];
            if (selection is not null && !entries.ContainsKey(selection.SourcePath))
                throw new InvalidDataException(
                    "The effective quality declaration is not committed.");
            string directory = project.Directory;
            while (true)
            {
                string path = (directory.Length == 0 ? "" : directory + "/") +
                    "workflow-delivery.quality.yml";
                if (entries.TryGetValue(path, out GitEntry? entry))
                    RequireFile(entry);
                Add(path, project.Directory);
                if (selection?.SourcePath == path)
                    break;
                if (directory.Length == 0)
                {
                    if (selection is not null)
                        throw new InvalidDataException(
                            "The effective quality declaration is outside the ancestor search.");
                    break;
                }
                int separator = directory.LastIndexOf('/');
                directory = separator < 0 ? "" : directory[..separator];
            }
        }

        foreach (PnpmLocalInput input in localInputs)
        {
            ValidatePath(input.Path);
            if (input.Consumers.Length == 0 || input.Consumers.Any(id => !consumers.Contains(id)))
                throw new InvalidDataException("Unresolved local input consumer.");
            GitEntry[] files = input.IsDirectory
                ? revision.Entries.Where(entry => entry.Path.StartsWith(input.Path + "/",
                    StringComparison.Ordinal)).ToArray()
                : entries.TryGetValue(input.Path, out GitEntry? localFile) ? [localFile] : [];
            if (files.Length == 0)
                throw new InvalidDataException("A required local input has no committed source.");
            foreach (GitEntry file in files)
            {
                RequireFile(file);
                foreach (string consumer in input.Consumers)
                    Add(file.Path, consumer);
            }
        }

        return inputs.OrderBy(input => input.Key, StringComparer.Ordinal)
            .Select(input => new SharedInput(input.Key,
                input.Value.Order(StringComparer.Ordinal).ToArray())).ToArray();

        void Add(string path, string consumer)
        {
            ValidatePath(path);
            if (!inputs.TryGetValue(path, out HashSet<string>? owners))
                inputs.Add(path, owners = new(StringComparer.Ordinal));
            owners.Add(consumer);
        }
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("A required input coordinate is not representable.");
    }

    private static void RequireFile(GitEntry entry)
    {
        if (entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755"))
            throw new InvalidDataException(
                "Required local input coverage needs a regular Git file.");
    }
}
