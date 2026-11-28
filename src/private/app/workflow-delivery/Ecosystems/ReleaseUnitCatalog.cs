using System.Reflection;
using System.Text.Json;
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
    internal string ProjectPath => ReleaseUnitCatalog.NormalizeRelativePath(
        ReleaseUnitCatalog.ParentDirectory(Declaration) + "/" + EntryPoint);
}

internal sealed record NpmReleaseUnit(string Id, string Declaration, string SourceName,
    string BuddyName, string OfficialName, string Main, bool RegistryDependencies,
    bool TestBeforeBuild, string Probe, ReleaseUnitDeclaration Source)
{
    internal NpmPackRecipe Native =>
        new(SourceName, Main, RegistryDependencies, Probe, TestBeforeBuild);
    internal string[] Required => Native.Required;
    internal string PackageName(ReleaseSubject subject) =>
        subject.Channel == "buddy" ? BuddyName : OfficialName;

    internal static string UnscopedName(string package) =>
        package.StartsWith('@') && package.IndexOf('/') is var slash and > 1
            ? package[(slash + 1)..] : package;

    internal NativeCommand Import(string directory, string package, int deadlineSeconds = 60) =>
        NpmPackageOperations.Import(directory, package, Probe, deadlineSeconds);
}

internal sealed record PythonReleaseUnit(string Id, string Declaration, string ProjectDirectory,
    bool StaticVersion, bool RuntimeClosure, string[] Dependencies, string? Tests, string[] Api,
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

    internal WindowsAssetsRecipe Native => new(AssetPrefix, Executable, Editor);
    internal string Asset(string kind, string version) => Native.Asset(kind, version);

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
        ByDefinition("dotnet/nuget-package-v1").Select(decl => ToNuGet(decl));

    internal static IEnumerable<NpmReleaseUnit> NpmUnits =>
        ByDefinition("node/npm-package-v1").Select(ToNpm);

    internal static NativeCommand AdoptedNpmImport(string directory) =>
        NpmUnits.Single(unit => unit.OfficialName == AdoptedNpmPackage.PackageName)
            .Import(directory, AdoptedNpmPackage.PackageName, deadlineSeconds: 30);

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
        bool allowedStoreChannel = subject.Channel == "official" ||
            (subject.Channel == "buddy" && subject.DryRun);
        if (subject.Declaration != unit.Declaration || !allowedStoreChannel ||
            !unit.Source.AllowsChannel(subject.Channel, subject.DryRun))
            throw new InvalidDataException("Unsupported browser-store release request.");
        return unit;
    }

    internal static PythonPackageOperations PythonPackage(string id, PythonPackageTools tools,
        string root)
    {
        PythonReleaseUnit unit = Python(id);
        return new(new(unit.Id, unit.ProjectDirectory, unit.StaticVersion, unit.RuntimeClosure,
            unit.Tests, unit.Api), tools, root);
    }

    internal static RubyPackageOperations RubyPackage(RubyReleaseUnit unit, RubyFactTools tools,
        string gem, string nbgv, string state) =>
        new(new(unit.Id, unit.Require, unit.NbgvEnv, unit.LockedBundle, unit.Dependencies,
            unit.Probe, unit.Preset == RubyPackageChecks.RSpecPreset), tools, gem, nbgv, state);

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

    internal static string NormalizeRelativePath(string path)
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

    internal static NuGetReleaseUnit ToNuGet(ReleaseUnitDeclaration decl,
        IReadOnlyDictionary<string, string>? manifests = null)
    {
        ReleaseBuild build = decl.Builds.Single();
        string directory = ParentDirectory(decl.SourcePath);
        string manifestPath = NormalizeRelativePath(directory + "/" + build.EntryPoint);
        string projectDirectory = ParentDirectory(manifestPath);
        IReadOnlyDictionary<string, string> store = manifests ?? Snapshot.Value.Manifests;
        string csprojXml = store.TryGetValue(manifestPath, out string? xml)
            ? xml
            : throw new InvalidDataException($"Missing embedded .csproj manifest {manifestPath}.");
        DotNetProjectManifest manifest = DotNetProjectManifest.Read(csprojXml);
        string stem = Path.GetFileNameWithoutExtension(build.EntryPoint);
        string packageId = manifest.PackageId ?? stem;
        string assemblyName = manifest.AssemblyName ?? stem;
        string[] companions = [.. manifest.ProjectReferences.Select(reference => ParentDirectory(
            NormalizeRelativePath(projectDirectory + "/" + reference.Replace('\\', '/'))))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        var qual = (NuGetQualification?)build.Qualification;
        return new(decl.Id, decl.SourcePath, build.EntryPoint, packageId, assemblyName,
            qual?.Probe, qual?.Analyzers ?? [], qual?.Declarations, companions, decl);
    }

    private static NpmReleaseUnit ToNpm(ReleaseUnitDeclaration decl)
    {
        ReleaseBuild build = decl.Builds.Single();
        string directory = ParentDirectory(decl.SourcePath);
        string manifestPath = NormalizeRelativePath(directory + "/" + build.EntryPoint);
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
        NpmQualification qual = build.Qualification as NpmQualification
            ?? throw new InvalidDataException($"Missing npm qualification on {decl.Id}.");
        return new(decl.Id, decl.SourcePath, sourceName, buddyName, sourceName,
            main, registryDeps, qual.TestBeforeBuild, qual.Probe, decl);
    }

    internal static PythonReleaseUnit ToPython(ReleaseUnitDeclaration decl)
    {
        ReleaseBuild build = decl.Builds.Single();
        var qual = (PythonQualification?)build.Qualification;
        bool staticVersion = qual?.StaticVersion ?? false;
        if (staticVersion && decl.Id != "nbgv-python")
            throw new InvalidDataException(
                "Static Python versioning is reserved for nbgv-python.");
        string entryPath = NormalizeRelativePath(
            ParentDirectory(decl.SourcePath) + "/" + build.EntryPoint);
        return new(decl.Id, decl.SourcePath, ParentDirectory(entryPath), staticVersion,
            qual?.RuntimeClosure ?? false, qual?.Dependencies ?? [],
            qual?.Tests, qual?.Api ?? [], decl);
    }

    private static RubyReleaseUnit ToRuby(ReleaseUnitDeclaration decl)
    {
        ReleaseBuild build = decl.Builds.Single();
        RubyQualification qual = build.Qualification as RubyQualification
            ?? throw new InvalidDataException($"Missing Ruby qualification on {decl.Id}.");
        return new(decl.Id, decl.SourcePath,
            qual.RSpec ? RubyPackageChecks.RSpecPreset : RubyPackageChecks.Preset,
            qual.Require ?? decl.Id, qual.NbgvEnv, qual.LockedBundle, qual.Dependencies,
            qual.Probe, decl);
    }

    private static GithubReleaseUnit ToGithubRelease(ReleaseUnitDeclaration decl)
    {
        ReleaseBuild build = decl.Builds.Single();
        WindowsAssetsQualification qual = build.Qualification as WindowsAssetsQualification
            ?? throw new InvalidDataException("Missing GitHub Release qualification.");
        return new(decl.Id, decl.SourcePath, ParentDirectory(decl.SourcePath), build.EntryPoint,
            qual.AssetPrefix, qual.Executable, qual.Editor, decl);
    }

    internal static BrowserStoreUnit ToBrowserStore(ReleaseUnitDeclaration decl)
    {
        if (decl.EffectiveChannels.Contains("buddy", StringComparer.Ordinal))
            throw new InvalidDataException(
                "Browser stores must remain Official-only for live releases.");
        ReleaseBuild build = decl.Builds.Single();
        BrowserExtensionQualification qual = build.Qualification as BrowserExtensionQualification
            ?? throw new InvalidDataException($"Missing browser-store qualification on {decl.Id}.");
        return new(decl.Id, decl.SourcePath, ParentDirectory(decl.SourcePath),
            qual.ManifestName, decl);
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
