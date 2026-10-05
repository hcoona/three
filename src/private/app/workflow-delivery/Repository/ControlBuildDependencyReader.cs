using Microsoft.Build.Framework;
using NuGet.Packaging;
using NuGet.ProjectModel;

namespace WorkflowDelivery.Repository;

internal sealed record ControlBuildDependencies(ControlDependencyDirectory[] Directories,
    string[] RestoreConfigurationFiles, string[] RestoreGeneratedFiles);

// Consume NuGet's restored model and the SDK resolver's actual import locations.
// Dependency resolution and locked-mode validation remain native operations.
internal static class ControlBuildDependencyReader
{
    internal static ControlBuildDependencies Read(ControlBuildContext operation,
        ControlBuildLog log)
    {
        operation.RequireSameOperation(log);
        LockFile assets = new LockFileFormat().Read(operation.AssetsFile)
            ?? throw new InvalidDataException("Native restore assets are unavailable.");
        ProjectRestoreMetadata restore = assets.PackageSpec?.RestoreMetadata
            ?? throw new InvalidDataException("Native restore context is unavailable.");
        if (log.Project != operation.Project || assets.Version <= 0 ||
            restore.ProjectPath != operation.Project ||
            !restore.RestoreLockProperties.RestoreLockedMode ||
            !restore.OriginalTargetFrameworks.SequenceEqual(
                [operation.Dimension.TargetFramework]) ||
            Path.TrimEndingDirectorySeparator(restore.OutputPath) !=
                operation.BaseIntermediateDirectory ||
            assets.Targets.Count != 1 ||
            !string.IsNullOrEmpty(assets.Targets[0].RuntimeIdentifier) ||
            assets.Targets[0].TargetFramework.GetShortFolderName(
                ) != operation.Dimension.TargetFramework)
            throw new InvalidDataException(
                "Native restore belongs to a different control operation.");
        var directories = new Dictionary<string, ControlDependencyDirectory>(
            StringComparer.Ordinal);
        foreach (LockFileLibrary library in assets.Libraries)
        {
            if (library.Type != "package" || string.IsNullOrWhiteSpace(library.Name) ||
                library.Version is null || string.IsNullOrWhiteSpace(library.Path) ||
                string.IsNullOrWhiteSpace(library.Sha512))
                throw new InvalidDataException("Unsupported native restore dependency identity.");
            string[] candidates = assets.PackageFolders.Select(folder =>
                Child(folder.Path, library.Path)).Where(Directory.Exists).ToArray();
            if (candidates.Length != 1)
                throw new InvalidDataException("Native restore package location is ambiguous.");
            string directory = candidates[0];
            string[] files = library.Files.Select(file => Child(directory, file)).ToArray();
            directories.Add(directory, new(
                library.Name + "/" + library.Version.ToNormalizedString(),
                directory, files));
        }
        foreach (ControlBuildImport import in log.Imports)
        {
            string manifests = Path.Combine(operation.DotNetDirectory, "sdk-manifests");
            string manifest = Path.GetRelativePath(manifests, import.Path);
            if (Path.GetFileName(import.Path) == "WorkloadManifest.targets" &&
                !Path.IsPathRooted(manifest) &&
                !manifest.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                string manifestDirectory = Path.GetDirectoryName(import.Path)!;
                directories.TryAdd(manifestDirectory,
                    new("selected SDK workload manifest/" + manifest,
                        manifestDirectory, [import.Path]));
            }
            string? sdk = Path.GetDirectoryName(import.Path);
            if (sdk is null || Path.GetFileName(sdk) != "Sdk" ||
                Path.GetFileName(import.Path) is not ("Sdk.props" or "Sdk.targets")) continue;
            string directory = Path.GetDirectoryName(sdk)!;
            string relative = Path.GetRelativePath(operation.PackageDirectory, directory);
            if (Path.IsPathRooted(relative) || relative.StartsWith("..",
                StringComparison.Ordinal) ||
                relative.Split(Path.DirectorySeparatorChar).Length != 2) continue;
            // These imports were selected by the maintained resolver, including its
            // global.json SDK versions. Read their package identity with NuGet itself.
            using var reader = new PackageFolderReader(directory);
            NuGet.Packaging.Core.PackageIdentity identity = reader.GetIdentity();
            string[] files = reader.GetFiles().Select(file => Child(directory, file)).ToArray();
            directories.TryAdd(directory, new(identity.Id + "/" +
                identity.Version.ToNormalizedString(), directory, files));
        }
        AddFrameworkPacks(operation, log, directories);
        string extensions = restore.OutputPath;
        string project = Path.GetFileName(operation.Project);
        return new(directories.Values.OrderBy(d => d.Identity, StringComparer.Ordinal).ToArray(),
            restore.ConfigFilePaths.Select(Path.GetFullPath).ToArray(),
            [operation.AssetsFile, Path.Combine(extensions, project + ".nuget.g.props"),
                Path.Combine(extensions, project + ".nuget.g.targets")]);
    }

