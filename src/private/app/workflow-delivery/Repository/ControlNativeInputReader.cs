using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record ControlReadFiles(DotNetReadFiles Graph, string TargetFile);
internal sealed record ControlBuildContext(string Revision, string Project,
    MsBuildDimension Dimension, string SdkVersion, string TargetFile);
internal sealed record ControlSourceInput(string Path, string Role, bool Present);
internal sealed record ControlNativeInputs(ControlBuildContext Operation,
    ControlSourceInput[] Sources);

// The caller already built this exact endpoint. Native evaluation owns its inputs;
// SDK/package task implementations and generated artifacts need no second proof.
internal sealed class ControlNativeInputReader(GitMaterialization checkout,
    ControlReadFiles files)
{
    internal static Dictionary<string, string> Globals() => new(StringComparer.Ordinal)
    {
        ["Configuration"] = "Debug",
        ["ContinuousIntegrationBuild"] = "true",
        ["RestoreLockedMode"] = "true",
        ["NuGetInteractive"] = "false",
    };

    internal async Task<ControlNativeInputs> ReadAsync(CancellationToken token)
    {
        var reader = new DotNetNativeGraphReader(checkout, files.Graph);
        DotNetBoundGraph graph = await reader.ReadAsync(NodeGroupReader.ControlProject,
            Globals(), token);
        if (graph.Nodes.Length != 1)
            throw new InvalidDataException("Control project references require an adapter.");
        NbgvInputs version = new NbgvInputReader(checkout.Root).Read(checkout.Revision,
            reader.VersionDirectory(graph.Nodes[0]), token);
        return Project(checkout, graph, files.TargetFile, version.ConfigurationCandidates, token);
    }

    internal static ControlNativeInputs Project(GitMaterialization checkout,
        DotNetBoundGraph graph, string targetFile, string[] versionCandidates,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (graph.Graph.Revision != checkout.Revision.Commit ||
            !Same(graph.Graph.Root, checkout.Root) ||
            !Same(graph.Graph.Entry, Path.Combine(checkout.Root, NodeGroupReader.ControlProject)))
            throw new InvalidDataException("Native control has a different endpoint.");
        if (graph.Nodes.Length != 1 || graph.Graph.Edges.Length != 0)
            throw new InvalidDataException("Control project references require an adapter.");
        DotNetBoundNode node = graph.Nodes[0];
        DotNetGraphNode native = node.Native;
        if (node.Project != NodeGroupReader.ControlProject || native.OuterBuild ||
            native.Dimension.Configuration != "Debug" ||
            string.IsNullOrWhiteSpace(native.Dimension.TargetFramework) ||
            native.TargetFrameworks != "" || native.Dimension.RuntimeIdentifier != "" ||
            native.RuntimeIdentifiers != "" || string.IsNullOrWhiteSpace(native.SdkVersion))
            throw new InvalidDataException("Unsupported native control operation.");
        if (!Same(native.TargetFile, targetFile) || !File.Exists(targetFile) ||
            !Path.GetFullPath(targetFile).StartsWith(checkout.Root + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                    StringComparison.Ordinal))
            throw new InvalidDataException("The prepared native control output differs.");
        var sources = new HashSet<ControlSourceInput>();
        foreach (DotNetBoundInput input in graph.Inputs)
        {
            if (input.Path is null) continue;
            string role = input.Native.Role;
            if (role is "Content" or "None" &&
                (input.Native.CopyToOutputDirectory.Length == 0 ||
                input.Native.CopyToOutputDirectory.Equals("Never",
                    StringComparison.OrdinalIgnoreCase)))
                continue;
            bool present = checkout.Revision.Entries.Any(entry => entry.Path == input.Path);
            sources.Add(new(input.Path, role, present));
        }
        // Control self-version provenance is not consumed by the Node/.NET projects.
        // Native NBGV configuration still affects the executable's behavior.
        foreach (string path in versionCandidates)
        {
            ImpactPlanner.ValidatePath(path);
            GitEntry? entry = checkout.Revision.Entries.SingleOrDefault(
                value => value.Path == path);
            if (entry is not null && (entry.ObjectType != "blob" ||
                entry.Mode is not ("100644" or "100755")))
                throw new InvalidDataException(
                    "Unsupported native control version configuration.");
            if (entry is null && (File.Exists(Path.Combine(checkout.Root, path)) ||
                Directory.Exists(Path.Combine(checkout.Root, path))))
                throw new InvalidDataException("Uncommitted control configuration is not absence.");
            sources.Add(new(path, "NbgvConfigurationCandidate", entry is not null));
        }
        return new(new(checkout.Revision.Commit, native.Identity.Project, native.Dimension,
            native.SdkVersion, Path.GetFullPath(targetFile)), sources.OrderBy(input => input.Path,
                StringComparer.Ordinal).ThenBy(input => input.Role,
                    StringComparer.Ordinal).ToArray());
    }

    private static bool Same(string first, string second) =>
        Path.IsPathFullyQualified(first) && Path.IsPathFullyQualified(second) &&
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                StringComparison.Ordinal);
}
