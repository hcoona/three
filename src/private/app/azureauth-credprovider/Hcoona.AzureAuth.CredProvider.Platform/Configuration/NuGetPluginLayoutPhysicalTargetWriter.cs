using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal sealed class NuGetPluginLayoutPhysicalTargetWriter(IFileSystem fileSystem)
    : IConfigurationPhysicalTargetWriter
{
    internal const string MarkerFileName = ".azureauth-credprovider.nuget-plugin-layout";
    internal const string LegacyMarkerValue =
        "azureauth-credprovider nuget-plugin-layout\n"
        + "phase=10\n"
        + "runtime=netcore\n"
        + "entrypoint=azureauth-credprovider.dll\n";
    private const string MarkerSchemaVersion =
        "azureauth-credprovider-nuget-plugin-activation-v1";
    private const string PluginEntrypointFileName = "azureauth-credprovider.dll";
    private const string SupportedKey = "physical-target";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);
    private static readonly JsonSerializerOptions MarkerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = NuGetPluginActivationJsonContext.Default,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public void Validate(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        if (
            request.PlanOperation == ConfigurationPlanOperation.Remove
            || request.Change.Operation == ConfigurationChangeOperation.Remove
        )
        {
            ValidateCurrentState(request);
            return;
        }

        SourcePayload payload = ReadSourcePayload(request.Change.Value!);
        ValidateDisjointRoots(request.Change.TargetPathOrName, payload);
        ExistingActivation? existingActivation = ValidateCurrentState(
            request,
            allowNewOwnershipIntent: false,
            payload
        );
        ValidateNewTargets(request.Change.TargetPathOrName, existingActivation, payload);
    }

    public void Write(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        bool remove =
            request.PlanOperation == ConfigurationPlanOperation.Remove
            || request.Change.Operation == ConfigurationChangeOperation.Remove;
        if (remove)
        {
            ExistingActivation? removalActivation = ValidateCurrentState(
                request,
                allowNewOwnershipIntent: false
            );
            RemoveActivation(request.Change.TargetPathOrName, removalActivation);
            return;
        }

        SourcePayload payload = ReadSourcePayload(request.Change.Value!);
        ValidateDisjointRoots(request.Change.TargetPathOrName, payload);
        ExistingActivation? existingActivation = ValidateCurrentState(
            request,
            allowNewOwnershipIntent: true,
            payload
        );
        ValidateNewTargets(request.Change.TargetPathOrName, existingActivation, payload);
        if (
            existingActivation is { IsLegacy: false }
            && ManifestsEquivalent(existingActivation.Manifest, payload.Manifest)
            && TargetModesMatch(request.Change.TargetPathOrName, payload)
        )
        {
            return;
        }
        ReplaceActivation(request.Change.TargetPathOrName, existingActivation, payload);
    }

    public bool IsSatisfied(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        if (
            request.PlanOperation == ConfigurationPlanOperation.Remove
            || request.Change.Operation == ConfigurationChangeOperation.Remove
        )
        {
            return !fileSystem.DirectoryExists(request.Change.TargetPathOrName)
                && !fileSystem.FileExists(GetMarkerPath(request.Change.TargetPathOrName));
        }

        try
        {
            SourcePayload payload = ReadSourcePayload(request.Change.Value!);
            ExistingActivation? existing = ReadAndValidateActivation(
                request.Change.TargetPathOrName,
                request.Change.Value,
                payload
            );
            return existing is { IsLegacy: false }
                && ManifestsEquivalent(existing.Manifest, payload.Manifest)
                && TargetModesMatch(request.Change.TargetPathOrName, payload);
        }
        catch (
            Exception exception
        ) when (
            exception
                is ArgumentException
                    or IOException
                    or InvalidOperationException
                    or JsonException
                    or UnauthorizedAccessException
        )
        {
            return false;
        }
    }

    private bool TargetModesMatch(string targetRootPath, SourcePayload payload)
    {
        if (FileSystemPathSemantics.UsesWindowsPaths(fileSystem))
        {
            return true;
        }

        return payload.Files.All(file =>
            file.UnixFileMode is { } mode
            && fileSystem.GetUnixFileMode(
                GetOwnedTargetPath(targetRootPath, file.RelativePath)
            ) == mode
        );
    }

    internal static string? GetPlanningValidationViolation(ConfigurationChange change)
    {
        if (change.TargetKind != ConfigurationTargetKind.NuGetPluginLayout)
        {
            return null;
        }
        if (!string.Equals(change.Key, SupportedKey, StringComparison.Ordinal))
        {
            return "The NuGet plugin layout writer requires the physical-target key.";
        }
        if (
            change.Operation
            is not (
                ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh
                or ConfigurationChangeOperation.Remove
            )
        )
        {
            return "The NuGet plugin layout writer supports value-writing and remove operations.";
        }
        if (
            change.Operation != ConfigurationChangeOperation.Remove
            && string.IsNullOrWhiteSpace(change.Value)
        )
        {
            return "The NuGet plugin source application root is required.";
        }
        return change.IsSecretValue
            ? "The NuGet plugin layout value must not contain secrets."
            : null;
    }

    private void ValidateRequest(ConfigurationPhysicalTargetWriterRequest request)
    {
        if (
            request.TargetKind != ConfigurationTargetKind.NuGetPluginLayout
            || request.Changes.Count != 1
        )
        {
            throw new NotSupportedException(
                "The NuGet plugin layout writer requires one layout change."
            );
        }
        string? violation =
            GetTargetRootPathValidationViolation(request.Change.TargetPathOrName)
            ?? GetPlanningValidationViolation(request.Change);
        if (violation is not null)
        {
            throw new NotSupportedException(violation);
        }
    }

    private string? GetTargetRootPathValidationViolation(string targetRootPath) =>
        string.IsNullOrWhiteSpace(targetRootPath)
        || !fileSystem.IsPathFullyQualified(targetRootPath)
            ? "The NuGet plugin layout target must be fully qualified."
            : null;

    private ExistingActivation? ValidateCurrentState(
        ConfigurationPhysicalTargetWriterRequest request,
        bool allowNewOwnershipIntent = false,
        SourcePayload? legacyPayload = null
    )
    {
        string targetRootPath = request.Change.TargetPathOrName;
        string markerPath = GetMarkerPath(targetRootPath);
        bool targetDirectoryExists = fileSystem.DirectoryExists(targetRootPath);
        bool targetFileExists = fileSystem.FileExists(targetRootPath);
        bool markerExists = fileSystem.FileExists(markerPath);
        bool markerDirectoryExists = fileSystem.DirectoryExists(markerPath);
        bool remove =
            request.PlanOperation == ConfigurationPlanOperation.Remove
            || request.Change.Operation == ConfigurationChangeOperation.Remove;

        if (targetFileExists || markerDirectoryExists)
        {
            throw new InvalidOperationException(
                "The NuGet plugin activation target is not a recognized directory layout."
            );
        }
        if (remove && !request.IsOwned(request.Change, fileSystem))
        {
            throw new InvalidOperationException(
                "NuGet plugin activation removal requires recognized ownership."
            );
        }
        if (!targetDirectoryExists && !markerExists)
        {
            return null;
        }
        if (!markerExists)
        {
            if (
                remove
                || (
                    !allowNewOwnershipIntent
                    && request.IsOwned(request.Change, fileSystem)
                )
            )
            {
                throw new InvalidOperationException(
                    "The NuGet plugin activation directory exists without its ownership marker."
                );
            }

            return null;
        }
        if (!request.IsOwned(request.Change, fileSystem))
        {
            throw new InvalidOperationException(
                "The NuGet plugin activation marker exists without recognized ownership."
            );
        }

        return ReadAndValidateActivation(
            targetRootPath,
            remove ? null : request.Change.Value,
            legacyPayload
        );
    }

    private SourcePayload ReadSourcePayload(string sourceRootPath)
    {
        if (
            !fileSystem.IsPathFullyQualified(sourceRootPath)
            || !fileSystem.DirectoryExists(sourceRootPath)
        )
        {
            throw new InvalidOperationException(
                "The NuGet plugin source application root is unavailable."
            );
        }

        string fullSourceRoot = fileSystem.GetFullPath(sourceRootPath);
        StringComparer pathComparer = FileSystemPathSemantics.GetComparer(fileSystem);
        var files = new List<SourcePayloadFile>();
        foreach (
            string sourcePath in fileSystem
                .EnumerateFiles(fullSourceRoot, "*", SearchOption.AllDirectories)
                .OrderBy(static path => path, pathComparer)
        )
        {
            string relativePath = GetSafeRelativePath(fullSourceRoot, sourcePath);
            if (pathComparer.Equals(relativePath, MarkerFileName))
            {
                throw new InvalidOperationException(
                    "The source application payload contains the reserved NuGet activation marker."
                );
            }

            byte[] content = fileSystem.ReadAllBytes(sourcePath);
            UnixFileMode? unixFileMode = FileSystemPathSemantics.UsesWindowsPaths(fileSystem)
                ? null
                : fileSystem.GetUnixFileMode(sourcePath);
            files.Add(
                new SourcePayloadFile(
                    relativePath,
                    content,
                    Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                    unixFileMode
                )
            );
        }

        if (
            files.Count == 0
            || !files.Any(file =>
                string.Equals(
                    file.RelativePath,
                    PluginEntrypointFileName,
                    StringComparison.Ordinal
                )
            )
        )
        {
            throw new InvalidOperationException(
                "The NuGet plugin source application payload is incomplete."
            );
        }

        return new SourcePayload(
            new NuGetPluginActivationManifest
            {
                SchemaVersion = MarkerSchemaVersion,
                SourceApplicationRoot = fullSourceRoot,
                Files = files
                    .Select(file => new NuGetPluginActivationFile
                    {
                        Path = file.RelativePath,
                        Length = file.Content.LongLength,
                        Sha256 = file.Sha256,
                        UnixFileMode = file.UnixFileMode is null
                            ? null
                            : (int)file.UnixFileMode.Value,
                    })
                    .ToArray(),
            },
            files
        );
    }

    private ExistingActivation? ReadAndValidateActivation(
        string targetRootPath,
        string? expectedSourceRoot,
        SourcePayload? legacyPayload = null
    )
    {
        string markerPath = GetMarkerPath(targetRootPath);
        if (!fileSystem.FileExists(markerPath))
        {
            return null;
        }

        string marker = fileSystem.ReadAllText(markerPath, Utf8NoBom);
        if (string.Equals(marker, LegacyMarkerValue, StringComparison.Ordinal))
        {
            return ReadLegacyActivation(targetRootPath, legacyPayload);
        }

        NuGetPluginActivationManifest manifest =
            JsonSerializer.Deserialize<NuGetPluginActivationManifest>(
                marker,
                MarkerJsonOptions
            )
            ?? throw new InvalidOperationException(
                "The NuGet plugin activation marker is invalid."
            );
        if (
            !string.Equals(manifest.SchemaVersion, MarkerSchemaVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(manifest.SourceApplicationRoot)
            || !fileSystem.IsPathFullyQualified(manifest.SourceApplicationRoot)
            || manifest.Files.Length == 0
            || (
                expectedSourceRoot is not null
                && !string.Equals(
                    fileSystem.GetFullPath(manifest.SourceApplicationRoot),
                    fileSystem.GetFullPath(expectedSourceRoot),
                    FileSystemPathSemantics.GetComparison(fileSystem)
                )
            )
        )
        {
            throw new InvalidOperationException(
                "The NuGet plugin activation marker is not recognized."
            );
        }

        var listedPaths = new HashSet<string>(FileSystemPathSemantics.GetComparer(fileSystem));
        bool entrypointListed = false;
        foreach (NuGetPluginActivationFile entry in manifest.Files)
        {
            string targetPath = GetOwnedTargetPath(targetRootPath, entry.Path);
            if (
                !listedPaths.Add(entry.Path)
                || entry.Length < 0
                || string.IsNullOrWhiteSpace(entry.Sha256)
                || !fileSystem.FileExists(targetPath)
                || fileSystem.DirectoryExists(targetPath)
            )
            {
                throw new InvalidOperationException(
                    "The NuGet plugin activation payload is damaged."
                );
            }

            byte[] content = fileSystem.ReadAllBytes(targetPath);
            string actualHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            if (
                content.LongLength != entry.Length
                || !string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase)
            )
            {
                throw new InvalidOperationException(
                    "The NuGet plugin activation payload is damaged."
                );
            }

            entrypointListed |= string.Equals(
                entry.Path,
                PluginEntrypointFileName,
                StringComparison.Ordinal
            );
        }

        return entrypointListed
            ? new ExistingActivation(manifest, IsLegacy: false)
            : throw new InvalidOperationException(
                "The NuGet plugin activation payload is incomplete."
            );
    }

    private ExistingActivation ReadLegacyActivation(
        string targetRootPath,
        SourcePayload? payload
    )
    {
        string entrypointPath = GetOwnedTargetPath(
            targetRootPath,
            PluginEntrypointFileName
        );
        if (
            !fileSystem.FileExists(entrypointPath)
            || fileSystem.DirectoryExists(entrypointPath)
        )
        {
            throw new InvalidOperationException(
                "The legacy NuGet plugin activation entrypoint is damaged."
            );
        }

        IEnumerable<string> candidatePaths = payload is null
            ? [PluginEntrypointFileName]
            : payload.Files.Select(static file => file.RelativePath);
        var files = new List<NuGetPluginActivationFile>();
        foreach (string relativePath in candidatePaths)
        {
            string targetPath = GetOwnedTargetPath(targetRootPath, relativePath);
            if (fileSystem.DirectoryExists(targetPath))
            {
                throw new InvalidOperationException(
                    "The legacy NuGet plugin activation payload is damaged."
                );
            }
            if (!fileSystem.FileExists(targetPath))
            {
                continue;
            }

            byte[] content = fileSystem.ReadAllBytes(targetPath);
            files.Add(
                new NuGetPluginActivationFile
                {
                    Path = relativePath,
                    Length = content.LongLength,
                    Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                    UnixFileMode = FileSystemPathSemantics.UsesWindowsPaths(fileSystem)
                        ? null
                        : (int)fileSystem.GetUnixFileMode(targetPath),
                }
            );
        }

        return new ExistingActivation(
            new NuGetPluginActivationManifest
            {
                SchemaVersion = MarkerSchemaVersion,
                SourceApplicationRoot =
                    payload?.Manifest.SourceApplicationRoot ?? targetRootPath,
                Files = files.ToArray(),
            },
            IsLegacy: true
        );
    }

    private void ValidateNewTargets(
        string targetRootPath,
        ExistingActivation? existingActivation,
        SourcePayload payload
    )
    {
        var existingOwnedPaths = new HashSet<string>(
            existingActivation?.Manifest.Files.Select(file => file.Path) ?? [],
            FileSystemPathSemantics.GetComparer(fileSystem)
        );
        HashSet<string> existingOwnedDirectories = GetOwnedAncestorDirectories(
            existingOwnedPaths
        );
        foreach (SourcePayloadFile file in payload.Files)
        {
            string targetPath = GetOwnedTargetPath(targetRootPath, file.RelativePath);
            if (
                fileSystem.DirectoryExists(targetPath)
                || (
                    fileSystem.FileExists(targetPath)
                    && !existingOwnedPaths.Contains(file.RelativePath)
                )
            )
            {
                throw new InvalidOperationException(
                    "The NuGet plugin activation would overwrite an unowned path."
                );
            }
            if (
                !existingOwnedPaths.Contains(file.RelativePath)
                && existingActivation is not { IsLegacy: true }
            )
            {
                ValidateNewPathAncestors(
                    targetRootPath,
                    file.RelativePath,
                    existingOwnedDirectories
                );
            }
        }
    }

    private void ValidateDisjointRoots(string targetRootPath, SourcePayload payload)
    {
        if (
            FileSystemPathSemantics.IsSameOrDescendant(
                fileSystem,
                payload.Manifest.SourceApplicationRoot,
                targetRootPath
            )
            || FileSystemPathSemantics.IsSameOrDescendant(
                fileSystem,
                targetRootPath,
                payload.Manifest.SourceApplicationRoot
            )
        )
        {
            throw new InvalidOperationException(
                "The NuGet plugin source and activation roots must be disjoint."
            );
        }
    }

    private HashSet<string> GetOwnedAncestorDirectories(IEnumerable<string> ownedPaths)
    {
        var result = new HashSet<string>(FileSystemPathSemantics.GetComparer(fileSystem));
        foreach (string ownedPath in ownedPaths)
        {
            string[] segments = GetRelativePathSegments(ownedPath);
            for (int segmentCount = 1; segmentCount < segments.Length; segmentCount++)
            {
                result.Add(string.Join('/', segments.Take(segmentCount)));
            }
        }
        return result;
    }

    private void ValidateNewPathAncestors(
        string targetRootPath,
        string relativePath,
        HashSet<string> existingOwnedDirectories
    )
    {
        string[] segments = GetRelativePathSegments(relativePath);
        for (int segmentCount = 1; segmentCount < segments.Length; segmentCount++)
        {
            string ancestorPath = string.Join('/', segments.Take(segmentCount));
            if (
                fileSystem.DirectoryExists(GetOwnedTargetPath(targetRootPath, ancestorPath))
                && !existingOwnedDirectories.Contains(ancestorPath)
            )
            {
                throw new InvalidOperationException(
                    "The NuGet plugin activation would use an unowned existing directory."
                );
            }
        }
    }

    private void ReplaceActivation(
        string targetRootPath,
        ExistingActivation? existingActivation,
        SourcePayload payload
    )
    {
        string markerPath = GetMarkerPath(targetRootPath);
        string? oldMarker = existingActivation is null
            ? null
            : fileSystem.ReadAllText(markerPath, Utf8NoBom);
        Dictionary<string, RestorableFile> oldFiles = SnapshotOwnedFiles(
            targetRootPath,
            existingActivation
        );
        var affectedPaths = new HashSet<string>(
            oldFiles.Keys,
            FileSystemPathSemantics.GetComparer(fileSystem)
        );
        foreach (SourcePayloadFile file in payload.Files)
        {
            affectedPaths.Add(file.RelativePath);
        }

        try
        {
            foreach (SourcePayloadFile file in payload.Files)
            {
                string targetPath = GetOwnedTargetPath(targetRootPath, file.RelativePath);
                fileSystem.AtomicWriteAllBytes(targetPath, file.Content);
                if (file.UnixFileMode is { } mode)
                {
                    fileSystem.SetUnixFileMode(targetPath, mode);
                }
            }

            var newPaths = new HashSet<string>(
                payload.Files.Select(file => file.RelativePath),
                FileSystemPathSemantics.GetComparer(fileSystem)
            );
            foreach (string obsoletePath in oldFiles.Keys.Where(path => !newPaths.Contains(path)))
            {
                fileSystem.DeleteFile(GetOwnedTargetPath(targetRootPath, obsoletePath));
            }

            fileSystem.AtomicWriteAllText(
                markerPath,
                JsonSerializer.Serialize(payload.Manifest, MarkerJsonOptions) + "\n",
                Utf8NoBom
            );
            PruneEmptyDirectories(
                targetRootPath,
                oldFiles.Keys.Where(path => !newPaths.Contains(path)),
                preserveTargetRoot: true
            );
        }
        catch
        {
            RestoreActivation(targetRootPath, markerPath, oldMarker, oldFiles, affectedPaths);
            throw;
        }
    }

    private void RemoveActivation(
        string targetRootPath,
        ExistingActivation? existingActivation
    )
    {
        if (existingActivation is null)
        {
            return;
        }

        string markerPath = GetMarkerPath(targetRootPath);
        string oldMarker = fileSystem.ReadAllText(markerPath, Utf8NoBom);
        Dictionary<string, RestorableFile> oldFiles = SnapshotOwnedFiles(
            targetRootPath,
            existingActivation
        );
        try
        {
            foreach (string relativePath in oldFiles.Keys)
            {
                fileSystem.DeleteFile(GetOwnedTargetPath(targetRootPath, relativePath));
            }
            fileSystem.DeleteFile(markerPath);
            PruneEmptyDirectories(
                targetRootPath,
                oldFiles.Keys,
                preserveTargetRoot: false
            );
        }
        catch
        {
            RestoreActivation(
                targetRootPath,
                markerPath,
                oldMarker,
                oldFiles,
                oldFiles.Keys
            );
            throw;
        }
    }

    private Dictionary<string, RestorableFile> SnapshotOwnedFiles(
        string targetRootPath,
        ExistingActivation? activation
    )
    {
        var result = new Dictionary<string, RestorableFile>(
            FileSystemPathSemantics.GetComparer(fileSystem)
        );
        if (activation is null)
        {
            return result;
        }

        foreach (NuGetPluginActivationFile entry in activation.Manifest.Files)
        {
            string targetPath = GetOwnedTargetPath(targetRootPath, entry.Path);
            result.Add(
                entry.Path,
                new RestorableFile(
                    fileSystem.ReadAllBytes(targetPath),
                    FileSystemPathSemantics.UsesWindowsPaths(fileSystem)
                        ? null
                        : fileSystem.GetUnixFileMode(targetPath)
                )
            );
        }
        return result;
    }

    private void RestoreActivation(
        string targetRootPath,
        string markerPath,
        string? oldMarker,
        IReadOnlyDictionary<string, RestorableFile> oldFiles,
        IEnumerable<string> affectedPaths
    )
    {
        foreach (string relativePath in affectedPaths.Distinct(
            FileSystemPathSemantics.GetComparer(fileSystem)
        ))
        {
            string targetPath = GetOwnedTargetPath(targetRootPath, relativePath);
            if (oldFiles.TryGetValue(relativePath, out RestorableFile? oldFile))
            {
                fileSystem.AtomicWriteAllBytes(targetPath, oldFile.Content);
                if (oldFile.UnixFileMode is { } mode)
                {
                    fileSystem.SetUnixFileMode(targetPath, mode);
                }
            }
            else if (fileSystem.FileExists(targetPath))
            {
                fileSystem.DeleteFile(targetPath);
            }
        }

        if (oldMarker is null)
        {
            if (fileSystem.FileExists(markerPath))
            {
                fileSystem.DeleteFile(markerPath);
            }
        }
        else
        {
            fileSystem.AtomicWriteAllText(markerPath, oldMarker, Utf8NoBom);
        }
        PruneEmptyDirectories(
            targetRootPath,
            affectedPaths,
            preserveTargetRoot: oldMarker is not null
        );
    }

    private void PruneEmptyDirectories(
        string targetRootPath,
        IEnumerable<string> affectedPaths,
        bool preserveTargetRoot
    )
    {
        if (!fileSystem.DirectoryExists(targetRootPath))
        {
            return;
        }

        var candidateDirectories = new HashSet<string>(
            FileSystemPathSemantics.GetComparer(fileSystem)
        );
        foreach (string affectedPath in affectedPaths)
        {
            string[] segments = GetRelativePathSegments(affectedPath);
            for (int segmentCount = segments.Length - 1; segmentCount > 0; segmentCount--)
            {
                candidateDirectories.Add(
                    GetOwnedTargetPath(
                        targetRootPath,
                        string.Join('/', segments.Take(segmentCount))
                    )
                );
            }
        }

        foreach (string directory in candidateDirectories.OrderByDescending(
            static path => path.Length
        ))
        {
            if (
                fileSystem.DirectoryExists(directory)
                && !fileSystem.EnumerateFiles(directory).Any()
                && !fileSystem.EnumerateDirectories(directory).Any()
            )
            {
                fileSystem.DeleteDirectory(directory);
            }
        }

        if (
            !preserveTargetRoot
            && !fileSystem.EnumerateFiles(targetRootPath).Any()
            && !fileSystem.EnumerateDirectories(targetRootPath).Any()
        )
        {
            fileSystem.DeleteDirectory(targetRootPath);
        }
    }

    private static string[] GetRelativePathSegments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private string GetSafeRelativePath(string rootPath, string filePath)
    {
        string fullRoot = fileSystem.GetFullPath(rootPath).TrimEnd('/', '\\');
        string fullPath = fileSystem.GetFullPath(filePath);
        char separator = FileSystemPathSemantics.UsesWindowsPaths(fileSystem) ? '\\' : '/';
        string prefix = fullRoot + separator;
        if (!fullPath.StartsWith(prefix, FileSystemPathSemantics.GetComparison(fileSystem)))
        {
            throw new InvalidOperationException(
                "The NuGet plugin source inventory escaped its application root."
            );
        }

        return fullPath[prefix.Length..].Replace('\\', '/');
    }

    private string GetOwnedTargetPath(string targetRootPath, string relativePath)
    {
        if (
            string.IsNullOrWhiteSpace(relativePath)
            || relativePath.StartsWith('/')
            || relativePath.StartsWith('\\')
            || relativePath
                .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or "..")
        )
        {
            throw new InvalidOperationException(
                "The NuGet plugin activation marker contains an unsafe path."
            );
        }

        string targetPath = FileSystemPathSemantics.Combine(
            fileSystem,
            targetRootPath,
            relativePath
        );
        if (!FileSystemPathSemantics.IsSameOrDescendant(fileSystem, targetPath, targetRootPath))
        {
            throw new InvalidOperationException(
                "The NuGet plugin activation marker contains an unsafe path."
            );
        }
        return targetPath;
    }

    private string GetMarkerPath(string targetRootPath) =>
        FileSystemPathSemantics.Combine(fileSystem, targetRootPath, MarkerFileName);

    private bool ManifestsEquivalent(
        NuGetPluginActivationManifest left,
        NuGetPluginActivationManifest right
    ) =>
        string.Equals(left.SchemaVersion, right.SchemaVersion, StringComparison.Ordinal)
        && string.Equals(
            left.SourceApplicationRoot,
            right.SourceApplicationRoot,
            FileSystemPathSemantics.GetComparison(fileSystem)
        )
        && left.Files.SequenceEqual(right.Files);

    private sealed record SourcePayload(
        NuGetPluginActivationManifest Manifest,
        IReadOnlyList<SourcePayloadFile> Files
    );

    private sealed record SourcePayloadFile(
        string RelativePath,
        byte[] Content,
        string Sha256,
        UnixFileMode? UnixFileMode
    );

    private sealed record ExistingActivation(
        NuGetPluginActivationManifest Manifest,
        bool IsLegacy
    );

    private sealed record RestorableFile(byte[] Content, UnixFileMode? UnixFileMode);
}

internal sealed record NuGetPluginActivationManifest
{
    public required string SchemaVersion { get; init; }

    public required string SourceApplicationRoot { get; init; }

    public required NuGetPluginActivationFile[] Files { get; init; }
}

internal sealed record NuGetPluginActivationFile
{
    public required string Path { get; init; }

    public required long Length { get; init; }

    public required string Sha256 { get; init; }

    public int? UnixFileMode { get; init; }
}

[JsonSerializable(typeof(NuGetPluginActivationManifest))]
[JsonSerializable(typeof(NuGetPluginActivationFile))]
[JsonSerializable(typeof(NuGetPluginActivationFile[]))]
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
internal sealed partial class NuGetPluginActivationJsonContext : JsonSerializerContext;
