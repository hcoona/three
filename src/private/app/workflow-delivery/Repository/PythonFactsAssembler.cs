using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record PythonPytestOperation(string Runner, string Variant,
    Dictionary<string, string> Dimensions);
internal sealed record PythonFactsEndpoint(GitRevision Revision,
    PythonProjectDependencies[] Projects, PythonPytestConfiguration Configuration,
    PythonPytestOperation Operation, ProjectFacts[] Packages, PythonInputBinding[] Inputs);
internal sealed record PythonFactAssembly(PlanRequest Request, SelectionReason[] PairedReasons);

// Compose supplied native answers and actual checks; the shared core plans execution.
internal static class PythonFactsAssembler
{
    internal const string Scope = "python/native-paired-facts-v1";

    internal static PythonFactAssembly Assemble(PythonFactsEndpoint basis,
        PythonFactsEndpoint candidate, string[] changedPaths, bool full)
    {
        var before = Endpoint(basis);
        var after = Endpoint(candidate);
        PythonOwnerImpact[] impacts = PythonOwnerProjection.Project(before.Owners,
            after.Owners, changedPaths);
        var reasons = impacts.SelectMany(impact => impact.Owners.Select(owner =>
            new SelectionReason(impact.Path, impact.Revision, owner))).Distinct().ToArray();
        string[] unaffected = impacts.GroupBy(impact => impact.Path, StringComparer.Ordinal)
            .Where(group => group.All(impact => impact.Owners.Length == 0))
            .Select(group => group.Key).Order(StringComparer.Ordinal).ToArray();
        return new(new(before.Facts with { UnaffectedPaths = Present(basis, unaffected) },
            after.Facts with { UnaffectedPaths = Present(candidate, unaffected) },
            changedPaths, full), reasons);
    }

    private static (RepositoryFacts Facts, PythonOwnerEndpoint Owners) Endpoint(
        PythonFactsEndpoint endpoint)
    {
        PythonPytestTargetSet targets = PythonPytestTargets.Bind(endpoint.Configuration,
            endpoint.Revision, endpoint.Projects);
        string[] options = endpoint.Configuration.Options;
        if (options.Length != 0 &&
            (options.Length != 1 || options[0] != "--import-mode=importlib"))
            throw new InvalidDataException("Unsupported supplied native pytest option scope.");
        var owners = new List<ProjectFacts>();
        var quality = new List<PythonQualityBinding>();
        var inputs = new List<PythonInputBinding>(endpoint.Inputs);
        foreach (PythonPytestTarget target in targets.Targets)
        {
            string id = "pytest:" + target.Target;
            var dimensions = new Dictionary<string, string>(endpoint.Operation.Dimensions,
                StringComparer.Ordinal);
            if (!dimensions.TryAdd("testPath", target.Target) ||
                !dimensions.TryAdd("configuration", targets.ConfigurationFile) ||
                dimensions.ContainsKey("importMode"))
                throw new InvalidDataException("Conflicting native pytest operation dimensions.");
            if (options.Length != 0) dimensions.Add("importMode", "importlib");
            var check = new CheckSpec(new(id, "python/pytest-v1", endpoint.Operation.Variant),
                dimensions, endpoint.Operation.Runner, true, []);
            owners.Add(new(id, target.Member ?? ".", [], [], null, null, [check],
                target.Inputs, CheckOrigin.NativeRetained));
            quality.Add(new(id, target.Member, target.Inputs));
        }
        foreach (ProjectFacts package in endpoint.Packages)
        {
            if (!endpoint.Projects.Any(project => project.Directory == package.Directory) ||
                (package.Directory == "." && package.OwnedPaths is null))
                throw new InvalidDataException("Python package has no exact native member scope.");
            if (package.OwnedPaths is { } explicitPaths &&
                explicitPaths.Distinct(StringComparer.Ordinal).Count() != explicitPaths.Length)
                throw new InvalidDataException("Duplicate supplied Python package input.");
            foreach (string path in package.OwnedPaths ?? [])
            {
                ImpactPlanner.ValidatePath(path);
                inputs.Add(new(path, [], [package.Id]));
            }
            // Resolve over all native members, including nested input-only producers.
            string[] paths = endpoint.Revision.Entries.Select(entry => entry.Path)
                .Where(path => package.Directory != "." && Nearest(path) == package.Directory)
                .Concat(package.OwnedPaths ?? []).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            string[] committed = Present(endpoint, paths);
            owners.Add(package with { OwnedPaths = committed });
            quality.Add(new(package.Id, package.Directory, committed));
        }
        foreach (ProjectFacts owner in owners) ImpactPlanner.ValidateQualityOwner(owner);
        inputs.Add(new(targets.ConfigurationFile, [], targets.Targets.Select(target =>
            "pytest:" + target.Target).ToArray()));
        var present = endpoint.Revision.Entries.Select(entry => entry.Path)
            .ToHashSet(StringComparer.Ordinal);
        var facts = new RepositoryFacts(endpoint.Revision.Commit, Scope, owners.ToArray(),
            inputs.Where(input => input.Owners.Length != 0 && present.Contains(input.Path))
                .Select(input => new SharedInput(input.Path, input.Owners)).ToArray(), [], []);
        // Empty selection validates all available checks/relations and prerequisites.
        // Its result is not imported as selection; paired closure still runs below.
        ImpactPlanner.Plan(new(facts, facts, [], false));
        return (facts, new(endpoint.Revision, endpoint.Projects, quality.ToArray(),
            inputs.ToArray()));

        string? Nearest(string path) => endpoint.Projects.Select(project => project.Directory)
            .Where(directory => directory != "." &&
                path.StartsWith(directory + "/", StringComparison.Ordinal))
            .OrderByDescending(directory => directory.Length).FirstOrDefault();
    }

    private static string[] Present(PythonFactsEndpoint endpoint, string[] paths)
    {
        var committed = endpoint.Revision.Entries.Select(entry => entry.Path)
            .ToHashSet(StringComparer.Ordinal);
        return paths.Where(committed.Contains).ToArray();
    }
}
