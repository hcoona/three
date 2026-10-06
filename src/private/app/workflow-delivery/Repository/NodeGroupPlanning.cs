using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

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
