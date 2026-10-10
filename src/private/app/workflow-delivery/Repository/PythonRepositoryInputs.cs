using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

// Repository engineering inputs select actual supplied checks, never a native target roster.
internal static class PythonRepositoryInputs
{
    private static readonly string[] Shared = [
        ".github/workflows/ci.yml", "eng/scripts/ci_scope.py",
        "eng/scripts/repository_path_patterns.py", "eng/scripts/prepare_ci_control_inputs.py",
        "eng/scripts/run_python_ci_group.py",
        "mise.toml", "mise.lock"];
    private static readonly string[] Preparation = [
        "pyproject.toml", "uv.lock", "uv.toml", ".python-version", ".config/uv/uv.toml",
        "eng/scripts/sync_python_version.py"];
    private static readonly string[] Pytest = [
        "conftest.py", "pytest.ini", "eng/scripts/run_python_tests.py"];
    private static readonly string[] Dotnet = [
        ".editorconfig", "global.json", "NuGet.Config", "nuget.config",
        ".config/dotnet-tools.json", "version.json", "Directory.Build.props",
        "Directory.Build.targets", "Directory.Packages.props", "dirs.proj"];
    private static readonly string[] Node = [
        "package.json", "pnpm-lock.yaml", "pnpm-workspace.yaml", ".npmrc",
        "version.json", ".config/dotnet-tools.json", "global.json"];
    private static readonly string[] V3Roots = [
        "src/private/app/workflow-delivery-v3-dotnet-provider",
        "src/private/app/workflow-delivery-v3-nuget-authority",
        "src/private/app/workflow-delivery-v3-nuget-consumer",
        "tests/private/app/workflow-delivery-v3-nuget-authority",
        "tests/private/app/workflow-delivery-v3-nuget-consumer",
        "src/public/lib/hcoona-release-smoke-github-packages"];
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
        foreach (PythonPytestTarget target in targets.Targets)
        {
            string owner = "pytest:" + target.Target;
            foreach (string path in Dotnet)
                if (target.Target.StartsWith("src/public/lib/nbgv-python/",
                        StringComparison.Ordinal) ||
                    target.Target.StartsWith("src/public/lib/three-workflow-delivery-v3/",
                        StringComparison.Ordinal) ||
                    target.Target.StartsWith(AzureRoots[0] + "/", StringComparison.Ordinal))
                    Add(path, owner);
            foreach (string path in paths)
                if (Special(target.Target, path)) Add(path, owner);
        }
        return inputs.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new PythonInputBinding(item.Key, [],
                item.Value.Order(StringComparer.Ordinal).ToArray())).ToArray();

        void Add(string path, string owner)
        {
            ImpactPlanner.ValidatePath(path);
            if (entries.TryGetValue(path, out GitEntry? entry) &&
                (entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755")))
                throw new InvalidDataException("Python repository inputs require regular files.");
            if (!inputs.TryGetValue(path, out HashSet<string>? consumers))
                inputs.Add(path, consumers = new(StringComparer.Ordinal));
            consumers.Add(owner);
        }
    }

    private static bool Special(string target, string path) =>
        (target == "tests/private/app/workflow-delivery/Native/Python" &&
            (Under(path, "src/private/app/workflow-delivery/Native/Python") ||
             Under(path, "src/public/lib/nbgv-python"))) ||
        (target.StartsWith("src/public/lib/three-workflow-delivery-v3/",
            StringComparison.Ordinal) && V3(path)) ||
        (target.StartsWith(AzureRoots[0] + "/", StringComparison.Ordinal) && Azure(path)) ||
        (target == "tests/eng/test_legacy_release_contract.py" && Legacy(path)) ||
        (target == "tests/eng/test_typos_config.py" && path == ".typos.toml") ||
        (target == "tests/eng/test_run_node_ci_group.py" &&
            path == "eng/scripts/run_node_ci_group.py") ||
        (target == "tests/eng/test_run_dotnet_ci_group.py" &&
            path is "eng/scripts/run_dotnet_ci_group.py" or "eng/scripts/run_node_ci_group.py") ||
        (target == "tests/eng/test_run_python_ci_group.py" &&
            path is "eng/scripts/run_dotnet_ci_group.py" or "eng/scripts/run_node_ci_group.py") ||
        (target.StartsWith("tests/eng/", StringComparison.Ordinal) &&
            path is "eng/scripts/ci_scope.py" or "eng/scripts/sync_python_version.py" or
                "eng/scripts/workflow_delivery_v3_hk.py");

    private static bool Azure(string path) => DotnetInput(path, AzureRoots) ||
        Under(path, "eng/scripts/azureauth-credprovider") ||
        Under(path, "src/public/lib/nbgv-python");

    private static bool V3(string path) => DotnetInput(path, V3Roots) ||
        Node.Contains(path, StringComparer.Ordinal) ||
        path is "hk.pkl" or ".gitignore" or ".gitattributes" or ".github/CODEOWNERS" or
            "eng/scripts/hk_exec.py" or "eng/scripts/hk_file_operands.py" or
            "eng/scripts/hk_actionlint.py" or "eng/scripts/hk_pkl_eval.py" ||
        path.StartsWith(".github/workflows/workflow-delivery-v3-", StringComparison.Ordinal) ||
        path.StartsWith(".github/actions/workflow-delivery-v3", StringComparison.Ordinal) ||
        Under(path, ".github/workflow-delivery") || Under(path, "eng/workflow-delivery/v3") ||
        path.StartsWith("eng/scripts/workflow_delivery_v3_", StringComparison.Ordinal) ||
        Under(path, "src/private/lib/hk") ||
        path.StartsWith("tests/private/app/workflow-delivery-v3-", StringComparison.Ordinal) ||
        path.StartsWith("src/public/lib/hcoona-release-smoke-", StringComparison.Ordinal);

    private static bool DotnetInput(string path, string[] roots)
    {
        if (Dotnet.Contains(path, StringComparer.Ordinal)) return true;
        int separator = path.LastIndexOf('/');
        string name = path[(separator + 1)..];
        string directory = separator < 0 ? "" : path[..separator];
        bool inherited = name is ".editorconfig" or "Directory.Build.props" or
            "Directory.Build.targets" or "Directory.Packages.props" or "version.json" or
            "NuGet.Config" or "nuget.config" or "global.json";
        return roots.Any(root => Under(path, root) ||
            (inherited && (directory.Length == 0 || Under(root, directory))));
    }

    private static bool Legacy(string path) =>
        Under(path, ".github/workflows") || Under(path, "eng/release") ||
        path.StartsWith("src/public/lib/hcoona-release-smoke", StringComparison.Ordinal) ||
        path.StartsWith("src/public/lib/three-workflow-release-", StringComparison.Ordinal) ||
        path.StartsWith("tests/fixtures/workflow-release-", StringComparison.Ordinal) ||
        path.StartsWith("tests/test_workflow_release_", StringComparison.Ordinal) ||
        path.EndsWith("/three.release.yml", StringComparison.Ordinal) ||
        path.EndsWith("/three.quality.yml", StringComparison.Ordinal) ||
        path is ".github/actionlint.yaml" or "eng/release" or
            "eng/scripts/publish_node_gpr_idempotent.sh" or
            "eng/scripts/publish_node_npmjs_idempotent.sh" or
            "eng/scripts/release_orchestrate_lint_caller_completeness.sh" or
            "eng/scripts/verify_python_distribution_exactness.py" or
            "eng/scripts/workflow_release_acceptance_gate.py" or
            "eng/scripts/workflow_release_control.py";

    private static bool Under(string path, string root) =>
        path == root || path.StartsWith(root + "/", StringComparison.Ordinal);
}
