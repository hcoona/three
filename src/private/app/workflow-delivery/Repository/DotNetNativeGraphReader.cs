using System.Security;
using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record DotNetReadFiles(string Assembly, string Wrapper, string Request,
    string Response, int DeadlineSeconds);
internal sealed record DotNetBoundNode(string Project, DotNetGraphNode Native,
    string[] OwnedPaths);
internal sealed record DotNetBoundInput(string? Path, DotNetGraphInput Native);
internal sealed record DotNetBoundGraph(DotNetGraphResponse Graph,
    DotNetBoundNode[] Nodes, DotNetBoundInput[] Inputs);

// The caller owns prepared checkouts, the complete task distribution, and scratch
// lifetime. This reader binds native answers; it does not restore or build projects.
internal sealed class DotNetNativeGraphReader
{
    private static readonly StringComparer PhysicalPaths = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly HashSet<string> EvaluationRoles = new(StringComparer.Ordinal)
    {
        "Project", "Import", "Compile", "EmbeddedResource", "Content", "None",
        "AdditionalFiles", "Analyzer", "EditorConfigFiles", "GlobalAnalyzerConfigFiles",
        "PotentialEditorConfigFiles", "ApplicationDefinition", "Page", "Resource",
        "SplashScreen", "Manifest", "ApplicationIcon", "ApplicationManifest",
        "Win32Manifest", "Win32Resource", "SdkConfigurationCandidate",
        "ConfigurationCandidate", "RestoreLockCandidate", "CentralPackageConfiguration",
        "UserRuntimeConfigCandidate",
    };
    private readonly GitMaterialization checkout;
    private readonly DotNetReadFiles files;
    private readonly Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> query;
    private readonly Dictionary<string, GitEntry> coordinates = new(PhysicalPaths);

    internal DotNetNativeGraphReader(GitMaterialization checkout, DotNetReadFiles files,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>>? query = null)
    {
        this.checkout = checkout;
        this.files = files;
        this.query = query ?? NativeProcess.ExecuteAsync;
        foreach (GitEntry entry in checkout.Revision.Entries)
            if (!coordinates.TryAdd(Path.GetFullPath(Path.Combine(checkout.Root, entry.Path)),
                    entry))
                throw Unavailable("GitCoordinate", "Ambiguous physical Git coordinate: "
                    + entry.Path);
    }

