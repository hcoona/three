using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record NodeGroupRequest(MaterializedEndpoint Basis,
    MaterializedEndpoint Candidate, bool Full, string ScopePath,
    string? BasisBuildLog, string CandidateBuildLog);
internal sealed record NodeGroupReadback(CiPlan Adopted, RetainedNodeSelection[] Retained,
    string AdoptedDirectory, NodeRuntimeInputs Runtime, ControlBuildConsumption? BasisControl,
    ControlBuildConsumption CandidateControl);

// One concrete root-CI caller joins native consumption and endpoint responsibility.
internal static class NodeGroupReader
{
    internal const string ControlProject =
        "src/private/app/workflow-delivery/WorkflowDelivery.csproj";
    internal const string FixtureName = NodeRepositoryReader.FixtureName;
    internal static readonly string[] CallerSources = [".github/workflows/ci.yml",
        "eng/scripts/ci_scope.py", "eng/scripts/run_node_ci_group.py",
        "eng/scripts/repository_path_patterns.py", "eng/scripts/workflow_delivery_v3_hk.py",
        "pyproject.toml", "uv.lock", "uv.toml", ".python-version", ".config/uv/uv.toml",
        ".config/dotnet-tools.json"];

    internal static async Task<NodeGroupReadback> ReadAsync(NodeGroupRequest request,
        CancellationToken token)
    {
        string beforeRoot = Path.GetFullPath(request.Basis.Directory);
        string afterRoot = Path.GetFullPath(request.Candidate.Directory);
        if (Path.TrimEndingDirectorySeparator(beforeRoot) ==
            Path.TrimEndingDirectorySeparator(afterRoot))
            throw new InvalidDataException(
                "Node group endpoints require separate exact checkouts.");
        GitComparison comparison = await new GitReader(afterRoot).CompareAsync(
            request.Basis.Reference, request.Candidate.Reference, token);
        GitMaterialization before = await GitMaterialization.BindAsync(beforeRoot,
            comparison.Basis, token);
        GitMaterialization after = await GitMaterialization.BindAsync(afterRoot,
            comparison.Candidate, token);
        ScopeInputs scope = ScopeInputs.Read(await File.ReadAllTextAsync(
            request.ScopePath, token), comparison, request.Full);
        var basisReader = new NodeRepositoryReader(before);
        var candidateReader = new NodeRepositoryReader(after);
        NodeRevisionInputs basis = await basisReader.ReadAsync(token);
        NodeRevisionInputs candidate = await candidateReader.ReadAsync(token);
        ControlBuildConsumption? basisControl = await ControlAsync(before,
            request.BasisBuildLog, false, token);
        ControlBuildConsumption candidateControl = await ControlAsync(after,
            request.CandidateBuildLog, true, token)
            ?? throw new InvalidDataException("Missing candidate control build.");
        NodeRuntimeInputs runtime = await new NodeRuntimeReader(after).ReadAsync(token);
        basis = WithControlInputs(basis, basisControl, false);
        candidate = WithControlInputs(candidate, candidateControl, true);
        PnpmProject fixture = candidate.Graph.Projects.SingleOrDefault(project =>
            project.Name == FixtureName)
            ?? throw new InvalidDataException(
                "The adopted fixture has no native workspace identity.");
        NodeGroupPlan plan = await NodeGroupPlanning.PlanAsync(basis, candidate, scope,
            fixture.Directory, candidateReader.ScriptsAsync,
            candidateReader.NpmVersionAsync, token);
        return new(runtime.Expand(plan.Adopted), plan.Retained, fixture.Directory, runtime,
            basisControl, candidateControl);
    }

    internal static NodeRevisionInputs WithControlInputs(NodeRevisionInputs inputs,
        ControlBuildConsumption? control, bool candidate)
    {
        GitEntry? owner = inputs.Revision.Entries.SingleOrDefault(entry =>
            entry.Path == ControlProject);
        if ((owner is not null && (owner.ObjectType != "blob" ||
            owner.Mode is not ("100644" or "100755"))) ||
            (owner is null ? control is not null || candidate : control is null))
            throw new InvalidDataException("Unresolved native control owner or build.");
        if (control is not null && control.Operation.Revision != inputs.Revision.Commit)
            throw new InvalidDataException("Control inputs have a different endpoint revision.");
        string[] consumers = inputs.Graph.Projects.Where(project => project.Directory != ".")
            .Select(project => project.Directory).ToArray();
        var shared = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (SharedInput input in inputs.OperationInputs)
            Add(input.Path, input.Consumers);
        foreach (ControlSourceInput input in control?.Sources ?? [])
            Add(input.Path, consumers);
        foreach (string path in CallerSources)
        {
            GitEntry? entry = inputs.Revision.Entries.SingleOrDefault(entry => entry.Path == path);
            if (entry is not null && (entry.ObjectType != "blob" ||
                entry.Mode is not ("100644" or "100755")))
                throw new InvalidDataException("Unsupported Node group caller input: " + path);
            Add(path, consumers);
        }
        return inputs with
        {
            OperationInputs = shared.OrderBy(input => input.Key, StringComparer.Ordinal)
                .Select(input => new SharedInput(input.Key,
                    input.Value.Order(StringComparer.Ordinal).ToArray())).ToArray(),
        };

        void Add(string path, string[] owners)
        {
            ImpactPlanner.ValidatePath(path);
            if (!shared.TryGetValue(path, out HashSet<string>? values))
                shared.Add(path, values = new(StringComparer.Ordinal));
            values.UnionWith(owners);
        }
    }

    internal static async Task<ControlBuildConsumption?> ControlAsync(GitMaterialization checkout,
        string? binlog, bool required, CancellationToken token)
    {
        bool present = checkout.Revision.Entries.Any(entry => entry.Path == ControlProject);
        if (!present)
        {
            if (required || binlog is not null)
                throw new InvalidDataException("Missing or substituted control operation.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(binlog))
            throw new InvalidDataException("Missing native control build log.");
        ControlBuildContext operation = await new ControlBuildContextReader(checkout)
            .ReadAsync(ControlProject, token);
        ControlBuildLog log = ControlBuildLogReader.Read(Path.GetFullPath(binlog),
            operation.Project, token);
        ControlBuildDependencies dependencies = ControlBuildDependencyReader.Read(operation, log);
        NbgvInputs version = new NbgvInputReader(checkout.Root).Read(checkout.Revision,
            "src/private/app/workflow-delivery", token);
        return ControlBuildInputProjection.Project(checkout, operation, log, dependencies,
            version.ConfigurationCandidates);
    }
}
