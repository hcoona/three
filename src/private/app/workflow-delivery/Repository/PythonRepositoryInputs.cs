using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

// Repository engineering inputs select actual supplied checks, never a native target roster.
internal static class PythonRepositoryInputs
{
    private static readonly string[] Shared = [
        ".github/workflows/ci.yml", "eng/scripts/Bootstrap-CiControl.ps1",
        "src/private/app/workflow-delivery/CI/Callers/PythonCiCaller.cs",
        "src/private/app/workflow-delivery/CI/Callers/CiCallerCli.cs",
        "src/private/app/workflow-delivery/CI/Callers/CiCallerTransfer.cs",
        "mise.toml", "mise.lock"];
    private static readonly string[] Preparation = [
        "pyproject.toml", "uv.lock", "uv.toml", ".python-version", ".config/uv/uv.toml",
        "eng/scripts/sync_python_version.py"];
    private static readonly string[] Pytest = [
        "conftest.py", "pytest.ini", "eng/scripts/test-python.sh",
        "src/private/app/workflow-delivery/CI/Callers/LocalPythonCiCaller.cs"];
    private static readonly string[] AzureRoots = [
        "src/private/app/azureauth-credprovider", "tests/private/app/azureauth-credprovider"];
    internal static PythonInputBinding[] Map(GitRevision revision,
        PythonProjectDependencies[] projects, PythonPytestTargetSet targets,
        ProjectFacts[] packages) => Map(revision, projects, targets, packages,
            revision.Entries.Select(entry => entry.Path));

    internal static SelectionReason[] Pair(PythonFactsEndpoint basis,
        PythonFactsEndpoint candidate, string[] changedPaths)
    {
        if (changedPaths.Distinct(StringComparer.Ordinal).Count() != changedPaths.Length)
            throw new InvalidDataException("Duplicate paired Python repository input.");
        foreach (string path in changedPaths) ImpactPlanner.ValidatePath(path);
        PythonFactsEndpoint[] endpoints = [basis, candidate];
        PythonInputBinding[] bindings = endpoints.SelectMany(endpoint => Map(endpoint.Revision,
            endpoint.Projects, PythonPytestTargets.Bind(endpoint.Configuration,
                endpoint.Revision, endpoint.Projects), endpoint.Packages, changedPaths))
            .ToArray();
        var reasons = new HashSet<SelectionReason>();
        foreach (string path in changedPaths)
        {
            GitRevision[] sources = endpoints.Select(endpoint => endpoint.Revision)
                .Where(revision => revision.Entries.Any(entry => entry.Path == path)).ToArray();
            if (sources.Length == 0)
                throw new InvalidDataException("Paired Python input is absent at both endpoints.");
            foreach (GitRevision source in sources)
            {
                GitEntry entry = source.Entries.Single(entry => entry.Path == path);
                if (entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755"))
                    throw new InvalidDataException("Paired Python inputs require regular files.");
                foreach (string owner in bindings.Where(binding => binding.Path == path)
                    .SelectMany(binding => binding.Owners))
                    reasons.Add(new(path, source.Commit, owner));
            }
        }
        return reasons.OrderBy(reason => reason.Path, StringComparer.Ordinal)
            .ThenBy(reason => reason.Revision, StringComparer.Ordinal)
            .ThenBy(reason => reason.Project, StringComparer.Ordinal).ToArray();
    }

    private static PythonInputBinding[] Map(GitRevision revision,
        PythonProjectDependencies[] projects, PythonPytestTargetSet targets,
        ProjectFacts[] packages, IEnumerable<string> paths)
    {
        if (targets.Revision != revision.Commit)
            throw new InvalidDataException("Foreign Python repository target revision.");
        // Reuse the accepted native-target binding rather than another coordinate validator.
        PythonPytestTargetSet bound = PythonPytestTargets.Bind(new(targets.ConfigurationFile,
            targets.Targets.Select(target => target.Target).ToArray(), []), revision, projects);
        foreach (PythonPytestTarget target in targets.Targets)
        {
            PythonPytestTarget actual = bound.Targets.Single(item => item.Target == target.Target);
            if (target.Member != actual.Member ||
                !target.Inputs.SequenceEqual(actual.Inputs, StringComparer.Ordinal))
                throw new InvalidDataException("Unresolved supplied Python repository target.");
        }
        var owners = targets.Targets.Select(target => "pytest:" + target.Target)
            .ToHashSet(StringComparer.Ordinal);
        foreach (ProjectFacts package in packages)
        {
            ImpactPlanner.RequireText(package.Id, "Python package owner");
            ImpactPlanner.ValidateQualityOwner(package);
            if (!owners.Add(package.Id) || !projects.Any(project =>
                    project.Directory == package.Directory) ||
                (package.Directory == "." && package.OwnedPaths is null))
                throw new InvalidDataException("Duplicate or foreign Python package owner.");
        }
        var entries = revision.Entries.ToDictionary(entry => entry.Path,
            StringComparer.Ordinal);
        var inputs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string path in Shared.Concat(Preparation))
            foreach (string owner in owners) Add(path, owner);
        foreach (string path in Pytest)
            foreach (PythonPytestTarget target in targets.Targets)
                Add(path, "pytest:" + target.Target);
        foreach (string path in paths.Where(InertControlDocument)) Add(path, null);
        foreach (PythonPytestTarget target in targets.Targets)
        {
            string owner = "pytest:" + target.Target;
            foreach (string path in DotNetInheritedInputs.Root)
                if (target.Target.StartsWith("src/public/lib/nbgv-python/",
                        StringComparison.Ordinal) ||
                    target.Target.StartsWith(AzureRoots[0] + "/", StringComparison.Ordinal))
                    Add(path, owner);
            foreach (string path in paths)
                if (Special(target.Target, path)) Add(path, owner);
        }
        return inputs.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new PythonInputBinding(item.Key, [],
                item.Value.Order(StringComparer.Ordinal).ToArray())).ToArray();

