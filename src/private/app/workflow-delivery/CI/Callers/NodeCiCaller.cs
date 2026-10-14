using WorkflowDelivery.Repository;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.CI.Callers;

internal sealed record NodeCiCallerContext(MaterializedEndpoint Basis,
    MaterializedEndpoint Candidate, bool Full, string ScopePath, string Application);

internal static class NodeCiCaller
{
    internal static async Task<NodeCiCallerContext> MaterializeAsync(string root, string scopePath,
        string transfer, string directory, CancellationToken token)
    {
        string application = CiCallerTransfer.Application(transfer);
        CiCallerEndpoints endpoints = await CiCallerTransfer.MaterializeAsync(root, scopePath,
            directory, true, token);
        var context = new NodeCiCallerContext(endpoints.Basis, endpoints.Candidate, endpoints.Full,
            scopePath, application);
        CiCallerTransfer.Write(Path.Combine(directory, "context.json"), context,
            TransferJson.Default.NodeCiCallerContext);
        CiCallerTransfer.Output("basis_directory", context.Basis.Directory);
        CiCallerTransfer.Output("candidate_directory", context.Candidate.Directory);
        GitRevision basis = await new GitReader(root).ReadAsync(context.Basis.Reference, token);
        CiCallerTransfer.Output("basis_control", OwnerPresent(basis).ToString().ToLowerInvariant());
        return context;
    }

    internal static async Task<NodeGroupReadback> PlanAsync(string directory, string scopePath,
        string? rubyInputs, CancellationToken token)
    {
        NodeCiCallerContext context = Context(directory);
        GitRevision before = await new
            GitReader(context.Basis.Directory).ReadAsync(context.Basis.Reference, token);
        if (!OwnerPresent(await new
            GitReader(context.Candidate.Directory).ReadAsync(context.Candidate.Reference, token)))
            throw new InvalidDataException("The candidate must retain the control application.");
        DotNetReadFiles files(string name) => new(context.Application,
            Path.Combine(directory, name + "-control.proj"),
            Path.Combine(directory, name + "-control-request.json"),
            Path.Combine(directory, name + "-control-graph.json"), 900);
        var request = new NodeGroupRequest(context.Basis, context.Candidate, context.Full,
            scopePath,
            OwnerPresent(before) ? files("basis") : null, files("candidate"), rubyInputs is null
                ? null :
                CiCallerTransfer.Read(rubyInputs, TransferJson.Default.RubyNodeContribution));
        CiCallerTransfer.Write(Path.Combine(directory, "planning-request.json"), request,
            TransferJson.Default.NodeGroupRequest);
        NodeGroupReadback group = await NodeGroupReader.ReadAsync(request, token);
        CiCallerTransfer.Plan(group.Adopted, context.Basis.Reference, context.Candidate.Reference,
            NodeFactsAssembler.Scope);
        if (group.Runtime.Revision != context.Candidate.Reference)
            throw new InvalidDataException("Node runtime has a different original subject.");
        CiCallerTransfer.Write(Path.Combine(directory, "group-plan.json"), group,
            TransferJson.Default.NodeGroupReadback);
        CiCallerTransfer.Write(Path.Combine(directory, "parent-plan.json"), group.Adopted,
            TransferJson.Default.CiPlan);
        CiCallerTransfer.Output("retained", (group.Retained.Length !=
            0).ToString().ToLowerInvariant());
        CiCallerTransfer.OutputArray("retained_directories",
            group.Retained.Select(member => member.Directory).ToArray());
        CiCallerTransfer.Output("adopted_directory", group.AdoptedDirectory);
        CiCallerTransfer.Output("selected", (group.Adopted.Checks.Length !=
            0).ToString().ToLowerInvariant());
        return group;
    }

