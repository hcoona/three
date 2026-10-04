using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

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
