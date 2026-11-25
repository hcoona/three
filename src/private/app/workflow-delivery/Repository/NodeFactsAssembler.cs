using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record NodeRevisionInputs(
    GitRevision Revision,
    PnpmGraph Graph,
    NbgvInputs[] Versions,
    IReadOnlyDictionary<string, QualitySelection?> Quality,
    ReleaseUnitDeclaration[] Units,
    SharedInput[] OperationInputs
);

// Native readers supply semantics. This transaction joins identities, selects work, then
// resolves only selected execution contracts; an unrelated absent preset is not an error.
internal static class NodeFactsAssembler
{
    internal const string Scope = "node/pnpm-shared-workspace-v1";

    internal static async Task<CiPlan> PlanAsync(NodeRevisionInputs basis,
        NodeRevisionInputs candidate, string[] changedPaths, bool full,
        Func<PnpmProject, CancellationToken, Task<PnpmScripts>> scripts,
        Func<PnpmProject, CancellationToken, Task<string>> npmVersion,
        CancellationToken token)
    {
        RepositoryFacts before = Assemble(basis);
        RepositoryFacts after = Assemble(candidate);
        var request = new PlanRequest(before, after, changedPaths, full);
        Dictionary<string, HashSet<SelectionReason>> selected =
            ImpactPlanner.SelectProjects(request);
        return await CompleteAsync(candidate, request, selected, scripts, npmVersion, token);
    }

