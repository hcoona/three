using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record DotNetGroupRequest(MaterializedEndpoint Basis,
    MaterializedEndpoint Candidate, bool Full,
    DotNetReadFiles BasisFiles, DotNetReadFiles CandidateFiles,
    DotNetRunRequest Completion);
internal sealed record DotNetGroupReadback(CiPlan Plan, ScopeCoordinate[] Basis,
    ScopeCoordinate[] Candidate);

// The root caller joins actual native responsibilities before the shared scope binder.
internal static class DotNetGroupReader
{
    internal static readonly string[] CallerSources = [".github/workflows/ci.yml",
        "eng/scripts/Bootstrap-CiControl.ps1",
        "src/private/app/workflow-delivery/CI/Callers/DotNetCiCaller.cs",
        "src/private/app/workflow-delivery/CI/Callers/NodeCiCaller.cs",
        ".config/dotnet-tools.json", "src/private/app/workflow-delivery/CI/Callers/CiCallerCli.cs",
        "src/private/app/workflow-delivery/CI/Callers/CiCallerTransfer.cs",
        "src/private/app/workflow-delivery/CI/Callers/ControlCiCaller.cs",
        "mise.toml", "mise.lock"];

    internal static async Task<DotNetGroupReadback> ReadAsync(DotNetGroupRequest request,
        ScopeInputs scope, CancellationToken token)
    {
        GitComparison comparison = await new GitReader(request.Candidate.Directory).CompareAsync(
            request.Basis.Reference, request.Candidate.Reference, token);
        GitMaterialization basis = await GitMaterialization.BindAsync(request.Basis.Directory,
            comparison.Basis, token);
        GitMaterialization candidate = await GitMaterialization.BindAsync(
            request.Candidate.Directory,
            comparison.Candidate, token);
        if (scope.Comparison != new CiComparison(comparison.Basis.Commit,
                comparison.Candidate.Commit, request.Full))
            throw new InvalidDataException("Scope has a different native comparison.");
        DotNetRevisionInputs before = await new DotNetRepositoryReader(basis,
            request.BasisFiles).ReadAsync(token);
        DotNetRevisionInputs after = comparison.Basis.Commit == comparison.Candidate.Commit &&
            basis.Root == candidate.Root && request.BasisFiles == request.CandidateFiles ? before :
            await new DotNetRepositoryReader(candidate, request.CandidateFiles).ReadAsync(token);
        ControlInputFacts? controlBasis = ControlInputs.Read(before, false);
        ControlInputFacts controlCandidate = ControlInputs.Read(after, true) ??
            throw new InvalidDataException("Missing candidate control inputs.");
        NodeRevisionInputs nodeBefore = NodeGroupReader.WithControlInputs(
            await NodeReader(basis).ReadAsync(token), controlBasis, false);
        NodeRevisionInputs nodeAfter = NodeGroupReader.WithControlInputs(
            await NodeReader(candidate).ReadAsync(token), controlCandidate, true);
        before = WithControlInputs(before, controlBasis, false);
        after = WithControlInputs(after, controlCandidate, true);
        RepositoryFacts dotnetBefore = DotNetFactsAssembler.Assemble(before, []);
        RepositoryFacts dotnetAfter = DotNetFactsAssembler.Assemble(after, []);
        ScopeInputs joined = Join(scope, comparison, dotnetBefore, dotnetAfter,
            NodeFactsAssembler.Assemble(nodeBefore), NodeFactsAssembler.Assemble(nodeAfter));
        PlanRequest bound = joined.Bind(dotnetBefore, dotnetAfter);
        CiPlan plan = await DotNetFactsAssembler.CompleteAsync(
            DotNetFactsAssembler.Select(bound, after), request.Completion, token);
        return new(plan, joined.Basis, joined.Candidate);
    }