    // The caller owns source isolation. Native setup installs the returned runtime selector,
    // frozen PNPM dependencies and dotnet tools before ExecuteRuntimeAsync.
    internal static async Task<NodeRunRequest> RuntimeAsync(string root, string directory,
        string key, CancellationToken token)
    {
        NodeGroupReadback group = Group(directory);
        NodeCiCallerContext context = Context(directory);
        CiCallerTransfer.Plan(group.Adopted, context.Basis.Reference, context.Candidate.Reference,
            NodeFactsAssembler.Scope);
        NodeRuntimeVariant variant = Variant(group, key);
        if (group.Adopted.Checks.Length == 0)
            throw new InvalidDataException("Empty Node plan has no runtime execution.");
        string checkout = Path.Combine(directory, key);
        await CiCallerTransfer.RunAsync(new("git", root, ["worktree", "add", "--detach", checkout,
            group.Adopted.Candidate], 900), directory, key + "-checkout", token);
        var request = new NodeRunRequest(checkout,
            CiCallerTransfer.Fresh(Path.Combine(directory, key + "-scratch")), key);
        CiCallerTransfer.Write(Path.Combine(directory, key + "-request.json"), request,
            TransferJson.Default.NodeRunRequest);
        CiCallerTransfer.Output("checkout", checkout);
        CiCallerTransfer.Output("runtime_selector", "node@" + (variant.ExactVersion ??
            variant.Selector[..^2]));
        CiCallerTransfer.Output("runtime_locked", (variant.ExactVersion is not
            null).ToString().ToLowerInvariant());
        return request;
    }

    internal static async Task<CiOutcome> ExecuteRuntimeAsync(string directory, string key,
        CancellationToken token,
        Func<CiPlan, NodeRunRequest, CancellationToken, Task<NodeRunResult>>? execute = null)
    {
        NodeGroupReadback group = Group(directory);
        NodeCiCallerContext context = Context(directory);
        CiCallerTransfer.Plan(group.Adopted, context.Basis.Reference, context.Candidate.Reference,
            NodeFactsAssembler.Scope);
        _ = Variant(group, key);
        NodeRunRequest request = CiCallerTransfer.Read(Path.Combine(directory, key +
            "-request.json"),
            TransferJson.Default.NodeRunRequest);
        if (request.Runtime != key || request.Checkout != Path.Combine(directory, key) ||
            request.Scratch != Path.Combine(directory, key + "-scratch"))
            throw new
                InvalidDataException("Node runtime request has a different original subject.");
        NodeRunResult result = await (execute ?? NodeExecution.RunAsync)(group.Adopted, request,
            token);
        CiCallerTransfer.Write(Path.Combine(directory, key + "-result.json"), result,
            TransferJson.Default.NodeRunResult);
        if (result.Candidate != group.Adopted.Candidate || result.Runtime != key)
            throw new InvalidDataException("Runtime result has a different original subject.");
        return ResultCollector.Collect(NodeExecution.SelectRuntime(group.Adopted, key),
            result.Results);
    }

    internal static CiOutcome Join(string directory)
    {
        NodeGroupReadback group = Group(directory);
        NodeCiCallerContext context = Context(directory);
        CiCallerTransfer.Plan(group.Adopted, context.Basis.Reference, context.Candidate.Reference,
            NodeFactsAssembler.Scope);
        // Runtime absence is missing work, never success or a reason to run another variant.
        var sets = new List<NodeRunResult>();
        if (group.Adopted.Checks.Length != 0)
            foreach (NodeRuntimeVariant variant in group.Runtime.Variants)
            {
                string path = Path.Combine(directory, variant.Key + "-result.json");
                if (File.Exists(path)) sets.Add(CiCallerTransfer.Read(path,
                    TransferJson.Default.NodeRunResult));
            }
        (CheckResult[] results, CiOutcome outcome) = Join(group, sets.ToArray());
        CiCallerTransfer.Write(Path.Combine(directory, "results.json"), results,
            TransferJson.Default.CheckResultArray);
        CiCallerTransfer.Write(Path.Combine(directory, "outcome.json"), outcome,
            TransferJson.Default.CiOutcome);
        return outcome;
    }