    internal static async Task<CiPlan> CompleteAsync(NodeRevisionInputs candidate,
        PlanRequest request, IReadOnlyDictionary<string, HashSet<SelectionReason>> selected,
        Func<PnpmProject, CancellationToken, Task<PnpmScripts>> scripts,
        Func<PnpmProject, CancellationToken, Task<string>> npmVersion,
        CancellationToken token)
    {
        RepositoryFacts after = request.Candidate;
        var native = candidate.Graph.Projects.Where(project => project.Directory != ".")
            .ToDictionary(project => project.Directory, StringComparer.Ordinal);
        var identities = after.Projects.ToDictionary(project => project.Directory,
            project => project.Id, StringComparer.Ordinal);
        var work = new Dictionary<string, List<CheckSpec>>(StringComparer.Ordinal);
        foreach (ProjectFacts project in after.Projects.Where(project =>
            selected.ContainsKey(project.Id)))
        {
            token.ThrowIfCancellationRequested();
            PnpmScripts targets = await scripts(native[project.Directory], token);
            if (targets.Directory != project.Directory)
                throw new InvalidDataException("Native scripts resolved a different project.");
            work.Add(project.Id, [.. NodeQualityChecks.Expand(candidate.Quality[project.Directory],
                targets)]);
        }

        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ReleaseUnitDeclaration unit in candidate.Units)
        {
            string[] members = after.Projects.Where(project => project.ReleaseUnit == unit.Id)
                .Select(project => project.Directory).ToArray();
            if (!members.Any(member => work.ContainsKey(identities[member])))
                continue;
            foreach (string member in members)
            {
                if (!work.ContainsKey(identities[member]))
                    throw new InvalidDataException("Selected release unit has unresolved members.");
                if (!candidate.Versions.Any(version => version.Directory == member))
                    throw new InvalidDataException(
                        "Selected package has no native version inputs.");
                if (!versions.ContainsKey(member))
                    versions.Add(member, await npmVersion(native[member], token));
            }
            CheckSpec[] packages = NodePackageChecks.Expand(unit, native, versions);
            foreach (CheckSpec check in packages)
                work[identities[check.Package!.Directory]].Add(check);
        }
        after = after with
        {
            Projects = after.Projects.Select(project =>
            work.TryGetValue(project.Id, out List<CheckSpec>? checks)
                ? project with { Checks = [.. checks] } : project).ToArray()
        };
        return ImpactPlanner.PlanSelected(request with { Candidate = after }, selected);
    }

    internal static RepositoryFacts Assemble(NodeRevisionInputs inputs)
    {
        var entries = inputs.Revision.Entries.ToDictionary(entry => entry.Path,
            StringComparer.Ordinal);
        var native = inputs.Graph.Projects.ToDictionary(project => project.Directory,
            StringComparer.Ordinal);
        if (!native.TryGetValue(".", out PnpmProject? root))
            throw new InvalidDataException("Native Node membership omitted its workspace root.");
        // Root is a tooling/input role. A root local relation has no automatic CI target.
        if (root.Dependencies.Length != 0 || inputs.Graph.Projects.Any(project =>
            project.Dependencies.Contains(".", StringComparer.Ordinal)) ||
            inputs.Graph.LocalInputs.Any(input => input.Consumers.Contains(".",
                StringComparer.Ordinal)))
            throw new InvalidDataException("Unresolved native workspace-root relation.");
        string[] directories = native.Keys.Where(directory => directory != ".")
            .Order(StringComparer.Ordinal).ToArray();
        foreach (string directory in directories)
        {
            RequireFile(directory + "/package.json");
            foreach (GitEntry entry in inputs.Revision.Entries.Where(entry =>
                entry.Path.StartsWith(directory + "/", StringComparison.Ordinal)))
                if (entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755"))
                    throw new InvalidDataException("Unsupported Node source entry kind.");
        }

        var membership = new Dictionary<string, string>(StringComparer.Ordinal);
        var shared = new List<SharedInput>(MapInputs(inputs.Revision, directories,
            inputs.Versions, inputs.Quality, inputs.Graph.LocalInputs));
        foreach (SharedInput input in inputs.OperationInputs)
            if (entries.ContainsKey(input.Path))
                RequireFile(input.Path);
        shared.AddRange(inputs.OperationInputs);
        foreach (PnpmProject project in native.Values.Where(project =>
            project.Directory != "." && project.PublishDirectory is not null))
        {
            string output = project.PublishDirectory!;
            if (output == ".")
                throw new InvalidDataException("Unresolved root publish-directory ownership.");
            foreach (GitEntry entry in inputs.Revision.Entries.Where(entry =>
                entry.Path.StartsWith(output + "/", StringComparison.Ordinal)))
            {
                RequireFile(entry.Path);
                shared.Add(new(entry.Path, [project.Directory]));
            }
        }
        var unitIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ReleaseUnitDeclaration unit in inputs.Units)
        {
            RequireFile(unit.SourcePath);
            if (!unitIds.Add(unit.Id))
                throw new InvalidDataException("Ambiguous release-unit identity.");
            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (ReleaseBuild build in unit.Builds)
            {
                string parent = NodePackageChecks.Parent(unit.SourcePath);
                string entry = build.EntryPoint == "." ? parent :
                    parent.Length == 0 ? build.EntryPoint : parent + "/" + build.EntryPoint;
                string? source = native.ContainsKey(entry) && entry != "." ? entry :
                    GitPathOwnership.NearestDirectory(entry, directories);
                string[] owners = native.Values.Where(project => project.Directory != "." &&
                    project.PublishDirectory is { } output &&
                    (entry == output || entry.StartsWith(output + "/", StringComparison.Ordinal)))
                    .Select(project => project.Directory).Concat(source is null ? [] : [source])
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (owners.Length > 1)
                    throw new InvalidDataException(
                        "Ambiguous native release-unit entry ownership.");
                string? owner = owners.SingleOrDefault();
                if (owner is null)
                {
                    if (build.Definition.StartsWith("node/", StringComparison.Ordinal))
                        throw new InvalidDataException(
                            "Unresolved Node release-unit entry: " + entry);
                    continue;
                }
                if (entries.ContainsKey(entry) || !inputs.Revision.Entries.Any(item =>
                    item.Path.StartsWith(entry + "/", StringComparison.Ordinal)))
                    RequireFile(entry);
                members.Add(owner);
                if (membership.TryGetValue(owner, out string? previous) && previous != unit.Id)
                    throw new InvalidDataException(
                        "A Node project belongs to multiple release units.");
                membership[owner] = unit.Id;
            }
            if (members.Count != 0)
                shared.Add(new(unit.SourcePath, members.Order(StringComparer.Ordinal).ToArray()));
        }
        // Native names associate workspace projects across moves. They never resolve
        // a dependency's source directory; that remains the graph reader's native join.
        var identities = directories.ToDictionary(directory => directory,
            directory => native[directory].Name is { } name ? "npm:" + name : directory,
            StringComparer.Ordinal);
        if (identities.Values.Distinct(StringComparer.Ordinal).Count() != identities.Count)
            throw new InvalidDataException("Ambiguous native Node project identity.");
        return new(inputs.Revision.Commit, Scope, directories.Select(directory =>
            new ProjectFacts(identities[directory], directory,
                native[directory].Dependencies.Select(Identity)
                    .ToArray(), [],
                membership.GetValueOrDefault(directory), inputs.Quality[directory]?.Preset,
                [])).ToArray(), shared.Select(input => input with
                { Consumers = input.Consumers.Select(Identity).ToArray() })
                    .ToArray(), [], []);

        string Identity(string directory) => identities.TryGetValue(directory, out string? identity)
            ? identity : throw new InvalidDataException("Unresolved native Node relation: "
                + directory);

        void RequireFile(string path)
        {
            if (!entries.TryGetValue(path, out GitEntry? entry) || entry.ObjectType != "blob" ||
                entry.Mode is not ("100644" or "100755"))
                throw new InvalidDataException(
                    $"Required Node input is not a committed file: {path}");
        }
    }

    private static SharedInput[] MapInputs(GitRevision revision, string[] directories,
        NbgvInputs[] versions,
        IReadOnlyDictionary<string, QualitySelection?> quality, PnpmLocalInput[] localInputs)
    {
        var entries = revision.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var consumers = new HashSet<string>(StringComparer.Ordinal);
        foreach (string directory in directories)
        {
            ValidatePath(directory);
            if (!consumers.Add(directory))
                throw new InvalidDataException(
                    "Duplicate native input consumer.");
        }
        if (quality.Count != consumers.Count || quality.Keys.Any(key => !consumers.Contains(key)))
            throw new InvalidDataException(
                "Quality input consumers do not match the source projects.");

        var inputs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var versionConsumers = new HashSet<string>(StringComparer.Ordinal);
        foreach (NbgvInputs project in versions)
        {
            if (project.Commit != revision.Commit || !consumers.Contains(project.Directory) ||
                !versionConsumers.Add(project.Directory))
                throw new InvalidDataException("Unresolved native version consumer or revision.");
            foreach (string path in project.Paths)
            {
                if (!entries.TryGetValue(path, out GitEntry? entry))
                    throw new InvalidDataException("A native version input is not committed.");
                RequireFile(entry);
                Add(path, project.Directory);
            }
            foreach (string path in project.ConfigurationCandidates)
                Add(path, project.Directory);
        }
        foreach (string consumer in directories)
        {
            QualitySelection? selection = quality[consumer];
            if (selection is not null && !entries.ContainsKey(selection.SourcePath))
                throw new InvalidDataException(
                    "The effective quality declaration is not committed.");
            string directory = consumer;
            while (true)
            {
                string path = (directory.Length == 0 ? "" : directory + "/") +
                    "workflow-delivery.quality.yml";
                if (entries.TryGetValue(path, out GitEntry? entry))
                    RequireFile(entry);
                Add(path, consumer);
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

        return GitPathOwnership.GroupSharedInputs(inputs);

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
                "Required input coverage needs a regular Git file.");
    }
}
