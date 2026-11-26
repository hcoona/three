using System.Reflection;
using System.Text.Json;
using System.Xml;
using Microsoft.Build.Construction;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Ecosystems;

internal sealed record NuGetReleaseUnit(string Id, string Declaration, string EntryPoint,
    string PackageId, string AssemblyName, string? Probe, string[] Analyzers,
    string? Declarations, string[] CompanionDirectories, ReleaseUnitDeclaration Source)
{
    internal bool Library => Probe is not null;
    internal bool SmokeFixture => Source.SourcePath.StartsWith("tests/", StringComparison.Ordinal);
    internal string ProjectPath => ReleaseUnitCatalog.ParentDirectory(Declaration) + "/" +
        EntryPoint;
}

internal sealed record NpmReleaseUnit(string Id, string Declaration, string SourceName,
    string BuddyName, string OfficialName, string Main, bool RegistryDependencies,
    string[] Required, string Probe, ReleaseUnitDeclaration Source)
{
    internal string PackageName(ReleaseSubject subject) =>
        subject.Channel == "buddy" ? BuddyName : OfficialName;

    internal NativeCommand Import(string directory, string package, int deadlineSeconds = 60) =>
        new("node", Path.Combine(directory, "consumer"),
            ["--input-type=module", "--eval",
                Probe.Replace("@@PACKAGE@@", package, StringComparison.Ordinal)],
            deadlineSeconds);
}

internal sealed record PythonReleaseUnit(string Id, string Declaration, bool StaticVersion,
    bool RuntimeClosure, string[] Dependencies, string? Tests, string[] Api,
    ReleaseUnitDeclaration Source);

internal sealed record RubyReleaseUnit(string Id, string Declaration, string Preset,
    string Require, bool NbgvEnv, bool LockedBundle, string[] Dependencies, string Probe,
    ReleaseUnitDeclaration Source);

internal sealed record GithubReleaseUnit(string Id, string Declaration, string ProductDirectory,
    string EntryPoint, string AssetPrefix, string Executable, string Editor,
    ReleaseUnitDeclaration Source)
{
    internal const string BuddyDestination = "github-release-prerelease";
    internal const string OfficialDestination = "github-release";
    internal const string BuildDefinition = "dotnet/github-release-assets-v1";

    internal string Asset(string kind, string version) => kind switch
    {
        "inno-installer" => $"{AssetPrefix}-{version}-win-x64-setup.exe",
        "portable-zip" => $"{AssetPrefix}-{version}-win-x64-portable.zip",
        _ => throw new InvalidDataException("Unsupported GitHub Release asset kind."),
    };

    internal static string Tag(ReleaseSubject subject, string version) =>
        (subject.Channel == "buddy" ? "buddy/" : "release/") + subject.Unit + "/v" + version;

    internal static string Title(string unit, string version) => unit + " v" + version;
}

internal sealed record BrowserStoreUnit(string Id, string Declaration, string ProductDirectory,
    string ManifestName, ReleaseUnitDeclaration Source);

// Dynamic repository catalog loaded from committed workflow-delivery.release-unit.yml and
// native manifest resources embedded in WorkflowDelivery.dll.
internal static class ReleaseUnitCatalog
{
    private const string UnitPrefix = "ReleaseUnit:";
    private const string ManifestPrefix = "ReleaseManifest:";

    private static readonly Lazy<CatalogSnapshot> Snapshot = new(LoadSnapshot);

    internal static IEnumerable<ReleaseUnitDeclaration> AllDeclarations =>
        Snapshot.Value.Declarations.Values;

    internal static IEnumerable<NuGetReleaseUnit> NuGetUnits =>
        ByDefinition("dotnet/nuget-package-v1").Select(ToNuGet);

    internal static IEnumerable<NpmReleaseUnit> NpmUnits =>
        ByDefinition("node/npm-package-v1").Select(ToNpm);

    internal static IEnumerable<PythonReleaseUnit> PythonUnits =>
        ByDefinition("python/distribution-set-v1").Select(ToPython);

    internal static IEnumerable<RubyReleaseUnit> RubyUnits =>
        ByDefinition("ruby/gem-v1").Select(ToRuby);

    internal static IEnumerable<GithubReleaseUnit> GithubReleaseUnits =>
        ByDefinition(GithubReleaseUnit.BuildDefinition).Select(ToGithubRelease);

    internal static IEnumerable<BrowserStoreUnit> BrowserStoreUnits =>
        ByDefinition("node/browser-extension-v1").Select(ToBrowserStore);

    internal static NuGetReleaseUnit NuGet(string id) =>
        NuGetUnits.SingleOrDefault(unit => unit.Id == id) ??
            throw new InvalidDataException("Unsupported NuGet Release unit.");

