using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal sealed class PythonKeyringPhysicalTargetWriter(IFileSystem fileSystem)
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    private const string SupportedKey = "physical-target";
    private static readonly UnixFileMode CanonicalKeyringShimUnixFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public void Write(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        ValidateRequestShape(request, cancellationToken);
        PythonKeyringTargetDocument document = ReadDocument(GetSingleNormalizedTargetPath(request));
        ValidateCurrentState(document, request);
        ValidateRetainedOwnershipProofs(request.OwnershipProofs, cancellationToken);

        string updatedContents = CreateUpdatedContents(request.PlanOperation, request.Change);
        if (string.Equals(document.OriginalText, updatedContents, StringComparison.Ordinal))
        {
            if (document.OriginalContentsBytes is not null)
            {
                UnixFileMode? originalUnixFileMode =
                    request.TargetKind == ConfigurationTargetKind.KeyringShim
                    && !OperatingSystem.IsWindows()
                        ? fileSystem.GetUnixFileMode(document.TargetPath)
                        : null;
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        document.TargetPath,
                        true,
                        document.OriginalContentsBytes,
                        ComputeSha256(document.OriginalContentsBytes),
                        RequiresRollback:
                            originalUnixFileMode is not null
                            && originalUnixFileMode.Value != CanonicalKeyringShimUnixFileMode,
                        PreviousUnixFileMode: originalUnixFileMode
                    )
                );
                EnsureKeyringShimIsCanonicalUnixModeIfNeeded(
                    request.TargetKind,
                    document.TargetPath
                );
            }

            return;
        }

        byte[] updatedContentsBytes = Utf8NoBom.GetBytes(updatedContents);
        UnixFileMode? previousUnixFileMode =
            document.OriginalContentsBytes is not null
            && request.TargetKind == ConfigurationTargetKind.KeyringShim
            && !OperatingSystem.IsWindows()
                ? fileSystem.GetUnixFileMode(document.TargetPath)
                : null;
        var mutation = new ConfigurationPhysicalTargetFileMutation(
            document.TargetPath,
            document.OriginalContentsBytes is not null,
            document.OriginalContentsBytes,
            ComputeSha256(updatedContentsBytes),
            PreviousUnixFileMode: previousUnixFileMode
        );

        try
        {
            fileSystem.AtomicWriteAllText(
                document.TargetPath,
                updatedContents,
                Utf8NoBom,
                AtomicWriteOptions.None,
                document.MutationExpectation
            );
            request.RegisterCompletedFileMutation(mutation);
            EnsureKeyringShimIsCanonicalUnixModeIfNeeded(request.TargetKind, document.TargetPath);
        }
        catch (FileMutationException exception)
            when (exception.MutationMayHaveReachedDurableState)
        {
            request.RegisterCompletedFileMutation(mutation);
            throw;
        }
    }

    private void EnsureKeyringShimIsCanonicalUnixModeIfNeeded(
        ConfigurationTargetKind targetKind,
        string targetPath
    )
    {
        if (targetKind != ConfigurationTargetKind.KeyringShim || OperatingSystem.IsWindows())
        {
            return;
        }

        UnixFileMode currentUnixFileMode = fileSystem.GetUnixFileMode(targetPath);
        if (currentUnixFileMode == CanonicalKeyringShimUnixFileMode)
        {
            return;
        }

        fileSystem.SetUnixFileMode(targetPath, CanonicalKeyringShimUnixFileMode);
    }

    public void Validate(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        ValidateRequestShape(request, cancellationToken);
        PythonKeyringTargetDocument document = ReadDocument(GetSingleNormalizedTargetPath(request));
        ValidateRetainedOwnershipProofs(request.OwnershipProofs, cancellationToken);
        ValidateCurrentState(document, request);
        _ = CreateUpdatedContents(request.PlanOperation, request.Change);
    }

    private static void ValidateRequestShape(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (
            request.TargetKind is not ConfigurationTargetKind.PythonKeyringBackend
                and not ConfigurationTargetKind.KeyringShim
        )
        {
            throw new NotSupportedException(
                "The Python keyring physical writer supports only PythonKeyringBackend and "
                    + "KeyringShim targets."
            );
        }

        if (request.Changes.Count != 1)
        {
            throw new NotSupportedException(
                "The Python keyring physical writer supports one change per request."
            );
        }

        if (
            request.PlanOperation == ConfigurationPlanOperation.Apply
            && !IsValueWritingOperation(request.Change.Operation)
        )
        {
            throw new NotSupportedException(
                "The Python keyring physical writer supports value-writing changes only."
            );
        }

        if (
            request.PlanOperation == ConfigurationPlanOperation.Remove
            && request.Change.Operation != ConfigurationChangeOperation.Remove
        )
        {
            throw new NotSupportedException(
                "The Python keyring physical writer supports ownership-removing changes only."
            );
        }

        string? planningViolation = GetPlanningValidationViolation(request.Change);
        if (planningViolation is not null)
        {
            throw new NotSupportedException(planningViolation);
        }
    }

    public void ValidateRetainedOwnershipProofs(
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(ownershipProofs);

        ConfigurationPhysicalTargetOwnershipProof[] pythonProofs = ownershipProofs
            .Where(proof =>
                proof.TargetKind is ConfigurationTargetKind.PythonKeyringBackend
                    or ConfigurationTargetKind.KeyringShim
            )
            .ToArray();

        foreach (ConfigurationPhysicalTargetOwnershipProof proof in pythonProofs)
        {
            string? targetPathViolation = GetTargetPathValidationViolation(
                proof.TargetPathOrName,
                proof.TargetKind
            );
            if (targetPathViolation is not null)
            {
                throw new InvalidOperationException(targetPathViolation);
            }
        }

        foreach (
            IGrouping<string, ConfigurationPhysicalTargetOwnershipProof> proofsByTarget in
                pythonProofs.GroupBy(
                    proof => CreatePhysicalPathIdentity(proof.TargetPathOrName),
                    GetPathComparer()
                )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (proofsByTarget.Count() != 1)
            {
                throw new InvalidOperationException(
                    "Configuration conflict: Python keyring physical target retained ownership "
                        + "proofs must be unique per target path."
                );
            }

            ConfigurationPhysicalTargetOwnershipProof proof = proofsByTarget.Single();
            if (!string.Equals(proof.Key, SupportedKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Configuration conflict: Python keyring physical target retained ownership "
                        + "proofs must use the canonical physical key."
                );
            }

            PythonKeyringTargetDocument document = ReadDocument(proofsByTarget.Key);
            ValidateProofAgainstCurrentState(document, proof);
        }
    }

    internal static string? GetPlanningValidationViolation(ConfigurationChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (
            change.TargetKind is not ConfigurationTargetKind.PythonKeyringBackend
                and not ConfigurationTargetKind.KeyringShim
        )
        {
            return null;
        }

        string? targetPathViolation = GetTargetPathValidationViolation(
            change.TargetPathOrName,
            change.TargetKind
        );
        if (targetPathViolation is not null)
        {
            return targetPathViolation;
        }

        if (!string.Equals(change.Key, SupportedKey, StringComparison.Ordinal))
        {
            return "The Python keyring physical writer supports only the canonical physical "
                + "target key.";
        }

        if (
            change.Operation
            is not ConfigurationChangeOperation.Set
                and not ConfigurationChangeOperation.Create
                and not ConfigurationChangeOperation.Update
                and not ConfigurationChangeOperation.Refresh
                and not ConfigurationChangeOperation.Remove
        )
        {
            return "The Python keyring physical writer supports only value-writing and remove "
                + "changes.";
        }

        if (change.Operation == ConfigurationChangeOperation.Remove)
        {
            if (change.Value is not null)
            {
                return "The Python keyring physical writer supports only ownership-removing "
                    + "changes without a value.";
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(change.Value))
        {
            return "The Python keyring physical writer supports only non-empty value-writing "
                + "changes.";
        }

        if (change.IsSecretValue)
        {
            return "The Python keyring physical writer does not support secret value-writing "
                + "changes.";
        }

        return null;
    }

    internal static string? GetTargetPathValidationViolation(
        string targetPathOrName,
        ConfigurationTargetKind targetKind
    )
    {
        if (
            targetKind is not ConfigurationTargetKind.PythonKeyringBackend
                and not ConfigurationTargetKind.KeyringShim
        )
        {
            return null;
        }

        if (
            string.IsNullOrWhiteSpace(targetPathOrName)
            || !Path.IsPathFullyQualified(targetPathOrName)
            || ContainsPhysicalPathTraversalSegments(targetPathOrName)
        )
        {
            return GetTargetPathValidationMessage(targetKind);
        }

        string normalizedTargetPath = NormalizePhysicalTargetConfigurationPathSegments(
            targetPathOrName
        );
        string canonicalTargetPath = targetKind switch
        {
            ConfigurationTargetKind.PythonKeyringBackend =>
                GetCanonicalPythonKeyringBackendTargetPath(),
            ConfigurationTargetKind.KeyringShim => GetCanonicalKeyringShimTargetPath(),
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, null),
        };

        return string.Equals(
            normalizedTargetPath,
            NormalizePhysicalTargetConfigurationPathSegments(canonicalTargetPath),
            GetPathComparison()
        )
            ? null
            : GetTargetPathValidationMessage(targetKind);
    }

    private static string GetTargetPathValidationMessage(ConfigurationTargetKind targetKind) =>
        targetKind switch
        {
            ConfigurationTargetKind.PythonKeyringBackend =>
                "The Python keyring physical writer supports only the official per-user backend "
                    + "manifest file.",
            ConfigurationTargetKind.KeyringShim =>
                "The Python keyring physical writer supports only the official per-user keyring "
                    + "shim path.",
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, null),
        };

    private static string CreateUpdatedContents(
        ConfigurationPlanOperation planOperation,
        ConfigurationChange change
    ) =>
        planOperation switch
        {
            ConfigurationPlanOperation.Apply => CreateApplyContents(change),
            ConfigurationPlanOperation.Remove => string.Empty,
            _ => throw new NotSupportedException(
                "The Python keyring physical writer supports apply/remove operations only."
            ),
        };

    private static string CreateApplyContents(ConfigurationChange change)
    {
        if (!IsValueWritingOperation(change.Operation))
        {
            throw new NotSupportedException(
                "The Python keyring physical writer supports value-writing changes only."
            );
        }

        if (string.IsNullOrWhiteSpace(change.Value))
        {
            throw new InvalidOperationException(
                "Configuration conflict: Python keyring physical target value-writing changes "
                    + "require a value."
            );
        }

        if (change.IsSecretValue)
        {
            throw new NotSupportedException(
                "The Python keyring physical writer does not support secret value-writing changes."
            );
        }

        return change.Value;
    }

    private static void ValidateCurrentState(
        PythonKeyringTargetDocument document,
        ConfigurationPhysicalTargetWriterRequest request
    )
    {
        bool hasRelevantProof = request.OwnershipProofs.Any(proof =>
            proof.TargetKind == request.TargetKind
            && string.Equals(
                CreatePhysicalPathIdentity(proof.TargetPathOrName),
                document.TargetPath,
                GetPathComparison()
            )
        );

        if (!document.Exists)
        {
            if (request.PlanOperation == ConfigurationPlanOperation.Remove)
            {
                throw new InvalidOperationException(
                    GetTargetDoesNotExistMessage(request.TargetKind)
                );
            }

            return;
        }

        if (!hasRelevantProof)
        {
            throw new InvalidOperationException(
                GetTargetAlreadyExistsMessage(request.TargetKind)
            );
        }
    }

    private PythonKeyringTargetDocument ReadDocument(string targetPath)
    {
        EnsureTargetPathCanBeSafelyMutated(targetPath);

        if (!fileSystem.FileExists(targetPath))
        {
            if (fileSystem.DirectoryExists(targetPath))
            {
                throw new InvalidOperationException(
                    GetTargetPathExistsAsDirectoryMessage(targetPath)
                );
            }

            if (IsUnsupportedLinkOrReparsePoint(targetPath))
            {
                throw new NotSupportedException(
                    GetTargetPathIsUnsupportedLinkMessage(targetPath)
                );
            }

            return new PythonKeyringTargetDocument(
                targetPath,
                string.Empty,
                null,
                FileMutationExpectation.Missing
            );
        }

        if (fileSystem.DirectoryExists(targetPath))
        {
            throw new InvalidOperationException(GetTargetPathExistsAsDirectoryMessage(targetPath));
        }

        if (IsUnsupportedLinkOrReparsePoint(targetPath))
        {
            throw new NotSupportedException(GetTargetPathIsUnsupportedLinkMessage(targetPath));
        }

        byte[] contents = fileSystem.ReadAllBytes(targetPath);
        if (StartsWithUtf8Bom(contents))
        {
            throw new NotSupportedException(
                "Configuration conflict: Python keyring physical target files with a UTF-8 BOM "
                    + "are not supported for safe physical mutation."
            );
        }

        string text = Utf8NoBom.GetString(contents);
        return new PythonKeyringTargetDocument(
            targetPath,
            text,
            contents,
            FileMutationExpectation.Existing(ComputeSha256(contents))
        );
    }

    private void EnsureTargetPathCanBeSafelyMutated(string targetPath)
    {
        if (IsUnsupportedLinkOrReparsePoint(targetPath))
        {
            throw new NotSupportedException(GetTargetPathIsUnsupportedLinkMessage(targetPath));
        }

        string? targetParent = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrEmpty(targetParent))
        {
            targetParent = Directory.GetCurrentDirectory();
        }

        foreach (string directory in EnumerateDirectoryChain(targetParent))
        {
            try
            {
                if (IsUnsupportedLinkOrReparsePoint(directory))
                {
                    throw new NotSupportedException(
                        "Configuration conflict: Python keyring physical target parent path "
                            + "contains a symbolic-link or reparse-point directory."
                    );
                }

                if (!fileSystem.DirectoryExists(directory) && fileSystem.FileExists(directory))
                {
                    throw new NotSupportedException(
                        "Configuration conflict: Python keyring physical target parent path "
                            + "contains a non-directory entry."
                    );
                }
            }
            catch (FileNotFoundException)
            {
                // Missing parent directories are valid for first apply.
            }
            catch (DirectoryNotFoundException)
            {
                // Missing parent directories are valid for first apply.
            }
        }
    }

    private bool IsUnsupportedLinkOrReparsePoint(string path)
    {
        try
        {
            if (fileSystem.IsSymbolicLink(path))
            {
                return true;
            }

            return fileSystem is IFileSystemReparsePointSafety reparsePointSafety
                && reparsePointSafety.IsReparsePoint(path);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void ValidateProofAgainstCurrentState(
        PythonKeyringTargetDocument document,
        ConfigurationPhysicalTargetOwnershipProof proof
    )
    {
        if (!document.Exists)
        {
            throw new InvalidOperationException(
                GetTargetDoesNotExistProofMessage(proof.TargetKind)
            );
        }

        if (string.IsNullOrWhiteSpace(proof.PlannedValueSha256))
        {
            throw new InvalidOperationException(
                GetMissingPlannedValueHashMessage(proof.TargetKind)
            );
        }

        string currentHash = ComputeSha256(document.OriginalContentsBytes!);
        if (!string.Equals(currentHash, proof.PlannedValueSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                GetProofDoesNotMatchCurrentContentsMessage(proof.TargetKind)
            );
        }
    }

    private static string GetTargetDoesNotExistMessage(ConfigurationTargetKind targetKind) =>
        targetKind == ConfigurationTargetKind.PythonKeyringBackend
            ? "Configuration conflict: Python keyring backend target does not exist."
            : "Configuration conflict: Python keyring shim target does not exist.";

    private static string GetTargetAlreadyExistsMessage(ConfigurationTargetKind targetKind) =>
        targetKind == ConfigurationTargetKind.PythonKeyringBackend
            ? "Configuration conflict: Python keyring backend target already exists and no "
                + "retained ownership proof was provided."
            : "Configuration conflict: Python keyring shim target already exists and no retained "
                + "ownership proof was provided.";

    private static string GetTargetDoesNotExistProofMessage(ConfigurationTargetKind targetKind) =>
        targetKind == ConfigurationTargetKind.PythonKeyringBackend
            ? "Configuration conflict: Python keyring backend retained ownership proof does not "
                + "match any existing file."
            : "Configuration conflict: Python keyring shim retained ownership proof does not match "
                + "any existing file.";

    private static string GetMissingPlannedValueHashMessage(ConfigurationTargetKind targetKind) =>
        targetKind == ConfigurationTargetKind.PythonKeyringBackend
            ? "Configuration conflict: Python keyring backend retained ownership proof is "
                + "planned value hash."
            : "Configuration conflict: Python keyring shim retained ownership proof is missing a "
                + "planned value hash.";

    private static string GetProofDoesNotMatchCurrentContentsMessage(
        ConfigurationTargetKind targetKind
    ) =>
        targetKind == ConfigurationTargetKind.PythonKeyringBackend
            ? "Configuration conflict: Python keyring backend retained ownership proof does not "
                + "match the current file contents."
            : "Configuration conflict: Python keyring shim retained ownership proof does not "
                + "match the current file contents.";

    private static string GetTargetPathExistsAsDirectoryMessage(string targetPath) =>
        "Configuration conflict: Python keyring physical target path exists as a directory: "
        + targetPath
        + ".";

    private static string GetTargetPathIsUnsupportedLinkMessage(string targetPath) =>
        "Configuration conflict: Python keyring physical target path is a symbolic-link or "
        + "reparse-point and is not supported: "
        + targetPath
        + ".";

    private static string GetSingleNormalizedTargetPath(
        ConfigurationPhysicalTargetWriterRequest request
    )
    {
        string targetPath = CreatePhysicalPathIdentity(request.Change.TargetPathOrName);
        if (
            request.Changes
                .Skip(1)
                .Any(change =>
                    !string.Equals(
                        CreatePhysicalPathIdentity(change.TargetPathOrName),
                        targetPath,
                        GetPathComparison()
                    )
                )
        )
        {
            throw new NotSupportedException(
                "The Python keyring physical writer supports only batches that target one "
                    + "normalized path."
            );
        }

        return targetPath;
    }

    private static string CreatePhysicalPathIdentity(string targetPathOrName)
    {
        string normalizedTargetPath = NormalizePhysicalTargetConfigurationPathSegments(
            targetPathOrName
        );
        return Path.TrimEndingDirectorySeparator(normalizedTargetPath);
    }

    private static string GetCanonicalPythonKeyringBackendTargetPath() =>
        ConfigurationLayoutProjector.ProjectPythonKeyringBackend(
            CreateCurrentLayoutProjectionContext()
        ).TargetPath;

    private static string GetCanonicalKeyringShimTargetPath() =>
        ConfigurationLayoutProjector.ProjectKeyringShim(CreateCurrentLayoutProjectionContext())
            .TargetPath;

    private static ConfigurationLayoutProjectionContext CreateCurrentLayoutProjectionContext() =>
        new()
        {
            Platform = OperatingSystem.IsWindows()
                ? ConfigurationLayoutPlatform.Windows
                : OperatingSystem.IsMacOS()
                    ? ConfigurationLayoutPlatform.MacOs
                    : ConfigurationLayoutPlatform.Linux,
            HomeDirectory = GetHomeDirectory(),
            LocalAppDataDirectory = GetLocalAppDataDirectory(),
            XdgDataHomeDirectory = Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            XdgConfigHomeDirectory = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
        };

    private static string GetLocalAppDataDirectory()
    {
        string? localAppData =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.TrimEndingDirectorySeparator(localAppData);
        }

        string? windowsLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(windowsLocalAppData))
        {
            return Path.TrimEndingDirectorySeparator(windowsLocalAppData);
        }

        string? userProfile = GetHomeDirectoryOrNull();
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, "AppData", "Local");
        }

        throw new InvalidOperationException("User profile directory is unavailable.");
    }

    private static string GetHomeDirectory() =>
        GetHomeDirectoryOrNull()
        ?? throw new InvalidOperationException("User profile directory is unavailable.");

    private static string? GetHomeDirectoryOrNull()
    {
        string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.TrimEndingDirectorySeparator(userProfile);
        }

        if (OperatingSystem.IsWindows())
        {
            string? windowsUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrWhiteSpace(windowsUserProfile))
            {
                return Path.TrimEndingDirectorySeparator(windowsUserProfile);
            }

            string? homeDrive = Environment.GetEnvironmentVariable("HOMEDRIVE");
            string? homePath = Environment.GetEnvironmentVariable("HOMEPATH");
            if (!string.IsNullOrWhiteSpace(homeDrive) && !string.IsNullOrWhiteSpace(homePath))
            {
                return Path.TrimEndingDirectorySeparator(homeDrive + homePath);
            }
        }
        else
        {
            string? home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.TrimEndingDirectorySeparator(home);
            }
        }

        return null;
    }

    private bool IsSupportedLinkOrReparsePoint(string path)
    {
        try
        {
            if (fileSystem.IsSymbolicLink(path))
            {
                return true;
            }

            return fileSystem is IFileSystemReparsePointSafety reparsePointSafety
                && reparsePointSafety.IsReparsePoint(path);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static Stack<string> EnumerateDirectoryChain(string path)
    {
        var directories = new Stack<string>();
        string? current = Path.TrimEndingDirectorySeparator(path);
        while (!string.IsNullOrEmpty(current))
        {
            directories.Push(current);
            string? parent = Path.GetDirectoryName(current);
            if (
                string.IsNullOrEmpty(parent)
                || string.Equals(parent, current, StringComparison.Ordinal)
            )
            {
                break;
            }

            current = parent;
        }

        return directories;
    }

    private static bool StartsWithUtf8Bom(byte[] contents) =>
        contents.Length >= 3
        && contents[0] == 0xEF
        && contents[1] == 0xBB
        && contents[2] == 0xBF;

    private static string ComputeSha256(byte[] bytes)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsValueWritingOperation(ConfigurationChangeOperation operation) =>
        operation
            is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh;

    private static bool ContainsPhysicalPathTraversalSegments(string path)
    {
        string[] segments = NormalizeRelativeConfigurationPathSegments(path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment is "." or "..");
    }

    private static string NormalizePhysicalTargetConfigurationPathSegments(string path)
    {
        ConfigurationPathKind kind = GetConfigurationPathKind(path);
        return kind == ConfigurationPathKind.Invalid
            ? NormalizeRelativeConfigurationPathSegments(path)
            : NormalizeAbsoluteConfigurationPathSegments(path);
    }

    private static string NormalizeRelativeConfigurationPathSegments(string path)
    {
        string normalized = path.Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static string NormalizeAbsoluteConfigurationPathSegments(string path)
    {
        ConfigurationPathKind kind = GetConfigurationPathKind(path);
        if (kind == ConfigurationPathKind.Invalid)
        {
            return path;
        }

        string normalized = IsWindowsConfigurationPathKind(kind) ? path.Replace('\\', '/') : path;
        int duplicateSlashRootLength = kind == ConfigurationPathKind.WindowsUnc ? 2 : 0;
        while (normalized.IndexOf("//", duplicateSlashRootLength, StringComparison.Ordinal) >= 0)
        {
            normalized =
                normalized[..duplicateSlashRootLength]
                + normalized[duplicateSlashRootLength..].Replace(
                    "//",
                    "/",
                    StringComparison.Ordinal
                );
        }

        return normalized;
    }

    private static bool IsWindowsConfigurationPathKind(ConfigurationPathKind kind) =>
        kind is ConfigurationPathKind.WindowsDriveLetter or ConfigurationPathKind.WindowsUnc;

    private static ConfigurationPathKind GetConfigurationPathKind(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return ConfigurationPathKind.Invalid;
        }

        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            return ConfigurationPathKind.WindowsDriveLetter;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return ConfigurationPathKind.WindowsUnc;
        }

        if (path.StartsWith('/'))
        {
            return ConfigurationPathKind.Unix;
        }

        return ConfigurationPathKind.Invalid;
    }

    private enum ConfigurationPathKind
    {
        Invalid,
        Unix,
        WindowsDriveLetter,
        WindowsUnc,
    }

    private sealed record PythonKeyringTargetDocument(
        string TargetPath,
        string OriginalText,
        byte[]? OriginalContentsBytes,
        FileMutationExpectation MutationExpectation
    )
    {
        public bool Exists => OriginalContentsBytes is not null && OriginalContentsBytes.Length > 0;
    }
}
