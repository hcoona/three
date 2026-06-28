using System.Globalization;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal interface IConfigurationPhysicalTargetWriterDispatcher
{
    ValueTask Dispatch(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    );
}

internal interface IConfigurationPhysicalTargetWriterDispatcherPreclaimPolicy
{
    bool RejectSecretGitConfigValueWritesBeforeManifestPreclaim { get; }
}

internal interface IConfigurationPhysicalTargetWriterDispatcherValidator
{
    void Validate(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    );
}

internal interface IConfigurationPhysicalTargetRetainedOwnershipProofValidator
{
    void ValidateRetainedOwnershipProofs(
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        CancellationToken cancellationToken
    );
}

internal sealed class ConfigurationPhysicalTargetWriterDispatcher(
    IFileSystem fileSystem
) : IConfigurationPhysicalTargetWriterDispatcher,
    IConfigurationPhysicalTargetWriterDispatcherPreclaimPolicy,
    IConfigurationPhysicalTargetWriterDispatcherValidator,
    IConfigurationPhysicalTargetRetainedOwnershipProofValidator
{
    private readonly GitConfigPhysicalTargetWriter gitConfigWriter = new(fileSystem);
    private readonly NpmrcPhysicalTargetWriter npmrcWriter = new(fileSystem);
    private readonly NuGetPluginLayoutPhysicalTargetWriter nuGetPluginLayoutWriter =
        new(fileSystem);
    private readonly PythonKeyringPhysicalTargetWriter pythonKeyringWriter = new(fileSystem);

    public bool RejectSecretGitConfigValueWritesBeforeManifestPreclaim => true;

    public ValueTask Dispatch(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        switch (request.TargetKind)
        {
            case ConfigurationTargetKind.GitConfig:
                gitConfigWriter.Write(request, cancellationToken);
                break;
            case ConfigurationTargetKind.Npmrc:
                npmrcWriter.Write(request, cancellationToken);
                break;
            case ConfigurationTargetKind.NuGetPluginLayout:
                nuGetPluginLayoutWriter.Write(request, cancellationToken);
                break;
            case ConfigurationTargetKind.PythonKeyringBackend:
            case ConfigurationTargetKind.KeyringShim:
                pythonKeyringWriter.Write(request, cancellationToken);
                break;
            default:
                throw new NotSupportedException(
                    "Configuration apply/remove has no registered writer for this 4D physical "
                        + "configuration target kind."
                );
        }

        return ValueTask.CompletedTask;
    }

    public void Validate(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        switch (request.TargetKind)
        {
            case ConfigurationTargetKind.GitConfig:
                gitConfigWriter.Validate(request, cancellationToken);
                break;
            case ConfigurationTargetKind.Npmrc:
                npmrcWriter.Validate(request, cancellationToken);
                break;
            case ConfigurationTargetKind.NuGetPluginLayout:
                nuGetPluginLayoutWriter.Validate(request, cancellationToken);
                break;
            case ConfigurationTargetKind.PythonKeyringBackend:
            case ConfigurationTargetKind.KeyringShim:
                pythonKeyringWriter.Validate(request, cancellationToken);
                break;
            default:
                throw new NotSupportedException(
                    "Configuration dry-run has no registered validator for this 4D physical "
                        + "configuration target kind."
                );
        }
    }

    public void ValidateRetainedOwnershipProofs(
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(ownershipProofs);
        cancellationToken.ThrowIfCancellationRequested();
        gitConfigWriter.ValidateRetainedOwnershipProofs(ownershipProofs, cancellationToken);
        npmrcWriter.ValidateRetainedOwnershipProofs(ownershipProofs, cancellationToken);
        nuGetPluginLayoutWriter.ValidateRetainedOwnershipProofs(ownershipProofs, cancellationToken);
        pythonKeyringWriter.ValidateRetainedOwnershipProofs(ownershipProofs, cancellationToken);
    }
}

internal sealed record ConfigurationPhysicalTargetWriterRequest
{
    public ConfigurationPhysicalTargetWriterRequest(
        ConfigurationPlanOperation planOperation,
        ConfigurationTargetKind targetKind,
        ConfigurationChangeOperation changeOperation,
        ConfigurationChange change,
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof>? ownershipProofs = null
    )
        : this(planOperation, targetKind, [change], ownershipProofs)
    {
        if (changeOperation != change.Operation)
        {
            throw new ArgumentException(
                "Configuration physical writer request operation must match the change.",
                nameof(changeOperation)
            );
        }
    }

    public ConfigurationPhysicalTargetWriterRequest(
        ConfigurationPlanOperation planOperation,
        ConfigurationTargetKind targetKind,
        IReadOnlyList<ConfigurationChange> changes,
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof>? ownershipProofs = null
    )
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
        {
            throw new ArgumentException(
                "Configuration physical writer request requires at least one change.",
                nameof(changes)
            );
        }

        if (changes.Any(change => change.TargetKind != targetKind))
        {
            throw new ArgumentException(
                "Configuration physical writer request changes must match the target kind.",
                nameof(changes)
            );
        }

        PlanOperation = planOperation;
        TargetKind = targetKind;
        Changes = changes.ToArray();
        OwnershipProofs = ownershipProofs?.ToArray() ?? [];
    }

    public ConfigurationPlanOperation PlanOperation { get; }

    public ConfigurationTargetKind TargetKind { get; }

    public IReadOnlyList<ConfigurationChange> Changes { get; }

    public IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> OwnershipProofs { get; }

    public CanonicalResourceIdentity? ResourceIdentity { get; init; }

    public IReadOnlyList<ConfigurationPhysicalTargetFileMutation> CompletedFileMutations =>
        completedFileMutations;

    public ConfigurationChangeOperation ChangeOperation => Change.Operation;

    public ConfigurationChange Change =>
        Changes.Count == 1
            ? Changes[0]
            : throw new InvalidOperationException(
                "Configuration physical writer request contains multiple changes."
            );

    public void RegisterCompletedFileMutation(
        ConfigurationPhysicalTargetFileMutation mutation
    )
    {
        ArgumentNullException.ThrowIfNull(mutation);
        completedFileMutations.Add(mutation);
    }

    private readonly List<ConfigurationPhysicalTargetFileMutation> completedFileMutations = [];
}

internal sealed record ConfigurationPhysicalTargetOwnershipProof(
    ConfigurationTargetKind TargetKind,
    string TargetPathOrName,
    string Key,
    string? PlannedValueSha256
);

internal sealed record ConfigurationPhysicalTargetFileMutation(
    string Path,
    bool PreviouslyExisted,
    byte[]? PreviousContentsBytes,
    string? ExpectedCurrentSha256Hash,
    bool RequiresRollback = true,
    UnixFileMode? PreviousUnixFileMode = null
);

