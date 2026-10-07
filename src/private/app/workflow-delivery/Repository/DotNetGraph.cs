using System.Collections.Concurrent;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;

namespace WorkflowDelivery.Repository;

internal sealed record DotNetGraphRequest(string Revision, string Root, string Entry,
    Dictionary<string, string> Globals, string Destination, string[] CommittedPaths);
internal sealed record DotNetNodeIdentity(string Project, Dictionary<string, string> Globals);
internal sealed record DotNetGraphNode(DotNetNodeIdentity Identity, bool OuterBuild,
    MsBuildDimension Dimension, string TargetFrameworks, string RuntimeIdentifiers,
    string[] OwnedPaths, string TestCapability, string VersionBaseDirectory = "");
internal sealed record DotNetGraphEdge(DotNetNodeIdentity Consumer, DotNetNodeIdentity Dependency);
internal sealed record DotNetGraphInput(string Path, string Role, string Stage,
    DotNetNodeIdentity Consumer, DotNetInputProvider? Provider = null);
internal sealed record DotNetInputProvider(string Kind, string Root, string Identity,
    string Version);
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
        "SplashScreen", "Manifest"];
    private static readonly string[] EvaluatedFileProperties = ["ApplicationIcon",
        "ApplicationManifest", "Win32Manifest", "Win32Resource"];

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
        var imports = new ConcurrentDictionary<ProjectInstance, ResolvedImport[]>();
        var graph = new ProjectGraph([new ProjectGraphEntryPoint(entry, request.Globals)],
            ProjectCollection.GlobalProjectCollection,
            (path, globals, collection) =>
        {
            // The graph supplies this evaluation's parameters and collection. Observe
            // native SDK results here; a flat ProjectInstance import list loses them.
            var project = new Project(path, globals, null, collection);
            ProjectInstance instance = project.CreateProjectInstance(ProjectInstanceSettings.None);
            imports.TryAdd(instance, project.Imports.ToArray());
            return instance;
        });
        HashSet<ProjectGraphNode> helpers = DotNetNbgvCaching.Helpers(graph);
        ProjectGraphNode[] retained = graph.ProjectNodes.Where(node => !helpers.Contains(node))
            .ToArray();
        var identities = retained.ToDictionary(node => node,
            node => Identity(node.ProjectInstance));
        var ownership = new DotNetOwnership(root, retained.Select(node =>
            node.ProjectInstance), request.CommittedPaths);
        var producers = new DotNetRepositoryTargets(root, request.CommittedPaths);
        var restored = retained.ToDictionary(node => node, RestoreInputs);
        foreach (ProjectGraphNode helper in helpers)
            foreach (ProjectGraphNode consumer in helper.ReferencingProjects)
            {
                if (!restored.TryGetValue(consumer, out DotNetRestoreInputs[]? restore))
                    throw new InvalidDataException("Unsupported nested NBGV helper relation.");
                try { DotNetNbgvCaching.Validate(helper, consumer, restore); }
                catch (InvalidDataException error)
                {
                    throw Unavailable(identities[consumer], "NbgvCachingHelper", error.Message);
                }
                producers.Validate(helper.ProjectInstance, reason => Unavailable(
                    identities[consumer], "RepositoryTarget", reason));
            }
        DotNetGraphNode[] nodes = retained.Select(node =>
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
                TestCapability(project, identities[node]), VersionBase(project));
        }).ToArray();
        DotNetGraphEdge[] edges = retained.SelectMany(node =>
            node.ProjectReferences.Where(dependency => !helpers.Contains(dependency))
            .Select(dependency =>
                new DotNetGraphEdge(identities[node], identities[dependency]))).ToArray();
        DotNetGraphInput[] inputs = retained.SelectMany(node =>
        {
            ProjectInstance project = node.ProjectInstance;
            DotNetNodeIdentity consumer = identities[node];
            DotNetRestoreInputs[] restore = restored[node];
            var providers = new DotNetNativeInputs(project, imports[project], restore,
                request.CommittedPaths);
            return EvaluatedInputs(project, consumer, "Project")
                .Concat(DotNetConfigurationInputs.Candidates(project, consumer, root))
                .Concat(restore.SelectMany(value => value.Inputs))
                .Concat(node.ProjectReferences.Where(helpers.Contains).SelectMany(helper =>
                    EvaluatedInputs(helper.ProjectInstance, consumer, "Import")))
                .Select(providers.Classify);
        }).ToArray();
        return new(request.Revision, root, entry, nodes, edges, inputs);

        IEnumerable<DotNetGraphInput> EvaluatedInputs(ProjectInstance project,
            DotNetNodeIdentity consumer, string projectRole) =>
            new[] { new DotNetGraphInput(Absolute(project.FullPath), projectRole,
                "Evaluation", consumer) }
                .Concat(project.ImportPaths.Select(path => new DotNetGraphInput(Absolute(path),
                    "Import", "Evaluation", consumer)))
                .Concat(EvaluatedInputTypes.SelectMany(type => project.GetItems(type)
                    .Select(item => new DotNetGraphInput(
                        Absolute(item.GetMetadataValue("FullPath")), type,
                        "Evaluation", consumer))))
                .Concat(EvaluatedFileProperties.Select(name => (Name: name,
                        Value: project.GetPropertyValue(name)))
                    .Where(property => property.Value.Length != 0)
                    .Select(property => new DotNetGraphInput(Path.GetFullPath(property.Value,
                            Path.GetDirectoryName(project.FullPath)!), property.Name,
                        "Evaluation", consumer)));

        string VersionBase(ProjectInstance project)
        {
            string repository = project.GetPropertyValue("GitRepoRoot");
            if ((repository.Length != 0 && !string.Equals(
                    Path.TrimEndingDirectorySeparator(Absolute(repository)), root,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                        StringComparison.Ordinal)) ||
                project.GetPropertyValue("ProjectPathRelativeToGitRepoRoot").Length != 0)
                throw new InvalidDataException("Unsupported native NBGV repository context.");
            string directory = project.GetPropertyValue("GitVersionBaseDirectory");
            return directory.Length == 0 ? DotNetOwnership.IsManagedProject(project)
                ? Path.GetDirectoryName(project.FullPath)! : "" :
                Path.TrimEndingDirectorySeparator(Absolute(directory));
        }

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
                System.Text.Json.JsonSerializer.Serialize(identity.Globals,
                    TransferJson.Default.DictionaryStringString) +
                $", role {role}: {reason}");

        DotNetRestoreInputs[] RestoreInputs(ProjectGraphNode node)
        {
            ProjectInstance project = node.ProjectInstance;
            if (!DotNetOwnership.IsManagedProject(project))
                return project.GetPropertyValue("UsingMicrosoftTraversalSdk") == "true"
                    ? [DotNetConfigurationInputs.Traversal(project, identities[node], root,
                        request.CommittedPaths)] : [];
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
            return sources.Select(source => DotNetConfigurationInputs.Restore(source,
                identities[node], root, request.CommittedPaths)).ToArray();
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