    internal static (CheckResult[] Results, CiOutcome Outcome) Join(NodeGroupReadback group,
        NodeRunResult[] sets)
    {
        if (group.Runtime.Revision != group.Adopted.Candidate ||
            group.Runtime.Variants.Select(v => v.Key).Distinct(StringComparer.Ordinal).Count() !=
                group.Runtime.Variants.Length ||
            sets.Select(s => s.Runtime).Distinct(StringComparer.Ordinal).Count() != sets.Length)
            throw new
                InvalidDataException("Node runtime join has a different or repeated subject.");
        foreach (NodeRunResult result in sets)
        {
            _ = Variant(group, result.Runtime);
            CiPlan selected = NodeExecution.SelectRuntime(group.Adopted, result.Runtime);
            if (result.Candidate != group.Adopted.Candidate ||
                result.Results.Any(r => r.Candidate != group.Adopted.Candidate ||
                    r.Key.Variant != result.Runtime) ||
                ResultCollector.Collect(selected, result.Results).Errors.Length != 0)
                throw new
                    InvalidDataException("Node runtime result has a different original subject.");
        }
        CheckResult[] results = sets.SelectMany(result => result.Results).ToArray();
        return (results, ResultCollector.Collect(group.Adopted, results));
    }

    internal static async Task RetainedAsync(string root, string directory, string[] members,
        string adopted, string probe, string pnpm, CancellationToken token)
    {
        CiCallerTransfer.External(root, directory);
        foreach (string member in members.Append(adopted).Append(probe))
            ImpactPlanner.ValidatePath(member);
        if (members.Length == 0 || members.Distinct(StringComparer.Ordinal).Count() !=
            members.Length ||
            members.Contains(adopted))
            throw new
                InvalidDataException("Retained execution requires distinct " +
                    "selected native members.");
        CiCallerTransfer.Fresh(directory);
        string[] filters = [.. members.SelectMany(member => new[] { "--filter", "./" + member }),
            "--filter", "!./" + adopted];
        if (members.Contains(probe)) await Run("typecheck", ["--dir", probe, "run", "typecheck"]);
        await Run("test", [.. filters, "--recursive", "--if-present", "run", "test"]);
        await Run("build", [.. filters, "--recursive", "--if-present", "run", "build"]);
        if (members.Contains(probe)) await Run("packed", ["--dir", probe, "run",
            "validate:packed-artifact"]);
        Task<NativeCommandResult> Run(string label, string[] arguments) =>
            CiCallerTransfer.RunAsync(
            new(CiCallerTransfer.Absolute(pnpm), root, arguments, 900), directory, label, token);
    }

    private static bool OwnerPresent(GitRevision revision)
    {
        GitEntry? entry = revision.Entries.SingleOrDefault(e => e.Path ==
            NodeGroupReader.ControlProject);
        if (entry is not null && (entry.ObjectType != "blob" || entry.Mode is not ("100644" or
            "100755")))
            throw new
                InvalidDataException("The control owner must be a committed regular project.");
        return entry is not null;
    }

    private static NodeRuntimeVariant Variant(NodeGroupReadback group, string key)
    {
        NodeRuntimeVariant? variant = group.Runtime.Variants.SingleOrDefault(v => v.Key == key);
        if (variant is null || key.Contains('/') || key.Contains('\\') || key is "." or "..")
            throw new InvalidDataException("Unknown selected Node runtime.");
        return variant;
    }
    private static NodeCiCallerContext Context(string directory) => CiCallerTransfer.Read(
        Path.Combine(directory, "context.json"), TransferJson.Default.NodeCiCallerContext);
    private static NodeGroupReadback Group(string directory) => CiCallerTransfer.Read(
        Path.Combine(directory, "group-plan.json"), TransferJson.Default.NodeGroupReadback);
}
