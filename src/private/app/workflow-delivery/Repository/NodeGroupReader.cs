using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record NodeGroupRequest(MaterializedEndpoint Basis,
    MaterializedEndpoint Candidate, bool Full, string ScopePath,
    DotNetReadFiles? BasisControlFiles, DotNetReadFiles CandidateControlFiles);
internal sealed record NodeGroupReadback(CiPlan Adopted, RetainedNodeSelection[] Retained,
    string AdoptedDirectory, NodeRuntimeInputs Runtime);

// One concrete root-CI caller joins native consumption and endpoint responsibility.
internal static class NodeGroupReader
{
    internal const string ControlProject =
        "src/private/app/workflow-delivery/WorkflowDelivery.csproj";
    internal const string FixtureName = NodeRepositoryReader.FixtureName;
    internal static readonly string[] CallerSources = [".github/workflows/ci.yml",
        "src/private/app/workflow-delivery/CI/Callers/NodeCiCaller.cs",
        "src/private/app/workflow-delivery/CI/Callers/CiCallerCli.cs",
        "src/private/app/workflow-delivery/CI/Callers/CiCallerTransfer.cs",
        "eng/scripts/Bootstrap-CiControl.ps1", ".config/dotnet-tools.json",
        "eng/scripts/locked_tool_versions.py", "eng/dotnet-frameworks/mise.toml",
        "eng/dotnet-frameworks/mise.lock"];

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
        ControlInputFacts? basisControl = await ControlAsync(before,
            request.BasisControlFiles, false, token);
        ControlInputFacts candidateControl = await ControlAsync(after,
            request.CandidateControlFiles, true, token)
            ?? throw new InvalidDataException("Missing candidate control build.");
        NodeRuntimeInputs runtime = await new NodeRuntimeReader(after).ReadAsync(
            candidate.Graph.Projects.Select(project => project.Directory), token);
        basis = WithControlInputs(basis, basisControl, false);
        candidate = WithControlInputs(candidate, candidateControl, true);
        PnpmProject fixture = candidate.Graph.Projects.SingleOrDefault(project =>
            project.Name == FixtureName)
            ?? throw new InvalidDataException(
                "The adopted fixture has no native workspace identity.");
        NodeGroupPlan plan = await NodeGroupPlanning.PlanAsync(basis, candidate, scope,
            fixture.Directory, candidateReader.ScriptsAsync,
            candidateReader.NpmVersionAsync, token);
        return new(runtime.Expand(plan.Adopted), plan.Retained, fixture.Directory, runtime);
    }

    internal static NodeRevisionInputs WithControlInputs(NodeRevisionInputs inputs,
        ControlInputFacts? control, bool candidate)
    {
        GitEntry? owner = inputs.Revision.Entries.SingleOrDefault(entry =>
            entry.Path == ControlProject);
        if ((owner is not null && (owner.ObjectType != "blob" ||
            owner.Mode is not ("100644" or "100755"))) ||
            (owner is null ? control is not null || candidate : control is null))
            throw new InvalidDataException("Unresolved native control owner or build.");
        if (control is not null && control.Revision != inputs.Revision.Commit)
            throw new InvalidDataException("Control inputs have a different endpoint revision.");
        string[] consumers = inputs.Graph.Projects.Where(project => project.Directory != ".")
            .Select(project => project.Directory).ToArray();
        var shared = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (SharedInput input in inputs.OperationInputs)
            GitPathOwnership.AddSharedInput(shared, input.Path, input.Consumers);
        foreach (ControlSourceInput input in control?.Sources ?? [])
            GitPathOwnership.AddSharedInput(shared, input.Path, consumers);
        // Release-schedule data and every engines manifest plan one matrix for all members.
        string[] runtime = [NodeRuntimeReader.Schedule, .. NodeRuntimeReader.ManifestPaths(
            inputs.Graph.Projects.Select(project => project.Directory))];
        foreach (string path in CallerSources.Concat(runtime))
        {
            GitEntry? entry = inputs.Revision.Entries.SingleOrDefault(entry => entry.Path == path);
            if (entry is not null && (entry.ObjectType != "blob" ||
                entry.Mode is not ("100644" or "100755")))
                throw new InvalidDataException("Unsupported Node group caller input: " + path);
            GitPathOwnership.AddSharedInput(shared, path, consumers);
        }
        return inputs with { OperationInputs = GitPathOwnership.GroupSharedInputs(shared) };
    }

    internal static async Task<ControlInputFacts?> ControlAsync(GitMaterialization checkout,
        DotNetReadFiles? files, bool required, CancellationToken token)
    {
        bool present = checkout.Revision.Entries.Any(entry => entry.Path == ControlProject);
        if (!present)
        {
            if (required || files is not null)
                throw new InvalidDataException("Missing or substituted control input context.");
            return null;
        }
        if (files is null)
            throw new InvalidDataException("Missing native control input preparation.");
        DotNetRevisionInputs inputs = await new DotNetRepositoryReader(checkout, files,
            entryProject: ControlProject).ReadAsync(token);
        return ControlInputs.Read(inputs, required);
    }
}

