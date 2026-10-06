using Microsoft.Build.Execution;
using NuGet.ProjectModel;

namespace WorkflowDelivery.Repository;

// Native restore owns effective contributors; candidates only cover later additions.
internal static class DotNetConfigurationInputs
{
    private static readonly StringComparer Paths = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static IEnumerable<DotNetGraphInput> Restore(ProjectInstance project,
        DotNetNodeIdentity consumer, string root, string[] committedPaths)
    {
        string assetsPath = Absolute(project.GetPropertyValue("ProjectAssetsFile"));
        LockFile assets = new LockFileFormat().Read(assetsPath)
            ?? throw new InvalidDataException("Native restore assets are unavailable.");
        PackageSpec spec = assets.PackageSpec
            ?? throw new InvalidDataException("Native restore project is unavailable.");
        ProjectRestoreMetadata restore = spec.RestoreMetadata
            ?? throw new InvalidDataException("Native restore metadata is unavailable.");
        string configuration = Path.Combine(root, "nuget.config");
        if (assets.Version != LockFileFormat.Version ||
            !Paths.Equals(Absolute(restore.ProjectPath), Path.GetFullPath(project.FullPath)) ||
            restore.RestoreLockProperties is null ||
            !restore.RestoreLockProperties.RestoreLockedMode ||
            restore.OriginalTargetFrameworks.Count == 0 || assets.Targets.Count == 0 ||
            restore.ConfigFilePaths.Count == 0 ||
            !restore.ConfigFilePaths.Select(Absolute).Contains(configuration, Paths) ||
            !committedPaths.Contains(configuration, Paths))
            throw new InvalidDataException("Missing or different native locked restore metadata.");
        string lockPath = Absolute(PackagesLockFileUtilities.GetNuGetLockFilePath(spec));
        if (!File.Exists(assetsPath) || !File.Exists(lockPath) ||
            restore.ConfigFilePaths.Any(path => !File.Exists(Absolute(path))))
            throw new InvalidDataException("Native locked restore input is unavailable.");
        yield return new(assetsPath, "RestoreAssets", "LockedRestore", consumer);
        yield return new(lockPath, "RestoreLock", "LockedRestore", consumer);
        foreach (string path in restore.ConfigFilePaths)
            yield return new(Absolute(path), "RestoreConfiguration", "LockedRestore", consumer);
        string cpm = project.GetPropertyValue("DirectoryPackagesPropsPath");
        if (!string.IsNullOrEmpty(cpm))
            yield return new(Path.GetFullPath(cpm, Path.GetDirectoryName(project.FullPath)!),
                "CentralPackageConfiguration", "Evaluation", consumer);
    }

    internal static IEnumerable<DotNetGraphInput> Candidates(ProjectInstance project,
        DotNetNodeIdentity consumer, string root)
    {
        yield return new(Path.Combine(root, "global.json"), "SdkConfigurationCandidate",
            "Evaluation", consumer);
        if (!DotNetOwnership.IsManagedProject(project)) yield break;
        string directory = Path.GetDirectoryName(project.FullPath)!;
        yield return new(Path.Combine(directory, "packages.lock.json"),
            "RestoreLockCandidate", "Evaluation", consumer);
        while (Paths.Equals(root, directory) || directory.StartsWith(
            root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            foreach (string name in new[] { "Directory.Build.props", "Directory.Build.targets",
                "Directory.Packages.props", "NuGet.Config", "nuget.config" })
                yield return new(Path.Combine(directory, name), "ConfigurationCandidate",
                    "Evaluation", consumer);
            if (Paths.Equals(root, directory)) break;
            directory = Path.GetDirectoryName(directory)!;
        }
    }

    private static string Absolute(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Native restore coordinates must be absolute.");
        return Path.GetFullPath(path);
    }
}
