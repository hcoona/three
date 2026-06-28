using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal sealed class NpmrcPhysicalTargetWriter(IFileSystem fileSystem)
{
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
        NpmrcDocument document = ReadDocument(targetPath);
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
                        true,
                        document.OriginalContentsBytes,
                        ComputeSha256(document.OriginalContentsBytes),
                        RequiresRollback:
                            previousUnixFileMode is not null
                            && previousUnixFileMode.Value != OwnerOnlyUnixFileMode,
                        PreviousUnixFileMode: previousUnixFileMode
                    )
                );
                EnsureNpmrcIsOwnerOnlyUnixFileModeIfNeeded(targetPath, previousUnixFileMode);
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
        NpmrcDocument document = ReadDocument(targetPath);
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
                    .Where(proof => proof.TargetKind == ConfigurationTargetKind.Npmrc)
                    .GroupBy(
                        proof => CreatePhysicalPathIdentity(proof.TargetPathOrName),
                        PathIdentityComparer
                    )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureTargetPathCanBeSafelyMutated(proofsByTarget.Key);

            NpmrcDocument document = ReadDocument(proofsByTarget.Key);
            foreach (
                IGrouping<string, ConfigurationPhysicalTargetOwnershipProof> proofsByKey in
                    proofsByTarget.GroupBy(proof => proof.Key, StringComparer.Ordinal)
            )
            {
                if (proofsByKey.Count() > 1)
                {
                    throw new InvalidOperationException(
                        "Configuration conflict: Npmrc retained ownership proofs must be unique "
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

        if (change.TargetKind != ConfigurationTargetKind.Npmrc)
        {
            return null;
        }

        string? operationViolation = GetOperationValidationViolation(change.Operation);
        if (operationViolation is not null)
        {
            return operationViolation;
        }

        if (string.IsNullOrWhiteSpace(change.Key))
        {
            return "The Npmrc physical writer requires a non-empty key.";
        }

        if (ContainsLineBreak(change.Key))
        {
            return "The Npmrc physical writer supports keys without CR or LF.";
        }

        string? keyViolation = GetKeyValidationViolation(change.Key);
        if (keyViolation is not null)
        {
            return keyViolation;
        }

        if (
            change.Operation is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh
        )
        {
            if (change.Value is null)
            {
                return "The Npmrc physical writer requires a value for value-writing changes.";
            }

            if (ContainsLineBreak(change.Value))
            {
                return "The Npmrc physical writer supports values without CR or LF.";
            }

            string? valueViolation = GetValueValidationViolation(change.Value);
            if (valueViolation is not null)
            {
                return valueViolation;
            }

            if (IsNpmAuthTokenKey(change.Key) && !change.IsSecretValue)
            {
                return "The Npmrc physical writer requires auth token values to be marked "
                    + "as secret.";
            }

            if (change.IsSecretValue && !IsNpmAuthTokenKey(change.Key))
            {
                return "The Npmrc physical writer requires secret values to use auth token keys.";
            }

            if (change.IsSecretValue && IsNpmAuthTokenKey(change.Key))
            {
                string? selectorViolation = GetNpmrcSecretAuthTokenSelectorViolation(
                    resourceIdentity,
                    change.Key
                );
                if (selectorViolation is not null)
                {
                    return selectorViolation;
                }
            }
        }
        else if (change.Operation == ConfigurationChangeOperation.Remove)
        {
            if (change.Value is not null)
            {
                return "The Npmrc physical writer supports remove changes without a value.";
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

        if (request.TargetKind != ConfigurationTargetKind.Npmrc)
        {
            throw new NotSupportedException(
                "The Npmrc physical writer supports only Npmrc targets."
            );
        }

        if (request.PlanOperation is not ConfigurationPlanOperation.DryRun
            and not ConfigurationPlanOperation.Apply
            and not ConfigurationPlanOperation.Remove)
        {
            throw new NotSupportedException(
                "The Npmrc physical writer supports dry-run/apply/remove operations only."
            );
        }

        if (request.Changes.Count == 0)
        {
            throw new NotSupportedException(
                "The Npmrc physical writer requires at least one change."
            );
        }

        if (request.Changes.Any(change => change.TargetKind != ConfigurationTargetKind.Npmrc))
        {
            throw new NotSupportedException(
                "The Npmrc physical writer requires all request changes to target Npmrc."
            );
        }

        foreach (ConfigurationChange change in request.Changes)
        {
            string? operationViolation = GetOperationValidationViolation(change.Operation);
            if (operationViolation is not null)
            {
                throw new NotSupportedException(operationViolation);
            }
        }

        if (!AreChangesTargetingTheSamePath(request.Changes))
        {
            throw new NotSupportedException(
                "The Npmrc physical writer supports only one normalized physical file path per "
                    + "request."
            );
        }

        if (
            request.Changes.GroupBy(change => change.Key, StringComparer.Ordinal)
                .Any(group => group.Count() > 1)
        )
        {
            throw new NotSupportedException(
                "The Npmrc physical writer supports only one change per canonical key."
            );
        }

        if (
            request.PlanOperation == ConfigurationPlanOperation.Apply
            && request.Changes.Any(change => !IsValueWritingOperation(change.Operation))
        )
        {
            throw new NotSupportedException(
                "The Npmrc physical writer supports value-writing changes only for apply."
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
                "The Npmrc physical writer supports ownership-removing changes only for remove."
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
        NpmrcDocument document,
        ConfigurationPhysicalTargetWriterRequest request,
        string targetPath
    )
    {
        foreach (ConfigurationChange change in request.Changes)
        {
            ConfigurationPhysicalTargetOwnershipProof? proof = FindOwnershipProof(
                request.OwnershipProofs,
                targetPath,
                change.Key
            );
            NpmrcEntry[] existingEntries = document.FindEntries(change.Key).ToArray();
            ValidateCurrentState(change, existingEntries, proof);

            if (change.Operation == ConfigurationChangeOperation.Remove)
            {
                document.Lines.RemoveAt(existingEntries[0].LineIndex);
                continue;
            }

            if (
                existingEntries.Length == 1
                && string.Equals(existingEntries[0].Value, change.Value, StringComparison.Ordinal)
            )
            {
                continue;
            }

            string renderedLine = RenderEntryLine(change.Key, change.Value!);
            if (existingEntries.Length == 1)
            {
                document.Lines[existingEntries[0].LineIndex] = renderedLine;
                continue;
            }

            document.Lines.Add(renderedLine);
            document.TrailingNewLine = true;
        }
    }

    private static void ValidateCurrentState(
        ConfigurationChange change,
        IReadOnlyList<NpmrcEntry> existingEntries,
        ConfigurationPhysicalTargetOwnershipProof? proof
    )
    {
        if (existingEntries.Count > 1)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Npmrc key has multiple existing declarations and cannot "
                    + "be updated safely."
            );
        }

        if (existingEntries.Count == 0)
        {
            if (change.Operation == ConfigurationChangeOperation.Remove)
            {
                throw new InvalidOperationException(
                    "Configuration conflict: Npmrc key is missing from the physical "
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
                    "Configuration conflict: owned Npmrc key is missing from the physical "
                        + "configuration file."
                );
            }

            return;
        }

        if (change.Operation == ConfigurationChangeOperation.Create)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Npmrc create target already exists."
            );
        }

        if (proof is null)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Npmrc key already exists and is not proven to be owned by "
                    + "the existing manifest."
            );
        }

        ValidateProofAgainstEntry(existingEntries[0], proof);
    }

    private static void ValidateProofAgainstCurrentState(
        NpmrcDocument document,
        ConfigurationPhysicalTargetOwnershipProof proof
    )
    {
        if (string.IsNullOrWhiteSpace(proof.Key))
        {
            throw new InvalidOperationException(
                "Configuration conflict: Npmrc retained ownership proofs require a canonical key."
            );
        }

        NpmrcEntry[] existingEntries = document.FindEntries(proof.Key).ToArray();
        if (existingEntries.Length == 0)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Npmrc retained ownership proof does not match any "
                    + "existing file."
            );
        }

        if (existingEntries.Length > 1)
        {
            throw new InvalidOperationException(
                "Configuration conflict: Npmrc retained ownership proofs must be unique per "
                    + "canonical physical key."
            );
        }

        ValidateProofAgainstEntry(existingEntries[0], proof);
    }

    private static void ValidateProofAgainstEntry(
        NpmrcEntry existingEntry,
        ConfigurationPhysicalTargetOwnershipProof proof
    )
    {
        bool isSecretAuthToken = IsNpmAuthTokenKey(proof.Key);
        if (isSecretAuthToken)
        {
            if (proof.PlannedValueSha256 is not null)
            {
                throw new InvalidOperationException(
                    "Configuration conflict: Npmrc secret retained ownership proofs must not "
                        + "include planned value hashes."
                );
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(proof.PlannedValueSha256))
        {
            throw new InvalidOperationException(
                "Configuration conflict: Npmrc retained ownership proof is missing a planned "
                    + "value hash."
            );
        }

        string currentHash = ComputeSha256(existingEntry.Value);
        if (!string.Equals(currentHash, proof.PlannedValueSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Configuration conflict: Npmrc retained ownership proof does not match the current "
                    + "file contents."
            );
        }
    }

    private static string? GetNpmrcSecretAuthTokenSelectorViolation(
        CanonicalResourceIdentity? resourceIdentity,
        string selector
    )
    {
        if (resourceIdentity is null)
        {
            return "The Npmrc physical writer requires secret auth token keys to be derived "
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
                CredentialEcosystem.Npm
            )
        )
        {
            return "The Npmrc physical writer requires secret auth token keys to be derived "
                + "from a canonical registry identity.";
        }

        NpmCompatibleAuthSelectors selectors = NpmCompatibleAuthSelectorPolicy.Create(
            resourceIdentity
        );
        return string.Equals(selector, selectors.NpmAuthTokenKey, StringComparison.Ordinal)
            ? null
            : "The Npmrc physical writer requires secret auth token keys to match the canonical "
                + "registry identity.";
    }

    private NpmrcDocument ReadDocument(string targetPath)
    {
        EnsureTargetPathCanBeSafelyMutated(targetPath);

        if (!fileSystem.FileExists(targetPath))
        {
            if (fileSystem.DirectoryExists(targetPath))
            {
                throw new InvalidOperationException(
                    "Configuration conflict: Npmrc target exists as a directory."
                );
            }

            return new NpmrcDocument(
                targetPath,
                string.Empty,
                null,
                FileMutationExpectation.Missing,
                Environment.NewLine,
                false,
                new List<string>()
            );
        }

        if (fileSystem.DirectoryExists(targetPath))
        {
            throw new InvalidOperationException(
                "Configuration conflict: Npmrc target exists as a directory."
            );
        }

        if (IsUnsupportedLinkOrReparsePoint(targetPath))
        {
            throw new NotSupportedException(
                "Configuration conflict: Npmrc target path is a symbolic-link or reparse-point "
                    + "and is not supported."
            );
        }

        byte[] contents = fileSystem.ReadAllBytes(targetPath);
        if (StartsWithUtf8Bom(contents))
        {
            throw new NotSupportedException(
                "Configuration conflict: Npmrc files with a UTF-8 BOM are not supported for safe "
                    + "physical mutation."
            );
        }

        string text = Utf8NoBom.GetString(contents);
        string newLine = DetectNewLine(text);
        bool trailingNewLine = text.EndsWith('\n');
        return new NpmrcDocument(
            targetPath,
            text,
            contents,
            FileMutationExpectation.Existing(ComputeSha256(contents)),
            newLine,
            trailingNewLine,
            SplitLines(text)
        );
    }

    private void EnsureTargetPathCanBeSafelyMutated(string targetPath)
    {
        if (IsUnsupportedLinkOrReparsePoint(targetPath))
        {
            throw new NotSupportedException(
                "Configuration conflict: Npmrc target path is a symbolic-link or reparse-point and "
                    + "is not supported."
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
                        "Configuration conflict: Npmrc target parent path contains a symbolic-link "
                            + "or reparse-point directory."
                    );
                }

                if (!fileSystem.DirectoryExists(directory) && fileSystem.FileExists(directory))
                {
                    throw new NotSupportedException(
                        "Configuration conflict: Npmrc target parent path contains a non-directory "
                            + "entry."
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
                "The Npmrc physical writer supports only a single normalized physical file path "
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
            proof.TargetKind == ConfigurationTargetKind.Npmrc
            && PathIdentityComparer.Equals(
                CreatePhysicalPathIdentity(proof.TargetPathOrName),
                targetPath
            )
            && string.Equals(proof.Key, key, StringComparison.Ordinal)
        );

    private static string RenderEntryLine(string key, string value) =>
        $"{EscapeNpmIniValue(key)}={EscapeNpmIniValue(value)}";

    private static string EscapeNpmIniValue(string value)
    {
        var escapedValue = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (character is '\\' or ';' or '#')
            {
                escapedValue.Append('\\');
            }

            escapedValue.Append(character);
        }

        return escapedValue.ToString();
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
                "Configuration conflict: mixed or bare-CR Npmrc newline styles are not supported "
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

    private static bool TryParseEntryLine(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        string trimmedStart = line.TrimStart();
        if (trimmedStart.Length == 0 || trimmedStart[0] is '#' or ';')
        {
            return false;
        }

        int separatorIndex = line.IndexOf('=');
        if (separatorIndex < 0)
        {
            return false;
        }

        key = ParseNpmIniValue(line[..separatorIndex]);
        if (key.Length == 0)
        {
            return false;
        }

        value = ParseNpmIniValue(line[(separatorIndex + 1)..]);
        return true;
    }

    private static string ParseNpmIniValue(string value)
    {
        string trimmedValue = value.Trim();
        if (trimmedValue.Length == 0)
        {
            return string.Empty;
        }

        if (TryParseQuotedNpmIniValue(trimmedValue, out string quotedValue))
        {
            return ParseQuotedNpmIniValue(quotedValue);
        }

        var unescapedValue = new StringBuilder(trimmedValue.Length);
        bool escaped = false;
        foreach (char character in trimmedValue)
        {
            if (escaped)
            {
                if (character is '\\' or ';' or '#')
                {
                    unescapedValue.Append(character);
                }
                else
                {
                    unescapedValue.Append('\\');
                    unescapedValue.Append(character);
                }

                escaped = false;
                continue;
            }

            if (character is ';' or '#')
            {
                break;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            unescapedValue.Append(character);
        }

        if (escaped)
        {
            unescapedValue.Append('\\');
        }

        return unescapedValue.ToString().Trim();
    }

    private static bool TryParseQuotedNpmIniValue(string value, out string quotedValue)
    {
        quotedValue = string.Empty;
        if (value.Length < 2 || (value[0] != '\'' && value[0] != '"'))
        {
            return false;
        }

        int closingQuoteIndex =
            value[0] == '\'' ? value.LastIndexOf('\'') : FindClosingDoubleQuoteIndex(value);
        if (closingQuoteIndex <= 0)
        {
            return false;
        }

        int index = closingQuoteIndex + 1;
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }

        if (index == value.Length || value[index] is '#' or ';')
        {
            quotedValue = value[..(closingQuoteIndex + 1)];
            return true;
        }

        return false;
    }

    private static int FindClosingDoubleQuoteIndex(string value)
    {
        bool escaped = false;
        int closingQuoteIndex = -1;
        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                closingQuoteIndex = index;
            }
        }

        return closingQuoteIndex;
    }

    private static string ParseQuotedNpmIniValue(string value)
    {
        if (value[0] == '\'')
        {
            return value[1..^1];
        }

        var unescapedValue = new StringBuilder(value.Length - 2);
        bool escaped = false;
        for (int index = 1; index < value.Length - 1; index++)
        {
            char character = value[index];
            if (escaped)
            {
                if (character is '\\' or '"')
                {
                    unescapedValue.Append(character);
                }
                else
                {
                    unescapedValue.Append('\\');
                    unescapedValue.Append(character);
                }

                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            unescapedValue.Append(character);
        }

        if (escaped)
        {
            unescapedValue.Append('\\');
        }

        return unescapedValue.ToString();
    }

    private static bool IsQuoted(string value) =>
        value.Length > 1
        && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"'));

    private static bool StartsWithUtf8Bom(byte[] value) => value is [0xEF, 0xBB, 0xBF, ..];

    private static string ComputeSha256(string value) =>
        ComputeSha256(Utf8NoBom.GetBytes(value));

    private static string ComputeSha256(byte[] value)
    {
        byte[] hash = SHA256.HashData(value);
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }

    private static bool ContainsLineBreak(string value) =>
        value.Contains('\r', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal);

    private static string? GetKeyValidationViolation(string key)
    {
        if (HasLeadingOrTrailingWhiteSpace(key))
        {
            return "The Npmrc physical writer requires keys without surrounding whitespace.";
        }

        if (key.Any(character => character < ' ' || character == '\u007f'))
        {
            return "The Npmrc physical writer supports keys without control characters.";
        }

        if (key.Contains('='))
        {
            return "The Npmrc physical writer supports keys without '='.";
        }

        if (IsQuoted(key))
        {
            return "The Npmrc physical writer requires keys that are not quoted.";
        }

        if (key.Contains('#') || key.Contains(';'))
        {
            return "The Npmrc physical writer supports keys without comment markers.";
        }

        return null;
    }

    private static string? GetValueValidationViolation(string value)
    {
        if (HasLeadingOrTrailingWhiteSpace(value))
        {
            return "The Npmrc physical writer requires values without surrounding whitespace.";
        }

        if (value.Any(character => character < ' ' || character == '\u007f'))
        {
            return "The Npmrc physical writer supports values without control characters.";
        }

        if (IsQuoted(value))
        {
            return "The Npmrc physical writer requires values that are not quoted.";
        }

        if (value.Contains(';') || value.Contains('#'))
        {
            return "The Npmrc physical writer supports values without comment markers.";
        }

        return null;
    }

    private static string? GetOperationValidationViolation(
        ConfigurationChangeOperation operation
    ) =>
        operation switch
        {
            ConfigurationChangeOperation.EnsureFile =>
                "The Npmrc physical writer does not support ensure-file changes.",
            ConfigurationChangeOperation.InstallAdapter =>
                "The Npmrc physical writer does not support install-adapter changes.",
            ConfigurationChangeOperation.RemoveAdapter =>
                "The Npmrc physical writer does not support remove-adapter changes.",
            _ => null,
        };

    private UnixFileMode? GetCurrentUnixFileMode(string path) =>
        OperatingSystem.IsWindows() || !fileSystem.FileExists(path)
            ? null
            : fileSystem.GetUnixFileMode(path);

    private void EnsureNpmrcIsOwnerOnlyUnixFileModeIfNeeded(
        string path,
        UnixFileMode? currentUnixFileMode
    )
    {
        if (OperatingSystem.IsWindows() || currentUnixFileMode is null)
        {
            return;
        }

        if (currentUnixFileMode.Value != OwnerOnlyUnixFileMode)
        {
            fileSystem.SetUnixFileMode(path, OwnerOnlyUnixFileMode);
        }
    }

    private static bool IsValueWritingOperation(ConfigurationChangeOperation operation) =>
        operation
            is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh;

    private static bool IsNpmAuthTokenKey(string? key) =>
        key is not null
        && (
            string.Equals(key, "_authToken", StringComparison.Ordinal)
            || key.EndsWith(":_authToken", StringComparison.Ordinal)
        );

    private static bool HasLeadingOrTrailingWhiteSpace(string value) =>
        !string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private sealed record NpmrcEntry(int LineIndex, string Key, string Value);

    private sealed class NpmrcDocument(
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

        public IEnumerable<NpmrcEntry> FindEntries(string key)
        {
            for (int index = 0; index < Lines.Count; index++)
            {
                if (TryParseEntryLine(Lines[index], out string parsedKey, out string parsedValue)
                    && string.Equals(parsedKey, key, StringComparison.Ordinal))
                {
                    yield return new NpmrcEntry(index, parsedKey, parsedValue);
                }
            }
        }
    }
}