internal sealed record MaterializedEndpoint(string Directory, string Reference);
internal sealed record NodePlanRequest(MaterializedEndpoint Basis, MaterializedEndpoint Candidate,
    bool Full);

internal static class NodePlanning
{
    internal static async Task<CiPlan> PlanAsync(NodePlanRequest request, CancellationToken token)
    {
        string beforeRoot = Path.GetFullPath(request.Basis.Directory);
        string afterRoot = Path.GetFullPath(request.Candidate.Directory);
        if (Path.TrimEndingDirectorySeparator(beforeRoot) ==
            Path.TrimEndingDirectorySeparator(afterRoot))
            throw new InvalidDataException("Node endpoints require separate exact checkouts.");
        GitComparison comparison = await new GitReader(afterRoot).CompareAsync(
            request.Basis.Reference, request.Candidate.Reference, token);
        GitMaterialization before = await GitMaterialization.BindAsync(beforeRoot,
            comparison.Basis, token);
        GitMaterialization after = await GitMaterialization.BindAsync(afterRoot,
            comparison.Candidate, token);
        var basis = new NodeRepositoryReader(before);
        var candidate = new NodeRepositoryReader(after);
        NodeRevisionInputs basisInputs = await basis.ReadAsync(token);
        NodeRevisionInputs candidateInputs = await candidate.ReadAsync(token);
        return await NodeFactsAssembler.PlanAsync(basisInputs, candidateInputs,
            comparison.ChangedPaths, request.Full, candidate.ScriptsAsync,
            candidate.NpmVersionAsync, token);
    }
}

internal sealed record RetainedNodeSelection(string Directory, SelectionReason[] Reasons);
internal sealed record NodeGroupPlan(CiPlan Adopted, RetainedNodeSelection[] Retained);

// A missing replacement preset preserves retained execution; it never erases native impact.
internal static class NodeGroupPlanning
{
    internal static async Task<NodeGroupPlan> PlanAsync(NodeRevisionInputs basis,
        NodeRevisionInputs candidate, ScopeInputs scope, string? adoptedDirectory,
        Func<PnpmProject, CancellationToken, Task<PnpmScripts>> scripts,
        Func<PnpmProject, CancellationToken, Task<string>> npmVersion,
        CancellationToken token)
    {
        PlanRequest request = scope.Bind(NodeFactsAssembler.Assemble(basis),
            NodeFactsAssembler.Assemble(candidate));
        if (adoptedDirectory is not null && !request.Candidate.Projects.Any(project =>
            project.Directory == adoptedDirectory))
            throw new InvalidDataException("Adopted Node identity has no native member.");
        Dictionary<string, HashSet<SelectionReason>> selected =
            ImpactPlanner.SelectProjects(request);
        var adopted = new Dictionary<string, HashSet<SelectionReason>>(StringComparer.Ordinal);
        var retained = new List<RetainedNodeSelection>();
        foreach (ProjectFacts project in request.Candidate.Projects)
        {
            token.ThrowIfCancellationRequested();
            if (!selected.TryGetValue(project.Id, out HashSet<SelectionReason>? reasons))
                continue;
            if (project.Directory == adoptedDirectory)
                adopted.Add(project.Id, reasons);
            else
                retained.Add(new(project.Directory, reasons.OrderBy(reason => reason.Path,
                    StringComparer.Ordinal).ThenBy(reason => reason.Revision,
                    StringComparer.Ordinal).ThenBy(reason => reason.Project,
                    StringComparer.Ordinal).ToArray()));
        }
        CiPlan plan = await NodeFactsAssembler.CompleteAsync(candidate, request, adopted,
            scripts, npmVersion, token);
        return new(plan, retained.OrderBy(member => member.Directory,
            StringComparer.Ordinal).ToArray());
    }
}
