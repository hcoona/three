using Microsoft.Build.Execution;
using NuGet.Commands;
using NuGet.ProjectModel;

namespace WorkflowDelivery.Repository;

internal sealed record DotNetRestoreInputs(DotNetGraphInput[] Inputs,
    DotNetInputProvider[] Packages, string[] GeneratedImports);

// Native restore owns effective contributors; candidates only cover later additions.
internal static class DotNetConfigurationInputs
{
    private static readonly StringComparer Paths = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static DotNetRestoreInputs Restore(ProjectInstance project,
        DotNetNodeIdentity consumer, string root, string[] committedPaths)
        => Read(project, consumer, root, committedPaths, false);

    internal static DotNetRestoreInputs Traversal(ProjectInstance project,
        DotNetNodeIdentity consumer, string root, string[] committedPaths)
    {
        if (!Paths.Equals(Path.GetFullPath(project.FullPath), Path.Combine(root, "dirs.proj")) ||
            project.GetPropertyValue("UsingMicrosoftTraversalSdk") != "true")
            throw new InvalidDataException("Unsupported traversal restore subject.");
        return Read(project, consumer, root, committedPaths, true);
    }

    private static DotNetRestoreInputs Read(ProjectInstance project,
        DotNetNodeIdentity consumer, string root, string[] committedPaths, bool traversal)
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
            restore.ProjectStyle != ProjectStyle.PackageReference ||
            !Paths.Equals(Absolute(restore.ProjectPath), Path.GetFullPath(project.FullPath)) ||
            restore.RestoreLockProperties is null ||
            !restore.RestoreLockProperties.RestoreLockedMode ||
            restore.OriginalTargetFrameworks.Count == 0 || assets.Targets.Count == 0 ||
            restore.ConfigFilePaths.Count == 0 ||
            !restore.ConfigFilePaths.Select(Absolute).Contains(configuration, Paths) ||
            !committedPaths.Contains(configuration, Paths))
            throw new InvalidDataException("Missing or different native locked restore metadata.");
        if (!File.Exists(assetsPath) ||
            restore.ConfigFilePaths.Any(path => !File.Exists(Absolute(path))))
            throw new InvalidDataException("Native locked restore input is unavailable.");
        var inputs = new List<DotNetGraphInput>
        {
            new(assetsPath, "RestoreAssets", "LockedRestore", consumer,
                new("RestoreAssets", assetsPath, restore.ProjectPath, "")),
        };
        if (!traversal)
        {
            string lockPath = Absolute(PackagesLockFileUtilities.GetNuGetLockFilePath(spec));
            if (!File.Exists(lockPath))
                throw new InvalidDataException("Native locked restore input is unavailable.");
            inputs.Add(new(lockPath, "RestoreLock", "LockedRestore", consumer));
        }
        else
        {
            foreach (ProjectItemInstance reference in project.GetItems("GlobalPackageReference"))
            {
                string version = reference.GetMetadataValue("Version");
                var library = assets.Libraries.SingleOrDefault(value => value.Type == "package" &&
                    value.Name.Equals(reference.EvaluatedInclude,
                        StringComparison.OrdinalIgnoreCase));
                // NuGet owns applicability. An inactive declaration supplies no provider.
                if (library is not null && version != library.Version.ToNormalizedString())
                    throw new InvalidDataException(
                        "Traversal global package differs from its native restore: " +
                        reference.EvaluatedInclude + ", declared " + version + ", restored " +
                        library.Version.ToNormalizedString());
            }
        }
        foreach (string path in restore.ConfigFilePaths)
            inputs.Add(new(Absolute(path), "RestoreConfiguration", "LockedRestore", consumer,
                new("RestoreEnvironment", Absolute(path), restore.ProjectPath, "")));
        string cpm = project.GetPropertyValue("DirectoryPackagesPropsPath");
        if (!string.IsNullOrEmpty(cpm))
            inputs.Add(new(Path.GetFullPath(cpm, Path.GetDirectoryName(project.FullPath)!),
                "CentralPackageConfiguration", "Evaluation", consumer));
        DotNetInputProvider[] packages = assets.Libraries.Where(library =>
            library.Type == "package" && !string.IsNullOrEmpty(library.Path))
            .SelectMany(library => assets.PackageFolders.Select(folder =>
                new DotNetInputProvider(traversal ? "TraversalPackage" : "LockedPackage",
                    Path.GetFullPath(Path.Combine(
                    Absolute(folder.Path), library.Path)), library.Name,
                    library.Version.ToNormalizedString()))).ToArray();
        string[] generated = new[] { BuildAssetsUtils.PropsExtension,
                BuildAssetsUtils.TargetsExtension }
            .Select(extension => Absolute(BuildAssetsUtils
                .GetMSBuildFilePathForPackageReferenceStyleProject(spec, extension)))
            .ToArray();
        return new([.. inputs], packages, generated);
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
