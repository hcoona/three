using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record ControlSourceInput(string Path, string Role, bool Present);
internal sealed record ControlInputFacts(string Revision, string Project,
    ControlSourceInput[] Sources);

// Bind maintained native evaluation and explicit control consumers, without interpreting build
// tasks.
internal static class ControlInputs
{
    internal static ControlInputFacts? Read(DotNetRevisionInputs inputs, bool required)
    {
        DotNetBoundNode[] control = inputs.Nodes.Where(node =>
            node.Project == NodeGroupReader.ControlProject && !node.Native.OuterBuild).ToArray();
        if (control.Length == 0 && !required) return null;
        if (control.Length != 1)
            throw new InvalidDataException("The control requires one complete native dimension.");
        var paths = inputs.Nodes.Where(node => node.Project == NodeGroupReader.ControlProject)
            .SelectMany(node => node.OwnedPaths.Select(path =>
                new ControlSourceInput(path, "ControlSource", true))).ToList();
        foreach (DotNetBoundInput input in inputs.Inputs.Where(input => input.Path is not null &&
                     input.Native.Consumer.Project == control[0].Native.Identity.Project))
            paths.Add(new(input.Path!, input.Native.Role,
                inputs.Revision.Entries.Any(entry => entry.Path == input.Path)));
        if (inputs.Graph.Edges.Any(edge =>
                edge.Consumer.Project == control[0].Native.Identity.Project &&
                edge.Dependency.Project != control[0].Native.Identity.Project))
            throw new InvalidDataException(
                "Control project dependencies require a concrete adapter.");
        // Native NBGV distribution is a tool exception. Its configuration is consumed;
        // the control application's unused self-version does not add product source consumers.
        var versionSources = inputs.Versions.SelectMany(version => version.Paths)
            .ToHashSet(StringComparer.Ordinal);
        var versionConfiguration = inputs.Versions.SelectMany(
            version => version.ConfigurationCandidates)
            .ToHashSet(StringComparer.Ordinal);
        foreach (SharedInput input in inputs.SelectionInputs.Where(input =>
                     input.Consumers.Contains(NodeGroupReader.ControlProject,
                         StringComparer.Ordinal) &&
                     (!versionSources.Contains(input.Path) || versionConfiguration.Contains(
                         input.Path))))
            paths.Add(new(input.Path, "ControlConfiguration",
                inputs.Revision.Entries.Any(entry => entry.Path == input.Path)));
        return new(inputs.Revision.Commit, NodeGroupReader.ControlProject,
            paths.Distinct().OrderBy(input => input.Path, StringComparer.Ordinal)
                .ThenBy(input => input.Role, StringComparer.Ordinal).ToArray());
    }
}
