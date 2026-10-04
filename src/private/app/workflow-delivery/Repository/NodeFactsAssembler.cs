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
        var native = candidate.Graph.Projects.Where(project => project.Directory != ".")
            .ToDictionary(project => project.Directory, StringComparer.Ordinal);
        var work = new Dictionary<string, List<CheckSpec>>(StringComparer.Ordinal);
        foreach (ProjectFacts project in after.Projects.Where(project =>
            selected.ContainsKey(project.Id)))
        {
            token.ThrowIfCancellationRequested();
            PnpmScripts targets = await scripts(native[project.Id], token);
            if (targets.Directory != project.Directory)
                throw new InvalidDataException("Native scripts resolved a different project.");
            work.Add(project.Id, [.. NodeQualityChecks.Expand(candidate.Quality[project.Id],
                targets)]);
        }

        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ReleaseUnitDeclaration unit in candidate.Units)
        {
            string[] members = after.Projects.Where(project => project.ReleaseUnit == unit.Id)
                .Select(project => project.Id).ToArray();
            if (!members.Any(work.ContainsKey))
                continue;
            foreach (string member in members)
            {
                if (!work.ContainsKey(member))
                    throw new InvalidDataException("Selected release unit has unresolved members.");
                if (!candidate.Versions.Any(version => version.Directory == member))
                    throw new InvalidDataException(
                        "Selected package has no native version inputs.");
                if (!versions.ContainsKey(member))
                    versions.Add(member, await npmVersion(native[member], token));
            }
            CheckSpec[] packages = NodePackageChecks.Expand(unit, native, versions);
            foreach (CheckSpec check in packages)
                work[check.Package!.Directory].Add(check);
        }
        after = after with { Projects = after.Projects.Select(project =>
            work.TryGetValue(project.Id, out List<CheckSpec>? checks)
                ? project with { Checks = [.. checks] } : project).ToArray() };
        return ImpactPlanner.Plan(request with { Candidate = after });
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
        var shared = new List<SharedInput>(NodeInputConsumers.Map(inputs.Revision, directories,
            inputs.Versions, inputs.Quality, inputs.Graph.LocalInputs));
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
                string? source = directories.Where(directory => entry == directory ||
                    entry.StartsWith(directory + "/", StringComparison.Ordinal))
                    .OrderByDescending(directory => directory.Length).FirstOrDefault();
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
        return new(inputs.Revision.Commit, Scope, directories.Select(directory =>
            new ProjectFacts(directory, directory, native[directory].Dependencies, [],
                membership.GetValueOrDefault(directory), inputs.Quality[directory]?.Preset,
                [])).ToArray(), shared.ToArray(), [], []);

        void RequireFile(string path)
        {
            if (!entries.TryGetValue(path, out GitEntry? entry) || entry.ObjectType != "blob" ||
                entry.Mode is not ("100644" or "100755"))
                throw new InvalidDataException(
                    $"Required Node input is not a committed file: {path}");
        }
    }
}
