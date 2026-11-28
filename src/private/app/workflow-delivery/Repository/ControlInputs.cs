using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record ControlSourceInput(string Path, string Role, bool Present);
internal sealed record ControlInputFacts(string Revision, string Project,
    ControlSourceInput[] Sources);

// Bind maintained native evaluation and explicit control consumers, without interpreting build
// tasks.
internal static class ControlInputs
{
    private const string Root = "src/private/app/workflow-delivery";
    internal const string CoreProject = Root + "/Core/WorkflowDelivery.Core.csproj";

    private static readonly StringComparer PhysicalPaths = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // The control project's DefaultItemExcludes remove its docs subtree and README from the
    // native item region; they are records, not control-run inputs.
    internal static bool InertDocument(string path) => path == Root + "/README.md" ||
        path.StartsWith(Root + "/docs/", StringComparison.Ordinal);

    internal static ControlInputFacts? Read(DotNetRevisionInputs inputs, bool required)
    {
        DotNetBoundNode[] control = inputs.Nodes.Where(node =>
            node.Project == NodeGroupReader.ControlProject && !node.Native.OuterBuild).ToArray();
        if (control.Length == 0 && !required) return null;
        if (control.Length != 1)
            throw new InvalidDataException("The control requires one complete native dimension.");
        string cli = control[0].Native.Identity.Project;
        string? core = inputs.Nodes.FirstOrDefault(node => node.Project == CoreProject)
            ?.Native.Identity.Project;
        bool CliToCore(DotNetGraphEdge edge) => core is not null &&
            PhysicalPaths.Equals(edge.Consumer.Project, cli) &&
            PhysicalPaths.Equals(edge.Dependency.Project, core);
        bool usesCore = inputs.Graph.Edges.Any(CliToCore);
        bool Consumes(string nativeProject) => PhysicalPaths.Equals(nativeProject, cli) ||
            usesCore && PhysicalPaths.Equals(nativeProject, core);
        if (inputs.Graph.Edges.Any(edge => Consumes(edge.Consumer.Project) &&
                !PhysicalPaths.Equals(edge.Consumer.Project, edge.Dependency.Project) &&
                !CliToCore(edge)))
            throw new InvalidDataException(
                "Control project dependencies require a concrete adapter.");

        DotNetBoundNode[] consumed = inputs.Nodes.Where(node =>
            Consumes(node.Native.Identity.Project)).ToArray();
        var projects = consumed.Select(node => node.Project).ToHashSet(StringComparer.Ordinal);
        var paths = consumed
            .SelectMany(node => node.OwnedPaths.Select(path =>
                new ControlSourceInput(path, "ControlSource", true))).ToList();
        foreach (DotNetBoundInput input in inputs.Inputs.Where(input => input.Path is not null &&
                     Consumes(input.Native.Consumer.Project)))
            paths.Add(new(input.Path!, input.Native.Role,
                inputs.Revision.Entries.Any(entry => entry.Path == input.Path)));
        // Native NBGV distribution is a tool exception. Its configuration is consumed;
        // the control application's unused self-version does not add product source consumers.
        var versionSources = inputs.Versions.SelectMany(version => version.Paths)
            .ToHashSet(StringComparer.Ordinal);
        var versionConfiguration = inputs.Versions.SelectMany(
            version => version.ConfigurationCandidates)
            .ToHashSet(StringComparer.Ordinal);
        foreach (SharedInput input in inputs.SelectionInputs.Where(input =>
                     input.Consumers.Any(projects.Contains) &&
                     (!versionSources.Contains(input.Path) || versionConfiguration.Contains(
                         input.Path))))
            paths.Add(new(input.Path, "ControlConfiguration",
                inputs.Revision.Entries.Any(entry => entry.Path == input.Path)));
        return new(inputs.Revision.Commit, NodeGroupReader.ControlProject,
            paths.Distinct().OrderBy(input => input.Path, StringComparer.Ordinal)
                .ThenBy(input => input.Role, StringComparer.Ordinal).ToArray());
    }
}