    internal async Task<DotNetBoundGraph> ReadAsync(string project,
        Dictionary<string, string> globals, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string entry = Path.GetFullPath(Path.Combine(checkout.Root, project));
        RequireFile(entry, "Entry");
        if (new[] { files.Assembly, files.Wrapper, files.Request, files.Response }
            .Any(path => !Path.IsPathFullyQualified(path)) || files.DeadlineSeconds <= 0 ||
            !File.Exists(files.Assembly))
            throw Unavailable("Invocation", "Complete task distribution/scratch is unavailable.");
        var request = new DotNetGraphRequest(checkout.Revision.Commit, checkout.Root, entry,
            globals, files.Response, coordinates.Keys.ToArray());
        await using (var output = new FileStream(files.Request, FileMode.CreateNew,
                         FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(output, request,
                TransferJson.Default.DotNetGraphRequest, token);
        string wrapper = $"""
            <Project>
              <UsingTask TaskName="WorkflowDelivery.Repository.DotNetGraphTask"
                         AssemblyFile="{SecurityElement.Escape(files.Assembly)}" />
              <Target Name="Read">
                <WorkflowDelivery.Repository.DotNetGraphTask
                    RequestFile="{SecurityElement.Escape(files.Request)}" />
              </Target>
            </Project>
            """;
        await using (var output = new FileStream(files.Wrapper, FileMode.CreateNew,
                         FileAccess.Write, FileShare.None))
        await using (var writer = new StreamWriter(output))
            await writer.WriteAsync(wrapper.AsMemory(), token);
        NativeCommandResult result = await query(new("dotnet", checkout.Root,
            ["msbuild", files.Wrapper, "-nologo", "-noAutoResponse", "-target:Read"],
            files.DeadlineSeconds), token);
        token.ThrowIfCancellationRequested();
        if (!result.Succeeded)
            throw Unavailable("Invocation", $"Native graph {result.Termination}, exit " +
                $"{result.ExitCode}: {result.Error}\n{result.Stdout}{result.Stderr}");
        await using var response = File.OpenRead(files.Response);
        DotNetGraphResponse graph = await JsonSerializer.DeserializeAsync(response,
            TransferJson.Default.DotNetGraphResponse, token)
            ?? throw Unavailable("Response", "Missing native graph response.");
        return Bind(graph, project, token);
    }

    internal DotNetBoundGraph Bind(DotNetGraphResponse graph, string entry,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (graph.Revision != checkout.Revision.Commit ||
            !PhysicalPaths.Equals(Absolute(graph.Root), checkout.Root) ||
            !PhysicalPaths.Equals(Absolute(graph.Entry), Path.Combine(checkout.Root, entry)))
            throw Unavailable("Response", "Different native revision/root/entry.");
        RequireFile(graph.Entry, "Entry");
        if (graph.Nodes is null || graph.Edges is null || graph.Inputs is null ||
            graph.Nodes.Length == 0)
            throw Unavailable("Response", "Missing native node/edge/input arrays.");
        var nodes = new Dictionary<(string Project, string Globals), DotNetBoundNode>(
            new NativeIdentityComparer());
        foreach (DotNetGraphNode node in graph.Nodes)
        {
            if (node is null || node.Identity is null || node.Dimension is null ||
                node.OwnedPaths is null || node.TargetFrameworks is null ||
                node.RuntimeIdentifiers is null || node.Dimension.Configuration != "Debug" ||
                node.Dimension.TargetFramework is null ||
                node.Dimension.RuntimeIdentifier is null ||
                node.VersionBaseDirectory is null ||
                node.TestCapability is not ("None" or "MTP" or "VSTest"))
                throw Unavailable("Node", "Incomplete native node.");
            string project = RequireFile(node.Identity.Project, "Project");
            bool traversal = project == "dirs.proj";
            if (!traversal && !new[] { ".csproj", ".fsproj", ".vbproj" }.Contains(
                    Path.GetExtension(project), StringComparer.OrdinalIgnoreCase))
                throw Unavailable("Project", "Unsupported native project kind.", node.Identity);
            if (traversal && node.OwnedPaths.Length != 0)
                throw Unavailable("Ownership", "Traversal cannot own a root region.",
                    node.Identity);
            var bound = new DotNetBoundNode(project, node, node.OwnedPaths.Select(path =>
                RequireFile(path, "Ownership")).Distinct(StringComparer.Ordinal).ToArray());
            if (!nodes.TryAdd(Key(node.Identity), bound))
                throw Unavailable("Node", "Duplicate complete native identity.", node.Identity);
        }
        if (!nodes.Values.Any(node => node.Project == entry))
            throw Unavailable("Entry", "Native entry node is absent.");
        foreach (DotNetGraphEdge edge in graph.Edges)
        {
            if (edge is null) throw Unavailable("Edge", "Missing native edge.");
            Resolve(edge.Consumer);
            Resolve(edge.Dependency);
        }
        var inputs = new List<DotNetBoundInput>();
        var classifications = new Dictionary<((string Project, string Globals) Consumer,
            string Path, string Role, string Stage), DotNetInputProvider?>();
        foreach (DotNetGraphInput input in graph.Inputs)
        {
            if (input is null || input.CopyToOutputDirectory is null)
                throw Unavailable("Input", "Missing native input.");
            Resolve(input.Consumer);
            if (!(input.Stage == "Evaluation" && EvaluationRoles.Contains(input.Role)) &&
                !(input.Stage == "LockedRestore" && input.Role is "RestoreAssets" or
                    "RestoreLock" or "RestoreConfiguration"))
                throw Unavailable(input.Role, "Unsupported native role/stage: " + input.Stage,
                    input.Consumer);
            string absolute = Absolute(input.Path);
            string? path;
            if (coordinates.ContainsKey(absolute)) path = RequireFile(absolute, input.Role);
            else if (IsCandidate(input.Role) && IsInside(absolute))
            {
                if (File.Exists(absolute) || Directory.Exists(absolute))
                    throw Unavailable(input.Role, "Uncommitted source is not absence: " + absolute,
                        input.Consumer);
                path = Relative(absolute);
            }
            else if (IsCandidate(input.Role) && !File.Exists(absolute) &&
                !Directory.Exists(absolute))
                path = null;
            else
            {
                ValidateProvider(input);
                path = null;
            }
            var key = (Key(input.Consumer), absolute, input.Role, input.Stage);
            if (classifications.TryGetValue(key, out DotNetInputProvider? previous) &&
                previous != input.Provider)
                throw Unavailable(input.Role, "Conflicting native input classification.",
                    input.Consumer);
            classifications[key] = input.Provider;
            inputs.Add(new(path, input));
        }
        foreach (DotNetBoundNode node in nodes.Values)
            if (!inputs.Any(input => input.Native.Role == "Project" &&
                new NativeIdentityComparer().Equals(Key(input.Native.Consumer),
                    Key(node.Native.Identity)) &&
                input.Path == node.Project))
                throw Unavailable("Project", "Native project input is absent.",
                    node.Native.Identity);

        return new(graph, nodes.Values.ToArray(), inputs.ToArray());

        void Resolve(DotNetNodeIdentity identity)
        {
            if (!nodes.ContainsKey(Key(identity)))
                throw Unavailable("Relation", "Dangling/substituted complete native identity.",
                    identity);
        }

    }

    internal string VersionDirectory(DotNetBoundNode node)
    {
        string native = node.Native.VersionBaseDirectory;
        if (native.Length == 0) return Parent(node.Project);
        string absolute = Absolute(native);
        if (string.Equals(Path.TrimEndingDirectorySeparator(absolute), checkout.Root,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                StringComparison.Ordinal)) return ".";
        if (!IsInside(absolute))
            throw Unavailable("NBGV", "Version base is outside the endpoint.",
                node.Native.Identity);
        string directory = Relative(absolute);
        string[] committed = checkout.Revision.Entries.Where(entry =>
                entry.Path.StartsWith(directory + "/", OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .Select(entry => entry.Path[..directory.Length])
            .Distinct(StringComparer.Ordinal).ToArray();
        if (committed.Length != 1)
            throw Unavailable("NBGV", "Version base has no unique committed directory.",
                node.Native.Identity);
        return committed[0];
    }


    internal static (string Project, string Globals) Key(DotNetNodeIdentity identity)
    {
        if (identity is null || identity.Globals is null ||
            identity.Globals.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) ||
            identity.Globals.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                identity.Globals.Count)
            throw new InvalidDataException("Malformed complete native identity.");
        var properties = new Dictionary<string, string>(identity.Globals,
            StringComparer.OrdinalIgnoreCase);
        if (properties.GetValueOrDefault("Configuration") != "Debug" ||
            properties.GetValueOrDefault("ContinuousIntegrationBuild") != "true" ||
            properties.GetValueOrDefault("RestoreLockedMode") != "true")
            throw new InvalidDataException("Unsupported native operation globals.");
        // MSBuild property names are insensitive to case. Preserve returned values
        // in the graph; only the lookup key uses that native equality contract.
        return (Absolute(identity.Project), JsonSerializer.Serialize(properties
            .Select(pair => new KeyValuePair<string, string>(
                pair.Key.ToUpperInvariant(), pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
            TransferJson.Default.KeyValuePairStringStringArray));
    }

    internal sealed class NativeIdentityComparer :
        IEqualityComparer<(string Project, string Globals)>
    {
        public bool Equals((string Project, string Globals) first,
            (string Project, string Globals) second) =>
            PhysicalPaths.Equals(first.Project, second.Project) && first.Globals == second.Globals;
        public int GetHashCode((string Project, string Globals) value) =>
            HashCode.Combine(PhysicalPaths.GetHashCode(value.Project),
                StringComparer.Ordinal.GetHashCode(value.Globals));
    }

    private void ValidateProvider(DotNetGraphInput input)
    {
        DotNetInputProvider? provider = input.Provider;
        bool admitted = provider is not null && Path.IsPathFullyQualified(provider.Root) &&
            !string.IsNullOrWhiteSpace(provider.Identity) && provider.Version is not null &&
            File.Exists(input.Path) && (provider.Kind switch
            {
                "Toolset" or "Sdk" or "LockedPackage" => input.Stage == "Evaluation" &&
                    input.Role is not ("Project" or "CentralPackageConfiguration") &&
                    !IsCandidate(input.Role),
                "TraversalPackage" => input.Stage == "Evaluation" &&
                    input.Role is not ("Project" or "CentralPackageConfiguration") &&
                    !IsCandidate(input.Role) && PhysicalPaths.Equals(input.Consumer.Project,
                        Path.Combine(checkout.Root, "dirs.proj")),
                "RestoreGenerated" => input.Role == "Import" && input.Stage == "Evaluation",
                "RestoreAssets" => input.Role == "RestoreAssets" && input.Stage == "LockedRestore",
                "RestoreEnvironment" => input.Role == "RestoreConfiguration" &&
                    input.Stage == "LockedRestore" && !IsInside(input.Path),
                _ => false,
            });
        if (!admitted)
            throw Unavailable(input.Role, "Necessary source has no supported native provider: "
                + input.Path + ", stage " + input.Stage, input.Consumer);
    }

    internal string BindCandidate(string path, string role)
    {
        string absolute = Path.GetFullPath(Path.Combine(checkout.Root, path));
        if (coordinates.ContainsKey(absolute)) return RequireFile(absolute, role);
        if (!IsInside(absolute) || File.Exists(absolute) || Directory.Exists(absolute))
            throw Unavailable(role, "Unsupported candidate source: " + path);
        return Relative(absolute);
    }

    internal string RequireFile(string path, string role)
    {
        if (!coordinates.TryGetValue(Absolute(path), out GitEntry? entry) ||
            entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755"))
            throw Unavailable(role, "Required input is not regular committed source: " + path);
        ImpactPlanner.ValidatePath(entry.Path);
        return entry.Path;
    }

    internal bool IsInside(string path) => Path.GetFullPath(path).StartsWith(checkout.Root +
        Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal string Relative(string absolute)
    {
        string relative = Path.GetRelativePath(checkout.Root, absolute).Replace('\\', '/');
        ImpactPlanner.ValidatePath(relative);
        return relative;
    }

    private static bool IsCandidate(string role) => role is "GlobalAnalyzerConfigFiles" or
        "PotentialEditorConfigFiles" or "SdkConfigurationCandidate" or
        "ConfigurationCandidate" or "RestoreLockCandidate" or "UserRuntimeConfigCandidate";

    internal static string Absolute(string path) => !Path.IsPathFullyQualified(path)
        ? throw new InvalidDataException("Native endpoint coordinates must be absolute.")
        : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static string Parent(string path) => path.LastIndexOf('/') is int separator &&
        separator >= 0 ? path[..separator] : ".";

    private InvalidDataException Unavailable(string role, string reason,
        DotNetNodeIdentity? consumer = null) => new($"Native .NET fact unavailable at " +
        $"revision {checkout.Revision.Commit}, endpoint {checkout.Root}, " +
        $"consumer {JsonSerializer.Serialize(consumer,
            TransferJson.Default.DotNetNodeIdentity)}, " +
        $"role {role}: {reason}");
}