    internal static NpmReleaseUnit Npm(string id) =>
        NpmUnits.SingleOrDefault(unit => unit.Id == id) ??
            throw new InvalidDataException("Unsupported npm Release unit.");

    internal static PythonReleaseUnit Python(string id) =>
        PythonUnits.SingleOrDefault(unit => unit.Id == id) ??
            throw new InvalidDataException("Unsupported Python Release unit.");

    internal static RubyReleaseUnit Ruby(string id) =>
        RubyUnits.SingleOrDefault(unit => unit.Id == id) ??
            throw new InvalidDataException("Unsupported Ruby Release unit.");

    internal static GithubReleaseUnit GithubRelease(string id) =>
        GithubReleaseUnits.SingleOrDefault(unit => unit.Id == id) ??
            throw new InvalidDataException("Unsupported GitHub Release unit.");

    internal static BrowserStoreUnit BrowserStore(string id) =>
        BrowserStoreUnits.SingleOrDefault(unit => unit.Id == id) ??
            throw new InvalidDataException("Unsupported browser-store Release unit.");

    internal static bool IsPythonRegistered(string id) =>
        PythonUnits.Any(unit => unit.Id == id);

    internal static bool IsRubyRegistered(string id) =>
        RubyUnits.Any(unit => unit.Id == id);

    internal static NuGetReleaseUnit AdmitNuGet(ReleaseSubject subject)
    {
        NuGetReleaseUnit unit = NuGet(subject.Unit);
        if (subject.Declaration != unit.Declaration ||
            (subject.Channel, subject.Destination) is not (("buddy", "github-packages-nuget") or
                ("official", "nugetorg")))
            throw new InvalidDataException("Unsupported NuGet Release request.");
        if (!unit.Source.AllowsChannel(subject.Channel, subject.DryRun))
            throw new InvalidDataException("Official NuGet Release does not accept this unit.");
        return unit;
    }

    internal static NpmReleaseUnit AdmitNpm(ReleaseSubject subject)
    {
        NpmReleaseUnit unit = Npm(subject.Unit);
        bool buddy = subject.Channel == "buddy" && subject.Destination == "github-packages-npm";
        bool official = subject.Channel == "official" && subject.Destination == "npmjs";
        if (!(buddy || official))
            throw new InvalidDataException("Unsupported selected npm destination and mode.");
        if (!unit.Source.AllowsChannel(subject.Channel, subject.DryRun))
            throw new InvalidDataException("Official npm Release does not accept this unit.");
        ReleasePolicy.RequireEligible(subject);
        return unit;
    }

    internal static PythonReleaseUnit AdmitPython(ReleaseSubject subject)
    {
        PythonReleaseUnit unit = Python(subject.Unit);
        if (!unit.Source.AllowsChannel(subject.Channel, subject.DryRun))
            throw new InvalidDataException("Python Release does not accept this unit/channel.");
        return unit;
    }

    internal static RubyReleaseUnit AdmitRuby(ReleaseSubject subject)
    {
        RubyReleaseUnit unit = Ruby(subject.Unit);
        if (!unit.Source.AllowsChannel(subject.Channel, subject.DryRun))
            throw new InvalidDataException("Ruby Release does not accept this unit/channel.");
        return unit;
    }

    internal static (GithubReleaseUnit Unit, bool Official) AdmitGithubRelease(
        ReleaseSubject subject)
    {
        bool buddy = subject.Channel == "buddy" &&
            subject.Destination == GithubReleaseUnit.BuddyDestination;
        bool official = subject.Channel == "official" &&
            subject.Destination == GithubReleaseUnit.OfficialDestination;
        if (!(buddy || official) || string.IsNullOrWhiteSpace(subject.SelectedRef))
            throw new InvalidDataException("Unsupported GitHub Release channel or ref.");
        ReleasePolicy.RequireEligible(subject);
        if (!GitReader.IsCommitId(subject.Revision))
            throw new InvalidDataException("GitHub Release requires the exact selected revision.");
        GithubReleaseUnit unit = GithubRelease(subject.Unit);
        if (subject.Declaration != unit.Declaration ||
            !unit.Source.AllowsChannel(subject.Channel, subject.DryRun))
            throw new InvalidDataException("Unsupported GitHub Release unit.");
        return (unit, official);
    }

    internal static BrowserStoreUnit AdmitBrowserStore(ReleaseSubject subject)
    {
        BrowserStoreUnit unit = BrowserStore(subject.Unit);
        if (subject.Declaration != unit.Declaration ||
            !unit.Source.AllowsChannel(subject.Channel, subject.DryRun))
            throw new InvalidDataException("Unsupported browser-store release request.");
        return unit;
    }

