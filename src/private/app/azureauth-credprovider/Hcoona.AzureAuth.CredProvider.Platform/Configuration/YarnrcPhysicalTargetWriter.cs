using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal sealed class YarnrcPhysicalTargetWriter(IFileSystem fileSystem)
{
    private const string NpmRegistriesKey = "npmRegistries";
    private const string NpmAuthTokenKey = "npmAuthToken";
    private const string NpmAlwaysAuthKey = "npmAlwaysAuth";
    private const string NpmAuthIdentKey = "npmAuthIdent";
    private const string NpmRegistryServerKey = "npmRegistryServer";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );
    private static readonly ConfigurationManager.ConfigurationPathIdentityComparer
        PathIdentityComparer = ConfigurationManager.ConfigurationPathIdentityComparer.Instance;
    private static readonly UnixFileMode OwnerOnlyUnixFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public void Write(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        ValidateRequestShape(request, cancellationToken);
        string targetPath = GetSingleNormalizedTargetPath(request);
        YarnrcDocument document = ReadDocument(targetPath);
        UnixFileMode? previousUnixFileMode = GetCurrentUnixFileMode(document.Path);
        ValidateRetainedOwnershipProofs(request.OwnershipProofs, cancellationToken);
        ApplyChanges(document, request, targetPath);
        string updatedContents = Render(document.Lines, document.NewLine, document.TrailingNewLine);

        if (string.Equals(document.OriginalText, updatedContents, StringComparison.Ordinal))
        {
            if (document.OriginalContentsBytes is not null)
            {
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        targetPath,
                        PreviouslyExisted: true,
                        document.OriginalContentsBytes,
                        ComputeSha256(document.OriginalContentsBytes),
                        RequiresRollback:
                            previousUnixFileMode is not null
                            && previousUnixFileMode.Value != OwnerOnlyUnixFileMode,
                        PreviousUnixFileMode: previousUnixFileMode
                    )
                );
                EnsureYarnrcIsOwnerOnlyUnixFileModeIfNeeded(targetPath, previousUnixFileMode);
            }

            return;
        }

        byte[] updatedContentsBytes = Utf8NoBom.GetBytes(updatedContents);
        var mutation = new ConfigurationPhysicalTargetFileMutation(
            targetPath,
            document.OriginalContentsBytes is not null,
            document.OriginalContentsBytes,
            ComputeSha256(updatedContentsBytes),
            PreviousUnixFileMode: previousUnixFileMode
        );
        try
        {
            fileSystem.AtomicWriteAllText(
                targetPath,
                updatedContents,
                Utf8NoBom,
                AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly,
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
        string targetPath = GetSingleNormalizedTargetPath(request);
        YarnrcDocument document = ReadDocument(targetPath);
        ValidateRetainedOwnershipProofs(request.OwnershipProofs, cancellationToken);
        ApplyChanges(document, request, targetPath);
        _ = Render(document.Lines, document.NewLine, document.TrailingNewLine);
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
                    .Where(proof => proof.TargetKind == ConfigurationTargetKind.Yarnrc)
                    .GroupBy(
                        proof => CreatePhysicalPathIdentity(proof.TargetPathOrName),
                        PathIdentityComparer
                    )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureTargetPathCanBeSafelyMutated(proofsByTarget.Key);

            YarnrcDocument document = ReadDocument(proofsByTarget.Key);
            foreach (
                IGrouping<string, ConfigurationPhysicalTargetOwnershipProof> proofsByKey in
                    proofsByTarget.GroupBy(proof => proof.Key, StringComparer.Ordinal)
            )
            {
                if (proofsByKey.Count() > 1)
                {
                    throw new InvalidOperationException(
                        "Configuration conflict: Yarnrc retained ownership proofs must be unique "
                            + "per canonical physical key."
                    );
                }

                ValidateProofAgainstCurrentState(document, proofsByKey.Single());
            }
        }
    }

    internal static string? GetPlanningValidationViolation(
        ConfigurationChange change,
        CanonicalResourceIdentity? resourceIdentity = null
    )
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.TargetKind != ConfigurationTargetKind.Yarnrc)
        {
            return null;
        }

        if (
            change.Operation
            is ConfigurationChangeOperation.EnsureFile
                or ConfigurationChangeOperation.InstallAdapter
                or ConfigurationChangeOperation.RemoveAdapter
        )
        {
            return "The Yarnrc physical writer supports only value-writing and remove changes.";
        }

        bool isTopLevelRegistryServer = string.Equals(
            change.Key,
            NpmRegistryServerKey,
            StringComparison.Ordinal
        );
        string registryKey = string.Empty;
        string leafKey = NpmRegistryServerKey;
        if (
            !isTopLevelRegistryServer
            && !TryParseNpmRegistriesAuthKey(
                change.Key,
                out registryKey,
                out leafKey
            )
        )
        {
            return "The Yarnrc physical writer supports only npmRegistries auth token keys.";
        }

        if (string.Equals(leafKey, NpmAuthIdentKey, StringComparison.Ordinal))
        {
            return "The Yarnrc physical writer does not support npmAuthIdent entries.";
        }

        if (RequiresValue(change.Operation))
        {
            if (change.Value is null)
            {
                return "The Yarnrc physical writer requires a value for value-writing changes.";
            }

            if (ContainsLineBreak(change.Value) || ContainsControlCharacter(change.Value))
            {
                return "The Yarnrc physical writer supports values without control characters.";
            }

            if (string.Equals(leafKey, NpmAuthTokenKey, StringComparison.Ordinal))
            {
                if (!change.IsSecretValue)
                {
                    return "The Yarnrc physical writer requires npmAuthToken values to be secret.";
                }

                return GetYarnSecretAuthTokenSelectorViolation(resourceIdentity, registryKey);
            }

            if (change.IsSecretValue)
            {
                return "The Yarnrc physical writer supports secret values only for npmAuthToken.";
            }

            if (isTopLevelRegistryServer)
            {
                return resourceIdentity is not null
                    && string.Equals(
                        change.Value,
                        resourceIdentity.ServiceEndpoint.AbsoluteUri,
                        StringComparison.Ordinal
                    )
                    ? null
                    : "The Yarnrc npmRegistryServer value must match the canonical registry identity.";
            }

            if (!string.Equals(change.Value, "true", StringComparison.Ordinal))
            {
                return "The Yarnrc physical writer currently writes npmAlwaysAuth=true only.";
            }
        }
        else if (change.Operation == ConfigurationChangeOperation.Remove)
        {
            if (change.Value is not null)
            {
                return "The Yarnrc physical writer supports remove changes without a value.";
            }
        }

        return null;
    }

    private void ValidateRequestShape(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.TargetKind != ConfigurationTargetKind.Yarnrc)
        {
            throw new NotSupportedException(
                "The Yarnrc physical writer supports only Yarnrc targets."
            );
        }

        if (request.PlanOperation is not ConfigurationPlanOperation.DryRun
            and not ConfigurationPlanOperation.Apply
            and not ConfigurationPlanOperation.Remove)
        {
            throw new NotSupportedException(
                "The Yarnrc physical writer supports dry-run/apply/remove operations only."
            );
        }

        if (request.Changes.Count == 0)
        {
            throw new NotSupportedException(
                "The Yarnrc physical writer requires at least one change."
            );
        }

        if (request.Changes.Any(change => change.TargetKind != ConfigurationTargetKind.Yarnrc))
        {
            throw new NotSupportedException(
                "The Yarnrc physical writer requires all request changes to target Yarnrc."
            );
        }

        if (!AreChangesTargetingTheSamePath(request.Changes))
        {
            throw new NotSupportedException(
                "The Yarnrc physical writer supports only one normalized physical file path per "
                    + "request."
            );
        }

        if (
            request.Changes.GroupBy(change => change.Key, StringComparer.Ordinal)
                .Any(group => group.Count() > 1)
        )
        {
            throw new NotSupportedException(
                "The Yarnrc physical writer supports only one change per canonical key."
            );
        }

        if (
            request.PlanOperation == ConfigurationPlanOperation.Apply
            && request.Changes.Any(change => !RequiresValue(change.Operation))
        )
        {
            throw new NotSupportedException(
                "The Yarnrc physical writer supports value-writing changes only for apply."
            );
        }

        if (
            request.PlanOperation == ConfigurationPlanOperation.Remove
            && request.Changes.Any(change =>
                change.Operation != ConfigurationChangeOperation.Remove
            )
        )
        {
            throw new NotSupportedException(
                "The Yarnrc physical writer supports ownership-removing changes only for remove."
            );
        }

        foreach (ConfigurationChange change in request.Changes)
        {
            string? violation = GetPlanningValidationViolation(change, request.ResourceIdentity);
            if (violation is not null)
            {
                throw new NotSupportedException(violation);
            }
        }
    }

    private void ApplyChanges(
        YarnrcDocument document,
        ConfigurationPhysicalTargetWriterRequest request,
        string targetPath
    )
    {
        foreach (ConfigurationChange change in OrderChanges(request.Changes))
        {
            if (
                string.Equals(
                    change.Key,
                    NpmRegistryServerKey,
                    StringComparison.Ordinal
                )
            )
            {
                ApplyTopLevelRegistryServerChange(document, request, targetPath, change);
                continue;
            }

            if (
                !TryParseNpmRegistriesAuthKey(
                    change.Key,
                    out string registryKey,
                    out string leafKey
                )
            )
            {
                throw new NotSupportedException(
                    "The Yarnrc physical writer supports only npmRegistries auth token keys."
                );
            }

            ConfigurationPhysicalTargetOwnershipProof? proof = FindOwnershipProof(
                request.OwnershipProofs,
                targetPath,
                change.Key
            );
            if (change.Operation != ConfigurationChangeOperation.Remove)
            {
                EnsureNoNpmAuthIdentConflict(document, registryKey);
            }

            YarnrcEntry[] existingEntries = FindEntries(document, registryKey, leafKey).ToArray();
            ValidateCurrentState(change, existingEntries, proof);

            if (change.Operation == ConfigurationChangeOperation.Remove)
            {
                document.Lines.RemoveAt(existingEntries[0].LineIndex);
                RemoveEmptyRegistryBlock(document, registryKey);
                RemoveEmptyNpmRegistriesBlock(document);
                continue;
            }

            if (
                existingEntries.Length == 1
                && string.Equals(existingEntries[0].Value, change.Value, StringComparison.Ordinal)
            )
            {
                continue;
            }

            string renderedLine = RenderLeafLine(leafKey, change.Value!);
            if (existingEntries.Length == 1)
            {
                document.Lines[existingEntries[0].LineIndex] = renderedLine;
                continue;
            }

            int insertIndex = EnsureRegistryBlock(document, registryKey);
            document.Lines.Insert(insertIndex, renderedLine);
            document.TrailingNewLine = true;
        }
    }

    private void ApplyTopLevelRegistryServerChange(
        YarnrcDocument document,
        ConfigurationPhysicalTargetWriterRequest request,
        string targetPath,
        ConfigurationChange change
    )
    {
        ConfigurationPhysicalTargetOwnershipProof? proof = FindOwnershipProof(
            request.OwnershipProofs,
            targetPath,
            change.Key
        );
        YarnrcEntry[] existingEntries = FindTopLevelEntries(
                document,
                NpmRegistryServerKey
            )
            .ToArray();
        ValidateCurrentState(change, existingEntries, proof);

        if (change.Operation == ConfigurationChangeOperation.Remove)
        {
            document.Lines.RemoveAt(existingEntries[0].LineIndex);
            return;
        }

        string renderedLine =
            NpmRegistryServerKey
            + ": '"
            + EscapeSingleQuotedYamlScalar(change.Value!)
            + "'";
        if (existingEntries.Length == 1)
        {
            if (!string.Equals(existingEntries[0].Value, change.Value, StringComparison.Ordinal))
            {
                document.Lines[existingEntries[0].LineIndex] = renderedLine;
            }

            return;
        }

        document.Lines.Insert(0, renderedLine);
        document.TrailingNewLine = true;
    }

    private static void EnsureNoNpmAuthIdentConflict(
        YarnrcDocument document,
        string registryKey
    )
    {
        if (
            FindNonEmptyTopLevelNpmAuthIdentEntries(document).Any()
            || FindApplicableNonEmptyScopedNpmAuthIdentEntries(document, registryKey).Any()
            || FindNonEmptyNpmAuthIdentEntries(document, registryKey).Any()
        )
        {
            throw new NotSupportedException(
                "Configuration conflict: Yarnrc npmAuthIdent entries conflict with "
                    + "product-owned npmAuthToken plans."
            );
        }
    }

    private static ConfigurationChange[] OrderChanges(
        IReadOnlyList<ConfigurationChange> changes
    ) =>
        changes.OrderBy(change =>
            string.Equals(change.Key, NpmRegistryServerKey, StringComparison.Ordinal)
                ? 0
                : TryParseNpmRegistriesAuthKey(change.Key, out _, out string leafKey)
                    && string.Equals(leafKey, NpmAlwaysAuthKey, StringComparison.Ordinal)
                    ? 1
                    : 2
        ).ToArray();

    private static void ValidateCurrentState(
        ConfigurationChange change,
        YarnrcEntry[] existingEntries,
        ConfigurationPhysicalTargetOwnershipProof? proof
    )
    {
        if (existingEntries.Length > 1)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Yarnrc key has multiple existing declarations and cannot "
                    + "be updated safely."
            );
        }

        if (existingEntries.Length == 0)
        {
            if (change.Operation == ConfigurationChangeOperation.Remove)
            {
                throw new InvalidOperationException(
                    "Configuration conflict: Yarnrc key is missing from the physical "
                        + "configuration file."
                );
            }

            if (
                change.Operation is ConfigurationChangeOperation.Update
                    or ConfigurationChangeOperation.Refresh
                    || proof is not null
            )
            {
                throw new InvalidOperationException(
                    "Configuration conflict: owned Yarnrc key is missing from the physical "
                        + "configuration file."
                );
            }

            return;
        }

        if (change.Operation == ConfigurationChangeOperation.Create)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Yarnrc create target already exists."
            );
        }

        if (proof is null)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Yarnrc key already exists and is not proven to be owned "
                    + "by the existing manifest."
            );
        }

        if (change.Operation != ConfigurationChangeOperation.Remove)
        {
            ValidateProofAgainstEntry(existingEntries[0], proof);
        }
    }

    private static void ValidateProofAgainstCurrentState(
        YarnrcDocument document,
        ConfigurationPhysicalTargetOwnershipProof proof
    )
    {
        YarnrcEntry[] existingEntries;
        if (string.Equals(proof.Key, NpmRegistryServerKey, StringComparison.Ordinal))
        {
            existingEntries = FindTopLevelEntries(document, NpmRegistryServerKey).ToArray();
        }
        else if (
            TryParseNpmRegistriesAuthKey(
                proof.Key,
                out string registryKey,
                out string leafKey
            )
        )
        {
            existingEntries = FindEntries(document, registryKey, leafKey).ToArray();
        }
        else
        {
            throw new InvalidOperationException(
                "Configuration conflict: Yarnrc retained ownership proofs require canonical keys."
            );
        }

        if (existingEntries.Length == 0)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Yarnrc retained ownership proof does not match any "
                    + "existing file."
            );
        }

        if (existingEntries.Length > 1)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Yarnrc retained ownership proofs must be unique per "
                    + "canonical physical key."
            );
        }

        ValidateProofAgainstEntry(existingEntries[0], proof);
    }

    private static void ValidateProofAgainstEntry(
        YarnrcEntry existingEntry,
        ConfigurationPhysicalTargetOwnershipProof proof
    )
    {
        bool isSecretAuthToken = proof.Key.EndsWith(".npmAuthToken", StringComparison.Ordinal);
        if (isSecretAuthToken)
        {
            if (proof.PlannedValueSha256 is not null)
            {
                throw new InvalidOperationException(
                    "Configuration conflict: Yarnrc secret retained ownership proofs must not "
                        + "include planned value hashes."
                );
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(proof.PlannedValueSha256))
        {
            throw new InvalidOperationException(
                "Configuration conflict: Yarnrc retained ownership proof is missing a planned "
                    + "value hash."
            );
        }

        string currentHash = ComputeSha256(existingEntry.Value);
        if (!string.Equals(currentHash, proof.PlannedValueSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Configuration conflict: Yarnrc retained ownership proof does not match the "
                    + "current file contents."
            );
        }
    }

    private static string? GetYarnSecretAuthTokenSelectorViolation(
        CanonicalResourceIdentity? resourceIdentity,
        string registryKey
    )
    {
        if (resourceIdentity is null)
        {
            return "The Yarnrc physical writer requires secret auth token keys to be derived "
                + "from a canonical registry identity.";
        }

        string? resourceViolation = CanonicalResourceIdentityPolicy.GetViolation(resourceIdentity);
        if (resourceViolation is not null)
        {
            return resourceViolation;
        }

        if (
            !CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                resourceIdentity.ServiceEndpoint,
                CredentialEcosystem.Yarn
            )
        )
        {
            return "The Yarnrc physical writer requires secret auth token keys to be derived "
                + "from a canonical registry identity.";
        }

        return string.Equals(
            registryKey,
            resourceIdentity.ServiceEndpoint.AbsoluteUri,
            StringComparison.Ordinal
        )
            ? null
            : "The Yarnrc physical writer requires secret auth token keys to match the canonical "
                + "registry identity.";
    }

    private YarnrcDocument ReadDocument(string targetPath)
    {
        EnsureTargetPathCanBeSafelyMutated(targetPath);

        if (!fileSystem.FileExists(targetPath))
        {
            if (fileSystem.DirectoryExists(targetPath))
            {
                throw new InvalidOperationException(
                    "Configuration conflict: Yarnrc target exists as a directory."
                );
            }

            return new YarnrcDocument(
                targetPath,
                string.Empty,
                null,
                FileMutationExpectation.Missing,
                Environment.NewLine,
                trailingNewLine: false,
                []
            );
        }

        if (fileSystem.DirectoryExists(targetPath))
        {
            throw new InvalidOperationException(
                "Configuration conflict: Yarnrc target exists as a directory."
            );
        }

        if (IsUnsupportedLinkOrReparsePoint(targetPath))
        {
            throw new NotSupportedException(
                "Configuration conflict: Yarnrc target path is a symbolic-link or reparse-point "
                    + "and is not supported."
            );
        }

        byte[] contents = fileSystem.ReadAllBytes(targetPath);
        if (StartsWithUtf8Bom(contents))
        {
            throw new NotSupportedException(
                "Configuration conflict: Yarnrc files with a UTF-8 BOM are not supported for safe "
                    + "physical mutation."
            );
        }

        string text = Utf8NoBom.GetString(contents);
        string newLine = DetectNewLine(text);
        bool trailingNewLine = text.EndsWith('\n');
        var document = new YarnrcDocument(
            targetPath,
            text,
            contents,
            FileMutationExpectation.Existing(ComputeSha256(contents)),
            newLine,
            trailingNewLine,
            SplitLines(text)
        );
        ValidateSupportedNpmRegistriesShape(document);
        return document;
    }

    private static int EnsureRegistryBlock(YarnrcDocument document, string registryKey)
    {
        YarnrcRegistryBlock? block = FindRegistryBlock(document, registryKey);
        if (block is not null)
        {
            return block.Value.EndIndex;
        }

        int npmRegistriesHeaderIndex = FindNpmRegistriesHeaderIndex(document);
        if (npmRegistriesHeaderIndex < 0)
        {
            document.Lines.Add(NpmRegistriesKey + ":");
            document.Lines.Add(RenderRegistryLine(registryKey));
            return document.Lines.Count;
        }

        int insertIndex = FindNpmRegistriesBlockEndIndex(document, npmRegistriesHeaderIndex);
        document.Lines.Insert(insertIndex, RenderRegistryLine(registryKey));
        return insertIndex + 1;
    }

    private static void RemoveEmptyRegistryBlock(YarnrcDocument document, string registryKey)
    {
        YarnrcRegistryBlock? block = FindRegistryBlock(document, registryKey);
        if (block is null || block.Value.EndIndex > block.Value.StartIndex + 1)
        {
            return;
        }

        document.Lines.RemoveAt(block.Value.StartIndex);
    }

    private static void RemoveEmptyNpmRegistriesBlock(YarnrcDocument document)
    {
        int headerIndex = FindNpmRegistriesHeaderIndex(document);
        if (headerIndex < 0)
        {
            return;
        }

        int blockEndIndex = FindNpmRegistriesBlockEndIndex(document, headerIndex);
        if (blockEndIndex == headerIndex + 1)
        {
            document.Lines.RemoveAt(headerIndex);
        }
    }

    private static void ValidateSupportedNpmRegistriesShape(YarnrcDocument document)
    {
        int headerIndex = -1;
        for (int index = 0; index < document.Lines.Count; index++)
        {
            string line = StripYamlComment(document.Lines[index]);
            if (string.IsNullOrWhiteSpace(line) || CountLeadingSpaces(line) != 0)
            {
                continue;
            }

            string trimmed = line.Trim();
            if (
                TryParseYamlKeyValue(trimmed, out string? keyValueKey, out _)
                && string.Equals(keyValueKey, NpmRegistriesKey, StringComparison.Ordinal)
            )
            {
                throw new NotSupportedException(
                    "Configuration conflict: Yarnrc npmRegistries inline mappings are not "
                        + "supported for safe physical mutation."
                );
            }

            if (
                TryParseYamlMapKey(trimmed, out string? mapKey)
                && string.Equals(mapKey, NpmRegistriesKey, StringComparison.Ordinal)
            )
            {
                if (headerIndex >= 0)
                {
                    throw new NotSupportedException(
                        "Configuration conflict: Yarnrc files with multiple npmRegistries "
                            + "blocks are not supported for safe physical mutation."
                    );
                }

                headerIndex = index;
            }
        }

        if (headerIndex < 0)
        {
            return;
        }

        var registryKeys = new HashSet<string>(StringComparer.Ordinal);
        string? currentRegistryKey = null;
        int blockEndIndex = FindNpmRegistriesBlockEndIndex(document, headerIndex);
        for (int index = headerIndex + 1; index < blockEndIndex; index++)
        {
            string line = StripYamlComment(document.Lines[index]);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int indent = CountLeadingSpaces(line);
            string trimmed = line.Trim();
            if (indent == 2)
            {
                if (!TryParseYamlMapKey(trimmed, out string? registryKey))
                {
                    throw new NotSupportedException(
                        "Configuration conflict: Yarnrc npmRegistries entries must use "
                            + "two-space registry map keys for safe physical mutation."
                    );
                }

                if (registryKey is null || !registryKeys.Add(registryKey))
                {
                    throw new InvalidOperationException(
                        "Configuration conflict: Yarnrc npmRegistries registry keys must be "
                            + "unique for safe physical mutation."
                    );
                }

                currentRegistryKey = registryKey;
                continue;
            }

            if (
                indent == 4
                && currentRegistryKey is not null
                && TryParseYamlKeyValue(trimmed, out _, out _)
            )
            {
                continue;
            }

            throw new NotSupportedException(
                "Configuration conflict: Yarnrc npmRegistries indentation or nested mappings are "
                    + "not supported for safe physical mutation."
            );
        }
    }

    private static YarnrcRegistryBlock? FindRegistryBlock(
        YarnrcDocument document,
        string registryKey
    )
    {
        int headerIndex = FindNpmRegistriesHeaderIndex(document);
        if (headerIndex < 0)
        {
            return null;
        }

        int blockEndIndex = FindNpmRegistriesBlockEndIndex(document, headerIndex);
        for (int index = headerIndex + 1; index < blockEndIndex; index++)
        {
            string line = StripYamlComment(document.Lines[index]);
            if (string.IsNullOrWhiteSpace(line) || CountLeadingSpaces(line) != 2)
            {
                continue;
            }

            string trimmed = line.Trim();
            if (
                TryParseYamlMapKey(trimmed, out string? candidateKey)
                && string.Equals(candidateKey, registryKey, StringComparison.Ordinal)
            )
            {
                int registryEndIndex = index + 1;
                while (registryEndIndex < blockEndIndex)
                {
                    string childLine = StripYamlComment(document.Lines[registryEndIndex]);
                    if (
                        !string.IsNullOrWhiteSpace(childLine)
                        && CountLeadingSpaces(childLine) <= 2
                    )
                    {
                        break;
                    }

                    registryEndIndex++;
                }

                return new YarnrcRegistryBlock(index, registryEndIndex);
            }
        }

        return null;
    }

    private static IEnumerable<YarnrcEntry> FindEntries(
        YarnrcDocument document,
        string registryKey,
        string leafKey
    )
    {
        YarnrcRegistryBlock? block = FindRegistryBlock(document, registryKey);
        if (block is null)
        {
            yield break;
        }

        for (int index = block.Value.StartIndex + 1; index < block.Value.EndIndex; index++)
        {
            string line = StripYamlComment(document.Lines[index]);
            if (string.IsNullOrWhiteSpace(line) || CountLeadingSpaces(line) != 4)
            {
                continue;
            }

            string trimmed = line.Trim();
            if (
                TryParseYamlKeyValue(trimmed, out string? key, out string? value)
                && string.Equals(key, leafKey, StringComparison.Ordinal)
                && UnquoteYamlScalar(value) is { } parsedValue
            )
            {
                yield return new YarnrcEntry(index, registryKey, leafKey, parsedValue);
            }
        }
    }

    private static IEnumerable<YarnrcEntry> FindTopLevelEntries(
        YarnrcDocument document,
        string requestedKey
    )
    {
        for (int index = 0; index < document.Lines.Count; index++)
        {
            string line = StripYamlComment(document.Lines[index]);
            if (string.IsNullOrWhiteSpace(line) || CountLeadingSpaces(line) != 0)
            {
                continue;
            }

            if (
                TryParseYamlKeyValue(line.Trim(), out string? key, out string? value)
                && string.Equals(key, requestedKey, StringComparison.Ordinal)
                && UnquoteYamlScalar(value) is { } parsedValue
            )
            {
                yield return new YarnrcEntry(index, "<global>", requestedKey, parsedValue);
            }
        }
    }

    private static IEnumerable<YarnrcEntry> FindNonEmptyTopLevelNpmAuthIdentEntries(
        YarnrcDocument document
    )
    {
        for (int index = 0; index < document.Lines.Count; index++)
        {
            string line = StripYamlComment(document.Lines[index]);
            if (string.IsNullOrWhiteSpace(line) || CountLeadingSpaces(line) != 0)
            {
                continue;
            }

            string trimmed = line.Trim();
            if (
                TryParseYamlKeyValue(trimmed, out string? key, out string? value)
                && string.Equals(key, NpmAuthIdentKey, StringComparison.Ordinal)
                && UnquoteYamlScalar(value) is { } parsedValue
                && !string.IsNullOrWhiteSpace(parsedValue)
            )
            {
                yield return new YarnrcEntry(
                    index,
                    "<global>",
                    NpmAuthIdentKey,
                    parsedValue
                );
            }
        }
    }

    private static IEnumerable<YarnrcEntry> FindApplicableNonEmptyScopedNpmAuthIdentEntries(
        YarnrcDocument document,
        string registryKey
    )
    {
        int headerIndex = FindTopLevelMapHeaderIndex(document, "npmScopes");
        if (headerIndex < 0)
        {
            yield break;
        }

        string comparableRegistryKey = NormalizeComparableRegistryKey(registryKey);
        int blockEndIndex = FindTopLevelBlockEndIndex(document, headerIndex);
        for (int index = headerIndex + 1; index < blockEndIndex; index++)
        {
            string line = StripYamlComment(document.Lines[index]);
            if (string.IsNullOrWhiteSpace(line) || CountLeadingSpaces(line) != 2)
            {
                continue;
            }

            string trimmed = line.Trim();
            if (!TryParseYamlMapKey(trimmed, out string? scopeName))
            {
                continue;
            }

            int scopeEndIndex = index + 1;
            while (scopeEndIndex < blockEndIndex)
            {
                string childLine = StripYamlComment(document.Lines[scopeEndIndex]);
                if (
                    !string.IsNullOrWhiteSpace(childLine)
                    && CountLeadingSpaces(childLine) <= 2
                )
                {
                    break;
                }

                scopeEndIndex++;
            }

            YarnrcEntry? authIdentEntry = null;
            string? scopeRegistryServer = null;
            for (int childIndex = index + 1; childIndex < scopeEndIndex; childIndex++)
            {
                string childLine = StripYamlComment(document.Lines[childIndex]);
                if (string.IsNullOrWhiteSpace(childLine) || CountLeadingSpaces(childLine) != 4)
                {
                    continue;
                }

                string childTrimmed = childLine.Trim();
                if (!TryParseYamlKeyValue(childTrimmed, out string? key, out string? value))
                {
                    continue;
                }

                if (string.Equals(key, "npmRegistryServer", StringComparison.Ordinal))
                {
                    scopeRegistryServer = UnquoteYamlScalar(value);
                    continue;
                }

                if (
                    string.Equals(key, NpmAuthIdentKey, StringComparison.Ordinal)
                    && UnquoteYamlScalar(value) is { } parsedValue
                    && !string.IsNullOrWhiteSpace(parsedValue)
                )
                {
                    authIdentEntry = new YarnrcEntry(
                        childIndex,
                        "npmScopes." + scopeName,
                        NpmAuthIdentKey,
                        parsedValue
                    );
                }
            }

            if (
                authIdentEntry is { } entry
                && (
                    string.IsNullOrWhiteSpace(scopeRegistryServer)
                    || string.Equals(
                        NormalizeComparableRegistryKey(scopeRegistryServer),
                        comparableRegistryKey,
                        StringComparison.Ordinal
                    )
                )
            )
            {
                yield return entry;
            }

            index = scopeEndIndex - 1;
        }
    }

    private static IEnumerable<YarnrcEntry> FindNonEmptyNpmAuthIdentEntries(
        YarnrcDocument document,
        string registryKey
    )
    {
        YarnrcRegistryBlock? block = FindRegistryBlock(document, registryKey);
        if (block is null)
        {
            yield break;
        }

        for (int index = block.Value.StartIndex + 1; index < block.Value.EndIndex; index++)
        {
            string line = StripYamlComment(document.Lines[index]);
            if (string.IsNullOrWhiteSpace(line) || CountLeadingSpaces(line) != 4)
            {
                continue;
            }

            string trimmed = line.Trim();
            if (
                TryParseYamlKeyValue(trimmed, out string? key, out string? value)
                && string.Equals(key, NpmAuthIdentKey, StringComparison.Ordinal)
                && UnquoteYamlScalar(value) is { } parsedValue
                && !string.IsNullOrWhiteSpace(parsedValue)
            )
            {
                yield return new YarnrcEntry(index, registryKey, NpmAuthIdentKey, parsedValue);
            }
        }
    }

    private static int FindNpmRegistriesHeaderIndex(YarnrcDocument document)
    {
        return FindTopLevelMapHeaderIndex(document, NpmRegistriesKey);
    }

    private static int FindTopLevelMapHeaderIndex(YarnrcDocument document, string headerKey)
    {
        for (int index = 0; index < document.Lines.Count; index++)
        {
            string line = StripYamlComment(document.Lines[index]);
            if (
                CountLeadingSpaces(line) == 0
                && TryParseYamlMapKey(line.Trim(), out string? key)
                && string.Equals(key, headerKey, StringComparison.Ordinal)
            )
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindNpmRegistriesBlockEndIndex(
        YarnrcDocument document,
        int headerIndex
    ) => FindTopLevelBlockEndIndex(document, headerIndex);

    private static int FindTopLevelBlockEndIndex(
        YarnrcDocument document,
        int headerIndex
    )
    {
        int index = headerIndex + 1;
        while (index < document.Lines.Count)
        {
            string line = StripYamlComment(document.Lines[index]);
            if (!string.IsNullOrWhiteSpace(line) && CountLeadingSpaces(line) == 0)
            {
                break;
            }

            index++;
        }

        return index;
    }

    private void EnsureTargetPathCanBeSafelyMutated(string targetPath)
    {
        if (IsUnsupportedLinkOrReparsePoint(targetPath))
        {
            throw new NotSupportedException(
                "Configuration conflict: Yarnrc target path is a symbolic-link or reparse-point "
                    + "and is not supported."
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
                        "Configuration conflict: Yarnrc target parent path contains a "
                            + "symbolic-link or reparse-point directory."
                    );
                }

                if (!fileSystem.DirectoryExists(directory) && fileSystem.FileExists(directory))
                {
                    throw new NotSupportedException(
                        "Configuration conflict: Yarnrc target parent path contains a "
                            + "non-directory entry."
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

    private bool AreChangesTargetingTheSamePath(
        IReadOnlyList<ConfigurationChange> changes
    )
    {
        string firstPath = CreatePhysicalPathIdentity(changes[0].TargetPathOrName);
        return changes.Skip(1).All(change =>
            PathIdentityComparer.Equals(
                CreatePhysicalPathIdentity(change.TargetPathOrName),
                firstPath
            )
        );
    }

    private string CreatePhysicalPathIdentity(string targetPathOrName) =>
        Path.TrimEndingDirectorySeparator(fileSystem.GetFullPath(targetPathOrName));

    private string GetSingleNormalizedTargetPath(
        ConfigurationPhysicalTargetWriterRequest request
    )
    {
        string targetPath = CreatePhysicalPathIdentity(request.Changes[0].TargetPathOrName);
        if (
            request.Changes.Skip(1).Any(change =>
                !PathIdentityComparer.Equals(
                    CreatePhysicalPathIdentity(change.TargetPathOrName),
                    targetPath
                )
            )
        )
        {
            throw new NotSupportedException(
                "The Yarnrc physical writer supports only a single normalized physical file path "
                    + "per request."
            );
        }

        return targetPath;
    }

    private ConfigurationPhysicalTargetOwnershipProof? FindOwnershipProof(
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        string targetPath,
        string key
    ) =>
        ownershipProofs.FirstOrDefault(proof =>
            proof.TargetKind == ConfigurationTargetKind.Yarnrc
            && PathIdentityComparer.Equals(
                CreatePhysicalPathIdentity(proof.TargetPathOrName),
                targetPath
            )
            && string.Equals(proof.Key, key, StringComparison.Ordinal)
        );

    private static string RenderRegistryLine(string registryKey) =>
        "  '" + EscapeSingleQuotedYamlScalar(registryKey) + "':";

    private static string RenderLeafLine(string leafKey, string value) =>
        string.Equals(leafKey, NpmAlwaysAuthKey, StringComparison.Ordinal)
            ? "    " + leafKey + ": " + value
            : "    " + leafKey + ": '" + EscapeSingleQuotedYamlScalar(value) + "'";

    private static string EscapeSingleQuotedYamlScalar(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static string NormalizeComparableRegistryKey(string registryKey)
    {
        if (
            Uri.TryCreate(registryKey, UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        )
        {
            return uri.AbsoluteUri;
        }

        if (registryKey.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + registryKey;
        }

        return registryKey;
    }

    private static string? UnquoteYamlScalar(string? value)
    {
        string? trimmed = NullIfWhiteSpace(value);
        if (trimmed is null || IsYamlNull(trimmed))
        {
            return null;
        }

        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        }

        return trimmed;
    }

    private static bool TryParseYamlKeyValue(
        string text,
        out string? key,
        out string? value
    )
    {
        key = null;
        value = null;
        int colonIndex = FindUnquotedColon(text);
        if (colonIndex <= 0 || colonIndex == text.Length - 1)
        {
            return false;
        }

        key = UnquoteYamlScalar(text[..colonIndex].Trim());
        value = text[(colonIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(key);
    }

    private static bool TryParseYamlMapKey(string text, out string? key)
    {
        key = null;
        int colonIndex = FindUnquotedColon(text);
        if (colonIndex <= 0 || colonIndex != text.Length - 1)
        {
            return false;
        }

        key = UnquoteYamlScalar(text[..colonIndex].Trim());
        return !string.IsNullOrWhiteSpace(key);
    }

    private static int FindUnquotedColon(string text)
    {
        char? quote = null;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (quote is not null)
            {
                if (current == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                continue;
            }

            if (current == ':')
            {
                return index;
            }
        }

        return -1;
    }

    private static string StripYamlComment(string text)
    {
        char? quote = null;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (quote is not null)
            {
                if (current == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                continue;
            }

            if (current == '#')
            {
                return text[..index];
            }
        }

        return text;
    }

    private static int CountLeadingSpaces(string value)
    {
        int index = 0;
        while (index < value.Length && value[index] == ' ')
        {
            index++;
        }

        return index;
    }

    private static List<string> SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var lines = new List<string>();
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] is '\r')
            {
                lines.Add(text[start..index]);
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                start = index + 1;
            }
            else if (text[index] is '\n')
            {
                lines.Add(text[start..index]);
                start = index + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }

    private static string DetectNewLine(string text)
    {
        bool hasCrLf = false;
        bool hasLf = false;
        bool hasBareCr = false;

        for (int index = 0; index < text.Length; index++)
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
                "Configuration conflict: mixed or bare-CR Yarnrc newline styles are not supported "
                    + "for safe physical mutation."
            );
        }

        return hasCrLf ? "\r\n" : "\n";
    }

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

    private UnixFileMode? GetCurrentUnixFileMode(string targetPath)
    {
        if (OperatingSystem.IsWindows() || !fileSystem.FileExists(targetPath))
        {
            return null;
        }

        return fileSystem.GetUnixFileMode(targetPath);
    }

    private void EnsureYarnrcIsOwnerOnlyUnixFileModeIfNeeded(
        string targetPath,
        UnixFileMode? previousUnixFileMode
    )
    {
        if (previousUnixFileMode is null || previousUnixFileMode == OwnerOnlyUnixFileMode)
        {
            return;
        }

        fileSystem.SetUnixFileMode(targetPath, OwnerOnlyUnixFileMode);
    }

    private static bool TryParseNpmRegistriesAuthKey(
        string key,
        out string registryKey,
        out string leafKey
    )
    {
        registryKey = string.Empty;
        leafKey = string.Empty;
        if (ContainsLineBreak(key) || ContainsControlCharacter(key))
        {
            return false;
        }

        const string prefix = NpmRegistriesKey + ".";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        int leafSeparatorIndex = key.LastIndexOf('.');
        if (leafSeparatorIndex <= prefix.Length || leafSeparatorIndex == key.Length - 1)
        {
            return false;
        }

        registryKey = key[prefix.Length..leafSeparatorIndex];
        leafKey = key[(leafSeparatorIndex + 1)..];
        if (
            leafKey is not (NpmAuthTokenKey or NpmAlwaysAuthKey or NpmAuthIdentKey)
            || !Uri.TryCreate(registryKey, UriKind.Absolute, out Uri? registryUri)
        )
        {
            return false;
        }

        return CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
            registryUri,
            CredentialEcosystem.Yarn
        );
    }

    private static bool RequiresValue(ConfigurationChangeOperation operation) =>
        operation
            is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh;

    private static bool ContainsLineBreak(string value) =>
        value.Contains('\r', StringComparison.Ordinal)
        || value.Contains('\n', StringComparison.Ordinal);

    private static bool ContainsControlCharacter(string value) =>
        value.Any(character => character < ' ' || character == '\u007f');

    private static bool StartsWithUtf8Bom(byte[] bytes) =>
        bytes is [0xEF, 0xBB, 0xBF, ..];

    private static bool IsYamlNull(string value) =>
        string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "~", StringComparison.Ordinal);

    private static string ComputeSha256(string value) =>
        ComputeSha256(Utf8NoBom.GetBytes(value));

    private static string ComputeSha256(byte[] value)
    {
        byte[] hash = SHA256.HashData(value);
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class YarnrcDocument(
    string path,
    string originalText,
    byte[]? originalContentsBytes,
    FileMutationExpectation mutationExpectation,
    string newLine,
    bool trailingNewLine,
    List<string> lines
)
{
    public string Path { get; } = path;

    public string OriginalText { get; } = originalText;

    public byte[]? OriginalContentsBytes { get; } = originalContentsBytes;

    public FileMutationExpectation MutationExpectation { get; } = mutationExpectation;

    public string NewLine { get; } = newLine;

    public bool TrailingNewLine { get; set; } = trailingNewLine;

    public List<string> Lines { get; } = lines;
}

internal readonly record struct YarnrcRegistryBlock(int StartIndex, int EndIndex);

internal sealed record YarnrcEntry(
    int LineIndex,
    string RegistryKey,
    string Key,
    string Value
);