        void Add(string path, string? owner)
        {
            ImpactPlanner.ValidatePath(path);
            if (entries.TryGetValue(path, out GitEntry? entry) &&
                (entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755")))
                throw new InvalidDataException("Python repository inputs require regular files.");
            if (!inputs.TryGetValue(path, out HashSet<string>? consumers))
                inputs.Add(path, consumers = new(StringComparer.Ordinal));
            if (owner is not null) consumers.Add(owner);
        }
    }

    private static bool Special(string target, string path) =>
        (target == "tests/eng/test_python_runtime.py" && path == "renovate.json") ||
        (target == "tests/private/app/workflow-delivery/Native/Python" &&
            (Under(path, "src/private/app/workflow-delivery/Native/Python") ||
             Under(path, "src/public/lib/nbgv-python"))) ||
        (target.StartsWith(AzureRoots[0] + "/", StringComparison.Ordinal) && Azure(path)) ||
        (target == "tests/eng/test_legacy_release_contract.py" &&
            CiScope.LegacyReleaseInput(path)) ||
        (target == "tests/eng/test_general_ci_workflow.py" &&
            path == "tests/eng/ci_workflow_shell.py") ||
        (target == "tests/eng/test_typos_config.py" && path == ".typos.toml") ||
        (target == "tests/eng/test_python_release_caller.py" &&
            path == ".github/workflows/workflow-delivery-v3-python-smoke.yml") ||
        (target.StartsWith("tests/eng/", StringComparison.Ordinal) &&
            path is "eng/scripts/sync_python_version.py" or
                "eng/scripts/workflow_delivery_v3_hk.py" or
                // Basis-only while this selector's removal is compared; retire with the next
                // change once no supported basis contains it.
                "eng/scripts/ci_scope.py");

    private static bool Azure(string path) =>
        DotNetInheritedInputs.Consumes(path, AzureRoots) ||
        Under(path, "eng/scripts/azureauth-credprovider") ||
        Under(path, "src/public/lib/nbgv-python");

    private static bool InertControlDocument(string path) =>
        path == "src/private/app/workflow-delivery/README.md" ||
        path.StartsWith("src/private/app/workflow-delivery/docs/", StringComparison.Ordinal);

    private static bool Under(string path, string root) =>
        !(root == "src/private/app/workflow-delivery" && InertControlDocument(path)) &&
        (path == root || path.StartsWith(root + "/", StringComparison.Ordinal));
}