    internal static PythonPackageOperations PythonPackage(string id, PythonPackageTools tools,
        string root) => new(Python(id), tools, root);

    internal static RubyPackageOperations RubyPackage(RubyReleaseUnit unit, RubyFactTools tools,
        string gem, string nbgv, string state) => new(unit, tools, gem, nbgv, state);

    // Returns the unit's own directory plus ProjectReference companion directories from its
    // embedded .csproj manifest.
    internal static string[] NuGetPreparationRoots(NuGetReleaseUnit unit)
    {
        string directory = ParentDirectory(unit.Declaration);
        var roots = new SortedSet<string>(StringComparer.Ordinal) { directory };
        foreach (string companion in unit.CompanionDirectories)
            roots.Add(companion);
        return [.. roots];
    }

    internal static IEnumerable<string> CompanionDirectoriesFromCsproj(string projectDirectory,
        string csprojXml)
    {
        ProjectRootElement root = OpenProjectRoot(csprojXml);
        foreach (ProjectItemElement item in root.Items
                     .Where(el => el.ItemType == "ProjectReference" &&
                         string.IsNullOrEmpty(el.Include) is false))
        {
            string combined = NormalizeRelativePath(projectDirectory + "/" +
                item.Include.Replace('\\', '/'));
            yield return ParentDirectory(combined);
        }
    }

    private static ProjectRootElement OpenProjectRoot(string csprojXml)
    {
        using var collection = new Microsoft.Build.Evaluation.ProjectCollection();
        using var textReader = new StringReader(csprojXml);
        using var xmlReader = XmlReader.Create(textReader);
        return ProjectRootElement.Create(xmlReader, collection);
    }