    private static NodeRepositoryReader NodeReader(GitMaterialization endpoint) =>
        new(endpoint, (arguments, token) => NativeProcess.RunAsync("mise", endpoint.Root,
            ["exec", "--locked", "node", "pnpm", "--", "pnpm", .. arguments], token));

    internal static ScopeInputs Join(ScopeInputs scope, GitComparison comparison,
        RepositoryFacts dotnetBasis, RepositoryFacts dotnetCandidate,
        RepositoryFacts nodeBasis, RepositoryFacts nodeCandidate)
    {
        // Validate complete relations, including unselected and removed members.
        ImpactPlanner.SelectProjects(new(dotnetBasis, dotnetCandidate, [], false));
        ImpactPlanner.SelectProjects(new(nodeBasis, nodeCandidate, [], false));
        return scope.WithResponsibilities(comparison,
            Endpoint(scope.Basis, comparison.Basis, dotnetBasis, nodeBasis),
            Endpoint(scope.Candidate, comparison.Candidate, dotnetCandidate, nodeCandidate));

        static ScopeCoordinate[] Endpoint(ScopeCoordinate[] rows, GitRevision revision,
            RepositoryFacts dotnet, RepositoryFacts node)
        {
            if (dotnet.Revision != revision.Commit || node.Revision != revision.Commit)
                throw new InvalidDataException("Native responsibility has a different endpoint.");
            return rows.Select(row => row.Present ? row with
            {
                Reasons = [.. row.Reasons, .. Reasons(dotnet, "dotnet", row.Path),
                    .. Reasons(node, "node", row.Path)],
            } : row).ToArray();

            ScopeResponsibility[] Reasons(RepositoryFacts facts, string owner, string path) =>
                ImpactPlanner.DirectConsumers(facts, path).Order(StringComparer.Ordinal)
                    .Select(id =>
                    {
                        ProjectFacts project = facts.Projects.Single(project => project.Id == id);
                        string source = owner == "dotnet" ? project.Id :
                            project.Directory + "/package.json";
                        string[] origins = new[] { source, path }.Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal).ToArray();
                        return new ScopeResponsibility(owner, id, "native-consumer", origins);
                    }).ToArray();
        }
    }

    internal static DotNetRevisionInputs WithControlInputs(DotNetRevisionInputs inputs,
        ControlInputFacts? control, bool candidate)
    {
        bool owner = inputs.Revision.Entries.Any(entry => entry.Path ==
            NodeGroupReader.ControlProject);
        if ((owner ? control is null : control is not null || candidate) ||
            (control is not null && control.Revision != inputs.Revision.Commit))
            throw new InvalidDataException("Unresolved native .NET control owner or build.");
        string[] consumers = inputs.Nodes.Where(node => node.Project != "dirs.proj")
            .Select(node => node.Project).Distinct(StringComparer.Ordinal).ToArray();
        var shared = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (SharedInput input in inputs.SelectionInputs) Add(input.Path, input.Consumers);
        foreach (ControlSourceInput input in control?.Sources ?? []) Add(input.Path, consumers);
        foreach (string path in CallerSources)
        {
            GitEntry? entry = inputs.Revision.Entries.SingleOrDefault(entry => entry.Path == path);
            if (entry is not null && (entry.ObjectType != "blob" ||
                entry.Mode is not ("100644" or "100755")))
                throw new InvalidDataException("Unsupported .NET group caller input: " + path);
            Add(path, consumers);
        }
        return inputs with
        {
            SelectionInputs = shared.OrderBy(pair => pair.Key,
            StringComparer.Ordinal).Select(pair => new SharedInput(pair.Key,
                pair.Value.Order(StringComparer.Ordinal).ToArray())).ToArray()
        };

        void Add(string path, string[] owners)
        {
            ImpactPlanner.ValidatePath(path);
            if (!shared.TryGetValue(path, out HashSet<string>? values))
                shared.Add(path, values = new(StringComparer.Ordinal));
            values.UnionWith(owners);
        }
    }

}
