using System.Text.Json;
using System.Xml.Linq;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Versioning;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

// A concrete external consumer of the inspected original package, not a test launcher.
internal static class DotNetPackageConsumer
{
    internal const string Marker = "hcoona-release-smoke-github-packages";

    internal static async Task<string> RunAsync(DotNetOriginalPackage original,
        Dictionary<string, string> native, string root, int deadline,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> run,
        CancellationToken token)
    {
        DotNetPackageArchive.RequireOriginal(original);
        PackageIdentity identity = DotNetPackageArchive.Identity(original.Path);
        DotNetPackageArchive.RequireIdentity(identity, native);
        string feed = Directory.CreateDirectory(Path.Combine(root, "feed")).FullName;
        File.Copy(original.Path, Path.Combine(feed, Path.GetFileName(original.Path)));
        string consumer = Directory.CreateDirectory(Path.Combine(root, "consumer")).FullName;
        string packages = Directory.CreateDirectory(Path.Combine(root, "packages")).FullName;
        string profile = Directory.CreateDirectory(Path.Combine(root, "profile")).FullName;
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["NUGET_PACKAGES"] = packages,
            ["NUGET_HTTP_CACHE_PATH"] =
                Directory.CreateDirectory(Path.Combine(root, "http")).FullName,
            ["NUGET_PLUGINS_CACHE_PATH"] =
                Directory.CreateDirectory(Path.Combine(root, "plugins")).FullName,
            ["DOTNET_CLI_HOME"] = Directory.CreateDirectory(Path.Combine(root, "cli")).FullName,
            ["HOME"] = profile,
            ["USERPROFILE"] = profile,
            ["APPDATA"] = Directory.CreateDirectory(Path.Combine(profile, "appdata")).FullName,
            ["LOCALAPPDATA"] =
                Directory.CreateDirectory(Path.Combine(profile, "localappdata")).FullName,
            ["XDG_CONFIG_HOME"] =
                Directory.CreateDirectory(Path.Combine(profile, "config")).FullName,
            ["XDG_DATA_HOME"] = Directory.CreateDirectory(Path.Combine(profile, "data")).FullName,
            ["NUGET_PLUGIN_PATHS"] = null,
            ["NUGET_NETCORE_PLUGIN_PATHS"] = null,
            ["NUGET_CREDENTIALPROVIDERS_PATH"] = null,
        };
        string project = Path.Combine(consumer, "Consumer.csproj");
        string config = Path.Combine(consumer, "NuGet.Config");
        var range = new VersionRange(identity.Version, true, identity.Version, true);
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup",
                new XElement("OutputType", "Exe"),
                new XElement("TargetFramework", native["TargetFramework"]),
                new XElement("UseAppHost", "false"),
                new XElement("ImportDirectoryBuildProps", "false"),
                new XElement("ImportDirectoryBuildTargets", "false"),
                new XElement("ManagePackageVersionsCentrally", "false"),
                new XElement("DisableImplicitLibraryPacksFolder", "true"),
                new XElement("RestoreFallbackFolders", ""),
                new XElement("RestoreAdditionalProjectFallbackFolders", ""),
                new XElement("RestoreAdditionalProjectSources", "")),
            new XElement("ItemGroup", new XElement("PackageReference",
                new XAttribute("Include", identity.Id),
                new XAttribute("Version", range.ToNormalizedString()))))).Save(project);
        new XDocument(new XElement("configuration",
            new XElement("packageSources", new XElement("clear"), new XElement("add",
                new XAttribute("key", "original"), new XAttribute("value", feed))),
            new XElement("packageSourceMapping", new XElement("clear")),
            new XElement("disabledPackageSources", new XElement("clear")),
            new XElement("fallbackPackageFolders", new XElement("clear")))).Save(config);
        await using (var file = new FileStream(Path.Combine(consumer, "global.json"),
                         FileMode.CreateNew))
        await using (var writer = new Utf8JsonWriter(file))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("sdk");
            writer.WriteString("version", native["NETCoreSdkVersion"]);
            writer.WriteString("rollForward", "disable");
            writer.WriteEndObject();
            writer.WriteEndObject();
            await writer.FlushAsync(token);
        }
        await File.WriteAllTextAsync(Path.Combine(consumer, "Program.cs"),
            "System.Console.WriteLine(HcoonaReleaseSmokeGithubPackages.Smoke.ProjectId);", token);
        await run(new("dotnet", consumer,
            ["restore", project, "--configfile", config, "--packages", packages],
            deadline, environment), token);
        string assetsPath = Path.Combine(consumer, "obj", "project.assets.json");
        RequireAssets(assetsPath, project, packages, feed, config, identity, original);
        string response = Path.Combine(consumer, "build.json");
        await run(new("dotnet", consumer,
            ["msbuild", project, "-nologo", "-noAutoResponse", "-target:Build,GetTargetPath",
                "-getTargetResult:Build,GetTargetPath", "-getResultOutputFile:" + response],
            deadline, environment), token);
        using JsonDocument document = await DotNetPackageChecks.ResponseAsync(response, token);
        DotNetPackageChecks.Target(document.RootElement, "Build");
        JsonElement target = DotNetPackageChecks.Target(document.RootElement, "GetTargetPath");
        if (!target.TryGetProperty("Items", out JsonElement items) ||
            items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != 1 ||
            items[0].ValueKind != JsonValueKind.Object ||
            !items[0].TryGetProperty("FullPath", out JsonElement path) ||
            path.ValueKind != JsonValueKind.String ||
            !Path.IsPathFullyQualified(path.GetString()!))
            throw new InvalidDataException("Missing native consumer GetTargetPath output.");
        string output = Path.GetFullPath(path.GetString()!);
        if (!output.StartsWith(consumer + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                    StringComparison.Ordinal))
            throw new InvalidDataException(
                "Native consumer output is outside its owned directory.");
        RequireFile(output);
        NativeCommandResult invocation = await run(new("dotnet", consumer, [output],
            Math.Min(deadline, 30), environment), token);
        if (invocation.Stdout.Trim() != Marker)
            throw new InvalidDataException(
                "The installed package marker API returned a different value.");
        DotNetPackageArchive.RequireOriginal(original);
        return output;
    }

    internal static void RequireAssets(string path, string project, string packages,
        string feed, string config, PackageIdentity identity, DotNetOriginalPackage original)
    {
        RequireFile(path);
        LockFile assets = new LockFileFormat().Read(path) ??
            throw new InvalidDataException("Missing native consumer restore assets.");
        ProjectRestoreMetadata? restore = assets.PackageSpec?.RestoreMetadata;
        if (assets.Version != LockFileFormat.Version)
            throw AssetsMismatch("format version", LockFileFormat.Version, assets.Version);
        if (restore is null)
            throw AssetsMismatch("restore metadata", "present", "missing");
        if (!SamePath(restore.ProjectPath, project))
            throw AssetsMismatch("project path", project, LocalPath(restore.ProjectPath));
        if (restore.ProjectStyle != ProjectStyle.PackageReference)
            throw AssetsMismatch("project style", ProjectStyle.PackageReference,
                restore.ProjectStyle);
        if (assets.PackageFolders is not [var folder])
            throw AssetsMismatch("package folder count", 1, assets.PackageFolders.Count);
        if (!SamePath(folder.Path, packages))
            throw AssetsMismatch("package folder", packages, LocalPath(folder.Path));
        if (restore.FallbackFolders.Count != 0)
            throw AssetsMismatch("fallback folder count", 0, restore.FallbackFolders.Count);
        if (restore.Sources is not [var source])
            throw AssetsMismatch("source count", 1,
                $"{restore.Sources.Count} [{string.Join(", ",
                    restore.Sources.Take(4).Select(item => LocalPath(item.Source)))}]");
        if (!SamePath(source.Source, feed))
            throw AssetsMismatch("source path", feed, LocalPath(source.Source));
        if (restore.ConfigFilePaths is not [string configured])
            throw AssetsMismatch("configuration file count", 1,
                $"{restore.ConfigFilePaths.Count} [{string.Join(", ",
                    restore.ConfigFilePaths.Take(4).Select(LocalPath))}]");
        if (!SamePath(configured, config))
            throw AssetsMismatch("configuration file path", config, LocalPath(configured));
        if (assets.Libraries is not [var library])
            throw AssetsMismatch("package library count", 1, assets.Libraries.Count);
        if (library.Type != "package")
            throw AssetsMismatch("library type", "package", library.Type);
        if (!PackageIdentityComparer.Default.Equals(new(library.Name, library.Version), identity))
            throw AssetsMismatch("library identity", identity,
                new PackageIdentity(library.Name, library.Version));
        if (assets.Targets is not [var target])
            throw AssetsMismatch("target count", 1, assets.Targets.Count);
        if (target.Libraries is not [var restored])
            throw AssetsMismatch("target package library count", 1, target.Libraries.Count);
        if (restored.Type != "package")
            throw AssetsMismatch("target library type", "package", restored.Type);
        if (string.IsNullOrWhiteSpace(restored.Name))
            throw AssetsMismatch("target library name", identity.Id, "missing");
        if (!PackageIdentityComparer.Default.Equals(new(restored.Name, restored.Version), identity))
            throw AssetsMismatch("target library identity", identity,
                new PackageIdentity(restored.Name, restored.Version));
        string installed = new VersionFolderPathResolver(folder.Path)
            .GetPackageFilePath(identity.Id, identity.Version);
        RequireFile(installed);
        if (new FileInfo(installed).Length != original.Size ||
            DotNetPackageArchive.Digest(installed) != original.Sha256)
            throw new InvalidDataException(
                "The restored package bytes differ from the original archive.");
    }

    private static InvalidDataException AssetsMismatch(string field, object expected,
        object? actual) => new(
        $"Native consumer assets differ from the isolated package contract: {field}; " +
        $"expected {expected}; actual {actual ?? "<missing>"}.");

    private static string LocalPath(string path) => Path.IsPathFullyQualified(path)
        ? path : "<not a local absolute path>";

    private static bool SamePath(string left, string right) => Path.IsPathFullyQualified(left) &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static void RequireFile(string path)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) &
                (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("Required native output is not a regular file.");
    }
}