    private static string NormalizeRelativePath(string path)
    {
        var stack = new List<string>();
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (stack.Count == 0)
                    throw new InvalidDataException("Companion path escapes repository root.");
                stack.RemoveAt(stack.Count - 1);
            }
            else stack.Add(part);
        }
        return string.Join('/', stack);
    }

    internal static string ParentDirectory(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    private static IEnumerable<ReleaseUnitDeclaration> ByDefinition(string definition) =>
        Snapshot.Value.Declarations.Values
            .Where(decl => decl.Builds is [var b] && b.Definition == definition)
            .OrderBy(decl => decl.Id, StringComparer.Ordinal);

    private static NuGetReleaseUnit ToNuGet(ReleaseUnitDeclaration decl)
    {
        ReleaseBuild build = decl.Builds.Single();
        string directory = ParentDirectory(decl.SourcePath);
        string manifestPath = directory + "/" + build.EntryPoint;
        string csprojXml = Snapshot.Value.Manifests.TryGetValue(manifestPath, out string? xml)
            ? xml
            : throw new InvalidDataException($"Missing embedded .csproj manifest {manifestPath}.");
        ProjectRootElement root = OpenProjectRoot(csprojXml);
        string stem = Path.GetFileNameWithoutExtension(build.EntryPoint);
        string packageId = ReadUnconditionalProperty(root, "PackageId") ?? stem;
        string assemblyName = ReadUnconditionalProperty(root, "AssemblyName") ?? stem;
        string[] companions = [.. CompanionDirectoriesFromCsproj(directory, csprojXml)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        return new(decl.Id, decl.SourcePath, build.EntryPoint, packageId, assemblyName,
            build.Qualification?.Probe, build.Qualification?.Analyzers ?? [],
            build.Qualification?.Declarations, companions, decl);
    }

    private static string? ReadUnconditionalProperty(ProjectRootElement root,
        string propertyName) =>
        root.Properties.FirstOrDefault(prop => prop.Name == propertyName
            && string.IsNullOrEmpty(prop.Condition))?.Value.Trim() is { Length: > 0 } value
                ? value : null;

    private static NpmReleaseUnit ToNpm(ReleaseUnitDeclaration decl)
    {
        ReleaseBuild build = decl.Builds.Single();
        string directory = ParentDirectory(decl.SourcePath);
        string manifestPath = directory + "/" + build.EntryPoint;
        string packageJson = Snapshot.Value.Manifests.TryGetValue(manifestPath, out string? json)
            ? json
            : throw new InvalidDataException($"Missing embedded npm manifest {manifestPath}.");
        using JsonDocument doc = JsonDocument.Parse(packageJson);
        JsonElement root = doc.RootElement;
        string sourceName = root.GetProperty("name").GetString()
            ?? throw new InvalidDataException($"Missing name in {manifestPath}.");
        string main = root.TryGetProperty("main", out JsonElement mainEl)
            && mainEl.GetString() is { Length: > 0 } m ? m : "./dist/index.js";
        bool registryDeps = root.TryGetProperty("dependencies", out JsonElement deps)
            && deps.ValueKind == JsonValueKind.Object && deps.EnumerateObject().Any();
        string buddyName = sourceName.StartsWith("@hcoona/", StringComparison.Ordinal)
            ? sourceName : "@hcoona/" + sourceName;
        ReleaseQualificationSpec qual = build.Qualification
            ?? throw new InvalidDataException($"Missing npm qualification on {decl.Id}.");
        string[] required = qual.TestBeforeBuild
            ? ["native-input", "test", "build", "pack", "contents", "consumer", "original-bytes"]
            : ["native-input", "build", "test", "pack", "contents", "consumer", "original-bytes"];
        return new(decl.Id, decl.SourcePath, sourceName, buddyName, sourceName,
            main, registryDeps, required,
            qual.Probe ?? throw new InvalidDataException($"Missing npm probe on {decl.Id}."),
            decl);
    }

    internal static PythonReleaseUnit ToPython(ReleaseUnitDeclaration decl)
    {
        ReleaseBuild build = decl.Builds.Single();
        ReleaseQualificationSpec? qual = build.Qualification;
        bool staticVersion = qual?.StaticVersion ?? false;
        if (staticVersion && decl.Id != "nbgv-python")
            throw new InvalidDataException(
                "Static Python versioning is reserved for nbgv-python.");
        return new(decl.Id, decl.SourcePath, staticVersion,
            qual?.RuntimeClosure ?? false, qual?.Dependencies ?? [],
            qual?.Tests, qual?.Api ?? [], decl);
    }

    private static RubyReleaseUnit ToRuby(ReleaseUnitDeclaration decl)
    {
        ReleaseBuild build = decl.Builds.Single();
        ReleaseQualificationSpec qual = build.Qualification
            ?? throw new InvalidDataException($"Missing Ruby qualification on {decl.Id}.");
        return new(decl.Id, decl.SourcePath,
            qual.RSpec ? RubyPackageChecks.RSpecPreset : RubyPackageChecks.Preset,
            qual.Require ?? decl.Id, qual.NbgvEnv, qual.LockedBundle, qual.Dependencies ?? [],
            qual.Probe ?? throw new InvalidDataException($"Missing Ruby probe on {decl.Id}."),
            decl);
    }

    private static GithubReleaseUnit ToGithubRelease(ReleaseUnitDeclaration decl)
    {
        ReleaseBuild build = decl.Builds.Single();
        ReleaseQualificationSpec qual = build.Qualification
            ?? throw new InvalidDataException("Missing GitHub Release qualification.");
        return new(decl.Id, decl.SourcePath, ParentDirectory(decl.SourcePath), build.EntryPoint,
            qual.AssetPrefix ?? throw new InvalidDataException("Missing asset-prefix."),
            qual.Executable ?? throw new InvalidDataException("Missing executable."),
            qual.Editor ?? throw new InvalidDataException("Missing editor."), decl);
    }

    private static BrowserStoreUnit ToBrowserStore(ReleaseUnitDeclaration decl)
    {
        ReleaseBuild build = decl.Builds.Single();
        ReleaseQualificationSpec qual = build.Qualification
            ?? throw new InvalidDataException($"Missing browser-store qualification on {decl.Id}.");
        return new(decl.Id, decl.SourcePath, ParentDirectory(decl.SourcePath),
            qual.ManifestName ?? throw new InvalidDataException("Missing manifest-name."), decl);
    }

    private sealed record CatalogSnapshot(
        Dictionary<string, ReleaseUnitDeclaration> Declarations,
        Dictionary<string, string> Manifests);

    private static CatalogSnapshot LoadSnapshot()
    {
        Assembly assembly = typeof(ReleaseUnitCatalog).Assembly;
        var declarations = new Dictionary<string, ReleaseUnitDeclaration>(StringComparer.Ordinal);
        var manifests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string resource in assembly.GetManifestResourceNames()
                     .Order(StringComparer.Ordinal))
        {
            if (resource.StartsWith(UnitPrefix, StringComparison.Ordinal))
            {
                string sourcePath = resource[UnitPrefix.Length..];
                using Stream stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                ReleaseUnitDeclaration parsed = ReleaseUnitDeclarationReader.Read(
                    reader.ReadToEnd(), sourcePath);
                if (!declarations.TryAdd(parsed.Id, parsed))
                    throw new InvalidDataException(
                        $"Duplicate release-unit identity: {parsed.Id}.");
            }
            else if (resource.StartsWith(ManifestPrefix, StringComparison.Ordinal))
            {
                string manifestPath = resource[ManifestPrefix.Length..];
                using Stream stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                manifests.Add(manifestPath, reader.ReadToEnd());
            }
        }
        return new(declarations, manifests);
    }
}
