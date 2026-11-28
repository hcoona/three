using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Globbing;
using Microsoft.Build.Graph;
using NuGet.Versioning;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record DotNetGraphRequest(string Revision, string Root, string Entry,
    Dictionary<string, string> Globals, string Destination, string[] CommittedPaths);
internal sealed record DotNetNodeIdentity(string Project, Dictionary<string, string> Globals);
internal sealed record DotNetGraphNode(DotNetNodeIdentity Identity, bool OuterBuild,
    MsBuildDimension Dimension, string TargetFrameworks, string RuntimeIdentifiers,
    string[] OwnedPaths, string TestCapability, string VersionBaseDirectory = "",
    string[]? RequiredTools = null);
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
    private static readonly string[] UnsupportedControlIndirectInputs = ["CompilerResponseFile",
        "AdditionalLibPaths", "AddModules", "LinkResource", "KeyOriginatorFile"];
    private static readonly string[] EvaluatedFileProperties = ["ApplicationIcon",
        "ApplicationManifest", "Win32Manifest", "Win32Resource", "Win32Icon",
        "AppConfig", "AppConfigForCompiler", "CodeAnalysisRuleSet", "AssemblyOriginatorKeyFile"];

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
        var validateTargets = DotNetRepositoryTargets.Bind(root, request.CommittedPaths);
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
                validateTargets(helper.ProjectInstance, reason => Unavailable(
                    identities[consumer], "RepositoryTarget", reason));
            }
        Func<string[]>[] owned = ownership.Read(retained.Select(node =>
            node.ProjectInstance).ToArray());
        DotNetGraphNode[] nodes = retained.Select((node, index) =>
        {
            ProjectInstance project = node.ProjectInstance;
            validateTargets(project, reason => Unavailable(identities[node],
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
                project.GetPropertyValue("RuntimeIdentifiers"), owned[index](),
                TestCapability(project, identities[node]), VersionBase(project),
                RequiredTools(project, identities[node]));
        }).ToArray();
        foreach (DotNetGraphNode[] variants in nodes.GroupBy(node => node.Identity.Project)
            .Select(group => group.ToArray()))
            if (variants.Any(node => !node.RequiredTools!.SequenceEqual(
                variants[0].RequiredTools!)))
                throw Unavailable(variants[0].Identity, DotNetTools.Item,
                    "Variants of one project declare different required tools.");
        DotNetGraphEdge[] edges = retained.SelectMany(node =>
            node.ProjectReferences.Where(dependency => !helpers.Contains(dependency))
            .Select(dependency =>
                new DotNetGraphEdge(identities[node], identities[dependency]))).ToArray();
        var control = ControlInputs.Reachable(root, nodes, edges, required: false);
        DotNetGraphInput[] inputs = retained.SelectMany(node =>
        {
            ProjectInstance project = node.ProjectInstance;
            DotNetNodeIdentity consumer = identities[node];
            if (control.Count != 0 && control.Contains(DotNetRepositoryReader.Key(consumer)))
            {
                foreach (string name in UnsupportedControlIndirectInputs)
                    if (project.GetPropertyValue(name).Length != 0 || project.GetItems(
                        name).Count != 0)
                        throw Unavailable(consumer, name,
                            "Indirect control inputs require a concrete native adapter.");
                if (project.GetItems("Reference").Any(item => item.GetMetadataValue(
                    "HintPath").Length == 0))
                    throw Unavailable(consumer, "ReferenceHintPath",
                        "Control references require explicit native HintPaths.");
            }
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
                .Concat(project.GetItems("Reference").Where(item =>
                    item.GetMetadataValue("HintPath").Length != 0).Select(
                        item => new DotNetGraphInput(
                        Path.GetFullPath(item.GetMetadataValue("HintPath"),
                            Path.GetDirectoryName(project.FullPath)!),
                        "ReferenceHintPath", "Evaluation", consumer)))
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

        string[] RequiredTools(ProjectInstance project, DotNetNodeIdentity identity)
        {
            ProjectItemInstance[] items = project.GetItems(DotNetTools.Item).ToArray();
            if (items.Length == 0) return [];
            // Metadata includes item-definition metadata but not built-in item metadata.
            if (items.Any(item => item.Metadata.Any()))
                throw Unavailable(identity, DotNetTools.Item,
                    "Required tool declarations do not accept metadata.");
            if (TestCapability(project, identity) == "None")
                throw Unavailable(identity, DotNetTools.Item,
                    "Only test projects declare required tools.");
            try { return DotNetTools.Names(items.Select(item => item.EvaluatedInclude)); }
            catch (InvalidDataException error)
            {
                throw Unavailable(identity, DotNetTools.Item, error.Message);
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

    private static string Absolute(string path) =>
        OwnedPath.FullPath(path, "Native graph coordinates must be absolute.");

    // These concrete repository operations are adapted; SDK/package targets remain native.
    private static class DotNetRepositoryTargets
    {
        private static readonly StringComparer PhysicalPaths = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        private static readonly (string Origin, string Name)[] Supported =
        [
            ("src/public/lib/CircularList/CircularList.csproj", "SetPackageReleaseNotes"),
            ("src/public/lib/Hjg.Pngcs/Hjg.Pngcs.csproj", "SetPackageReleaseNotes"),
            ("src/public/lib/Memoization/Memoization.csproj", "SetPackageReleaseNotes"),
            ("src/public/lib/MicrosoftExtensions.Logging.MSTest/" +
                "MicrosoftExtensions.Logging.MSTest.csproj", "SetPackageReleaseNotes"),
            ("src/public/lib/MicrosoftExtensions.Logging.Xunit/" +
                "MicrosoftExtensions.Logging.Xunit.csproj", "SetPackageReleaseNotes"),
            ("src/public/lib/MicrosoftExtensions.Options.DedupChangeExtensions/" +
                "MicrosoftExtensions.Options.DedupChangeExtensions.csproj",
                "SetPackageReleaseNotes"),
            ("src/public/lib/PhiFailureDetector/PhiFailureDetector.csproj",
                "SetPackageReleaseNotes"),
            ("src/public/lib/WebHdfs.Extensions.FileProviders/" +
                "WebHdfs.Extensions.FileProviders.csproj", "SetPackageReleaseNotes"),
            ("src/private/app/workflow-delivery/WorkflowDelivery.csproj", "ReferenceOfficialNbgv"),
            ("tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj",
                "ReferenceOfficialNbgv"),
            ("tests/public/lib/WebHdfs.Extensions.FileProviders.UnitTest/" +
                "WebHdfs.Extensions.FileProviders.UnitTest.csproj", "PreBuild"),
            ("dirs.proj", "RestoreWindowsOnlyProjectsOnNonWindows"),
        ];

        internal static Action<ProjectInstance, Func<string, InvalidDataException>> Bind(
            string root, string[] committedPaths)
        {
            var committed = new HashSet<string>(committedPaths.Select(Path.GetFullPath),
                PhysicalPaths);
            var supported = Supported.Select(pair => (Origin: Path.GetFullPath(Path.Combine(root,
                pair.Origin.Replace('/', Path.DirectorySeparatorChar))), pair.Name)).ToArray();
            return Validate;

            void Validate(ProjectInstance project, Func<string, InvalidDataException> unavailable)
            {
                foreach (ProjectTargetInstance target in project.Targets.Values)
                {
                    string origin = target.Location.File;
                    if (!Path.IsPathFullyQualified(origin))
                        throw unavailable(
                            $"Target {target.Name} has no absolute native definition origin.");
                    origin = Path.GetFullPath(origin);
                    // Being under the endpoint (including obj) does not imply ownership.
                    if (!committed.Contains(origin)) continue;
                    if (!supported.Any(pair => PhysicalPaths.Equals(pair.Origin, origin) &&
                        StringComparer.OrdinalIgnoreCase.Equals(pair.Name, target.Name)))
                        throw unavailable($"Unsupported effective repository producer " +
                            $"{target.Name}, origin {origin}; a reviewed operation adapter " +
                            "is required.");
                }
            }
        }
    }

    // A finite projection of NBGV's native tool dependency, not a helper-node framework.
    private static class DotNetNbgvCaching
    {
        private const string Package = "Nerdbank.GitVersioning";
        private static readonly StringComparer Paths = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        internal static HashSet<ProjectGraphNode> Helpers(ProjectGraph graph) =>
            graph.ProjectNodes.Where(helper => helper.ReferencingProjects.Any(consumer =>
                References(consumer, helper).Any(reference =>
                    reference.GetMetadataValue("NBGV_InnerProject") == "true"))).ToHashSet();

        internal static void Validate(ProjectGraphNode helper, ProjectGraphNode consumer,
            DotNetRestoreInputs[] restore)
        {
            ProjectItemInstance[] references = References(consumer, helper);
            if (helper.ProjectReferences.Count != 0 || references.Length == 0 ||
                references.Any(reference => reference.GetMetadataValue("NBGV_InnerProject") !=
                    "true") || consumer.ProjectInstance.GetPropertyValue("NBGV_CacheMode") !=
                    "MSBuildTargetCaching")
                throw new InvalidDataException("Unsupported native NBGV helper relation.");
            string path = Path.GetFullPath(helper.ProjectInstance.FullPath);
            DotNetInputProvider[] suppliers = restore.SelectMany(value => value.Packages)
                .Where(package => package.Identity.Equals(Package,
                        StringComparison.OrdinalIgnoreCase) && Paths.Equals(path,
                        Path.GetFullPath(Path.Combine(package.Root, "build",
                            "PrivateP2PCaching.proj"))))
                .ToArray();
            if (suppliers.Length == 0)
                throw new InvalidDataException(
                    "Native NBGV helper is not supplied by the consumer's restored package.");
            string declared = Declared(consumer.ProjectInstance);
            // The consumer's own evaluated declaration is the pin; the restore must supply it.
            if (!VersionRange.TryParse(declared, out VersionRange? range) ||
                range.MinVersion is null || !range.IsMinInclusive ||
                suppliers.Any(package => !NuGetVersion.TryParse(package.Version,
                    out NuGetVersion? restored) || restored != range.MinVersion))
                throw new InvalidDataException(
                    "Native NBGV helper package differs from the consumer's declared pin: " +
                    "declared " + declared + ", restored " +
                    string.Join(", ", suppliers.Select(package => package.Version).Distinct()) +
                    ".");
        }

        // NuGet's central management projects GlobalPackageReference into PackageVersion items;
        // an explicit PackageReference version or VersionOverride takes precedence.
        private static string Declared(ProjectInstance consumer)
        {
            string[] Values(string type, string metadata) => consumer.GetItems(type)
                .Where(item => item.EvaluatedInclude.Equals(Package,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item => item.GetMetadataValue(metadata))
                .Where(value => value.Length != 0).Distinct(StringComparer.Ordinal).ToArray();
            string[] declared = Values("PackageReference", "VersionOverride");
            if (declared.Length == 0) declared = Values("PackageReference", "Version");
            if (declared.Length == 0) declared = Values("PackageVersion", "Version");
            return declared.Length == 1 ? declared[0] : throw new InvalidDataException(
                "Native NBGV helper consumer declares " + (declared.Length == 0 ? "no" :
                    "ambiguous (" + string.Join(", ", declared) + ")") +
                " Nerdbank.GitVersioning version.");
        }

        private static ProjectItemInstance[] References(ProjectGraphNode consumer,
            ProjectGraphNode helper) => consumer.ProjectInstance.GetItems("ProjectReference")
            .Where(reference => Paths.Equals(
                Path.GetFullPath(reference.GetMetadataValue("FullPath")),
                Path.GetFullPath(helper.ProjectInstance.FullPath))).ToArray();
    }
}

// The existing application supplies this entry; ordinary MSBuild loads its dependencies.
public sealed class DotNetGraphTask : ITask
{
    public IBuildEngine BuildEngine { get; set; } = null!;
    public ITaskHost HostObject { get; set; } = null!;

    [Required]
    public string RequestFile { get; set; } = "";

    public bool Execute()
    {
        DotNetGraphRequest request = JsonSerializer.Deserialize(File.ReadAllText(RequestFile),
            TransferJson.Default.DotNetGraphRequest)
            ?? throw new InvalidDataException("Missing native graph request.");
        DotNetGraphResponse response = DotNetGraph.Read(request);
        using var output = new FileStream(request.Destination, FileMode.CreateNew,
            FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, response, TransferJson.Default.DotNetGraphResponse);
        using var diagnostics = new StreamWriter(new FileStream(
            request.Destination + ".host.log", FileMode.CreateNew, FileAccess.Write,
            FileShare.None));
        diagnostics.WriteLine($"Revision: {response.Revision}");
        diagnostics.WriteLine($"Root: {response.Root}");
        diagnostics.WriteLine($"Entry: {response.Entry}");
        foreach (Assembly assembly in new[] { typeof(ProjectGraph).Assembly,
            typeof(ITask).Assembly, typeof(Microsoft.Build.Utilities.Task).Assembly })
            diagnostics.WriteLine($"Assembly: {assembly.FullName}; FileVersion: " +
                $"{assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version}; " +
                $"Location: {assembly.Location}");
        return true;
    }
}

// Git supplies committed entries; the selected MSBuild supplies region exclusions.
internal sealed class DotNetOwnership
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly string[] _regions;
    private readonly string[] _paths;
    // Request-local: graph nodes often share a directory and the same exclusion specs.
    private readonly ConcurrentDictionary<(string Directory, string Spec), MSBuildGlob> _globs =
        new();

    internal DotNetOwnership(string root, IEnumerable<ProjectInstance> projects,
        string[] committedPaths)
    {
        _regions = projects.Where(IsManagedProject).Select(project =>
                Path.GetDirectoryName(project.FullPath)!)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase :
                StringComparer.Ordinal).OrderByDescending(path => path.Length).ToArray();
        _paths = committedPaths.Select(path =>
        {
            if (!Path.IsPathFullyQualified(path))
                throw new InvalidDataException(
                    "Committed ownership coordinates must be absolute.");
            string full = Path.GetFullPath(path);
            if (!Inside(root, full))
                throw new InvalidDataException(
                    "Committed ownership entry is outside its endpoint.");
            return full;
        }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    // Returns one deferred result per project, in order. Invoking a result returns its paths or
    // rethrows its failure, so callers keep the serial failure order.
    internal Func<string[]>[] Read(IReadOnlyList<ProjectInstance> projects)
    {
        // ProjectInstance documents no concurrent-read contract; read its inputs serially.
        (string? Directory, (string Name, string Spec)[] Specs)[] requests = projects.Select(
            project => IsManagedProject(project) ? (Path.GetDirectoryName(project.FullPath)!,
                [.. Specs(project, "DefaultItemExcludes"),
                    .. Specs(project, "DefaultExcludesInProjectFolder")]) :
                ((string?)null, Array.Empty<(string, string)>())).ToArray();
        var results = new Func<string[]>[requests.Length];
        // MSBuild parses and matches these globs concurrently during its own evaluation.
        Parallel.For(0, requests.Length, index =>
        {
            try
            {
                string[] owned = Read(requests[index].Directory, requests[index].Specs);
                results[index] = () => owned;
            }
            catch (Exception error)
            {
                var failure = ExceptionDispatchInfo.Capture(error);
                results[index] = () =>
                {
                    failure.Throw();
                    return [];
                };
            }
        });
        return results;

        static IEnumerable<(string, string)> Specs(ProjectInstance project, string name)
        {
            ProjectPropertyInstance? property = project.GetProperty(name);
            if (property is null) return [];
            // Split while escaped so a literal semicolon cannot become a list separator.
            return ProjectInstance.GetPropertyValueEscaped(property).Split(';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(spec => (name, spec));
        }
    }

    private string[] Read(string? directory, (string Name, string Spec)[] specs)
    {
        if (directory is null) return [];
        MSBuildGlob[] excludes = specs.Select(entry =>
        {
            if (entry.Spec.Contains("%2a", StringComparison.OrdinalIgnoreCase) ||
                entry.Spec.Contains("%3f", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unsupported escaped native ownership wildcard.");
            MSBuildGlob glob = _globs.GetOrAdd((directory, entry.Spec), key =>
                MSBuildGlob.Parse(key.Directory, ProjectCollection.Unescape(key.Spec)));
            if (!glob.IsLegal)
                throw new InvalidDataException("Illegal native ownership filespec: " +
                    entry.Name);
            return glob;
        }).ToArray();
        return _paths.Where(path =>
            string.Equals(_regions.FirstOrDefault(region => Inside(region, path)), directory,
                PathComparison) && !excludes.Any(glob => glob.IsMatch(path))).ToArray();
    }

    internal static bool IsManagedProject(ProjectInstance project)
    {
        string extension = Path.GetExtension(project.FullPath);
        return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Inside(string directory, string path) => path.StartsWith(
        Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, PathComparison);
}