    private static void AddFrameworkPacks(ControlBuildContext operation, ControlBuildLog log,
        Dictionary<string, ControlDependencyDirectory> directories)
    {
        string root = Path.Combine(operation.DotNetDirectory, "packs");
        foreach (ControlBuildTask task in log.Tasks.Where(
            t => t.Name == "ResolveTargetingPackAssets"))
        {
            ControlBuildValue[] packs = task.Parameters.Where(p =>
                p.Kind == TaskParameterMessageKind.TaskInput && p.Name == "ResolvedTargetingPacks")
                .SelectMany(p => p.Values).ToArray();
            foreach (ControlBuildValue pack in packs)
            {
                if (!pack.Metadata.TryGetValue("PackageDirectory", out string? directory) ||
                    !pack.Metadata.TryGetValue("NuGetPackageId", out string? id) ||
                    !pack.Metadata.TryGetValue("NuGetPackageVersion", out string? version) ||
                    string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
                    throw new InvalidDataException(
                        "Native targeting pack identity is unavailable.");
                directory = Path.GetFullPath(directory);
                if (directory != Child(root, id + "/" + version))
                    throw new InvalidDataException(
                        "Native targeting pack location is unsupported.");
                string[] files = task.Parameters.Where(p =>
                    p.Kind == TaskParameterMessageKind.TaskOutput &&
                    p.Name is "ReferencesToAdd" or "AnalyzersToAdd" or "PlatformManifests")
                    .SelectMany(p => p.Values).Select(v => Path.GetFullPath(v.Identity))
                    .Where(file => IsChild(directory, file)).Distinct(
                        StringComparer.Ordinal).ToArray();
                directories.TryAdd(directory, new("selected SDK pack/" + id + "/" + version,
                    directory, files));
            }
        }
        foreach (ControlBuildValue host in log.Tasks.Where(t => t.Name == "ResolveAppHosts")
            .SelectMany(t => t.Parameters).Where(
                p => p.Kind == TaskParameterMessageKind.TaskOutput &&
                p.Name == "AppHost").SelectMany(p => p.Values))
        {
            if (!host.Metadata.TryGetValue("PackageDirectory", out string? directory) ||
                !host.Metadata.TryGetValue("Path", out string? path) ||
                !host.Metadata.TryGetValue("PathInPackage", out string? relative))
                throw new InvalidDataException("Native apphost identity is unavailable.");
            directory = Path.GetFullPath(directory);
            if (!IsChild(root, directory) || Path.GetFullPath(path) != Child(directory, relative))
                throw new InvalidDataException("Native apphost location is unsupported.");
            directories.TryAdd(directory, new("selected SDK apphost/" +
                Path.GetRelativePath(root, directory).Replace(Path.DirectorySeparatorChar, '/'),
                directory, [Path.GetFullPath(path)]));
        }
    }

    private static bool IsChild(string directory, string path)
    {
        string relative = Path.GetRelativePath(directory, path);
        return relative != "." && !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string Child(string directory, string path)
    {
        string absolute = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar),
            directory);
        string relative = Path.GetRelativePath(directory, absolute);
        if (relative == "." || Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Native package file escapes its resolved directory.");
        return absolute;
    }
}