internal sealed class GitConfigPhysicalTargetWriter(IFileSystem fileSystem)
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );
    private const string CredentialSectionName = "credential";
    private const string IncludeSectionName = "include";
    private const string IncludeIfSectionName = "includeIf";
    private const string DevAzureComCredentialSubsection = "https://dev.azure.com";
    private const string DevAzureComHost = "dev.azure.com";
    private const string HelperVariableName = "helper";
    private const string UseHttpPathVariableName = "useHttpPath";
    private const string UnsafeCredentialHelperValueMessage =
        "The Git config physical writer supports credential.helper only as a simple helper "
            + "name or fully qualified path without shell syntax.";
    private const string DevAzureComUseHttpPathCanonicalConfigurationKey =
        "credential.https://dev.azure.com.useHttpPath";
    private static readonly string DevAzureComUseHttpPathCanonicalTrueSha256 =
        ComputeSha256("true");

    public void Write(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        string targetPath = GetSingleNormalizedGitConfigTargetPath(request, cancellationToken);
        GitConfigChange[] changes = request
            .Changes.Select(change => CreateGitConfigChange(request, targetPath, change))
            .ToArray();
        EnsureNoDuplicateGitConfigChanges(changes);
        GitConfigDocument document = ReadDocument(targetPath);
        ValidateExistingOwnershipProofs(document, request.OwnershipProofs, targetPath);
        ValidateRetainedOwnershipProofs(request.OwnershipProofs, cancellationToken);
        ValidateEffectiveCredentialHelperConflicts(
            request.PlanOperation,
            document,
            changes,
            request.OwnershipProofs,
            targetPath
        );
        string updatedContents = CreateUpdatedContents(request.PlanOperation, document, changes);

        if (string.Equals(document.OriginalText, updatedContents, StringComparison.Ordinal))
        {
            if (document.OriginalContentsBytes is not null)
            {
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        targetPath,
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
            targetPath,
            document.OriginalContentsBytes is not null,
            document.OriginalContentsBytes,
            ComputeSha256(updatedContentsBytes)
        );
        try
        {
            fileSystem.AtomicWriteAllText(
                targetPath,
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
        string targetPath = GetSingleNormalizedGitConfigTargetPath(request, cancellationToken);
        GitConfigChange[] changes = request
            .Changes.Select(change => CreateGitConfigChange(request, targetPath, change))
            .ToArray();
        EnsureNoDuplicateGitConfigChanges(changes);
        GitConfigDocument document = ReadDocument(targetPath);
        ValidateExistingOwnershipProofs(document, request.OwnershipProofs, targetPath);
        ValidateRetainedOwnershipProofs(request.OwnershipProofs, cancellationToken);
        ValidateEffectiveCredentialHelperConflicts(
            request.PlanOperation,
            document,
            changes,
            request.OwnershipProofs,
            targetPath
        );
        _ = CreateUpdatedContents(request.PlanOperation, document, changes);
    }

    public void ValidateRetainedOwnershipProofs(
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(ownershipProofs);
        foreach (
            IGrouping<string, ConfigurationPhysicalTargetOwnershipProof> proofsByTarget in
                ownershipProofs
                    .Where(proof => proof.TargetKind == ConfigurationTargetKind.GitConfig)
                    .GroupBy(
                        proof => CreatePhysicalPathIdentity(proof.TargetPathOrName),
                        GetPathComparer()
                    )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            GitConfigDocument document = ReadDocument(proofsByTarget.Key);
            ValidateExistingOwnershipProofs(
                document,
                proofsByTarget.ToArray(),
                proofsByTarget.Key
            );
            ValidateEffectiveCredentialHelperConflictsForRetainedOwnershipProofs(
                document,
                proofsByTarget.ToArray(),
                proofsByTarget.Key
            );
        }
    }

    private string GetSingleNormalizedGitConfigTargetPath(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.TargetKind != ConfigurationTargetKind.GitConfig)
        {
            throw new NotSupportedException(
                "The Git config physical writer supports only GitConfig targets."
            );
        }

        return GetSingleNormalizedTargetPath(request.Changes);
    }

    private static string CreateUpdatedContents(
        ConfigurationPlanOperation planOperation,
        GitConfigDocument document,
        IReadOnlyList<GitConfigChange> changes
    ) =>
        planOperation switch
        {
            ConfigurationPlanOperation.Apply => Apply(document, changes),
            ConfigurationPlanOperation.Remove => Remove(document, changes),
            _ => throw new NotSupportedException(
                "The Git config physical writer supports apply/remove operations only."
            ),
        };

    private string GetSingleNormalizedTargetPath(IReadOnlyList<ConfigurationChange> changes)
    {
        string targetPath = CreatePhysicalPathIdentity(changes[0].TargetPathOrName);
        if (
            changes
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
                "The Git config physical writer supports only batches that target one normalized "
                    + "Git config file path."
            );
        }

        return targetPath;
    }

    private GitConfigDocument ReadDocument(string targetPath)
    {
        EnsureTargetPathCanBeSafelyMutated(targetPath);
        if (!fileSystem.FileExists(targetPath))
        {
            if (fileSystem.DirectoryExists(targetPath))
            {
                throw new InvalidOperationException(
                    "Configuration conflict: Git config target exists as a directory."
                );
            }

            return GitConfigDocument.CreateMissing(targetPath);
        }

        byte[] contents = fileSystem.ReadAllBytes(targetPath);
        if (StartsWithUtf8Bom(contents))
        {
            throw new NotSupportedException(
                "Configuration conflict: BOM-prefixed Git config files are not supported for "
                    + "safe physical mutation."
            );
        }

        string text = Utf8NoBom.GetString(contents);
        return GitConfigDocument.Parse(
            targetPath,
            text,
            FileMutationExpectation.Existing(ComputeSha256(contents)),
            contents
        );
    }

    private void EnsureTargetPathCanBeSafelyMutated(string targetPath)
    {
        if (IsUnsupportedLinkOrReparsePoint(targetPath))
        {
            throw new NotSupportedException(
                "Configuration conflict: Git config target path is a symbolic-link or "
                    + "reparse-point and is not supported."
            );
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
                        "Configuration conflict: Git config target parent path contains a "
                            + "symbolic-link or reparse-point directory."
                    );
                }

                if (!fileSystem.DirectoryExists(directory) && fileSystem.FileExists(directory))
                {
                    throw new NotSupportedException(
                        "Configuration conflict: Git config target parent path contains a "
                            + "non-directory entry."
                    );
                }
            }
            catch (FileNotFoundException)
            {
                // Missing parent directories are valid for first apply; the conditional write
                // creates them after the existing chain has been proven safe.
            }
            catch (DirectoryNotFoundException)
            {
                // See FileNotFoundException handling above.
            }
        }
    }

    private bool IsUnsupportedLinkOrReparsePoint(string targetPath)
    {
        try
        {
            if (fileSystem.IsSymbolicLink(targetPath))
            {
                return true;
            }

            return fileSystem is IFileSystemReparsePointSafety reparsePointSafety
                && reparsePointSafety.IsReparsePoint(targetPath);
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


    private static string Apply(
        GitConfigDocument document,
        IReadOnlyList<GitConfigChange> changes
    )
    {
        List<string> lines = [.. document.Lines];
        bool appended = false;
        foreach (GitConfigChange change in changes)
        {
            if (change.Value is null)
            {
                throw new NotSupportedException(
                    "Git config apply changes require a planned value."
                );
            }

            ThrowIfValueCannotBeWritten(change.Value);
            GitConfigEntryLocation[] existingEntries = document.FindEntries(change.Key).ToArray();
            ValidateApplyExistingEntries(change, existingEntries);
            string renderedLine = RenderEntryLine(change.Key, change.Value);
            if (existingEntries.Length == 1)
            {
                lines[existingEntries[0].LineIndex] = renderedLine;
                document = document with { Lines = lines };
                continue;
            }

            InsertEntryLine(lines, document, change.Key, renderedLine);
            appended = true;
            document = GitConfigDocument.Parse(
                document.Path,
                Render(lines, document.NewLine, document.HadTrailingNewLine || appended),
                document.MutationExpectation
            );
        }

        return Render(lines, document.NewLine, document.HadTrailingNewLine || appended);
    }

    private static string Remove(
        GitConfigDocument document,
        IReadOnlyList<GitConfigChange> changes
    )
    {
        List<string> lines = [.. document.Lines];
        var removeLineIndexes = new SortedSet<int>();
        foreach (GitConfigChange change in changes)
        {
            GitConfigEntryLocation[] existingEntries = document.FindEntries(change.Key).ToArray();
            ValidateRemoveExistingEntries(change, existingEntries);
            removeLineIndexes.Add(existingEntries[0].LineIndex);
        }

        foreach (int lineIndex in removeLineIndexes.Reverse())
        {
            lines.RemoveAt(lineIndex);
        }

        return Render(lines, document.NewLine, document.HadTrailingNewLine);
    }

    private static void ValidateApplyExistingEntries(
        GitConfigChange change,
        IReadOnlyList<GitConfigEntryLocation> existingEntries
    )
    {
        if (existingEntries.Count > 1)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Git config key has multiple existing declarations and "
                    + "cannot be updated safely."
            );
        }

        if (existingEntries.Count == 0)
        {
            if (
                change.Change.Operation
                    is ConfigurationChangeOperation.Update or ConfigurationChangeOperation.Refresh
                || change.HasOwnershipProof
            )
            {
                throw new InvalidOperationException(
                    "Configuration conflict: owned Git config key is missing from the physical "
                        + "configuration file."
                );
            }

            return;
        }

        if (change.Change.Operation == ConfigurationChangeOperation.Create)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Git config create target already exists."
            );
        }

        if (!change.HasOwnershipProof)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Git config key already exists and is not proven to be "
                    + "owned by the existing manifest."
            );
        }

        ValidateOwnedExistingEntryValueHash(change, existingEntries[0]);
    }

    private static void ValidateRemoveExistingEntries(
        GitConfigChange change,
        IReadOnlyList<GitConfigEntryLocation> existingEntries
    )
    {
        if (change.Change.Operation != ConfigurationChangeOperation.Remove)
        {
            throw new NotSupportedException(
                "Git config remove supports only remove changes."
            );
        }

        if (existingEntries.Count == 0)
        {
            throw new InvalidOperationException(
                "Configuration conflict: owned Git config key is missing from the physical "
                    + "configuration file."
            );
        }

        if (existingEntries.Count > 1)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Git config key has multiple existing declarations and "
                    + "cannot be removed safely."
            );
        }

        if (!change.HasOwnershipProof)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Git config key is not proven to be owned by this "
                    + "product in the existing manifest."
            );
        }

        ValidateOwnedExistingEntryValueHash(change, existingEntries[0]);
    }

    private static void ValidateOwnedExistingEntryValueHash(
        GitConfigChange change,
        GitConfigEntryLocation existingEntry
    )
    {
        string? expectedHash = change.OwnershipProofPlannedValueSha256;
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            throw new InvalidOperationException(
                "Configuration conflict: owned Git config key manifest planned value hash is "
                    + "required."
            );
        }

        ValidateOwnedUseHttpPathCanonicalTrueValue(
            change.Key,
            expectedHash,
            existingEntry
        );
        string actualHash = ComputeSha256(existingEntry.Value);
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Configuration conflict: owned Git config key current value hash does not match "
                    + "the existing manifest."
            );
        }
    }

    private static void InsertEntryLine(
        List<string> lines,
        GitConfigDocument document,
        GitConfigKey key,
        string renderedLine
    )
    {
        GitConfigSectionLocation? section = document.FindSection(key);
        if (section is null)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add(string.Empty);
            }

            lines.Add(RenderSectionHeader(key));
            lines.Add(renderedLine);
            return;
        }

        lines.Insert(section.Value.EndExclusiveLineIndex, renderedLine);
    }

    private GitConfigChange CreateGitConfigChange(
        ConfigurationPhysicalTargetWriterRequest request,
        string normalizedTargetPath,
        ConfigurationChange change
    )
    {
        if (change.TargetKind != ConfigurationTargetKind.GitConfig)
        {
            throw new NotSupportedException(
                "The Git config physical writer supports only GitConfig changes."
            );
        }

        GitConfigKey key = ParseSupportedGitConfigKey(change.Key);
        ThrowIfUnsupportedGoldenSliceValue(
            key,
            change,
            rejectSecretValueWrites: true
        );
        return new GitConfigChange(
            key,
            change,
            FindOwnershipProof(request.OwnershipProofs, normalizedTargetPath, key)
        );
    }

    private static void ThrowIfUnsupportedGoldenSliceValue(
        GitConfigKey key,
        ConfigurationChange change,
        bool rejectSecretValueWrites
    )
    {
        if (
            rejectSecretValueWrites
            && change.Operation != ConfigurationChangeOperation.Remove
            && change.IsSecretValue
        )
        {
            throw new NotSupportedException(
                "The Git config physical writer requires non-secret values so ownership can be "
                    + "verified against physical file contents."
            );
        }

        if (
            string.Equals(
                key.CanonicalConfigurationKey,
                DevAzureComUseHttpPathCanonicalConfigurationKey,
                StringComparison.Ordinal
            )
            && change.Operation != ConfigurationChangeOperation.Remove
            && !string.Equals(change.Value, "true", StringComparison.Ordinal)
        )
        {
            throw new NotSupportedException(
                "The Git config physical writer supports credential "
                    + "\"https://dev.azure.com\".useHttpPath only with canonical value true."
            );
        }

        if (
            string.Equals(
                key.CanonicalConfigurationKey,
                "credential.helper",
                StringComparison.Ordinal
            )
            && change.Operation != ConfigurationChangeOperation.Remove
            && change.Value is not null
        )
        {
            ThrowIfUnsafeCredentialHelperValue(change.Value);
        }
    }

    internal static bool TryCanonicalizeSupportedConfigurationKey(
        string key,
        out string canonicalKey
    )
    {
        if (string.Equals(key, "credential.helper", StringComparison.Ordinal))
        {
            canonicalKey = "credential.helper";
            return true;
        }

        if (
            string.Equals(
                key,
                DevAzureComUseHttpPathCanonicalConfigurationKey,
                StringComparison.Ordinal
            )
            || string.Equals(
                key,
                "credential \"https://dev.azure.com\".useHttpPath",
                StringComparison.Ordinal
            )
        )
        {
            canonicalKey = DevAzureComUseHttpPathCanonicalConfigurationKey;
            return true;
        }

        canonicalKey = key;
        return false;
    }

    internal static string CanonicalizeSupportedConfigurationKey(string key) =>
        TryCanonicalizeSupportedConfigurationKey(key, out string canonicalKey)
            ? canonicalKey
            : throw new NotSupportedException(
                "The Git config physical writer currently supports only credential.helper and "
                    + "credential \"https://dev.azure.com\".useHttpPath keys."
            );

    internal static void ValidateChangeBeforeManifestPreclaim(
        ConfigurationChange change,
        bool rejectSecretValueWrites
    )
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.TargetKind != ConfigurationTargetKind.GitConfig)
        {
            return;
        }

        if (change.Operation == ConfigurationChangeOperation.RemoveAdapter)
        {
            throw new NotSupportedException(
                "The Git config physical writer does not support remove-adapter changes."
            );
        }

        GitConfigKey key = ParseSupportedGitConfigKey(change.Key);
        ThrowIfUnsupportedGoldenSliceValue(key, change, rejectSecretValueWrites);
        if (change.Operation != ConfigurationChangeOperation.Remove && change.Value is not null)
        {
            ThrowIfValueCannotBeWritten(change.Value);
        }
    }

    private static GitConfigKey ParseSupportedGitConfigKey(string key)
    {
        string canonicalKey = CanonicalizeSupportedConfigurationKey(key);
        if (string.Equals(canonicalKey, "credential.helper", StringComparison.Ordinal))
        {
            return new GitConfigKey(
                CredentialSectionName,
                null,
                HelperVariableName,
                canonicalKey
            );
        }

        if (
            string.Equals(
                canonicalKey,
                DevAzureComUseHttpPathCanonicalConfigurationKey,
                StringComparison.Ordinal
            )
        )
        {
            return new GitConfigKey(
                CredentialSectionName,
                DevAzureComCredentialSubsection,
                UseHttpPathVariableName,
                canonicalKey
            );
        }

        throw new NotSupportedException(
            "The Git config physical writer currently supports only credential.helper and "
                + "credential \"https://dev.azure.com\".useHttpPath keys."
        );
    }

    private ConfigurationPhysicalTargetOwnershipProof? FindOwnershipProof(
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        string normalizedTargetPath,
        GitConfigKey key
    ) =>
        ownershipProofs.FirstOrDefault(proof =>
            proof.TargetKind == ConfigurationTargetKind.GitConfig
            && string.Equals(
                CreatePhysicalPathIdentity(proof.TargetPathOrName),
                normalizedTargetPath,
                GetPathComparison()
            )
            && TryCanonicalizeSupportedConfigurationKey(proof.Key, out string proofKey)
            && string.Equals(proofKey, key.CanonicalConfigurationKey, StringComparison.Ordinal)
        );

    private void ValidateExistingOwnershipProofs(
        GitConfigDocument document,
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        string normalizedTargetPath
    )
    {
        foreach (
            ConfigurationPhysicalTargetOwnershipProof proof in ownershipProofs.Where(proof =>
                proof.TargetKind == ConfigurationTargetKind.GitConfig
                && string.Equals(
                    CreatePhysicalPathIdentity(proof.TargetPathOrName),
                    normalizedTargetPath,
                    GetPathComparison()
                )
            )
        )
        {
            GitConfigKey key = ParseSupportedGitConfigKey(proof.Key);
            GitConfigEntryLocation[] entries = document.FindEntries(key).ToArray();
            if (entries.Length != 1)
            {
                throw new InvalidOperationException(
                    "Configuration conflict: owned Git config key is missing or duplicated in "
                        + "the physical configuration file."
                );
            }

            if (string.IsNullOrWhiteSpace(proof.PlannedValueSha256))
            {
                throw new InvalidOperationException(
                    "Configuration conflict: owned Git config key has no verifiable planned "
                        + "value hash."
                );
            }

            ValidateOwnedUseHttpPathCanonicalTrueValue(
                key,
                proof.PlannedValueSha256,
                entries[0]
            );
            string physicalValueHash = ComputeSha256(entries[0].Value);
            if (
                !string.Equals(
                    physicalValueHash,
                    proof.PlannedValueSha256,
                    StringComparison.Ordinal
                )
            )
            {
                throw new InvalidOperationException(
                    "Configuration conflict: owned Git config current value hash does not "
                        + "match the existing manifest."
                );
            }
        }
    }

    private void ValidateEffectiveCredentialHelperConflicts(
        ConfigurationPlanOperation planOperation,
        GitConfigDocument document,
        IReadOnlyList<GitConfigChange> changes,
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        string normalizedTargetPath
    )
    {
        if (planOperation != ConfigurationPlanOperation.Apply)
        {
            return;
        }

        bool hasHelperWrite =
            changes.Any(change => IsSupportedGlobalCredentialHelperKey(change.Key));
        bool hasUseHttpPathWrite = changes.Any(change => IsDevAzureComUseHttpPathKey(change.Key));
        if (!hasHelperWrite && !hasUseHttpPathWrite)
        {
            return;
        }

        if (hasHelperWrite && !hasUseHttpPathWrite)
        {
            GitConfigKey useHttpPathKey = ParseSupportedGitConfigKey(
                DevAzureComUseHttpPathCanonicalConfigurationKey
            );
            GitConfigEntryLocation[] effectiveUseHttpPathEntries = document
                .FindEntries(useHttpPathKey)
                .ToArray();
            if (
                effectiveUseHttpPathEntries.Length > 0
                && IsTruthyGitConfigBooleanValue(effectiveUseHttpPathEntries[^1].Value)
                && FindOwnershipProof(ownershipProofs, normalizedTargetPath, useHttpPathKey)
                    is null
            )
            {
                throw new InvalidOperationException(
                    "Configuration conflict: existing credential "
                        + "\"https://dev.azure.com\".useHttpPath=true entries must be owned "
                        + "before writing credential.helper."
                );
            }
        }

        ValidateEffectiveCredentialHelperConflicts(
            document,
            ownershipProofs,
            normalizedTargetPath
        );
    }

    private void ValidateEffectiveCredentialHelperConflictsForRetainedOwnershipProofs(
        GitConfigDocument document,
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        string normalizedTargetPath
    )
    {
        if (!ownershipProofs.Any(IsEffectiveCredentialHelperConflictRelevantProof))
        {
            return;
        }

        ValidateEffectiveCredentialHelperConflicts(
            document,
            ownershipProofs,
            normalizedTargetPath
        );
    }

    private void ValidateEffectiveCredentialHelperConflicts(
        GitConfigDocument document,
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        string normalizedTargetPath
    )
    {
        GitConfigEntryLocation[] effectiveCredentialHelpers = document
            .FindAzureDevOpsEffectiveCredentialHelperEntries()
            .ToArray();
        if (effectiveCredentialHelpers.Length == 0)
        {
            return;
        }

        GitConfigKey globalHelperKey = ParseSupportedGitConfigKey("credential.helper");
        ConfigurationPhysicalTargetOwnershipProof? globalHelperOwnershipProof =
            FindOwnershipProof(ownershipProofs, normalizedTargetPath, globalHelperKey);
        foreach (GitConfigEntryLocation helperEntry in effectiveCredentialHelpers)
        {
            if (!IsSupportedGlobalCredentialHelperKey(helperEntry.Key))
            {
                throw new InvalidOperationException(
                    "Configuration conflict: effective Azure DevOps Git credential helper "
                        + "entries must be owned supported global credential.helper entries."
                );
            }

            if (globalHelperOwnershipProof is null)
            {
                throw new InvalidOperationException(
                    "Configuration conflict: effective Git credential helper is not proven to be "
                        + "owned by the existing manifest."
                );
            }

            ValidateOwnedCredentialHelperEntryValueHash(
                globalHelperOwnershipProof,
                helperEntry
            );
        }
    }

    private static bool IsEffectiveCredentialHelperConflictRelevantKey(GitConfigKey key) =>
        IsSupportedGlobalCredentialHelperKey(key) || IsDevAzureComUseHttpPathKey(key);

    private static bool IsEffectiveCredentialHelperConflictRelevantProof(
        ConfigurationPhysicalTargetOwnershipProof ownershipProof
    ) =>
        TryCanonicalizeSupportedConfigurationKey(
            ownershipProof.Key,
            out string canonicalKey
        )
        && (
            string.Equals(canonicalKey, "credential.helper", StringComparison.Ordinal)
            || string.Equals(
                canonicalKey,
                DevAzureComUseHttpPathCanonicalConfigurationKey,
                StringComparison.Ordinal
            )
        );

    private static bool IsSupportedGlobalCredentialHelperKey(GitConfigKey key) =>
        string.Equals(
            key.SectionName,
            CredentialSectionName,
            StringComparison.OrdinalIgnoreCase
        )
        && key.Subsection is null
        && string.Equals(
            key.VariableName,
            HelperVariableName,
            StringComparison.OrdinalIgnoreCase
        );

    private static bool IsDevAzureComUseHttpPathKey(GitConfigKey key) =>
        string.Equals(
            key.CanonicalConfigurationKey,
            DevAzureComUseHttpPathCanonicalConfigurationKey,
            StringComparison.Ordinal
        );

    private static bool IsTruthyGitConfigBooleanValue(string value)
    {
        if (
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        if (value.Length == 0)
        {
            return false;
        }

        int index = 0;
        if (value[index] is '+' or '-')
        {
            index++;
            if (index == value.Length)
            {
                return false;
            }
        }

        if (value[index] == '0')
        {
            if (index + 1 == value.Length)
            {
                return false;
            }

            if (value[index + 1] is 'x' or 'X')
            {
                return TryParseTruthyGitConfigNumericValue(value, index + 2, 16);
            }

            return TryParseTruthyGitConfigNumericValue(value, index + 1, 8);
        }

        return TryParseTruthyGitConfigNumericValue(value, index, 10);
    }

    private static bool TryParseTruthyGitConfigNumericValue(
        string value,
        int startIndex,
        int numberBase
    )
    {
        bool hasDigit = false;
        bool hasNonZeroDigit = false;
        for (int index = startIndex; index < value.Length; index++)
        {
            int digit = value[index] switch
            {
                >= '0' and <= '9' => value[index] - '0',
                >= 'a' and <= 'f' when numberBase == 16 => value[index] - 'a' + 10,
                >= 'A' and <= 'F' when numberBase == 16 => value[index] - 'A' + 10,
                _ => -1,
            };
            if (digit < 0 || digit >= numberBase)
            {
                return false;
            }

            hasDigit = true;
            if (digit != 0)
            {
                hasNonZeroDigit = true;
            }
        }

        return hasDigit && hasNonZeroDigit;
    }

    private static void ValidateOwnedUseHttpPathCanonicalTrueValue(
        GitConfigKey key,
        string plannedValueSha256,
        GitConfigEntryLocation existingEntry
    )
    {
        if (!IsDevAzureComUseHttpPathKey(key))
        {
            return;
        }

        if (
            string.Equals(
                plannedValueSha256,
                DevAzureComUseHttpPathCanonicalTrueSha256,
                StringComparison.Ordinal
            )
            && string.Equals(existingEntry.Value, "true", StringComparison.Ordinal)
        )
        {
            return;
        }

        throw new InvalidOperationException(
            "Configuration conflict: owned Git config credential "
                + "\"https://dev.azure.com\".useHttpPath entries must retain canonical value true."
        );
    }

    private static void ValidateOwnedCredentialHelperEntryValueHash(
        ConfigurationPhysicalTargetOwnershipProof ownershipProof,
        GitConfigEntryLocation helperEntry
    )
    {
        if (string.IsNullOrWhiteSpace(ownershipProof.PlannedValueSha256))
        {
            throw new InvalidOperationException(
                "Configuration conflict: owned Git config key manifest planned value hash is "
                    + "required."
            );
        }

        string actualHash = ComputeSha256(helperEntry.Value);
        if (
            !string.Equals(
                actualHash,
                ownershipProof.PlannedValueSha256,
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                "Configuration conflict: owned Git config key current value hash does not match "
                    + "the existing manifest."
            );
        }
    }

    private static void EnsureNoDuplicateGitConfigChanges(
        IReadOnlyList<GitConfigChange> changes
    )
    {
        if (
            changes
                .GroupBy(
                    change => change.Key.CanonicalConfigurationKey,
                    StringComparer.Ordinal
                )
                .Any(group => group.Count() > 1)
        )
        {
            throw new NotSupportedException(
                "The Git config physical writer does not support multiple changes for the same "
                    + "canonical Git config key in one batch."
            );
        }
    }

    private static bool StartsWithUtf8Bom(byte[] contents) =>
        contents.Length >= 3
        && contents[0] == 0xEF
        && contents[1] == 0xBB
        && contents[2] == 0xBF;

    private static void ThrowIfValueCannotBeWritten(string value)
    {
        if (
            value.Contains('\n')
            || value.Contains('\r')
            || value.Any(character => character < ' ' || character == '\u007f')
        )
        {
            throw new NotSupportedException(
                "Git config physical writer values must be single-line printable values."
            );
        }
    }

    private static void ThrowIfUnsafeCredentialHelperValue(string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        if (value.StartsWith('!'))
        {
            throw new NotSupportedException(
                "The Git config physical writer supports credential.helper only with installed "
                    + "helper entries, not shell snippet helpers."
            );
        }

        bool isFullyQualifiedPath = Path.IsPathFullyQualified(value);
        if (
            value.Any(character =>
                character is '(' or ')'
                    ? !isFullyQualifiedPath
                    : !IsSafeCredentialHelperCharacter(character)
            )
        )
        {
            throw new NotSupportedException(UnsafeCredentialHelperValueMessage);
        }

        if (
            value.Length >= 2
            && char.IsLetter(value[0])
            && value[1] == ':'
            && !Path.IsPathFullyQualified(value)
        )
        {
            throw new NotSupportedException(UnsafeCredentialHelperValueMessage);
        }

        if (HasCredentialHelperPathSeparator(value) && !Path.IsPathFullyQualified(value))
        {
            throw new NotSupportedException(UnsafeCredentialHelperValueMessage);
        }
    }

    private static bool HasCredentialHelperPathSeparator(string value) =>
        value.Contains(Path.DirectorySeparatorChar)
        || value.Contains(Path.AltDirectorySeparatorChar);

    private static bool IsSafeCredentialHelperCharacter(char character) =>
        character is >= '0' and <= '9'
        or >= 'A' and <= 'Z'
        or >= 'a' and <= 'z'
        or '.' or '_' or '-' or '/' or ':' or '\\';

    internal static string EscapeCredentialHelperPathForShell(string value)
    {
        if (!Path.IsPathFullyQualified(value) || (!value.Contains('(') && !value.Contains(')')))
        {
            return value;
        }

        return value
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }

    private static string RenderGitConfigValue(GitConfigKey key, string value) =>
        IsSupportedGlobalCredentialHelperKey(key)
            ? EscapeCredentialHelperPathForShell(value)
            : value;

    private static string RenderEntryLine(GitConfigKey key, string value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"\t{key.VariableName} = {QuoteValue(RenderGitConfigValue(key, value))}"
        );

    private static string QuoteValue(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (char character in value)
        {
            if (character is '\\' or '"')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string RenderSectionHeader(GitConfigKey key) =>
        key.Subsection is null
            ? $"[{key.SectionName}]"
            : $"[{key.SectionName} \"{EscapeSubsection(key.Subsection)}\"]";

    private static string EscapeSubsection(string subsection) =>
        subsection.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string Render(
        List<string> lines,
        string newLine,
        bool trailingNewLine
    )
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        string text = string.Join(newLine, lines);
        return trailingNewLine ? text + newLine : text;
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

    private static string NormalizePhysicalTargetConfigurationPathSegments(string path)
    {
        ConfigurationPathKind kind = GetConfigurationPathKind(path);
        return kind == ConfigurationPathKind.Invalid
            ? NormalizeRelativeConfigurationPathSegments(path)
            : NormalizeAbsoluteConfigurationPathSegments(path);
    }

    private static string NormalizeAbsoluteConfigurationPathSegments(string path)
    {
        ConfigurationPathKind kind = GetConfigurationPathKind(path);
        if (kind == ConfigurationPathKind.Invalid)
        {
            return path;
        }

        string normalized = IsWindowsConfigurationPathKind(kind)
            ? path.Replace('\\', '/')
            : path;
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

        int rootLength = GetAbsoluteConfigurationPathRootLength(normalized, kind);
        while (normalized.Length > rootLength && normalized.EndsWith('/'))
        {
            normalized = normalized[..^1];
        }

        string root = normalized[..rootLength];
        string remainder = normalized[rootLength..];
        var segments = new List<string>();
        foreach (string segment in remainder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            return root;
        }

        string joinedSegments = string.Join('/', segments);
        return root.EndsWith('/') ? root + joinedSegments : root + "/" + joinedSegments;
    }

    private static string NormalizeRelativeConfigurationPathSegments(string path)
    {
        if (IsRootedInvalidConfigurationPath(path))
        {
            return NormalizeConfigurationPath(path);
        }

        string normalized = path.Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        var segments = new List<string>();
        foreach (string segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0 && segments[^1] != "..")
                {
                    segments.RemoveAt(segments.Count - 1);
                }
                else
                {
                    segments.Add(segment);
                }

                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static string NormalizeConfigurationPath(string path)
    {
        ConfigurationPathKind kind = GetConfigurationPathKind(path);
        string normalized = IsWindowsConfigurationPathKind(kind)
            ? path.Replace('\\', '/')
            : path;
        int rootLength = kind == ConfigurationPathKind.WindowsUnc ? 2 : 0;
        while (normalized.IndexOf("//", rootLength, StringComparison.Ordinal) >= 0)
        {
            normalized =
                normalized[..rootLength]
                + normalized[rootLength..].Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized.TrimEnd('/');
    }

    private static bool IsRootedInvalidConfigurationPath(string path) =>
        path.Length > 0 && (path[0] == '/' || path[0] == '\\');

    private static int GetAbsoluteConfigurationPathRootLength(
        string normalizedPath,
        ConfigurationPathKind kind
    )
    {
        if (kind == ConfigurationPathKind.PosixAbsolute)
        {
            return 1;
        }

        if (kind == ConfigurationPathKind.WindowsDrive)
        {
            return Math.Min(3, normalizedPath.Length);
        }

        int serverEnd = normalizedPath.IndexOf('/', 2);
        if (serverEnd < 0)
        {
            return normalizedPath.Length;
        }

        int shareEnd = normalizedPath.IndexOf('/', serverEnd + 1);
        return shareEnd < 0 ? normalizedPath.Length : shareEnd;
    }

    private static ConfigurationPathKind GetConfigurationPathKind(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return ConfigurationPathKind.Invalid;
        }

        if (
            path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal)
        )
        {
            return ConfigurationPathKind.WindowsUnc;
        }

        if (
            path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/')
        )
        {
            return ConfigurationPathKind.WindowsDrive;
        }

        if (path[0] == '/')
        {
            return path.Contains('\\', StringComparison.Ordinal)
                ? ConfigurationPathKind.Invalid
                : ConfigurationPathKind.PosixAbsolute;
        }

        return ConfigurationPathKind.Invalid;
    }

    private static bool IsWindowsConfigurationPathKind(ConfigurationPathKind kind) =>
        kind is ConfigurationPathKind.WindowsDrive or ConfigurationPathKind.WindowsUnc;

    private enum ConfigurationPathKind
    {
        Invalid,
        WindowsDrive,
        WindowsUnc,
        PosixAbsolute,
    }

    private static string ComputeSha256(string value) =>
        ComputeSha256(Encoding.UTF8.GetBytes(value));

    private static string ComputeSha256(byte[] value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(value))
            .ToLower(CultureInfo.InvariantCulture);

    private sealed record GitConfigChange(
        GitConfigKey Key,
        ConfigurationChange Change,
        ConfigurationPhysicalTargetOwnershipProof? OwnershipProof
    )
    {
        public string? Value => Change.Value;

        public bool HasOwnershipProof => OwnershipProof is not null;

        public string? OwnershipProofPlannedValueSha256 =>
            OwnershipProof?.PlannedValueSha256;
    }

    private readonly record struct GitConfigKey(
        string SectionName,
        string? Subsection,
        string VariableName,
        string CanonicalConfigurationKey
    );

    private readonly record struct GitConfigSectionLocation(
        GitConfigKey Key,
        int HeaderLineIndex,
        int EndExclusiveLineIndex
    );

    private readonly record struct GitConfigEntryLocation(
        GitConfigKey Key,
        int LineIndex,
        string Value
    );

    private sealed record GitConfigDocument
    {
        private GitConfigDocument(
            string path,
            string originalText,
            byte[]? originalContentsBytes,
            IReadOnlyList<string> lines,
            string newLine,
            bool hadTrailingNewLine,
            IReadOnlyList<GitConfigSectionLocation> sections,
            IReadOnlyList<GitConfigEntryLocation> entries,
            FileMutationExpectation mutationExpectation
        )
        {
            Path = path;
            OriginalText = originalText;
            OriginalContentsBytes = originalContentsBytes;
            Lines = lines;
            NewLine = newLine;
            HadTrailingNewLine = hadTrailingNewLine;
            Sections = sections;
            Entries = entries;
            MutationExpectation = mutationExpectation;
        }

        public string Path { get; }

        public string OriginalText { get; }

        public byte[]? OriginalContentsBytes { get; }

        public IReadOnlyList<string> Lines { get; init; }

        public string NewLine { get; }

        public bool HadTrailingNewLine { get; }

        public IReadOnlyList<GitConfigSectionLocation> Sections { get; }

        public IReadOnlyList<GitConfigEntryLocation> Entries { get; }

        public FileMutationExpectation MutationExpectation { get; }

        public static GitConfigDocument CreateMissing(string path) =>
            new(
                path,
                string.Empty,
                null,
                [],
                Environment.NewLine,
                hadTrailingNewLine: false,
                [],
                [],
                FileMutationExpectation.Missing
            );

        public static GitConfigDocument Parse(
            string path,
            string text,
            FileMutationExpectation mutationExpectation,
            byte[]? originalContentsBytes = null
        )
        {
            string newLine = DetectNewLine(text);
            string[] lines = SplitLines(text);
            bool hadTrailingNewLine = text.EndsWith('\n');
            var sections = new List<GitConfigSectionLocation>();
            var entries = new List<GitConfigEntryLocation>();
            GitConfigSectionBuilder? currentSection = null;

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                ThrowIfLineContainsUnsupportedSyntax(line);
                if (TryParseSection(line, out GitConfigKey sectionKey))
                {
                    ThrowIfUnsupportedIncludeDirective(sectionKey);
                    if (currentSection is not null)
                    {
                        sections.Add(currentSection.ToLocation(lineIndex));
                    }

                    currentSection = new GitConfigSectionBuilder(sectionKey, lineIndex);
                    continue;
                }

                if (line.TrimStart().StartsWith('['))
                {
                    throw new NotSupportedException(
                        "Configuration conflict: Git config section syntax is not supported for "
                            + "safe physical mutation."
                    );
                }

                if (
                    currentSection is not null
                    && TryParseVariableAssignment(
                        line,
                        out string variableName,
                        out string variableValue
                    )
                )
                {
                    entries.Add(
                        new GitConfigEntryLocation(
                            currentSection.Key with { VariableName = variableName },
                            lineIndex,
                            variableValue
                        )
                    );
                    continue;
                }

                if (currentSection is null && IsNonCommentContent(line))
                {
                    throw new NotSupportedException(
                        "Configuration conflict: top-level Git config content is not supported "
                            + "for safe physical mutation."
                    );
                }

                if (currentSection is not null && IsNonCommentContent(line))
                {
                    throw new NotSupportedException(
                        "Configuration conflict: Git config variable syntax is not supported for "
                            + "safe physical mutation."
                    );
                }
            }

            if (currentSection is not null)
            {
                sections.Add(currentSection.ToLocation(lines.Length));
            }

            return new GitConfigDocument(
                path,
                text,
                originalContentsBytes,
                lines,
                newLine,
                hadTrailingNewLine,
                sections,
                entries,
                mutationExpectation
            );
        }

        public IEnumerable<GitConfigEntryLocation> FindEntries(GitConfigKey key) =>
            Entries.Where(entry => KeysEqual(entry.Key, key));

        public IEnumerable<GitConfigEntryLocation>
            FindAzureDevOpsEffectiveCredentialHelperEntries()
        {
            foreach (GitConfigEntryLocation entry in Entries)
            {
                if (!IsCredentialHelperEntry(entry.Key))
                {
                    continue;
                }

                if (entry.Key.Subsection is null)
                {
                    yield return entry;
                    continue;
                }

                DevAzureComCredentialSubsectionMatch match =
                    GetDevAzureComCredentialSubsectionMatch(entry.Key.Subsection);
                if (match == DevAzureComCredentialSubsectionMatch.UnsafeEffectiveAlias)
                {
                    throw new NotSupportedException(
                        "Configuration conflict: Git config contains an effective Azure DevOps "
                            + "credential helper subsection alias that cannot be canonicalized "
                            + "safely."
                    );
                }

                if (match == DevAzureComCredentialSubsectionMatch.Canonicalizable)
                {
                    yield return entry;
                }
            }
        }

        public GitConfigSectionLocation? FindSection(GitConfigKey key)
        {
            foreach (GitConfigSectionLocation section in Sections)
            {
                if (SectionsEqual(section.Key, key))
                {
                    return section;
                }
            }

            return null;
        }

        private static string DetectNewLine(string text)
        {
            bool hasCrLf = false;
            bool hasLf = false;
            bool hasBareCr = false;
            for (var index = 0; index < text.Length; index++)
            {
                char character = text[index];
                if (character == '\r')
                {
                    if (index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        hasCrLf = true;
                        index++;
                    }
                    else
                    {
                        hasBareCr = true;
                    }

                    continue;
                }

                if (character == '\n')
                {
                    hasLf = true;
                }
            }

            if (hasBareCr || (hasCrLf && hasLf))
            {
                throw new NotSupportedException(
                    "Configuration conflict: mixed Git config newline styles are not supported "
                        + "for safe physical mutation."
                );
            }

            return hasCrLf ? "\r\n" : "\n";
        }

        private static string[] SplitLines(string text)
        {
            if (text.Length == 0)
            {
                return [];
            }

            string[] rawLines = text.Split('\n');
            int lineCount = text.EndsWith('\n') ? rawLines.Length - 1 : rawLines.Length;
            var lines = new string[lineCount];
            for (var index = 0; index < lineCount; index++)
            {
                lines[index] = rawLines[index].EndsWith('\r')
                    ? rawLines[index][..^1]
                    : rawLines[index];
            }

            return lines;
        }

        private static bool TryParseSection(string line, out GitConfigKey key)
        {
            key = default;
            string trimmedStart = line.TrimStart();
            if (!trimmedStart.StartsWith('['))
            {
                return false;
            }

            var index = 1;
            int sectionNameStartIndex = index;
            while (
                index < trimmedStart.Length
                && IsSupportedSectionNameCharacter(trimmedStart[index])
            )
            {
                index++;
            }

            if (index == sectionNameStartIndex)
            {
                return false;
            }

            string sectionName = trimmedStart[sectionNameStartIndex..index];
            if (!IsSupportedSectionName(sectionName))
            {
                return false;
            }

            if (index >= trimmedStart.Length)
            {
                return false;
            }

            if (trimmedStart[index] == ']')
            {
                if (!IsSectionHeaderTrailingTextSupported(trimmedStart[(index + 1)..]))
                {
                    return false;
                }

                key = new GitConfigKey(sectionName, null, string.Empty, string.Empty);
                return true;
            }

            if (trimmedStart[index] is not ' ' and not '\t')
            {
                return false;
            }

            while (index < trimmedStart.Length && trimmedStart[index] is ' ' or '\t')
            {
                index++;
            }

            if (
                index >= trimmedStart.Length
                || trimmedStart[index] != '"'
                || !TryParseQuotedSubsection(
                    trimmedStart,
                    index,
                    out string subsection,
                    out int closingQuoteIndex
                )
                || closingQuoteIndex + 1 >= trimmedStart.Length
                || trimmedStart[closingQuoteIndex + 1] != ']'
                || !IsSectionHeaderTrailingTextSupported(
                    trimmedStart[(closingQuoteIndex + 2)..]
                )
            )
            {
                return false;
            }

            key = new GitConfigKey(
                sectionName,
                subsection,
                string.Empty,
                string.Empty
            );
            return true;
        }

        private static bool IsSupportedSectionName(string sectionName) =>
            sectionName.Length > 0
            && sectionName.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '-' or '.'
            );

        private static bool IsSupportedSectionNameCharacter(char character) =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '.';

        private static void ThrowIfUnsupportedIncludeDirective(GitConfigKey sectionKey)
        {
            if (
                string.Equals(
                    sectionKey.SectionName,
                    IncludeSectionName,
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    sectionKey.SectionName,
                    IncludeIfSectionName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new NotSupportedException(
                    "Configuration conflict: Git config include/includeIf directives are not "
                        + "supported for safe physical mutation."
                );
            }
        }

        private static bool IsSectionHeaderTrailingTextSupported(string text)
        {
            string trailingText = text.TrimStart();
            return trailingText.Length == 0 || trailingText[0] is '#' or ';';
        }

        private static void ThrowIfLineContainsUnsupportedSyntax(string line)
        {
            if (
                line.Any(character =>
                    character != '\t' && (character < ' ' || character == '\u007f')
                )
            )
            {
                throw new NotSupportedException(
                    "Configuration conflict: Git config contains unsupported control characters."
                );
            }

            string trimmedEnd = line.TrimEnd();
            if (trimmedEnd.EndsWith('\\'))
            {
                throw new NotSupportedException(
                    "Configuration conflict: Git config line continuations are not supported for "
                        + "safe physical mutation."
                );
            }
        }

        private static bool TryParseVariableAssignment(
            string line,
            out string variableName,
            out string value
        )
        {
            variableName = string.Empty;
            value = string.Empty;
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] is '#' or ';' or '[')
            {
                return false;
            }

            int nameEnd = 0;
            while (
                nameEnd < trimmed.Length
                && IsSupportedVariableNameCharacter(trimmed[nameEnd])
            )
            {
                nameEnd++;
            }

            if (
                nameEnd == 0
                || !char.IsAsciiLetter(trimmed[0])
                || (nameEnd < trimmed.Length && !IsVariableNameTerminator(trimmed[nameEnd]))
            )
            {
                return false;
            }

            int index = nameEnd;
            while (index < trimmed.Length && trimmed[index] is ' ' or '\t')
            {
                index++;
            }

            if (index >= trimmed.Length || trimmed[index] != '=')
            {
                return false;
            }

            index++;
            while (index < trimmed.Length && trimmed[index] is ' ' or '\t')
            {
                index++;
            }

            if (!TryParseVariableValue(trimmed[index..], out value))
            {
                return false;
            }

            variableName = trimmed[..nameEnd];
            return true;
        }

        private static bool IsSupportedVariableNameCharacter(char character) =>
            char.IsAsciiLetterOrDigit(character) || character == '-';

        private static bool IsVariableNameTerminator(char character) =>
            character is ' ' or '\t' or '=';

        private static bool TryParseVariableValue(string valueText, out string value)
        {
            value = string.Empty;
            if (valueText.Length == 0)
            {
                return true;
            }

            if (valueText[0] == '"')
            {
                return TryParseQuotedVariableValue(valueText, out value);
            }

            return TryParseSimpleUnquotedVariableValue(valueText, out value);
        }

        private static bool TryParseSimpleUnquotedVariableValue(
            string valueText,
            out string value
        )
        {
            value = string.Empty;
            if (
                valueText.Length != valueText.TrimEnd(' ', '\t').Length
                || valueText.Contains('"', StringComparison.Ordinal)
                || valueText.Contains('\\', StringComparison.Ordinal)
                || valueText.Contains('#', StringComparison.Ordinal)
                || valueText.Contains(';', StringComparison.Ordinal)
            )
            {
                return false;
            }

            value = valueText;
            return true;
        }

        private static bool TryParseQuotedVariableValue(string valueText, out string value)
        {
            value = string.Empty;
            if (
                !TryParseQuotedSubsection(
                    valueText,
                    openingQuoteIndex: 0,
                    out string unescaped,
                    out int closingQuoteIndex
                )
            )
            {
                return false;
            }

            string trailingText = valueText[(closingQuoteIndex + 1)..].TrimStart();
            if (trailingText.Length > 0 && trailingText[0] is not '#' and not ';')
            {
                return false;
            }

            value = unescaped;
            return true;
        }

        private static bool IsNonCommentContent(string line)
        {
            string trimmed = line.TrimStart();
            return trimmed.Length > 0 && trimmed[0] is not '#' and not ';';
        }

        private static bool TryParseQuotedSubsection(
            string text,
            int openingQuoteIndex,
            out string unescaped,
            out int closingQuoteIndex
        )
        {
            var builder = new StringBuilder(text.Length - openingQuoteIndex);
            var escaping = false;
            for (int index = openingQuoteIndex + 1; index < text.Length; index++)
            {
                char character = text[index];
                if (escaping)
                {
                    builder.Append(
                        character switch
                        {
                            '\\' => '\\',
                            '"' => '"',
                            _ => '\0',
                        }
                    );
                    if (builder[^1] == '\0')
                    {
                        unescaped = string.Empty;
                        closingQuoteIndex = -1;
                        return false;
                    }

                    escaping = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (character == '"')
                {
                    unescaped = builder.ToString();
                    closingQuoteIndex = index;
                    return !unescaped.Any(subsectionCharacter =>
                        subsectionCharacter < ' ' || subsectionCharacter == '\u007f'
                    );
                }

                builder.Append(character);
            }

            unescaped = string.Empty;
            closingQuoteIndex = -1;
            return false;
        }

        private static bool KeysEqual(GitConfigKey left, GitConfigKey right) =>
            string.Equals(left.SectionName, right.SectionName, StringComparison.OrdinalIgnoreCase)
            && CredentialSubsectionsEqualForKey(left, right, failOnUnsafeDevAzureAlias: true)
            && string.Equals(
                left.VariableName,
                right.VariableName,
                StringComparison.OrdinalIgnoreCase
            );

        private static bool SectionsEqual(GitConfigKey left, GitConfigKey right) =>
            string.Equals(left.SectionName, right.SectionName, StringComparison.OrdinalIgnoreCase)
            && CredentialSubsectionsEqualForSection(
                left,
                right,
                failOnUnsafeDevAzureAlias: false
            );

        private static bool CredentialSubsectionsEqualForKey(
            GitConfigKey left,
            GitConfigKey right,
            bool failOnUnsafeDevAzureAlias
        )
        {
            if (
                IsCredentialUseHttpPathKey(left)
                && IsCredentialUseHttpPathKey(right)
                && (
                    IsDevAzureComCredentialSubsection(
                        left.Subsection,
                        failOnUnsafeDevAzureAlias
                    )
                    || IsDevAzureComCredentialSubsection(
                        right.Subsection,
                        failOnUnsafeDevAzureAlias
                    )
                )
            )
            {
                return IsDevAzureComCredentialSubsection(
                        left.Subsection,
                        failOnUnsafeDevAzureAlias
                    )
                    && IsDevAzureComCredentialSubsection(
                        right.Subsection,
                        failOnUnsafeDevAzureAlias
                    );
            }

            return string.Equals(left.Subsection, right.Subsection, StringComparison.Ordinal);
        }

        private static bool CredentialSubsectionsEqualForSection(
            GitConfigKey section,
            GitConfigKey target,
            bool failOnUnsafeDevAzureAlias
        )
        {
            if (
                IsCredentialSection(section)
                && IsCredentialUseHttpPathKey(target)
                && IsDevAzureComCredentialSubsection(
                    target.Subsection,
                    failOnUnsafeDevAzureAlias
                )
            )
            {
                return IsDevAzureComCredentialSubsection(
                    section.Subsection,
                    failOnUnsafeDevAzureAlias
                );
            }

            return string.Equals(section.Subsection, target.Subsection, StringComparison.Ordinal);
        }

        private static bool IsCredentialSection(GitConfigKey key) =>
            string.Equals(
                key.SectionName,
                CredentialSectionName,
                StringComparison.OrdinalIgnoreCase
            );

        private static bool IsCredentialUseHttpPathKey(GitConfigKey key) =>
            IsCredentialSection(key)
            && string.Equals(
                key.VariableName,
                UseHttpPathVariableName,
                StringComparison.OrdinalIgnoreCase
            );

        private static bool IsCredentialHelperEntry(GitConfigKey key) =>
            IsCredentialSection(key)
            && string.Equals(
                key.VariableName,
                HelperVariableName,
                StringComparison.OrdinalIgnoreCase
            );

        private static bool IsDevAzureComCredentialSubsection(
            string? subsection,
            bool failOnUnsafeAlias
        )
        {
            DevAzureComCredentialSubsectionMatch match =
                GetDevAzureComCredentialSubsectionMatch(subsection);
            if (match == DevAzureComCredentialSubsectionMatch.UnsafeEffectiveAlias)
            {
                if (failOnUnsafeAlias)
                {
                    throw new NotSupportedException(
                        "Configuration conflict: Git config contains a credential "
                            + "\"https://dev.azure.com\".useHttpPath subsection alias that "
                            + "cannot be canonicalized safely."
                    );
                }

                return false;
            }

            return match == DevAzureComCredentialSubsectionMatch.Canonicalizable;
        }

        private static DevAzureComCredentialSubsectionMatch
            GetDevAzureComCredentialSubsectionMatch(string? subsection)
        {
            if (subsection is null)
            {
                return DevAzureComCredentialSubsectionMatch.NotDevAzureCom;
            }

            if (
                string.Equals(
                    subsection,
                    DevAzureComCredentialSubsection,
                    StringComparison.Ordinal
                )
            )
            {
                return DevAzureComCredentialSubsectionMatch.Canonicalizable;
            }

            if (
                !Uri.TryCreate(subsection, UriKind.Absolute, out Uri? uri)
                || !string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return DevAzureComCredentialSubsectionMatch.NotDevAzureCom;
            }

            DevAzureComCredentialSubsectionMatch hostMatch =
                GetDevAzureComCredentialHostMatch(uri);
            if (hostMatch != DevAzureComCredentialSubsectionMatch.Canonicalizable)
            {
                return hostMatch;
            }

            if (
                !uri.IsDefaultPort
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
            )
            {
                return DevAzureComCredentialSubsectionMatch.UnsafeEffectiveAlias;
            }

            return uri.AbsolutePath is "" or "/"
                ? DevAzureComCredentialSubsectionMatch.Canonicalizable
                : DevAzureComCredentialSubsectionMatch.UnsafeEffectiveAlias;
        }

        private static DevAzureComCredentialSubsectionMatch GetDevAzureComCredentialHostMatch(
            Uri uri
        )
        {
            if (
                IsCanonicalDevAzureComHost(uri.IdnHost)
                && IsCanonicalDevAzureComHost(uri.Host)
            )
            {
                return DevAzureComCredentialSubsectionMatch.Canonicalizable;
            }

            return IsDevAzureComEffectiveHostAlias(uri.IdnHost)
                || IsDevAzureComEffectiveHostAlias(uri.Host)
                ? DevAzureComCredentialSubsectionMatch.UnsafeEffectiveAlias
                : DevAzureComCredentialSubsectionMatch.NotDevAzureCom;
        }

        private static bool IsCanonicalDevAzureComHost(string host) =>
            string.Equals(host, DevAzureComHost, StringComparison.OrdinalIgnoreCase);

        private static bool IsDevAzureComEffectiveHostAlias(string host)
        {
            string trimmedHost = host.TrimEnd('.');
            return trimmedHost.Length != host.Length
                && string.Equals(
                    trimmedHost,
                    DevAzureComHost,
                    StringComparison.OrdinalIgnoreCase
                );
        }
    }

    private enum DevAzureComCredentialSubsectionMatch
    {
        NotDevAzureCom,
        Canonicalizable,
        UnsafeEffectiveAlias,
    }

    private sealed record GitConfigSectionBuilder(
        GitConfigKey Key,
        int HeaderLineIndex
    )
    {
        public GitConfigSectionLocation ToLocation(int endExclusiveLineIndex) =>
            new(Key, HeaderLineIndex, endExclusiveLineIndex);
    }
}
