using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal sealed class NuGetPluginLayoutPhysicalTargetWriter(IFileSystem fileSystem)
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    private const string LayoutMarkerFileName = ".azureauth-credprovider.nuget-plugin-layout";
    private const string SupportedKey = "physical-target";

    public void Write(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        ValidateRequestShape(request, cancellationToken);
        NuGetPluginLayoutDocument document = ReadDocument(
            GetSingleNormalizedTargetRootPath(request)
        );
        ValidateCurrentState(document, request);
        ValidateRetainedOwnershipProofs(request.OwnershipProofs, cancellationToken);
        string updatedContents = CreateUpdatedContents(request.PlanOperation, request.Change);
        if (string.Equals(document.OriginalText, updatedContents, StringComparison.Ordinal))
        {
            if (document.OriginalContentsBytes is not null)
            {
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        document.MarkerPath,
                        true,
                        document.OriginalContentsBytes,
                        ComputeSha256(document.OriginalContentsBytes),
                        RequiresRollback: false
                    )
                );
            }

            return;
        }

        byte[] updatedContentsBytes = Utf8NoBom.GetBytes(updatedContents);
        var mutation = new ConfigurationPhysicalTargetFileMutation(
            document.MarkerPath,
            document.OriginalContentsBytes is not null,
            document.OriginalContentsBytes,
            ComputeSha256(updatedContentsBytes)
        );

        try
        {
            fileSystem.AtomicWriteAllText(
                document.MarkerPath,
                updatedContents,
                Utf8NoBom,
                AtomicWriteOptions.None,
                document.MutationExpectation
            );
            request.RegisterCompletedFileMutation(mutation);
        }
        catch (FileMutationException exception)
            when (exception.MutationMayHaveReachedDurableState)
        {
            request.RegisterCompletedFileMutation(mutation);
            throw;
        }
    }

    public void Validate(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        ValidateRequestShape(request, cancellationToken);
        NuGetPluginLayoutDocument document = ReadDocument(
            GetSingleNormalizedTargetRootPath(request)
        );
        ValidateRetainedOwnershipProofs(request.OwnershipProofs, cancellationToken);
        ValidateCurrentState(document, request);
        _ = CreateUpdatedContents(request.PlanOperation, request.Change);
    }

    public void ValidateRetainedOwnershipProofs(
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(ownershipProofs);
        ConfigurationPhysicalTargetOwnershipProof[] nuGetProofs = ownershipProofs
            .Where(proof => proof.TargetKind == ConfigurationTargetKind.NuGetPluginLayout)
            .ToArray();
        foreach (ConfigurationPhysicalTargetOwnershipProof proof in nuGetProofs)
        {
            string? targetRootViolation = GetTargetRootPathValidationViolation(
                proof.TargetPathOrName
            );
            if (targetRootViolation is not null)
            {
                throw new InvalidOperationException(targetRootViolation);
            }
        }

        foreach (
            IGrouping<string, ConfigurationPhysicalTargetOwnershipProof>
                proofsByTarget in nuGetProofs
                .GroupBy(
                    proof => CreatePhysicalPathIdentity(proof.TargetPathOrName),
                    GetPathComparer()
                )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (proofsByTarget.Count() != 1)
            {
                throw new InvalidOperationException(
                    "Configuration conflict: NuGet plugin layout retained ownership proofs "
                    + "must be unique per target path."
                );
            }

            ConfigurationPhysicalTargetOwnershipProof proof = proofsByTarget.Single();
            if (!string.Equals(proof.Key, SupportedKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Configuration conflict: NuGet plugin layout retained ownership proofs "
                    + "must use the canonical physical key."
                );
            }

            NuGetPluginLayoutDocument document = ReadDocument(proofsByTarget.Key);
            ValidateProofAgainstCurrentState(document, proof);
        }
    }

    private static void ValidateRequestShape(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.TargetKind != ConfigurationTargetKind.NuGetPluginLayout)
        {
            throw new NotSupportedException(
                "The NuGet plugin layout physical writer supports only NuGetPluginLayout targets."
            );
        }

        if (request.Changes.Count != 1)
        {
            throw new NotSupportedException(
                "The NuGet plugin layout physical writer supports one change per request."
            );
        }

        if (
            request.PlanOperation == ConfigurationPlanOperation.Apply
            && !IsValueWritingOperation(request.Change.Operation)
        )
        {
            throw new NotSupportedException(
                "The NuGet plugin layout physical writer supports value-writing changes only."
            );
        }

        if (
            request.PlanOperation == ConfigurationPlanOperation.Remove
            && request.Change.Operation != ConfigurationChangeOperation.Remove
        )
        {
            throw new NotSupportedException(
                "The NuGet plugin layout physical writer supports ownership-removing changes only."
            );
        }

        string? planningViolation = GetPlanningValidationViolation(request.Change);
        if (planningViolation is not null)
        {
            throw new NotSupportedException(planningViolation);
        }
    }

    private string GetSingleNormalizedTargetRootPath(
        ConfigurationPhysicalTargetWriterRequest request
    )
    {
        string targetRootPath = CreatePhysicalPathIdentity(request.Change.TargetPathOrName);
        if (
            request.Changes
                .Skip(1)
                .Any(change =>
                    !string.Equals(
                        CreatePhysicalPathIdentity(change.TargetPathOrName),
                        targetRootPath,
                        GetPathComparison()
                    )
                )
        )
        {
            throw new NotSupportedException(
                "The NuGet plugin layout physical writer supports only batches that target "
                + "one normalized plugin root path."
            );
        }

        return targetRootPath;
    }

    internal static string? GetPlanningValidationViolation(ConfigurationChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.TargetKind != ConfigurationTargetKind.NuGetPluginLayout)
        {
            return null;
        }

        string? rootViolation = GetTargetRootPathValidationViolation(change.TargetPathOrName);
        if (rootViolation is not null)
        {
            return rootViolation;
        }

        if (!string.Equals(change.Key, SupportedKey, StringComparison.Ordinal))
        {
            return
                "The NuGet plugin layout physical writer supports only the canonical "
                + "physical target key.";
        }

        if (change.Operation == ConfigurationChangeOperation.RemoveAdapter)
        {
            return
                "The NuGet plugin layout physical writer does not support remove-adapter "
                + "changes.";
        }

        if (IsValueWritingOperation(change.Operation))
        {
            if (string.IsNullOrWhiteSpace(change.Value))
            {
                return
                    "The NuGet plugin layout physical writer supports only non-empty "
                    + "value-writing changes.";
            }

            if (change.IsSecretValue)
            {
                return
                    "The NuGet plugin layout physical writer does not support secret "
                    + "value-writing changes.";
            }
        }

        return null;
    }

    internal static string? GetTargetRootPathValidationViolation(
        string targetRootPath
    ) =>
        ConfigurationManager.IsCanonicalNuGetPluginLayoutTargetRootPath(targetRootPath)
            ? null
            : "The NuGet plugin layout physical writer supports only the official per-user "
                + "plugin convention root.";

    private NuGetPluginLayoutDocument ReadDocument(string targetRootPath)
    {
        EnsureTargetRootPathCanBeSafelyMutated(targetRootPath);
        string markerPath = Path.Combine(targetRootPath, LayoutMarkerFileName);
        if (!fileSystem.FileExists(markerPath))
        {
            if (fileSystem.DirectoryExists(markerPath))
            {
                throw new InvalidOperationException(
                    "Configuration conflict: NuGet plugin layout marker path exists as a "
                    + "directory."
                );
            }

            if (IsUnsupportedLinkOrReparsePoint(markerPath))
            {
                throw new NotSupportedException(
                    "Configuration conflict: NuGet plugin layout marker path is a "
                    + "symbolic-link or reparse-point and is not supported."
                );
            }

            return new NuGetPluginLayoutDocument(
                targetRootPath,
                markerPath,
                string.Empty,
                null,
                FileMutationExpectation.Missing
            );
        }

        if (fileSystem.DirectoryExists(markerPath))
        {
            throw new InvalidOperationException(
                "Configuration conflict: NuGet plugin layout marker path exists as a directory."
            );
        }

        if (IsUnsupportedLinkOrReparsePoint(markerPath))
        {
            throw new NotSupportedException(
                "Configuration conflict: NuGet plugin layout marker path is a "
                + "symbolic-link or reparse-point and is not supported."
            );
        }

        byte[] contents = fileSystem.ReadAllBytes(markerPath);
        if (StartsWithUtf8Bom(contents))
        {
            throw new NotSupportedException(
                "Configuration conflict: BOM-prefixed NuGet plugin layout marker files are "
                + "not supported for safe physical mutation."
            );
        }

        string text = Utf8NoBom.GetString(contents);
        return new NuGetPluginLayoutDocument(
            targetRootPath,
            markerPath,
            text,
            contents,
            FileMutationExpectation.Existing(ComputeSha256(contents))
        );
    }

    private void ValidateCurrentState(
        NuGetPluginLayoutDocument document,
        ConfigurationPhysicalTargetWriterRequest request
    )
    {
        bool hasRelevantProof = request.OwnershipProofs.Any(proof =>
            proof.TargetKind == ConfigurationTargetKind.NuGetPluginLayout
            && string.Equals(
                CreatePhysicalPathIdentity(proof.TargetPathOrName),
                document.TargetRootPath,
                GetPathComparison()
            )
        );

        if (!document.Exists)
        {
            if (request.PlanOperation == ConfigurationPlanOperation.Remove)
            {
                throw new InvalidOperationException(
                    "Configuration conflict: NuGet plugin layout target does not exist."
                );
            }

            return;
        }

        if (!hasRelevantProof)
        {
            throw new InvalidOperationException(
                "Configuration conflict: NuGet plugin layout target already exists and no "
                + "retained ownership proof was provided."
            );
        }
    }

    private static void ValidateProofAgainstCurrentState(
        NuGetPluginLayoutDocument document,
        ConfigurationPhysicalTargetOwnershipProof proof
    )
    {
        if (!document.Exists)
        {
            throw new InvalidOperationException(
                "Configuration conflict: NuGet plugin layout retained ownership proof does "
                + "not match any existing file."
            );
        }

        if (string.IsNullOrWhiteSpace(proof.PlannedValueSha256))
        {
            throw new InvalidOperationException(
                "Configuration conflict: NuGet plugin layout retained ownership proof is "
                + "missing a planned value hash."
            );
        }

        string currentHash = ComputeSha256(document.OriginalContentsBytes!);
        if (!string.Equals(currentHash, proof.PlannedValueSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Configuration conflict: NuGet plugin layout retained ownership proof does "
                + "not match the current marker contents."
            );
        }
    }

    private static string CreateUpdatedContents(
        ConfigurationPlanOperation planOperation,
        ConfigurationChange change
    ) =>
        planOperation switch
        {
            ConfigurationPlanOperation.Apply => CreateApplyContents(change),
            ConfigurationPlanOperation.Remove => string.Empty,
            _ => throw new NotSupportedException(
                "The NuGet plugin layout physical writer supports apply/remove operations only."
            ),
        };

    private static string CreateApplyContents(ConfigurationChange change)
    {
        if (!IsValueWritingOperation(change.Operation))
        {
            throw new NotSupportedException(
                "The NuGet plugin layout physical writer supports value-writing changes only."
            );
        }

        if (string.IsNullOrWhiteSpace(change.Value))
        {
            throw new InvalidOperationException(
                "Configuration conflict: NuGet plugin layout value-writing changes require a value."
            );
        }

        return change.Value;
    }

    private void EnsureTargetRootPathCanBeSafelyMutated(string targetRootPath)
    {
        if (IsUnsupportedLinkOrReparsePoint(targetRootPath))
        {
            throw new NotSupportedException(
                "Configuration conflict: NuGet plugin layout target root path is a "
                + "symbolic-link or reparse-point and is not supported."
            );
        }

        if (fileSystem.FileExists(targetRootPath))
        {
            throw new InvalidOperationException(
                "Configuration conflict: NuGet plugin layout target exists as a file."
            );
        }

        string? targetParent = Path.GetDirectoryName(targetRootPath);
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
                        "Configuration conflict: NuGet plugin layout target parent path "
                        + "contains a symbolic-link or reparse-point directory."
                    );
                }

                if (!fileSystem.DirectoryExists(directory) && fileSystem.FileExists(directory))
                {
                    throw new NotSupportedException(
                        "Configuration conflict: NuGet plugin layout target parent path "
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

    private string CreatePhysicalPathIdentity(string targetPathOrName)
    {
        string targetPath = fileSystem.GetFullPath(targetPathOrName);
        return NormalizePhysicalTargetConfigurationPathSegments(
            Path.TrimEndingDirectorySeparator(targetPath)
        );
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
                + normalized[duplicateSlashRootLength..]
                    .Replace("//", "/", StringComparison.Ordinal);
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

    private sealed record NuGetPluginLayoutDocument(
        string TargetRootPath,
        string MarkerPath,
        string OriginalText,
        byte[]? OriginalContentsBytes,
        FileMutationExpectation MutationExpectation
    )
    {
        public bool Exists => OriginalContentsBytes is not null && OriginalContentsBytes.Length > 0;
    }
}
