using Microsoft.Build.Execution;
using Microsoft.Build.Graph;

namespace WorkflowDelivery.Repository;

internal sealed record DotNetGraphRequest(string Revision, string Root, string Entry,
    Dictionary<string, string> Globals, string Destination, string[] CommittedPaths);
internal sealed record DotNetNodeIdentity(string Project, Dictionary<string, string> Globals);
internal sealed record DotNetGraphNode(DotNetNodeIdentity Identity, bool OuterBuild,
    MsBuildDimension Dimension, string TargetFrameworks, string RuntimeIdentifiers,
    string[] OwnedPaths, string TestCapability);
internal sealed record DotNetGraphEdge(DotNetNodeIdentity Consumer, DotNetNodeIdentity Dependency);
internal sealed record DotNetGraphInput(string Path, string Role, string Stage,
    DotNetNodeIdentity Consumer);
internal sealed record DotNetGraphResponse(string Revision, string Root, string Entry,
    DotNetGraphNode[] Nodes, DotNetGraphEdge[] Edges, DotNetGraphInput[] Inputs);

// Called only inside the endpoint-selected MSBuild process. MSBuild owns graph construction.
internal static class DotNetGraph
{
    private static readonly string[] CallerVariantSelectors = ["TargetFramework",
        "TargetFrameworks", "RuntimeIdentifier", "RuntimeIdentifiers"];
    private static readonly string[] EvaluatedInputTypes = ["Compile", "EmbeddedResource",
        "Content", "None", "AdditionalFiles", "Analyzer", "EditorConfigFiles",
        "PotentialEditorConfigFiles",
        "GlobalAnalyzerConfigFiles", "ApplicationDefinition", "Page", "Resource",
        "SplashScreen"];

