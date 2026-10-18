using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record DotNetGroupRequest(MaterializedEndpoint Basis,
    MaterializedEndpoint Candidate, bool Full,
    DotNetReadFiles BasisFiles, DotNetReadFiles CandidateFiles,
    DotNetRunRequest Completion);
// Endpoint facts from native readers alone, before control-run inputs are joined.
internal sealed record NativeGroupFacts(RepositoryFacts DotNetBasis,
    RepositoryFacts DotNetCandidate, RepositoryFacts NodeBasis, RepositoryFacts NodeCandidate);
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
        return await ReadAsync(request, scope, comparison, basis, candidate, token);
    }

    // The caller has already compared and bound both request endpoints in this invocation.
    internal static async Task<DotNetGroupReadback> ReadAsync(DotNetGroupRequest request,
        ScopeInputs scope, GitComparison comparison, GitMaterialization basis,
        GitMaterialization candidate, CancellationToken token)
    {
        // Earlier native queries may have changed tracked files; re-check before evaluation.
        basis = await GitMaterialization.ReuseOrBindAsync(basis, request.Basis.Directory,
            comparison.Basis.Commit, token);
        candidate = await GitMaterialization.ReuseOrBindAsync(candidate,
            request.Candidate.Directory, comparison.Candidate.Commit, token);
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
        NodeRevisionInputs nodeBefore = await NodeReader(basis).ReadAsync(token);
        NodeRevisionInputs nodeAfter = await NodeReader(candidate).ReadAsync(token);
        // Native facts before the application-defined control-run inputs are joined.
        var native = new NativeGroupFacts(DotNetFactsAssembler.Assemble(before, []),
            DotNetFactsAssembler.Assemble(after, []), NodeFactsAssembler.Assemble(nodeBefore),
            NodeFactsAssembler.Assemble(nodeAfter));
        nodeBefore = NodeGroupReader.WithControlInputs(nodeBefore, controlBasis, false);
        nodeAfter = NodeGroupReader.WithControlInputs(nodeAfter, controlCandidate, true);
        before = WithControlInputs(before, controlBasis, false);
        after = WithControlInputs(after, controlCandidate, true);
        RepositoryFacts dotnetBefore = DotNetFactsAssembler.Assemble(before, []);
        RepositoryFacts dotnetAfter = DotNetFactsAssembler.Assemble(after, []);
        ScopeInputs joined = Join(scope, comparison, dotnetBefore, dotnetAfter,
            NodeFactsAssembler.Assemble(nodeBefore), NodeFactsAssembler.Assemble(nodeAfter),
            native);
        PlanRequest bound = joined.Bind(dotnetBefore, dotnetAfter);
        CiPlan plan = await DotNetFactsAssembler.CompleteAsync(
            DotNetFactsAssembler.Select(bound, after), request.Completion, token);
        return new(plan, joined.Basis, joined.Candidate);
    }

    private static NodeRepositoryReader NodeReader(GitMaterialization endpoint) =>
        new(endpoint, (arguments, token) => NativeProcess.RunAsync("mise", endpoint.Root,
            ["exec", "--locked", "node", "pnpm", "--", "pnpm", .. arguments], token));

    // Planning facts include the control-run inputs; native facts omit them. A consumer reached
    // only through a control-run input is reported as that application-defined relation.
    internal static ScopeInputs Join(ScopeInputs scope, GitComparison comparison,
        RepositoryFacts dotnetBasis, RepositoryFacts dotnetCandidate,
        RepositoryFacts nodeBasis, RepositoryFacts nodeCandidate,
        NativeGroupFacts? native = null)
    {
        native ??= new(dotnetBasis, dotnetCandidate, nodeBasis, nodeCandidate);
        // Validate complete relations, including unselected and removed members.
        ImpactPlanner.SelectProjects(new(dotnetBasis, dotnetCandidate, [], false));
        ImpactPlanner.SelectProjects(new(nodeBasis, nodeCandidate, [], false));
        return scope.WithResponsibilities(comparison,
            Endpoint(scope.Basis, comparison.Basis, dotnetBasis, native.DotNetBasis, nodeBasis,
                native.NodeBasis),
            Endpoint(scope.Candidate, comparison.Candidate, dotnetCandidate,
                native.DotNetCandidate, nodeCandidate, native.NodeCandidate));

        static ScopeCoordinate[] Endpoint(ScopeCoordinate[] rows, GitRevision revision,
            RepositoryFacts dotnet, RepositoryFacts dotnetNative, RepositoryFacts node,
            RepositoryFacts nodeNative)
        {
            if (new[] { dotnet, dotnetNative, node, nodeNative }.Any(facts =>
                    facts.Revision != revision.Commit))
                throw new InvalidDataException("Native responsibility has a different endpoint.");
            bool control = revision.Entries.Any(entry =>
                entry.Path == NodeGroupReader.ControlProject);
            return rows.Select(row => row.Present ? row with
            {
                Reasons = [.. row.Reasons, .. Reasons(dotnet, dotnetNative, "dotnet", row.Path),
                    .. Reasons(node, nodeNative, "node", row.Path)],
            } : row).ToArray();

            ScopeResponsibility[] Reasons(RepositoryFacts facts, RepositoryFacts nativeFacts,
                string owner, string path)
            {
                var consumers = ImpactPlanner.DirectConsumers(nativeFacts, path)
                    .ToHashSet(StringComparer.Ordinal);
                return ImpactPlanner.DirectConsumers(facts, path).Order(StringComparer.Ordinal)
                    .Select(id =>
                    {
                        ProjectFacts project = facts.Projects.Single(project => project.Id == id);
                        if (!consumers.Contains(id))
                            return new ScopeResponsibility(owner, id, "control-run-input",
                                Sources(control ? NodeGroupReader.ControlProject : path, path));
                        string source = owner == "dotnet" ? project.Id :
                            project.Directory + "/package.json";
                        return new ScopeResponsibility(owner, id, "native-consumer",
                            Sources(source, path));
                    }).ToArray();
            }

            static string[] Sources(string source, string path) => new[] { source, path }
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
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