    internal static DotNetGraphResponse Read(DotNetGraphRequest request)
    {
        if (request.Revision.Length != 40 ||
            request.Revision.Any(value => !char.IsAsciiHexDigit(value)))
            throw new InvalidDataException("Native graph requires an exact endpoint revision.");
        string root = Path.TrimEndingDirectorySeparator(Absolute(request.Root));
        string entry = Absolute(request.Entry);
        string relative = Path.GetRelativePath(root, entry);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Native graph entry is outside its endpoint.");
        if (!File.Exists(entry))
            throw new InvalidDataException("Native graph entry is unavailable.");
        if (request.Globals.Keys.Any(name => CallerVariantSelectors.Contains(name,
            StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                "Native graph discovery does not accept caller variant selectors.");
        if (!request.Globals.TryGetValue("Configuration", out string? configuration) ||
            configuration != "Debug" ||
            !request.Globals.TryGetValue("ContinuousIntegrationBuild", out string? ci) ||
            ci != "true" ||
            !request.Globals.TryGetValue("RestoreLockedMode", out string? locked) ||
            locked != "true")
            throw new InvalidDataException("Native graph requires the supported CI operation.");
        var graph = new ProjectGraph(entry, request.Globals);
        var identities = graph.ProjectNodes.ToDictionary(node => node,
            node => Identity(node.ProjectInstance));
        var ownership = new DotNetOwnership(root, graph.ProjectNodes.Select(node =>
            node.ProjectInstance), request.CommittedPaths);
        var producers = new DotNetRepositoryTargets(root, request.CommittedPaths);
        DotNetGraphNode[] nodes = graph.ProjectNodes.Select(node =>
        {
            ProjectInstance project = node.ProjectInstance;
            producers.Validate(project, reason => Unavailable(identities[node],
                "RepositoryTarget", reason));
            string outer = project.GetPropertyValue("IsCrossTargetingBuild");
            if (outer != "" && !bool.TryParse(outer, out _))
                throw new InvalidDataException("Unsupported native outer-build identity.");
            return new DotNetGraphNode(identities[node],
                string.Equals(outer, "true", StringComparison.OrdinalIgnoreCase),
                new(project.GetPropertyValue("Configuration"),
                    project.GetPropertyValue("TargetFramework"),
                    project.GetPropertyValue("RuntimeIdentifier")),
                project.GetPropertyValue("TargetFrameworks"),
                project.GetPropertyValue("RuntimeIdentifiers"), ownership.Read(project),
                TestCapability(project, identities[node]));
        }).ToArray();
        DotNetGraphEdge[] edges = graph.ProjectNodes.SelectMany(node =>
            node.ProjectReferences.Select(dependency =>
                new DotNetGraphEdge(identities[node], identities[dependency]))).ToArray();
        DotNetGraphInput[] inputs = graph.ProjectNodes.SelectMany(node =>
        {
            ProjectInstance project = node.ProjectInstance;
            DotNetNodeIdentity consumer = identities[node];
            return new[] { new DotNetGraphInput(Absolute(project.FullPath), "Project",
                    "Evaluation", consumer) }
                .Concat(project.ImportPaths.Select(path => new DotNetGraphInput(Absolute(path),
                    "Import", "Evaluation", consumer)))
                .Concat(EvaluatedInputTypes.SelectMany(type => project.GetItems(type)
                    .Select(item => new DotNetGraphInput(
                        Absolute(item.GetMetadataValue("FullPath")), type,
                        "Evaluation", consumer))))
                .Concat(DotNetConfigurationInputs.Candidates(project, consumer, root))
                .Concat(RestoreInputs(node));
        }).ToArray();
        return new(request.Revision, root, entry, nodes, edges, inputs);

        string TestCapability(ProjectInstance project, DotNetNodeIdentity identity)
        {
            if (Property("IsTestingPlatformApplication")) return "MTP";
            return Property("IsTestProject") ? "VSTest" : "None";

            bool Property(string name)
            {
                string value = project.GetPropertyValue(name);
                if (value.Length == 0) return false;
                if (bool.TryParse(value, out bool enabled)) return enabled;
                throw Unavailable(identity, name, "Malformed required native Boolean: " + value);
            }
        }

        InvalidDataException Unavailable(DotNetNodeIdentity identity, string role, string reason) =>
            new($"Native fact unavailable at revision {request.Revision}, endpoint {root}, " +
                $"project {identity.Project}, globals " +
                System.Text.Json.JsonSerializer.Serialize(identity.Globals) +
                $", role {role}: {reason}");

        IEnumerable<DotNetGraphInput> RestoreInputs(ProjectGraphNode node)
        {
            ProjectInstance project = node.ProjectInstance;
            if (!DotNetOwnership.IsManagedProject(project)) return [];
            ProjectInstance[] sources = [project];
            if (string.IsNullOrEmpty(project.GetPropertyValue("ProjectAssetsFile")) &&
                string.Equals(project.GetPropertyValue("IsCrossTargetingBuild"), "true",
                    StringComparison.OrdinalIgnoreCase))
            {
                // Outer nodes consume their native inner builds, not an invented assets path.
                sources = node.ProjectReferences.Select(reference => reference.ProjectInstance)
                    .Where(inner => string.Equals(inner.FullPath, project.FullPath,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                            StringComparison.Ordinal) &&
                        !string.Equals(inner.GetPropertyValue("IsCrossTargetingBuild"), "true",
                            StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            if (sources.Length == 0)
                throw new InvalidDataException(
                    "Native outer restore contributors are unavailable.");
            return sources.SelectMany(source => DotNetConfigurationInputs.Restore(source,
                identities[node], root, request.CommittedPaths)).Distinct();
        }

        DotNetNodeIdentity Identity(ProjectInstance project)
        {
            var globals = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in project.GlobalProperties.OrderBy(property =>
                property.Key, StringComparer.Ordinal))
            {
                if (property.Value.Replace('\\', '/').Contains(root.Replace('\\', '/'),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "Endpoint-dependent native semantic global: " + property.Key);
                globals.Add(property.Key, property.Value);
            }
            return new(Absolute(project.FullPath), globals);
        }
    }

    private static string Absolute(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Native graph coordinates must be absolute.");
        return Path.GetFullPath(path);
    }
}
